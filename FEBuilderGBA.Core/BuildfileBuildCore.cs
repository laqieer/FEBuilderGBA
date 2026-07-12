// SPDX-License-Identifier: GPL-3.0-or-later
// Strict, independent schema-v1 buildfile CONSUMER (#1936).
//
// This is the counterpart to BuildfileExportCore (#1935). It reconstructs a target ROM
// SOLELY from `buildfile.json` + `data/` payloads. It NEVER executes or parses
// `main.event`, invokes ColorzCore, consumes `source/`, or trusts the advisory patch
// inventory / projection metadata / warnings — those surfaces cannot influence a single
// output byte. It never mutates the input ROM or any project file: `buildfile.json` is
// opened as an exact no-follow regular file and `data/` is captured through the existing
// handle-relative, no-follow snapshot reader into immutable managed memory.
//
// The reader FAILS CLOSED on any structural, identity, size, range, path, hash, bounds,
// UTF-8, JSON, schema, or filesystem-safety violation. It draws a deliberate line between
//   - STRUCTURAL failure  (bad clean ROM / malformed or self-inconsistent recipe /
//     missing / tampered payload)  => Success == false, and
//   - declared-TARGET-identity drift (the recipe reconstructs deterministically but the
//     recomputed target CRC32/SHA-256 differ from the manifest's declared target)
//     => Success == true, TargetIdentityMatches == false, bytes still returned,
// because the two callers classify them differently: `--build-buildfile` refuses to
// publish on either, while `--buildfile-roundtrip` treats declared-target drift as
// reproducibility drift (exit 2), not a usage/validation error (exit 1).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FEBuilderGBA
{
    /// <summary>Options for the strict schema-v1 buildfile consumer.</summary>
    public sealed class BuildfileBuildOptions
    {
        /// <summary>Maximum accepted <c>buildfile.json</c> size (16 MiB).</summary>
        public const int MaxManifestBytes = 16 * 1024 * 1024;

        /// <summary>Maximum accepted target size (32 MiB), mirroring the exporter.</summary>
        public const int MaxRomSize = 32 * 1024 * 1024;

        /// <summary>Maximum accepted payload-range count, mirroring the exporter.</summary>
        public const int MaxPayloadRanges = 16384;

        /// <summary>Maximum accepted JSON nesting depth for the manifest.</summary>
        public const int MaxJsonDepth = 64;

        /// <summary>
        /// Test-only manifest byte-cap override so the oversized-manifest rejection can be
        /// proven cheaply without materializing a 16 MiB file. Internal by design: it is
        /// NOT a hidden command-line flag or environment bypass and has no production surface.
        /// </summary>
        internal int? MaxManifestBytesForTest { get; set; }

        internal long EffectiveMaxManifestBytes => MaxManifestBytesForTest ?? MaxManifestBytes;
    }

    /// <summary>Outcome of a strict schema-v1 buildfile reconstruction.</summary>
    public sealed class BuildfileBuildResult
    {
        /// <summary>
        /// True when reconstruction completed structurally (clean matched, manifest valid,
        /// payloads present + verified, bytes rebuilt). A declared-target-identity mismatch
        /// does NOT clear this flag — inspect <see cref="TargetIdentityMatches"/>.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>Structural failure detail (empty on success).</summary>
        public string Error { get; set; } = "";

        /// <summary>Reconstructed target bytes; present whenever <see cref="Success"/>.</summary>
        public byte[] TargetBytes { get; set; }

        /// <summary>The validated manifest; present whenever <see cref="Success"/>.</summary>
        public BuildfileManifest Manifest { get; set; }

        /// <summary>True when the recomputed target CRC32/SHA-256 match the declared target.</summary>
        public bool TargetIdentityMatches { get; set; }

        /// <summary>Diagnostic for a declared-target-identity mismatch (empty when matching).</summary>
        public string TargetIdentityDetail { get; set; } = "";

        /// <summary>Recomputed canonical target CRC32 spelling.</summary>
        public string TargetCrc32 { get; set; } = "";

        /// <summary>Recomputed canonical target SHA-256 spelling.</summary>
        public string TargetSha256 { get; set; } = "";

        public static BuildfileBuildResult Fail(string error)
            => new BuildfileBuildResult { Success = false, Error = error ?? "" };
    }

    internal delegate bool BuildfileDeletePath(string path, out string error);

    /// <summary>
    /// Strict schema-v1 buildfile consumer plus the atomic no-replace publication and
    /// scratch primitives its CLI front-ends need. The <see cref="Build"/> method never
    /// mutates the input ROM or the project; publication writes ONLY a brand-new destination.
    /// </summary>
    public static class BuildfileBuildCore
    {
        /// <summary>Internal signal used to unwind fail-closed on any structural violation.</summary>
        sealed class BuildfileValidationException : Exception
        {
            public BuildfileValidationException(string message) : base(message) { }
        }

        static readonly HashSet<string> RootPropertyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "schemaVersion", "tool", "game", "version", "entryEvent", "dataDirectory",
                "clean", "target", "extension", "totalRanges", "totalChangedBytes", "ranges",
                "patches", "projection", "warnings",
            };
        static readonly HashSet<string> RomIdentityPropertyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "size", "crc32", "sha256", "isCanonicalOriginal",
            };
        static readonly HashSet<string> ExtensionPropertyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "start", "length", "fillByte",
            };
        static readonly HashSet<string> RangePropertyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "index", "offset", "gbaAddress", "length", "changedBytes",
                "category", "confidence", "suggestion", "payload", "payloadSha256",
            };
        static readonly HashSet<string> PatchInventoryPropertyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "status", "reason", "baseRelative", "installed",
            };
        static readonly HashSet<string> PatchRecordPropertyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "name", "path", "status", "confidence", "reason", "params",
            };
        static readonly HashSet<string> PatchParamPropertyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "key", "value",
            };
        static readonly HashSet<string> ProjectionPropertyNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "status", "reason", "directory",
            };

        // ----------------------------------------------------------------- reconstruct

        /// <summary>
        /// Reconstruct the target ROM from <c>buildfile.json</c> + <c>data/</c> under
        /// <paramref name="projectDirectory"/>, validated strictly against
        /// <paramref name="cleanRom"/>. Reads only; never mutates the ROM or the project.
        /// </summary>
        public static BuildfileBuildResult Build(
            ROM cleanRom,
            string projectDirectory,
            BuildfileBuildOptions options)
        {
            if (cleanRom == null) return BuildfileBuildResult.Fail("Clean ROM is required.");
            if (string.IsNullOrEmpty(projectDirectory))
                return BuildfileBuildResult.Fail("Project directory is required.");
            options ??= new BuildfileBuildOptions();

            byte[] cleanBytes = cleanRom.Data;
            if (cleanBytes == null || cleanBytes.Length == 0)
                return BuildfileBuildResult.Fail("Clean ROM has no data.");
            string cleanVersion = cleanRom.RomInfo?.VersionToFilename;
            if (string.IsNullOrEmpty(cleanVersion))
                return BuildfileBuildResult.Fail(
                    "Clean ROM is not a recognized Fire Emblem GBA version.");
            uint canonicalCrc = cleanRom.RomInfo != null ? cleanRom.RomInfo.orignal_crc32 : 0;

            string projectDir;
            try
            {
                // Resolve the project root physically once before opening the manifest/data
                // descendants. This preserves benign symlinked ancestors while ensuring every
                // later pathname starts from the same canonical directory.
                projectDir = BuildfilePathSafety.ResolvePhysicalPath(projectDirectory);
            }
            catch (Exception ex)
            {
                return BuildfileBuildResult.Fail("Invalid project directory: " + ex.Message);
            }

            try
            {
                if (!Directory.Exists(projectDir))
                    return BuildfileBuildResult.Fail("Project directory not found: " + projectDir);
                if (!ProjectionFileSystemSafety.TryValidateDirectory(projectDir, out string dirError))
                    return BuildfileBuildResult.Fail("Project path is not a directory: " + dirError);

                BuildfileManifest manifest = ReadAndValidateManifest(
                    projectDir, cleanBytes, cleanVersion, canonicalCrc, options);
                Dictionary<string, byte[]> payloads =
                    CaptureAndValidatePayloads(projectDir, manifest, cleanBytes);
                return Reconstruct(manifest, cleanBytes, payloads);
            }
            catch (BuildfileValidationException ex)
            {
                return BuildfileBuildResult.Fail(ex.Message);
            }
            catch (Exception ex) when (IsExpectedIo(ex))
            {
                return BuildfileBuildResult.Fail("Buildfile build failed: " + ex.Message);
            }
        }

        static BuildfileBuildResult Reconstruct(
            BuildfileManifest m,
            byte[] cleanBytes,
            Dictionary<string, byte[]> payloads)
        {
            int targetSize = (int)m.Target.Size;
            var target = new byte[targetSize];

            // 1) Exact clean bytes (clean.Size == cleanBytes.Length, validated).
            Array.Copy(cleanBytes, 0, target, 0, cleanBytes.Length);

            // 2) Declared extension fill (bytes past the clean region), if any.
            if (m.Extension != null)
            {
                byte fill = (byte)Convert.ToInt32(m.Extension.FillByte.Substring(2), 16);
                int start = (int)m.Extension.Start;
                int end = start + (int)m.Extension.Length;
                for (int i = start; i < end; i++) target[i] = fill;
            }

            // 3) Validated payloads in manifest order (a payload may override extension fill
            //    or span the clean/extension boundary — application order guarantees it wins).
            foreach (BuildfileRange r in m.Ranges)
            {
                byte[] bytes = payloads[r.Payload];
                Array.Copy(bytes, 0, target, (int)r.Offset, (int)r.Length);
            }

            string crc = BuildfileFormat.Crc32Hex(target);
            string sha = BuildfileFormat.Sha256Hex(target);
            bool matches = string.Equals(crc, m.Target.Crc32, StringComparison.Ordinal)
                && string.Equals(sha, m.Target.Sha256, StringComparison.Ordinal);

            var result = new BuildfileBuildResult
            {
                Success = true,
                TargetBytes = target,
                Manifest = m,
                TargetCrc32 = crc,
                TargetSha256 = sha,
                TargetIdentityMatches = matches,
            };
            if (!matches)
            {
                result.TargetIdentityDetail =
                    "declared crc32=" + m.Target.Crc32 + "/sha256=" + m.Target.Sha256
                    + ", actual crc32=" + crc + "/sha256=" + sha;
            }
            return result;
        }

        // -------------------------------------------------------------- manifest reading

        static BuildfileManifest ReadAndValidateManifest(
            string projectDir,
            byte[] cleanBytes,
            string cleanVersion,
            uint canonicalCrc,
            BuildfileBuildOptions options)
        {
            string manifestPath = Path.Combine(projectDir, "buildfile.json");
            byte[] raw = ReadManifestBytes(manifestPath, options);
            byte[] json = StripBomAndValidateUtf8(raw);
            ThrowIfDuplicateProperties(json);

            using JsonDocument doc = ParseJson(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new BuildfileValidationException("buildfile.json root must be a JSON object.");
            RejectUnknownProperties(root, "root object", RootPropertyNames);

            int schema = RequireInt(root, "schemaVersion");
            if (schema != 1)
                throw new BuildfileValidationException(
                    "Unsupported schemaVersion " + schema + "; only schema version 1 is supported.");

            var m = new BuildfileManifest { SchemaVersion = 1 };

            // Version identity: must be a recognized version equal to the clean ROM's.
            m.Version = RequireString(root, "version");
            if (m.Version.Length == 0)
                throw new BuildfileValidationException("buildfile.json 'version' must be non-empty.");
            if (!string.Equals(m.Version, cleanVersion, StringComparison.Ordinal))
                throw new BuildfileValidationException(
                    "Recipe version '" + m.Version + "' does not match the clean ROM version '"
                    + cleanVersion + "'.");

            m.DataDirectory = RequireString(root, "dataDirectory");
            if (!string.Equals(m.DataDirectory, "data", StringComparison.Ordinal))
                throw new BuildfileValidationException(
                    "dataDirectory must be \"data\" (got \"" + m.DataDirectory + "\").");
            PopulateAndValidateAdvisoryStructure(root, m);

            // Exact clean identity (size + canonical crc32 + sha256 + canonical flag).
            JsonElement cleanEl = RequireObject(root, "clean");
            m.Clean = ReadRomIdentity(cleanEl, requireCanonicalFlag: true);
            uint cleanCrc = BuildfileFormat.Crc32(cleanBytes);
            bool cleanIsCanonical = canonicalCrc != 0 && cleanCrc == canonicalCrc;
            if (m.Clean.Size != (uint)cleanBytes.Length)
                throw new BuildfileValidationException(
                    "Clean ROM size " + cleanBytes.Length
                    + " does not match the recipe's declared clean size " + m.Clean.Size + ".");
            if (!string.Equals(m.Clean.Crc32, BuildfileFormat.Hex32(cleanCrc), StringComparison.Ordinal))
                throw new BuildfileValidationException(
                    "Clean ROM crc32 does not match the recipe's declared clean crc32.");
            if (!string.Equals(m.Clean.Sha256, BuildfileFormat.Sha256Hex(cleanBytes), StringComparison.Ordinal))
                throw new BuildfileValidationException(
                    "Clean ROM sha256 does not match the recipe's declared clean sha256.");
            if (m.Clean.IsCanonicalOriginal != cleanIsCanonical)
                throw new BuildfileValidationException(
                    "Clean ROM canonical-original flag does not match the recipe.");

            // Target identity: canonical spellings now; VALUE compared post-reconstruction.
            JsonElement targetEl = RequireObject(root, "target");
            m.Target = ReadRomIdentity(targetEl, requireCanonicalFlag: false);
            if (!BuildfileFormat.IsCanonicalHex32(m.Target.Crc32))
                throw new BuildfileValidationException("target.crc32 is not a canonical crc32 spelling.");
            if (!BuildfileFormat.IsCanonicalSha256(m.Target.Sha256))
                throw new BuildfileValidationException("target.sha256 is not a canonical sha256 spelling.");
            uint cleanSize = m.Clean.Size;
            uint targetSize = m.Target.Size;
            if (targetSize < cleanSize)
                throw new BuildfileValidationException(
                    "target size " + targetSize + " is smaller than clean size " + cleanSize + ".");
            if (targetSize > (uint)BuildfileBuildOptions.MaxRomSize)
                throw new BuildfileValidationException(
                    "target size " + targetSize + " exceeds the "
                    + BuildfileBuildOptions.MaxRomSize + "-byte (32 MiB) limit.");

            // Extension geometry: present exactly when the target extends the clean ROM.
            bool hasExtension = targetSize != cleanSize;
            bool extensionPropertyPresent =
                root.TryGetProperty("extension", out JsonElement extEl);
            if (extensionPropertyPresent && extEl.ValueKind == JsonValueKind.Null)
            {
                throw new BuildfileValidationException(hasExtension
                    ? "extension must be an object (not null) when the target extends the clean ROM."
                    : "extension must be omitted (not null) when the target size equals the clean size.");
            }
            if (hasExtension)
            {
                if (!extensionPropertyPresent)
                    throw new BuildfileValidationException(
                        "extension is required when the target extends the clean ROM.");
                if (extEl.ValueKind != JsonValueKind.Object)
                    throw new BuildfileValidationException("extension must be an object.");
                RejectUnknownProperties(extEl, "extension object", ExtensionPropertyNames);
                uint start = RequireUInt(extEl, "start");
                uint length = RequireUInt(extEl, "length");
                string fillByte = RequireString(extEl, "fillByte");
                if (start != cleanSize)
                    throw new BuildfileValidationException(
                        "extension.start must equal the clean size " + cleanSize + ".");
                if (length != targetSize - cleanSize)
                    throw new BuildfileValidationException(
                        "extension.length must equal target - clean = " + (targetSize - cleanSize) + ".");
                if (!BuildfileFormat.IsCanonicalHex8(fillByte))
                    throw new BuildfileValidationException(
                        "extension.fillByte must be a canonical 0xNN byte.");
                m.Extension = new BuildfileExtension { Start = start, Length = length, FillByte = fillByte };
            }
            else if (extensionPropertyPresent)
            {
                throw new BuildfileValidationException(
                    "extension must be omitted when the target size equals the clean size.");
            }

            // Ranges + totals.
            int totalRanges = RequireInt(root, "totalRanges");
            uint totalChangedBytes = RequireUInt(root, "totalChangedBytes");
            JsonElement rangesEl = RequireArray(root, "ranges");
            int rangeCount = rangesEl.GetArrayLength();
            if (rangeCount > BuildfileBuildOptions.MaxPayloadRanges)
                throw new BuildfileValidationException(
                    "ranges count " + rangeCount + " exceeds the "
                    + BuildfileBuildOptions.MaxPayloadRanges + " limit.");
            if (totalRanges != rangeCount)
                throw new BuildfileValidationException(
                    "totalRanges " + totalRanges + " does not match the ranges array length "
                    + rangeCount + ".");

            long previousEnd = 0;
            long changedSum = 0;
            int index = 0;
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement rEl in rangesEl.EnumerateArray())
            {
                if (rEl.ValueKind != JsonValueKind.Object)
                    throw new BuildfileValidationException("Each range must be a JSON object.");
                RejectUnknownProperties(rEl, "range object", RangePropertyNames);
                int rangeIndex = RequireInt(rEl, "index");
                uint offset = RequireUInt(rEl, "offset");
                uint length = RequireUInt(rEl, "length");
                uint changedBytes = RequireUInt(rEl, "changedBytes");
                string gbaAddress = RequireString(rEl, "gbaAddress");
                string payload = RequireString(rEl, "payload");
                string payloadSha256 = RequireString(rEl, "payloadSha256");

                if (rangeIndex != index)
                    throw new BuildfileValidationException(
                        "range index " + rangeIndex + " is not the contiguous zero-based index "
                        + index + ".");
                if (length == 0)
                    throw new BuildfileValidationException(
                        "range " + index + " must have a positive length.");
                long end = (long)offset + length;
                if (end > targetSize)
                    throw new BuildfileValidationException(
                        "range " + index + " [offset " + offset + ", length " + length
                        + "] exceeds the target size " + targetSize + ".");
                if (index > 0 && offset <= previousEnd)
                    throw new BuildfileValidationException(
                        "range " + index
                        + " overlaps, touches, or is not strictly ordered after the previous range.");
                if (changedBytes != length)
                    throw new BuildfileValidationException(
                        "range " + index + " changedBytes " + changedBytes
                        + " must equal length " + length + ".");
                if (!string.Equals(gbaAddress, BuildfileFormat.GbaAddress(offset), StringComparison.Ordinal))
                    throw new BuildfileValidationException(
                        "range " + index + " gbaAddress is not the canonical mapped address.");
                string expectedPayload = BuildfileFormat.PayloadPath(index, offset, length);
                if (!string.Equals(payload, expectedPayload, StringComparison.Ordinal))
                    throw new BuildfileValidationException(
                        "range " + index + " payload path must be exactly '" + expectedPayload + "'.");
                if (!seenPaths.Add(payload))
                    throw new BuildfileValidationException(
                        "duplicate payload path '" + payload + "'.");
                if (!BuildfileFormat.IsCanonicalSha256(payloadSha256))
                    throw new BuildfileValidationException(
                        "range " + index + " payloadSha256 is not a canonical sha256 spelling.");

                m.Ranges.Add(new BuildfileRange
                {
                    Index = index,
                    Offset = offset,
                    GbaAddress = gbaAddress,
                    Length = length,
                    ChangedBytes = changedBytes,
                    Category = OptionalString(rEl, "category"),
                    Confidence = OptionalString(rEl, "confidence"),
                    Suggestion = OptionalString(rEl, "suggestion"),
                    Payload = payload,
                    PayloadSha256 = payloadSha256,
                });

                previousEnd = end;
                changedSum += length;
                index++;
            }
            if (changedSum != totalChangedBytes)
                throw new BuildfileValidationException(
                    "totalChangedBytes " + totalChangedBytes
                    + " does not equal the sum of range lengths " + changedSum + ".");

            m.TotalRanges = totalRanges;
            m.TotalChangedBytes = totalChangedBytes;
            return m;
        }

        static BuildfileRomIdentity ReadRomIdentity(JsonElement el, bool requireCanonicalFlag)
        {
            string context = requireCanonicalFlag ? "clean object" : "target object";
            RejectUnknownProperties(el, context, RomIdentityPropertyNames);
            var id = new BuildfileRomIdentity
            {
                Size = RequireUInt(el, "size"),
                Crc32 = RequireString(el, "crc32"),
                Sha256 = RequireString(el, "sha256"),
            };
            if (requireCanonicalFlag)
                id.IsCanonicalOriginal = RequireBool(el, "isCanonicalOriginal");
            else if (el.TryGetProperty("isCanonicalOriginal", out _))
                id.IsCanonicalOriginal = RequireBool(el, "isCanonicalOriginal");
            return id;
        }

        static byte[] ReadManifestBytes(string manifestPath, BuildfileBuildOptions options)
        {
            long cap = options.EffectiveMaxManifestBytes;
            FileStream stream;
            try
            {
                stream = ProjectionFileSystemSafety.OpenRegularFileForRead(manifestPath);
            }
            catch (Exception ex) when (IsExpectedIo(ex))
            {
                throw new BuildfileValidationException(
                    "buildfile.json is missing or not a plain regular file: " + ex.Message);
            }

            using (stream)
            {
                long length = stream.Length;
                if (length < 0)
                    throw new BuildfileValidationException("buildfile.json has an invalid length.");
                if (length > cap)
                    throw new BuildfileValidationException(
                        "buildfile.json exceeds the " + cap + "-byte limit.");
                var data = new byte[(int)length];
                int offset = 0;
                while (offset < data.Length)
                {
                    int count = stream.Read(data, offset, data.Length - offset);
                    if (count == 0)
                        throw new BuildfileValidationException(
                            "buildfile.json changed or was truncated while being read.");
                    offset += count;
                }
                if (stream.ReadByte() != -1)
                    throw new BuildfileValidationException("buildfile.json grew while being read.");
                return data;
            }
        }

        static byte[] StripBomAndValidateUtf8(byte[] raw)
        {
            if (raw.Length >= 2
                && ((raw[0] == 0xFF && raw[1] == 0xFE) || (raw[0] == 0xFE && raw[1] == 0xFF)))
                throw new BuildfileValidationException(
                    "buildfile.json must be UTF-8 (a UTF-16 byte-order mark was found).");
            if (raw.Length >= 4
                && raw[0] == 0x00 && raw[1] == 0x00 && raw[2] == 0xFE && raw[3] == 0xFF)
                throw new BuildfileValidationException(
                    "buildfile.json must be UTF-8 (a UTF-32 byte-order mark was found).");

            byte[] body = raw;
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            {
                body = new byte[raw.Length - 3];
                Array.Copy(raw, 3, body, 0, body.Length);
            }

            try
            {
                new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(body);
            }
            catch (DecoderFallbackException)
            {
                throw new BuildfileValidationException("buildfile.json is not valid UTF-8.");
            }
            return body;
        }

        static void ThrowIfDuplicateProperties(byte[] utf8)
        {
            var readerOptions = new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = BuildfileBuildOptions.MaxJsonDepth,
            };
            var reader = new Utf8JsonReader(utf8, readerOptions);
            var scopes = new Stack<HashSet<string>>();
            try
            {
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                            break;
                        case JsonTokenType.EndObject:
                            if (scopes.Count > 0) scopes.Pop();
                            break;
                        case JsonTokenType.PropertyName:
                            string name = reader.GetString() ?? "";
                            if (scopes.Count > 0 && !scopes.Peek().Add(name))
                                throw new BuildfileValidationException(
                                    "buildfile.json contains a duplicate property name '" + name + "'.");
                            break;
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new BuildfileValidationException("buildfile.json is not valid JSON: " + ex.Message);
            }
        }

        static JsonDocument ParseJson(byte[] utf8)
        {
            try
            {
                return JsonDocument.Parse(utf8, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                    MaxDepth = BuildfileBuildOptions.MaxJsonDepth,
                });
            }
            catch (JsonException ex)
            {
                throw new BuildfileValidationException("buildfile.json is not valid JSON: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- data capture

        static Dictionary<string, byte[]> CaptureAndValidatePayloads(
            string projectDir,
            BuildfileManifest m,
            byte[] cleanBytes)
        {
            string dataDir = Path.Combine(projectDir, m.DataDirectory);
            if (!Directory.Exists(dataDir))
                throw new BuildfileValidationException(
                    "Recipe data directory not found: " + m.DataDirectory + "/");

            int maxEntries = Math.Max(1, m.TotalRanges);
            long maxBytes = Math.Max(1L, m.TotalChangedBytes);

            ProjectionTreeSnapshot snapshot;
            try
            {
                snapshot = ProjectionTreeSnapshotReader.Capture(
                    dataDir,
                    maxEntries,
                    maxBytes,
                    maxBytes,
                    null);
            }
            catch (Exception ex) when (IsExpectedIo(ex))
            {
                throw new BuildfileValidationException(
                    "Cannot read the recipe data directory safely: " + ex.Message);
            }

            if (snapshot.Directories.Count > 0)
                throw new BuildfileValidationException(
                    "Recipe data directory must not contain subdirectories: data/"
                    + snapshot.Directories[0]);

            var captured = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var caseFold = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectionTreeSnapshotFile f in snapshot.Files)
            {
                // No-subdirectory invariant already enforced, so the snapshot-relative name
                // is exactly the payload's file name mapped back under the manifest data/ prefix.
                string path = "data/" + f.RelativePath;
                if (!caseFold.Add(path))
                    throw new BuildfileValidationException(
                        "Recipe data directory contains a case-colliding payload name: " + path);
                captured[path] = f.Data;
            }

            foreach (BuildfileRange r in m.Ranges)
            {
                if (!captured.TryGetValue(r.Payload, out byte[] bytes))
                    throw new BuildfileValidationException(
                        "Missing payload file declared by the recipe: " + r.Payload);
                if ((uint)bytes.Length != r.Length)
                    throw new BuildfileValidationException(
                        "Payload " + r.Payload + " length " + bytes.Length
                        + " does not match the declared length " + r.Length + ".");
                if (!string.Equals(BuildfileFormat.Sha256Hex(bytes), r.PayloadSha256, StringComparison.Ordinal))
                    throw new BuildfileValidationException(
                        "Payload " + r.Payload + " sha256 does not match the declared hash.");

                byte extensionFill = m.Extension == null
                    ? (byte)0
                    : (byte)Convert.ToInt32(m.Extension.FillByte.Substring(2), 16);
                for (int i = 0; i < bytes.Length; i++)
                {
                    long targetOffset = (long)r.Offset + i;
                    byte baseline = targetOffset < cleanBytes.Length
                        ? cleanBytes[targetOffset]
                        : extensionFill;
                    if (bytes[i] == baseline)
                    {
                        throw new BuildfileValidationException(
                            "Payload " + r.Payload + " contains an unchanged byte at range offset "
                            + i + "; changedBytes must equal length.");
                    }
                }
            }

            if (captured.Count > m.Ranges.Count)
            {
                var declared = new HashSet<string>(StringComparer.Ordinal);
                foreach (BuildfileRange r in m.Ranges) declared.Add(r.Payload);
                foreach (string key in captured.Keys)
                    if (!declared.Contains(key))
                        throw new BuildfileValidationException(
                            "Unexpected payload file not declared by the recipe: " + key);
            }

            return captured;
        }

        // --------------------------------------------------- atomic no-replace publish

        /// <summary>
        /// Atomically publish <paramref name="data"/> to a brand-new
        /// <paramref name="destinationPath"/> using a same-parent exclusive staging file,
        /// a durable flush, a dispose-before-native no-replace rename, and verified staging
        /// cleanup on failure. Never overwrites an existing destination (a destination race
        /// fails without replacing either file).
        /// </summary>
        public static bool PublishBytesNoReplace(byte[] data, string destinationPath, out string error)
            => PublishBytesNoReplace(data, destinationPath, null, null, out error);

        internal static bool PublishBytesNoReplace(
            byte[] data,
            string destinationPath,
            Action<string, string> beforePublishForTest,
            BuildfileDeletePath deleteStagingForTest,
            out string error)
        {
            error = "";
            if (data == null) { error = "No data to publish."; return false; }
            if (string.IsNullOrEmpty(destinationPath)) { error = "Destination path is required."; return false; }

            string dest;
            try
            {
                dest = BuildfilePathSafety.NormalizeFullPath(destinationPath);
            }
            catch (Exception ex)
            {
                error = "Invalid destination path: " + ex.Message;
                return false;
            }

            string parent = Path.GetDirectoryName(dest);
            if (string.IsNullOrEmpty(parent))
            {
                error = "Destination has no parent directory: " + dest;
                return false;
            }
            if (!Directory.Exists(parent))
            {
                error = "Destination parent directory does not exist: " + parent;
                return false;
            }
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                error = "Destination already exists: " + dest;
                return false;
            }
            try
            {
                if (BuildfilePathSafety.IsReparsePoint(parent))
                {
                    error = "Destination parent directory is a symlink/junction (reparse point); "
                        + "refusing to guarantee an atomic same-parent publish: " + parent;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            string stage = null;
            try
            {
                stage = WriteStagingFile(parent, Path.GetFileName(dest), data);
                // File-level wrapper over the shared native no-replace rename contract
                // (Windows MoveFileExW without replace, Linux renameat2(RENAME_NOREPLACE),
                // macOS renamex_np(RENAME_EXCL)); identical semantics for a regular file.
                beforePublishForTest?.Invoke(stage, dest);
                BuildfileExportCore.PublishDirectoryNoReplace(stage, dest);
                return true;
            }
            catch (Exception ex)
            {
                string cleanupError = "";
                bool cleanupOk = stage == null
                    || (deleteStagingForTest != null
                        ? deleteStagingForTest(stage, out cleanupError)
                        : DeleteFileAndVerifyGone(stage, out cleanupError));
                if (!cleanupOk)
                    error = "Publish failed: " + ex.Message
                        + " Cleanup incomplete for staging file '" + stage + "': " + cleanupError;
                else
                    error = "Publish failed: " + ex.Message;
                return false;
            }
        }

        static string WriteStagingFile(string parent, string destName, byte[] data)
        {
            string prefix = BuildfileExportCore.MakeTemporaryDirectoryPrefix(
                destName,
                "rom-stage");
            for (int attempt = 0; attempt < 64; attempt++)
            {
                string stage = Path.Combine(
                    parent, prefix + Guid.NewGuid().ToString("N") + ".tmp");
                FileStream stream;
                try
                {
                    stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (IOException) when (File.Exists(stage))
                {
                    continue; // name collision — reserve a different sibling
                }
                try
                {
                    using (stream)
                    {
                        stream.Write(data, 0, data.Length);
                        stream.Flush(flushToDisk: true); // durable flush before dispose + rename
                    }
                }
                catch
                {
                    // The exclusive staging file was created but not fully written. Never hide
                    // a cleanup failure: the caller must receive the exact residual path.
                    if (!DeleteFileAndVerifyGone(stage, out string cleanupError))
                    {
                        throw new IOException(
                            "Staging write failed and cleanup was incomplete for '"
                            + stage + "': " + cleanupError);
                    }
                    throw;
                }
                return stage;
            }
            throw new IOException(
                "Could not reserve a unique staging file after repeated name collisions.");
        }

        static bool DeleteFileAndVerifyGone(string path, out string error)
        {
            error = "";
            try
            {
                File.Delete(path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) when (IsExpectedIo(ex))
            {
                error = ex.Message;
                return false;
            }
            if (File.Exists(path))
            {
                error = "staging file still present after delete";
                return false;
            }
            return true;
        }

        // --------------------------------------------------------- scratch reservation

        /// <summary>
        /// Atomically reserve a uniquely named private scratch directory under
        /// <paramref name="parent"/> (exclusive create-new; a name collision is never reused).
        /// </summary>
        public static string ReserveScratchDirectory(string parent, string prefix)
        {
            if (string.IsNullOrEmpty(parent))
                throw new ArgumentException("Scratch parent is required.", nameof(parent));
            for (int attempt = 0; attempt < 64; attempt++)
            {
                string candidate = Path.Combine(parent, (prefix ?? "") + Guid.NewGuid().ToString("N"));
                if (BuildfileExportCore.TryCreateDirectoryExclusive(candidate))
                    return candidate;
            }
            throw new IOException(
                "Could not reserve a unique scratch directory after repeated name collisions.");
        }

        /// <summary>Delete a directory tree and verify it is gone (shared exporter contract).</summary>
        public static bool DeleteTreeAndVerifyGone(string dir, out string error)
            => BuildfileExportCore.DeleteAndVerifyGone(
                dir, Directory.Delete, File.GetAttributes, out error);

        // ------------------------------------------------------------- strict JSON reads

        static void RejectUnknownProperties(
            JsonElement element,
            string context,
            HashSet<string> allowedNames)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!allowedNames.Contains(property.Name))
                {
                    throw new BuildfileValidationException(
                        "buildfile.json " + context + " contains unknown property '"
                        + property.Name + "'.");
                }
            }
        }

        // Populates the advisory (non-authority) portions of the manifest POCO while
        // performing EXACTLY the same structural/type validation as before. Advisory
        // values are recorded for round-trip fidelity (e.g. re-export, diagnostics,
        // review tooling) but — per the file header — can NEVER influence a single
        // reconstructed target byte; only clean bytes, extension geometry/fill, and
        // ranges/payloads do that.
        static void PopulateAndValidateAdvisoryStructure(JsonElement root, BuildfileManifest m)
        {
            m.Tool = OptionalString(root, "tool");
            m.Game = OptionalString(root, "game");
            m.EntryEvent = OptionalString(root, "entryEvent");

            if (root.TryGetProperty("patches", out JsonElement patches))
            {
                if (patches.ValueKind != JsonValueKind.Object)
                    throw new BuildfileValidationException(
                        "buildfile.json property 'patches' must be an object.");
                RejectUnknownProperties(patches, "patches object", PatchInventoryPropertyNames);
                var patchInventory = new BuildfilePatchInventory
                {
                    Status = OptionalString(patches, "status"),
                    Reason = OptionalString(patches, "reason"),
                    BaseRelative = OptionalString(patches, "baseRelative"),
                };

                if (patches.TryGetProperty("installed", out JsonElement installed))
                {
                    if (installed.ValueKind != JsonValueKind.Array)
                        throw new BuildfileValidationException(
                            "buildfile.json property 'patches.installed' must be an array.");
                    foreach (JsonElement record in installed.EnumerateArray())
                    {
                        if (record.ValueKind != JsonValueKind.Object)
                            throw new BuildfileValidationException(
                                "buildfile.json patches.installed entries must be objects.");
                        RejectUnknownProperties(
                            record, "patch record object", PatchRecordPropertyNames);
                        var patchRecord = new BuildfilePatchRecord
                        {
                            Name = OptionalString(record, "name"),
                            Path = OptionalString(record, "path"),
                            Status = OptionalString(record, "status"),
                            Confidence = OptionalString(record, "confidence"),
                            Reason = OptionalString(record, "reason"),
                        };

                        if (record.TryGetProperty("params", out JsonElement parameters))
                        {
                            if (parameters.ValueKind != JsonValueKind.Array)
                                throw new BuildfileValidationException(
                                    "buildfile.json property 'patches.installed.params' "
                                    + "must be an array.");
                            foreach (JsonElement parameter in parameters.EnumerateArray())
                            {
                                if (parameter.ValueKind != JsonValueKind.Object)
                                    throw new BuildfileValidationException(
                                        "buildfile.json patch params entries must be objects.");
                                RejectUnknownProperties(
                                    parameter, "patch param object", PatchParamPropertyNames);
                                patchRecord.Params.Add(new BuildfilePatchParam
                                {
                                    Key = OptionalString(parameter, "key"),
                                    Value = OptionalString(parameter, "value"),
                                });
                            }
                        }

                        patchInventory.Installed.Add(patchRecord);
                    }
                }

                m.Patches = patchInventory;
            }

            if (root.TryGetProperty("projection", out JsonElement projection))
            {
                if (projection.ValueKind != JsonValueKind.Object)
                    throw new BuildfileValidationException(
                        "buildfile.json property 'projection' must be an object.");
                RejectUnknownProperties(projection, "projection object", ProjectionPropertyNames);
                var projectionInfo = new BuildfileProjectionInfo
                {
                    Status = OptionalString(projection, "status"),
                    Reason = OptionalString(projection, "reason"),
                };
                if (projection.TryGetProperty("directory", out JsonElement directory))
                {
                    if (directory.ValueKind == JsonValueKind.String)
                        projectionInfo.Directory = directory.GetString();
                    else if (directory.ValueKind == JsonValueKind.Null)
                        projectionInfo.Directory = null;
                    else
                        throw new BuildfileValidationException(
                            "buildfile.json property 'projection.directory' "
                            + "must be a string or null.");
                }
                m.Projection = projectionInfo;
            }

            if (root.TryGetProperty("warnings", out JsonElement warnings))
            {
                if (warnings.ValueKind != JsonValueKind.Array)
                    throw new BuildfileValidationException(
                        "buildfile.json property 'warnings' must be an array.");
                var warningList = new List<string>();
                foreach (JsonElement warning in warnings.EnumerateArray())
                {
                    if (warning.ValueKind != JsonValueKind.String)
                        throw new BuildfileValidationException(
                            "buildfile.json warnings entries must be strings.");
                    warningList.Add(warning.GetString() ?? "");
                }
                m.Warnings = warningList;
            }
        }

        static JsonElement RequireProperty(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object
                || !parent.TryGetProperty(name, out JsonElement value))
                throw new BuildfileValidationException(
                    "buildfile.json is missing required property '" + name + "'.");
            return value;
        }

        static string RequireString(JsonElement parent, string name)
        {
            JsonElement value = RequireProperty(parent, name);
            if (value.ValueKind != JsonValueKind.String)
                throw new BuildfileValidationException(
                    "buildfile.json property '" + name + "' must be a string.");
            return value.GetString() ?? "";
        }

        static int RequireInt(JsonElement parent, string name)
        {
            JsonElement value = RequireProperty(parent, name);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
                throw new BuildfileValidationException(
                    "buildfile.json property '" + name + "' must be a 32-bit integer.");
            return result;
        }

        static uint RequireUInt(JsonElement parent, string name)
        {
            JsonElement value = RequireProperty(parent, name);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out uint result))
                throw new BuildfileValidationException(
                    "buildfile.json property '" + name + "' must be an unsigned 32-bit integer.");
            return result;
        }

        static bool RequireBool(JsonElement parent, string name)
        {
            JsonElement value = RequireProperty(parent, name);
            if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                throw new BuildfileValidationException(
                    "buildfile.json property '" + name + "' must be a boolean.");
            return value.GetBoolean();
        }

        static JsonElement RequireObject(JsonElement parent, string name)
        {
            JsonElement value = RequireProperty(parent, name);
            if (value.ValueKind != JsonValueKind.Object)
                throw new BuildfileValidationException(
                    "buildfile.json property '" + name + "' must be an object.");
            return value;
        }

        static JsonElement RequireArray(JsonElement parent, string name)
        {
            JsonElement value = RequireProperty(parent, name);
            if (value.ValueKind != JsonValueKind.Array)
                throw new BuildfileValidationException(
                    "buildfile.json property '" + name + "' must be an array.");
            return value;
        }

        static string OptionalString(JsonElement parent, string name)
        {
            if (parent.ValueKind == JsonValueKind.Object
                && parent.TryGetProperty(name, out JsonElement value))
            {
                if (value.ValueKind != JsonValueKind.String)
                    throw new BuildfileValidationException(
                        "buildfile.json property '" + name + "' must be a string.");
                return value.GetString() ?? "";
            }
            return "";
        }

        static bool IsExpectedIo(Exception ex)
            => ex is IOException
            || ex is UnauthorizedAccessException
            || ex is NotSupportedException
            || ex is System.Security.SecurityException
            || ex is ArgumentException;
    }
}
