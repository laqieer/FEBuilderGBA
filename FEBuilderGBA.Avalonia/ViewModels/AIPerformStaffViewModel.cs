using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class AIPerformStaffViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "W0", "W2", "D4" });

        uint _currentAddr;
        bool _isLoaded;
        uint _item;
        uint _unused2;
        uint _asmPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Staff/Item ID (u16 at offset 0)</summary>
        public uint Item { get => _item; set => SetField(ref _item, value); }
        /// <summary>Unused word (u16 at offset 2)</summary>
        public uint Unused2 { get => _unused2; set => SetField(ref _unused2, value); }
        /// <summary>ASM function pointer (u32 at offset 4)</summary>
        public uint AsmPointer { get => _asmPointer; set => SetField(ref _asmPointer, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.ai_preform_staff_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 8;
            var result = new List<AddrResult>();
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0x0000) break;

                uint itemId = rom.u16(addr);
                string itemName = NameResolver.GetItemName(itemId);
                result.Add(new AddrResult(addr, $"0x{i:X2} {itemName}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            Item = values["W0"];
            Unused2 = values["W2"];
            AsmPointer = values["D4"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            var values = new Dictionary<string, uint>
            {
                ["W0"] = Item, ["W2"] = Unused2, ["D4"] = AsmPointer,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Item"] = Item.ToString("X04"),
                ["Unused2"] = Unused2.ToString("X04"),
                ["AsmPointer"] = AsmPointer.ToString("X08"),
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
