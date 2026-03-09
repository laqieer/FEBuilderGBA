using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapPathViewModel : ViewModelBase, IDataVerifiable
    {
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.worldmap_road_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 12);
                if (addr + 12 > (uint)rom.Data.Length) break;

                // Termination: first u32 must be a pointer
                if (!U.isPointer(rom.u32(addr))) break;

                string name = U.ToHexString(i) + " Path";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        uint _currentAddr;
        bool _isLoaded;
        uint _pathDataPointer;
        uint _startBasePointId;
        uint _endBasePointId;
        uint _padding6;
        uint _padding7;
        uint _pathMovePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>P0: Pointer to path tile data</summary>
        public uint PathDataPointer { get => _pathDataPointer; set => SetField(ref _pathDataPointer, value); }
        /// <summary>B4: Start base point ID (origin node)</summary>
        public uint StartBasePointId { get => _startBasePointId; set => SetField(ref _startBasePointId, value); }
        /// <summary>B5: End base point ID (destination node)</summary>
        public uint EndBasePointId { get => _endBasePointId; set => SetField(ref _endBasePointId, value); }
        /// <summary>B6: Padding / unknown</summary>
        public uint Padding6 { get => _padding6; set => SetField(ref _padding6, value); }
        /// <summary>B7: Padding / unknown</summary>
        public uint Padding7 { get => _padding7; set => SetField(ref _padding7, value); }
        /// <summary>P8: Pointer to path movement data (NULL = straight line)</summary>
        public uint PathMovePointer { get => _pathMovePointer; set => SetField(ref _pathMovePointer, value); }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 12 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            PathDataPointer = rom.u32(addr + 0);
            StartBasePointId = rom.u8(addr + 4);
            EndBasePointId = rom.u8(addr + 5);
            Padding6 = rom.u8(addr + 6);
            Padding7 = rom.u8(addr + 7);
            PathMovePointer = rom.u32(addr + 8);
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, PathDataPointer);
            rom.write_u8(addr + 4, (byte)StartBasePointId);
            rom.write_u8(addr + 5, (byte)EndBasePointId);
            rom.write_u8(addr + 6, (byte)Padding6);
            rom.write_u8(addr + 7, (byte)Padding7);
            rom.write_u32(addr + 8, PathMovePointer);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PathDataPointer"] = $"0x{PathDataPointer:X08}",
                ["StartBasePointId"] = $"0x{StartBasePointId:X02}",
                ["EndBasePointId"] = $"0x{EndBasePointId:X02}",
                ["Padding6"] = $"0x{Padding6:X02}",
                ["Padding7"] = $"0x{Padding7:X02}",
                ["PathMovePointer"] = $"0x{PathMovePointer:X08}",
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
                ["PathDataPointer@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["StartBasePointId@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["EndBasePointId@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["Padding6@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["Padding7@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["PathMovePointer@0x08"] = $"0x{rom.u32(a + 8):X08}",
            };
        }
    }
}
