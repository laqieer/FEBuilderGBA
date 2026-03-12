using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusRMenuViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _upPtr;
        uint _downPtr;
        uint _leftPtr;
        uint _rightPtr;
        uint _posX, _posY;
        uint _textId;
        uint _loopRoutine, _getterRoutine;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint UpPtr { get => _upPtr; set => SetField(ref _upPtr, value); }
        public uint DownPtr { get => _downPtr; set => SetField(ref _downPtr, value); }
        public uint LeftPtr { get => _leftPtr; set => SetField(ref _leftPtr, value); }
        public uint RightPtr { get => _rightPtr; set => SetField(ref _rightPtr, value); }
        public uint PosX { get => _posX; set => SetField(ref _posX, value); }
        public uint PosY { get => _posY; set => SetField(ref _posY, value); }
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }
        public uint LoopRoutine { get => _loopRoutine; set => SetField(ref _loopRoutine, value); }
        public uint GetterRoutine { get => _getterRoutine; set => SetField(ref _getterRoutine, value); }

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
                string textName = NameResolver.GetTextById(textId);
                string name = $"{U.ToHexString(i)} {textName}";
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
            PosX = rom.u8(addr + 16);
            PosY = rom.u8(addr + 17);
            TextId = rom.u16(addr + 18);
            LoopRoutine = rom.u32(addr + 20);
            GetterRoutine = rom.u32(addr + 24);
            CanWrite = true;
        }

        public void WriteStatusRMenu()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 28 > (uint)rom.Data.Length) return;

            rom.write_u32(addr + 0, UpPtr);
            rom.write_u32(addr + 4, DownPtr);
            rom.write_u32(addr + 8, LeftPtr);
            rom.write_u32(addr + 12, RightPtr);
            rom.write_u8(addr + 16, (byte)PosX);
            rom.write_u8(addr + 17, (byte)PosY);
            rom.write_u16(addr + 18, (ushort)TextId);
            rom.write_u32(addr + 20, LoopRoutine);
            rom.write_u32(addr + 24, GetterRoutine);
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
                ["PosX"] = $"0x{PosX:X02}",
                ["PosY"] = $"0x{PosY:X02}",
                ["TextId"] = $"0x{TextId:X04}",
                ["LoopRoutine"] = $"0x{LoopRoutine:X08}",
                ["GetterRoutine"] = $"0x{GetterRoutine:X08}",
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
                ["u32@0x00_UpPtr"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04_DownPtr"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08_LeftPtr"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C_RightPtr"] = $"0x{rom.u32(a + 12):X08}",
                ["u8@0x10_PosX"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_PosY"] = $"0x{rom.u8(a + 17):X02}",
                ["u16@0x12_TextId"] = $"0x{rom.u16(a + 18):X04}",
                ["u32@0x14_LoopRoutine"] = $"0x{rom.u32(a + 20):X08}",
                ["u32@0x18_GetterRoutine"] = $"0x{rom.u32(a + 24):X08}",
            };
        }
    }
}
