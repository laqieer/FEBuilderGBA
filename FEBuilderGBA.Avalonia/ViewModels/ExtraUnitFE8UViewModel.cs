using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ExtraUnit (FE8U) editor — uses EditorFormRef auto-binding.
    /// Struct layout: D0=FlagId (4 bytes), P4=UnitInfoPtr (GBA pointer, 4 bytes) = 8 bytes total.
    /// </summary>
    public class ExtraUnitFE8UViewModel : ViewModelBase, IDataVerifiable
    {
        // Field definitions detected from naming convention
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "P4" });

        const uint BaseAddress = 0x37D88;
        const uint EntrySize = 8;

        uint _currentAddr;
        bool _isLoaded;
        uint _flagId;
        uint _unitInfoPtr;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint FlagId { get => _flagId; set => SetField(ref _flagId, value); }
        public uint UnitInfoPtr { get => _unitInfoPtr; set => SetField(ref _unitInfoPtr, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            return EditorFormRef.BuildListWithCount(rom, BaseAddress, EntrySize,
                (i, addr) => U.isSafetyPointer(rom.u32(addr + 4)),
                (i, addr) =>
                {
                    uint flagId = rom.u32(addr + 0);
                    uint unitsAddr = rom.p32(addr + 4);
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

            // Auto-read all fields from ROM using EditorFormRef
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            FlagId = values["D0"];
            UnitInfoPtr = values["P4"];

            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            // Auto-write all fields to ROM using EditorFormRef
            var values = new Dictionary<string, uint>
            {
                ["D0"] = FlagId,
                ["P4"] = UnitInfoPtr,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return EditorFormRef.CountEntries(rom, BaseAddress, EntrySize,
                (i, addr) => U.isSafetyPointer(rom.u32(addr + 4)));
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["D0_FlagId"] = $"0x{FlagId:X08}",
                ["P4_UnitInfoPtr"] = $"0x{UnitInfoPtr:X08}",
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
                ["u32@0x00_FlagId"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04_UnitInfoPtr"] = $"0x{rom.u32(a + 4):X08}",
            };
        }
    }
}
