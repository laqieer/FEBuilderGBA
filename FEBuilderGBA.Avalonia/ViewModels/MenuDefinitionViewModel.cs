using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MenuDefinitionViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _textId;
        uint _handlerPtr;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }
        public uint HandlerPtr { get => _handlerPtr; set => SetField(ref _handlerPtr, value); }

        public List<AddrResult> LoadMenuDefinitionList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.menu_definiton_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 36);
                if (addr + 36 > (uint)rom.Data.Length) break;

                // Termination: offset+8 must be a pointer
                if (!U.isPointer(rom.u32(addr + 8))) break;

                string name = U.ToHexString(i) + " Menu Definition";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMenuDefinition(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 36 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            TextId = rom.u16(addr + 4);
            HandlerPtr = rom.u32(addr + 8);
            IsLoaded = true;
        }
    }
}
