using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapMiniMapTerrainImageViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        /// <summary>
        /// Load the list of minimap terrain entries from map_minimap_tile_array_pointer.
        /// Each entry is 4 bytes (a pointer/DWORD), total count = map_terrain_type_count.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_minimap_tile_array_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            int count = (int)rom.RomInfo.map_terrain_type_count;
            const uint entrySize = 4;
            var result = new List<AddrResult>();
            for (int i = 0; i < count; i++)
            {
                uint addr = (uint)(baseAddr + i * entrySize);
                if (addr + entrySize > (uint)rom.Data.Length) break;
                result.Add(new AddrResult(addr, $"0x{i:X02} Terrain {i}", (uint)i));
            }
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _p0;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            P0 = values["D0"];
            IsLoaded = true;
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
                ["P0"] = $"0x{P0:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["P0"] = "u32@0x00",
        };
    }
}
