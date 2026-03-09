using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SystemIconViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _iconIndex;
        uint _tileOffset;
        uint _imageGbaPointer, _paletteGbaPointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint IconIndex { get => _iconIndex; set => SetField(ref _iconIndex, value); }
        public uint TileOffset { get => _tileOffset; set => SetField(ref _tileOffset, value); }
        public uint ImageGbaPointer { get => _imageGbaPointer; set => SetField(ref _imageGbaPointer, value); }
        public uint PaletteGbaPointer { get => _paletteGbaPointer; set => SetField(ref _paletteGbaPointer, value); }

        // Cached decoded tile data and palette for the icon sheet
        byte[] _tileData;
        byte[] _palette;
        int _sheetTilesX; // sheet width in tiles
        uint _imgRomAddr; // ROM offset of compressed image data

        public List<AddrResult> LoadSystemIconList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint imgPtr = rom.RomInfo.system_icon_pointer;
            if (imgPtr == 0) return new List<AddrResult>();

            _imgRomAddr = rom.p32(imgPtr);
            if (!U.isSafetyOffset(_imgRomAddr)) return new List<AddrResult>();

            _tileData = LZ77.decompress(rom.Data, _imgRomAddr);
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
                // Use icon index as addr (passed to LoadSystemIconByIndex via SelectedAddressChanged)
                result.Add(new AddrResult(i, name, i));
            }
            return result;
        }

        public void LoadSystemIcon(uint addr)
        {
            // addr is the icon index (from AddrResult.addr)
            LoadSystemIconByIndex(addr);
        }

        /// <summary>
        /// Load a specific icon by list index.
        /// </summary>
        public void LoadSystemIconByIndex(uint index)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            IconIndex = index;
            _iconIndex = index;
            CurrentAddr = _imgRomAddr; // ROM offset of compressed image data

            // Store GBA pointers for display
            ImageGbaPointer = rom.u32(rom.RomInfo.system_icon_pointer);
            PaletteGbaPointer = rom.u32(rom.RomInfo.system_icon_palette_pointer);

            // Calculate tile offset for this icon within decompressed data
            int iconsPerRow = _sheetTilesX / 2;
            if (iconsPerRow > 0)
            {
                int iconX = (int)(index % (uint)iconsPerRow);
                int iconY = (int)(index / (uint)iconsPerRow);
                int startTileX = iconX * 2;
                int startTileY = iconY * 2;
                TileOffset = (uint)((startTileY * _sheetTilesX + startTileX) * 32);
            }

            CanWrite = true;
        }

        /// <summary>
        /// Extract and render a single 16×16 icon from the tile sheet.
        /// </summary>
        public IImage TryLoadImage()
        {
            if (_tileData == null || _palette == null || CoreState.ImageService == null)
                return null;

            int iconsPerRow = _sheetTilesX / 2;
            if (iconsPerRow <= 0) return null;

            int iconX = (int)(_iconIndex % (uint)iconsPerRow);
            int iconY = (int)(_iconIndex / (uint)iconsPerRow);

            int startTileX = iconX * 2;
            int startTileY = iconY * 2;

            // Extract 4 tiles (2×2) into a contiguous 16×16 tile buffer
            byte[] iconTiles = new byte[4 * 32];
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
                ["IconIndex"] = $"0x{IconIndex:X02}",
                ["TileOffset"] = $"0x{TileOffset:X04}",
                ["ImageGbaPointer"] = $"0x{ImageGbaPointer:X08}",
                ["PaletteGbaPointer"] = $"0x{PaletteGbaPointer:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();

            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["system_icon_pointer"] = $"0x{rom.RomInfo.system_icon_pointer:X08}",
                ["system_icon_palette_pointer"] = $"0x{rom.RomInfo.system_icon_palette_pointer:X08}",
                ["system_icon_width_address"] = $"0x{rom.RomInfo.system_icon_width_address:X08}",
            };
        }
    }
}
