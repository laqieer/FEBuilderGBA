using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class BattleTerrainViewerViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        string _terrainName = "";
        uint _imagePointer, _palettePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string TerrainName { get => _terrainName; set => SetField(ref _terrainName, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        public List<AddrResult> LoadBattleTerrainList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.battle_terrain_pointer;
            if (baseAddr == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 24);
                if (addr + 24 > (uint)rom.Data.Length) break;

                // Entry validity: offset 12 should be a pointer
                uint ptr12 = rom.u32(addr + 12);
                if (!U.isPointer(ptr12)) break;

                // Name is stored as ASCII at offset 0, up to 11 bytes
                string tname = "";
                try
                {
                    for (int c = 0; c < 11; c++)
                    {
                        uint b = rom.u8((uint)(addr + c));
                        if (b == 0) break;
                        tname += (char)b;
                    }
                }
                catch { }

                string name = U.ToHexString(i) + " " + tname;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadBattleTerrain(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;

            // Read terrain name from offset 0 (up to 11 bytes ASCII)
            string tname = "";
            try
            {
                for (int c = 0; c < 11; c++)
                {
                    uint b = rom.u8((uint)(addr + c));
                    if (b == 0) break;
                    tname += (char)b;
                }
            }
            catch { }
            TerrainName = tname;

            // Image pointer at offset 12, palette at offset 16
            ImagePointer = rom.u32(addr + 12);
            PalettePointer = rom.u32(addr + 16);
            IsLoaded = true;
        }

        /// <summary>
        /// Try to load battle terrain image as RGBA pixels.
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
                uint palPtr = PalettePointer;
                if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return null;

                uint imgAddr = U.toOffset(imgPtr);
                uint palAddr = U.toOffset(palPtr);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;

                // Render as 32-tile wide strip (256 px)
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
