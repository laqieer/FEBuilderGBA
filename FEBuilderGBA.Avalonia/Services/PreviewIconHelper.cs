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

                return ImageUtilCore.LoadROMTiles4bpp(imgAddr, palette, 4, 4, true);
            }
            catch
            {
                return null;
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
