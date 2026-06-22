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

        /// <summary>Reserved: build args from the manifest (empty array if unset).</summary>
        public string[] BuildArgs => Manifest?.BuildArgs ?? Array.Empty<string>();

        /// <summary>Reserved: compare target from the manifest (null if unset).</summary>
        public string CompareTarget => Manifest?.CompareTarget;

        /// <summary>True when the project has opted into FEBuilder-managed builds.</summary>
        public bool IsBuildEnabled => Manifest?.BuildEnabled == true;

        /// <summary>True when a non-empty built ROM path is set and the file exists.</summary>
        public bool IsBuilt => !string.IsNullOrEmpty(BuiltRomPath) && File.Exists(BuiltRomPath);

        /// <summary>
        /// Set to true by a source-backed writer (#1132) after it rewrites a
        /// declared table-owner source file. Surfaces a "needs rebuild" hint in the
        /// host badge so the user re-runs the build to refresh the preview ROM.
        /// Default false; never throws to read/write (plain property).
        /// </summary>
        public bool NeedsRebuild { get; set; }

        /// <summary>
        /// Case-insensitive table-owner lookup over the manifest's <c>tables</c>
        /// section (#1132). Returns the matching <see cref="DecompTableEntry"/> whose
        /// <c>table</c> equals <paramref name="tableName"/>, or null when there is no
        /// owner declaration, no manifest, or the section is malformed. NEVER throws.
        ///
        /// #1141: <c>units</c> and <c>characters</c> are aliases — a lookup for one
        /// also matches an owner declared under the other (decomp trees commonly name
        /// the character/unit table either way). The direct name is tried first.
        /// </summary>
        public DecompTableEntry TryGetTableOwner(string tableName)
        {
            try
            {
                if (string.IsNullOrEmpty(tableName) || Manifest == null)
                    return null;

                DecompTableEntry direct = FindOwnerByExactName(tableName);
                if (direct != null)
                    return direct;

                // #1141: units <-> characters alias.
                if (string.Equals(tableName, "units", StringComparison.OrdinalIgnoreCase))
                    return FindOwnerByExactName("characters");
                if (string.Equals(tableName, "characters", StringComparison.OrdinalIgnoreCase))
                    return FindOwnerByExactName("units");

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Exact (case-insensitive) table-name match over the parsed owners. NEVER throws.</summary>
        DecompTableEntry FindOwnerByExactName(string tableName)
        {
            foreach (DecompTableEntry e in Manifest.TablesList)
            {
                if (e != null
                    && !string.IsNullOrEmpty(e.Table)
                    && string.Equals(e.Table, tableName, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }

        /// <summary>
        /// Case-insensitive variable-length LIST-owner lookup over the manifest's
        /// <c>tables</c> section (#1347). Returns the matching <see cref="DecompTableEntry"/>
        /// whose <see cref="DecompTableEntry.Format"/> is <c>"u16-list"</c> AND whose
        /// <see cref="DecompTableEntry.Table"/>, <see cref="DecompTableEntry.Symbol"/> OR
        /// <see cref="DecompTableEntry.ArrayName"/> equals <paramref name="symbolOrLabel"/>,
        /// or null when there is no manifest, no match, or any fault. NEVER throws.
        ///
        /// This is the list-owner analogue of <see cref="TryGetTableOwner"/>: it lets the
        /// shop-list writer find the owning declaration from either the FEBuilder table key
        /// or the resolved C array symbol.
        /// </summary>
        public DecompTableEntry TryGetListOwner(string symbolOrLabel)
        {
            try
            {
                if (string.IsNullOrEmpty(symbolOrLabel) || Manifest == null)
                    return null;

                foreach (DecompTableEntry e in Manifest.TablesList)
                {
                    if (e == null)
                        continue;
                    if (!string.Equals(e.Format, "u16-list", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (NameEquals(e.Table, symbolOrLabel)
                        || NameEquals(e.Symbol, symbolOrLabel)
                        || NameEquals(e.ArrayName, symbolOrLabel))
                        return e;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        static bool NameEquals(string candidate, string target)
            => !string.IsNullOrEmpty(candidate)
               && string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One table-owner declaration in the manifest's <c>tables</c> section (#1132).
    /// Declares which structured ROM table is source-backed, the source file that
    /// owns it, and (optionally) the C array symbol + field layout used by the
    /// source-backed writer. All fields tolerant/optional; the
    /// <see cref="Extra"/> bag keeps parse lossless for forward-compat.
    /// </summary>
    public sealed class DecompTableEntry
    {
        /// <summary>FEBuilder table name (e.g. "items"). Case-insensitive match key.</summary>
        [JsonPropertyName("table")]
        public string Table { get; set; }

        /// <summary>Source format: "cstruct" (C array) or "json". Default treated as "cstruct".</summary>
        [JsonPropertyName("format")]
        public string Format { get; set; }

        /// <summary>Write policy: "source" (rewrite source), "romOnly", or "manual".</summary>
        [JsonPropertyName("writePolicy")]
        public string WritePolicy { get; set; }

        /// <summary>Owner kind hint (free-form; not consumed by the MVP writer).</summary>
        [JsonPropertyName("ownerKind")]
        public string OwnerKind { get; set; }

        /// <summary>C array identifier holding the entries (alias of <see cref="Symbol"/>).</summary>
        [JsonPropertyName("arrayName")]
        public string ArrayName { get; set; }

        /// <summary>C symbol for the array (alias of <see cref="ArrayName"/>).</summary>
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }

        /// <summary>Project-relative path to the source file that owns this table.</summary>
        [JsonPropertyName("sourceFile")]
        public string SourceFile { get; set; }

        /// <summary>C element type name (informational).</summary>
        [JsonPropertyName("ctype")]
        public string CType { get; set; }

        /// <summary>JSON schema id when <see cref="Format"/> is "json" (informational).</summary>
        [JsonPropertyName("jsonSchema")]
        public string JsonSchema { get; set; }

        /// <summary>Declared entry count (informational; null when unspecified).</summary>
        [JsonPropertyName("count")]
        public int? Count { get; set; }

        /// <summary>Declared entry byte size (informational; null when unspecified).</summary>
        [JsonPropertyName("entrySize")]
        public int? EntrySize { get; set; }

        /// <summary>Index base for entry ids (0 or 1; null defaults to 0 at the writer).</summary>
        [JsonPropertyName("indexBase")]
        public int? IndexBase { get; set; }

        /// <summary>
        /// Sentinel value that terminates a variable-length list owner (#1347;
        /// <c>format == "u16-list"</c>). Null defaults to <c>0x0000</c> (ITEM_NONE) at the
        /// writer. Informational for the v1 shop-list writer (which always emits 0x0000).
        /// </summary>
        [JsonPropertyName("terminator")]
        public int? Terminator { get; set; }

        /// <summary>
        /// Byte width of each list element (#1347; <c>format == "u16-list"</c>). Null defaults
        /// to 2 (u16) at the writer; the v1 writer supports ONLY width 2 (any other value is
        /// refused as manual).
        /// </summary>
        [JsonPropertyName("elementWidth")]
        public int? ElementWidth { get; set; }

        /// <summary>Declared C fields, in struct order (drives positional initializers).</summary>
        [JsonPropertyName("fields")]
        public List<DecompTableField> Fields { get; set; }

        /// <summary>
        /// Optional project-relative path to the constants header that defines the
        /// <c>ITEM_*</c> macros for a symbolic <c>u16-list</c> shop owner (#1354;
        /// typically <c>include/constants/items.h</c>). When set, the shop-list source
        /// writer parses it to serialize SYMBOLIC item-id-only lists (e.g.
        /// <c>{ ITEM_SWORD_IRON, ITEM_NONE, }</c>). An EXPLICIT path that is absolute /
        /// escapes the project root / missing / unparseable makes the constants resolver
        /// <see cref="DecompConstantResolver.IsUnavailable"/> (the conventional default is
        /// NOT used as a fallback). Optional/tolerant; absent ⇒ the manifest top-level
        /// <c>artifacts.itemConstants</c> then the default
        /// <c>include/constants/items.h</c> are tried.
        /// </summary>
        [JsonPropertyName("constantsHeader")]
        public string ConstantsHeader { get; set; }

        /// <summary>Effective array symbol: <see cref="ArrayName"/> when set, else <see cref="Symbol"/>.</summary>
        [JsonIgnore]
        public string EffectiveSymbol =>
            !string.IsNullOrEmpty(ArrayName) ? ArrayName : Symbol;

        /// <summary>Catch-all so unknown table keys never break parse.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; }
    }

    /// <summary>
    /// One C field declaration inside a <see cref="DecompTableEntry"/> (#1132).
    /// <see cref="Name"/> matches the FEBuilder/manifest field key the writer is
    /// asked to change; the optional offset/width/signed hints + the
    /// <see cref="Extra"/> bag (enum/constant maps) are tolerant and forward-compat.
    /// </summary>
    public sealed class DecompTableField
    {
        /// <summary>C struct field name (and the change-set key the writer matches).</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>Byte offset of the field (informational; null when unspecified).</summary>
        [JsonPropertyName("offset")]
        public int? Offset { get; set; }

        /// <summary>Field byte width (informational; null when unspecified).</summary>
        [JsonPropertyName("width")]
        public int? Width { get; set; }

        /// <summary>True when the field is signed (informational; null when unspecified).</summary>
        [JsonPropertyName("signed")]
        public bool? Signed { get; set; }

        /// <summary>Catch-all (e.g. enum/constants maps) so unknown keys never break parse.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; }
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

        // ---- Reserved artifact paths for #1130-#1134. Typed as tolerant
        //      JsonElement? (NOT string) so a future object/array shape — e.g.
        //      `"build": { "command": "make" }` — never throws during parse and
        //      rejects an otherwise-valid manifest-only project. Scalar values are
        //      surfaced via the string getters below. ----
        [JsonPropertyName("elf")]
        public JsonElement? Elf { get; set; }

        [JsonPropertyName("map")]
        public JsonElement? Map { get; set; }

        [JsonPropertyName("sym")]
        public JsonElement? Sym { get; set; }

        [JsonPropertyName("build")]
        public JsonElement? Build { get; set; }

        /// <summary>ELF path when the reserved <c>elf</c> value is a plain string, else null.</summary>
        [JsonIgnore]
        public string ElfPath => AsString(Elf);

        /// <summary>.map path when the reserved <c>map</c> value is a plain string, else null.</summary>
        [JsonIgnore]
        public string MapPath => AsString(Map);

        /// <summary>.sym path when the reserved <c>sym</c> value is a plain string, else null.</summary>
        [JsonIgnore]
        public string SymPath => AsString(Sym);

        /// <summary>
        /// Build command: the <c>build</c> value when it is a plain string, or its
        /// <c>command</c> property when <c>build</c> is an object; else null.
        /// </summary>
        [JsonIgnore]
        public string BuildCommand
        {
            get
            {
                if (Build is JsonElement b)
                {
                    if (b.ValueKind == JsonValueKind.String)
                        return b.GetString();
                    if (b.ValueKind == JsonValueKind.Object
                        && b.TryGetProperty("command", out var cmd)
                        && cmd.ValueKind == JsonValueKind.String)
                        return cmd.GetString();
                }
                return null;
            }
        }

        /// <summary>
        /// Build args: the <c>args</c> array when <c>build</c> is an object with an
        /// <c>args</c> property that is an array of strings; else empty. Never throws.
        /// </summary>
        [JsonIgnore]
        public string[] BuildArgs
        {
            get
            {
                try
                {
                    if (Build is JsonElement b && b.ValueKind == JsonValueKind.Object
                        && b.TryGetProperty("args", out var argsEl)
                        && argsEl.ValueKind == JsonValueKind.Array)
                    {
                        var list = new List<string>();
                        foreach (var item in argsEl.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                                list.Add(item.GetString() ?? "");
                        }
                        return list.ToArray();
                    }
                }
                catch { }
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Compare target: the <c>compareTarget</c> string when <c>build</c> is an object
        /// with that property; else null. Never throws.
        /// </summary>
        [JsonIgnore]
        public string CompareTarget
        {
            get
            {
                try
                {
                    if (Build is JsonElement b && b.ValueKind == JsonValueKind.Object
                        && b.TryGetProperty("compareTarget", out var ct)
                        && ct.ValueKind == JsonValueKind.String)
                        return ct.GetString();
                }
                catch { }
                return null;
            }
        }

        /// <summary>
        /// True when the project opts into FEBuilder-managed builds. Build must be non-null
        /// AND (a non-empty string, an object [even empty {}], or has enabled:true/command/args).
        /// A null build section → false. Never throws.
        /// </summary>
        [JsonIgnore]
        public bool BuildEnabled
        {
            get
            {
                try
                {
                    if (!(Build is JsonElement b))
                        return false;
                    if (b.ValueKind == JsonValueKind.String)
                        return !string.IsNullOrEmpty(b.GetString());
                    if (b.ValueKind == JsonValueKind.Object)
                    {
                        // An explicit "enabled": false is a deliberate opt-OUT even
                        // when other build keys are present (security: honor the
                        // user's explicit disable). Otherwise any object — even an
                        // empty {} — counts as opt-in.
                        if (b.TryGetProperty("enabled", out var en)
                            && en.ValueKind == JsonValueKind.False)
                            return false;
                        return true;
                    }
                }
                catch { }
                return false;
            }
        }

        /// <summary>Return a JsonElement's string value, or null for any non-string/absent value.</summary>
        static string AsString(JsonElement? e)
            => (e is JsonElement je && je.ValueKind == JsonValueKind.String) ? je.GetString() : null;

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

        // Cached parse of the tolerant Tables JsonElement (#1132). Lazily built on
        // first access; null until parsed, then a (possibly empty) list.
        [JsonIgnore]
        List<DecompTableEntry> _tablesList;

        /// <summary>
        /// Parsed view of the tolerant <see cref="Tables"/> JsonElement (#1132).
        /// Accepts BOTH a JSON array (<c>[ { "table": ... }, ... ]</c>) AND an
        /// object-map (<c>{ "items": { ... } }</c> — the key is injected as
        /// <c>table</c> when the entry object lacks one). Malformed / absent →
        /// an empty list. NEVER throws. The parse is cached on first access.
        /// </summary>
        [JsonIgnore]
        public List<DecompTableEntry> TablesList
        {
            get
            {
                if (_tablesList != null)
                    return _tablesList;
                _tablesList = ParseTables(Tables);
                return _tablesList;
            }
        }

        static readonly JsonSerializerOptions TablesJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Tolerant parse of the <c>tables</c> element into a list of owners.
        /// Array → each element deserialized. Object-map → each value
        /// deserialized with its key injected as <c>table</c> when the value
        /// object omits one. Any per-entry fault is skipped (not fatal). NEVER throws.
        /// </summary>
        static List<DecompTableEntry> ParseTables(JsonElement? tables)
        {
            var list = new List<DecompTableEntry>();
            try
            {
                if (!(tables is JsonElement el))
                    return list;

                if (el.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in el.EnumerateArray())
                    {
                        DecompTableEntry entry = TryDeserializeEntry(item, null);
                        if (entry != null)
                            list.Add(entry);
                    }
                }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty prop in el.EnumerateObject())
                    {
                        DecompTableEntry entry = TryDeserializeEntry(prop.Value, prop.Name);
                        if (entry != null)
                            list.Add(entry);
                    }
                }
            }
            catch
            {
                // tolerant: return whatever was accumulated (never throw)
            }
            return list;
        }

        /// <summary>
        /// Deserialize one table-owner object. When <paramref name="injectKey"/> is
        /// non-null (object-map form) and the deserialized entry has no
        /// <c>table</c>, the map key is injected. Returns null on any per-entry fault.
        /// </summary>
        static DecompTableEntry TryDeserializeEntry(JsonElement item, string injectKey)
        {
            try
            {
                if (item.ValueKind != JsonValueKind.Object)
                    return null;
                DecompTableEntry entry = item.Deserialize<DecompTableEntry>(TablesJsonOptions);
                if (entry == null)
                    return null;
                if (string.IsNullOrEmpty(entry.Table) && !string.IsNullOrEmpty(injectKey))
                    entry.Table = injectKey;
                return entry;
            }
            catch
            {
                return null;
            }
        }

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

                // 2. Makefile ROM stem → <stem>.gba in root. Route through the same
                //    project-root containment check as the manifest builtRom path so a
                //    rooted (`/tmp/out`, `C:\out`) or `..`-escaping stem can never load
                //    a ROM outside the selected project directory (Amendment 3 parity).
                string stem = ParseMakefileRomStem(root);
                if (!string.IsNullOrEmpty(stem))
                {
                    string candidate = ResolveProjectRelative(root, stem + ".gba");
                    if (candidate != null && File.Exists(candidate))
                        return ResolvedRom.Ok(candidate);
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

                // A decomp ROM stem is always project-relative. Reject a rooted
                // stem (e.g. `/tmp/out` or `C:\out`) so that the caller's
                // `Path.Combine(root, stem + ".gba")` cannot silently escape the
                // project root (the manifest builtRom containment rule, Amendment 3,
                // would otherwise be bypassed via the Makefile-stem path).
                if (Path.IsPathRooted(stem))
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
        /// Resolve a project-relative artifact path (.map/.elf/.sym/.json) under
        /// <paramref name="root"/>, reusing the same containment rule as the
        /// manifest builtRom path (rejects absolute / <c>..</c>-escaping values).
        /// Returns the absolute path, or null on reject / fault. NEVER throws.
        /// Used by <see cref="DecompSymbolResolver"/> (#1130).
        /// </summary>
        internal static string ResolveArtifact(string root, string relative)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(relative))
                    return null;
                return ResolveProjectRelative(Path.GetFullPath(root), relative);
            }
            catch
            {
                return null;
            }
        }

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

                // Ensure the resolved path stays under the project root. Use the
                // filesystem's own case sensitivity: ignore-case on Windows, but
                // case-SENSITIVE on Linux/macOS so a different-cased sibling dir can't
                // be mistaken for "inside" the project root (Copilot PR #1136 finding).
                StringComparison cmp = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                string withSep = normRoot.EndsWith(Path.DirectorySeparatorChar.ToString())
                    ? normRoot
                    : normRoot + Path.DirectorySeparatorChar;
                if (!combined.StartsWith(withSep, cmp)
                    && !string.Equals(combined, normRoot, cmp))
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
