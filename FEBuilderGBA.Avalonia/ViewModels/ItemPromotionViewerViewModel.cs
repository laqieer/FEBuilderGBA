using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemPromotionViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        uint _targetClassId;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint TargetClassId { get => _targetClassId; set => SetField(ref _targetClassId, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadItemPromotionList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.item_promotion1_array_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i);
                if (addr >= (uint)rom.Data.Length) break;

                uint classId = rom.u8(addr);
                if (classId == 0x00) break;

                string name = U.ToHexString(i) + " ClassID=0x" + classId.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItemPromotion(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            TargetClassId = rom.u8(addr);

            IsLoaded = true;
        }
    }
}
