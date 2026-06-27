using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>World map path MOVEMENT node editor (#1598 corruption fix).
    /// WinForms parity: WorldMapPathMoveEditorForm — a PathType selector picks one of the
    /// 12-byte path records, and the movement data is resolved as p32(record+8) — the
    /// 8-byte-stride movement sub-table (ElapsedTime u16 @+0, X u16 @+4, Y u16 @+6),
    /// terminated by a u32 0xFFFFFFFF sentinel.
    ///
    /// The old VM fell back to p32(worldmap_road_pointer) — the base of the path-RECORD
    /// table — walked THAT at the movement stride reading garbage, and Write() corrupted
    /// the record table. There is now NO record-table fallback: the list stays EMPTY until
    /// a path is selected (SelectPath), and Write() only ever targets a validated movement
    /// node (WorldMapPathMoveCore.WriteNode rejects the record base / terminator). ElapsedTime
    /// is read/written as u16 (was a 4-byte DWord — the format bug).</summary>
    public class WorldMapPathMoveEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        uint _movementBase;
        bool _isLoaded;
        uint _elapsedTime;
        uint _coordinateX;
        uint _coordinateY;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ElapsedTime { get => _elapsedTime; set => SetField(ref _elapsedTime, value); }
        public uint CoordinateX { get => _coordinateX; set => SetField(ref _coordinateX, value); }
        public uint CoordinateY { get => _coordinateY; set => SetField(ref _coordinateY, value); }

        /// <summary>The 12-byte path-record selector list (id carried in AddrResult.tag).</summary>
        public List<AddrResult> PathList { get; private set; } = new();

        /// <summary>The resolved movement sub-table base (p32(record+8)); 0 until a path is selected.</summary>
        public uint MovementBase => _movementBase;

        /// <summary>True once a path is selected and its movement base resolved.</summary>
        public bool HasPath => _movementBase != 0;

        /// <summary>True when a node is loaded AND a path is selected (Write gate).</summary>
        public bool CanWrite => HasPath && IsLoaded;

        /// <summary>Build (and cache) the path-record selector list.</summary>
        public List<AddrResult> LoadPathList()
        {
            PathList = WorldMapPathMoveCore.MakePathList(CoreState.ROM);
            return PathList;
        }

        /// <summary>Resolve the movement base for the given path id; returns HasPath.</summary>
        public bool SelectPath(int pathId)
        {
            ROM rom = CoreState.ROM;
            WorldMapPathMoveCore.ResolveMovementBase(rom, pathId, out _movementBase);
            return HasPath;
        }

        /// <summary>Movement node list for the currently-selected path (empty until a path is selected).</summary>
        public List<AddrResult> LoadList()
        {
            if (_movementBase == 0) return new List<AddrResult>();
            return WorldMapPathMoveCore.LoadMovementList(CoreState.ROM, _movementBase);
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + WorldMapPathMoveCore.NODE_SIZE > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            // ElapsedTime is u16 @+0 (NOT a 4-byte DWord — the format bug); X u16 @+4, Y u16 @+6.
            ElapsedTime = rom.u16(addr + 0);
            CoordinateX = rom.u16(addr + 4);
            CoordinateY = rom.u16(addr + 6);
            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        /// <summary>Write the loaded node back to ROM (validate-before-mutate; record/terminator-safe).
        /// Returns "" on success, or a non-empty user-facing error string (zero mutation).</summary>
        public string Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return R.Error("ROM not loaded.");
            return WorldMapPathMoveCore.WriteNode(rom, CurrentAddr, ElapsedTime, CoordinateX, CoordinateY);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ElapsedTime"] = $"0x{ElapsedTime:X04}",
                ["CoordinateX"] = $"0x{CoordinateX:X04}",
                ["CoordinateY"] = $"0x{CoordinateY:X04}",
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
                ["ElapsedTime@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["CoordinateX@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["CoordinateY@0x06"] = $"0x{rom.u16(a + 6):X04}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["ElapsedTime"] = "ElapsedTime@0x00",
            ["CoordinateX"] = "CoordinateX@0x04",
            ["CoordinateY"] = "CoordinateY@0x06",
        };
    }
}
