using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageChapterTitleFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _p0;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint SaveImagePointer => P0;

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.image_chapter_title_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // Build map name lookup from MapSettingCore
            var mapList = MapSettingCore.MakeMapIDList();
            var mapNames = new Dictionary<uint, string>();
            foreach (var m in mapList)
                mapNames[m.tag] = m.name;

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                uint ptr0 = rom.u32(addr);
                if (!U.isPointer(ptr0)) break;

                string mapName = mapNames.TryGetValue(i, out string n) ? n : "";
                string label = mapName != "" ? mapName : U.ToHexString(i) + " Chapter Title FE7";
                result.Add(new AddrResult(addr, label, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            P0 = rom.u32(addr + 0);

            IsLoaded = true;
        }

        public byte[] TryLoadImage(out int width, out int height)
        {
            width = 0; height = 0;
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                uint imgPtr = P0;
                if (!U.isPointer(imgPtr)) return null;

                uint imgAddr = U.toOffset(imgPtr);
                if (!U.isSafetyOffset(imgAddr)) return null;

                uint paletteAddr = rom.RomInfo.image_chapter_title_palette;
                if (paletteAddr == 0 || !U.isSafetyOffset(paletteAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(paletteAddr, 16);
                if (palette == null) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                // Width = 32 tiles (256px), height from data size
                int tilesX = 32;
                width = tilesX * 8;
                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;
                int tilesY = (totalTiles + tilesX - 1) / tilesX;
                if (tilesY <= 0) tilesY = 1;
                height = tilesY * 8;

                if (CoreState.ImageService == null) return null;
                var image = CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, palette);
                if (image == null) return null;
                return image.GetPixelData();
            }
            catch { return null; }
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
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
