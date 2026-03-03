using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapSettingViewModel : ViewModelBase
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
    }
}
