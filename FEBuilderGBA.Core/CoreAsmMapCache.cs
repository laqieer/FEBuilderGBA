// SPDX-License-Identifier: GPL-3.0-or-later
// Production IAsmMapCache for headless / cross-platform hosts (CLI + Avalonia),
// replacing the no-op HeadlessAsmMapCache for hardcode detection (#1035).
//
// Backs the per-unit / per-class / per-item "[HardCoding]" warning hyperlink in
// the Unit / Class / Item editors with the patch-scan detector
// PatchHardCodeScanner.ScanHardCodes. It does NOT (yet) cover the full WinForms
// ASM/MAP symbol pipeline (GetAsmMapFile / SearchNear = #1026); only patch-scan
// hardcode flags are populated.
//
// Lazy invalidation contract (intentional, matches WinForms cost profile):
//   - ClearCache() only marks the cache dirty. It does NOT rescan. Undo.Push /
//     rollback call ClearCache; FE8U ships ~1900 patch files, so eager rescans on
//     every undo would stutter the UI. The rescan happens lazily on the next
//     IsHardCode* read.
//   - The first IsHardCode* read after a dirty mark runs ScanHardCodes once under
//     the lock, clears the dirty flag, then answers from the arrays.
using System;

namespace FEBuilderGBA
{
    public class CoreAsmMapCache : IAsmMapCache
    {
        readonly object _lock = new object();
        readonly bool[] _unit = new bool[256];
        readonly bool[] _class = new bool[256];
        readonly bool[] _item = new bool[256];
        bool _dirty = true;

        // ---- ASM/MAP symbol table (#1026) ----
        // Built lazily + SEPARATELY from the hardcode arrays under its own dirty
        // flag, so a hardcode rescan never pays the symbol-parse cost and vice
        // versa. Backs IAsmMapCache.GetAsmMapFile() for the Pointer Tool
        // "What is this address?" lookup.
        AsmMapSymbolFile _symbolMap;
        bool _symbolDirty = true;

        // ---- Decomp project symbol overlay (#1130) ----
        // Lazily built once per (dirty) cycle when IsDecompMode. Layered OVER the
        // shipped _symbolMap via MergedAsmMapFile so project .map/ELF/.sym/JSON
        // symbols win at the same address. Null + dirty in classic mode (no overlay).
        DecompSymbolResolver _projectResolver;
        bool _projectDirty = true;
        // Cached merged wrapper (Copilot PR #1138): MergedAsmMapFile copies+sorts all
        // project keys at construction, so rebuilding it on every GetAsmMapFile() call
        // is wasteful. Built once per dirty cycle alongside _projectResolver; nulled
        // in ClearCache() / rebuilt when _projectDirty.
        MergedAsmMapFile _mergedMap;

        /// <summary>
        /// The ROM this cache scans. Captured at construction so a cache wired for
        /// a previous ROM never scans a later one (each ROM load creates a new
        /// cache).
        /// </summary>
        readonly ROM _rom;

        public CoreAsmMapCache(ROM rom)
        {
            _rom = rom;
        }

        /// <summary>Convenience ctor — captures the currently active ROM.</summary>
        public CoreAsmMapCache() : this(CoreState.ROM) { }

        /// <summary>
        /// Mark the cache dirty so the next IsHardCode* read rescans. Does NOT
        /// scan eagerly — see the lazy-invalidation contract above.
        /// </summary>
        public void ClearCache()
        {
            lock (_lock)
            {
                _dirty = true;
                _symbolDirty = true;
                _projectDirty = true;
                _mergedMap = null;
            }
        }

        /// <summary>
        /// Return the lazily-built ASM/MAP symbol table (#1026). The symbol map
        /// is parsed once on the first call after a dirty mark, under the lock,
        /// SEPARATELY from the hardcode arrays (it never touches
        /// <see cref="EnsureScannedNoLock"/>). On a parse fault a non-null EMPTY
        /// map is cached so callers (the Pointer Tool) never re-throw and simply
        /// resolve no symbol.
        /// </summary>
        public IAsmMapFile GetAsmMapFile()
        {
            lock (_lock)
            {
                bool symbolMapRebuilt = false;
                if (_symbolDirty || _symbolMap == null)
                {
                    try
                    {
                        _symbolMap = new AsmMapSymbolFile(_rom);
                    }
                    catch
                    {
                        // AsmMapSymbolFile's ctor is documented never-throw, but
                        // be defensive: leave a non-null empty map so the next
                        // call doesn't keep re-parsing / re-throwing.
                        _symbolMap = new AsmMapSymbolFile(null);
                    }
                    _symbolDirty = false;
                    symbolMapRebuilt = true;
                }

                // #1130: in decomp mode, layer the project's symbol artifacts OVER
                // the shipped map (project wins) via a CACHED MergedAsmMapFile
                // wrapper (#1138 — building it copies+sorts all project keys, so it
                // is rebuilt only when the project resolver or the shipped map
                // changes). Any fault returns the shipped map unchanged so classic
                // behaviour is never disturbed.
                if (CoreState.IsDecompMode && CoreState.DecompProject != null)
                {
                    try
                    {
                        if (_projectDirty || _projectResolver == null)
                        {
                            _projectResolver = DecompSymbolResolver.Load(CoreState.DecompProject);
                            _projectResolver.RegisterToCommentCache(_rom);
                            _projectDirty = false;
                            _mergedMap = null;   // resolver changed -> rebuild wrapper
                        }
                        if (_mergedMap == null || symbolMapRebuilt)
                        {
                            _mergedMap = new MergedAsmMapFile(_symbolMap, _projectResolver);
                        }
                        return _mergedMap;
                    }
                    catch
                    {
                        // Never cache a half-built merged map on fault.
                        return _symbolMap;
                    }
                }

                return _symbolMap;
            }
        }

        public bool IsHardCodeUnit(uint unitId)
        {
            lock (_lock)
            {
                EnsureScannedNoLock();
                return _unit[(byte)unitId];
            }
        }

        public bool IsHardCodeClass(uint classId)
        {
            lock (_lock)
            {
                EnsureScannedNoLock();
                return _class[(byte)classId];
            }
        }

        public bool IsHardCodeItem(uint itemId)
        {
            lock (_lock)
            {
                EnsureScannedNoLock();
                return _item[(byte)itemId];
            }
        }

        void EnsureScannedNoLock()
        {
            if (!_dirty) return;

            Array.Clear(_unit, 0, _unit.Length);
            Array.Clear(_class, 0, _class.Length);
            Array.Clear(_item, 0, _item.Length);

            try
            {
                PatchHardCodeScanner.ScanHardCodes(_rom, _unit, _class, _item);
            }
            catch
            {
                // Defensive: the scanner is documented never-throw, but if
                // anything slips through, fall back to "no hardcode flags" rather
                // than crash the editor's RefreshHardCodingWarning path.
            }

            _dirty = false;
        }
    }
}
