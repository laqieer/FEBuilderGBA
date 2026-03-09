using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIPerformItemViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _item;
        uint _unused2;
        uint _asmPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Item ID (u16 at offset 0)</summary>
        public uint Item { get => _item; set => SetField(ref _item, value); }
        /// <summary>Unused word (u16 at offset 2)</summary>
        public uint Unused2 { get => _unused2; set => SetField(ref _unused2, value); }
        /// <summary>ASM function pointer (u32 at offset 4)</summary>
        public uint AsmPointer { get => _asmPointer; set => SetField(ref _asmPointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "AI Item Performance", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            Item = rom.u16(addr + 0);
            Unused2 = rom.u16(addr + 2);
            AsmPointer = rom.u32(addr + 4);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            rom.write_u16(addr + 0, Item);
            rom.write_u16(addr + 2, Unused2);
            rom.write_u32(addr + 4, AsmPointer);
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                { "Item", Item.ToString("X04") },
                { "Unused2", Unused2.ToString("X04") },
                { "AsmPointer", AsmPointer.ToString("X08") },
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
                ["u16@0x00_Item"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02_Unused2"] = $"0x{rom.u16(a + 2):X04}",
                ["u32@0x04_AsmPointer"] = $"0x{rom.u32(a + 4):X08}",
            };
        }
    }
}
