using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Support Unit Editor for FE7/FE8.
    /// Block size = 24 bytes.  Layout (all u8):
    ///   B0-B6   : Partner unit IDs (7 slots)
    ///   B7-B13  : Initial support values
    ///   B14-B20 : Support growth rates
    ///   B21     : Support partner count
    ///   B22-B23 : Reserved / padding
    /// </summary>
    public class SupportUnitEditorViewModel : ViewModelBase, IDataVerifiable
    {
        const uint BLOCK_SIZE = 24;

        uint _currentAddr;
        bool _canWrite;

        // B0-B6: Partner unit IDs
        uint _b0, _b1, _b2, _b3, _b4, _b5, _b6;
        // B7-B13: Initial values
        uint _b7, _b8, _b9, _b10, _b11, _b12, _b13;
        // B14-B20: Growth rates
        uint _b14, _b15, _b16, _b17, _b18, _b19, _b20;
        // B21: Partner count, B22-B23: reserved
        uint _b21, _b22, _b23;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // Partner unit IDs
        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }

        // Initial values
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }

        // Growth rates
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        public uint B18 { get => _b18; set => SetField(ref _b18, value); }
        public uint B19 { get => _b19; set => SetField(ref _b19, value); }
        public uint B20 { get => _b20; set => SetField(ref _b20, value); }

        // Partner count + reserved
        public uint B21 { get => _b21; set => SetField(ref _b21, value); }
        public uint B22 { get => _b22; set => SetField(ref _b22, value); }
        public uint B23 { get => _b23; set => SetField(ref _b23, value); }

        public List<AddrResult> LoadSupportUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr;
            if (ptr >= 0x08000000)
                baseAddr = ptr - 0x08000000;
            else
            {
                baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();
            }

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * BLOCK_SIZE);
                if (addr + BLOCK_SIZE > (uint)rom.Data.Length) break;

                uint firstWord = rom.u16(addr);
                if (firstWord == 0 && i > 0)
                {
                    bool hasMore = false;
                    for (uint j = 1; j <= 4 && (i + j) < 0x100; j++)
                    {
                        uint checkAddr = (uint)(baseAddr + (i + j) * BLOCK_SIZE);
                        if (checkAddr + BLOCK_SIZE > (uint)rom.Data.Length) break;
                        if (rom.u16(checkAddr) != 0) { hasMore = true; break; }
                    }
                    if (!hasMore) break;
                }

                string name = U.ToHexString(i) + " Entry " + U.ToHexString(i);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSupportUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + BLOCK_SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            B0  = rom.u8(addr + 0);
            B1  = rom.u8(addr + 1);
            B2  = rom.u8(addr + 2);
            B3  = rom.u8(addr + 3);
            B4  = rom.u8(addr + 4);
            B5  = rom.u8(addr + 5);
            B6  = rom.u8(addr + 6);

            B7  = rom.u8(addr + 7);
            B8  = rom.u8(addr + 8);
            B9  = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            B12 = rom.u8(addr + 12);
            B13 = rom.u8(addr + 13);

            B14 = rom.u8(addr + 14);
            B15 = rom.u8(addr + 15);
            B16 = rom.u8(addr + 16);
            B17 = rom.u8(addr + 17);
            B18 = rom.u8(addr + 18);
            B19 = rom.u8(addr + 19);
            B20 = rom.u8(addr + 20);

            B21 = rom.u8(addr + 21);
            B22 = rom.u8(addr + 22);
            B23 = rom.u8(addr + 23);

            CanWrite = true;
        }

        public void WriteSupportUnit()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + BLOCK_SIZE > (uint)rom.Data.Length) return;

            rom.write_u8(a + 0,  B0);
            rom.write_u8(a + 1,  B1);
            rom.write_u8(a + 2,  B2);
            rom.write_u8(a + 3,  B3);
            rom.write_u8(a + 4,  B4);
            rom.write_u8(a + 5,  B5);
            rom.write_u8(a + 6,  B6);

            rom.write_u8(a + 7,  B7);
            rom.write_u8(a + 8,  B8);
            rom.write_u8(a + 9,  B9);
            rom.write_u8(a + 10, B10);
            rom.write_u8(a + 11, B11);
            rom.write_u8(a + 12, B12);
            rom.write_u8(a + 13, B13);

            rom.write_u8(a + 14, B14);
            rom.write_u8(a + 15, B15);
            rom.write_u8(a + 16, B16);
            rom.write_u8(a + 17, B17);
            rom.write_u8(a + 18, B18);
            rom.write_u8(a + 19, B19);
            rom.write_u8(a + 20, B20);

            rom.write_u8(a + 21, B21);
            rom.write_u8(a + 22, B22);
            rom.write_u8(a + 23, B23);
        }

        public int GetListCount() => LoadSupportUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
            };
            for (int i = 0; i < 24; i++)
            {
                uint val = i switch
                {
                    0 => B0, 1 => B1, 2 => B2, 3 => B3, 4 => B4, 5 => B5, 6 => B6,
                    7 => B7, 8 => B8, 9 => B9, 10 => B10, 11 => B11, 12 => B12, 13 => B13,
                    14 => B14, 15 => B15, 16 => B16, 17 => B17, 18 => B18, 19 => B19, 20 => B20,
                    21 => B21, 22 => B22, _ => B23,
                };
                report[$"B{i}"] = $"0x{val:X02}";
            }
            return report;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
            };
            for (int i = 0; i < 24; i++)
            {
                report[$"u8@0x{i:X02}"] = $"0x{rom.u8(a + (uint)i):X02}";
            }
            return report;
        }
    }
}
