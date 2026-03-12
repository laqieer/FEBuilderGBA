using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>World map path movement node editor.
    /// WinForms: WorldMapPathMoveEditorForm — 8-byte entries (ElapsedTime u32, X u16, Y u16).
    /// Terminated when ElapsedTime == 0 (all zeros). Opened via JumpTo with a base address.</summary>
    public class WorldMapPathMoveEditorViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "W4", "W6" });

        uint _baseAddr;
        uint _currentAddr;
        bool _isLoaded;
        uint _elapsedTime;
        uint _coordinateX;
        uint _coordinateY;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ElapsedTime { get => _elapsedTime; set => SetField(ref _elapsedTime, value); }
        public uint CoordinateX { get => _coordinateX; set => SetField(ref _coordinateX, value); }
        public uint CoordinateY { get => _coordinateY; set => SetField(ref _coordinateY, value); }

        /// <summary>Build the path node list from a given base address.</summary>
        public List<AddrResult> BuildList(uint baseAddr)
        {
            _baseAddr = baseAddr;
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom == null || baseAddr == 0) return result;

            const uint blockSize = 8;
            for (int i = 0; i < 256; i++)
            {
                uint addr = baseAddr + (uint)(i * blockSize);
                if (addr + blockSize > (uint)rom.Data.Length) break;

                uint elapsed = rom.u32(addr);
                uint x = rom.u16(addr + 4);
                uint y = rom.u16(addr + 6);

                // Terminator: all zeros
                if (elapsed == 0 && x == 0 && y == 0 && i > 0) break;

                string display = $"Node {i}: T={elapsed} ({x},{y})";
                result.Add(new AddrResult(addr, display, (uint)i));
            }
            return result;
        }

        public List<AddrResult> LoadList()
        {
            if (_baseAddr != 0) return BuildList(_baseAddr);

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            // Try worldmap_road_pointer as fallback
            uint roadPtr = rom.RomInfo.worldmap_road_pointer;
            if (roadPtr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(roadPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();
            return BuildList(baseAddr);
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 8 > (uint)rom.Data.Length) return;

            IsLoading = true;
            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            ElapsedTime = v["D0"];
            CoordinateX = v["W4"];
            CoordinateY = v["W6"];
            IsLoaded = true;
            IsLoading = false;
            MarkClean();
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 8 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["D0"] = ElapsedTime, ["W4"] = CoordinateX, ["W6"] = CoordinateY,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ElapsedTime"] = $"0x{ElapsedTime:X08}",
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
                ["ElapsedTime@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["CoordinateX@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["CoordinateY@0x06"] = $"0x{rom.u16(a + 6):X04}",
            };
        }
    }
}
