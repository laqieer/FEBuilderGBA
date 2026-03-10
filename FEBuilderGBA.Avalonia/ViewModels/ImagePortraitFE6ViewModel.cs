using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImagePortraitFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 16;

        uint _currentAddr;
        bool _isLoaded;
        uint _portraitImagePtr, _miniPortraitPtr, _palettePtr;
        uint _mouthX, _mouthY, _unused14, _unused15;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // D0: Portrait image data pointer (Unit Face)
        public uint PortraitImagePtr { get => _portraitImagePtr; set => SetField(ref _portraitImagePtr, value); }
        // D4: Mini portrait / map sprite face pointer
        public uint MiniPortraitPtr { get => _miniPortraitPtr; set => SetField(ref _miniPortraitPtr, value); }
        // D8: Palette pointer
        public uint PalettePtr { get => _palettePtr; set => SetField(ref _palettePtr, value); }
        // B12: Mouth coordinate X
        public uint MouthX { get => _mouthX; set => SetField(ref _mouthX, value); }
        // B13: Mouth coordinate Y
        public uint MouthY { get => _mouthY; set => SetField(ref _mouthY, value); }
        // B14: Unused / reserved
        public uint Unused14 { get => _unused14; set => SetField(ref _unused14, value); }
        // B15: Unused / reserved
        public uint Unused15 { get => _unused15; set => SetField(ref _unused15, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.portrait_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0) dataSize = SIZE;

            var result = new List<AddrResult>();
            int nullCount = 0;
            for (uint i = 0; i < 0x400; i++)
            {
                uint addr = (uint)(baseAddr + i * dataSize);
                if (addr + dataSize > (uint)rom.Data.Length) break;

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
                        if (nullCount >= 10) break;
                    }
                    else nullCount = 0;
                }

                string name = U.ToHexString(i) + " Portrait FE6";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            PortraitImagePtr = rom.u32(addr + 0);
            MiniPortraitPtr = rom.u32(addr + 4);
            PalettePtr = rom.u32(addr + 8);
            MouthX = rom.u8(addr + 12);
            MouthY = rom.u8(addr + 13);
            Unused14 = rom.u8(addr + 14);
            Unused15 = rom.u8(addr + 15);

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + SIZE > (uint)rom.Data.Length) return;

            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, PortraitImagePtr);
            rom.write_u32(addr + 4, MiniPortraitPtr);
            rom.write_u32(addr + 8, PalettePtr);
            rom.write_u8(addr + 12, MouthX);
            rom.write_u8(addr + 13, MouthY);
            rom.write_u8(addr + 14, Unused14);
            rom.write_u8(addr + 15, Unused15);
        }

        /// <summary>
        /// Draw the FE6 portrait. Returns the assembled 96x80 portrait image, or the map sprite if no main face.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            IImageService svc = CoreState.ImageService;
            if (rom == null || svc == null || CurrentAddr == 0) return null;
            try
            {
                uint unitFace = PortraitImagePtr;
                uint mapFace = MiniPortraitPtr;
                uint palette = PalettePtr;

                // Try main portrait first
                if (U.isPointer(unitFace) && U.isPointer(palette))
                {
                    uint faceOff = U.toOffset(unitFace);
                    uint palOff = U.toOffset(palette);
                    if (U.isSafetyOffset(faceOff) && U.isSafetyOffset(palOff))
                    {
                        // FE6 portraits are LZ77 compressed
                        byte[] imageUZ = LZ77.decompress(rom.Data, faceOff);
                        if (imageUZ != null && imageUZ.Length > 0)
                        {
                            // Decode as 256x40 (32*8 x 5*8) tile sheet
                            byte[] gbaPalette = ImageUtilCore.GetPalette(palOff, 16);
                            if (gbaPalette != null)
                            {
                                // Return the raw sprite sheet (32*8 x 5*8) for comparison
                                return svc.Decode4bppTiles(imageUZ, 0, 32 * 8, 5 * 8, gbaPalette);
                            }
                        }
                    }
                }

                // Fallback to map portrait
                if (U.isPointer(mapFace) && U.isPointer(palette))
                    return PortraitRendererCore.DrawPortraitMap(mapFace, palette);

                return null;
            }
            catch { return null; }
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PortraitImagePtr"] = $"0x{PortraitImagePtr:X08}",
                ["MiniPortraitPtr"] = $"0x{MiniPortraitPtr:X08}",
                ["PalettePtr"] = $"0x{PalettePtr:X08}",
                ["MouthX"] = $"0x{MouthX:X02}",
                ["MouthY"] = $"0x{MouthY:X02}",
                ["Unused14"] = $"0x{Unused14:X02}",
                ["Unused15"] = $"0x{Unused15:X02}",
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
                ["u32@0_PortraitImagePtr"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@4_MiniPortraitPtr"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8_PalettePtr"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@12_MouthX"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@13_MouthY"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@14_Unused14"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@15_Unused15"] = $"0x{rom.u8(a + 15):X02}",
            };
        }
    }
}
