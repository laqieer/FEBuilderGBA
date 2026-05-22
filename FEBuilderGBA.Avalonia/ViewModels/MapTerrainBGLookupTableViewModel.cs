using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// View model for `MapTerrainBGLookupTableView` — the per-filter list
    /// of terrain → battle-BG mappings (one byte per entry). Mirrors the
    /// WinForms `MapTerrainBGLookupTableForm` but routes pointer logic
    /// through the cross-platform <see cref="MapTerrainLookupCore"/>.
    ///
    /// Phase 4 gap-fix (#441): adds FilterIndex (the 21 vanilla set entries
    /// or N-row extended table when the ExtendsBattleBG patch is installed)
    /// so the AV view exposes the same configuration combo the WF Designer
    /// did. NavigateToFilterAndRow lets the Floor sister view jump back
    /// preserving both filter and selected address (parity contract from
    /// the Floor #482 gap-fix). This file is `partial` so the manifest in
    /// `MapTerrainBGLookupTableViewModel.NavigationTargets.cs` can declare
    /// the three Phase 4 jump targets without bringing
    /// `FEBuilderGBA.Avalonia.Views` into the main VM.
    /// </summary>
    public partial class MapTerrainBGLookupTableViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        bool _isLoaded;
        bool _isLoading;
        uint _battleBG;

        uint _filterIndex;
        uint _readStartAddress;
        uint _readCount;
        uint _itemAddress;
        bool _isAllocated = true;
        bool _isExtendsPatchInstalled;
        List<string> _filterEntries = new();
        uint[] _filterPointers = System.Array.Empty<uint>();

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
        /// <summary>Battle BG index (B0 / J_0_BATTLEBG).</summary>
        public uint BattleBG { get => _battleBG; set => SetField(ref _battleBG, value); }

        /// <summary>Selected filter row (0..FilterEntries.Count-1). Each filter swaps the list base pointer.</summary>
        public uint FilterIndex { get => _filterIndex; set => SetField(ref _filterIndex, value); }
        /// <summary>Read-only display of the current list base address.</summary>
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        /// <summary>Read-only display of map_terrain_type_count (matches the WF "ReadCount" NumericUpDown).</summary>
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        /// <summary>The currently selected entry's ROM address (matches the WF "Address" NumericUpDown).</summary>
        public uint ItemAddress { get => _itemAddress; set => SetField(ref _itemAddress, value); }
        /// <summary>False when the extended-patch is installed but the current filter slot has no data — drives the ERROR label.</summary>
        public bool IsAllocated { get => _isAllocated; set => SetField(ref _isAllocated, value); }
        /// <summary>True when ExtendsBattleBG patch is installed (drives the patch-install button visibility).</summary>
        public bool IsExtendsPatchInstalled { get => _isExtendsPatchInstalled; set => SetField(ref _isExtendsPatchInstalled, value); }
        /// <summary>Localized filter-row labels (e.g. "Plain", "Forest"). Indexed 0..Count-1; the count varies with the patch.</summary>
        public IReadOnlyList<string> FilterEntries => _filterEntries;
        /// <summary>Raw pointers backing the filter combo — exposed for cross-checks.</summary>
        public IReadOnlyList<uint> FilterPointers => _filterPointers;

        // ---------------------------------------------------------------
        // Loading: filter combo + list rows
        // ---------------------------------------------------------------

        /// <summary>
        /// Populate the filter combo and the pointer cache. Call once on
        /// view-load, then call <see cref="LoadList(int)"/> with a specific
        /// filter index whenever the user changes the combo selection.
        /// Mirrors <see cref="MapTerrainFloorLookupTableViewModel.LoadFilterEntries"/>.
        /// </summary>
        public void LoadFilterEntries()
        {
            ROM rom = CoreState.ROM;
            _filterEntries.Clear();
            _filterPointers = System.Array.Empty<uint>();

            if (rom?.RomInfo == null) return;

            _filterPointers = MapTerrainLookupCore.GetPointers(rom, isFloor: false);
            IsExtendsPatchInstalled =
                PatchDetection.SearchExtendsBattleBG(rom) == PatchDetection.ExtendsBattleBG_extends.Extends;

            var dic = MapTerrainLookupCore.GetTerrainSetDic(rom);
            var names = U.DictionaryToValuesList(dic);
            for (int i = 0; i < _filterPointers.Length; i++)
            {
                string name = i < names.Count ? names[i] : $"Extends {i:X02}";
                _filterEntries.Add($"0x{i:X02} {name}");
            }
            OnPropertyChanged(nameof(FilterEntries));
            OnPropertyChanged(nameof(FilterPointers));
        }

        /// <summary>
        /// Load the list of terrain entries for the requested BG filter index
        /// (0..20 vanilla, 0..N when the ExtendsBattleBG patch is installed).
        /// Each entry is 1 byte, total count = map_terrain_type_count.
        ///
        /// Updates IsAllocated based on whether the slot's pointer is valid
        /// (extends patch may have empty/unallocated slots) so the patch-install
        /// button stays in sync with the active filter.
        ///
        /// Phase 4 gap-fix (#442): the Floor view's "Jump to BG" button passes
        /// the source filterIndex via <see cref="MapTerrainBGLookupTableView.NavigateToFilterAndRow"/>,
        /// which delegates here. Without this overload, non-zero source
        /// filters would map to the WRONG BG list (Copilot CLI review point 2).
        /// </summary>
        public List<AddrResult> LoadList(int filterIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            if (_filterPointers.Length == 0)
            {
                LoadFilterEntries();
            }
            if (filterIndex < 0 || filterIndex >= _filterPointers.Length)
            {
                IsAllocated = false;
                return new List<AddrResult>();
            }

            uint ptr = _filterPointers[filterIndex];
            if (ptr == 0)
            {
                IsAllocated = false;
                return new List<AddrResult>();
            }

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                IsAllocated = false;
                return new List<AddrResult>();
            }

            IsAllocated = true;
            FilterIndex = (uint)filterIndex;
            ReadStartAddress = baseAddr;

            int count = (int)rom.RomInfo.map_terrain_type_count;
            ReadCount = (uint)count;
            var result = new List<AddrResult>();
            for (int i = 0; i < count; i++)
            {
                uint addr = (uint)(baseAddr + i);
                if (addr >= (uint)rom.Data.Length) break;
                result.Add(new AddrResult(addr, $"0x{i:X02} Terrain {i}", (uint)i));
            }
            return result;
        }

        /// <summary>
        /// Vanilla-filter overload (filter=0). Kept for source compatibility
        /// with existing tests + the on-Opened auto-load path.
        /// </summary>
        public List<AddrResult> LoadList() => LoadList(0);

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            ItemAddress = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            BattleBG = values["B0"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint> { ["B0"] = BattleBG };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return (int)rom.RomInfo.map_terrain_type_count;
        }

        /// <summary>
        /// Mirror WinForms <c>MapTerrainBGLookupTableForm.GetFilterIndexOfAddr</c>:
        /// given a list-row address, return the filter index whose pointer
        /// resolves to that address's base. Used by code-behind to pick the
        /// correct filter when the view is opened via a deep-link.
        /// </summary>
        public uint ResolveFilterIndexForAddress(uint addr)
            => MapTerrainLookupCore.ResolveFilterIndexForAddress(CoreState.ROM, addr, isFloor: false);

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["BattleBG"] = $"0x{BattleBG:X02}",
                ["FilterIndex"] = $"0x{FilterIndex:X02}",
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
                ["BattleBG@0x00"] = $"0x{rom.u8(a + 0):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["BattleBG"] = "BattleBG@0x00",
        };
    }
}
