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
