using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MoveCostEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _className = "";
        byte[] _moveCosts = Array.Empty<byte>();
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string ClassName { get => _className; set => SetField(ref _className, value); }
        public byte[] MoveCosts { get => _moveCosts; set => SetField(ref _moveCosts, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>
        /// Load class list (same as ClassEditor but we read the move cost table pointer).
        /// </summary>
        public List<AddrResult> LoadClassList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.class_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.class_datasize;
            var result = new List<AddrResult>();
            for (uint i = 0; i <= 0xFF; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                if (i > 0 && rom.u8(addr + 4) == 0) break;

                uint nameId = rom.u16(addr + 0);
                string decoded;
                try { decoded = FETextDecode.Direct(nameId); }
                catch { decoded = "???"; }
                string name = U.ToHexString(i) + " " + decoded;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>
        /// Load move cost table for a class.
        /// The move cost pointer is at a version-specific offset within the class struct.
        /// </summary>
        public void LoadMoveCost(uint classAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.class_datasize;
            if (classAddr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = classAddr;

            uint nameId = rom.u16(classAddr + 0);
            try { ClassName = FETextDecode.Direct(nameId); }
            catch { ClassName = "???"; }

            // Move cost pointer offset varies:
            // FE6: offset 52 (sunny move cost)
            // FE7/FE8: depends on struct layout, typically offset 48 or 52
            uint moveCostPtrOffset;
            if (rom.RomInfo.version == 6)
                moveCostPtrOffset = 52;
            else
                moveCostPtrOffset = 48;

            if (classAddr + moveCostPtrOffset + 3 >= (uint)rom.Data.Length)
            {
                MoveCosts = Array.Empty<byte>();
                CanWrite = false;
                return;
            }

            uint moveCostPtr = rom.u32(classAddr + moveCostPtrOffset);
            if (!U.isPointer(moveCostPtr))
            {
                MoveCosts = Array.Empty<byte>();
                CanWrite = false;
                return;
            }

            uint moveCostAddr = moveCostPtr - 0x08000000;
            if (!U.isSafetyOffset(moveCostAddr))
            {
                MoveCosts = Array.Empty<byte>();
                CanWrite = false;
                return;
            }

            // Read terrain move costs (64 terrain types max)
            int terrainCount = 64;
            if (moveCostAddr + terrainCount > (uint)rom.Data.Length)
                terrainCount = (int)((uint)rom.Data.Length - moveCostAddr);

            byte[] costs = new byte[terrainCount];
            for (int i = 0; i < terrainCount; i++)
                costs[i] = (byte)rom.u8((uint)(moveCostAddr + i));

            MoveCosts = costs;
            CanWrite = true;
        }

        public int GetListCount() => LoadClassList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
            };
            for (int i = 0; i < MoveCosts.Length; i++)
            {
                report[$"MoveCost[0x{i:X02}]"] = $"0x{MoveCosts[i]:X02}";
            }
            return report;
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            var report = new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
            };

            // Read the move cost pointer from class struct
            uint moveCostPtrOffset = (rom.RomInfo.version == 6) ? 52u : 48u;
            if (a + moveCostPtrOffset + 3 < (uint)rom.Data.Length)
            {
                report[$"u32@0x{moveCostPtrOffset:X02}"] = $"0x{rom.u32(a + moveCostPtrOffset):X08}";
            }
            return report;
        }
    }
}
