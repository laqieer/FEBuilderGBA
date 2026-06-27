using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Map tile animation type 1 editor.
    /// WinForms: MapTileAnimation1Form — block size 8, validated by isPointer(u32(addr+4)).
    /// Fields: AnimInterval (u16@0), DataCount (u16@2), MapTileDataPointer (u32@4).
    ///
    /// <para>ANIME1 PLIST FILTER (#955, #957 W1c): this editor now mirrors
    /// MapTileAnimation2 — a filter combo enumerates the distinct anime1 PLISTs
    /// referenced by the map settings (<see cref="LoadPlistList"/> →
    /// <see cref="MapTileAnimation1Core.BuildPlistList"/>), and selecting a PLIST
    /// drives the entry list off that PLIST's resolved data table
    /// (<see cref="BuildList"/> → <see cref="MapTileAnimation1Core.ScanEntries"/>).
    /// Previously the VM treated <c>map_tileanime1_pointer</c> (the PLIST TABLE)
    /// as a flat 8-byte entry table, which was structurally wrong — the WF form
    /// resolves the SELECTED PLIST to its own data table via
    /// <c>PlistToOffsetAddr(ANIMATION, anime1_plist)</c>. ANIME1 resolves under
    /// <see cref="MapChangeCore.PlistType.ANIMATION"/> (ANIME1/ANIME2 share that
    /// base in vanilla ROMs).</para></summary>
    public class MapTileAnimation1ViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "W0", "W2", "D4" });

        /// <summary>WF block size constant (8 bytes per row).</summary>
        public const uint BLOCK_SIZE = 8;

        uint _currentAddr;
        bool _isLoaded;
        uint _animInterval;
        uint _dataCount;
        uint _mapTileDataPointer;

        // Filter panel (top read-config bar) — mirrors MapTileAnimation2ViewModel.
        uint _readStartAddress;
        uint _readCount;
        uint? _selectedPlist;
        List<MapTileAnimation1Core.PlistRow> _plistRows = new();

        // Selection bar.
        uint _selectedAddress;

        // Image preview / Import-Export (#1602). The selected 16-color sub-palette
        // index drives both the preview render and the import remap target.
        int _selectedPaletteIndex;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint AnimInterval { get => _animInterval; set => SetField(ref _animInterval, value); }
        public uint DataCount { get => _dataCount; set => SetField(ref _dataCount, value); }
        public uint MapTileDataPointer { get => _mapTileDataPointer; set => SetField(ref _mapTileDataPointer, value); }

        public uint BlockSize => BLOCK_SIZE;

        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint? SelectedPlist { get => _selectedPlist; set => SetField(ref _selectedPlist, value); }
        public List<MapTileAnimation1Core.PlistRow> PlistRows
        {
            get => _plistRows;
            set => SetField(ref _plistRows, value ?? new());
        }
        public uint SelectedAddress { get => _selectedAddress; set => SetField(ref _selectedAddress, value); }
        public int SelectedPaletteIndex { get => _selectedPaletteIndex; set => SetField(ref _selectedPaletteIndex, value); }

        /// <summary>Build the filter combo list — one row per distinct anime1
        /// PLIST referenced by any map (mirrors WF MakeTileAnimation1).</summary>
        public List<MapTileAnimation1Core.PlistRow> LoadPlistList()
        {
            var rom = CoreState.ROM;
            var rows = MapTileAnimation1Core.BuildPlistList(rom);
            PlistRows = rows;
            return rows;
        }

        /// <summary>Build the address-list for a specific PLIST root address.</summary>
        public List<AddrResult> BuildList(uint baseAddr)
        {
            var rom = CoreState.ROM;
            var result = new List<AddrResult>();
            if (rom == null || baseAddr == 0) return result;
            ReadStartAddress = baseAddr;
            var entries = MapTileAnimation1Core.ScanEntries(rom, baseAddr, maxRows: 256);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                string display = $"0x{i:X2} Interval={e.Wait:X4} Count={e.Length:X4}";
                result.Add(new AddrResult(e.Addr, display, (uint)i));
            }
            ReadCount = (uint)entries.Count;
            return result;
        }

        /// <summary>
        /// Build the list shown when the editor opens with no PLIST chosen —
        /// scan the PLIST list for the first valid (non-broken) entry. Kept for
        /// the <c>IDataVerifiable</c> contract that calls <c>LoadList()</c> with
        /// no arguments. Mirrors MapTileAnimation2ViewModel.LoadList().
        /// </summary>
        public List<AddrResult> LoadList()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var plistRows = LoadPlistList();
            foreach (var row in plistRows)
            {
                if (row.IsBroken) continue;
                SelectedPlist = row.Plist;
                return BuildList(row.Addr);
            }
            return new List<AddrResult>();
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            SelectedAddress = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            AnimInterval = values["W0"];
            DataCount = values["W2"];
            MapTileDataPointer = values["D4"];
            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        /// <summary>
        /// Reset the detail fields when the selected PLIST has NO entries (an
        /// empty data table) or is broken, so the panel doesn't show the
        /// previously-selected entry's stale data (#960, same class as the #9
        /// Map Exit stale-detail bug). Gates the Write button via
        /// <see cref="IsLoaded"/>=false (the view's Write_Click early-returns
        /// when <c>!IsLoaded</c>).
        /// </summary>
        public void ClearEntry()
        {
            CurrentAddr = 0;
            SelectedAddress = 0;
            AnimInterval = 0;
            DataCount = 0;
            MapTileDataPointer = 0;
            IsLoaded = false;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["W0"] = AnimInterval, ["W2"] = DataCount,
                ["D4"] = MapTileDataPointer,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        // -----------------------------------------------------------------
        // Image preview + Import/Export (#1602). All ROM access goes through
        // MapTileAnimation1ImageCore (RAW 4bpp at the entry's +4 pointer; the
        // entry's +2 length stays authoritative on single import).
        // -----------------------------------------------------------------

        /// <summary>Resolve the parent map's palette offset for the selected
        /// anime1 PLIST; 0 when none resolves.</summary>
        public uint ResolvePaletteOffset()
        {
            var rom = CoreState.ROM;
            if (rom == null || SelectedPlist == null) return 0;
            return MapTileAnimation1ImageCore.ResolvePaletteOffset(rom, SelectedPlist.Value, out uint off)
                ? off : 0;
        }

        /// <summary>Render the currently-selected entry's image (256 × CalcHeight)
        /// using the selected sub-palette, or null when it cannot be rendered.</summary>
        public IImage RenderPreview()
        {
            var rom = CoreState.ROM;
            if (rom == null || !IsLoaded || CurrentAddr == 0) return null;
            uint palOffset = ResolvePaletteOffset();
            if (palOffset == 0) return null;
            return MapTileAnimation1ImageCore.RenderEntryImage(
                rom, MapTileDataPointer, DataCount, palOffset, SelectedPaletteIndex);
        }

        /// <summary>Single-PNG import (WF parity; +2 length unchanged). Returns ""
        /// on success or a localized error. Caller owns the undo scope.</summary>
        public string ImportImage(byte[] rgba, int width, int height)
        {
            var rom = CoreState.ROM;
            if (rom == null || !IsLoaded || CurrentAddr == 0) return "No entry selected.";
            uint palOffset = ResolvePaletteOffset();
            if (palOffset == 0) return "Cannot resolve the tile animation palette.";
            string err = MapTileAnimation1ImageCore.ImportEntryImage(
                rom, CurrentAddr, rgba, width, height, palOffset, SelectedPaletteIndex);
            if (err == "")
            {
                // Re-read the entry so MapTileDataPointer reflects the new +4.
                LoadEntry(CurrentAddr);
            }
            return err;
        }

        /// <summary>Export the current PLIST's entries to a .mapanime1.txt manifest
        /// + per-entry PNGs. Returns "" on success or a localized error.</summary>
        public string ExportBatch(string txtPath, System.Action<IImage, string> savePng)
        {
            var rom = CoreState.ROM;
            if (rom == null || ReadStartAddress == 0) return "No PLIST selected.";
            uint palOffset = ResolvePaletteOffset();
            if (palOffset == 0) return "Cannot resolve the tile animation palette.";
            return MapTileAnimation1ImageCore.ExportBatchTxt(
                rom, ReadStartAddress, (int)ReadCount, palOffset, SelectedPaletteIndex,
                txtPath, savePng);
        }

        /// <summary>Export every anime1 frame as an animated GIF, compositing each
        /// frame onto the rendered chapter map that uses the selected PLIST (the
        /// deferred half of #1602 — WF MapTileAnimation1Form.ExportGif). READ-ONLY.
        /// Returns "" on success or a localized error.</summary>
        public string ExportGif(string gifPath)
        {
            var rom = CoreState.ROM;
            if (rom == null || SelectedPlist == null) return "No PLIST selected.";
            return MapTileAnimation1ImageCore.ExportGif(rom, SelectedPlist.Value, gifPath);
        }

        /// <summary>Re-import a .mapanime1.txt manifest into the selected PLIST.
        /// Returns "" on success or a localized error. Caller owns the undo
        /// scope.</summary>
        public string ImportBatch(string txtPath, MapTileAnimation1ImageCore.LoadRgbaDelegate loadRgba)
        {
            var rom = CoreState.ROM;
            if (rom == null || SelectedPlist == null) return "No PLIST selected.";
            uint palOffset = ResolvePaletteOffset();
            if (palOffset == 0) return "Cannot resolve the tile animation palette.";
            // Resolve the PLIST table pointer slot so the rebuilt table can be repointed.
            uint dataAddr = MapChangeCore.PlistToOffsetAddr(
                rom, MapChangeCore.PlistType.ANIMATION, SelectedPlist.Value, out uint pointerSlot);
            if (dataAddr == U.NOT_FOUND || pointerSlot == 0)
                return "Cannot resolve the tile animation pointer slot.";
            return MapTileAnimation1ImageCore.ImportBatchTxt(
                rom, pointerSlot, txtPath, palOffset, SelectedPaletteIndex, loadRgba);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AnimInterval"] = $"0x{AnimInterval:X04}",
                ["DataCount"] = $"0x{DataCount:X04}",
                ["MapTileDataPointer"] = $"0x{MapTileDataPointer:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["AnimInterval@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["DataCount@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["MapTileDataPointer@0x04"] = $"0x{rom.u32(a + 4):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["AnimInterval"] = "AnimInterval@0x00",
                ["DataCount"] = "DataCount@0x02",
                ["MapTileDataPointer"] = "MapTileDataPointer@0x04",
            };
        }
    }
}
