using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageUnitPaletteViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 16;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _id0, _id1, _id2, _id3, _id4, _id5, _id6, _id7, _id8, _id9, _id10, _id11;
        uint _palettePointer;
        string _identifierName = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // B0-B11: Identifier string bytes (12 chars)
        public uint Id0 { get => _id0; set => SetField(ref _id0, value); }
        public uint Id1 { get => _id1; set => SetField(ref _id1, value); }
        public uint Id2 { get => _id2; set => SetField(ref _id2, value); }
        public uint Id3 { get => _id3; set => SetField(ref _id3, value); }
        public uint Id4 { get => _id4; set => SetField(ref _id4, value); }
        public uint Id5 { get => _id5; set => SetField(ref _id5, value); }
        public uint Id6 { get => _id6; set => SetField(ref _id6, value); }
        public uint Id7 { get => _id7; set => SetField(ref _id7, value); }
        public uint Id8 { get => _id8; set => SetField(ref _id8, value); }
        public uint Id9 { get => _id9; set => SetField(ref _id9, value); }
        public uint Id10 { get => _id10; set => SetField(ref _id10, value); }
        public uint Id11 { get => _id11; set => SetField(ref _id11, value); }

        // Decoded identifier name from B0-B11
        public string IdentifierName { get => _identifierName; set => SetField(ref _identifierName, value); }

        // P12: Palette data pointer
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Unit Palette Editor", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            Id0 = rom.u8(addr + 0);
            Id1 = rom.u8(addr + 1);
            Id2 = rom.u8(addr + 2);
            Id3 = rom.u8(addr + 3);
            Id4 = rom.u8(addr + 4);
            Id5 = rom.u8(addr + 5);
            Id6 = rom.u8(addr + 6);
            Id7 = rom.u8(addr + 7);
            Id8 = rom.u8(addr + 8);
            Id9 = rom.u8(addr + 9);
            Id10 = rom.u8(addr + 10);
            Id11 = rom.u8(addr + 11);
            PalettePointer = rom.u32(addr + 12);

            // Decode identifier as ASCII string
            var chars = new char[12];
            for (int i = 0; i < 12; i++)
            {
                uint b = rom.u8(addr + (uint)i);
                chars[i] = (b >= 0x20 && b < 0x7F) ? (char)b : '.';
            }
            IdentifierName = new string(chars).TrimEnd();

            IsLoaded = true;
            CanWrite = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, Id0);
            rom.write_u8(addr + 1, Id1);
            rom.write_u8(addr + 2, Id2);
            rom.write_u8(addr + 3, Id3);
            rom.write_u8(addr + 4, Id4);
            rom.write_u8(addr + 5, Id5);
            rom.write_u8(addr + 6, Id6);
            rom.write_u8(addr + 7, Id7);
            rom.write_u8(addr + 8, Id8);
            rom.write_u8(addr + 9, Id9);
            rom.write_u8(addr + 10, Id10);
            rom.write_u8(addr + 11, Id11);
            rom.write_u32(addr + 12, PalettePointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Identifier"] = IdentifierName,
                ["Id0"] = $"0x{Id0:X02}",
                ["Id1"] = $"0x{Id1:X02}",
                ["Id2"] = $"0x{Id2:X02}",
                ["Id3"] = $"0x{Id3:X02}",
                ["Id4"] = $"0x{Id4:X02}",
                ["Id5"] = $"0x{Id5:X02}",
                ["Id6"] = $"0x{Id6:X02}",
                ["Id7"] = $"0x{Id7:X02}",
                ["Id8"] = $"0x{Id8:X02}",
                ["Id9"] = $"0x{Id9:X02}",
                ["Id10"] = $"0x{Id10:X02}",
                ["Id11"] = $"0x{Id11:X02}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
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
                ["u8@0"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@1"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@3"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@4"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@5"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@6"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@7"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@8"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@9"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@10"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@11"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@12"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
