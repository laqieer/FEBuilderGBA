using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventFunctionPointerViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        uint _currentAddr;
        bool _isLoaded;
        uint _eventCommandFunctionPointer;
        int _filterIndex;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        // P0: Event command function pointer (u32 at offset 0) — ASM pointer
        public uint EventCommandFunctionPointer { get => _eventCommandFunctionPointer; set => SetField(ref _eventCommandFunctionPointer, value); }

        // #1441 — list filter: 0 = Primary (event_function_pointer_table_pointer),
        // 1 = Worldmap (event_function_pointer_table2_pointer, FE8-only). Mirrors
        // the WinForms EventFunctionPointerForm FilterComboBox; LoadList() selects
        // the table base by this index and applies the WinForms +0x80 worldmap id
        // offset. LoadEntry/Write stay absolute-address based, so only the list
        // base changes.
        public int FilterIndex { get => _filterIndex; set => SetField(ref _filterIndex, value); }

        // WinForms +0x80 displayed/tagged event-id offset for the worldmap table.
        const uint WorldmapIdOffset = 0x80;

        /// <summary>
        /// True when the FE8-only worldmap event-function pointer table slot is
        /// present in this ROM's metadata (FE8U/FE8JP nonzero; FE7/FE6 == 0).
        /// Parity with WinForms: the filter option is gated purely on the
        /// metadata slot, NOT on the pointed-table validity — a corrupt/repointed
        /// FE8 table must still surface the option (LoadList then yields an empty
        /// list rather than hiding the table).
        /// </summary>
        public bool IsWorldmapAvailable
        {
            get
            {
                ROM rom = CoreState.ROM;
                return rom?.RomInfo != null
                    && rom.RomInfo.event_function_pointer_table2_pointer != 0;
            }
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            bool worldmap = _filterIndex == 1;
            uint pointer = worldmap
                ? rom.RomInfo.event_function_pointer_table2_pointer
                : rom.RomInfo.event_function_pointer_table_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            uint idOffset = worldmap ? WorldmapIdOffset : 0u;

            const uint blockSize = 4;
            var result = new List<AddrResult>();
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;
                uint funcPtr = rom.u32(addr);
                // Mirror WinForms Init: a valid entry is a Thumb (odd) code
                // pointer; the first failure terminates the scan (the table is
                // not a sparse/skip list).
                if (!U.isPointer(funcPtr) || !U.IsValueOdd(funcPtr)) break;

                uint displayId = idOffset + (uint)i;
                string ptrStr = $"0x{funcPtr:X08}";
                result.Add(new AddrResult(addr, $"0x{displayId:X2} {ptrStr}", displayId));
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
            EventCommandFunctionPointer = v["D0"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint a = CurrentAddr;
            if (a + 4 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint> { ["D0"] = EventCommandFunctionPointer };
            EditorFormRef.WriteFields(rom, a, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["EventCommandFunctionPointer"] = $"0x{EventCommandFunctionPointer:X08}",
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
                ["u32@0x00_EventCommandFunctionPointer"] = $"0x{rom.u32(a + 0):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["EventCommandFunctionPointer"] = "u32@0x00_EventCommandFunctionPointer",
        };
    }
}
