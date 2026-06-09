using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Unit Increase Height editor — controls which portraits get height adjustment in status screen.
    /// WinForms: UnitIncreaseHeightForm.cs
    /// Struct layout: D0 = height value (4 bytes) = 4 bytes total.
    /// Base address comes from p32(RomInfo.unit_increase_height_pointer).
    /// Count comes from u8(RomInfo.unit_increase_height_switch2_address + 2).
    /// Only active when the switch2 instruction pattern is present.
    /// </summary>
    public class UnitIncreaseHeightViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        const uint EntrySize = 4;

        uint _currentAddr;
        bool _isLoaded;
        uint _heightValue;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint HeightValue { get => _heightValue; set => SetField(ref _heightValue, value); }

        /// <summary>
        /// Check if the switch2 instruction pattern is present at the given address.
        /// Replicates PatchUtil.IsSwitch2Enable() from WinForms.
        /// Pattern: SUB (op 0x38-0x3D) followed by CMP (op 0x28-0x2D),
        /// optionally with a LDR r2,[sp,#0] (0x9A00) in between.
        /// </summary>
        static bool IsSwitch2Enable(ROM rom, uint switch2Addr)
        {
            if (switch2Addr == 0 || !U.isSafetyOffset(switch2Addr + 5))
                return false;

            uint extraByte = 0;
            if (rom.u16(switch2Addr + 2) == 0x9A00)
                extraByte = 2;

            uint op1 = rom.u8(switch2Addr + 1);
            if (op1 < 0x38 || op1 > 0x3D) // SUB
                return false;

            uint op2 = rom.u8(switch2Addr + 3 + extraByte);
            if (op2 < 0x28 || op2 > 0x2D) // CMP
                return false;

            return true;
        }

        /// <summary>
        /// Resolve the base address and count for the height table.
        /// Returns (baseAddr, count) or (0, 0) if disabled.
        /// </summary>
        static (uint baseAddr, uint count) ResolveTable(ROM rom)
        {
            if (rom?.RomInfo == null) return (0, 0);

            uint switch2Addr = rom.RomInfo.unit_increase_height_switch2_address;
            uint pointer = rom.RomInfo.unit_increase_height_pointer;

            if (switch2Addr == 0 || pointer == 0) return (0, 0);
            if (!IsSwitch2Enable(rom, switch2Addr)) return (0, 0);

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return (0, 0);

            uint count = rom.u8(switch2Addr + 2) + 1u;
            return (baseAddr, count);
        }

        /// <summary>
        /// Resolve the ROM address of the height row for a given portrait id.
        ///
        /// The Unit Increase Height table is a DENSE FE8 table: row 0 belongs to
        /// the first managed portrait id (<c>u8(switch2_address)</c>) and the rows
        /// are contiguous from there. The id therefore maps to a row only when it
        /// falls inside <c>[startId, startId + count)</c>, where <c>count</c> is the
        /// RESOLVED row count from <see cref="ResolveTable"/> (raw switch byte + 1).
        ///
        /// Returns 0 ("no height row for this id") when the table is disabled, the
        /// id is below the first managed id, the id is at/above the end of the
        /// table (this also rejects the <c>0xFFFFFFFF</c> "no selection" sentinel
        /// without unsigned underflow), or the resolved row would fall outside the
        /// ROM. Never throws.
        /// </summary>
        public static uint IdToAddress(ROM rom, uint portraitId)
        {
            if (rom?.RomInfo == null) return 0;

            var (baseAddr, count) = ResolveTable(rom);
            if (baseAddr == 0 || count == 0) return 0;

            uint startId = rom.u8(rom.RomInfo.unit_increase_height_switch2_address);

            // Guard BEFORE subtracting so an id below the first managed id — or
            // the 0xFFFFFFFF sentinel — can never underflow the unsigned math.
            if (portraitId < startId) return 0;
            if (portraitId >= startId + count) return 0;

            uint addr = baseAddr + (portraitId - startId) * EntrySize;

            // Full-row + safety bounds.
            if ((long)addr + EntrySize > rom.Data.Length) return 0;
            if (!U.isSafetyOffset(addr, rom)) return 0;

            return addr;
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var (baseAddr, count) = ResolveTable(rom);
            if (baseAddr == 0 || count == 0) return new List<AddrResult>();

            uint startId = rom.u8(rom.RomInfo.unit_increase_height_switch2_address);

            return EditorFormRef.BuildList(rom, baseAddr, EntrySize, (int)count,
                (i, addr) =>
                {
                    uint id = startId + (uint)i;
                    string name = NameResolver.GetPortraitName(id);
                    return $"{U.ToHexString(id)} {name}";
                });
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + EntrySize > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            HeightValue = values["D0"];
            IsLoaded = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["D0"] = HeightValue,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            var (_, count) = ResolveTable(rom);
            return (int)count;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["D0_HeightValue"] = $"0x{HeightValue:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;

            // Include switch2 instruction bytes used by IsSwitch2Enable validation
            uint switch2Addr = rom.RomInfo.unit_increase_height_switch2_address;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00_HeightValue"] = $"0x{rom.u32(a + 0):X08}",
            };

            if (switch2Addr != 0 && U.isSafetyOffset(switch2Addr + 5))
            {
                report["u8@0x01_Switch2Op1"] = $"0x{rom.u8(switch2Addr + 1):X02}";
                report["u16@0x02_Switch2Word"] = $"0x{rom.u16(switch2Addr + 2):X04}";
                report["u8@0x03_Switch2Op2"] = $"0x{rom.u8(switch2Addr + 3):X02}";
            }

            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["D0_HeightValue"] = "u32@0x00_HeightValue",
        };
    }
}
