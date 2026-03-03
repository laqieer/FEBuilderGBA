using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SupportTalkViewModel : ViewModelBase
    {
        uint _currentAddr;
        uint _unitId1;
        uint _unitId2;
        uint _textIdC;
        uint _textIdB;
        uint _textIdA;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint UnitId1 { get => _unitId1; set => SetField(ref _unitId1, value); }
        public uint UnitId2 { get => _unitId2; set => SetField(ref _unitId2, value); }
        public uint TextIdC { get => _textIdC; set => SetField(ref _textIdC, value); }
        public uint TextIdB { get => _textIdB; set => SetField(ref _textIdB, value); }
        public uint TextIdA { get => _textIdA; set => SetField(ref _textIdA, value); }
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

            UnitId1 = rom.u8(addr + 0);
            UnitId2 = rom.u8(addr + 2);
            TextIdC = rom.u16(addr + 4);
            TextIdB = rom.u16(addr + 6);
            TextIdA = rom.u16(addr + 8);

            IsLoaded = true;
        }
    }
}
