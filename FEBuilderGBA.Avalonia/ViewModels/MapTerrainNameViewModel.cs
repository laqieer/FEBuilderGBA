using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapTerrainNameViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _terrainNamePointer;
        string _terrainNameText = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint TerrainNamePointer { get => _terrainNamePointer; set => SetField(ref _terrainNamePointer, value); }
        public string TerrainNameText { get => _terrainNameText; set => SetField(ref _terrainNameText, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.map_terrain_name_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            // Multibyte (Japanese) ROMs: 4-byte pointer entries
            const uint blockSize = 4;
            uint maxCount = rom.RomInfo.map_terrain_type_count;
            if (maxCount == 0) maxCount = 65;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint ptr = rom.u32(addr);
                string name;
                if (U.isPointer(ptr))
                {
                    name = $"Terrain {i}";
                }
                else
                {
                    name = $"(empty)";
                }
                result.Add(new AddrResult(addr, $"0x{i:X2} {name}", i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            TerrainNamePointer = rom.u32(addr);

            if (U.isPointer(TerrainNamePointer))
            {
                uint strAddr = U.toOffset(TerrainNamePointer);
                if (U.isSafetyOffset(strAddr, rom))
                {
                    TerrainNameText = $"-> 0x{strAddr:X08}";
                }
                else
                {
                    TerrainNameText = "(invalid pointer)";
                }
            }
            else if (TerrainNamePointer == 0)
            {
                TerrainNameText = "(null)";
            }
            else
            {
                TerrainNameText = "(not a pointer)";
            }

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u32(CurrentAddr, TerrainNamePointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TerrainNamePointer"] = $"0x{TerrainNamePointer:X08}",
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
                ["TerrainNamePointer@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }
    }
}
