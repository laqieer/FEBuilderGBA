using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapTerrainNameEngViewModel : ViewModelBase, IDataVerifiable
    {
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.map_terrain_name_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 2;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint textId = rom.u16(addr);
                if (textId == 0x0000) break;

                string name = NameResolver.GetTextById(textId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {name}", (uint)i));
            }
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _terrainNameTextID;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Terrain name text ID (W0 / J_0_TEXT).</summary>
        public uint TerrainNameTextID { get => _terrainNameTextID; set => SetField(ref _terrainNameTextID, value); }

        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "W0" });

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 2 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            TerrainNameTextID = v["W0"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint> { ["W0"] = TerrainNameTextID };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TerrainNameTextID"] = $"0x{TerrainNameTextID:X04}",
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
                ["TerrainNameTextID@0x00"] = $"0x{rom.u16(a + 0):X04}",
            };
        }
    }
}
