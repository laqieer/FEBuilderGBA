using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class OPClassDemoViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _classId;
        uint _animationType;
        uint _battleAnime;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public uint AnimationType { get => _animationType; set => SetField(ref _animationType, value); }
        public uint BattleAnime { get => _battleAnime; set => SetField(ref _battleAnime, value); }

        public List<AddrResult> LoadOPClassDemoList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.op_class_demo_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;

                // Validity: byte at offset 0x0F should be <= 4
                uint animType = rom.u8(addr + 0x0F);
                if (animType > 4) break;

                uint classId = rom.u8(addr + 14);
                string name = U.ToHexString(classId) + " Class Demo";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadOPClassDemo(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            ClassId = rom.u8(addr + 14);
            AnimationType = rom.u8(addr + 0x0F);
            BattleAnime = rom.u8(addr + 16);
            IsLoaded = true;
        }
    }
}
