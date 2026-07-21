// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace FEBuilderGBA
{
    /// <summary>Typed outcome of looking up a per-tileset-fingerprint FEMapCreator mapping (#1978 Slice 2).</summary>
    public enum FEMapCreatorMappingStatus
    {
        /// <summary>No mapping has ever been recorded for this fingerprint.</summary>
        NoMapping,

        /// <summary>A mapping exists and its recorded external-file identity still matches the files on disk right now.</summary>
        Current,

        /// <summary>A mapping exists but the mapped image/generation-data file has changed size, timestamp, or
        /// content hash, has gone missing, or the currently-configured FEMapCreator executable/assets root
        /// no longer matches what was recorded when the mapping was made.</summary>
        Stale,

        /// <summary>A mapping entry exists in storage but is structurally unusable (e.g. missing required fields
        /// from an unknown/legacy schema) and cannot be trusted.</summary>
        Invalid,
    }

    /// <summary>
    /// One persisted external FEMapCreator tileset mapped to a specific built-in tileset fingerprint
    /// (#1978 Slice 2). Immutable; captures enough concrete file identity (path + size + last-write
    /// time + SHA-256) for the mapped image/generation-data files, AND for the FEMapCreator
    /// executable itself, so <see cref="FEMapCreatorTilesetMappingStoreCore.Lookup"/> can detect
    /// staleness caused by any of those three files changing — without re-running FEMapCreator
    /// discovery. The assets root is recorded as a normalized path only (it is a directory, not a
    /// single hashable file); an empty assets root is a valid, fully-supported identity value.
    /// </summary>
    public sealed class FEMapCreatorTilesetMappingEntry
    {
        public FEMapCreatorTilesetMappingEntry(
            string fingerprintValue,
            string tilesetName,
            string imagePath, long imageSizeBytes, long imageLastWriteUtcTicks, string imageSha256,
            string generationDataPath, long generationDataSizeBytes, long generationDataLastWriteUtcTicks, string generationDataSha256,
            string executablePath, long executableSizeBytes, long executableLastWriteUtcTicks, string executableSha256,
            string assetsRoot)
        {
            FingerprintValue = fingerprintValue ?? "";
            TilesetName = tilesetName ?? "";
            ImagePath = imagePath ?? "";
            ImageSizeBytes = imageSizeBytes;
            ImageLastWriteUtcTicks = imageLastWriteUtcTicks;
            ImageSha256 = imageSha256 ?? "";
            GenerationDataPath = generationDataPath ?? "";
            GenerationDataSizeBytes = generationDataSizeBytes;
            GenerationDataLastWriteUtcTicks = generationDataLastWriteUtcTicks;
            GenerationDataSha256 = generationDataSha256 ?? "";
            ExecutablePath = executablePath ?? "";
            ExecutableSizeBytes = executableSizeBytes;
            ExecutableLastWriteUtcTicks = executableLastWriteUtcTicks;
            ExecutableSha256 = executableSha256 ?? "";
            AssetsRoot = assetsRoot ?? "";
        }

        /// <summary>Stable key: <see cref="TilesetFingerprint.Value"/> of the built-in tileset this mapping applies to.</summary>
        public string FingerprintValue { get; }

        /// <summary>Logical FEMapCreator tileset name reported by discovery.</summary>
        public string TilesetName { get; }

        /// <summary>Confined absolute path to the mapped tileset image file at the time this mapping was recorded.</summary>
        public string ImagePath { get; }
        public long ImageSizeBytes { get; }
        public long ImageLastWriteUtcTicks { get; }
        public string ImageSha256 { get; }

        /// <summary>Confined absolute path to the mapped generation-data file at the time this mapping was recorded.</summary>
        public string GenerationDataPath { get; }
        public long GenerationDataSizeBytes { get; }
        public long GenerationDataLastWriteUtcTicks { get; }
        public string GenerationDataSha256 { get; }

        /// <summary>Normalized FEMapCreator executable path in effect when this mapping was recorded.</summary>
        public string ExecutablePath { get; }
        public long ExecutableSizeBytes { get; }
        public long ExecutableLastWriteUtcTicks { get; }
        public string ExecutableSha256 { get; }

        /// <summary>Normalized FEMapCreator assets root in effect when this mapping was recorded (may be empty — a blank assets root is a valid identity value).</summary>
        public string AssetsRoot { get; }

        /// <summary>
        /// True when the entry has the minimum fields required to be trusted: non-empty
        /// fingerprint, tileset name, both mapped file paths, and a recorded executable
        /// path+hash (the assets root remains optional/may be blank). Entries failing this check
        /// are treated as unknown/legacy/malformed and preserved by
        /// <see cref="FEMapCreatorTilesetMappingStoreCore.LoadAll"/> when they retain a
        /// fingerprint, then flagged <see cref="FEMapCreatorMappingStatus.Invalid"/> by
        /// <see cref="FEMapCreatorTilesetMappingStoreCore.Lookup"/>.
        /// </summary>
        public bool IsStructurallyValid =>
            !string.IsNullOrWhiteSpace(FingerprintValue)
            && !string.IsNullOrWhiteSpace(TilesetName)
            && !string.IsNullOrWhiteSpace(ImagePath)
            && !string.IsNullOrWhiteSpace(GenerationDataPath)
            && !string.IsNullOrWhiteSpace(ExecutablePath)
            && !string.IsNullOrWhiteSpace(ExecutableSha256);
    }

    /// <summary>Result of <see cref="FEMapCreatorTilesetMappingStoreCore.Lookup"/>.</summary>
    public sealed class FEMapCreatorMappingLookupResult
    {
        FEMapCreatorMappingLookupResult(FEMapCreatorMappingStatus status, FEMapCreatorTilesetMappingEntry entry, string reason)
        {
            Status = status;
            Entry = entry;
            Reason = reason ?? "";
        }

        /// <summary>Typed lookup outcome.</summary>
        public FEMapCreatorMappingStatus Status { get; }

        /// <summary>The stored entry for <see cref="FEMapCreatorMappingStatus.Current"/>, <see cref="FEMapCreatorMappingStatus.Stale"/>,
        /// and <see cref="FEMapCreatorMappingStatus.Invalid"/>; null for <see cref="FEMapCreatorMappingStatus.NoMapping"/>.</summary>
        public FEMapCreatorTilesetMappingEntry Entry { get; }

        /// <summary>Human-readable detail explaining a <see cref="FEMapCreatorMappingStatus.Stale"/> or
        /// <see cref="FEMapCreatorMappingStatus.Invalid"/> result; "" for Current/NoMapping.</summary>
        public string Reason { get; }

        public static FEMapCreatorMappingLookupResult NoMapping() =>
            new FEMapCreatorMappingLookupResult(FEMapCreatorMappingStatus.NoMapping, null, "");
        public static FEMapCreatorMappingLookupResult Current(FEMapCreatorTilesetMappingEntry entry) =>
            new FEMapCreatorMappingLookupResult(FEMapCreatorMappingStatus.Current, entry, "");
        public static FEMapCreatorMappingLookupResult Stale(FEMapCreatorTilesetMappingEntry entry, string reason) =>
            new FEMapCreatorMappingLookupResult(FEMapCreatorMappingStatus.Stale, entry, reason);
        public static FEMapCreatorMappingLookupResult Invalid(FEMapCreatorTilesetMappingEntry entry, string reason) =>
            new FEMapCreatorMappingLookupResult(FEMapCreatorMappingStatus.Invalid, entry, reason);
    }

    /// <summary>
    /// Persists and revalidates per-tileset-fingerprint FEMapCreator mappings inside the shared
    /// <see cref="Config"/> store (#1978 Slice 2). Deliberately holds no static/in-memory cache:
    /// every call re-reads the mappings supplied by the caller (loaded fresh from
    /// <see cref="Config"/> via <see cref="LoadAll"/>) and, for <see cref="Lookup"/>, re-stats and
    /// re-hashes the mapped files on disk right now, AND requires the caller to pass a freshly
    /// validated <see cref="FEMapCreatorSetupSnapshot"/> (<c>currentProfile</c>) so the currently
    /// configured executable/assets-root identity is compared against what was recorded at
    /// mapping-creation time — so a mapping never leaks stale state across ROM/tileset switches or
    /// FEMapCreator reconfiguration, and a concurrently-edited external file or executable is
    /// always caught rather than silently presented as current. File reads only: this class never
    /// launches a process or touches the network.
    /// </summary>
    public static class FEMapCreatorTilesetMappingStoreCore
    {
        /// <summary>Config key holding the JSON-serialized mapping list.</summary>
        public const string MappingsConfigKey = "femapcreator_tileset_mappings";

        sealed class MappingDto
        {
            public string FingerprintValue { get; set; } = "";
            public string TilesetName { get; set; } = "";
            public string ImagePath { get; set; } = "";
            public long ImageSizeBytes { get; set; }
            public long ImageLastWriteUtcTicks { get; set; }
            public string ImageSha256 { get; set; } = "";
            public string GenerationDataPath { get; set; } = "";
            public long GenerationDataSizeBytes { get; set; }
            public long GenerationDataLastWriteUtcTicks { get; set; }
            public string GenerationDataSha256 { get; set; } = "";
            public string ExecutablePath { get; set; } = "";
            public long ExecutableSizeBytes { get; set; }
            public long ExecutableLastWriteUtcTicks { get; set; }
            public string ExecutableSha256 { get; set; } = "";
            public string AssetsRoot { get; set; } = "";
        }

        /// <summary>
        /// Load all persisted mappings from <paramref name="config"/>. Tolerates a missing key, an
        /// empty/corrupt JSON blob (treated as zero mappings), and individual malformed/legacy
        /// entries. Fingerprinted malformed entries are preserved so lookup can report Invalid;
        /// only rows without a fingerprint are skipped. This method never throws.
        /// </summary>
        public static IReadOnlyList<FEMapCreatorTilesetMappingEntry> LoadAll(Config config)
        {
            var result = new List<FEMapCreatorTilesetMappingEntry>();
            if (config == null) return result;

            string raw = config.at(MappingsConfigKey, "");
            if (string.IsNullOrWhiteSpace(raw)) return result;

            List<MappingDto> dtos;
            try
            {
                dtos = JsonSerializer.Deserialize<List<MappingDto>>(raw);
            }
            catch (JsonException)
            {
                // Corrupt/foreign blob: treat as zero mappings rather than crashing the caller.
                return result;
            }
            if (dtos == null) return result;

            foreach (MappingDto dto in dtos)
            {
                if (dto == null) continue;
                var entry = new FEMapCreatorTilesetMappingEntry(
                    dto.FingerprintValue, dto.TilesetName,
                    dto.ImagePath, dto.ImageSizeBytes, dto.ImageLastWriteUtcTicks, dto.ImageSha256,
                    dto.GenerationDataPath, dto.GenerationDataSizeBytes, dto.GenerationDataLastWriteUtcTicks, dto.GenerationDataSha256,
                    dto.ExecutablePath, dto.ExecutableSizeBytes, dto.ExecutableLastWriteUtcTicks, dto.ExecutableSha256,
                    dto.AssetsRoot);
                if (string.IsNullOrWhiteSpace(entry.FingerprintValue))
                    continue;
                result.Add(entry);
            }
            return result;
        }

        /// <summary>
        /// Serialize <paramref name="mappings"/> back into <paramref name="config"/>. The caller is
        /// responsible for persisting the config afterward. Workflows that publish a success state
        /// must use <see cref="Config.SaveOrThrow(string)"/> so write failures remain observable.
        /// </summary>
        public static void SaveAll(Config config, IReadOnlyList<FEMapCreatorTilesetMappingEntry> mappings)
        {
            if (config == null) return;
            mappings ??= Array.Empty<FEMapCreatorTilesetMappingEntry>();

            var dtos = mappings
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.FingerprintValue))
                .Select(e => new MappingDto
            {
                FingerprintValue = e.FingerprintValue,
                TilesetName = e.TilesetName,
                ImagePath = e.ImagePath,
                ImageSizeBytes = e.ImageSizeBytes,
                ImageLastWriteUtcTicks = e.ImageLastWriteUtcTicks,
                ImageSha256 = e.ImageSha256,
                GenerationDataPath = e.GenerationDataPath,
                GenerationDataSizeBytes = e.GenerationDataSizeBytes,
                GenerationDataLastWriteUtcTicks = e.GenerationDataLastWriteUtcTicks,
                GenerationDataSha256 = e.GenerationDataSha256,
                ExecutablePath = e.ExecutablePath,
                ExecutableSizeBytes = e.ExecutableSizeBytes,
                ExecutableLastWriteUtcTicks = e.ExecutableLastWriteUtcTicks,
                ExecutableSha256 = e.ExecutableSha256,
                AssetsRoot = e.AssetsRoot,
            }).ToList();

            config[MappingsConfigKey] = JsonSerializer.Serialize(dtos);
        }

        /// <summary>
        /// Build a new entry from the current on-disk state of <paramref name="imagePath"/> and
        /// <paramref name="generationDataPath"/>, recording <paramref name="currentProfile"/>'s
        /// executable identity and assets root alongside them. Never throws; returns false with
        /// <paramref name="error"/> set when the fingerprint/tileset name are blank, either mapped
        /// file cannot be read, or <paramref name="currentProfile"/> is not currently
        /// <see cref="FEMapCreatorSetupStatus.Configured"/> (a mapping cannot safely record an
        /// executable identity that was never successfully validated).
        /// </summary>
        public static bool TryCreateEntry(
            TilesetFingerprint fingerprint,
            string tilesetName,
            string imagePath,
            string generationDataPath,
            FEMapCreatorSetupSnapshot currentProfile,
            out FEMapCreatorTilesetMappingEntry entry,
            out string error)
            => TryCreateEntry(
                fingerprint,
                tilesetName,
                imagePath,
                generationDataPath,
                currentProfile,
                CancellationToken.None,
                out entry,
                out error);

        /// <summary>
        /// Cancellation-aware entry creation used by asynchronous UI workflows. Cancellation is
        /// observed before validation and throughout both bounded file-hash reads; validation
        /// failures still return false, while cancellation propagates as
        /// <see cref="OperationCanceledException"/>.
        /// </summary>
        internal static bool TryCreateEntry(
            TilesetFingerprint fingerprint,
            string tilesetName,
            string imagePath,
            string generationDataPath,
            FEMapCreatorSetupSnapshot currentProfile,
            CancellationToken cancellationToken,
            out FEMapCreatorTilesetMappingEntry entry,
            out string error)
        {
            entry = null;
            error = "";
            cancellationToken.ThrowIfCancellationRequested();

            if (fingerprint.IsEmpty)
            {
                error = "Tileset fingerprint is empty.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(tilesetName))
            {
                error = "Tileset name is empty.";
                return false;
            }
            if (currentProfile == null
                || currentProfile.Status != FEMapCreatorSetupStatus.Configured
                || string.IsNullOrWhiteSpace(currentProfile.ExecutableSha256))
            {
                error = "FEMapCreator executable is not authoritatively validated; cannot record a mapping without a current executable content hash.";
                return false;
            }

            if (!FileContentIdentityCore.TryCompute(
                imagePath,
                cancellationToken,
                out long imageSize,
                out long imageTicks,
                out string imageSha,
                out error))
                return false;
            if (!FileContentIdentityCore.TryCompute(
                generationDataPath,
                cancellationToken,
                out long dataSize,
                out long dataTicks,
                out string dataSha,
                out error))
                return false;

            entry = new FEMapCreatorTilesetMappingEntry(
                fingerprint.Value, tilesetName,
                imagePath, imageSize, imageTicks, imageSha,
                generationDataPath, dataSize, dataTicks, dataSha,
                currentProfile.ExecutablePath, currentProfile.ExecutableSizeBytes, currentProfile.ExecutableLastWriteUtcTicks, currentProfile.ExecutableSha256,
                currentProfile.AssetsRoot);
            return true;
        }

        /// <summary>
        /// Return a new list with any existing entry for the same fingerprint replaced in place
        /// (preserving its original position), or the new entry appended at the end when no such
        /// fingerprint was present. Pure — does not mutate <paramref name="mappings"/>.
        /// </summary>
        public static IReadOnlyList<FEMapCreatorTilesetMappingEntry> Upsert(
            IReadOnlyList<FEMapCreatorTilesetMappingEntry> mappings,
            FEMapCreatorTilesetMappingEntry entry)
        {
            mappings ??= Array.Empty<FEMapCreatorTilesetMappingEntry>();
            if (entry == null) return mappings.ToList();

            var result = new List<FEMapCreatorTilesetMappingEntry>(mappings.Count + 1);
            bool replaced = false;
            foreach (FEMapCreatorTilesetMappingEntry existing in mappings)
            {
                if (!replaced && string.Equals(existing.FingerprintValue, entry.FingerprintValue, StringComparison.Ordinal))
                {
                    result.Add(entry);
                    replaced = true;
                }
                else
                {
                    result.Add(existing);
                }
            }
            if (!replaced) result.Add(entry);
            return result;
        }

        /// <summary>Return a new list with the mapping for <paramref name="fingerprint"/> removed, if present.</summary>
        public static IReadOnlyList<FEMapCreatorTilesetMappingEntry> Remove(
            IReadOnlyList<FEMapCreatorTilesetMappingEntry> mappings,
            TilesetFingerprint fingerprint)
        {
            mappings ??= Array.Empty<FEMapCreatorTilesetMappingEntry>();
            return mappings.Where(e => !string.Equals(e.FingerprintValue, fingerprint.Value, StringComparison.Ordinal)).ToList();
        }

        /// <summary>
        /// Find the mapping for <paramref name="fingerprint"/> and revalidate its recorded
        /// identity — the mapped image/generation-data files on disk right now, AND
        /// <paramref name="currentProfile"/> (the caller's freshly-validated
        /// <see cref="FEMapCreatorProfileCore.Validate"/> snapshot) against the executable/assets
        /// identity recorded at mapping-creation time — before ever reporting
        /// <see cref="FEMapCreatorMappingStatus.Current"/>. This is the only safe selection path:
        /// a mapping is never presented as current merely because the mapped tileset files are
        /// unchanged while the configured FEMapCreator executable or assets root has since
        /// changed, been cleared, or become invalid. Any missing/changed mapped file, executable
        /// mismatch, or assets-root mismatch yields <see cref="FEMapCreatorMappingStatus.Stale"/>
        /// with a specific <see cref="FEMapCreatorMappingLookupResult.Reason"/>. A structurally
        /// invalid stored entry (e.g. from a future/foreign schema) yields
        /// <see cref="FEMapCreatorMappingStatus.Invalid"/> rather than being silently skipped or
        /// presented as usable.
        /// </summary>
        public static FEMapCreatorMappingLookupResult Lookup(
            IReadOnlyList<FEMapCreatorTilesetMappingEntry> mappings,
            TilesetFingerprint fingerprint,
            FEMapCreatorSetupSnapshot currentProfile)
            => Lookup(mappings, fingerprint, currentProfile, CancellationToken.None);

        /// <summary>
        /// Cancellation-aware authoritative lookup used by one-click generation. Cancellation
        /// propagates while either mapped file is being hashed.
        /// </summary>
        internal static FEMapCreatorMappingLookupResult Lookup(
            IReadOnlyList<FEMapCreatorTilesetMappingEntry> mappings,
            TilesetFingerprint fingerprint,
            FEMapCreatorSetupSnapshot currentProfile,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mappings == null || fingerprint.IsEmpty)
                return FEMapCreatorMappingLookupResult.NoMapping();

            FEMapCreatorTilesetMappingEntry entry = mappings.FirstOrDefault(
                e => string.Equals(e.FingerprintValue, fingerprint.Value, StringComparison.Ordinal));
            if (entry == null)
                return FEMapCreatorMappingLookupResult.NoMapping();

            if (!entry.IsStructurallyValid)
                return FEMapCreatorMappingLookupResult.Invalid(entry, "Stored mapping entry is missing required fields.");

            if (!FileContentIdentityCore.TryCompute(
                entry.ImagePath,
                cancellationToken,
                out long imageSize,
                out long imageTicks,
                out string imageSha,
                out string imageError))
                return FEMapCreatorMappingLookupResult.Stale(entry, "Mapped image file is no longer readable: " + imageError);
            if (imageSize != entry.ImageSizeBytes || imageTicks != entry.ImageLastWriteUtcTicks
                || !string.Equals(imageSha, entry.ImageSha256, StringComparison.Ordinal))
                return FEMapCreatorMappingLookupResult.Stale(entry, "Mapped image file has changed since this mapping was recorded.");

            if (!FileContentIdentityCore.TryCompute(
                entry.GenerationDataPath,
                cancellationToken,
                out long dataSize,
                out long dataTicks,
                out string dataSha,
                out string dataError))
                return FEMapCreatorMappingLookupResult.Stale(entry, "Mapped generation-data file is no longer readable: " + dataError);
            if (dataSize != entry.GenerationDataSizeBytes || dataTicks != entry.GenerationDataLastWriteUtcTicks
                || !string.Equals(dataSha, entry.GenerationDataSha256, StringComparison.Ordinal))
                return FEMapCreatorMappingLookupResult.Stale(entry, "Mapped generation-data file has changed since this mapping was recorded.");

            // Executable/assets-root identity: never accept the mapping as Current on stale
            // image/gen-data evidence alone — the configured FEMapCreator setup itself must
            // still match what was recorded when the mapping was made.
            if (currentProfile == null || currentProfile.Status != FEMapCreatorSetupStatus.Configured)
                return FEMapCreatorMappingLookupResult.Stale(entry, "FEMapCreator executable is not currently configured or is invalid.");

            if (!string.Equals(currentProfile.ExecutablePath, entry.ExecutablePath, FEMapCreatorLauncherCore.PathComparison))
                return FEMapCreatorMappingLookupResult.Stale(entry, "The configured FEMapCreator executable path has changed since this mapping was recorded.");
            if (currentProfile.ExecutableSizeBytes != entry.ExecutableSizeBytes
                || currentProfile.ExecutableLastWriteUtcTicks != entry.ExecutableLastWriteUtcTicks
                || !string.Equals(currentProfile.ExecutableSha256, entry.ExecutableSha256, StringComparison.Ordinal))
                return FEMapCreatorMappingLookupResult.Stale(entry, "The configured FEMapCreator executable's content has changed since this mapping was recorded.");
            if (!string.Equals(currentProfile.AssetsRoot, entry.AssetsRoot, FEMapCreatorLauncherCore.PathComparison))
                return FEMapCreatorMappingLookupResult.Stale(entry, "The configured FEMapCreator assets root has changed since this mapping was recorded.");

            return FEMapCreatorMappingLookupResult.Current(entry);
        }
    }
}
