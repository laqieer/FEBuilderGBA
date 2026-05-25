using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Map tile animation type 2 editor (palette animation).
    /// WinForms: <c>MapTileAnimation2Form</c> - block size 8, validated by
    /// <c>isPointer(u32(addr+0))</c>. Fields: PaletteDataPointer (GBA pointer
    /// at +0), AnimInterval (u8@4), DataCount (u8@5), StartPaletteIndex (u8@6),
    /// Unknown7 (u8@7).
    ///
    /// The +0 field is a <see cref="EditorFormRef.FieldType.Pointer"/> (P0)
    /// because WinForms uses <c>Program.ROM.p32</c>/<c>write_p32</c> which
    /// strips and re-adds the <c>0x08000000</c> high bit. Storing it as a raw
    /// DWord (D0) would write the offset form back to ROM without the high
    /// bit, corrupting the address (gap-sweep #426, Copilot CLI plan-review
    /// finding #1).
    /// </summary>
    public class MapTileAnimation2ViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "P0", "B4", "B5", "B6", "B7" });

        /// <summary>WF block size constant (8 bytes per row).</summary>
        public const uint BLOCK_SIZE = 8;

        /// <summary>WF sub-list block size constant (2 bytes per palette row).</summary>
        public const uint N_BLOCK_SIZE = 2;

        uint _currentAddr;
        bool _isLoaded;
        uint _paletteDataPointer;
        uint _animInterval;
        uint _dataCount;
        uint _startPaletteIndex;
        uint _unknown7;

        // Filter panel (top read-config bar).
        uint _readStartAddress;
        uint _readCount;
        uint? _selectedPlist;
        List<MapTileAnimation2Core.PlistRow> _plistRows = new();

        // Selection bar.
        uint _selectedAddress;

        // Palette sub-list (panel2).
        List<MapTileAnimation2Core.PaletteRow> _paletteRows = new();
        int _selectedPaletteRowIndex = -1;
        uint _paletteR;
        uint _paletteG;
        uint _paletteB;
        uint _paletteGba;
        uint _nReadStartAddress;
        uint _nReadCount;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint PaletteDataPointer { get => _paletteDataPointer; set => SetField(ref _paletteDataPointer, value); }
        public uint AnimInterval { get => _animInterval; set => SetField(ref _animInterval, value); }
        public uint DataCount { get => _dataCount; set => SetField(ref _dataCount, value); }
        public uint StartPaletteIndex { get => _startPaletteIndex; set => SetField(ref _startPaletteIndex, value); }
        public uint Unknown7 { get => _unknown7; set => SetField(ref _unknown7, value); }

        public uint BlockSize => BLOCK_SIZE;
        public uint NBlockSize => N_BLOCK_SIZE;

        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint? SelectedPlist { get => _selectedPlist; set => SetField(ref _selectedPlist, value); }
        public List<MapTileAnimation2Core.PlistRow> PlistRows
        {
            get => _plistRows;
            set => SetField(ref _plistRows, value ?? new());
        }
        public uint SelectedAddress { get => _selectedAddress; set => SetField(ref _selectedAddress, value); }

        public List<MapTileAnimation2Core.PaletteRow> PaletteRows
        {
            get => _paletteRows;
            set => SetField(ref _paletteRows, value ?? new());
        }
        public int SelectedPaletteRowIndex { get => _selectedPaletteRowIndex; set => SetField(ref _selectedPaletteRowIndex, value); }
        public uint PaletteR { get => _paletteR; set => SetField(ref _paletteR, value); }
        public uint PaletteG { get => _paletteG; set => SetField(ref _paletteG, value); }
        public uint PaletteB { get => _paletteB; set => SetField(ref _paletteB, value); }
        public uint PaletteGba { get => _paletteGba; set => SetField(ref _paletteGba, value); }
        public uint NReadStartAddress { get => _nReadStartAddress; set => SetField(ref _nReadStartAddress, value); }
        public uint NReadCount { get => _nReadCount; set => SetField(ref _nReadCount, value); }

        /// <summary>Build the filter combo list - one row per distinct PLIST referenced by any map.</summary>
        public List<MapTileAnimation2Core.PlistRow> LoadPlistList()
        {
            var rom = CoreState.ROM;
            var rows = MapTileAnimation2Core.BuildPlistList(rom);
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
            var entries = MapTileAnimation2Core.ScanEntries(rom, baseAddr, maxRows: 256);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                string display = $"0x{i:X2} Palette Interval={e.Wait:X2} Count={e.Count:X2}";
                result.Add(new AddrResult(e.Addr, display, (uint)i));
            }
            ReadCount = (uint)entries.Count;
            return result;
        }

        /// <summary>
        /// Build the list shown when the editor opens with no PLIST chosen -
        /// scan the PLIST list for the first valid entry. Kept for backwards
        /// compatibility with the existing <c>DataVerifiable</c> contract that
        /// calls <c>LoadList()</c> with no arguments.
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
            var rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            SelectedAddress = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            PaletteDataPointer = values["P0"];
            AnimInterval = values["B4"];
            DataCount = values["B5"];
            StartPaletteIndex = values["B6"];
            Unknown7 = values["B7"];

            // Rebuild palette sub-list for the new entry.
            PaletteRows = MapTileAnimation2Core.BuildPaletteList(rom, PaletteDataPointer, DataCount);
            SelectedPaletteRowIndex = PaletteRows.Count > 0 ? 0 : -1;
            if (PaletteRows.Count > 0)
            {
                var first = PaletteRows[0];
                PaletteR = first.R;
                PaletteG = first.G;
                PaletteB = first.B;
                PaletteGba = first.Gba;
                NReadStartAddress = U.toOffset(PaletteDataPointer);
                NReadCount = DataCount;
            }
            else
            {
                // Clear stale N-address bar / RGB editor state when the
                // entry has DataCount==0 or an unsafe/empty palette pointer.
                // Without this reset, switching from an entry with palette
                // rows to one without (e.g., a freshly-zeroed DataCount)
                // would leave the previous entry's N-address, R/G/B inputs,
                // and GBA color preview visible (Copilot CLI inline review
                // on PR #534).
                NReadStartAddress = 0;
                NReadCount = 0;
                PaletteR = 0;
                PaletteG = 0;
                PaletteB = 0;
                PaletteGba = 0;
            }

            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        /// <summary>Load the R/G/B fields from a specific palette row index.</summary>
        public void LoadPaletteRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= PaletteRows.Count) return;
            // Suppress dirty marking while syncing the R/G/B / GBA fields
            // from the selected palette row - this is a navigation event,
            // not a user edit. Do NOT MarkClean() afterwards because that
            // would wipe a pre-existing dirty state from prior edits to
            // the main entry fields (Copilot CLI inline review on PR #534).
            IsLoading = true;
            var row = PaletteRows[rowIndex];
            SelectedPaletteRowIndex = rowIndex;
            PaletteR = row.R;
            PaletteG = row.G;
            PaletteB = row.B;
            PaletteGba = row.Gba;
            IsLoading = false;
        }

        /// <summary>
        /// Recompute the encoded GBA color from the current R/G/B values.
        /// Called by the view when R/G/B inputs change.
        /// </summary>
        public void RecomputeGbaColor()
        {
            PaletteGba = MapTileAnimation2Core.RgbToGba((byte)PaletteR, (byte)PaletteG, (byte)PaletteB);
        }

        /// <summary>Write the current main-entry fields to ROM via EditorFormRef.</summary>
        public void Write()
        {
            var rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["P0"] = PaletteDataPointer, ["B4"] = AnimInterval,
                ["B5"] = DataCount, ["B6"] = StartPaletteIndex,
                ["B7"] = Unknown7,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        /// <summary>
        /// Write the currently selected palette row (R/G/B encoded as a 15-bit
        /// GBA word) back to ROM at <c>dataPointer + 2 * rowIndex</c>.
        /// Returns <c>true</c> on success, <c>false</c> if the row index or
        /// data pointer is invalid. Also normalizes the in-memory
        /// <see cref="PaletteR"/>/<see cref="PaletteG"/>/<see cref="PaletteB"/>
        /// to the post-truncation (multiples of 8) values so the UI inputs
        /// stay in sync with the ROM bytes (Copilot CLI inline review on
        /// PR #534 - the 5-bit quantization drops the low 3 bits, so a
        /// user-typed value like R=125 ends up persisted as 120).
        /// </summary>
        public bool WritePaletteRow()
        {
            var rom = CoreState.ROM;
            if (rom == null) return false;
            if (SelectedPaletteRowIndex < 0 || SelectedPaletteRowIndex >= PaletteRows.Count) return false;
            if (PaletteDataPointer == 0) return false;
            uint offset = U.toOffset(PaletteDataPointer);
            if (!U.isSafetyOffset(offset, rom)) return false;
            uint addr = offset + (uint)(SelectedPaletteRowIndex * 2);
            if (addr + 2 > (uint)rom.Data.Length) return false;
            ushort gba = MapTileAnimation2Core.RgbToGba((byte)PaletteR, (byte)PaletteG, (byte)PaletteB);
            rom.write_u16(addr, gba);
            // Normalize the UI fields to the truncated (multiples of 8)
            // values so PaletteR/G/B match the bytes that ended up in ROM.
            var (nr, ng, nb) = MapTileAnimation2Core.GbaToRgb(gba);
            IsLoading = true;
            try { PaletteR = nr; PaletteG = ng; PaletteB = nb; }
            finally { IsLoading = false; }
            PaletteGba = gba;
            // Refresh palette rows so the sub-list reflects the new value.
            PaletteRows = MapTileAnimation2Core.BuildPaletteList(rom, PaletteDataPointer, DataCount);
            return true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PaletteDataPointer"] = $"0x{PaletteDataPointer:X08}",
                ["AnimInterval"] = $"0x{AnimInterval:X02}",
                ["DataCount"] = $"0x{DataCount:X02}",
                ["StartPaletteIndex"] = $"0x{StartPaletteIndex:X02}",
                ["Unknown7"] = $"0x{Unknown7:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            var rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["PaletteDataPointer@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["AnimInterval@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["DataCount@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["StartPaletteIndex@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["Unknown7@0x07"] = $"0x{rom.u8(a + 7):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["PaletteDataPointer"] = "PaletteDataPointer@0x00",
            ["AnimInterval"] = "AnimInterval@0x04",
            ["DataCount"] = "DataCount@0x05",
            ["StartPaletteIndex"] = "StartPaletteIndex@0x06",
            ["Unknown7"] = "Unknown7@0x07",
        };

        // ----------------------------------------------------------------
        // Bulk Import / Bulk Export / Data Expansion (#524).
        // ----------------------------------------------------------------

        /// <summary>
        /// Resolve the PLIST table slot address for the currently-selected
        /// PLIST so the BulkImport / ExpandEntryList helpers can repoint it.
        /// Returns 0 when no PLIST is selected or the table is unreachable.
        /// Mirrors the WF <c>MapPointerForm.PlistToOffsetAddrFast(...)</c>
        /// "out pointer" path: slot = plistTableBase + plist*4.
        /// </summary>
        public uint GetPlistTableSlot()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            if (!SelectedPlist.HasValue || SelectedPlist.Value == 0) return 0;
            uint plistTablePtr = rom.RomInfo.map_tileanime2_pointer;
            if (plistTablePtr == 0) return 0;
            uint plistTableBase = rom.p32(plistTablePtr);
            if (!U.isSafetyOffset(plistTableBase, rom)) return 0;
            uint slot = plistTableBase + SelectedPlist.Value * 4u;
            if (!U.isSafetyOffset(slot, rom)) return 0;
            return slot;
        }


        /// <summary>
        /// Export the currently-selected PLIST's entry table to a
        /// .mapanime2.txt file via <see cref="MapTileAnimation2Core.BulkExport"/>.
        /// Returns the empty string on success, an error message otherwise.
        /// </summary>
        public string BulkExport(string filename)
        {
            var rom = CoreState.ROM;
            if (rom == null) return "ROM is null.";
            if (ReadStartAddress == 0) return "No PLIST selected.";
            return MapTileAnimation2Core.BulkExport(rom, filename, ReadStartAddress, ReadCount);
        }

        /// <summary>
        /// Import a .mapanime2.txt file into the currently-selected PLIST.
        /// The caller is expected to have opened an undo scope via
        /// <c>_undoService.Begin</c> in the view layer; the Core helper
        /// records each ROM write into the ambient UndoData.
        ///
        /// Returns the empty string on success, an error message otherwise.
        /// </summary>
        public string BulkImport(string filename, uint pointerSlot)
        {
            var rom = CoreState.ROM;
            if (rom == null) return "ROM is null.";
            if (ReadStartAddress == 0) return "No PLIST selected.";
            return MapTileAnimation2Core.BulkImport(rom, filename, pointerSlot,
                ReadStartAddress, ReadCount);
        }

        /// <summary>
        /// Grow the entry table by one row (oldCount + 1 = newCount).
        /// The caller opens an undo scope; the Core helper records writes
        /// into the ambient scope. Returns the new base offset (ROM offset)
        /// or <see cref="U.NOT_FOUND"/> on failure.
        /// </summary>
        public uint ExpandEntryListByOne(uint pointerSlot)
        {
            var rom = CoreState.ROM;
            if (rom == null) return U.NOT_FOUND;
            if (ReadStartAddress == 0) return U.NOT_FOUND;
            uint newCount = ReadCount + 1;
            return MapTileAnimation2Core.ExpandEntryList(rom, pointerSlot,
                ReadStartAddress, ReadCount, newCount);
        }

        /// <summary>
        /// Grow the palette sub-table by one row. The pointer slot is the
        /// +0 byte of the current entry row (the palette data pointer field).
        /// Mirrors the WF `EventUnitForm.AddressListExpandsEvent` behavior of
        /// also updating the entry's DataCount (B5 at <c>CurrentAddr+5</c>) so
        /// the editor sees the new row on the next reload (Copilot bot
        /// review on PR #634). The DataCount write happens within the same
        /// ambient undo scope as the table allocation + repoint, so a rollback
        /// reverts both.
        /// </summary>
        public uint ExpandPaletteRowListByOne()
        {
            var rom = CoreState.ROM;
            if (rom == null) return U.NOT_FOUND;
            if (CurrentAddr == 0) return U.NOT_FOUND;
            if (PaletteDataPointer == 0) return U.NOT_FOUND;
            uint paletteSlot = CurrentAddr + 0; // P0 field
            uint oldBase = U.toOffset(PaletteDataPointer);
            uint newCount = DataCount + 1;
            // The Core helper caps DataCount at 255 since B5 is one byte.
            if (newCount > 0xFF) return U.NOT_FOUND;
            uint newBase = MapTileAnimation2Core.ExpandPaletteRowList(rom,
                paletteSlot, oldBase, DataCount, newCount);
            if (newBase == U.NOT_FOUND) return U.NOT_FOUND;
            // Also bump the entry's B5 (DataCount) so a reload picks up the
            // new row. The ambient undo scope captures this write too.
            rom.write_u8(CurrentAddr + 5, newCount);
            // Update the VM's in-memory state so the view's reload picks up
            // the new count without a fresh u8 read.
            IsLoading = true;
            try { DataCount = newCount; }
            finally { IsLoading = false; }
            return newBase;
        }
    }
}
