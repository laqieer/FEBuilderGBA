using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class OPClassFontViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _imagePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }

        public List<AddrResult> LoadOPClassFontList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.op_class_font_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                uint ptr = rom.u32(addr);
                if (!U.isPointer(ptr)) break;

                string name = U.ToHexString(i) + " OP Class Font";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadOPClassFont(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            ImagePointer = rom.u32(addr);
            CanWrite = true;
        }

        /// <summary>
        /// Try to load OP class font image.
        /// Font tiles are LZ77-compressed 4bpp, rendered as 4x4 tiles (32x32 px).
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

                uint palPtrAddr = rom.RomInfo.op_class_font_palette_pointer;
                if (palPtrAddr == 0) return null;

                uint palAddr = rom.p32(palPtrAddr);
                if (!U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                int width = 4 * 8;
                int height = 4 * 8;

                int totalTiles = tileData.Length / 32;
                if (totalTiles < 16)
                {
                    int tilesX = 4;
                    int tilesY = (totalTiles + tilesX - 1) / tilesX;
                    if (tilesY <= 0) tilesY = 1;
                    height = tilesY * 8;
                }

                if (CoreState.ImageService == null) return null;
                return CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, palette);
            }
            catch { return null; }
        }

        public void WriteOPClassFont()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u32(CurrentAddr, ImagePointer);
        }

        public int GetListCount() => LoadOPClassFontList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
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
            };
        }
    }
}
