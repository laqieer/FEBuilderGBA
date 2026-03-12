using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MenuExtendSplitMenuViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "D4", "D8", "D12", "D16", "D20", "D24", "D28", "D32", "D36" });

        uint _currentAddr;
        bool _isLoaded;
        uint _posX, _posY, _width, _style;
        uint _str1, _str2, _str3, _str4, _str5, _str6, _str7, _str8;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint PosX { get => _posX; set => SetField(ref _posX, value); }
        public uint PosY { get => _posY; set => SetField(ref _posY, value); }
        public uint Width { get => _width; set => SetField(ref _width, value); }
        public uint Style { get => _style; set => SetField(ref _style, value); }
        public uint String0 { get => _str1; set => SetField(ref _str1, value); }
        public uint String1 { get => _str2; set => SetField(ref _str2, value); }
        public uint String2 { get => _str3; set => SetField(ref _str3, value); }
        public uint String3 { get => _str4; set => SetField(ref _str4, value); }
        public uint String4 { get => _str5; set => SetField(ref _str5, value); }
        public uint String5 { get => _str6; set => SetField(ref _str6, value); }
        public uint String6 { get => _str7; set => SetField(ref _str7, value); }
        public uint String7 { get => _str8; set => SetField(ref _str8, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.menu_definiton_split_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 40;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 32; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                // Stop if first string pointer is null
                uint style = rom.u32(addr + 4);
                if (style == 0 && i > 0) break;

                result.Add(new AddrResult(addr, $"0x{i:X02} Split Menu {i}", i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 40 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            PosX = values["B0"];
            PosY = values["B1"];
            Width = values["B2"];
            // byte 3 is padding
            Style = values["D4"];
            String0 = values["D8"];
            String1 = values["D12"];
            String2 = values["D16"];
            String3 = values["D20"];
            String4 = values["D24"];
            String5 = values["D28"];
            String6 = values["D32"];
            String7 = values["D36"];

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 40 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = PosX, ["B1"] = PosY, ["B2"] = Width,
                ["D4"] = Style,
                ["D8"] = String0, ["D12"] = String1, ["D16"] = String2, ["D20"] = String3,
                ["D24"] = String4, ["D28"] = String5, ["D32"] = String6, ["D36"] = String7,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PosX"] = $"0x{PosX:X02}",
                ["PosY"] = $"0x{PosY:X02}",
                ["Width"] = $"0x{Width:X02}",
                ["Style"] = $"0x{Style:X08}",
                ["String0"] = $"0x{String0:X08}",
                ["String1"] = $"0x{String1:X08}",
                ["String2"] = $"0x{String2:X08}",
                ["String3"] = $"0x{String3:X08}",
                ["String4"] = $"0x{String4:X08}",
                ["String5"] = $"0x{String5:X08}",
                ["String6"] = $"0x{String6:X08}",
                ["String7"] = $"0x{String7:X08}",
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
                ["u8@0x00_PosX"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_PosY"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Width"] = $"0x{rom.u8(a + 2):X02}",
                ["u32@0x04_Style"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08_String0"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C_String1"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10_String2"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14_String3"] = $"0x{rom.u32(a + 20):X08}",
                ["u32@0x18_String4"] = $"0x{rom.u32(a + 24):X08}",
                ["u32@0x1C_String5"] = $"0x{rom.u32(a + 28):X08}",
                ["u32@0x20_String6"] = $"0x{rom.u32(a + 32):X08}",
                ["u32@0x24_String7"] = $"0x{rom.u32(a + 36):X08}",
            };
        }
    }
}
