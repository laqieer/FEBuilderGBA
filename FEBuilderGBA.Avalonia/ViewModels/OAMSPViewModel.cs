using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Special OAM editor (#1179) — Avalonia port of WinForms
    /// <c>OAMSPForm</c>. A READ-ONLY discovery + hex-inspection tool: it scans the
    /// ROM's ARM-Thumb LDR literal-pool loads (via the cross-platform
    /// <see cref="SpecialOamScanCore"/>) to discover special-OAM sprite-assembly
    /// pointer arrays, labels them from the <c>oam_name_</c> resource, and shows a
    /// hex dump of the selected entry's pointer array + OAM12 sub-blocks.
    ///
    /// <para>The full-ROM LDR scan is expensive, so the LDR map AND the scanned
    /// entries are cached per-ROM (keyed by <see cref="object.ReferenceEquals"/> on
    /// the <see cref="ROM"/> instance — same pattern as
    /// <c>PointerToolViewModel</c>) and rebuilt only on a ROM reload. Selection is
    /// read-only: it never mutates the ROM and must not mark the editor dirty.</para>
    /// </summary>
    public class OAMSPViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        string _entryName = "";
        string _entryLength = "";
        string _oam12Count = "";
        string _detailText = "";

        // ----- Per-ROM cache (#1179) -----
        // DisassemblerTrumb.MakeLDRMap (via PointerToolAutoSearchCore.BuildLdrMap)
        // is a full-ROM scan; both the LDR map and the scanned OAMSP entries are
        // cached and tagged with the ROM instance they were built for, so repeated
        // LoadList()/LoadEntry() calls do not re-scan. Invalidated on a ROM reload
        // (a new ROM instance fails the ReferenceEquals check).
        List<DisassemblerTrumb.LDRPointer> _ldrMap;
        List<SpecialOamScanCore.OamSpEntry> _entries;
        ROM _cacheRom;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Selected entry's label (e.g. "OAMSP 0x08abcdef").</summary>
        public string EntryName { get => _entryName; set => SetField(ref _entryName, value); }
        /// <summary>Selected entry's pointer-array byte length, formatted as hex.</summary>
        public string EntryLength { get => _entryLength; set => SetField(ref _entryLength, value); }
        /// <summary>Number of OAM12 sub-blocks referenced by the selected entry.</summary>
        public string Oam12Count { get => _oam12Count; set => SetField(ref _oam12Count, value); }
        /// <summary>Hex-dump detail of the selected entry (pointer words + OAM12 records).</summary>
        public string DetailText { get => _detailText; set => SetField(ref _detailText, value); }

        /// <summary>
        /// Build (and cache) the LDR map + scanned OAMSP entries for the current
        /// ROM, then return the list of discovered entries as <see cref="AddrResult"/>
        /// (addr = the real entry ROM address; name = the OAMSP label). Empty when
        /// no ROM is loaded. Never re-scans when the ROM is unchanged.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;

            EnsureScan(rom);
            if (_entries == null) return result;

            foreach (var e in _entries)
            {
                result.Add(new AddrResult(e.Addr, e.Name, e.Addr));
            }
            return result;
        }

        /// <summary>
        /// Select the entry at <paramref name="addr"/> (a real ROM address from the
        /// list) and populate the detail labels + hex-dump text. Read-only — does
        /// not mutate the ROM and never throws.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            CurrentAddr = addr;
            EntryName = "";
            EntryLength = "";
            Oam12Count = "";
            DetailText = "";
            IsLoaded = false;

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            EnsureScan(rom);
            if (_entries == null) return;

            SpecialOamScanCore.OamSpEntry entry = null;
            foreach (var e in _entries)
            {
                if (e.Addr == addr) { entry = e; break; }
            }
            if (entry == null) return;

            EntryName = entry.Name;
            EntryLength = string.Format("0x{0:X}", entry.Length);
            // Recompute the COMPLETE OAM12 list fresh (NOT the deduped scan-time
            // entry.Oam12) so the count matches WF's per-selection detail (#1179).
            Oam12Count = SpecialOamScanCore.ComputeOam12Blocks(rom, entry).Count.ToString();
            DetailText = SpecialOamScanCore.BuildDetailDump(rom, entry);
            IsLoaded = true;
        }

        /// <summary>
        /// (Re)build the cached LDR map + OAMSP entry scan when missing or stale
        /// (the ROM instance changed). Cheap when cached + ROM unchanged. The LDR
        /// scan + special-OAM scan are both never-throws.
        /// </summary>
        void EnsureScan(ROM rom)
        {
            if (_entries != null && _ldrMap != null && ReferenceEquals(_cacheRom, rom))
                return;

            _ldrMap = PointerToolAutoSearchCore.BuildLdrMap(rom?.Data);
            _entries = SpecialOamScanCore.ScanSpecialOam(rom, _ldrMap);
            _cacheRom = rom;
        }

        /// <summary>Test/diagnostic hook: true when the scan cache is populated for
        /// the given ROM instance (so a second <see cref="LoadList"/> reuses it
        /// rather than re-scanning).</summary>
        public bool IsCachedFor(ROM rom) => _entries != null && _ldrMap != null && ReferenceEquals(_cacheRom, rom);
    }
}
