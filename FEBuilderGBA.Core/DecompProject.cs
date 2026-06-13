using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// Decomp-project "open mode" descriptor (#1129 slice 1 — MVP).
    ///
    /// A decomp project is a source tree (e.g. a fireemblem8u / fe6 decompilation)
    /// that BUILDS a .gba ROM. FEBuilderGBA opens the BUILT ROM as a read-only
    /// PREVIEW; the source is the source of truth and editing/saving the preview ROM
    /// is intentionally blocked (the user edits source and rebuilds instead).
    ///
    /// This whole subsystem is READ-ONLY and NEVER throws: every public method is
    /// bounds-/try-guarded and returns null / NotProject on any fault so that an
    /// early, headless, or hostile call can never crash the host.
    ///
    /// Slice 1 delivers: detection, the descriptor, built-ROM resolution + the
    /// IsDecompMode badge. Symbol resolution, diff-to-source, source writers,
    /// asset exporters and build/reload are deferred to #1130-#1134.
    /// </summary>
    public sealed class DecompProject
    {
        /// <summary>Manifest file name searched at the project root.</summary>
        public const string ManifestFileName = "febuilder.project.json";

        /// <summary>The only manifest schema version slice 1 understands.</summary>
        public const int SupportedSchemaVersion = 1;

        /// <summary>Absolute path to the project root directory.</summary>
        public string ProjectRoot { get; set; } = "";

        /// <summary>
        /// Absolute path to the resolved built ROM. Empty string until
        /// <see cref="DecompProjectDetector.ResolveBuiltRom"/> succeeds and the
        /// host assigns the result.
        /// </summary>
        public string BuiltRomPath { get; set; } = "";

        /// <summary>
        /// Optional forced ROM version string (e.g. "FE8U") from the manifest;
        /// null for auto-detect.
        /// </summary>
        public string ForceVersion { get; set; }

        /// <summary>
        /// Parsed manifest, or null when the project was detected purely by
        /// heuristics (no febuilder.project.json present / parseable).
        /// </summary>
        public DecompManifest Manifest { get; set; }

        // ---- Reserved lazy getters for slices #1130-#1134 (manifest value or null) ----

        /// <summary>Reserved: built ELF path from the manifest (null if unset).</summary>
        public string ElfPath => Manifest?.ElfPath;

        /// <summary>Reserved: linker .map path from the manifest (null if unset).</summary>
        public string MapPath => Manifest?.MapPath;

        /// <summary>Reserved: symbol (.sym) path from the manifest (null if unset).</summary>
        public string SymPath => Manifest?.SymPath;

        /// <summary>Reserved: build command from the manifest (null if unset).</summary>
        public string BuildCommand => Manifest?.BuildCommand;

        /// <summary>True when a non-empty built ROM path is set and the file exists.</summary>
        public bool IsBuilt => !string.IsNullOrEmpty(BuiltRomPath) && File.Exists(BuiltRomPath);
    }

    /// <summary>Status of a built-ROM resolution attempt.</summary>
    public enum DecompResolveStatus
    {
        /// <summary>A built ROM was found.</summary>
        Ok = 0,
        /// <summary>Directory is a decomp project, but no built ROM yet (run the build).</summary>
        NotBuilt = 1,
        /// <summary>Directory is not a decomp project at all.</summary>
        NotProject = 2,
    }

    /// <summary>Typed result of <see cref="DecompProjectDetector.ResolveBuiltRom"/>.</summary>
    public readonly struct ResolvedRom
    {
        public DecompResolveStatus Status { get; }
        public string Path { get; }

        public ResolvedRom(DecompResolveStatus status, string path)
        {
            Status = status;
            Path = path ?? "";
        }

        public static ResolvedRom Ok(string path) => new ResolvedRom(DecompResolveStatus.Ok, path);
        public static readonly ResolvedRom NotBuilt = new ResolvedRom(DecompResolveStatus.NotBuilt, "");
        public static readonly ResolvedRom NotProject = new ResolvedRom(DecompResolveStatus.NotProject, "");
    }

    /// <summary>
    /// Manifest POCO for <c>febuilder.project.json</c>. Only the slice-1 fields are
    /// typed; every reserved section is captured as a tolerant <see cref="JsonElement"/>
    /// (plus a catch-all extension bag) so that future shape changes never break parse.
    /// </summary>
    public sealed class DecompManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int? SchemaVersion { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("builtRom")]
        public string BuiltRom { get; set; }

        [JsonPropertyName("forceVersion")]
        public string ForceVersion { get; set; }

        // ---- Reserved getters for #1130-#1134 (read off artifacts if present) ----
        [JsonPropertyName("elf")]
        public string ElfPath { get; set; }

        [JsonPropertyName("map")]
        public string MapPath { get; set; }

        [JsonPropertyName("sym")]
        public string SymPath { get; set; }

        [JsonPropertyName("build")]
        public string BuildCommand { get; set; }

        // ---- Reserved sections: tolerant, never break parse on shape change ----
        [JsonPropertyName("artifacts")]
        public JsonElement? Artifacts { get; set; }

        [JsonPropertyName("buildConfig")]
        public JsonElement? BuildConfig { get; set; }

        [JsonPropertyName("tables")]
        public JsonElement? Tables { get; set; }

        [JsonPropertyName("assets")]
        public JsonElement? Assets { get; set; }

        /// <summary>Catch-all for any future top-level key — keeps parse lossless.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; }

        /// <summary>
        /// True when at least one slice-1 "known" field is set, used by the
        /// short-circuit accept rule alongside an explicit schemaVersion==1.
        /// </summary>
        public bool HasAnyKnownField =>
            !string.IsNullOrEmpty(BuiltRom)
            || !string.IsNullOrEmpty(ForceVersion)
            || !string.IsNullOrEmpty(Mode)
            || !string.IsNullOrEmpty(Name);
    }

    /// <summary>
    /// Pure (no Drawing/WinForms) decomp-project detector + built-ROM resolver.
    /// Every public method is fully guarded and NEVER throws.
    /// </summary>
    public static class DecompProjectDetector
    {
        static readonly JsonSerializerOptions ManifestJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // Matches a leading-whitespace Makefile line that assigns ROM or BUILD_NAME
        // (with := or =). Key tokens are matched case-sensitively (decomp convention)
        // but a leading-whitespace tolerance is allowed.
        static readonly Regex MakefileRomLineRegex =
            new Regex(@"^\s*(ROM|BUILD_NAME)\s*:?=", RegexOptions.Multiline | RegexOptions.Compiled);

        // Captures the stem of `ROM := <stem>` / `BUILD_NAME := <stem>` (rest of line).
        static readonly Regex MakefileRomStemRegex =
            new Regex(@"^\s*(?:ROM|BUILD_NAME)\s*:?=\s*(\S+)", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Detect whether <paramref name="dir"/> is a decomp project.
        /// Returns a <see cref="DecompProject"/> on accept, or null. NEVER throws.
        /// Acceptance: explicit-manifest short-circuit OR heuristic score &gt;= 2.
        /// </summary>
        public static DecompProject Detect(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return null;

                string root = Path.GetFullPath(dir);

                // ---- Manifest short-circuit ----
                string manifestPath = Path.Combine(root, DecompProject.ManifestFileName);
                if (File.Exists(manifestPath))
                {
                    DecompManifest manifest = ParseManifest(manifestPath);
                    if (manifest != null)
                    {
                        // Amendment 1: an explicit but UNSUPPORTED schemaVersion
                        // (non-null && != 1) is a hard reject (don't silently treat
                        // as v1). null schemaVersion falls through to the field check.
                        if (manifest.SchemaVersion.HasValue
                            && manifest.SchemaVersion.Value != DecompProject.SupportedSchemaVersion)
                        {
                            return null;
                        }

                        bool accept =
                            manifest.SchemaVersion == DecompProject.SupportedSchemaVersion
                            || manifest.HasAnyKnownField;

                        if (accept)
                        {
                            return new DecompProject
                            {
                                ProjectRoot = root,
                                ForceVersion = string.IsNullOrEmpty(manifest.ForceVersion) ? null : manifest.ForceVersion,
                                Manifest = manifest,
                            };
                        }
                        // empty {} (no known field, no schemaVersion): do NOT short-circuit;
                        // fall through to heuristics.
                    }
                    // malformed manifest (ParseManifest returned null): ignore it, fall through.
                }

                // ---- Heuristic score ----
                int score = ScoreHeuristics(root);
                if (score >= 2)
                {
                    return new DecompProject
                    {
                        ProjectRoot = root,
                        ForceVersion = null,
                        Manifest = null,
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Weighted heuristic score. Pure, guarded, never throws.
        /// Accept threshold (in <see cref="Detect"/>) is &gt;= 2.
        /// </summary>
        static int ScoreHeuristics(string root)
        {
            int score = 0;

            try
            {
                // Makefile: ROM:= / BUILD_NAME:= → 2; agbcc-only Makefile → 1.
                string makefile = Path.Combine(root, "Makefile");
                if (File.Exists(makefile))
                {
                    string head = ReadHead(makefile, 200);
                    if (head != null)
                    {
                        if (MakefileRomLineRegex.IsMatch(head))
                            score += 2;                       // weight 2
                        else if (head.IndexOf("agbcc", StringComparison.OrdinalIgnoreCase) >= 0)
                            score += 1;                       // Amendment 2: weight 1
                    }
                }

                // *.sha1 or checksum.sha1 in root → 1.
                if (HasFileMatching(root, "*.sha1"))
                    score += 1;

                // ldscript.txt or any *.lds in root → 1.
                if (File.Exists(Path.Combine(root, "ldscript.txt")) || HasFileMatching(root, "*.lds"))
                    score += 1;

                // src/ AND (asm/ OR data/) → 1.
                if (Directory.Exists(Path.Combine(root, "src"))
                    && (Directory.Exists(Path.Combine(root, "asm")) || Directory.Exists(Path.Combine(root, "data"))))
                    score += 1;

                // tools/agbcc/ → 1.
                if (Directory.Exists(Path.Combine(root, "tools", "agbcc")))
                    score += 1;
            }
            catch
            {
                // never throw — return whatever we accumulated
            }

            return score;
        }

        /// <summary>
        /// Parse a manifest file with tolerant options. Returns null on any fault
        /// (missing file, malformed JSON). NEVER throws.
        /// </summary>
        public static DecompManifest ParseManifest(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return null;
                return JsonSerializer.Deserialize<DecompManifest>(json, ManifestJsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resolve the built ROM for a detected project. Typed result; NEVER throws.
        /// Returns NotProject when the dir is not a project at all.
        /// </summary>
        public static ResolvedRom ResolveBuiltRom(string dir, DecompProject project)
        {
            try
            {
                if (project == null)
                    return ResolvedRom.NotProject;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return ResolvedRom.NotProject;

                string root = Path.GetFullPath(dir);

                // 1. manifest builtRom (validated/normalized).
                string manifestRom = project.Manifest?.BuiltRom;
                if (!string.IsNullOrEmpty(manifestRom))
                {
                    string resolved = ResolveProjectRelative(root, manifestRom);
                    // Amendment 3: reject absolute or ..-escaping paths; fall through.
                    if (resolved != null && File.Exists(resolved))
                        return ResolvedRom.Ok(resolved);
                }

                // 2. Makefile ROM stem → <stem>.gba in root.
                string stem = ParseMakefileRomStem(root);
                if (!string.IsNullOrEmpty(stem))
                {
                    string candidate = Path.Combine(root, stem + ".gba");
                    if (File.Exists(candidate))
                        return ResolvedRom.Ok(Path.GetFullPath(candidate));
                }

                // 3. glob root *.gba with a same-stem *.elf sibling (Amendment 4).
                string globbed = ResolveBySiblingElf(root, stem);
                if (globbed != null)
                    return ResolvedRom.Ok(globbed);

                // 4. nothing built.
                return ResolvedRom.NotBuilt;
            }
            catch
            {
                return ResolvedRom.NotBuilt;
            }
        }

        /// <summary>
        /// Glob root *.gba files that each have a same-stem *.elf sibling. If multiple
        /// qualify, prefer the one whose stem equals the Makefile stem.
        /// </summary>
        static string ResolveBySiblingElf(string root, string makefileStem)
        {
            try
            {
                string[] gbas = Directory.GetFiles(root, "*.gba", SearchOption.TopDirectoryOnly);
                string preferred = null;
                string firstMatch = null;
                foreach (string gba in gbas)
                {
                    string s = Path.GetFileNameWithoutExtension(gba);
                    string elf = Path.Combine(root, s + ".elf");
                    if (!File.Exists(elf))
                        continue;   // Amendment 4: same-stem .elf is REQUIRED (stops baserom.gba)
                    string full = Path.GetFullPath(gba);
                    if (firstMatch == null) firstMatch = full;
                    if (!string.IsNullOrEmpty(makefileStem)
                        && string.Equals(s, makefileStem, StringComparison.OrdinalIgnoreCase))
                    {
                        preferred = full;
                    }
                }
                return preferred ?? firstMatch;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read the root Makefile head (~200 lines) and return the &lt;stem&gt; from
        /// <c>ROM := &lt;stem&gt;.gba</c> / <c>ROM = ...</c> / <c>BUILD_NAME := ...</c>,
        /// stripping a trailing <c>.gba</c> extension. Returns null if none. NEVER throws.
        /// </summary>
        public static string ParseMakefileRomStem(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return null;
                string makefile = Path.Combine(dir, "Makefile");
                if (!File.Exists(makefile)) return null;

                string head = ReadHead(makefile, 200);
                if (head == null) return null;

                Match m = MakefileRomStemRegex.Match(head);
                if (!m.Success) return null;

                string stem = m.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(stem)) return null;

                // Strip a trailing .gba if present.
                if (stem.EndsWith(".gba", StringComparison.OrdinalIgnoreCase))
                    stem = stem.Substring(0, stem.Length - 4);

                // Guard against make-variable references like $(NAME) — those
                // cannot resolve to a real file, so treat as "no stem".
                if (stem.IndexOf('$') >= 0)
                    return null;

                return string.IsNullOrEmpty(stem) ? null : stem;
            }
            catch
            {
                return null;
            }
        }

        // -------------------------------------------------------------- helpers

        /// <summary>
        /// Resolve a project-relative manifest path. Returns the absolute path when
        /// it stays under <paramref name="root"/>; null when the value is absolute
        /// or escapes the root via "..". (Amendment 3.)
        /// </summary>
        static string ResolveProjectRelative(string root, string relative)
        {
            try
            {
                if (string.IsNullOrEmpty(relative))
                    return null;
                // Reject absolute paths outright.
                if (Path.IsPathRooted(relative))
                    return null;

                string combined = Path.GetFullPath(Path.Combine(root, relative));
                string normRoot = Path.GetFullPath(root);

                // Ensure the resolved path stays under the project root.
                string withSep = normRoot.EndsWith(Path.DirectorySeparatorChar.ToString())
                    ? normRoot
                    : normRoot + Path.DirectorySeparatorChar;
                if (!combined.StartsWith(withSep, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(combined, normRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return null;   // escapes root via ..
                }

                return combined;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Read up to <paramref name="maxLines"/> lines of a text file. Null on fault.</summary>
        static string ReadHead(string path, int maxLines)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                using (var reader = new StreamReader(path))
                {
                    string line;
                    int n = 0;
                    while (n < maxLines && (line = reader.ReadLine()) != null)
                    {
                        sb.Append(line).Append('\n');
                        n++;
                    }
                }
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>True if the root directory has at least one file matching the glob.</summary>
        static bool HasFileMatching(string root, string pattern)
        {
            try
            {
                string[] hits = Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly);
                return hits != null && hits.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
