using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// World Map Event (FE7) editor — port of WinForms <c>WorldMapEventPointerFE7Form</c>.
    /// FE7's world-map-event editor is the simpler sibling of the FE8 base
    /// (<see cref="WorldMapEventPointerViewModel"/>): a single list of stage-select event
    /// pointers (4 bytes each, at <c>p32(worldmap_event_on_stageselect_pointer)</c>) plus the two
    /// global ending-event pointers. Per-entry event pointers and the two endings are read in
    /// offset form (<c>p32</c>) and written back via <c>write_p32</c> (mask re-applied).
    /// </summary>
    public class WorldMapEventPointerFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        public const uint EntrySize = 4;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _eventPointer;
        uint _ending1Event;
        uint _ending2Event;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        /// <summary>Event pointer for the selected stage-select row (offset form).</summary>
        public uint EventPointer { get => _eventPointer; set => SetField(ref _eventPointer, value); }
        /// <summary>Global ending-1 (Eliwood) event pointer (offset form).</summary>
        public uint Ending1Event { get => _ending1Event; set => SetField(ref _ending1Event, value); }
        /// <summary>Global ending-2 (Hector) event pointer (offset form).</summary>
        public uint Ending2Event { get => _ending2Event; set => SetField(ref _ending2Event, value); }

        public void LoadGlobalEvents()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                Ending1Event = Ending2Event = 0;
                return;
            }
            Ending1Event = rom.p32(rom.RomInfo.ending1_event_pointer);
            Ending2Event = rom.p32(rom.RomInfo.ending2_event_pointer);
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.worldmap_event_on_stageselect_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            // Mirror WF Init(): row 0 always passes; later rows require U.isPointer; stop at the
            // first non-pointer entry past row 0.
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * EntrySize;
                if (addr + EntrySize > (uint)rom.Data.Length) break;

                uint eventPtr = rom.u32(addr);
                bool include = i == 0 || U.isPointer(eventPtr);
                if (!include) break;

                result.Add(new AddrResult(addr, $"{U.ToHexString(i)} 0x{eventPtr:X08}", i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + EntrySize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            EventPointer = rom.p32(addr);
            IsLoaded = true;
            CanWrite = true;
        }

        /// <summary>Write the selected row's event pointer. Returns false when no row is selected.</summary>
        public bool WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return false;
            if (CurrentAddr + EntrySize > (uint)rom.Data.Length) return false;
            rom.write_p32(CurrentAddr, EventPointer);
            return true;
        }

        /// <summary>Write both global ending-event pointers (the WF "Event Write" button).</summary>
        public bool WriteGlobalEvents()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;
            rom.write_p32(rom.RomInfo.ending1_event_pointer, Ending1Event);
            rom.write_p32(rom.RomInfo.ending2_event_pointer, Ending2Event);
            return true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["EventPointer"] = $"0x{EventPointer:X08}",
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
                ["p32@0x00"] = $"0x{rom.p32(a):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["EventPointer"] = "p32@0x00",
        };
    }
}
