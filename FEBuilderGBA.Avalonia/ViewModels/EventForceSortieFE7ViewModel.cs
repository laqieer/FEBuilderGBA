using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventForceSortieFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });
        static readonly List<EditorFormRef.FieldDef> _subFields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3" });

        uint _currentAddr;
        bool _isLoaded;
        uint _unitListPointer;

        // Sub-entry fields (N_ prefix in WinForms)
        uint _subAddr;
        uint _unitId;
        uint _unknown1;
        uint _unknown2;
        uint _unknown3;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // P0: Pointer to unit list (u32 at offset 0)
        public uint UnitListPointer { get => _unitListPointer; set => SetField(ref _unitListPointer, value); }

        // Sub-entry fields (inner list pointed to by UnitListPointer)
        public uint SubAddr { get => _subAddr; set => SetField(ref _subAddr, value); }
        // N_B0: Unit ID (u8 at offset 0)
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }
        // N_B1: Unknown (u8 at offset 1)
        public uint Unknown1 { get => _unknown1; set => SetField(ref _unknown1, value); }
        // N_B2: Unknown (u8 at offset 2)
        public uint Unknown2 { get => _unknown2; set => SetField(ref _unknown2, value); }
        // N_B3: Unknown (u8 at offset 3)
        public uint Unknown3 { get => _unknown3; set => SetField(ref _unknown3, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.event_force_sortie_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            // FE7: 23 entries starting from Dragon's Gate (map 0x17)
            for (int i = 0; i < 23; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint mapId = (uint)i + 0x17;
                result.Add(new AddrResult(addr, $"0x{i:X2} Map 0x{mapId:X2}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            UnitListPointer = v["D0"];
            IsLoaded = true;
        }

        /// <summary>
        /// Build the inner unit-list pointed to by the outer entry's D0
        /// pointer (<see cref="UnitListPointer"/>). Faithful port of the
        /// WinForms <c>EventForceSortieFE7Form.N_Init</c> list: dereference D0
        /// (<c>u32 -&gt; toOffset</c>), then walk 4-byte sub-entries terminated
        /// when <c>u8(addr) == 0x00</c> OR <c>u8(addr+3) == 0xD1</c>.
        /// <para>
        /// Each 4-byte read is bounded by an explicit <c>addr + 4 &lt;= Data.Length</c>
        /// guard so a malformed pointer near EOF cannot over-read the
        /// terminator byte at <c>addr+3</c> (Copilot CLI plan-review #1).
        /// </para>
        /// Returns an empty list (no rows) when D0 is not a valid pointer.
        /// </summary>
        public List<AddrResult> LoadSubList()
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom == null) return result;

            uint ptr = UnitListPointer;
            if (!U.isPointer(ptr)) return result;
            uint addr = U.toOffset(ptr);
            if (!U.isSafetyOffset(addr, rom)) return result;

            const uint blockSize = 4;
            // Hard cap so a corrupt list without a terminator cannot loop the
            // whole ROM; WF relies on InputFormRef's DataCount scan but a
            // generous bound keeps us safe and matches other Avalonia walks.
            for (int i = 0; i < 1024; i++, addr += blockSize)
            {
                // Bound the full 4-byte sub-entry before reading either the
                // id byte or the terminator byte at +3.
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint id = rom.u8(addr);
                uint term = rom.u8(addr + 3);
                // WF predicate: continue while (id != 0x00 && term != 0xD1).
                if (id == 0x00 || term == 0xD1) break;

                // WF label: U.ToHexString(uid) + " " + UnitForm.GetUnitName(uid).
                // UnitForm.GetUnitName is the Core NameResolver.GetUnitName alias
                // (0-based table index), so the raw u8 maps directly.
                string unitName = NameResolver.GetUnitName(id);
                result.Add(new AddrResult(addr, U.ToHexString(id) + " " + unitName, id));
            }
            return result;
        }

        /// <summary>
        /// Clear the selected sub-entry. After this call <see cref="SubAddr"/>
        /// is 0 (so <see cref="WriteSubEntry"/> no-ops via its guard) and the
        /// Unit ID / Unknown fields read 0. Called when the outer entry's D0
        /// pointer is invalid or the inner list is empty so a stale sub-entry
        /// from a previous outer selection cannot be written (Copilot CLI
        /// plan-review #2).
        /// </summary>
        public void ResetSubEntry()
        {
            SubAddr = 0;
            UnitId = 0;
            Unknown1 = 0;
            Unknown2 = 0;
            Unknown3 = 0;
        }

        public void LoadSubEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            SubAddr = addr;
            var sv = EditorFormRef.ReadFields(rom, addr, _subFields);
            UnitId = sv["B0"];
            Unknown1 = sv["B1"];
            Unknown2 = sv["B2"];
            Unknown3 = sv["B3"];
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 4 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint> { ["D0"] = UnitListPointer };
            EditorFormRef.WriteFields(rom, a, values, _fields);
        }

        public void WriteSubEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || SubAddr == 0) return;
            uint a = SubAddr;
            if (a + 4 > (uint)rom.Data.Length) return;

            var subValues = new Dictionary<string, uint>
            {
                ["B0"] = UnitId, ["B1"] = Unknown1, ["B2"] = Unknown2, ["B3"] = Unknown3,
            };
            EditorFormRef.WriteFields(rom, a, subValues, _subFields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UnitListPointer"] = $"0x{UnitListPointer:X08}",
                ["UnitId"] = $"0x{UnitId:X02}",
                ["Unknown1"] = $"0x{Unknown1:X02}",
                ["Unknown2"] = $"0x{Unknown2:X02}",
                ["Unknown3"] = $"0x{Unknown3:X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["UnitListPointer"] = "u32@0x00_UnitListPointer",
                ["UnitId"] = "u8@0x00_UnitId",
                ["Unknown1"] = "u8@0x01_Unknown1",
                ["Unknown2"] = "u8@0x02_Unknown2",
                ["Unknown3"] = "u8@0x03_Unknown3",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00_UnitListPointer"] = $"0x{rom.u32(a + 0):X08}",
            };

            if (SubAddr != 0 && SubAddr + 4 <= (uint)rom.Data.Length)
            {
                uint s = SubAddr;
                report["u8@0x00_UnitId"] = $"0x{rom.u8(s + 0):X02}";
                report["u8@0x01_Unknown1"] = $"0x{rom.u8(s + 1):X02}";
                report["u8@0x02_Unknown2"] = $"0x{rom.u8(s + 2):X02}";
                report["u8@0x03_Unknown3"] = $"0x{rom.u8(s + 3):X02}";
            }

            return report;
        }
    }
}
