using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ChapterTitleViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _saveImagePointer, _chapterImagePointer, _titleImagePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint SaveImagePointer { get => _saveImagePointer; set => SetField(ref _saveImagePointer, value); }
        public uint ChapterImagePointer { get => _chapterImagePointer; set => SetField(ref _chapterImagePointer, value); }
        public uint TitleImagePointer { get => _titleImagePointer; set => SetField(ref _titleImagePointer, value); }

        public List<AddrResult> LoadChapterTitleList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.image_chapter_title_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 12);
                if (addr + 12 > (uint)rom.Data.Length) break;

                // Entry validity: offset 0 should be a pointer
                uint ptr0 = rom.u32(addr + 0);
                if (!U.isPointer(ptr0)) break;

                string name = U.ToHexString(i) + " Chapter Title";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadChapterTitle(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            SaveImagePointer = rom.u32(addr + 0);
            ChapterImagePointer = rom.u32(addr + 4);
            TitleImagePointer = rom.u32(addr + 8);
            IsLoaded = true;
        }

        /// <summary>
        /// Try to load the save screen chapter title image as RGBA pixels.
        /// Uses LZ77-compressed 4bpp tiles with a shared palette.
        /// Returns null on failure.
        /// </summary>
        public byte[] TryLoadImage(out int width, out int height)
        {
            width = 0; height = 0;
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                uint imgPtr = SaveImagePointer;
                if (!U.isPointer(imgPtr)) return null;

                uint imgAddr = U.toOffset(imgPtr);
                if (!U.isSafetyOffset(imgAddr)) return null;

                uint paletteAddr = rom.RomInfo.image_chapter_title_palette;
                if (paletteAddr == 0 || !U.isSafetyOffset(paletteAddr)) return null;

                // Palette may be a direct address (not a pointer to dereference)
                byte[] palette = ImageUtilCore.GetPalette(paletteAddr, 16);
                if (palette == null) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                // Width is 32 tiles (256 px), calculate height from data
                int tilesX = 32;
                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;
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

        public int GetListCount() => LoadChapterTitleList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SaveImagePointer"] = $"0x{SaveImagePointer:X08}",
                ["ChapterImagePointer"] = $"0x{ChapterImagePointer:X08}",
                ["TitleImagePointer"] = $"0x{TitleImagePointer:X08}",
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
            };
        }
    }
}
