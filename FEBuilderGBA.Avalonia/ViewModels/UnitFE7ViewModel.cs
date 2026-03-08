using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        string _name = "";

        // W# = u16 at offset #
        uint _w0, _w2, _w6;

        // B# = u8 at offset #
        uint _b4, _b5, _b8, _b9, _b10, _b11;
        uint _b20, _b21, _b22, _b23, _b24, _b25, _b26, _b27;
        uint _b28, _b29, _b30, _b31, _b32, _b33, _b34;
        uint _b35, _b36, _b37, _b38, _b39, _b40, _b41, _b42, _b43;
        uint _b48, _b49, _b50, _b51;

        // P# = u32 pointer at offset #
        uint _p44;

        // b# (lowercase) = signed byte at offset #
        int _sb12, _sb13, _sb14, _sb15, _sb16, _sb17, _sb18, _sb19;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }

        // W0: Name text ID (u16 @ offset 0)
        public uint NameId { get => _w0; set => SetField(ref _w0, value); }
        // W2: Description text ID (u16 @ offset 2)
        public uint W2 { get => _w2; set => SetField(ref _w2, value); }
        // W6: u16 @ offset 6
        public uint W6 { get => _w6; set => SetField(ref _w6, value); }

        // B4: u8 @ offset 4
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        // B5: u8 @ offset 5
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        // B8: u8 @ offset 8
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        // B9: u8 @ offset 9
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        // B10: u8 @ offset 10
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        // B11: Level (u8 @ offset 11)
        public uint Level { get => _b11; set => SetField(ref _b11, value); }

        // b12-b19: signed bytes (base stats / con / etc.)
        public int HP { get => _sb12; set => SetField(ref _sb12, value); }
        public int Str { get => _sb13; set => SetField(ref _sb13, value); }
        public int Skl { get => _sb14; set => SetField(ref _sb14, value); }
        public int Spd { get => _sb15; set => SetField(ref _sb15, value); }
        public int Def { get => _sb16; set => SetField(ref _sb16, value); }
        public int Res { get => _sb17; set => SetField(ref _sb17, value); }
        public int Lck { get => _sb18; set => SetField(ref _sb18, value); }
        public int B19Signed { get => _sb19; set => SetField(ref _sb19, value); }

        // B20-B27: u8 @ offsets 20-27
        public uint B20 { get => _b20; set => SetField(ref _b20, value); }
        public uint B21 { get => _b21; set => SetField(ref _b21, value); }
        public uint B22 { get => _b22; set => SetField(ref _b22, value); }
        public uint B23 { get => _b23; set => SetField(ref _b23, value); }
        public uint B24 { get => _b24; set => SetField(ref _b24, value); }
        public uint B25 { get => _b25; set => SetField(ref _b25, value); }
        public uint B26 { get => _b26; set => SetField(ref _b26, value); }
        public uint B27 { get => _b27; set => SetField(ref _b27, value); }

        // B28-B34: Growth rates (u8 @ offsets 28-34)
        public uint GrowHP { get => _b28; set => SetField(ref _b28, value); }
        public uint GrowSTR { get => _b29; set => SetField(ref _b29, value); }
        public uint GrowSKL { get => _b30; set => SetField(ref _b30, value); }
        public uint GrowSPD { get => _b31; set => SetField(ref _b31, value); }
        public uint GrowDEF { get => _b32; set => SetField(ref _b32, value); }
        public uint GrowRES { get => _b33; set => SetField(ref _b33, value); }
        public uint GrowLCK { get => _b34; set => SetField(ref _b34, value); }

        // B35-B43: u8 @ offsets 35-43
        public uint B35 { get => _b35; set => SetField(ref _b35, value); }
        public uint B36 { get => _b36; set => SetField(ref _b36, value); }
        public uint B37 { get => _b37; set => SetField(ref _b37, value); }
        public uint B38 { get => _b38; set => SetField(ref _b38, value); }
        public uint B39 { get => _b39; set => SetField(ref _b39, value); }
        public uint B40 { get => _b40; set => SetField(ref _b40, value); }
        public uint B41 { get => _b41; set => SetField(ref _b41, value); }
        public uint B42 { get => _b42; set => SetField(ref _b42, value); }
        public uint B43 { get => _b43; set => SetField(ref _b43, value); }

        // P44: pointer (u32 @ offset 44)
        public uint P44 { get => _p44; set => SetField(ref _p44, value); }

        // B48-B51: u8 @ offsets 48-51
        public uint B48 { get => _b48; set => SetField(ref _b48, value); }
        public uint B49 { get => _b49; set => SetField(ref _b49, value); }
        public uint B50 { get => _b50; set => SetField(ref _b50, value); }
        public uint B51 { get => _b51; set => SetField(ref _b51, value); }

        public List<AddrResult> LoadUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.unit_datasize;
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 253;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = FETextDecode.Direct(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.unit_datasize;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // W# fields (u16)
            NameId = rom.u16(addr + 0);
            W2 = rom.u16(addr + 2);
            W6 = rom.u16(addr + 6);

            try { Name = FETextDecode.Direct(NameId); }
            catch { Name = "???"; }

            // B# fields (u8)
            B4 = rom.u8(addr + 4);
            B5 = rom.u8(addr + 5);
            B8 = rom.u8(addr + 8);
            B9 = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            Level = rom.u8(addr + 11);

            // b# fields (signed byte)
            HP = (sbyte)rom.u8(addr + 12);
            Str = (sbyte)rom.u8(addr + 13);
            Skl = (sbyte)rom.u8(addr + 14);
            Spd = (sbyte)rom.u8(addr + 15);
            Def = (sbyte)rom.u8(addr + 16);
            Res = (sbyte)rom.u8(addr + 17);
            Lck = (sbyte)rom.u8(addr + 18);
            B19Signed = (sbyte)rom.u8(addr + 19);

            // B20-B27 (u8)
            B20 = rom.u8(addr + 20);
            B21 = rom.u8(addr + 21);
            B22 = rom.u8(addr + 22);
            B23 = rom.u8(addr + 23);
            B24 = rom.u8(addr + 24);
            B25 = rom.u8(addr + 25);
            B26 = rom.u8(addr + 26);
            B27 = rom.u8(addr + 27);

            // B28-B34: Growth rates (u8)
            GrowHP = rom.u8(addr + 28);
            GrowSTR = rom.u8(addr + 29);
            GrowSKL = rom.u8(addr + 30);
            GrowSPD = rom.u8(addr + 31);
            GrowDEF = rom.u8(addr + 32);
            GrowRES = rom.u8(addr + 33);
            GrowLCK = rom.u8(addr + 34);

            // B35-B43 (u8)
            B35 = rom.u8(addr + 35);
            B36 = rom.u8(addr + 36);
            B37 = rom.u8(addr + 37);
            B38 = rom.u8(addr + 38);
            B39 = rom.u8(addr + 39);
            B40 = rom.u8(addr + 40);
            B41 = rom.u8(addr + 41);
            B42 = rom.u8(addr + 42);
            B43 = rom.u8(addr + 43);

            // P44: pointer (u32)
            P44 = rom.u32(addr + 44);

            // B48-B51 (u8)
            B48 = rom.u8(addr + 48);
            B49 = rom.u8(addr + 49);
            B50 = rom.u8(addr + 50);
            B51 = rom.u8(addr + 51);

            CanWrite = true;
        }

        public void WriteUnit()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            uint dataSize = rom.RomInfo.unit_datasize;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            rom.write_u16(addr + 0, (ushort)NameId);
            rom.write_u16(addr + 2, (ushort)W2);
            rom.write_u8(addr + 4, (byte)B4);
            rom.write_u8(addr + 5, (byte)B5);
            rom.write_u16(addr + 6, (ushort)W6);
            rom.write_u8(addr + 8, (byte)B8);
            rom.write_u8(addr + 9, (byte)B9);
            rom.write_u8(addr + 10, (byte)B10);
            rom.write_u8(addr + 11, (byte)Level);
            rom.write_u8(addr + 12, (byte)(sbyte)HP);
            rom.write_u8(addr + 13, (byte)(sbyte)Str);
            rom.write_u8(addr + 14, (byte)(sbyte)Skl);
            rom.write_u8(addr + 15, (byte)(sbyte)Spd);
            rom.write_u8(addr + 16, (byte)(sbyte)Def);
            rom.write_u8(addr + 17, (byte)(sbyte)Res);
            rom.write_u8(addr + 18, (byte)(sbyte)Lck);
            rom.write_u8(addr + 19, (byte)(sbyte)B19Signed);
            rom.write_u8(addr + 20, (byte)B20);
            rom.write_u8(addr + 21, (byte)B21);
            rom.write_u8(addr + 22, (byte)B22);
            rom.write_u8(addr + 23, (byte)B23);
            rom.write_u8(addr + 24, (byte)B24);
            rom.write_u8(addr + 25, (byte)B25);
            rom.write_u8(addr + 26, (byte)B26);
            rom.write_u8(addr + 27, (byte)B27);
            rom.write_u8(addr + 28, (byte)GrowHP);
            rom.write_u8(addr + 29, (byte)GrowSTR);
            rom.write_u8(addr + 30, (byte)GrowSKL);
            rom.write_u8(addr + 31, (byte)GrowSPD);
            rom.write_u8(addr + 32, (byte)GrowDEF);
            rom.write_u8(addr + 33, (byte)GrowRES);
            rom.write_u8(addr + 34, (byte)GrowLCK);
            rom.write_u8(addr + 35, (byte)B35);
            rom.write_u8(addr + 36, (byte)B36);
            rom.write_u8(addr + 37, (byte)B37);
            rom.write_u8(addr + 38, (byte)B38);
            rom.write_u8(addr + 39, (byte)B39);
            rom.write_u8(addr + 40, (byte)B40);
            rom.write_u8(addr + 41, (byte)B41);
            rom.write_u8(addr + 42, (byte)B42);
            rom.write_u8(addr + 43, (byte)B43);
            rom.write_u32(addr + 44, P44);
            rom.write_u8(addr + 48, (byte)B48);
            rom.write_u8(addr + 49, (byte)B49);
            rom.write_u8(addr + 50, (byte)B50);
            rom.write_u8(addr + 51, (byte)B51);
        }

        public int GetListCount() => LoadUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["W0_NameId"] = $"0x{NameId:X04}",
                ["W2"] = $"0x{W2:X04}",
                ["W6"] = $"0x{W6:X04}",
                ["B4"] = $"0x{B4:X02}",
                ["B5"] = $"0x{B5:X02}",
                ["B8"] = $"0x{B8:X02}",
                ["B9"] = $"0x{B9:X02}",
                ["B10"] = $"0x{B10:X02}",
                ["B11_Level"] = $"0x{Level:X02}",
                ["b12_HP"] = $"{HP}",
                ["b13_Str"] = $"{Str}",
                ["b14_Skl"] = $"{Skl}",
                ["b15_Spd"] = $"{Spd}",
                ["b16_Def"] = $"{Def}",
                ["b17_Res"] = $"{Res}",
                ["b18_Lck"] = $"{Lck}",
                ["b19"] = $"{B19Signed}",
                ["B20"] = $"0x{B20:X02}",
                ["B21"] = $"0x{B21:X02}",
                ["B22"] = $"0x{B22:X02}",
                ["B23"] = $"0x{B23:X02}",
                ["B24"] = $"0x{B24:X02}",
                ["B25"] = $"0x{B25:X02}",
                ["B26"] = $"0x{B26:X02}",
                ["B27"] = $"0x{B27:X02}",
                ["B28_GrowHP"] = $"0x{GrowHP:X02}",
                ["B29_GrowSTR"] = $"0x{GrowSTR:X02}",
                ["B30_GrowSKL"] = $"0x{GrowSKL:X02}",
                ["B31_GrowSPD"] = $"0x{GrowSPD:X02}",
                ["B32_GrowDEF"] = $"0x{GrowDEF:X02}",
                ["B33_GrowRES"] = $"0x{GrowRES:X02}",
                ["B34_GrowLCK"] = $"0x{GrowLCK:X02}",
                ["B35"] = $"0x{B35:X02}",
                ["B36"] = $"0x{B36:X02}",
                ["B37"] = $"0x{B37:X02}",
                ["B38"] = $"0x{B38:X02}",
                ["B39"] = $"0x{B39:X02}",
                ["B40"] = $"0x{B40:X02}",
                ["B41"] = $"0x{B41:X02}",
                ["B42"] = $"0x{B42:X02}",
                ["B43"] = $"0x{B43:X02}",
                ["P44"] = $"0x{P44:X08}",
                ["B48"] = $"0x{B48:X02}",
                ["B49"] = $"0x{B49:X02}",
                ["B50"] = $"0x{B50:X02}",
                ["B51"] = $"0x{B51:X02}",
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
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u16@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["s8@0x0C"] = $"{(sbyte)rom.u8(a + 12)}",
                ["s8@0x0D"] = $"{(sbyte)rom.u8(a + 13)}",
                ["s8@0x0E"] = $"{(sbyte)rom.u8(a + 14)}",
                ["s8@0x0F"] = $"{(sbyte)rom.u8(a + 15)}",
                ["s8@0x10"] = $"{(sbyte)rom.u8(a + 16)}",
                ["s8@0x11"] = $"{(sbyte)rom.u8(a + 17)}",
                ["s8@0x12"] = $"{(sbyte)rom.u8(a + 18)}",
                ["s8@0x13"] = $"{(sbyte)rom.u8(a + 19)}",
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@0x1B"] = $"0x{rom.u8(a + 27):X02}",
                ["u8@0x1C"] = $"0x{rom.u8(a + 28):X02}",
                ["u8@0x1D"] = $"0x{rom.u8(a + 29):X02}",
                ["u8@0x1E"] = $"0x{rom.u8(a + 30):X02}",
                ["u8@0x1F"] = $"0x{rom.u8(a + 31):X02}",
                ["u8@0x20"] = $"0x{rom.u8(a + 32):X02}",
                ["u8@0x21"] = $"0x{rom.u8(a + 33):X02}",
                ["u8@0x22"] = $"0x{rom.u8(a + 34):X02}",
                ["u8@0x23"] = $"0x{rom.u8(a + 35):X02}",
                ["u8@0x24"] = $"0x{rom.u8(a + 36):X02}",
                ["u8@0x25"] = $"0x{rom.u8(a + 37):X02}",
                ["u8@0x26"] = $"0x{rom.u8(a + 38):X02}",
                ["u8@0x27"] = $"0x{rom.u8(a + 39):X02}",
                ["u8@0x28"] = $"0x{rom.u8(a + 40):X02}",
                ["u8@0x29"] = $"0x{rom.u8(a + 41):X02}",
                ["u8@0x2A"] = $"0x{rom.u8(a + 42):X02}",
                ["u8@0x2B"] = $"0x{rom.u8(a + 43):X02}",
                ["u32@0x2C"] = $"0x{rom.u32(a + 44):X08}",
                ["u8@0x30"] = $"0x{rom.u8(a + 48):X02}",
                ["u8@0x31"] = $"0x{rom.u8(a + 49):X02}",
                ["u8@0x32"] = $"0x{rom.u8(a + 50):X02}",
                ["u8@0x33"] = $"0x{rom.u8(a + 51):X02}",
            };
        }
    }
}
