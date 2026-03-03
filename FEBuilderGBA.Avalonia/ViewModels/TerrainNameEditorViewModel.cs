using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class TerrainNameEditorViewModel : ViewModelBase
    {
        uint _currentAddr;
        string _terrainName = "";
        uint _textId;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string TerrainName { get => _terrainName; set => SetField(ref _terrainName, value); }
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadTerrainNameList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_terrain_name_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 2); // Each entry is a u16 text ID
                if (addr + 1 >= (uint)rom.Data.Length) break;

                uint textId = rom.u16(addr);
                string decoded;
                try { decoded = FETextDecode.Direct(textId); }
                catch { decoded = "???"; }

                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadTerrainName(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 1 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            TextId = rom.u16(addr);
            try { TerrainName = FETextDecode.Direct(TextId); }
            catch { TerrainName = "???"; }
            CanWrite = true;
        }

        public void WriteTerrainName()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            rom.write_u16(CurrentAddr, TextId);
        }
    }
}
