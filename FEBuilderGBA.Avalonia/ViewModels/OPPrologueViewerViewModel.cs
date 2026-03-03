using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class OPPrologueViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _imagePointer, _tsaPointer, _paletteColorPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint TSAPointer { get => _tsaPointer; set => SetField(ref _tsaPointer, value); }
        public uint PaletteColorPointer { get => _paletteColorPointer; set => SetField(ref _paletteColorPointer, value); }

        public List<AddrResult> LoadOPPrologueList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.op_prologue_image_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

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

            // Palette comes from a separate pointer in ROM info
            uint palPtrAddr = rom.RomInfo.op_prologue_palette_color_pointer;
            if (palPtrAddr != 0)
                PaletteColorPointer = rom.p32(palPtrAddr);
            else
                PaletteColorPointer = 0;

            IsLoaded = true;
        }

        /// <summary>
        /// Try to load OP prologue image as RGBA pixels.
        /// Uses LZ77-compressed tiles with shared palette.
        /// Returns null on failure.
        /// </summary>
        public byte[] TryLoadImage(out int width, out int height)
        {
            width = 0; height = 0;
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

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                // Render as 32 tiles wide (256px), calculate height
                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;

                int tilesX = 32;
                int tilesY = (totalTiles + tilesX - 1) / tilesX;
                if (tilesY <= 0) tilesY = 1;

                width = tilesX * 8;
                height = tilesY * 8;

                if (CoreState.ImageService == null) return null;
                var image = CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, palette);
                if (image == null) return null;
                return image.GetPixelData();
            }
            catch { return null; }
        }
    }
}
