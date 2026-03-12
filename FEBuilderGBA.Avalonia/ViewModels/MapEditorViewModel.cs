using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        int _mapWidth;
        int _mapHeight;
        string _mapInfo = "";
        uint _mapId;

        // Tile selection state
        int _selectedTileX = -1;
        int _selectedTileY = -1;
        int _selectedTileId;
        string _tileInfo = "";
        bool _hasTileSelected;

        // Cached decompressed map data for the current map
        byte[] _cachedMapData;
        uint _cachedMapPointerEntryAddr; // address of the pointer table entry for this map's data

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public int MapWidth { get => _mapWidth; set => SetField(ref _mapWidth, value); }
        public int MapHeight { get => _mapHeight; set => SetField(ref _mapHeight, value); }
        public string MapInfo { get => _mapInfo; set => SetField(ref _mapInfo, value); }
        public uint MapId { get => _mapId; set => SetField(ref _mapId, value); }

        public int SelectedTileX { get => _selectedTileX; set => SetField(ref _selectedTileX, value); }
        public int SelectedTileY { get => _selectedTileY; set => SetField(ref _selectedTileY, value); }
        public int SelectedTileId { get => _selectedTileId; set => SetField(ref _selectedTileId, value); }
        public string TileInfo { get => _tileInfo; set => SetField(ref _tileInfo, value); }
        public bool HasTileSelected { get => _hasTileSelected; set => SetField(ref _hasTileSelected, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            return MapSettingCore.MakeMapIDList();
        }

        /// <summary>
        /// Resolve a PLIST index to a ROM offset using the given pointer table address.
        /// The pointer table at basePointer contains an array of 4-byte GBA pointers.
        /// PLIST is the index into that array.
        /// </summary>
        static uint PlistToOffset(ROM rom, uint basePointer, uint plist)
        {
            if (basePointer == 0 || plist == 0) return U.NOT_FOUND;

            uint tableBase = rom.p32(basePointer);
            if (!U.isSafetyOffset(tableBase)) return U.NOT_FOUND;

            uint entryAddr = (uint)(tableBase + plist * 4);
            if (entryAddr + 4 > (uint)rom.Data.Length) return U.NOT_FOUND;

            uint dataPtr = rom.p32(entryAddr);
            if (!U.isSafetyOffset(dataPtr)) return U.NOT_FOUND;

            return dataPtr;
        }

        /// <summary>
        /// Load and render a map from the given map setting address.
        /// Returns RGBA pixel data and sets MapWidth/MapHeight, or null on failure.
        /// </summary>
        public byte[] LoadMapImage(uint addr, uint mapId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || CoreState.ImageService == null)
            {
                MapInfo = "No ROM loaded";
                return null;
            }

            CurrentAddr = addr;
            MapId = mapId;

            // Clear previous tile selection
            SelectedTileX = -1;
            SelectedTileY = -1;
            SelectedTileId = 0;
            TileInfo = "";
            HasTileSelected = false;
            _cachedMapData = null;

            // Read PLIST values from map setting data
            // Map setting layout: offset +4 = obj_plist (u16), +6 = palette_plist (u8),
            //                     +7 = config_plist (u8), +8 = mappointer_plist (u8)
            uint obj_plist = rom.u16(addr + 4);
            uint palette_plist = rom.u8(addr + 6);
            uint config_plist = rom.u8(addr + 7);
            uint mappointer_plist = rom.u8(addr + 8);

            // Resolve PLIST to ROM offsets
            uint obj_offset = PlistToOffset(rom, rom.RomInfo.map_obj_pointer, obj_plist & 0xFF);
            uint pal_offset = PlistToOffset(rom, rom.RomInfo.map_pal_pointer, palette_plist);
            uint config_offset = PlistToOffset(rom, rom.RomInfo.map_config_pointer, config_plist);
            uint map_offset = PlistToOffset(rom, rom.RomInfo.map_map_pointer_pointer, mappointer_plist);

            // For FE7, obj_plist can have a second byte for a second tileset
            uint obj2_plist = (obj_plist >> 8) & 0xFF;
            uint obj2_offset = 0;
            if (obj2_plist > 0)
            {
                obj2_offset = PlistToOffset(rom, rom.RomInfo.map_obj_pointer, obj2_plist);
            }

            // Build info string
            var info = new System.Text.StringBuilder();
            info.AppendLine($"Map ID: 0x{mapId:X02}  Address: 0x{addr:X08}");
            info.AppendLine($"OBJ PLIST: 0x{obj_plist:X04} -> 0x{(obj_offset != U.NOT_FOUND ? obj_offset : 0):X08}");
            info.AppendLine($"PAL PLIST: 0x{palette_plist:X02} -> 0x{(pal_offset != U.NOT_FOUND ? pal_offset : 0):X08}");
            info.AppendLine($"CFG PLIST: 0x{config_plist:X02} -> 0x{(config_offset != U.NOT_FOUND ? config_offset : 0):X08}");
            info.AppendLine($"MAP PLIST: 0x{mappointer_plist:X02} -> 0x{(map_offset != U.NOT_FOUND ? map_offset : 0):X08}");

            if (obj_offset == U.NOT_FOUND || pal_offset == U.NOT_FOUND ||
                config_offset == U.NOT_FOUND || map_offset == U.NOT_FOUND)
            {
                info.AppendLine("ERROR: Could not resolve one or more PLIST addresses.");
                MapInfo = info.ToString();
                MapWidth = 0;
                MapHeight = 0;
                IsLoaded = true;
                return null;
            }

            // Decompress tile graphics (OBJ)
            byte[] objData = LZ77.decompress(rom.Data, obj_offset);
            if (objData == null || objData.Length == 0)
            {
                info.AppendLine("ERROR: Failed to decompress OBJ tile data.");
                MapInfo = info.ToString();
                IsLoaded = true;
                return null;
            }

            // Append second tileset if present (FE7)
            if (obj2_plist > 0 && obj2_offset != U.NOT_FOUND)
            {
                byte[] obj2Data = LZ77.decompress(rom.Data, obj2_offset);
                if (obj2Data != null && obj2Data.Length > 0)
                {
                    byte[] combined = new byte[objData.Length + obj2Data.Length];
                    Array.Copy(objData, 0, combined, 0, objData.Length);
                    Array.Copy(obj2Data, 0, combined, objData.Length, obj2Data.Length);
                    objData = combined;
                }
            }

            // Read palette (16 palettes * 16 colors * 2 bytes = 512 bytes)
            int palLen = Math.Min((2 * 16) * 16, rom.Data.Length - (int)pal_offset);
            if (palLen <= 0)
            {
                info.AppendLine("ERROR: Invalid palette offset.");
                MapInfo = info.ToString();
                IsLoaded = true;
                return null;
            }
            byte[] paletteData = new byte[palLen];
            Array.Copy(rom.Data, pal_offset, paletteData, 0, palLen);

            // Decompress chipset config (TSA mapping from map tile -> 4 sub-tiles)
            byte[] configData = LZ77.decompress(rom.Data, config_offset);
            if (configData == null || configData.Length == 0)
            {
                info.AppendLine("ERROR: Failed to decompress chipset config.");
                MapInfo = info.ToString();
                IsLoaded = true;
                return null;
            }

            // Compute the pointer table entry address for write-back
            uint mapTableBase = rom.p32(rom.RomInfo.map_map_pointer_pointer);
            _cachedMapPointerEntryAddr = (uint)(mapTableBase + mappointer_plist * 4);

            // Decompress map arrangement data
            byte[] mapData = LZ77.decompress(rom.Data, map_offset);
            if (mapData == null || mapData.Length < 2)
            {
                info.AppendLine("ERROR: Failed to decompress map data.");
                MapInfo = info.ToString();
                IsLoaded = true;
                _cachedMapData = null;
                return null;
            }

            // Cache the decompressed map data for tile selection/editing
            _cachedMapData = (byte[])mapData.Clone();

            // Map data format: first 2 bytes = width, height (in 16x16 tiles)
            int mapW = mapData[0];
            int mapH = mapData[1];

            if (mapW <= 0 || mapH <= 0 || mapW > 64 || mapH > 64)
            {
                info.AppendLine($"ERROR: Invalid map dimensions: {mapW}x{mapH}");
                MapInfo = info.ToString();
                IsLoaded = true;
                return null;
            }

            MapWidth = mapW;
            MapHeight = mapH;

            info.AppendLine($"Map Size: {mapW} x {mapH} tiles ({mapW * 16} x {mapH * 16} px)");
            info.AppendLine($"OBJ data: {objData.Length} bytes, Config: {configData.Length} bytes");

            // Build the TSA array: each 16x16 map tile maps to 4 8x8 sub-tiles via the chipset config
            // The map data (after 2-byte header) contains 16-bit tile indices.
            // Each index * 2 = offset into configData, which has 4 * 2 = 8 bytes (4 TSA entries).
            int pixelW = mapW * 16;
            int pixelH = mapH * 16;

            // sub-tile grid is 2x the map tile grid (each 16x16 tile = 2x2 8x8 sub-tiles)
            int subW = mapW * 2;
            int subH = mapH * 2;
            ushort[] tsaArray = new ushort[subW * subH];

            int x = 0, y = 0;
            for (int i = 2; i + 1 < mapData.Length; i += 2)
            {
                int m = mapData[i] | (mapData[i + 1] << 8);
                int tsaIdx = m << 1; // m * 2 = byte offset into configData (each entry is 8 bytes at m*8... actually m*2 because each sub-tile TSA is 2 bytes)

                if (tsaIdx + 7 >= configData.Length)
                    break;

                // Read 4 TSA entries (2 bytes each) for the 2x2 sub-tiles
                ushort leftTop = (ushort)(configData[tsaIdx] | (configData[tsaIdx + 1] << 8));
                ushort rightTop = (ushort)(configData[tsaIdx + 2] | (configData[tsaIdx + 3] << 8));
                ushort leftBottom = (ushort)(configData[tsaIdx + 4] | (configData[tsaIdx + 5] << 8));
                ushort rightBottom = (ushort)(configData[tsaIdx + 6] | (configData[tsaIdx + 7] << 8));

                int idx;
                idx = x + y * subW;
                if (idx < tsaArray.Length) tsaArray[idx] = leftTop;

                idx = (x + 1) + y * subW;
                if (idx < tsaArray.Length) tsaArray[idx] = rightTop;

                idx = x + (y + 1) * subW;
                if (idx < tsaArray.Length) tsaArray[idx] = leftBottom;

                idx = (x + 1) + (y + 1) * subW;
                if (idx < tsaArray.Length) tsaArray[idx] = rightBottom;

                x += 2;
                if (x >= subW)
                {
                    x = 0;
                    y += 2;
                    if (y >= subH)
                        break;
                }
            }

            // Render the TSA array to RGBA pixels
            byte[] pixels = new byte[pixelW * pixelH * 4];

            for (int si = 0; si < tsaArray.Length; si++)
            {
                ushort tsaEntry = tsaArray[si];
                int tileIndex = tsaEntry & 0x3FF;
                bool hFlip = (tsaEntry & 0x400) != 0;
                bool vFlip = (tsaEntry & 0x800) != 0;
                int palIndex = (tsaEntry >> 12) & 0xF;

                int tileX = (si % subW) * 8;
                int tileY = (si / subW) * 8;

                RenderTile4bpp(objData, tileIndex, paletteData, palIndex,
                    pixels, pixelW, tileX, tileY, hFlip, vFlip);
            }

            info.AppendLine("Rendering complete.");
            MapInfo = info.ToString();
            IsLoaded = true;
            return pixels;
        }

        /// <summary>
        /// Select a tile at the given map coordinates (in 16x16 tile units).
        /// Reads the tile ID from the cached decompressed map data.
        /// </summary>
        public bool SelectTile(int tileX, int tileY)
        {
            if (_cachedMapData == null || tileX < 0 || tileY < 0 ||
                tileX >= MapWidth || tileY >= MapHeight)
            {
                HasTileSelected = false;
                TileInfo = "";
                return false;
            }

            SelectedTileX = tileX;
            SelectedTileY = tileY;

            // Map data: 2-byte header (width, height), then u16 tile IDs row-major
            int offset = 2 + (tileY * MapWidth + tileX) * 2;
            if (offset + 1 >= _cachedMapData.Length)
            {
                HasTileSelected = false;
                TileInfo = "Tile offset out of range";
                return false;
            }

            int tileId = _cachedMapData[offset] | (_cachedMapData[offset + 1] << 8);
            SelectedTileId = tileId;
            HasTileSelected = true;
            TileInfo = $"Tile at ({tileX}, {tileY}): 0x{tileId:X04}  [map data offset: 0x{offset:X04}]";
            return true;
        }

        /// <summary>
        /// Write the current SelectedTileId back to ROM at the selected tile position.
        /// Modifies the cached decompressed map data, recompresses with LZ77,
        /// writes to ROM free space, and updates the pointer.
        /// Returns true on success.
        /// </summary>
        public bool WriteTile()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || _cachedMapData == null || !HasTileSelected)
                return false;

            if (SelectedTileX < 0 || SelectedTileY < 0 ||
                SelectedTileX >= MapWidth || SelectedTileY >= MapHeight)
                return false;

            // Update the tile in the cached map data
            int offset = 2 + (SelectedTileY * MapWidth + SelectedTileX) * 2;
            if (offset + 1 >= _cachedMapData.Length)
                return false;

            ushort newTileId = (ushort)(SelectedTileId & 0xFFFF);
            _cachedMapData[offset] = (byte)(newTileId & 0xFF);
            _cachedMapData[offset + 1] = (byte)((newTileId >> 8) & 0xFF);

            // Recompress the map data
            byte[] compressed = LZ77.compress(_cachedMapData);
            if (compressed == null || compressed.Length == 0)
            {
                TileInfo = "ERROR: LZ77 compression failed";
                return false;
            }

            // Write compressed data to ROM free space and update the pointer
            uint writeAddr = ImageImportCore.FindAndWriteData(rom, compressed);
            if (writeAddr == U.NOT_FOUND)
            {
                TileInfo = "ERROR: No free space in ROM for map data";
                return false;
            }

            // Update the pointer table entry to point to the new compressed data
            rom.write_p32(_cachedMapPointerEntryAddr, writeAddr);

            TileInfo = $"Tile at ({SelectedTileX}, {SelectedTileY}) set to 0x{newTileId:X04} — written to ROM at 0x{writeAddr:X08}";
            return true;
        }

        /// <summary>
        /// Render a single 4bpp 8x8 tile into the RGBA pixel buffer.
        /// </summary>
        static void RenderTile4bpp(byte[] tileData, int tileIndex, byte[] palette, int palIndex,
            byte[] pixels, int imageWidth, int tileX, int tileY, bool hFlip, bool vFlip)
        {
            const int bytesPerTile = 32; // 8x8 pixels at 4bpp
            int tileOffset = tileIndex * bytesPerTile;
            if (tileOffset + bytesPerTile > tileData.Length) return;

            int palOffset = palIndex * 16 * 2; // 16 colors * 2 bytes each

            for (int py = 0; py < 8; py++)
            {
                int srcY = vFlip ? (7 - py) : py;
                for (int px = 0; px < 8; px++)
                {
                    int srcX = hFlip ? (7 - px) : px;

                    int bytePos = tileOffset + srcY * 4 + srcX / 2;
                    if (bytePos >= tileData.Length) continue;

                    byte b = tileData[bytePos];
                    int colorIndex = (srcX % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);

                    int palByteOffset = palOffset + colorIndex * 2;
                    if (palByteOffset + 2 > palette.Length) continue;

                    ushort gbaColor = (ushort)(palette[palByteOffset] | (palette[palByteOffset + 1] << 8));

                    // GBA 15-bit BGR: bits 0-4=R, 5-9=G, 10-14=B
                    byte r = (byte)(((gbaColor >> 0) & 0x1F) << 3);
                    byte g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
                    byte bl = (byte)(((gbaColor >> 10) & 0x1F) << 3);
                    byte a = (byte)(colorIndex == 0 ? 0 : 255); // index 0 = transparent

                    int destX = tileX + px;
                    int destY = tileY + py;
                    if (destX >= imageWidth || destY * imageWidth + destX < 0) continue;

                    int idx = (destY * imageWidth + destX) * 4;
                    if (idx + 3 >= pixels.Length) continue;

                    pixels[idx + 0] = r;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = bl;
                    pixels[idx + 3] = a;
                }
            }
        }
    }
}
