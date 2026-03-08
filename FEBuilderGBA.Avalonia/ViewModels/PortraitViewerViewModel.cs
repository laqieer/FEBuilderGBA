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
        uint _d12, _d16;
        uint _b20, _b21, _b22, _b23, _b24, _b25, _b26, _b27;
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public uint PortraitIndex { get => _portraitIndex; set => SetField(ref _portraitIndex, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint MapPointer { get => _mapPointer; set => SetField(ref _mapPointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }
        public uint D12 { get => _d12; set => SetField(ref _d12, value); }
        public uint D16 { get => _d16; set => SetField(ref _d16, value); }
        public uint B20 { get => _b20; set => SetField(ref _b20, value); }
        public uint B21 { get => _b21; set => SetField(ref _b21, value); }
        public uint B22 { get => _b22; set => SetField(ref _b22, value); }
        public uint B23 { get => _b23; set => SetField(ref _b23, value); }
        public uint B24 { get => _b24; set => SetField(ref _b24, value); }
        public uint B25 { get => _b25; set => SetField(ref _b25, value); }
        public uint B26 { get => _b26; set => SetField(ref _b26, value); }
        public uint B27 { get => _b27; set => SetField(ref _b27, value); }
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
            D12 = rom.u32(addr + 12);
            D16 = rom.u32(addr + 16);
            B20 = rom.u8(addr + 20);
            B21 = rom.u8(addr + 21);
            B22 = rom.u8(addr + 22);
            B23 = rom.u8(addr + 23);
            B24 = rom.u8(addr + 24);
            B25 = rom.u8(addr + 25);
            B26 = rom.u8(addr + 26);
            B27 = rom.u8(addr + 27);
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
            rom.write_u32(addr + 12, D12);
            rom.write_u32(addr + 16, D16);
            rom.write_u8(addr + 20, (byte)B20);
            rom.write_u8(addr + 21, (byte)B21);
            rom.write_u8(addr + 22, (byte)B22);
            rom.write_u8(addr + 23, (byte)B23);
            rom.write_u8(addr + 24, (byte)B24);
            rom.write_u8(addr + 25, (byte)B25);
            rom.write_u8(addr + 26, (byte)B26);
            rom.write_u8(addr + 27, (byte)B27);
        }

        /// <summary>
        /// Try to load portrait image data as RGBA pixels using Core image utils.
        /// Returns null if the portrait cannot be decoded.
        /// </summary>
        public byte[] TryLoadPortraitImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;

            try
            {
                uint imgPtr = rom.u32(CurrentAddr + 0);
                uint palPtr = rom.u32(CurrentAddr + 8);

                if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return null;

                uint imgAddr = imgPtr - 0x08000000;
                uint palAddr = palPtr - 0x08000000;

                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                // Load palette (16 colors * 2 bytes = 32 bytes)
                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                // Load compressed image tiles (typically 4bpp, 32x32 = 4 tiles x 4 tiles)
                var image = ImageUtilCore.LoadROMTiles4bpp(imgAddr, palette, 4, 4, true);
                if (image == null) return null;

                return image.GetPixelData();
            }
            catch
            {
                return null;
            }
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
                ["D12"] = $"0x{D12:X08}",
                ["D16"] = $"0x{D16:X08}",
                ["B20"] = $"0x{B20:X02}",
                ["B21"] = $"0x{B21:X02}",
                ["B22"] = $"0x{B22:X02}",
                ["B23"] = $"0x{B23:X02}",
                ["B24"] = $"0x{B24:X02}",
                ["B25"] = $"0x{B25:X02}",
                ["B26"] = $"0x{B26:X02}",
                ["B27"] = $"0x{B27:X02}",
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
