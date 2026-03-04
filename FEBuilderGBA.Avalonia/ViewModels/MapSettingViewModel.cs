using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapSettingViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";
        uint _tilesetPLIST, _mapPLIST, _palettePLIST, _weather;
        uint _chapterNameId, _objType;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public uint TilesetPLIST { get => _tilesetPLIST; set => SetField(ref _tilesetPLIST, value); }
        public uint MapPLIST { get => _mapPLIST; set => SetField(ref _mapPLIST, value); }
        public uint PalettePLIST { get => _palettePLIST; set => SetField(ref _palettePLIST, value); }
        public uint Weather { get => _weather; set => SetField(ref _weather, value); }
        public uint ChapterNameId { get => _chapterNameId; set => SetField(ref _chapterNameId, value); }
        public uint ObjType { get => _objType; set => SetField(ref _objType, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadMapSettingList()
        {
            // Use MapSettingCore if available
            try
            {
                return MapSettingCore.MakeMapIDList();
            }
            catch
            {
                return new List<AddrResult>();
            }
        }

        public void LoadMapSetting(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (dataSize == 0) dataSize = 72;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Map setting struct layout varies by version
            // Common fields for FE7/FE8 style:
            if (rom.RomInfo.version == 6)
            {
                // FE6: simpler struct
                TilesetPLIST = rom.u8(addr + 4);
                MapPLIST = rom.u8(addr + 5);
                PalettePLIST = rom.u8(addr + 10);
                Weather = rom.u8(addr + 12);
                ChapterNameId = rom.u16(addr + 0);
                try { Name = FETextDecode.Direct(ChapterNameId); }
                catch { Name = "???"; }
            }
            else
            {
                // FE7/FE8
                TilesetPLIST = rom.u8(addr + 4);
                MapPLIST = rom.u8(addr + 5);
                PalettePLIST = rom.u8(addr + 10);
                Weather = rom.u8(addr + 12);
                ObjType = rom.u8(addr + 13);
                // Chapter name text ID is at different offset depending on version
                if (dataSize >= 0x74)
                {
                    ChapterNameId = rom.u16(addr + 0x70);
                    try { Name = FETextDecode.Direct(ChapterNameId); }
                    catch { Name = "???"; }
                }
                else
                {
                    Name = $"Map 0x{addr:X}";
                }
            }

            IsLoaded = true;
        }

        public int GetListCount() => LoadMapSettingList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["TilesetPLIST"] = $"0x{TilesetPLIST:X02}",
                ["MapPLIST"] = $"0x{MapPLIST:X02}",
                ["PalettePLIST"] = $"0x{PalettePLIST:X02}",
                ["Weather"] = $"0x{Weather:X02}",
                ["ChapterNameId"] = $"0x{ChapterNameId:X04}",
                ["ObjType"] = $"0x{ObjType:X02}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
            };
            if (rom.RomInfo.version == 6)
            {
                report["u16@0x00"] = $"0x{rom.u16(a + 0):X04}";
            }
            else
            {
                report["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}";
                uint dataSize = rom.RomInfo.map_setting_datasize;
                if (dataSize >= 0x74)
                    report["u16@0x70"] = $"0x{rom.u16(a + 0x70):X04}";
            }
            return report;
        }
    }
}
