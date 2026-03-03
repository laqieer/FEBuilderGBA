using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SoundFootStepsViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _dataPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint DataPointer { get => _dataPointer; set => SetField(ref _dataPointer, value); }

        public List<AddrResult> LoadSoundFootStepsList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.sound_foot_steps_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint startClassId = 0;
            uint switchAddr = rom.RomInfo.sound_foot_steps_switch2_address;
            if (switchAddr != 0 && U.isSafetyOffset(switchAddr))
            {
                startClassId = rom.u8(switchAddr);
            }

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                uint value = rom.u32(addr);
                if (!U.isPointer(value)) break;

                uint classId = startClassId + i;
                string name = U.ToHexString(classId) + " Footstep 0x" + value.ToString("X08");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadSoundFootSteps(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            DataPointer = rom.u32(addr);
            IsLoaded = true;
        }
    }
}
