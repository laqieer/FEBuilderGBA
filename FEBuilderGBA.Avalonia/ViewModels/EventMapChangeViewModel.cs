using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventMapChangeViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "D8" });

        uint _currentAddr;
        bool _isLoaded;
        uint _b0;
        uint _b1;
        uint _b2;
        uint _b3;
        uint _b4;
        uint _b5;
        uint _b6;
        uint _b7;
        uint _p8;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }

        public List<AddrResult> LoadList() => LoadEventMapChangeList();
        public void LoadEntry(uint addr) => LoadEventMapChange(addr);

        public List<AddrResult> LoadEventMapChangeList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // EventMapChangeForm loads from map settings: each map has a PLIST for map change data.
            // Walk map settings to find the first map with a non-zero map change PLIST,
            // then resolve the PLIST to a ROM address.
            uint mapPtr = rom.RomInfo.map_setting_pointer;
            uint mapDataSize = rom.RomInfo.map_setting_datasize;
            uint mapChangePtr = rom.RomInfo.map_mapchange_pointer;
            if (mapPtr == 0 || mapDataSize == 0 || mapChangePtr == 0)
                return new List<AddrResult>();

            uint mapBase = rom.p32(mapPtr);
            if (!U.isSafetyOffset(mapBase)) return new List<AddrResult>();

            // Walk map change PLIST pointer table to find the first valid entry
            uint plistBase = rom.p32(mapChangePtr);
            if (!U.isSafetyOffset(plistBase)) return new List<AddrResult>();

            uint romLen = (uint)rom.Data.Length;

            // Map settings have the map change PLIST at offset 11 (byte)
            for (int mapId = 0; mapId < 256; mapId++)
            {
                uint mapAddr = (uint)(mapBase + mapId * mapDataSize);
                if (mapAddr + mapDataSize > romLen) break;

                uint plist = rom.u8(mapAddr + 11);
                if (plist == 0 || plist == 0xFF) continue;

                // Resolve PLIST: read the pointer at plistBase + plist * 4
                uint plistEntryAddr = (uint)(plistBase + plist * 4);
                if (plistEntryAddr + 4 > romLen) continue;

                uint changeAddr = rom.p32(plistEntryAddr);
                if (!U.isSafetyOffset(changeAddr) || changeAddr + 12 > romLen) continue;

                // Validate: first byte should not be 0xFF (end marker)
                if (rom.u8(changeAddr) == 0xFF) continue;

                LoadEventMapChange(changeAddr);
                return new List<AddrResult> { new AddrResult(changeAddr, $"Map {mapId} Change 0", 0) };
            }

            return new List<AddrResult>();
        }

        public void LoadEventMapChange(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 12 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            B0 = values["B0"];
            B1 = values["B1"];
            B2 = values["B2"];
            B3 = values["B3"];
            B4 = values["B4"];
            B5 = values["B5"];
            B6 = values["B6"];
            B7 = values["B7"];
            P8 = values["D8"];
            IsLoaded = true;
        }

        public int GetListCount() => IsLoaded && CurrentAddr != 0 ? 1 : 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0"] = $"0x{B0:X02}",
                ["B1"] = $"0x{B1:X02}",
                ["B2"] = $"0x{B2:X02}",
                ["B3"] = $"0x{B3:X02}",
                ["B4"] = $"0x{B4:X02}",
                ["B5"] = $"0x{B5:X02}",
                ["B6"] = $"0x{B6:X02}",
                ["B7"] = $"0x{B7:X02}",
                ["P8"] = $"0x{P8:X08}",
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["B0"] = "u8@0x00",
            ["B1"] = "u8@0x01",
            ["B2"] = "u8@0x02",
            ["B3"] = "u8@0x03",
            ["B4"] = "u8@0x04",
            ["B5"] = "u8@0x05",
            ["B6"] = "u8@0x06",
            ["B7"] = "u8@0x07",
            ["P8"] = "u32@0x08",
        };
    }
}
