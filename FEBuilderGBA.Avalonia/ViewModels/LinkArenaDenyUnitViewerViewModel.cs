using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class LinkArenaDenyUnitViewerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        bool _canWrite;
        uint _unitId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint UnitId { get => _unitId; set => SetField(ref _unitId, value); }

        public List<AddrResult> LoadLinkArenaDenyUnitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.link_arena_deny_unit_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 2);
                if (addr + 1 >= (uint)rom.Data.Length) break;

                uint unitId = rom.u8(addr);
                if (unitId == 0x00) break;

                string unitName = NameResolver.GetUnitName(unitId);
                string name = $"{U.ToHexString(unitId)} {unitName}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadLinkArenaDenyUnit(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            UnitId = values["B0"];
            CanWrite = true;
        }

        public void WriteLinkArenaDenyUnit()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            var values = new Dictionary<string, uint> { ["B0"] = UnitId };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadLinkArenaDenyUnitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UnitId"] = $"0x{UnitId:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a):X02}",
            };
        }
    }
}
