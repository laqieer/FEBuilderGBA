using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusRMenuViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _upPtr;
        uint _downPtr;
        uint _leftPtr;
        uint _rightPtr;
        uint _b16, _b17;
        uint _textId;
        uint _p20, _p24;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint UpPtr { get => _upPtr; set => SetField(ref _upPtr, value); }
        public uint DownPtr { get => _downPtr; set => SetField(ref _downPtr, value); }
        public uint LeftPtr { get => _leftPtr; set => SetField(ref _leftPtr, value); }
        public uint RightPtr { get => _rightPtr; set => SetField(ref _rightPtr, value); }
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }
        public uint P20 { get => _p20; set => SetField(ref _p20, value); }
        public uint P24 { get => _p24; set => SetField(ref _p24, value); }

        public List<AddrResult> LoadStatusRMenuList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptrAddr = rom.RomInfo.status_rmenu_unit_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            // Scan consecutive 28-byte blocks; stop when none of the 4 directional pointers are valid
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;

                uint up = rom.u32(addr + 0);
                uint down = rom.u32(addr + 4);
                uint left = rom.u32(addr + 8);
                uint right = rom.u32(addr + 12);

                // At least one directional pointer must be valid (pointer or null)
                bool anyValid = U.isPointerOrNULL(up) || U.isPointerOrNULL(down)
                    || U.isPointerOrNULL(left) || U.isPointerOrNULL(right);
                if (!anyValid) break;

                uint textId = rom.u16(addr + 18);
                string name = U.ToHexString(i) + " RMenu Text:0x" + textId.ToString("X04");
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadStatusRMenu(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 28 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UpPtr = rom.u32(addr + 0);
            DownPtr = rom.u32(addr + 4);
            LeftPtr = rom.u32(addr + 8);
            RightPtr = rom.u32(addr + 12);
            B16 = rom.u8(addr + 16);
            B17 = rom.u8(addr + 17);
            TextId = rom.u16(addr + 18);
            P20 = rom.u32(addr + 20);
            P24 = rom.u32(addr + 24);
            IsLoaded = true;
        }

        public int GetListCount() => LoadStatusRMenuList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UpPtr"] = $"0x{UpPtr:X08}",
                ["DownPtr"] = $"0x{DownPtr:X08}",
                ["LeftPtr"] = $"0x{LeftPtr:X08}",
                ["RightPtr"] = $"0x{RightPtr:X08}",
                ["TextId"] = $"0x{TextId:X04}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u16@0x12"] = $"0x{rom.u16(a + 18):X04}",
            };
        }
    }
}
