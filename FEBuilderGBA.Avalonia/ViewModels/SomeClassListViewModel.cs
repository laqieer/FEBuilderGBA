using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SomeClassListViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0" });

        uint _currentAddr;
        uint _baseAddr;
        bool _canWrite;
        uint _classId;
        string _classDisplayName = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ClassId { get => _classId; set => SetField(ref _classId, value); }
        public string ClassDisplayName { get => _classDisplayName; set => SetField(ref _classDisplayName, value); }

        /// <summary>Build the full null-terminated class list starting at the given base address.</summary>
        public List<AddrResult> BuildList(uint baseAddr)
        {
            _baseAddr = baseAddr;
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom == null) return result;

            for (uint addr = baseAddr; addr < (uint)rom.Data.Length; addr++)
            {
                uint classId = rom.u8(addr);
                if (classId == 0x00) break;
                string name = $"0x{classId:X2} {NameResolver.GetClassName(classId)}";
                result.Add(new AddrResult(addr, name, classId));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            ClassId = values["B0"];
            ClassDisplayName = NameResolver.GetClassName(ClassId);
            CanWrite = true;
            IsLoading = false;
            MarkClean();
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 1 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint> { ["B0"] = ClassId };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount()
        {
            if (_baseAddr == 0) return 0;
            return BuildList(_baseAddr).Count;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0_ClassId"] = $"0x{ClassId:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
            };
        }
    }
}
