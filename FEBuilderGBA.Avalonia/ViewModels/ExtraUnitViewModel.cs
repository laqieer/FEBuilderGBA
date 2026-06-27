using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ExtraUnit (FE8J) editor — uses EditorFormRef auto-binding.
    /// Struct layout: P0=UnitDataPointer (GBA pointer, 4 bytes) = 4 bytes total.
    ///
    /// The ONLY editable field (WinForms parity, <c>ExtraUnitForm.WriteButton_Click</c>)
    /// is the per-entry FLAG byte at a SEPARATE absolute address (i * 0x14 + 0x37E10),
    /// NOT an offset within the 4-byte P0 row. P0 (the unit-data pointer) is read-only
    /// list-driver data and is surfaced for DISPLAY ONLY — it must never be written.
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
        uint _flagId;
        int _selectedIndex;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint FlagId { get => _flagId; set => SetField(ref _flagId, value); }

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
                    // 1-based ROM-stored unit ID.
                    string unitName = NameResolver.GetUnitNameByOneBasedId(unitId);
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

            // Auto-read fields from ROM using EditorFormRef (P0 = read-only display).
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            P0 = values["P0"];

            // The editable FLAG byte lives at a SEPARATE absolute address.
            uint flagAddr = GetFlagAddr(_selectedIndex);
            FlagId = U.isSafetyOffset(flagAddr) ? rom.u8(flagAddr) : 0;

            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            // WinForms parity (ExtraUnitForm.WriteButton_Click): the FE8J editor's ONLY
            // editable field is the per-entry FLAG byte at GetFlagAddr(index). P0 (the
            // unit-data pointer) is read-only list-driver data and must NOT be written.
            uint flagAddr = GetFlagAddr(_selectedIndex);
            if (U.isSafetyOffset(flagAddr))
                rom.write_u8(flagAddr, FlagId & 0xFF);
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
                ["FlagId"] = $"0x{FlagId:X02}",
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
                // P0 is a Pointer field (EditorFormRef reads via p32, stripping 0x08 prefix),
                // so raw report must also use p32 to match.
                ["p32@0x00"] = $"0x{rom.p32(a + 0):X08}",
            };
            // The editable FLAG byte lives at a SEPARATE, ROW-SPECIFIC absolute
            // address: GetFlagAddr(i) = i*0x14 + 0x37E10 (0x37E10 for row 0, 0x37E24
            // for row 1, ...) — NOT an offset within the 4-byte P0 row. The key is
            // anchored on the flag TABLE BASE (0x37E10, a regex-matchable literal the
            // data-verify completeness meta-test counts). The VALUE stays a BARE byte
            // (0xNN) so the --data-verify-full FieldLevelCrossCheck against
            // GetDataReport["FlagId"] is byte-equal; the EXACT per-row absolute
            // address actually read is surfaced in a SEPARATE "flagaddr" key so the
            // report is accurate (and diagnosable) for every entry, not just row 0.
            if (U.isSafetyOffset(flagAddr))
            {
                report["u8@0x37E10_FlagId"] = $"0x{rom.u8(flagAddr):X02}";
                report["flagaddr"] = $"0x{flagAddr:X08}";
            }
            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["FlagId"] = "u8@0x37E10_FlagId",
            ["P0"] = "p32@0x00",
        };
    }
}
