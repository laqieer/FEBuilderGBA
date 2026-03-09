using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SystemIconViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _imagePointer, _palettePointer;
        uint _selectedIconIndex;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        // Cached decoded tile data and palette for the icon sheet
        byte[] _tileData;
        byte[] _palette;
        int _sheetTilesX; // sheet width in tiles

        public List<AddrResult> LoadSystemIconList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint imgPtr = rom.RomInfo.system_icon_pointer;
            if (imgPtr == 0) return new List<AddrResult>();

            uint imgAddr = rom.p32(imgPtr);
            if (!U.isSafetyOffset(imgAddr)) return new List<AddrResult>();

            _tileData = LZ77.decompress(rom.Data, imgAddr);
            if (_tileData == null || _tileData.Length == 0) return new List<AddrResult>();

            uint palPtr = rom.RomInfo.system_icon_palette_pointer;
            if (palPtr == 0) return new List<AddrResult>();
            uint palAddr = rom.p32(palPtr);
            if (!U.isSafetyOffset(palAddr)) return new List<AddrResult>();

            _palette = ImageUtilCore.GetPalette(palAddr, 16);
            if (_palette == null) return new List<AddrResult>();

            // Determine sheet width from ROM (matches WinForms GetSystemIconImageSize)
            uint widthVal = rom.u8(rom.RomInfo.system_icon_width_address);
            if (widthVal > 32) widthVal = 32;
            else if (widthVal < 0x12) widthVal = 0x12;
            _sheetTilesX = (int)widthVal; // width in tiles (each tile = 8px)

            // Each icon is 2×2 tiles (16×16 px)
            int iconsPerRow = _sheetTilesX / 2;
            int totalTiles = _tileData.Length / 32;
            int sheetTilesY = (totalTiles + _sheetTilesX - 1) / _sheetTilesX;
            int iconRows = sheetTilesY / 2;
            int totalIcons = iconsPerRow * iconRows;

            var result = new List<AddrResult>();
            for (uint i = 0; i < (uint)totalIcons; i++)
            {
                string name = U.ToHexString(i) + " System Icon";
                result.Add(new AddrResult(i, name, i));
            }
            return result;
        }

        public void LoadSystemIcon(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            _selectedIconIndex = addr; // addr is the icon index (set as AddrResult.addr = i)
            ImagePointer = rom.RomInfo.system_icon_pointer;
            PalettePointer = rom.RomInfo.system_icon_palette_pointer;
            CanWrite = true;
        }

        /// <summary>
        /// Extract and render a single 16×16 icon from the tile sheet.
        /// Icons are arranged in a grid: iconsPerRow icons across, each 2×2 tiles.
        /// Tile data is stored row-by-row in the sheet (tile at sheet position (tx,ty)
        /// is at offset (ty * sheetTilesX + tx) * 32 in the decompressed data).
        /// </summary>
        public IImage TryLoadImage()
        {
            if (_tileData == null || _palette == null || CoreState.ImageService == null)
                return null;

            int iconsPerRow = _sheetTilesX / 2;
            if (iconsPerRow <= 0) return null;

            int iconX = (int)(_selectedIconIndex % (uint)iconsPerRow); // icon column
            int iconY = (int)(_selectedIconIndex / (uint)iconsPerRow); // icon row

            // Each icon starts at tile position (iconX*2, iconY*2) in the sheet
            int startTileX = iconX * 2;
            int startTileY = iconY * 2;

            // Extract 4 tiles (2×2) into a contiguous 16×16 tile buffer
            byte[] iconTiles = new byte[4 * 32]; // 4 tiles × 32 bytes per 4bpp tile
            for (int ty = 0; ty < 2; ty++)
            {
                for (int tx = 0; tx < 2; tx++)
                {
                    int sheetTileIdx = (startTileY + ty) * _sheetTilesX + (startTileX + tx);
                    int srcOffset = sheetTileIdx * 32;
                    int dstOffset = (ty * 2 + tx) * 32;

                    if (srcOffset + 32 <= _tileData.Length)
                        Array.Copy(_tileData, srcOffset, iconTiles, dstOffset, 32);
                }
            }

            return CoreState.ImageService.Decode4bppTiles(iconTiles, 0, 16, 16, _palette);
        }

        public int GetListCount() => LoadSystemIconList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();

            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["system_icon_pointer"] = $"0x{ImagePointer:X08}",
                ["system_icon_palette_pointer"] = $"0x{PalettePointer:X08}",
            };
        }
    }
}
