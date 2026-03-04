using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SupportTalkFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _unit1Id, _unit2Id;
        uint _textC, _textB, _textA;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint Unit1Id { get => _unit1Id; set => SetField(ref _unit1Id, value); }
        public uint Unit2Id { get => _unit2Id; set => SetField(ref _unit2Id, value); }
        public uint TextC { get => _textC; set => SetField(ref _textC, value); }
        public uint TextB { get => _textB; set => SetField(ref _textB, value); }
        public uint TextA { get => _textA; set => SetField(ref _textA, value); }

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
                string name = U.ToHexString(i) + " Unit 0x" + uid1.ToString("X02") + " & Unit 0x" + uid2.ToString("X02");
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
            Unit1Id = rom.u8(addr + 0);
            Unit2Id = rom.u8(addr + 1);
            TextC = rom.u32(addr + 4);
            TextB = rom.u32(addr + 8);
            TextA = rom.u32(addr + 12);

            IsLoaded = true;
        }

        public int GetListCount() => LoadSupportTalkList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Unit1Id"] = $"0x{Unit1Id:X02}",
                ["Unit2Id"] = $"0x{Unit2Id:X02}",
                ["TextC"] = $"0x{TextC:X08}",
                ["TextB"] = $"0x{TextB:X08}",
                ["TextA"] = $"0x{TextA:X08}",
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
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
