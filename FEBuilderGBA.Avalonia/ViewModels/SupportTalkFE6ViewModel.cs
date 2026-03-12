using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SupportTalkFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _supportPartner1, _supportPartner2;      // B0, B1
        uint _textC, _textB, _textA;                   // W4, W8, W12 (u16)
        uint _padding1, _padding2;                     // B14, B15

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint SupportPartner1 { get => _supportPartner1; set => SetField(ref _supportPartner1, value); }
        public uint SupportPartner2 { get => _supportPartner2; set => SetField(ref _supportPartner2, value); }
        public uint TextC { get => _textC; set => SetField(ref _textC, value); }
        public uint TextB { get => _textB; set => SetField(ref _textB, value); }
        public uint TextA { get => _textA; set => SetField(ref _textA, value); }
        public uint Padding1 { get => _padding1; set => SetField(ref _padding1, value); }
        public uint Padding2 { get => _padding2; set => SetField(ref _padding2, value); }

        public List<AddrResult> LoadSupportTalkList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = 16;
            var result = new List<AddrResult>();
            int emptyCount = 0;
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                uint first = rom.u16(addr);
                if (first == 0)
                {
                    emptyCount++;
                    if (emptyCount >= 10) break;
                    continue;
                }
                emptyCount = 0;

                uint uid1 = rom.u8(addr + 0);
                uint uid2 = rom.u8(addr + 1);
                string n1 = NameResolver.GetUnitName(uid1);
                string n2 = NameResolver.GetUnitName(uid2);
                string name = $"{U.ToHexString(i)} {n1} (0x{uid1:X02}) & {n2} (0x{uid2:X02})";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSupportTalk(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 16 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            SupportPartner1 = rom.u8(addr + 0);
            SupportPartner2 = rom.u8(addr + 1);
            TextC = rom.u16(addr + 4);
            TextB = rom.u16(addr + 8);
            TextA = rom.u16(addr + 12);
            Padding1 = rom.u8(addr + 14);
            Padding2 = rom.u8(addr + 15);

            IsLoaded = true;
        }

        public void WriteSupportTalk()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 16 > (uint)rom.Data.Length) return;

            rom.write_u8(a + 0,  SupportPartner1);
            rom.write_u8(a + 1,  SupportPartner2);
            rom.write_u16(a + 4, (uint)TextC);
            rom.write_u16(a + 8, (uint)TextB);
            rom.write_u16(a + 12, (uint)TextA);
            rom.write_u8(a + 14, Padding1);
            rom.write_u8(a + 15, Padding2);
        }

        public int GetListCount() => LoadSupportTalkList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SupportPartner1"] = $"0x{SupportPartner1:X02}",
                ["SupportPartner2"] = $"0x{SupportPartner2:X02}",
                ["TextC"] = $"0x{TextC:X04}",
                ["TextB"] = $"0x{TextB:X04}",
                ["TextA"] = $"0x{TextA:X04}",
                ["Padding1"] = $"0x{Padding1:X02}",
                ["Padding2"] = $"0x{Padding2:X02}",
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
                ["SupportPartner1@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["SupportPartner2@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["TextC@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["TextB@0x08"] = $"0x{rom.u16(a + 8):X04}",
                ["TextA@0x0C"] = $"0x{rom.u16(a + 12):X04}",
                ["Padding1@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["Padding2@0x0F"] = $"0x{rom.u8(a + 15):X02}",
            };
        }
    }
}
