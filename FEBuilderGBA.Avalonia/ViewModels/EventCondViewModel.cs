using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventCondViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _mapDataSize;
        string _rawBytes = "";
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint MapDataSize { get => _mapDataSize; set => SetField(ref _mapDataSize, value); }
        public string RawBytes { get => _rawBytes; set => SetField(ref _rawBytes, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

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

            CanWrite = true;
        }

        public int GetListCount() => LoadEventCondList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["MapDataSize"] = $"0x{MapDataSize:X04}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            uint bytesToRead = Math.Min(MapDataSize, 32);
            if (a + bytesToRead > (uint)rom.Data.Length)
                bytesToRead = (uint)rom.Data.Length - a;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
            };
            for (uint i = 0; i < bytesToRead; i++)
            {
                report[$"u8@0x{i:X02}"] = $"0x{rom.u8(a + i):X02}";
            }
            return report;
        }
    }
}
