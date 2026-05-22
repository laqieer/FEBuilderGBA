using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapTerrainBGLookupTableViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        bool _isLoaded;
        bool _isLoading;
        uint _battleBG;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
        /// <summary>Battle BG index (B0 / J_0_BATTLEBG).</summary>
        public uint BattleBG { get => _battleBG; set => SetField(ref _battleBG, value); }

        /// <summary>
        /// Load the list of terrain entries for the requested BG filter index
        /// (0..20 vanilla, 0..N when the ExtendsBattleBG patch is installed).
        /// Each entry is 1 byte, total count = map_terrain_type_count.
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

            uint[] pointers = MapTerrainLookupCore.GetPointers(rom, isFloor: false);
            if (filterIndex < 0 || filterIndex >= pointers.Length)
                return new List<AddrResult>();
            uint ptr = pointers[filterIndex];
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            int count = (int)rom.RomInfo.map_terrain_type_count;
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

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["BattleBG"] = $"0x{BattleBG:X02}",
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
