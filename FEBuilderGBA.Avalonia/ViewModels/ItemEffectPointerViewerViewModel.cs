using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ItemEffectPointerViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _effectPointer;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public uint EffectPointer { get => _effectPointer; set => SetField(ref _effectPointer, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadItemEffectPointerList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.item_effect_pointer_table_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint funcPtr = rom.u32(addr);
                if (!U.isPointerOrNULL(funcPtr)) break;
                if (funcPtr != 0 && funcPtr <= 0x08000100) break;
                if (i > 0xFD) break;

                string name = U.ToHexString(i) + " C" + i.ToString("X02");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadItemEffectPointer(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            EffectPointer = rom.u32(addr);

            CanWrite = true;
        }

        public void WriteItemEffectPointer()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u32(addr, EffectPointer);
        }

        public int GetListCount() => LoadItemEffectPointerList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["EffectPointer"] = $"0x{EffectPointer:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }
    }
}
