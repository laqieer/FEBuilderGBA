using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SupportTalkViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _supportPartner1;  // B0 - Support Partner 1
        uint _supportPartner2;  // B2 - Support Partner 2
        uint _textIdC;          // W4 - C Support Text
        uint _textIdB;          // W6 - B Support Text
        uint _textIdA;          // W8 - A Support Text
        uint _songC;            // W10 - C Support Song
        uint _songB;            // W12 - B Support Song
        uint _songA;            // W14 - A Support Song
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint SupportPartner1 { get => _supportPartner1; set => SetField(ref _supportPartner1, value); }
        public uint SupportPartner2 { get => _supportPartner2; set => SetField(ref _supportPartner2, value); }
        public uint TextIdC { get => _textIdC; set => SetField(ref _textIdC, value); }
        public uint TextIdB { get => _textIdB; set => SetField(ref _textIdB, value); }
        public uint TextIdA { get => _textIdA; set => SetField(ref _textIdA, value); }
        public uint SongC { get => _songC; set => SetField(ref _songC, value); }
        public uint SongB { get => _songB; set => SetField(ref _songB, value); }
        public uint SongA { get => _songA; set => SetField(ref _songA, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadSupportTalkList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.support_talk_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            // Each entry is 16 bytes; stop on 0xFFFF or empty
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 16);
                if (addr + 15 >= (uint)rom.Data.Length) break;

                uint first = rom.u16(addr);
                if (first == 0xFFFF) break;
                if (i > 10 && rom.IsEmpty(addr, 16 * 10)) break;

                uint uid1 = rom.u8(addr + 0);
                uint uid2 = rom.u8(addr + 2);
                string name = U.ToHexString(i) + " Unit 0x" + uid1.ToString("X02") + " & Unit 0x" + uid2.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSupportTalk(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 15 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            SupportPartner1 = rom.u8(addr + 0);
            SupportPartner2 = rom.u8(addr + 2);
            TextIdC = rom.u16(addr + 4);
            TextIdB = rom.u16(addr + 6);
            TextIdA = rom.u16(addr + 8);
            SongC = rom.u16(addr + 10);
            SongB = rom.u16(addr + 12);
            SongA = rom.u16(addr + 14);

            IsLoaded = true;
        }

        public void WriteSupportTalk()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 16 > (uint)rom.Data.Length) return;

            rom.write_u8(a + 0,  SupportPartner1);
            rom.write_u8(a + 2,  SupportPartner2);
            rom.write_u16(a + 4, (uint)TextIdC);
            rom.write_u16(a + 6, (uint)TextIdB);
            rom.write_u16(a + 8, (uint)TextIdA);
            rom.write_u16(a + 10, (uint)SongC);
            rom.write_u16(a + 12, (uint)SongB);
            rom.write_u16(a + 14, (uint)SongA);
        }

        public int GetListCount() => LoadSupportTalkList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SupportPartner1"] = $"0x{SupportPartner1:X02}",
                ["SupportPartner2"] = $"0x{SupportPartner2:X02}",
                ["TextIdC"] = $"0x{TextIdC:X04}",
                ["TextIdB"] = $"0x{TextIdB:X04}",
                ["TextIdA"] = $"0x{TextIdA:X04}",
                ["SongC"] = $"0x{SongC:X04}",
                ["SongB"] = $"0x{SongB:X04}",
                ["SongA"] = $"0x{SongA:X04}",
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
                ["SupportPartner2@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["TextIdC@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["TextIdB@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["TextIdA@0x08"] = $"0x{rom.u16(a + 8):X04}",
                ["SongC@0x0A"] = $"0x{rom.u16(a + 10):X04}",
                ["SongB@0x0C"] = $"0x{rom.u16(a + 12):X04}",
                ["SongA@0x0E"] = $"0x{rom.u16(a + 14):X04}",
            };
        }
    }
}
