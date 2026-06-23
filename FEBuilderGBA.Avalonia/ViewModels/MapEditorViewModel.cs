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

        // Chipset palette state (first slice of #658)
        int _selectedChipsetIndex = -1;
        string _chipsetInfo = "";

        // Cached decompressed map data for the current map
        byte[] _cachedMapData;
        uint _cachedMapPointerEntryAddr; // address of the pointer table entry for this map's data

        // Cached tileset data so the chipset palette can render without re-decompressing.
        // These three buffers are populated by LoadMapImage and reused by RenderChipsetPalette.
        byte[] _cachedObjData;
        byte[] _cachedPaletteData;
        byte[] _cachedConfigData;

        /// <summary>
        /// Test-only read-only accessor for the in-memory cache. Returns null if no map loaded.
        /// Marked <c>internal</c> because <c>FEBuilderGBA.Avalonia</c> exposes its internals
        /// to <c>FEBuilderGBA.Avalonia.Tests</c> via <c>InternalsVisibleTo</c>.
        /// </summary>
        internal byte[] GetMapDataSnapshot() => _cachedMapData == null ? null : (byte[])_cachedMapData.Clone();

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

        /// <summary>
        /// Currently-selected chipset palette index (0..1023), or -1 if none selected.
        /// When set, the paint flow uses this chipset's MAR value
        /// (<c>chipsetIndex &lt;&lt; 2</c>) for click-to-paint.
        /// </summary>
        public int SelectedChipsetIndex { get => _selectedChipsetIndex; set => SetField(ref _selectedChipsetIndex, value); }

        /// <summary>Human-readable info about the selected chipset (index + terrain).</summary>
        public string ChipsetInfo { get => _chipsetInfo; set => SetField(ref _chipsetInfo, value); }

        /// <summary>Whether a chipset is currently selected in the palette.</summary>
        public bool HasChipsetSelected => _selectedChipsetIndex >= 0;

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

            // Clear previous tile selection (chipset palette selection is preserved
            // intentionally so the user can paint across maps with the same picked chipset)
            SelectedTileX = -1;
            SelectedTileY = -1;
            SelectedTileId = 0;
            TileInfo = "";
            HasTileSelected = false;
            _cachedMapData = null;
            _cachedObjData = null;
            _cachedPaletteData = null;
            _cachedConfigData = null;

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

            // Cache the decompressed map data for tile selection/editing,
            // and cache the OBJ/PAL/CONFIG buffers so the chipset palette
            // can render without re-decompressing each load (first slice of #658).
            _cachedMapData = (byte[])mapData.Clone();
            _cachedObjData = objData;
            _cachedPaletteData = paletteData;
            _cachedConfigData = configData;

            // SelectedChipsetIndex is deliberately preserved across map loads so the
            // user can paint the same chipset across maps. But the derived
            // ChipsetInfo string (which includes terrain data) depends on the NEW
            // map's configUZ — refresh it so the UI doesn't show stale terrain info
            // from the previous map.
            if (_selectedChipsetIndex >= 0)
                SetSelectedChipsetIndex(_selectedChipsetIndex);

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
        /// Eyedropper: read the MAR value at the given tile and set
        /// <see cref="SelectedChipsetIndex"/> to the matching palette index. Returns
        /// true if the tile was in range and the chipset was set.
        /// </summary>
        public bool EyedropperAt(int tileX, int tileY)
        {
            if (!MapEditorTilesetCore.TryReadMar(_cachedMapData, MapWidth, MapHeight, tileX, tileY, out ushort mar))
                return false;
            return SetSelectedChipsetIndex(MapEditorTilesetCore.MarToChipsetIndex(mar));
        }

        /// <summary>
        /// Write the current <see cref="SelectedTileId"/> back to ROM at the selected
        /// tile position. Defers to <see cref="ApplyMapEdit"/> which guarantees the
        /// in-memory cache is only updated when the ROM write succeeds.
        /// Returns true on success.
        /// </summary>
        public bool WriteTile()
        {
            if (!HasTileSelected) return false;
            ushort newTileId = (ushort)(SelectedTileId & 0xFFFF);
            bool ok = ApplyMapEdit(SelectedTileX, SelectedTileY, newTileId, out string error, out uint writeAddr);
            if (!ok)
            {
                // ApplyMapEdit returns false in three cases: real error (sets error),
                // no-op same value, or out-of-range. Provide user feedback in each case.
                if (!string.IsNullOrEmpty(error))
                    TileInfo = "ERROR: " + error;
                else
                    TileInfo = $"No change at ({SelectedTileX}, {SelectedTileY}) — tile already 0x{newTileId:X04}";
                return false;
            }
            TileInfo = $"Tile at ({SelectedTileX}, {SelectedTileY}) set to 0x{newTileId:X04} — written to ROM at 0x{writeAddr:X08}";
            return true;
        }

        /// <summary>
        /// Paint the currently-selected palette chipset onto the map at
        /// (tileX, tileY) and commit it to ROM. Returns true if the operation
        /// produced an actual change (and was written), false on no-op
        /// (same MAR value, out-of-range coords) or failure. <see cref="TileInfo"/>
        /// is updated with a status string in both cases.
        ///
        /// <para>The cache rollback discipline (#658 v4): a successful return is the
        /// ONLY path that mutates <c>_cachedMapData</c>. Failures, exceptions, and
        /// no-ops never advance the cache.</para>
        /// </summary>
        public bool PaintTileAt(int tileX, int tileY)
        {
            if (!HasChipsetSelected)
            {
                TileInfo = "No chipset selected — pick one from the palette first";
                return false;
            }

            // Quick OOR pre-check so we can distinguish "out of range" from
            // "value unchanged" — TryStageMarEdit (inside ApplyMapEdit) returns
            // false for both, indistinguishably from the caller.
            if (tileX < 0 || tileY < 0 || tileX >= MapWidth || tileY >= MapHeight)
            {
                TileInfo = $"Click ({tileX}, {tileY}) is outside the map bounds";
                return false;
            }

            ushort newMar = MapEditorTilesetCore.ChipsetIndexToMar(SelectedChipsetIndex);
            bool ok = ApplyMapEdit(tileX, tileY, newMar, out string error, out uint writeAddr);
            if (!ok)
            {
                if (!string.IsNullOrEmpty(error))
                    TileInfo = $"Paint failed at ({tileX}, {tileY}): {error}";
                else
                    TileInfo = $"No change at ({tileX}, {tileY}) — tile already chipset #{SelectedChipsetIndex}";
                return false;
            }

            // Update selection so the manual editor reflects the painted tile
            SelectedTileX = tileX;
            SelectedTileY = tileY;
            SelectedTileId = newMar;
            HasTileSelected = true;
            TileInfo = $"Painted chipset #{SelectedChipsetIndex} (MAR 0x{newMar:X04}) at ({tileX}, {tileY}) — written at 0x{writeAddr:X08}";
            return true;
        }

        /// <summary>
        /// Set the currently-selected chipset from a click in the palette grid.
        /// Returns true if a valid chipset was selected.
        /// </summary>
        public bool SelectChipsetFromPaletteClick(int pixelX, int pixelY)
        {
            int idx = MapEditorTilesetCore.PixelToChipsetIndex(pixelX, pixelY);
            if (idx < 0)
            {
                // Outside the grid — leave selection unchanged
                return false;
            }
            return SetSelectedChipsetIndex(idx);
        }

        /// <summary>
        /// Directly set the selected chipset by index (also used by the eyedropper).
        /// </summary>
        public bool SetSelectedChipsetIndex(int idx)
        {
            if (idx < 0 || idx >= MapEditorTilesetCore.CHIPSET_COUNT) return false;
            SelectedChipsetIndex = idx;
            uint terrain = MapEditorTilesetCore.GetTerrainDataFromChipset(idx, _cachedConfigData);
            ushort mar = MapEditorTilesetCore.ChipsetIndexToMar(idx);
            string terrainStr = terrain == U.NOT_FOUND ? "?" : "0x" + terrain.ToString("X02");
            ChipsetInfo = $"Chipset #{idx} (MAR 0x{mar:X04}, terrain {terrainStr})";
            return true;
        }

        /// <summary>
        /// Re-render a single 16x16 map tile into <paramref name="rgba"/> at the
        /// pixel position corresponding to (tileX, tileY). Used by the click-to-paint
        /// flow to avoid a full LZ77 decompress + render after every paint.
        /// Returns false if the cached tileset is unavailable or coordinates are OOR.
        /// </summary>
        public bool RenderTileInto(byte[] rgba, int imageWidth, int tileX, int tileY)
        {
            if (rgba == null || _cachedMapData == null
                || _cachedObjData == null || _cachedConfigData == null || _cachedPaletteData == null)
                return false;
            if (!MapEditorTilesetCore.TryReadMar(_cachedMapData, MapWidth, MapHeight, tileX, tileY, out ushort mar))
                return false;

            int destX = tileX * 16;
            int destY = tileY * 16;
            MapEditorTilesetCore.RenderChipsetIntoBuffer(
                mar, _cachedConfigData, _cachedObjData, _cachedPaletteData,
                rgba, imageWidth, destX, destY);
            return true;
        }

        /// <summary>
        /// Render the chipset palette grid for the currently-loaded map.
        /// Returns null and zeroes out the dimensions if no map is loaded.
        /// </summary>
        public byte[] RenderChipsetPalette(out int paletteWidth, out int paletteHeight)
        {
            paletteWidth = 0;
            paletteHeight = 0;
            if (_cachedObjData == null || _cachedPaletteData == null || _cachedConfigData == null)
                return null;
            return MapEditorTilesetCore.RenderChipsetPalette(
                _cachedObjData, _cachedConfigData, _cachedPaletteData,
                out paletteWidth, out paletteHeight);
        }

        /// <summary>
        /// Single-source-of-truth ROM write path for both the manual editor and the
        /// paint flow. Stages a CLONED map-data buffer with the new MAR value,
        /// compresses it, writes it to ROM free space, and updates the pointer.
        ///
        /// <para>Order of operations is deliberate: the cache <c>_cachedMapData</c>
        /// only becomes the staged buffer AFTER the compressed write + pointer
        /// update have both succeeded. Any failure or exception leaves the
        /// previous cache and ROM in sync (no half-applied state).</para>
        /// </summary>
        /// <param name="error">Human-readable failure reason, or null on success/no-op.</param>
        /// <param name="writeAddr">ROM address of the newly-written compressed data, on success.</param>
        public bool ApplyMapEdit(int tileX, int tileY, ushort newMar, out string error, out uint writeAddr)
        {
            error = null;
            writeAddr = 0;

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                error = "No ROM loaded";
                return false;
            }
            if (_cachedMapData == null)
            {
                error = "No map loaded";
                return false;
            }

            // Stage on a CLONE — original cache untouched if anything below fails.
            if (!MapEditorTilesetCore.TryStageMarEdit(
                _cachedMapData, MapWidth, MapHeight, tileX, tileY, newMar,
                out byte[] staged, out ushort oldMar))
            {
                // No-op (same value or out-of-range): NOT an error, but no commit.
                return false;
            }

            byte[] compressed;
            try
            {
                compressed = LZ77.compress(staged);
            }
            catch (Exception ex)
            {
                error = "LZ77 compression threw: " + ex.Message;
                return false;
            }
            if (compressed == null || compressed.Length == 0)
            {
                error = "LZ77 compression returned empty";
                return false;
            }

            try
            {
                writeAddr = ImageImportCore.FindAndWriteData(rom, compressed);
            }
            catch (Exception ex)
            {
                error = "ROM write threw: " + ex.Message;
                return false;
            }
            if (writeAddr == U.NOT_FOUND)
            {
                error = "No free space in ROM for compressed map data";
                return false;
            }

            try
            {
                rom.write_p32(_cachedMapPointerEntryAddr, writeAddr);
            }
            catch (Exception ex)
            {
                error = "Pointer write threw: " + ex.Message;
                return false;
            }

            // SUCCESS — swap the cache in only now.
            _cachedMapData = staged;
            _ = oldMar; // (oldMar would be useful for undo of the in-memory cache, but
                       // the caller's undo scope covers ROM; cache always tracks ROM here.)
            return true;
        }

        /// <summary>
        /// Apply a full-grid replacement to the map from a row-major array of MAR values
        /// (as produced by <see cref="MapExportCsv.Parse"/>). Mirrors
        /// <see cref="ApplyMapEdit"/> but for the entire grid at once under a single
        /// LZ77 compress + write + repoint operation.
        ///
        /// <para>The cache <c>_cachedMapData</c> only advances AFTER the ROM write and
        /// pointer update both succeed — same fault-safe discipline as
        /// <see cref="ApplyMapEdit"/>.</para>
        /// </summary>
        /// <param name="mars">Row-major MAR values; must have length == width*height.</param>
        /// <param name="width">Expected map width (must equal <see cref="MapWidth"/>).</param>
        /// <param name="height">Expected map height (must equal <see cref="MapHeight"/>).</param>
        /// <param name="error">Human-readable failure reason, or null on success.</param>
        /// <param name="writeAddr">ROM address of the newly-written compressed data, on success.</param>
        public bool ApplyMapGrid(ushort[] mars, int width, int height, out string error, out uint writeAddr)
        {
            error = null;
            writeAddr = 0;

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                error = "No ROM loaded";
                return false;
            }
            if (_cachedMapData == null)
            {
                error = "No map loaded";
                return false;
            }

            if (width != MapWidth || height != MapHeight)
            {
                // Format-agnostic wording: this path serves both the CSV and the .tmx
                // (Tiled) importers, so don't name a specific file format here.
                error = $"imported map size {width}x{height} does not match the selected map ({MapWidth}x{MapHeight}). Resize is not supported — select a matching map or edit the imported file.";
                return false;
            }

            if (mars == null || mars.Length != width * height)
            {
                error = $"mars array is null or wrong length (expected {width * height})";
                return false;
            }

            if (!MapEditorTilesetCore.TryStageGridEdit(
                _cachedMapData, width, height, mars,
                out byte[] staged, out string stageErr))
            {
                error = stageErr;
                return false;
            }

            byte[] compressed;
            try
            {
                compressed = LZ77.compress(staged);
            }
            catch (Exception ex)
            {
                error = "LZ77 compression threw: " + ex.Message;
                return false;
            }
            if (compressed == null || compressed.Length == 0)
            {
                error = "LZ77 compression returned empty";
                return false;
            }

            try
            {
                writeAddr = ImageImportCore.FindAndWriteData(rom, compressed);
            }
            catch (Exception ex)
            {
                error = "ROM write threw: " + ex.Message;
                return false;
            }
            if (writeAddr == U.NOT_FOUND)
            {
                error = "No free space in ROM for compressed map data";
                return false;
            }

            try
            {
                rom.write_p32(_cachedMapPointerEntryAddr, writeAddr);
            }
            catch (Exception ex)
            {
                error = "Pointer write threw: " + ex.Message;
                return false;
            }

            // SUCCESS — swap the cache in only now.
            _cachedMapData = staged;
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
