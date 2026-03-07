using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MenuCommandViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _p0;
        uint _w4, _w6;
        uint _b8, _b9, _b10, _b11;
        uint _d8;
        uint _p8;
        uint _p12, _p16, _p20, _p24, _p28, _p32;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint W4 { get => _w4; set => SetField(ref _w4, value); }
        public uint W6 { get => _w6; set => SetField(ref _w6, value); }
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint D8 { get => _d8; set => SetField(ref _d8, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        public uint P12 { get => _p12; set => SetField(ref _p12, value); }
        public uint P16 { get => _p16; set => SetField(ref _p16, value); }
        public uint P20 { get => _p20; set => SetField(ref _p20, value); }
        public uint P24 { get => _p24; set => SetField(ref _p24, value); }
        public uint P28 { get => _p28; set => SetField(ref _p28, value); }
        public uint P32 { get => _p32; set => SetField(ref _p32, value); }
        // Legacy aliases
        public uint UsabilityPtr => P12;
        public uint EffectPtr => P16;
        public uint MenuCommandId => W4;

        public List<AddrResult> LoadMenuCommandList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // MenuCommand entries are accessed from MenuDefinition handler pointers.
            // List the well-known MenuCommand function addresses first.
            var result = new List<AddrResult>();

            uint always = rom.RomInfo.MenuCommand_UsabilityAlways;
            if (always != 0)
                result.Add(new AddrResult(always, "0 UsabilityAlways", 0));

            uint never = rom.RomInfo.MenuCommand_UsabilityNever;
            if (never != 0)
                result.Add(new AddrResult(never, "1 UsabilityNever", 1));

            // Also enumerate entries from the primary menu definition table
            uint ptr = rom.RomInfo.menu_definiton_pointer;
            if (ptr != 0)
            {
                uint defBase = rom.p32(ptr);
                if (U.isSafetyOffset(defBase))
                {
                    uint idx = 2;
                    for (uint i = 0; i < 0x100; i++)
                    {
                        uint defAddr = (uint)(defBase + i * 36);
                        if (defAddr + 36 > (uint)rom.Data.Length) break;
                        if (!U.isPointer(rom.u32(defAddr + 8))) break;

                        uint menuCmdPtr = rom.p32(defAddr + 8);
                        if (!U.isSafetyOffset(menuCmdPtr)) continue;

                        // Each menu command entry is 36 bytes, check for pointer at +0xC
                        for (uint j = 0; j < 0x40; j++)
                        {
                            uint cmdAddr = (uint)(menuCmdPtr + j * 36);
                            if (cmdAddr + 36 > (uint)rom.Data.Length) break;
                            if (!U.isPointer(rom.u32(cmdAddr + 0xC))) break;

                            string name = U.ToHexString(idx) + " MenuCmd Def" + i.ToString() + "_" + j.ToString();
                            result.Add(new AddrResult(cmdAddr, name, idx));
                            idx++;
                        }
                    }
                }
            }

            return result;
        }

        public void LoadMenuCommand(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            P0 = rom.u32(addr + 0);
            W4 = rom.u16(addr + 4);
            W6 = rom.u16(addr + 6);
            B8 = rom.u8(addr + 8);
            B9 = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            D8 = rom.u32(addr + 8);
            P8 = rom.u32(addr + 8);
            P12 = rom.u32(addr + 12);
            P16 = rom.u32(addr + 16);
            P20 = rom.u32(addr + 20);
            P24 = rom.u32(addr + 24);
            P28 = rom.u32(addr + 28);
            P32 = rom.u32(addr + 32);
            IsLoaded = true;
        }

        public int GetListCount() => LoadMenuCommandList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
                ["W4"] = $"0x{W4:X04}",
                ["W6"] = $"0x{W6:X04}",
                ["B8"] = $"0x{B8:X02}",
                ["B9"] = $"0x{B9:X02}",
                ["B10"] = $"0x{B10:X02}",
                ["B11"] = $"0x{B11:X02}",
                ["D8"] = $"0x{D8:X08}",
                ["P8"] = $"0x{P8:X08}",
                ["P12"] = $"0x{P12:X08}",
                ["P16"] = $"0x{P16:X08}",
                ["P20"] = $"0x{P20:X08}",
                ["P24"] = $"0x{P24:X08}",
                ["P28"] = $"0x{P28:X08}",
                ["P32"] = $"0x{P32:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14"] = $"0x{rom.u32(a + 20):X08}",
                ["u32@0x18"] = $"0x{rom.u32(a + 24):X08}",
                ["u32@0x1C"] = $"0x{rom.u32(a + 28):X08}",
                ["u32@0x20"] = $"0x{rom.u32(a + 32):X08}",
            };
        }
    }
}
