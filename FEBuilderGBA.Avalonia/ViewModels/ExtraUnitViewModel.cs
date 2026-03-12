using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ExtraUnit (FE8J) editor — uses EditorFormRef auto-binding.
    /// Struct layout: P0=UnitDataPointer (GBA pointer, 4 bytes) = 4 bytes total.
    /// Flag data lives at a separate address (i * 0x14 + 0x37E10).
    /// </summary>
    public class ExtraUnitViewModel : ViewModelBase, IDataVerifiable
    {
        // Field definitions detected from naming convention
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "P0" });

        const uint BaseAddress = 0x37EE4;
        const uint EntrySize = 4;

        uint _currentAddr;
        bool _isLoaded;
        uint _p0;
        int _selectedIndex;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }

        static uint GetFlagAddr(int i) => (uint)(i * 0x14 + 0x37E10);

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            return EditorFormRef.BuildListWithCount(rom, BaseAddress, EntrySize,
                (i, addr) => U.isSafetyPointer(rom.u32(addr)),
                (i, addr) =>
                {
                    uint flagAddr = GetFlagAddr(i);
                    uint flagId = rom.u8(flagAddr);
                    uint unitsAddr = rom.p32(addr);
                    uint unitId = rom.u8(unitsAddr);
                    string unitName = NameResolver.GetUnitName(unitId);
                    return $"{U.ToHexString(unitId)} {unitName} (Flag:0x{flagId:X})";
                });
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + EntrySize > (uint)rom.Data.Length) return;
            CurrentAddr = addr;

            // Compute selected index for flag lookup
            _selectedIndex = (int)((addr - BaseAddress) / EntrySize);

            // Auto-read fields from ROM using EditorFormRef
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            P0 = values["P0"];

            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            // Auto-write fields to ROM using EditorFormRef
            var values = new Dictionary<string, uint>
            {
                ["P0"] = P0,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return EditorFormRef.CountEntries(rom, BaseAddress, EntrySize,
                (i, addr) => U.isSafetyPointer(rom.u32(addr)));
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            uint flagAddr = GetFlagAddr(_selectedIndex);
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };
            if (U.isSafetyOffset(flagAddr))
                report[$"u8@0x{flagAddr:X08}"] = $"0x{rom.u8(flagAddr):X02}";
            return report;
        }
    }
}
