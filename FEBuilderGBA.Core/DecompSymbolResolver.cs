// SPDX-License-Identifier: GPL-3.0-or-later
// Decomp-project address-to-source symbol resolver (#1130 slice 2).
//
// Discovers and loads a decomp project's symbol artifacts (.map -> ELF -> .sym ->
// JSON, in that precedence) into a single GBA-pointer-keyed AsmMapSt dictionary,
// remembering each symbol's source, and layers them OVER the shipped asmmap
// symbol table at the CoreAsmMapCache.GetAsmMapFile() chokepoint via
// MergedAsmMapFile (project symbols WIN at the same address).
//
// READ-ONLY and NEVER throws: discovery, every parser, and the CommentCache feed
// are fully guarded. The shipped asmmap path is completely untouched in classic
// (non-decomp) mode.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FEBuilderGBA
{
    /// <summary>Which artifact defined a resolved symbol.</summary>
    public enum DecompArtifactSource
    {
        None = 0,
        Map = 1,
        Elf = 2,
        Sym = 3,
        Json = 4,
    }

    /// <summary>
    /// Loaded, GBA-pointer-keyed decomp symbol table for one project. Built by
    /// <see cref="Load"/>; consumed by <see cref="MergedAsmMapFile"/>.
    /// </summary>
    public sealed class DecompSymbolResolver
    {
        readonly Dictionary<uint, AsmMapSt> _symbols = new Dictionary<uint, AsmMapSt>();
        readonly Dictionary<uint, DecompArtifactSource> _sources = new Dictionary<uint, DecompArtifactSource>();
        // Owning section name + best-effort object/source path per resolved pointer
        // (kept in parallel like _sources). READ-ONLY hints for the #1131
        // diff-to-source migration assistant's SourceFile suggestion. Only entries
        // whose source artifact carried a non-empty value are stored — never
        // fabricated. Consumed via TryGetSection / TryGetObjectPath.
        readonly Dictionary<uint, string> _sections = new Dictionary<uint, string>();
        readonly Dictionary<uint, string> _objectPaths = new Dictionary<uint, string>();
        // Per-source loaded counts (for the CLI rom-info breakdown).
        public int CountMap { get; private set; }
        public int CountElf { get; private set; }
        public int CountSym { get; private set; }
        public int CountJson { get; private set; }

        /// <summary>GBA-pointer-keyed loaded symbols (project precedence applied).</summary>
        public IReadOnlyDictionary<uint, AsmMapSt> Symbols => _symbols;

        /// <summary>Total loaded symbol count.</summary>
        public int Count => _symbols.Count;

        /// <summary>Smallest symbol pointer, or <see cref="U.NOT_FOUND"/> when empty.</summary>
        public uint MinKey
        {
            get
            {
                uint min = U.NOT_FOUND;
                foreach (uint k in _symbols.Keys)
                    if (k < min) min = k;
                return min;
            }
        }

        /// <summary>Source artifact of a resolved pointer; false when not a project symbol.</summary>
        public bool TryGetSource(uint pointer, out DecompArtifactSource src)
        {
            return _sources.TryGetValue(pointer, out src);
        }

        /// <summary>
        /// Owning section name of a resolved pointer (e.g. <c>.rodata</c>), when the
        /// source artifact recorded one. Returns false (empty) otherwise. READ-ONLY
        /// hint for the #1131 migration assistant; never fabricated.
        /// </summary>
        public bool TryGetSection(uint pointer, out string section)
        {
            return _sections.TryGetValue(pointer, out section);
        }

        /// <summary>
        /// Best-effort object/source path of a resolved pointer (e.g.
        /// <c>build/src/unit.o</c>), when the linker map recorded one. Returns false
        /// (empty) otherwise. READ-ONLY hint for the #1131 migration assistant;
        /// never fabricated.
        /// </summary>
        public bool TryGetObjectPath(uint pointer, out string objectPath)
        {
            return _objectPaths.TryGetValue(pointer, out objectPath);
        }

        DecompSymbolResolver() { }

        /// <summary>
        /// Discover + load a project's symbol artifacts in the precedence
        /// .map -> ELF -> .sym -> JSON. The first source to define a normalized
        /// address WINS (so .map is primary). NEVER throws — any file / parse
        /// fault simply skips that source. Returns a non-null resolver always.
        /// </summary>
        public static DecompSymbolResolver Load(DecompProject project)
        {
            var resolver = new DecompSymbolResolver();
            try
            {
                if (project == null || string.IsNullOrEmpty(project.ProjectRoot))
                    return resolver;

                string root = project.ProjectRoot;
                string stem = "";
                try { stem = Path.GetFileNameWithoutExtension(project.BuiltRomPath ?? ""); }
                catch { stem = ""; }

                string mapPath  = DiscoverArtifact(project, root, stem, "map", project.MapPath, ".map");
                string elfPath  = DiscoverArtifact(project, root, stem, "elf", project.ElfPath, ".elf");
                string symPath  = DiscoverArtifact(project, root, stem, "sym", project.SymPath, ".sym", new[] { "sym_jp.txt" });
                string jsonPath = DiscoverArtifact(project, root, stem, "symbolJson", null, ".sym.json");

                // .map (primary)
                if (!string.IsNullOrEmpty(mapPath) && File.Exists(mapPath))
                {
                    var syms = SafeParse(() => DecompMapParser.Parse(SafeReadAllText(mapPath)));
                    resolver.CountMap = resolver.Ingest(syms, DecompArtifactSource.Map);
                }

                // ELF
                if (!string.IsNullOrEmpty(elfPath) && File.Exists(elfPath))
                {
                    var syms = SafeParse(() => LoadElf(elfPath));
                    resolver.CountElf = resolver.Ingest(syms, DecompArtifactSource.Elf);
                }

                // .sym (no$gba)
                if (!string.IsNullOrEmpty(symPath) && File.Exists(symPath))
                {
                    var syms = SafeParse(() => DecompSymParser.Parse(SafeReadAllText(symPath)));
                    resolver.CountSym = resolver.Ingest(syms, DecompArtifactSource.Sym);
                }

                // JSON
                if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
                {
                    var syms = SafeParse(() => DecompSymbolJsonParser.Parse(SafeReadAllText(jsonPath)));
                    resolver.CountJson = resolver.Ingest(syms, DecompArtifactSource.Json);
                }
            }
            catch
            {
                // never throw — return whatever loaded
            }
            return resolver;
        }

        // Insert symbols under the precedence rule (first source to define a
        // normalized key wins). Returns how many of these symbols were NEW.
        int Ingest(List<DecompSymbol> syms, DecompArtifactSource source)
        {
            int added = 0;
            if (syms == null) return 0;
            foreach (DecompSymbol s in syms)
            {
                if (s == null || string.IsNullOrEmpty(s.Name)) continue;
                uint key = U.toPointer(DisassemblerTrumb.ProgramAddrToPlain(s.Addr));
                if (_symbols.ContainsKey(key)) continue;     // earlier source wins
                _symbols[key] = new AsmMapSt { Name = s.Name, Length = s.Size };
                _sources[key] = source;
                // Keep section / object-path hints only when non-empty (never
                // fabricate). Parallel to _sources; first-source-wins is already
                // guaranteed by the _symbols.ContainsKey guard above.
                if (!string.IsNullOrEmpty(s.Section))
                    _sections[key] = s.Section;
                if (!string.IsNullOrEmpty(s.ObjectPath))
                    _objectPaths[key] = s.ObjectPath;
                added++;
            }
            return added;
        }

        // Resolve an artifact path: manifest artifacts.{key} -> top-level getter ->
        // auto-discover <stem><ext> in root. project-relative + containment guarded.
        static string DiscoverArtifact(
            DecompProject project, string root, string stem,
            string manifestKey, string topLevelValue, string ext,
            string[] knownNames = null)
        {
            try
            {
                // 1. manifest "artifacts": { "<key>": "<relpath>" }
                string fromArtifacts = ReadArtifactsString(project, manifestKey);
                if (!string.IsNullOrEmpty(fromArtifacts))
                {
                    string r = DecompProjectDetector.ResolveArtifact(root, fromArtifacts);
                    if (r != null) return r;
                }

                // 2. top-level manifest getter (map/elf/sym).
                if (!string.IsNullOrEmpty(topLevelValue))
                {
                    string r = DecompProjectDetector.ResolveArtifact(root, topLevelValue);
                    if (r != null) return r;
                }

                // 3. auto-discover <stem><ext> in the project root (must actually exist,
                //    else fall through to the known-name fallbacks below).
                if (!string.IsNullOrEmpty(stem))
                {
                    string r = DecompProjectDetector.ResolveArtifact(root, stem + ext);
                    if (r != null && File.Exists(r)) return r;
                }

                // 4. well-known artifact filenames not derived from the built-ROM stem
                //    (e.g. the FE8J JP symbol table sym_jp.txt, #1773).
                if (knownNames != null)
                {
                    foreach (string kn in knownNames)
                    {
                        if (string.IsNullOrEmpty(kn)) continue;
                        string r = DecompProjectDetector.ResolveArtifact(root, kn);
                        if (r != null && File.Exists(r)) return r;
                    }
                }
            }
            catch { }
            return null;
        }

        // Read manifest.Artifacts.<key> as a string when present, else null.
        static string ReadArtifactsString(DecompProject project, string key)
        {
            try
            {
                if (project?.Manifest?.Artifacts is JsonElement art
                    && art.ValueKind == JsonValueKind.Object
                    && art.TryGetProperty(key, out var v)
                    && v.ValueKind == JsonValueKind.String)
                {
                    return v.GetString();
                }
            }
            catch { }
            return null;
        }

        static List<DecompSymbol> LoadElf(string path)
        {
            var result = new List<DecompSymbol>();
            try
            {
                var elf = new Elf(path, useHookMode: false, decompMode: true);
                foreach (Elf.Sym s in elf.SymList)
                {
                    if (s == null || string.IsNullOrEmpty(s.name)) continue;
                    result.Add(new DecompSymbol { Name = s.name, Addr = s.addr, Size = s.length, Section = "" });
                }
            }
            catch
            {
                return result;
            }
            return result;
        }

        static string SafeReadAllText(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return null; }
        }

        static List<DecompSymbol> SafeParse(Func<List<DecompSymbol>> f)
        {
            try { return f() ?? new List<DecompSymbol>(); }
            catch { return new List<DecompSymbol>(); }
        }

        /// <summary>
        /// Feed ROM-space project symbols into the shared CommentCache so the
        /// disassembler / pointer tools surface decomp names. GUARDED: no-op unless
        /// the resolver's symbols belong to the currently active ROM
        /// (<paramref name="rom"/> == <see cref="CoreState.ROM"/>) and a CommentCache
        /// is wired. RAM-only (EWRAM / IWRAM) symbols are SKIPPED — only ROM-space
        /// (0x08000000..0x09FFFFFF) addresses get a comment. NEVER throws.
        /// </summary>
        public void RegisterToCommentCache(ROM rom)
        {
            try
            {
                if (rom == null) return;
                if (!ReferenceEquals(CoreState.ROM, rom)) return;
                if (CoreState.CommentCache == null) return;

                var list = new List<Address>();
                foreach (var pair in _symbols)
                {
                    uint key = pair.Key;
                    if (key < 0x08000000 || key >= 0x0A000000) continue;  // ROM-space only
                    AsmMapSt st = pair.Value;
                    if (st == null || string.IsNullOrEmpty(st.Name)) continue;
                    try
                    {
                        Address.AddCommentData(list, key, st.Name + "@decomp");
                    }
                    catch { /* per-symbol guard */ }
                }

                foreach (Address a in list)
                {
                    try { CoreState.CommentCache.Update(a.Addr, a.Info); }
                    catch { /* per-entry guard */ }
                }
            }
            catch
            {
                // never throw
            }
        }
    }

    /// <summary>
    /// An <see cref="IAsmMapFile"/> that overlays a decomp project's symbol table
    /// on top of the shipped asmmap symbol table. PROJECT WINS at the same address.
    /// READ-ONLY; every method is null-/fault-guarded and never throws.
    /// </summary>
    public sealed class MergedAsmMapFile : IAsmMapFile
    {
        readonly IAsmMapFile _shipped;
        readonly DecompSymbolResolver _project;
        readonly List<uint> _projectKeys;   // ascending, for SearchNear

        public MergedAsmMapFile(IAsmMapFile shipped, DecompSymbolResolver project)
        {
            _shipped = shipped;
            _project = project;
            _projectKeys = new List<uint>();
            if (project?.Symbols != null)
            {
                _projectKeys.AddRange(project.Symbols.Keys);
                _projectKeys.Sort();
            }
        }

        /// <summary>Project dict first, else shipped. Project wins at the same addr.</summary>
        public bool TryGetValue(uint pointer, out AsmMapSt out_p)
        {
            if (_project?.Symbols != null && _project.Symbols.TryGetValue(pointer, out out_p))
                return true;
            if (_shipped != null)
                return _shipped.TryGetValue(pointer, out out_p);
            out_p = null;
            return false;
        }

        /// <summary>Project name first, else shipped.</summary>
        public string GetName(uint pointer)
        {
            uint plain = U.toPointer(DisassemblerTrumb.ProgramAddrToPlain(pointer));
            if (_project?.Symbols != null && _project.Symbols.TryGetValue(plain, out var p) && p != null
                && !string.IsNullOrEmpty(p.Name))
                return p.Name;
            return _shipped != null ? _shipped.GetName(pointer) : "";
        }

        /// <summary>Project name match first, else shipped.</summary>
        public uint SearchName(string name)
        {
            if (string.IsNullOrEmpty(name)) return U.NOT_FOUND;
            if (_project?.Symbols != null)
            {
                foreach (var pair in _project.Symbols)
                {
                    if (pair.Value != null && pair.Value.Name == name)
                        return pair.Key;
                }
            }
            return _shipped != null ? _shipped.SearchName(name) : U.NOT_FOUND;
        }

        /// <summary>
        /// Span-covering nearest-symbol walk over BOTH tables (amendment 4). The
        /// shipped table is not key-enumerable through the interface, so we delegate
        /// its own <see cref="IAsmMapFile.SearchNear"/> for the shipped candidate
        /// and compute the project candidate from the enumerable project dict.
        ///
        /// Selection: prefer the candidate whose symbol SPAN (key + Length) actually
        /// covers <paramref name="pointer"/>; if both cover, prefer the HIGHER
        /// (nearer) base; if neither covers, return the higher of the two
        /// nearest-at/below keys; if only one is valid, return it. This guarantees a
        /// zero-length project key can NOT suppress a covering shipped span.
        /// Returns <see cref="U.NOT_FOUND"/> only when both are NOT_FOUND.
        /// </summary>
        public uint SearchNear(uint pointer)
        {
            uint shippedNear = _shipped != null ? _shipped.SearchNear(pointer) : U.NOT_FOUND;
            uint projectNear = ProjectSearchNear(pointer);

            bool hasShipped = shippedNear != U.NOT_FOUND;
            bool hasProject = projectNear != U.NOT_FOUND;

            if (!hasShipped && !hasProject) return U.NOT_FOUND;
            if (hasShipped && !hasProject) return shippedNear;
            if (!hasShipped && hasProject) return projectNear;

            bool shippedCovers = Covers(shippedNear, pointer, fromProject: false);
            bool projectCovers = Covers(projectNear, pointer, fromProject: true);

            if (shippedCovers && !projectCovers) return shippedNear;
            if (projectCovers && !shippedCovers) return projectNear;

            // Both cover, or neither covers: prefer the higher (nearer) base.
            return shippedNear >= projectNear ? shippedNear : projectNear;
        }

        // Project-side span-covering nearest key (Copilot PR #1138). Walk the
        // ascending project keys at/below `pointer`, tracking BOTH the nearest
        // at/below key AND the highest-base key whose SPAN (key + Length) actually
        // covers `pointer`. A covering key wins so a later zero-length project point
        // can't mask an earlier sized project symbol that still covers the address;
        // when none covers, fall back to the nearest at/below key. NOT_FOUND when
        // `pointer` is below every project key.
        uint ProjectSearchNear(uint pointer)
        {
            uint nearest = U.NOT_FOUND;
            uint covering = U.NOT_FOUND;
            // _projectKeys is ascending; linear is fine (project tables are small).
            for (int i = 0; i < _projectKeys.Count; i++)
            {
                uint k = _projectKeys[i];
                if (k > pointer) break;
                nearest = k;
                if (_project?.Symbols != null
                    && _project.Symbols.TryGetValue(k, out var st)
                    && st != null)
                {
                    ulong span = (ulong)k + st.Length;
                    if (pointer < span)
                        covering = k;   // highest base that covers wins
                }
            }
            return covering != U.NOT_FOUND ? covering : nearest;
        }

        // Does the symbol at `key` (in the given table) span-cover `pointer`?
        bool Covers(uint key, uint pointer, bool fromProject)
        {
            if (key == U.NOT_FOUND) return false;
            AsmMapSt st = null;
            if (fromProject)
                _project?.Symbols?.TryGetValue(key, out st);
            else
                _shipped?.TryGetValue(key, out st);
            if (st == null) return false;
            ulong span = (ulong)key + st.Length;
            return pointer >= key && pointer < span;
        }

        /// <summary>Smallest key for the screenshot seed: project min, else shipped's.</summary>
        public uint FirstKeyForScreenshot()
        {
            uint projMin = _project?.MinKey ?? U.NOT_FOUND;
            if (projMin != U.NOT_FOUND) return projMin;
            if (_shipped is AsmMapSymbolFile sym) return sym.FirstKeyForScreenshot();
            return U.NOT_FOUND;
        }

        /// <summary>Source artifact of a resolved pointer (project symbols only).</summary>
        public bool TryGetSource(uint pointer, out DecompArtifactSource src)
        {
            if (_project != null) return _project.TryGetSource(pointer, out src);
            src = DecompArtifactSource.None;
            return false;
        }

        /// <summary>Owning section of a resolved pointer (project symbols only). #1131.</summary>
        public bool TryGetSection(uint pointer, out string section)
        {
            if (_project != null) return _project.TryGetSection(pointer, out section);
            section = "";
            return false;
        }

        /// <summary>Object/source path of a resolved pointer (project symbols only). #1131.</summary>
        public bool TryGetObjectPath(uint pointer, out string objectPath)
        {
            if (_project != null) return _project.TryGetObjectPath(pointer, out objectPath);
            objectPath = "";
            return false;
        }
    }
}
