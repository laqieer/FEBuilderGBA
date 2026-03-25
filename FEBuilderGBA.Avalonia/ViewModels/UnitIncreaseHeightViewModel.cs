using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitIncreaseHeightViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        const uint EntrySize = 4;

        uint _currentAddr;
        bool _isLoaded;
        uint _p0;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }

        /// <summary>
        /// Check if the switch2 opcode pattern is present (inline version of PatchUtil.IsSwitch2Enable).
        /// Looks for SUB + CMP ARM opcodes at switch2_address.
        /// </summary>
        static bool IsSwitch2Enable(ROM rom, uint switch2Addr)
        {
            if (switch2Addr == 0 || !U.isSafetyOffset(switch2Addr + 4)) return false;

            uint extraByte = 0;
            if (rom.u16(switch2Addr + 2) == 0x9A00)
                extraByte = 2;

            uint op1 = rom.u8(switch2Addr + 1);
            if (op1 < 0x38 || op1 > 0x3D) return false; // SUB check

            uint op2 = rom.u8(switch2Addr + 3 + extraByte);
            if (op2 < 0x28 || op2 > 0x2D) return false; // CMP check

            return true;
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptrAddr = rom.RomInfo.unit_increase_height_pointer;
            uint switch2Addr = rom.RomInfo.unit_increase_height_switch2_address;
            if (ptrAddr == 0 || switch2Addr == 0) return new List<AddrResult>();
            if (!IsSwitch2Enable(rom, switch2Addr)) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint count = rom.u8(switch2Addr + 2);

            var result = new List<AddrResult>();
            for (uint i = 0; i <= count; i++)
            {
                uint addr = baseAddr + i * EntrySize;
                if (addr + EntrySize > (uint)rom.Data.Length) break;

                uint startId = rom.u8(switch2Addr);
                uint id = startId + i;
                string name = U.ToHexString(id) + " Unit Height";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            P0 = values["D0"];
            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

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
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["P0"] = "u32@0x00",
        };
    }
}
