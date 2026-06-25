using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventMoveDataFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _moveDirection;
        uint _time;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // B0: Move direction (u8 at offset 0)
        // 00=Left, 01=Right, 02=Down, 03=Up, 04=End, 09=Highlight, 0A=Collision mark, 0C=Speed change
        public uint MoveDirection
        {
            get => _moveDirection;
            set
            {
                if (SetField(ref _moveDirection, value))
                    OnPropertyChanged(nameof(HasTimeField));
            }
        }

        // B1: time/speed parameter byte (u8 at offset+1), only present for types 9 (Highlight) and 0xC (Speed change).
        public uint Time { get => _time; set => SetField(ref _time, value); }

        /// <summary>
        /// True when the current command type (B0) carries an extra time/speed
        /// parameter byte at offset+1 (types 9 and 0xC), mirroring WinForms
        /// <c>IsAppnedData</c>. Drives Time-field visibility (WinForms <c>X_TIME</c>).
        /// </summary>
        public bool HasTimeField => EventMoveDataFE7Core.IsAppnedData(MoveDirection);

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Find the first valid move data block from event scripts, then walk
            // every command in the variable-length sequence (each is its own row).
            uint addr = EventSubEditorHelper.FindFirstMoveDataAddr(rom);
            if (addr != 0)
            {
                var list = EventMoveDataFE7Core.WalkCommands(rom, addr);
                if (list.Count > 0)
                {
                    LoadEntry(list[0].addr);
                    return list;
                }
            }

            return new List<AddrResult>();
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            MoveDirection = rom.u8(addr);

            // Read the time/speed byte at addr+1 for appended types (9/0xC); 0 otherwise.
            if (HasTimeField && addr + 2 <= (uint)rom.Data.Length)
                Time = rom.u8(addr + 1);
            else
                Time = 0;

            IsLoaded = true;
        }

        /// <summary>
        /// Write the current command. The caller (View) is responsible for the
        /// ambient undo scope (UndoService.Begin / ROM.BeginUndoScope); every
        /// write below registers into that active scope.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 1 > (uint)rom.Data.Length) return;

            // B0 (direction) is always written at offset 0.
            rom.write_u8(CurrentAddr, MoveDirection);

            // B1 (time/speed) is written at offset+1 ONLY for appended types
            // (9/0xC) and only when that byte is in range — correct gating,
            // NOT the inverted WinForms guard (#1440).
            if (HasTimeField && CurrentAddr + 2 <= (uint)rom.Data.Length)
                rom.write_u8(CurrentAddr + 1, Time);
        }

        public int GetListCount() => IsLoaded && CurrentAddr != 0 ? 1 : 0;

        public Dictionary<string, string> GetDataReport()
        {
            var d = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["MoveDirection"] = $"0x{MoveDirection:X02}",
            };
            if (HasTimeField)
                d["Time"] = $"0x{Time:X02}";
            return d;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            var d = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_MoveDirection"] = $"0x{rom.u8(a + 0):X02}",
            };
            if (HasTimeField && a + 2 <= (uint)rom.Data.Length)
                d["u8@0x01_Time"] = $"0x{rom.u8(a + 1):X02}";
            return d;
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            var d = new Dictionary<string, string>
            {
                ["MoveDirection"] = "u8@0x00_MoveDirection",
            };
            if (HasTimeField)
                d["Time"] = "u8@0x01_Time";
            return d;
        }
    }
}
