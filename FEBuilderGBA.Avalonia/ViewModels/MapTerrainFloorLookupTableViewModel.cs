using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapTerrainFloorLookupTableViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        bool _isLoaded;
        uint _terrainBattleFloor;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Terrain battle floor index (B0 / J_0_TERRAINBATTLE).</summary>
        public uint TerrainBattleFloor { get => _terrainBattleFloor; set => SetField(ref _terrainBattleFloor, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
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

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TerrainBattleFloor"] = $"0x{TerrainBattleFloor:X02}",
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
    }
}
