using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventCondViewModel : ViewModelBase
    {
        uint _currentAddr;
        uint _mapDataSize;
        string _rawBytes = "";
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint MapDataSize { get => _mapDataSize; set => SetField(ref _mapDataSize, value); }
        public string RawBytes { get => _rawBytes; set => SetField(ref _rawBytes, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadEventCondList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Use MapSettingCore to get the map list for navigation
            var mapList = MapSettingCore.MakeMapIDList();
            return mapList;
        }

        public void LoadEventCond(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;

            uint dataSize = rom.RomInfo.map_setting_datasize;
            MapDataSize = dataSize;

            // Read raw bytes of the map setting entry for display
            uint bytesToRead = Math.Min(dataSize, 32); // Show up to 32 bytes
            if (addr + bytesToRead > (uint)rom.Data.Length)
            {
                bytesToRead = (uint)rom.Data.Length - addr;
            }

            var sb = new System.Text.StringBuilder();
            for (uint i = 0; i < bytesToRead; i++)
            {
                if (i > 0 && i % 16 == 0)
                    sb.Append("\n");
                else if (i > 0)
                    sb.Append(" ");
                sb.Append(rom.u8(addr + i).ToString("X02"));
            }
            RawBytes = sb.ToString();

            IsLoaded = true;
        }
    }
}
