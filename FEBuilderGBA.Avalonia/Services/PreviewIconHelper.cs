using System;
using System.IO;

using global::Avalonia.Media.Imaging;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Helper to convert IImage (SkiaSharp-backed) to Avalonia Bitmap.
    /// </summary>
    public static class ImageConversionHelper
    {
        /// <summary>
        /// Convert an IImage to an Avalonia Bitmap via PNG encoding.
        /// Returns null if the input is null or encoding fails.
        /// </summary>
        public static Bitmap? ToAvaloniaBitmap(IImage? image)
        {
            if (image == null) return null;
            try
            {
                byte[] pngData = image.EncodePng();
                if (pngData == null || pngData.Length == 0) return null;
                using var ms = new MemoryStream(pngData);
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Helper to render small preview icons for unit portraits, item icons, and class wait icons.
    /// Used by editor views to show a sidebar preview next to the address list.
    /// </summary>
    public static class PreviewIconHelper
    {
        /// <summary>
        /// Load a mini portrait (32x32) for the given portrait ID.
        /// Returns null on failure.
        /// </summary>
        public static IImage LoadPortraitMini(uint portraitId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || portraitId == 0) return null;

            try
            {
                uint ptr = rom.RomInfo.portrait_pointer;
                if (ptr == 0) return null;

                uint portraitBase = rom.p32(ptr);
                if (!U.isSafetyOffset(portraitBase)) return null;

                uint dataSize = rom.RomInfo.portrait_datasize;
                if (dataSize == 0) dataSize = 28;

                uint portraitAddr = portraitBase + portraitId * dataSize;
                if (portraitAddr + dataSize > (uint)rom.Data.Length) return null;

                uint imgPtr = rom.u32(portraitAddr + 4);  // offset 4 = map/mini face
                uint palPtr = rom.u32(portraitAddr + 8);

                if (!U.isPointer(imgPtr) || !U.isPointer(palPtr)) return null;

                uint imgAddr = imgPtr - 0x08000000;
                uint palAddr = palPtr - 0x08000000;

                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                // FE6's mini-face image data is stored uncompressed; FE7/FE8
                // store it LZ77-compressed. Mirrors WinForms version-split
                // in ImagePortraitFE6Form.DrawPortraitFE6Map (uses raw bytes
                // via ByteToImage16Tile) vs ImagePortraitForm.DrawPortraitMap
                // (uses LZ77.decompress). Issue #361 follow-up: required to
                // make FE6 SupportTalkView render portraits at all.
                bool isCompressed = rom.RomInfo.version != 6;
                return ImageUtilCore.LoadROMTiles4bpp(imgAddr, palette, 4, 4, isCompressed);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load two mini portraits (32x32 each) horizontally stitched into a
        /// single 64x32 RGBA <see cref="IImage"/>. Used by the Support Talk
        /// editor list to show both members of a support pair (issue #361).
        ///
        /// Why RGBA (not indexed): each portrait has its OWN 16-color palette,
        /// so two portraits cannot share a single indexed image. The output is
        /// composed by expanding each portrait's indexed pixels through its
        /// palette into RGBA and blitting into a fresh RGBA canvas.
        ///
        /// Behavior:
        ///   - If a portrait ID is 0, OR <see cref="LoadPortraitMini"/> fails
        ///     for that ID, the corresponding 32x32 half stays fully
        ///     transparent (RGBA 0,0,0,0).
        ///   - Returns null if both portrait IDs are 0 (nothing to render),
        ///     or if <see cref="CoreState.ImageService"/> is unavailable.
        /// </summary>
        public static IImage LoadPortraitMiniPair(uint portraitId1, uint portraitId2)
        {
            if (portraitId1 == 0 && portraitId2 == 0) return null;
            var svc = CoreState.ImageService;
            if (svc == null) return null;

            // Geometry: two 32x32 mini portraits side-by-side -> 64x32 RGBA.
            // HALF_W is also the dstX for the right half.
            const int HALF_W = 32;
            const int HALF_H = 32;
            const int OUT_W = 2 * HALF_W;
            const int OUT_H = HALF_H;
            const int OUT_BYTES = OUT_W * OUT_H * 4;

            byte[] outRgba = new byte[OUT_BYTES];  // zero-initialized => transparent

            // Per-half failure isolation (Copilot review): each portrait may
            // throw inside LoadPortraitMini / palette decoding for one ID
            // without invalidating the other half. Without per-call try/catch
            // a single bad portrait blanks the whole pair, violating the
            // documented "each half is independent" contract.
            TryBlitPortraitHalfRgba(portraitId1, outRgba, dstX: 0,      canvasW: OUT_W);
            TryBlitPortraitHalfRgba(portraitId2, outRgba, dstX: HALF_W, canvasW: OUT_W);

            // Dispose the canvas on the failure path (Copilot review): IImage
            // is IDisposable and SetPixelData can throw on size mismatch.
            IImage canvas = null;
            try
            {
                canvas = svc.CreateImage(OUT_W, OUT_H);
                canvas.SetPixelData(outRgba);
                var ret = canvas;
                canvas = null;  // ownership transferred to caller
                return ret;
            }
            catch
            {
                canvas?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Render one portrait (by ID) into the given 32x32 sub-region of an
        /// RGBA pair-canvas, starting at <paramref name="dstX"/> in the
        /// <paramref name="canvasW"/>-wide canvas. Leaves the region
        /// transparent if the portrait is missing or fails to load.
        ///
        /// Per-call try/catch ensures one portrait's failure does NOT blank
        /// the other half (Copilot review item).
        /// </summary>
        static void TryBlitPortraitHalfRgba(uint portraitId, byte[] dstRgba, int dstX, int canvasW)
        {
            try
            {
                BlitPortraitHalfRgba(portraitId, dstRgba, dstX, canvasW);
            }
            catch
            {
                // Half stays transparent. Other half is unaffected.
            }
        }

        static void BlitPortraitHalfRgba(uint portraitId, byte[] dstRgba, int dstX, int canvasW)
        {
            if (portraitId == 0) return;
            using IImage solo = LoadPortraitMini(portraitId);
            if (solo == null) return;
            if (solo.Width != 32 || solo.Height != 32) return;
            if (!solo.IsIndexed) return;

            byte[] indices = solo.GetPixelData();
            if (indices == null || indices.Length < 32 * 32) return;
            byte[] paletteRgba = solo.GetPaletteRGBA();
            if (paletteRgba == null || paletteRgba.Length == 0) return;
            if (paletteRgba.Length % 4 != 0) return;
            int paletteColors = paletteRgba.Length / 4;

            // Verify destination range so we never index out-of-bounds even
            // if a future caller passes a wrong canvasW/dstX combination.
            if ((long)(31 * canvasW + dstX + 31) * 4 + 3 >= dstRgba.Length) return;

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    // idx is byte (unsigned), so it is always >= 0 — only
                    // the upper-bound check is needed (Copilot review).
                    int idx = indices[y * 32 + x];
                    if (idx >= paletteColors) continue;
                    int palOff = idx * 4;
                    int dstOff = (y * canvasW + dstX + x) * 4;
                    dstRgba[dstOff]     = paletteRgba[palOff];
                    dstRgba[dstOff + 1] = paletteRgba[palOff + 1];
                    dstRgba[dstOff + 2] = paletteRgba[palOff + 2];
                    dstRgba[dstOff + 3] = paletteRgba[palOff + 3];
                }
            }
        }

        /// <summary>
        /// Resolve the portrait ID for a unit: returns the unit's own portrait ID,
        /// or falls back to the class portrait if the unit has none (portrait ID 0).
        /// </summary>
        public static uint ResolveUnitPortraitId(uint unitAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || !U.isSafetyOffset(unitAddr + 7)) return 0;

            try
            {
                uint portraitId = rom.u16(unitAddr + 6);
                if (portraitId == 0)
                    portraitId = GetClassPortraitId(rom.u8(unitAddr + 5));
                return portraitId;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Resolve the portrait ID for a unit by unit ID (not address).
        /// Looks up the unit struct from the unit table, then calls ResolveUnitPortraitId.
        /// </summary>
        public static uint ResolveUnitPortraitIdByUnitId(uint unitId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || unitId == 0) return 0;

            try
            {
                uint unitPtr = rom.RomInfo.unit_pointer;
                if (unitPtr == 0) return 0;

                uint unitBase = rom.p32(unitPtr);
                if (!U.isSafetyOffset(unitBase)) return 0;

                uint unitSize = rom.RomInfo.unit_datasize;
                // unitId is 0-based (matches InputFormRef.IDToAddr: base + id * size)
                uint unitAddr = unitBase + unitId * unitSize;
                if (unitAddr + unitSize > (uint)rom.Data.Length) return 0;

                return ResolveUnitPortraitId(unitAddr);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get the portrait ID associated with a class ID (offset +8 in class struct).
        /// Returns 0 if the class has no portrait.
        /// </summary>
        public static uint GetClassPortraitId(uint classId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || classId == 0) return 0;

            try
            {
                uint classPtr = rom.RomInfo.class_pointer;
                if (classPtr == 0) return 0;

                uint classBase = rom.p32(classPtr);
                if (!U.isSafetyOffset(classBase)) return 0;

                uint classSize = rom.RomInfo.class_datasize;
                uint classAddr = classBase + classId * classSize;
                if (classAddr + classSize > (uint)rom.Data.Length) return 0;

                return rom.u16(classAddr + 8);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Load a 16x16 item icon for the given icon index (B29 field).
        /// Returns null on failure.
        /// </summary>
        public static IImage LoadItemIcon(uint iconIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            try
            {
                uint ptr = rom.RomInfo.icon_pointer;
                if (ptr == 0) return null;

                uint baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return null;

                // Load palette
                uint palPtr = rom.RomInfo.icon_palette_pointer;
                if (palPtr == 0) return null;

                uint palGba = rom.u32(palPtr);
                if (!U.isPointer(palGba)) return null;

                byte[] palette = ImageUtilCore.GetPalette(U.toOffset(palGba), 16);
                if (palette == null) return null;

                // Each icon is 128 bytes (2x2 tiles at 4bpp = 16x16 pixels)
                uint iconAddr = baseAddr + iconIndex * 128;
                if (iconAddr + 128 > (uint)rom.Data.Length) return null;

                return CoreState.ImageService?.Decode4bppTiles(rom.Data, (int)iconAddr, 16, 16, palette);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load the class face portrait ("class card") by portrait ID.
        /// Mirrors the WinForms <c>L_8_PORTRAIT_CLASS</c> picture box behavior
        /// powered by <c>InputFormRef</c>'s <c>PORTRAIT:CLASS</c> linktype, which
        /// calls <c>ImagePortraitForm.DrawPortraitClass(face_id)</c> for FE7/8
        /// and <c>ImagePortraitFE6Form.DrawPortraitClassFE6(id)</c> for FE6.
        ///
        /// Portrait struct layout differs by version (issue #357 plan v2):
        ///   FE6:   16-byte struct, D0=unit_face, D4=map_face, D8=palette.
        ///          The class card is rendered from D0 with palette D8 ONLY
        ///          when D4==0 (entry is a pure class card, not a regular
        ///          unit portrait).
        ///   FE7/8: 28-byte struct, D8=palette, D16=class_card. Renders D16
        ///          with palette D8.
        ///
        /// Returns null on any error / out-of-range / missing data.
        /// </summary>
        public static IImage LoadClassFacePortrait(uint portraitId)
        {
            if (portraitId == 0) return null;
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            try
            {
                // Dereference the portrait pointer-table location to get the
                // portrait-table base. RomInfo.portrait_pointer is the address
                // of the pointer, not the base itself.
                uint ptr = rom.RomInfo.portrait_pointer;
                if (ptr == 0) return null;

                uint baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return null;

                uint dataSize = rom.RomInfo.portrait_datasize;
                if (dataSize == 0)
                {
                    // Defensive fallback mirroring LoadPortraitMini for FE7/8:
                    // when RomInfo did not declare a portrait struct size (e.g.,
                    // a malformed/partial RomInfo) use the canonical struct size
                    // for the current version (16 for FE6, 28 for FE7/8) instead
                    // of bailing out. PR #471 Copilot inline review fix.
                    dataSize = rom.RomInfo.version == 6 ? 16u : 28u;
                }

                uint addr = baseAddr + portraitId * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) return null;

                if (rom.RomInfo.version == 6)
                {
                    // FE6 — 16-byte struct, mirror DrawPortraitClassFE6.
                    uint unitFace = rom.u32(addr + 0);
                    uint mapFace = rom.u32(addr + 4);
                    uint palette = rom.u32(addr + 8);
                    // WinForms only renders the class card when map_face == 0.
                    if (mapFace != 0) return null;
                    if (!U.isPointer(unitFace) || !U.isPointer(palette)) return null;
                    return PortraitRendererCore.DrawPortraitClass(unitFace, palette);
                }
                else
                {
                    // FE7/8 — 28-byte struct, D16 = class card pointer.
                    uint palette = rom.u32(addr + 8);
                    uint classCard = rom.u32(addr + 16);
                    if (!U.isPointer(classCard) || !U.isPointer(palette)) return null;
                    return PortraitRendererCore.DrawPortraitClass(classCard, palette);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a class wait icon by class ID. Resolves class ID -> wait icon index (offset +6) -> loads icon.
        /// </summary>
        public static IImage LoadClassWaitIconByClassId(uint classId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || classId == 0) return null;
            try
            {
                uint classPtr = rom.RomInfo.class_pointer;
                if (classPtr == 0) return null;
                uint classBase = rom.p32(classPtr);
                if (!U.isSafetyOffset(classBase)) return null;
                uint classSize = rom.RomInfo.class_datasize;
                uint classAddr = classBase + classId * classSize;
                if (classAddr + classSize > (uint)rom.Data.Length) return null;
                uint waitIconIndex = rom.u8(classAddr + 6);
                if (waitIconIndex == 0) return null;
                return LoadClassWaitIcon(waitIconIndex);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load an item icon by item ID. Resolves item ID -> icon index (offset +29) -> loads icon.
        /// </summary>
        public static IImage LoadItemIconByItemId(uint itemId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || itemId == 0) return null;
            try
            {
                uint itemPtr = rom.RomInfo.item_pointer;
                if (itemPtr == 0) return null;
                uint itemBase = rom.p32(itemPtr);
                if (!U.isSafetyOffset(itemBase)) return null;
                uint itemSize = rom.RomInfo.item_datasize;
                uint itemAddr = itemBase + itemId * itemSize;
                if (itemAddr + itemSize > (uint)rom.Data.Length) return null;
                uint iconIndex = rom.u8(itemAddr + 29);
                return LoadItemIcon(iconIndex);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load a class wait icon (map sprite) for the given wait icon index (B6 field).
        /// The wait icon table stores compressed 16x16 (2x2 tile) sprites per entry.
        /// Returns null on failure.
        /// </summary>
        public static IImage LoadClassWaitIcon(uint waitIconIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            try
            {
                uint ptr = rom.RomInfo.unit_wait_icon_pointer;
                if (ptr == 0) return null;

                uint baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return null;

                // Each entry in the unit wait icon table is 8 bytes:
                //   +0: flags (4 bytes), with byte at +2 indicating animation type
                //   +4: sprite pointer (4 bytes, GBA pointer to LZ77-compressed tile data)
                uint entryAddr = baseAddr + waitIconIndex * 8;
                if (entryAddr + 8 > (uint)rom.Data.Length) return null;

                // Read animation type from byte at offset +2 to determine sprite dimensions
                byte animType = (byte)rom.u8(entryAddr + 2);

                uint spriteGba = rom.u32(entryAddr + 4);
                if (!U.isPointer(spriteGba)) return null;

                uint spriteAddr = U.toOffset(spriteGba);
                if (!U.isSafetyOffset(spriteAddr)) return null;

                // Palette: use the unit icon palette address
                uint palAddr = rom.RomInfo.unit_icon_palette_address;
                if (palAddr == 0 || !U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                // Determine tile dimensions based on animation type (matches WinForms):
                // Type 0: 16x16 (2x2 tiles)
                // Type 1: 16x24 (2x3 tiles)
                // Type 2: 32x32 (4x4 tiles)
                int tilesW = animType == 2 ? 4 : 2;
                int tilesH = animType == 0 ? 2 : animType == 1 ? 3 : 4;

                return ImageUtilCore.LoadROMTiles4bpp(spriteAddr, palette, tilesW, tilesH, true);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a move icon for the given move icon index.
        /// Move icon IDs are 1-based (subtracts 1 to get table index), matching WinForms DrawMoveUnitIconBitmap.
        /// Entry structure: 8 bytes, GBA pointer at offset +0 (pic data), LZ77-compressed 4bpp.
        /// Returns the first frame (32x32) of the move animation, or null on failure.
        /// </summary>
        public static IImage LoadMoveIcon(uint moveIconIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || moveIconIndex == 0) return null;

            try
            {
                // Move icon IDs are 1-based
                uint tableIndex = moveIconIndex - 1;

                uint ptr = rom.RomInfo.unit_move_icon_pointer;
                if (ptr == 0) return null;

                uint baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return null;

                // Each entry is 8 bytes: +0 = GBA pointer to LZ77 pic data, +4 = flags/animation
                uint entryAddr = baseAddr + tableIndex * 8;
                if (entryAddr + 8 > (uint)rom.Data.Length) return null;

                uint picGba = rom.u32(entryAddr + 0);
                if (!U.isPointer(picGba)) return null;

                uint picAddr = U.toOffset(picGba);
                if (!U.isSafetyOffset(picAddr)) return null;

                // Palette: use the unit icon palette address
                uint palAddr = rom.RomInfo.unit_icon_palette_address;
                if (palAddr == 0 || !U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                // Decompress and render at 4x4 tiles (32x32), first frame only (step=0)
                byte[] imageUZ = LZ77.decompress(rom.Data, picAddr);
                if (imageUZ == null || imageUZ.Length == 0) return null;

                IImageService svc = CoreState.ImageService;
                if (svc == null) return null;

                // Render at 4 tiles wide (32px), compute height from data
                int width = 4 * 8; // 32 pixels
                int height = 4 * 8; // 32 pixels for first frame
                int bytesNeeded = (width / 8) * (height / 8) * 32; // 4bpp tile data
                if (imageUZ.Length < bytesNeeded)
                {
                    // If not enough data for full 32x32, use what we have
                    int totalTiles = imageUZ.Length / 32;
                    if (totalTiles <= 0) return null;
                    int tilesX = 4;
                    int tilesY = Math.Max(1, (totalTiles + tilesX - 1) / tilesX);
                    height = tilesY * 8;
                }

                return svc.Decode4bppTiles(imageUZ, 0, width, height, palette);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Create a solid color swatch image from a GBA BGR555 color value.
        /// Returns a 16x16 solid-color IImage.
        /// </summary>
        public static IImage CreateColorSwatch(uint gbaColor)
        {
            IImageService svc = CoreState.ImageService;
            if (svc == null) return null;

            try
            {
                // Convert GBA BGR555 to RGB
                byte r = (byte)(((gbaColor) & 0x1F) << 3);
                byte g = (byte)(((gbaColor >> 5) & 0x1F) << 3);
                byte b = (byte)(((gbaColor >> 10) & 0x1F) << 3);

                const int size = 16;
                var image = svc.CreateImage(size, size);
                byte[] pixels = new byte[size * size * 4];
                for (int i = 0; i < size * size; i++)
                {
                    pixels[i * 4 + 0] = r;
                    pixels[i * 4 + 1] = g;
                    pixels[i * 4 + 2] = b;
                    pixels[i * 4 + 3] = 255;
                }
                image.SetPixelData(pixels);
                return image;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a battle animation thumbnail by animation ID.
        /// Renders the first frame tile sheet of the animation, scaled to a small icon.
        /// Animation IDs are 1-based (same as WinForms).
        /// Returns null if the animation cannot be loaded.
        /// </summary>
        public static IImage LoadBattleAnimeThumbnail(uint animeId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || animeId == 0) return null;

            try
            {
                uint pointer = rom.RomInfo.image_battle_animelist_pointer;
                if (pointer == 0) return null;

                uint tableBase = rom.p32(pointer);
                if (!U.isSafetyOffset(tableBase, rom)) return null;

                const uint ANIME_RECORD_SIZE = 32;
                uint id = animeId - 1;  // 1-based to 0-based
                uint addr = tableBase + id * ANIME_RECORD_SIZE;
                if (addr + ANIME_RECORD_SIZE > (uint)rom.Data.Length) return null;

                // Use BattleAnimeRendererCore to render the tile sheet
                return BattleAnimeRendererCore.RenderAnimationTileSheet(addr, 8);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a BG image thumbnail from a BG entry address.
        /// BG entry layout: P0=image pointer, P4=TSA pointer, P8=palette pointer.
        /// Returns a decoded image (full-size, caller should use for thumbnail), or null.
        /// </summary>
        public static IImage LoadBGThumbnail(uint entryAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || entryAddr == 0) return null;

            try
            {
                uint p0 = rom.u32(entryAddr + 0);
                uint p4 = rom.u32(entryAddr + 4);
                uint p8 = rom.u32(entryAddr + 8);

                if (!U.isPointer(p0) || !U.isPointer(p8)) return null;
                uint imgAddr = U.toOffset(p0);
                uint palAddr = U.toOffset(p8);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 256);
                if (palette == null || palette.Length == 0) return null;

                if (U.isPointer(p4))
                {
                    uint tsaAddr = U.toOffset(p4);
                    if (U.isSafetyOffset(tsaAddr))
                    {
                        int tsaLen = Math.Min(32 * 20 * 2 + 4, (int)((uint)rom.Data.Length - tsaAddr));
                        if (tsaLen > 0)
                        {
                            byte[] tsaData = new byte[tsaLen];
                            Array.Copy(rom.Data, tsaAddr, tsaData, 0, tsaLen);
                            return ImageUtilCore.DecodeHeaderTSA(tileData, tsaData, palette, 32, 20);
                        }
                    }
                }

                // Fallback: P4 is not a pointer. When BG256Color patch is installed,
                // P4 is used as a flag (0/1) for 256/224-color 8bpp mode.
                // Since 8bpp decode is not available in Core, return null
                // rather than producing a wrong 4bpp thumbnail.
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a CG image thumbnail from a CG entry address.
        /// CG entry layout (FE8): P0=image pointer, P4=TSA pointer, P8=palette pointer.
        /// Returns a decoded image, or null.
        /// </summary>
        public static IImage LoadCGThumbnail(uint entryAddr)
        {
            // CG uses the same 3-pointer layout as BG
            return LoadBGThumbnail(entryAddr);
        }

        /// <summary>
        /// Load a CG image thumbnail for FE7U, which has a different entry layout.
        /// FE7U CG entry: B0=type, B1-B3=reserved, P4=image, P8=TSA, P12=palette.
        /// Returns a decoded image, or null.
        /// </summary>
        public static IImage LoadCGFE7UThumbnail(uint entryAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || entryAddr == 0) return null;

            try
            {
                uint p4 = rom.u32(entryAddr + 4);
                uint p8 = rom.u32(entryAddr + 8);
                uint p12 = rom.u32(entryAddr + 12);

                if (!U.isPointer(p4) || !U.isPointer(p12)) return null;
                uint imgAddr = U.toOffset(p4);
                uint palAddr = U.toOffset(p12);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 256);
                if (palette == null || palette.Length == 0) return null;

                if (U.isPointer(p8))
                {
                    uint tsaAddr = U.toOffset(p8);
                    if (U.isSafetyOffset(tsaAddr))
                    {
                        int tsaLen = Math.Min(32 * 20 * 2 + 4, (int)((uint)rom.Data.Length - tsaAddr));
                        if (tsaLen > 0)
                        {
                            byte[] tsaData = new byte[tsaLen];
                            Array.Copy(rom.Data, tsaAddr, tsaData, 0, tsaLen);
                            return ImageUtilCore.DecodeHeaderTSA(tileData, tsaData, palette, 32, 20);
                        }
                    }
                }

                // Fallback: render raw tiles
                if (CoreState.ImageService == null) return null;
                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;
                int tilesX = 30;
                int tilesY = Math.Max(1, (totalTiles + tilesX - 1) / tilesX);
                return CoreState.ImageService.Decode4bppTiles(tileData, 0, tilesX * 8, tilesY * 8, palette);
            }
            catch
            {
                return null;
            }
        }
        /// <summary>
        /// Load item icon with the weapon palette instead of the normal item palette.
        /// Used for attribute (affinity) icons which share item icon graphics but use
        /// the weapon palette (system_weapon_icon_palette_pointer).
        /// Matches WinForms ImageItemIconForm.DrawIconWhereID_UsingWeaponPalette.
        /// </summary>
        public static IImage LoadItemIconWithWeaponPalette(uint iconIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            try
            {
                uint ptr = rom.RomInfo.icon_pointer;
                if (ptr == 0) return null;

                uint baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return null;

                // Load weapon palette instead of normal item palette
                uint palPtr = rom.RomInfo.system_weapon_icon_palette_pointer;
                if (palPtr == 0) return null;

                uint palGba = rom.u32(palPtr);
                if (!U.isPointer(palGba)) return null;

                byte[] palette = ImageUtilCore.GetPalette(U.toOffset(palGba), 16);
                if (palette == null) return null;

                // Each icon is 128 bytes (2x2 tiles at 4bpp = 16x16 pixels)
                uint iconAddr = baseAddr + iconIndex * 128;
                if (iconAddr + 128 > (uint)rom.Data.Length) return null;

                return CoreState.ImageService?.Decode4bppTiles(rom.Data, (int)iconAddr, 16, 16, palette);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a single 16x16 weapon-type icon by weapon type ID.
        /// Mirrors WinForms <c>ImageSystemIconForm.WeaponIcon(type)</c>:
        ///   FE6   -> source sub-region at (type+6)*16, y=8 from the decompressed sheet
        ///   FE7/8 -> source sub-region at (type+3)*16, y=0
        /// The sheet is LZ77-compressed and palette is read from
        /// <c>system_weapon_icon_palette_pointer</c>. Returns null on failure.
        /// Issue #370.
        /// </summary>
        public static IImage LoadWeaponTypeIcon(uint type)
        {
            return LoadWeaponTypeIconInternal(type, outWidth: 16);
        }

        /// <summary>
        /// Internal helper for <see cref="LoadWeaponTypeIcon"/> and the
        /// "pair" overload. <paramref name="outWidth"/> must be 16 (single
        /// icon) or 32 (pair). Returns null on ROM/service failure.
        /// </summary>
        static IImage LoadWeaponTypeIconInternal(uint type, int outWidth)
        {
            // Reuses the pair-icon path by sentinel-blanking the right half
            // and then cropping the output to the requested width. Cropping
            // is a row-by-row Buffer.BlockCopy of the 16-wide left half.
            using IImage pair = LoadWeaponTypePairIcon(type, 0xFFFFFFFFu);
            if (pair == null) return null;
            if (outWidth == 32) return CloneIndexedImage(pair);
            if (outWidth != 16) return null;

            var svc = CoreState.ImageService;
            if (svc == null) return null;

            byte[] pairData = pair.GetPixelData();
            byte[] cropData = new byte[16 * 16];
            for (int y = 0; y < 16; y++)
            {
                Buffer.BlockCopy(pairData, y * 32, cropData, y * 16, 16);
            }
            var outImage = svc.CreateIndexedImage(16, 16, pair.GetPaletteGBA(), 16);
            outImage.SetPixelData(cropData);
            return outImage;
        }

        /// <summary>Create a fresh indexed copy of <paramref name="src"/>.</summary>
        static IImage CloneIndexedImage(IImage src)
        {
            var svc = CoreState.ImageService;
            if (svc == null) return null;
            var copy = svc.CreateIndexedImage(src.Width, src.Height, src.GetPaletteGBA(), 16);
            copy.SetPixelData(src.GetPixelData());
            return copy;
        }

        /// <summary>
        /// Load a horizontally-stitched 32x16 icon containing two weapon-type
        /// icons side-by-side (left = <paramref name="type1"/>, right =
        /// <paramref name="type2"/>). This mirrors WinForms
        /// <c>ListBoxEx.DrawWeaponTypeIcon2AndText</c> which draws two
        /// <c>ImageSystemIconForm.WeaponIcon</c> icons in the list row.
        ///
        /// Implementation notes:
        ///   * The full weapon icon sheet is an LZ77-compressed 4bpp tile
        ///     bitmap rendered as an indexed <see cref="IImage"/>.
        ///   * For each half, we copy palette indices from the source
        ///     sub-region into a fresh 32x16 indexed image that shares the
        ///     same palette (no RGBA conversion needed). If a type ID is
        ///     out-of-bounds the corresponding half stays zero (transparent).
        ///   * If <paramref name="type2"/> is <c>0xFFFFFFFF</c> the right half
        ///     is left blank — used by the single-icon overload.
        ///
        /// Returns null on ROM/service failure. Issue #370.
        /// </summary>
        public static IImage LoadWeaponTypePairIcon(uint type1, uint type2)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;
            var svc = CoreState.ImageService;
            if (svc == null) return null;

            try
            {
                uint imagePtr = rom.RomInfo.system_weapon_icon_pointer;
                uint palPtr = rom.RomInfo.system_weapon_icon_palette_pointer;
                if (imagePtr == 0 || palPtr == 0) return null;

                uint imageGba = rom.u32(imagePtr);
                uint palGba = rom.u32(palPtr);
                if (!U.isPointer(imageGba) || !U.isPointer(palGba)) return null;

                uint imageOffset = U.toOffset(imageGba);
                uint palOffset = U.toOffset(palGba);
                if (!U.isSafetyOffset(imageOffset) || !U.isSafetyOffset(palOffset)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palOffset, 16);
                if (palette == null) return null;

                // Sheet dimensions and source Y differ by ROM version:
                //   FE6:   32x3 tiles (256x24) and weapon icons start at row 1 (y=8),
                //          column offset (type+6)*16.
                //   FE7/8: 32x2 tiles (256x16) and weapon icons start at row 0 (y=0),
                //          column offset (type+3)*16.
                // Mirrors WinForms `ImageSystemIconForm.BaseWeaponImage` +
                // `WeaponIcon`.
                bool isFE6 = rom.RomInfo.version == 6;
                int sheetTilesY = isFE6 ? 3 : 2;
                int srcY = isFE6 ? 8 : 0;
                int columnAddend = isFE6 ? 6 : 3;

                using IImage sheet = ImageUtilCore.LoadROMTiles4bpp(
                    imageOffset, palette, 32, sheetTilesY, isCompressed: true);
                if (sheet == null) return null;

                int sheetW = sheet.Width;
                int sheetH = sheet.Height;
                byte[] sheetIndices = sheet.GetPixelData();
                if (sheetIndices == null || sheetIndices.Length < sheetW * sheetH) return null;

                const int OUT_W = 32;
                const int OUT_H = 16;
                byte[] outIndices = new byte[OUT_W * OUT_H];

                // Copy half[0] = type1 into outIndices[x in 0..15]
                // Copy half[1] = type2 into outIndices[x in 16..31]
                CopyWeaponHalf(sheetIndices, sheetW, sheetH, type1, srcY, columnAddend,
                    outIndices, OUT_W, dstX: 0);
                if (type2 != 0xFFFFFFFFu)
                {
                    CopyWeaponHalf(sheetIndices, sheetW, sheetH, type2, srcY, columnAddend,
                        outIndices, OUT_W, dstX: 16);
                }

                var outImage = svc.CreateIndexedImage(OUT_W, OUT_H, sheet.GetPaletteGBA(), 16);
                outImage.SetPixelData(outIndices);
                return outImage;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Copy a 16x16 weapon-type sub-region from the source sheet into the
        /// destination index buffer at <paramref name="dstX"/>. Out-of-bounds
        /// source regions leave the destination zero (transparent index).
        /// </summary>
        static void CopyWeaponHalf(byte[] sheetIndices, int sheetW, int sheetH,
            uint type, int srcY, int columnAddend,
            byte[] outIndices, int outW, int dstX)
        {
            // Type must be < 32 (sheet is 32 tiles wide at 16px/tile) AND
            // (type + columnAddend) must keep the 16-wide region within the sheet.
            if (type > 31) return;
            int srcX = ((int)type + columnAddend) * 16;
            if (srcX < 0 || srcX + 16 > sheetW) return;
            if (srcY < 0 || srcY + 16 > sheetH) return;

            for (int y = 0; y < 16; y++)
            {
                int srcRow = (srcY + y) * sheetW + srcX;
                int dstRow = y * outW + dstX;
                Buffer.BlockCopy(sheetIndices, srcRow, outIndices, dstRow, 16);
            }
        }

        /// <summary>
        /// Load a 16x16 skill icon for the SkillSystem patch.
        /// Each skill icon is 128 bytes of 4bpp tile data (16x16 pixels).
        /// Uses a fixed palette at ROM address 0x22370 (pointer to palette data).
        /// </summary>
        /// <param name="index">Skill index (0-based)</param>
        /// <param name="iconBaseAddress">Base address of the skill icon tile data in ROM</param>
        public static IImage LoadSkillIcon(uint index, uint iconBaseAddress)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || iconBaseAddress == 0) return null;

            try
            {
                const uint TILE_SIZE = 128; // 16x16 4bpp = 128 bytes
                uint iconAddr = iconBaseAddress + index * TILE_SIZE;
                if (iconAddr + TILE_SIZE > (uint)rom.Data.Length) return null;

                // Skill palette is at a fixed ROM pointer (same for all game versions with SkillSystem)
                const uint SKILL_PALETTE_POINTER = 0x22370;
                if (!U.isSafetyOffset(SKILL_PALETTE_POINTER + 3)) return null;

                uint palAddr = rom.p32(SKILL_PALETTE_POINTER);
                if (!U.isSafetyOffset(palAddr)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                return CoreState.ImageService?.Decode4bppTiles(rom.Data, (int)iconAddr, 16, 16, palette);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Find the SkillSystem icon base address by searching for known binary patterns.
        /// Mirrors WinForms SkillConfigSkillSystemForm.FindSkillPointer("ICON", 0).
        /// Returns the icon base address (dereferenced pointer), or 0 if not found.
        /// </summary>
        public static uint FindSkillSystemIconBaseAddress()
        {
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return 0;

            try
            {
                // Binary patterns for finding the ICON pointer, ordered by priority
                // (same as WinForms SkillConfigSkillSystemForm.FindSkillPointer)
                var iconPatterns = new (byte[] data, uint skip, bool hasMask)[]
                {
                    (new byte[] { 0x02, 0x40, 0x09, 0x4C, 0x05, 0x48, 0x00, 0x47, 0x05, 0x48, 0x00, 0x47, 0x05, 0x48, 0x00, 0x47 }, 24, false),
                    (new byte[] { 0x08, 0x42, 0x04, 0xD1, 0x12, 0x79, 0xAA, 0x42, 0x01, 0xD1, 0x01, 0x20, 0x03, 0xE0, 0x01, 0x34, 0xBF, 0x2C, 0xEA, 0xDD, 0x00, 0x20, 0x30, 0xBC, 0x02, 0xBC, 0x08, 0x47 }, 8, false),
                    (new byte[] { 0x38, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x80, 0x22, 0x0E, 0x49, 0x16, 0x48, 0x16, 0x4B, 0xFF, 0xFF, 0xFF, 0xFF, 0x2B, 0x34, 0x29, 0x00, 0x38, 0x00, 0x14, 0x4B, 0xFF, 0xFF, 0xFF, 0xFF, 0x26, 0x70, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x47, 0x01, 0x33, 0xCD, 0xE7, 0xC0, 0x46 }, 0, true),
                    (new byte[] { 0x38, 0x00, 0x00, 0xF0, 0xFB, 0xF9, 0x80, 0x22, 0x0E, 0x49, 0x16, 0x48, 0x16, 0x4B, 0x00, 0xF0, 0xF5, 0xF9, 0x2B, 0x34, 0x29, 0x00, 0x38, 0x00, 0x14, 0x4B, 0x00, 0xF0, 0xEF, 0xF9, 0x26, 0x70, 0xF8, 0xBC, 0x01, 0xBC, 0x00, 0x47, 0x01, 0x33, 0xCD, 0xE7, 0xC0, 0x46 }, 0, false),
                };

                uint start = 0xB00000;
                uint end = 0xC00000;

                foreach (var (data, skip, hasMask) in iconPatterns)
                {
                    uint found;
                    if (hasMask)
                    {
                        found = GrepWithMask(rom.Data, data, start, end, 4);
                    }
                    else
                    {
                        found = U.Grep(rom.Data, data, start, end, 4);
                    }

                    if (found == U.NOT_FOUND) continue;

                    uint a = (uint)(found + data.Length + skip);
                    if (!U.isSafetyOffset(a + 3)) continue;

                    uint p = rom.u32(a);
                    if (!U.isSafetyPointer(p)) continue;

                    // Dereference the pointer to get the icon base address
                    return U.toOffset(p);
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Load a map action animation thumbnail by rendering the first frame.
        /// The animation pointer is read from the map action animation table entry.
        /// Returns a 64x64 IImage of the first animation frame, or null.
        /// </summary>
        public static IImage LoadMapActionAnimationThumbnail(uint animePointer)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || animePointer == 0) return null;

            try
            {
                // animePointer is a GBA pointer to the frame table
                if (!U.isPointer(animePointer) && !U.isSafetyOffset(animePointer))
                    return null;

                // DrawFrame handles GBA pointer -> offset conversion internally
                return ImageUtilMapActionAnimationCore.DrawFrame(animePointer, 0);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Search ROM data for a byte pattern with 0xFF,0xFF wildcard pairs.
        /// Mirrors WinForms U.GrepPatternMatch with MakeMaskData logic.
        /// Adjacent 0xFF bytes are treated as wildcards (don't-care positions).
        /// </summary>
        static uint GrepWithMask(byte[] romData, byte[] pattern, uint start, uint end, uint blockSize)
        {
            if (romData == null || pattern == null || pattern.Length == 0) return U.NOT_FOUND;
            if (end == 0 || end > (uint)romData.Length) end = (uint)romData.Length;
            if (start + pattern.Length > end) return U.NOT_FOUND;

            // Build mask: false = wildcard (0xFF,0xFF pair positions)
            bool[] mustMatch = new bool[pattern.Length];
            for (int i = 0; i < pattern.Length; i++)
                mustMatch[i] = true;
            for (int i = 0; i < pattern.Length - 1; i++)
            {
                if (pattern[i] == 0xFF && pattern[i + 1] == 0xFF)
                {
                    mustMatch[i] = false;
                    mustMatch[i + 1] = false;
                }
            }

            uint limit = end - (uint)pattern.Length;
            for (uint pos = start; pos <= limit; pos += blockSize)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (mustMatch[j] && romData[pos + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return pos;
            }
            return U.NOT_FOUND;
        }
    }
}
