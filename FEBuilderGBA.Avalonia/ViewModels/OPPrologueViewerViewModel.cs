using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class OPPrologueViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _imagePointer, _tsaPointer, _paletteColorPointer;
        uint _b8, _b9, _b10, _b11;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint TSAPointer { get => _tsaPointer; set => SetField(ref _tsaPointer, value); }
        public uint PaletteColorPointer { get => _paletteColorPointer; set => SetField(ref _paletteColorPointer, value); }
        public uint B8 { get => _b8; set => SetField(ref _b8, value); }
        public uint B9 { get => _b9; set => SetField(ref _b9, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }

        public List<AddrResult> LoadOPPrologueList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptrAddr = rom.RomInfo.op_prologue_image_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 12);
                if (addr + 12 > (uint)rom.Data.Length) break;

                uint ptr0 = rom.u32(addr);
                if (!U.isPointer(ptr0)) break;

                string name = U.ToHexString(i) + " Prologue";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadOPPrologue(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            ImagePointer = rom.u32(addr + 0);
            TSAPointer = rom.u32(addr + 4);
            B8 = rom.u8(addr + 8);
            B9 = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);

            // Palette comes from a separate pointer in ROM info
            uint palPtrAddr = rom.RomInfo.op_prologue_palette_color_pointer;
            if (palPtrAddr != 0)
                PaletteColorPointer = rom.p32(palPtrAddr);
            else
                PaletteColorPointer = 0;

            CanWrite = true;
        }

        /// <summary>
        /// Try to load OP prologue image.
        /// Uses LZ77-compressed image + TSA with shared palette.
        /// Returns null on failure.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                uint imgPtr = ImagePointer;
                if (!U.isPointer(imgPtr)) return null;

                uint imgAddr = U.toOffset(imgPtr);
                if (!U.isSafetyOffset(imgAddr)) return null;

                uint palAddr = PaletteColorPointer;
                if (!U.isSafetyOffset(palAddr)) return null;

                // Palette is raw GBA data (from shared palette pointer)
                byte[] palette = ImageUtilCore.GetPalette(palAddr, 256);
                if (palette == null) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                // Use TSA if available (256x160 = 32x20 tiles)
                uint tsaPtr = TSAPointer;
                if (U.isPointer(tsaPtr))
                {
                    uint tsaAddr = U.toOffset(tsaPtr);
                    if (U.isSafetyOffset(tsaAddr))
                    {
                        byte[] tsaData = LZ77.decompress(rom.Data, tsaAddr);
                        if (tsaData != null && tsaData.Length > 0)
                            return ImageUtilCore.DecodeHeaderTSA(tileData, tsaData, palette, 32, 20, true);
                    }
                }

                // Fallback: render tiles without TSA
                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;

                int tilesX = 32;
                int tilesY = (totalTiles + tilesX - 1) / tilesX;
                if (tilesY <= 0) tilesY = 1;

                if (CoreState.ImageService == null) return null;
                return CoreState.ImageService.Decode4bppTiles(tileData, 0, tilesX * 8, tilesY * 8, palette);
            }
            catch { return null; }
        }

        public void WriteOPPrologue()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, ImagePointer);
            rom.write_u32(addr + 4, TSAPointer);
            rom.write_u8(addr + 8, (byte)B8);
            rom.write_u8(addr + 9, (byte)B9);
            rom.write_u8(addr + 10, (byte)B10);
            rom.write_u8(addr + 11, (byte)B11);
        }

        public int GetListCount() => LoadOPPrologueList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["TSAPointer"] = $"0x{TSAPointer:X08}",
                ["PaletteColorPointer"] = $"0x{PaletteColorPointer:X08}",
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
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
            };
        }
    }
}
