using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// View model for `MapTerrainFloorLookupTableView` — the per-filter list
    /// of terrain → battle-floor mappings (one byte per entry). Mirrors the
    /// WinForms `MapTerrainFloorLookupTableForm` but routes pointer logic
    /// through the cross-platform <see cref="MapTerrainLookupCore"/>.
    ///
    /// Phase 4 gap-fix (#442): adds FilterIndex (the 21 vanilla set entries
    /// or N-row extended table when the ExtendsBattleBG patch is installed)
    /// so the AV view exposes the same configuration combo the WF Designer
    /// did. NavigateTo + NavigateToFilter let the BG sister view jump back
    /// preserving both filter and selected address.
    /// </summary>
    public partial class MapTerrainFloorLookupTableViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        bool _isLoaded;
        bool _isLoading;
        uint _terrainBattleFloor;

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
        /// <summary>Terrain battle floor index (B0 / J_0_TERRAINBATTLE).</summary>
        public uint TerrainBattleFloor { get => _terrainBattleFloor; set => SetField(ref _terrainBattleFloor, value); }

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
        /// </summary>
        public void LoadFilterEntries()
        {
            ROM rom = CoreState.ROM;
            _filterEntries.Clear();
            _filterPointers = System.Array.Empty<uint>();

            if (rom?.RomInfo == null) return;

            _filterPointers = MapTerrainLookupCore.GetPointers(rom, isFloor: true);
            IsExtendsPatchInstalled =
                PatchDetection.SearchExtendsBattleBG(rom) == PatchDetection.ExtendsBattleBG_extends.Extends;

            var dic = MapTerrainLookupCore.GetTerrainSetDic(rom);
            var names = U.DictionaryToValuesList(dic);
            for (int i = 0; i < _filterPointers.Length; i++)
            {
                string name = i < names.Count ? names[i] : $"Extends {i:X02}";
                _filterEntries.Add($"0x{i:X02} {name}");
            }
            // Notify binding clients (FilterEntries / FilterPointers are
            // exposed as IReadOnlyList wrappers around mutable backing fields).
            OnPropertyChanged(nameof(FilterEntries));
            OnPropertyChanged(nameof(FilterPointers));
        }

        /// <summary>
        /// Load the list of terrain entries for the GIVEN filter index.
        /// Updates IsAllocated based on whether the slot's pointer is
        /// valid (extends patch may have empty/unallocated slots).
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
        /// Convenience overload — loads the first filter (vanilla terrain 00).
        /// Kept for source compatibility with existing tests.
        /// </summary>
        public List<AddrResult> LoadList() => LoadList(0);

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            ItemAddress = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            TerrainBattleFloor = v["B0"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint> { ["B0"] = TerrainBattleFloor };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return (int)rom.RomInfo.map_terrain_type_count;
        }

        /// <summary>
        /// Mirror WinForms <c>MapTerrainFloorLookupTableForm.GetFilterIndexOfAddr</c>:
        /// given a list-row address, return the filter index whose pointer
        /// resolves to that address's base. Used by code-behind to pick the
        /// correct filter when the view is opened via a deep-link.
        /// </summary>
        public uint ResolveFilterIndexForAddress(uint addr)
            => MapTerrainLookupCore.ResolveFilterIndexForAddress(CoreState.ROM, addr, isFloor: true);

        // ---------------------------------------------------------------
        // IDataVerifiable
        // ---------------------------------------------------------------

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TerrainBattleFloor"] = $"0x{TerrainBattleFloor:X02}",
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
                ["TerrainBattleFloor@0x00"] = $"0x{rom.u8(a + 0):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["TerrainBattleFloor"] = "TerrainBattleFloor@0x00",
        };
    }
}
