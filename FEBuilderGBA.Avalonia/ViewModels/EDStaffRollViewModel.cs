using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EDStaffRollViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "D4" });

        uint _currentAddr;
        bool _canWrite;
        uint _imagePointer;
        uint _tsaPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint TSAPointer { get => _tsaPointer; set => SetField(ref _tsaPointer, value); }

        public List<AddrResult> LoadEDStaffRollList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.ed_staffroll_image_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 12; i++) // Staff roll is limited to ~12 entries
            {
                uint addr = (uint)(baseAddr + i * 8);
                if (addr + 8 > (uint)rom.Data.Length) break;

                uint p = rom.u32(addr);
                if (!U.isPointer(p)) break;

                string name = U.ToHexString(i) + " Staff Roll";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEDStaffRoll(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            ImagePointer = v["D0"];
            TSAPointer = v["D4"];
            CanWrite = true;
        }

        public void WriteEDStaffRoll()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 8 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["D0"] = ImagePointer, ["D4"] = TSAPointer,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadEDStaffRollList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["TSAPointer"] = $"0x{TSAPointer:X08}",
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
                ["u32@0x00_ImagePointer"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04_TSAPointer"] = $"0x{rom.u32(a + 4):X08}",
            };
        }
    }
}
