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
