using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PortraitViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        string _name = "";
        uint _portraitIndex;
        uint _imagePointer, _mapPointer, _palettePointer;
        uint _mouthPointer, _classCardPointer;
        uint _mouthX, _mouthY, _eyeX, _eyeY, _state, _padding25, _padding26, _padding27;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public uint PortraitIndex { get => _portraitIndex; set => SetField(ref _portraitIndex, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint MapPointer { get => _mapPointer; set => SetField(ref _mapPointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }
        public uint MouthPointer { get => _mouthPointer; set => SetField(ref _mouthPointer, value); }
        public uint ClassCardPointer { get => _classCardPointer; set => SetField(ref _classCardPointer, value); }
        public uint MouthX { get => _mouthX; set => SetField(ref _mouthX, value); }
        public uint MouthY { get => _mouthY; set => SetField(ref _mouthY, value); }
        public uint EyeX { get => _eyeX; set => SetField(ref _eyeX, value); }
        public uint EyeY { get => _eyeY; set => SetField(ref _eyeY, value); }
        public uint State { get => _state; set => SetField(ref _state, value); }
        public uint Padding25 { get => _padding25; set => SetField(ref _padding25, value); }
        public uint Padding26 { get => _padding26; set => SetField(ref _padding26, value); }
        public uint Padding27 { get => _padding27; set => SetField(ref _padding27, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadPortraitList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.portrait_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0) dataSize = 28;

            var result = new List<AddrResult>();
            int nullCount = 0;
            for (uint i = 0; i < 0x400; i++) // generous max
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

                // Check validity: at least one of the first 3 pointers should be valid
                uint u0 = rom.u32(addr + 0);
                uint u4 = rom.u32(addr + 4);
                uint u8 = rom.u32(addr + 8);

                if (i > 0)
                {
                    if (!U.isPointerOrNULL(u0) || !U.isPointerOrNULL(u4) || !U.isPointerOrNULL(u8))
                        break;
                    if (u0 == 0 && u4 == 0 && u8 == 0)
                    {
                        nullCount++;
                        if (nullCount >= 100) break;
                    }
                    else nullCount = 0;
                }

                string name = U.ToHexString(i) + " Portrait";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadPortrait(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0) dataSize = 28;
            if (addr + dataSize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ImagePointer = rom.u32(addr + 0);
            MapPointer = rom.u32(addr + 4);
            PalettePointer = rom.u32(addr + 8);
            MouthPointer = rom.u32(addr + 12);
            ClassCardPointer = rom.u32(addr + 16);
            MouthX = rom.u8(addr + 20);
            MouthY = rom.u8(addr + 21);
            EyeX = rom.u8(addr + 22);
            EyeY = rom.u8(addr + 23);
            State = rom.u8(addr + 24);
            Padding25 = rom.u8(addr + 25);
            Padding26 = rom.u8(addr + 26);
            Padding27 = rom.u8(addr + 27);
            Name = $"Portrait at 0x{addr:X08}";
            CanWrite = true;
        }

        public void WritePortrait()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, ImagePointer);
            rom.write_u32(addr + 4, MapPointer);
            rom.write_u32(addr + 8, PalettePointer);
            rom.write_u32(addr + 12, MouthPointer);
            rom.write_u32(addr + 16, ClassCardPointer);
            rom.write_u8(addr + 20, (byte)MouthX);
            rom.write_u8(addr + 21, (byte)MouthY);
            rom.write_u8(addr + 22, (byte)EyeX);
            rom.write_u8(addr + 23, (byte)EyeY);
            rom.write_u8(addr + 24, (byte)State);
            rom.write_u8(addr + 25, (byte)Padding25);
            rom.write_u8(addr + 26, (byte)Padding26);
            rom.write_u8(addr + 27, (byte)Padding27);
        }

        /// <summary>
        /// Assemble the full 96x80 unit portrait from sprite sheet parts.
        /// </summary>
        public IImage TryLoadMainPortrait()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;

            try
            {
                return PortraitRendererCore.DrawPortraitUnit(
                    ImagePointer, PalettePointer,
                    (byte)EyeX, (byte)EyeY, (byte)State);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load the mini/map portrait (32x32).
        /// </summary>
        public IImage TryLoadMapPortrait()
        {
            if (CoreState.ROM == null || CurrentAddr == 0) return null;

            try
            {
                return PortraitRendererCore.DrawPortraitMap(MapPointer, PalettePointer);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load the class card portrait (80x80) from D16 pointer.
        /// </summary>
        public IImage TryLoadClassPortrait()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;

            try
            {
                // ClassCardPointer is the class face pointer
                if (!U.isPointer(ClassCardPointer)) return null;
                return PortraitRendererCore.DrawPortraitClass(ClassCardPointer, PalettePointer);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to load portrait map sprite (offset +4) as a 32x32 image.
        /// Map portraits are always LZ77-compressed 4bpp, 4x4 tiles.
        /// Returns null if the portrait cannot be decoded.
        /// </summary>
        public IImage TryLoadPortraitImage()
        {
            return TryLoadMapPortrait();
        }

        public int GetListCount() => LoadPortraitList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["MapPointer"] = $"0x{MapPointer:X08}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
                ["D12_MouthPointer"] = $"0x{MouthPointer:X08}",
                ["D16_ClassCardPointer"] = $"0x{ClassCardPointer:X08}",
                ["B20_MouthX"] = $"0x{MouthX:X02}",
                ["B21_MouthY"] = $"0x{MouthY:X02}",
                ["B22_EyeX"] = $"0x{EyeX:X02}",
                ["B23_EyeY"] = $"0x{EyeY:X02}",
                ["B24_State"] = $"0x{State:X02}",
                ["B25_Padding"] = $"0x{Padding25:X02}",
                ["B26_Padding"] = $"0x{Padding26:X02}",
                ["B27_Padding"] = $"0x{Padding27:X02}",
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
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["u8@0x14"] = $"0x{rom.u8(a + 20):X02}",
                ["u8@0x15"] = $"0x{rom.u8(a + 21):X02}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u8@0x18"] = $"0x{rom.u8(a + 24):X02}",
                ["u8@0x19"] = $"0x{rom.u8(a + 25):X02}",
                ["u8@0x1A"] = $"0x{rom.u8(a + 26):X02}",
                ["u8@0x1B"] = $"0x{rom.u8(a + 27):X02}",
            };
        }
    }
}
