using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Unit short text editor — maps each unit to a "description" text ID.
    /// WinForms: UnitsShortTextForm — DATAMAX = 0x46 entries, 2 bytes each (text ID u16).
    /// Display: "0xNN UnitName" where NN is the unit index.</summary>
    public class UnitsShortTextViewModel : ViewModelBase, IDataVerifiable
    {
        const int DATAMAX = 0x46;
        uint _currentAddr;
        uint _baseAddr;
        bool _canWrite;
        uint _textId;
        string _textPreview = "";
        string _unitName = "";
        int _entryIndex;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }
        public string TextPreview { get => _textPreview; set => SetField(ref _textPreview, value); }
        public string UnitName { get => _unitName; set => SetField(ref _unitName, value); }
        public int EntryIndex { get => _entryIndex; set => SetField(ref _entryIndex, value); }

        /// <summary>Build the unit short text list from a base address.</summary>
        public List<AddrResult> BuildList(uint baseAddr)
        {
            _baseAddr = baseAddr;
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom == null || baseAddr == 0) return result;

            for (int i = 0; i < DATAMAX; i++)
            {
                uint addr = baseAddr + (uint)(i * 2);
                if (addr + 2 > (uint)rom.Data.Length) break;
                string unitName = NameResolver.GetUnitName((uint)i);
                string display = $"0x{i:X2} {unitName}";
                result.Add(new AddrResult(addr, display, (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 2 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            TextId = rom.u16(addr + 0);

            // Calculate entry index from base address
            if (_baseAddr > 0 && addr >= _baseAddr)
                EntryIndex = (int)((addr - _baseAddr) / 2);

            UnitName = NameResolver.GetUnitName((uint)EntryIndex);
            TextPreview = TextId > 0 ? NameResolver.GetTextById(TextId) : "(empty)";

            CanWrite = true;
            IsLoading = false;
            MarkClean();
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 2 > (uint)rom.Data.Length) return;

            rom.write_u16(CurrentAddr + 0, (ushort)TextId);
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
                ["W0_TextId"] = $"0x{TextId:X04}",
                ["UnitName"] = UnitName,
                ["TextPreview"] = TextPreview,
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
                ["u16@0x00_TextId"] = $"0x{rom.u16(a + 0):X04}",
            };
        }
    }
}
