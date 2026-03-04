using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MoveCostFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _className = "";
        byte[] _moveCosts = Array.Empty<byte>();

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string ClassName { get => _className; set => SetField(ref _className, value); }
        public byte[] MoveCosts { get => _moveCosts; set => SetField(ref _moveCosts, value); }

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

            // FE6 move cost pointer is at offset 52 in the class struct
            uint moveCostPtrOffset = 52;

            if (classAddr + moveCostPtrOffset + 3 >= (uint)rom.Data.Length)
            {
                MoveCosts = Array.Empty<byte>();
                IsLoaded = true;
                return;
            }

            uint moveCostPtr = rom.u32(classAddr + moveCostPtrOffset);
            if (!U.isPointer(moveCostPtr))
            {
                MoveCosts = Array.Empty<byte>();
                IsLoaded = true;
                return;
            }

            uint moveCostAddr = moveCostPtr - 0x08000000;
            if (!U.isSafetyOffset(moveCostAddr))
            {
                MoveCosts = Array.Empty<byte>();
                IsLoaded = true;
                return;
            }

            // Read 50 terrain cost bytes
            int terrainCount = 50;
            if (moveCostAddr + terrainCount > (uint)rom.Data.Length)
                terrainCount = (int)((uint)rom.Data.Length - moveCostAddr);

            byte[] costs = new byte[terrainCount];
            for (int i = 0; i < terrainCount; i++)
                costs[i] = (byte)rom.u8((uint)(moveCostAddr + i));

            MoveCosts = costs;
            IsLoaded = true;
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
            if (a + 55 < (uint)rom.Data.Length)
            {
                report["u32@0x34"] = $"0x{rom.u32(a + 52):X08}";
            }
            return report;
        }
    }
}
