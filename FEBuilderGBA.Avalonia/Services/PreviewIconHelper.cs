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
        /// <remarks>
        /// #654: removed <c>portraitId == 0</c> short-circuit so callers that
        /// list portrait 0 (e.g. ImagePortraitView's first row) can render its
        /// icon. WinForms <c>ImagePortraitForm.DrawPortraitAuto(0)</c> attempts
        /// the read and returns <c>ImageUtil.BlankDummy()</c> on bad data — we
        /// return null on failure but at least give portrait 0 a chance.
        /// </remarks>
        public static IImage LoadPortraitMini(uint portraitId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

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
        /// Resolve the portrait ID for a unit by 0-based table index.
        /// Looks up the unit struct from the unit table, then calls ResolveUnitPortraitId.
        /// </summary>
        /// <remarks>
        /// For ROM-stored unit IDs (1-based per WinForms convention) or for the
        /// 1-based hex prefix extracted from a Unit-list label, use
        /// <see cref="ResolveUnitPortraitIdByOneBasedId(uint)"/> instead — passing
        /// a 1-based value here causes the wrong portrait to be loaded (issues #652, #653).
        /// </remarks>
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
        /// Resolve the portrait ID for a unit by 1-based unit ID (matching the
        /// WinForms convention used by <c>UnitForm.DrawUnitMapFacePicture(uid)</c>
        /// and the hex prefix on every Avalonia list label). Subtracts 1 internally
        /// before indexing, and on FE6 also skips the dummy entry at table index 0
        /// (via <see cref="SupportUnitNavigation.GetUnitTableBase"/>).
        /// </summary>
        /// <param name="oneBasedUnitId">
        /// The 1-based unit ID stored in ROM bytes / event data / list-label hex prefixes.
        /// <c>0</c> returns <c>0</c> (no portrait — matches WinForms blank-dummy behavior).
        /// </param>
        /// <remarks>
        /// Fixes the off-by-one portrait rendering bug in Support Unit Editor (#652) and
        /// Support Talk Editor (#653) where every row showed the previous unit's portrait.
        /// </remarks>
        public static uint ResolveUnitPortraitIdByOneBasedId(uint oneBasedUnitId)
        {
            // Early-return on 0 BEFORE any arithmetic so the `oneBasedUnitId - 1`
            // step below never underflows. A u16/u8 sentinel of 0 (no unit) is the
            // most common caller, but malformed list prefixes can also feed 0 here.
            if (oneBasedUnitId == 0) return 0;
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;

            try
            {
                // SupportUnitNavigation.GetUnitTableBase handles the FE6 dummy-entry
                // skip (FE6's table starts at p32(unit_pointer) + unit_datasize).
                uint unitBase = SupportUnitNavigation.GetUnitTableBase(rom);
                if (unitBase == 0) return 0;

                uint unitSize = rom.RomInfo.unit_datasize;
                if (unitSize == 0) return 0;

                // Reject out-of-range IDs up-front. unit_maxcount is the highest
                // legitimate 1-based ID in the table; anything above (e.g. a u16
                // field holding 0xFFFF or a corrupt list prefix) must not be
                // resolved, because a u32 address computed from oneBasedUnitId *
                // unitSize can wrap around and land back inside the ROM, producing
                // a wrong portrait. Use ulong for the arithmetic so we can detect
                // the wrap explicitly even before the bounds check.
                uint maxCount = rom.RomInfo.unit_maxcount;
                if (maxCount != 0 && oneBasedUnitId > maxCount) return 0;

                ulong zeroBasedIndex = (ulong)oneBasedUnitId - 1UL;
                ulong unitAddr64 = (ulong)unitBase + zeroBasedIndex * (ulong)unitSize;
                ulong romLen = (ulong)rom.Data.Length;
                if (unitAddr64 + (ulong)unitSize > romLen) return 0;
                if (unitAddr64 > uint.MaxValue) return 0;

                return ResolveUnitPortraitId((uint)unitAddr64);
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
        /// <remarks>
        /// #935: mirrors WinForms <c>ClassForm.DrawWaitIcon</c>, which guards
        /// only <c>cid &lt;= 0</c> (class 0 = the "null class" → blank) and then,
        /// for any class &gt;= 1, reads the wait-icon-table slot from offset +6
        /// and renders it directly — WITHOUT short-circuiting when that slot
        /// value is 0. Slot 0 of the wait-icon table is a real sprite, so a
        /// nonzero class whose <c>waitIconIndex</c> field happens to be 0 must
        /// still render slot 0 (the old <c>waitIconIndex == 0</c> bail hid the
        /// first real row's icon across ~30 class-prefixed list editors).
        /// </remarks>
        public static IImage LoadClassWaitIconByClassId(uint classId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;
            if (classId == 0) return null;
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
                return LoadClassWaitIcon(waitIconIndex);
            }
            catch { return null; }
        }

        /// <summary>
        /// Load an item icon by item ID. Resolves item ID -> icon index (offset +29) -> loads icon.
        /// </summary>
        /// <remarks>
        /// #935: mirrors WinForms <c>ItemForm.DrawIcon</c>, which guards only
        /// <c>item_id &lt;= 0</c> (item 0 = the "null item" → blank) and then,
        /// for any item &gt;= 1, reads the icon-table slot from offset +29 and
        /// renders it directly — WITHOUT short-circuiting when that slot value
        /// is 0. Slot 0 of the icon table is a real icon, so a nonzero item
        /// whose <c>iconIndex</c> field happens to be 0 must still render slot 0
        /// (the old <c>iconIndex == 0</c> bail hid the first real row's icon
        /// across the item-prefixed list editors).
        /// </remarks>
        public static IImage LoadItemIconByItemId(uint itemId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;
            if (itemId == 0) return null;
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
        /// The wait icon table stores LZ77-compressed sprite strips per entry; the
        /// animation type (byte at <c>entry+2</c>) determines both the per-frame
        /// dimensions AND the Y offset of the first frame inside the decompressed
        /// strip.
        ///
        /// <para>
        /// Mirrors WinForms <c>ImageUnitWaitIconFrom.DrawWaitUnitIcon</c>
        /// (<c>height16_limit=false</c>, <c>step=0</c>) which crops the first
        /// frame from the strip at the following rectangles (issue #342):
        /// </para>
        /// <list type="bullet">
        ///   <item><description>animType 0: <c>Rectangle(0, 0, 16, 16)</c></description></item>
        ///   <item><description>animType 1: <c>Rectangle(0, 8, 16, 24)</c> — Y=8 offset is the bug that issue #342 patches.</description></item>
        ///   <item><description>animType 2: <c>Rectangle(0, 0, 32, 32)</c></description></item>
        /// </list>
        ///
        /// <para>
        /// Previously this helper decoded only <c>tilesW × tilesH</c> from
        /// <c>Y=0</c>, which for animType 1 returned the 8-pixel padding header
        /// + the top 16 rows of the cavalier sprite, missing the bottom 8 rows.
        /// User-visible symptom (PR #667 follow-up screenshot): Social Knight
        /// row only shows the rider's head, no horse legs — the icon looked
        /// "cut" because the producer-side crop rectangle was wrong.
        /// </para>
        ///
        /// Returns null on any ROM/service failure or when the strip is too
        /// short to satisfy the per-animType crop rectangle.
        /// </summary>
        public static IImage LoadClassWaitIcon(uint waitIconIndex)
        {
            // #991: the decode + per-animType crop pipeline moved VERBATIM into
            // the cross-platform FEBuilderGBA.Core.WaitIconRenderCore (single
            // source of truth — the Avalonia Unit Wait Icon editor reuses it).
            // This wrapper preserves the existing behavior exactly: resolve the
            // ambient CoreState ROM + ImageService and render the step-0 frame
            // with the self palette (the #342/#667 16x24@Y=8 step-0 parity is
            // baked into RenderClassWaitIcon == RenderFrame(step:0)).
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;
            var svc = CoreState.ImageService;
            if (svc == null) return null;

            try
            {
                return FEBuilderGBA.Core.WaitIconRenderCore.RenderClassWaitIcon(rom, waitIconIndex, svc);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load a move icon for the given move icon index.
        /// Move icon IDs are 1-based (subtracts 1 to get the 0-based table index),
        /// matching WinForms DrawMoveUnitIconBitmap.
        /// Returns the first walk frame (32x32, self palette) of the move
        /// animation, or null on failure.
        /// </summary>
        public static IImage LoadMoveIcon(uint moveIconIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || moveIconIndex == 0) return null;

            var svc = CoreState.ImageService;
            if (svc == null) return null;

            try
            {
                // #1177: the decode + per-step crop pipeline moved into the
                // cross-platform FEBuilderGBA.Core.UnitMoveIconRenderCore (single
                // source of truth — the Avalonia Unit Move Icon editor reuses it).
                // Move icon IDs are 1-based; the Core renderer is 0-based by table
                // index. Render step 0 with the self palette.
                uint tableIndex = moveIconIndex - 1;
                return FEBuilderGBA.Core.UnitMoveIconRenderCore.RenderFrame(rom, tableIndex, 0, svc, 0);
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
        /// Find the SkillSystem TEXT pointer LOCATION (the address where the
        /// text-base pointer lives, equivalent to the WF `textPointer`
        /// argument passed to `InputFormRef.Init`). Mirrors WinForms
        /// `SkillConfigSkillSystemForm.FindTextPointer` -> `FindSkillPointer("TEXT", 0)`.
        ///
        /// Returns <see cref="U.NOT_FOUND"/> if no pattern matches in the
        /// 0xB00000..0xC00000 scan window, otherwise returns the address of
        /// the post-pattern u32 holding the text-base GBA pointer.
        ///
        /// Distinct from <see cref="FindSkillSystemTextBaseAddress"/> (which
        /// returns the dereferenced offset). The pointer-location form is
        /// needed by callers that want to render "Start Address" identically
        /// to WF and by `Write` paths that need to re-derive the same base
        /// after an Undo rollback.
        /// </summary>
        public static uint FindSkillSystemTextPointerLocation()
        {
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return U.NOT_FOUND;

            try
            {
                // Two known TEXT patterns from WinForms `FindSkillPointer("TEXT", 0)`.
                var textPatterns = new (byte[] data, uint skip)[]
                {
                    (new byte[] { 0x07, 0x49, 0x40, 0x00, 0x40, 0x18, 0x00, 0x88, 0x00, 0x28, 0x00, 0xD1, 0x06, 0x48, 0x21, 0x1C }, 16),
                    (new byte[] { 0x40, 0x5D, 0x08, 0x49, 0x40, 0x00, 0x40, 0x18, 0x00, 0x88, 0x00, 0x28, 0x00, 0xD1, 0x07, 0x48, 0x21, 0x1C, 0x4C, 0x31 }, 16),
                };

                // Clamp end to rom.Data.Length so smaller ROMs don't fall
                // into the U.Grep exception path (Copilot bot review).
                uint start = 0xB00000;
                uint end = Math.Min(0xC00000u, (uint)rom.Data.Length);
                if (start >= end) return U.NOT_FOUND;

                foreach (var (data, skip) in textPatterns)
                {
                    uint found = U.Grep(rom.Data, data, start, end, 4);
                    if (found == U.NOT_FOUND) continue;

                    uint a = (uint)(found + data.Length + skip);
                    if (!U.isSafetyOffset(a + 3, rom)) continue;
                    uint p = rom.u32(a);
                    if (!U.isSafetyPointer(p)) continue;

                    return a;
                }

                return U.NOT_FOUND;
            }
            catch
            {
                return U.NOT_FOUND;
            }
        }

        /// <summary>
        /// Find the SkillSystem TEXT base offset (dereferenced pointer).
        /// Convenience wrapper over <see cref="FindSkillSystemTextPointerLocation"/>
        /// that runs the dereference + <c>U.toOffset</c> for callers that
        /// don't need the pointer-location form. Returns 0 on miss.
        /// </summary>
        public static uint FindSkillSystemTextBaseAddress()
        {
            uint loc = FindSkillSystemTextPointerLocation();
            if (loc == U.NOT_FOUND) return 0;
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return 0;
            uint p = rom.u32(loc);
            if (!U.isSafetyPointer(p)) return 0;
            return U.toOffset(p);
        }

        /// <summary>
        /// Find the SkillSystem ANIME pointer LOCATION (the address where the
        /// animation-pointer-table base pointer lives). Mirrors WinForms
        /// `SkillConfigSkillSystemForm.FindAnimePointer` -> `FindSkillPointer("ANIME", 0)`.
        /// Returns <see cref="U.NOT_FOUND"/> on miss, otherwise returns the
        /// address of the post-pattern u32 holding the anime-base GBA pointer.
        /// </summary>
        public static uint FindSkillSystemAnimePointerLocation()
        {
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return U.NOT_FOUND;

            try
            {
                // Three known ANIME patterns from WinForms `FindSkillPointer("ANIME", 0)`,
                // ordered by appearance in the WF table (first match wins):
                //   1. skip=32, 16-byte literal pattern (primary path on most ROMs)
                //   2. skip=16, 64-byte pattern with 0xFF/0xFF wildcards at the
                //      pointer-table addresses (Copilot bot review on PR #525
                //      caught the gap - without this, ROMs that match only
                //      the masked signature wouldn't resolve)
                //   3. skip=12, 32-byte literal pattern (older ROMs)
                var animePatterns = new (byte[] data, uint skip, bool hasMask)[]
                {
                    (new byte[] { 0x00, 0x2B, 0x00, 0xD1, 0x06, 0x4B, 0x38, 0x1C, 0x9E, 0x46, 0x00, 0xF8, 0x05, 0x48, 0x00, 0x47 }, 32, false),
                    (new byte[] { 0x00, 0xD1, 0x33, 0x1C, 0x01, 0x33, 0x38, 0x1C, 0xFF, 0xFF, 0xFF, 0xFF, 0xF0, 0xBC, 0x11, 0x48, 0x00, 0x47, 0xF0, 0xBC, 0x10, 0x48, 0x00, 0x47, 0xF0, 0xBC, 0x10, 0x48, 0x00, 0x47, 0x18, 0x47, 0x6D, 0xA1, 0x05, 0x08, 0x35, 0x8A, 0x05, 0x08, 0x00, 0x08, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x10, 0xE3, 0x06, 0x08, 0x8C, 0xE5, 0x06, 0x08, 0xC4, 0xAE, 0x02, 0x08, 0x55, 0xA1, 0x05, 0x08 }, 16, true),
                    (new byte[] { 0x9D, 0x2E, 0x00, 0x08, 0x35, 0x8A, 0x05, 0x08, 0x00, 0x08, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x10, 0xE3, 0x06, 0x08, 0x8C, 0xE5, 0x06, 0x08, 0xC4, 0xAE, 0x02, 0x08, 0x55, 0xA1, 0x05, 0x08 }, 12, false),
                };

                // Clamp the scan window to rom.Data.Length so smaller ROMs
                // don't fall into the U.Grep exception path - matches the
                // GrepWithMask defensive clamp (Copilot bot review).
                uint start = 0xB00000;
                uint end = Math.Min(0xC00000u, (uint)rom.Data.Length);
                if (start >= end) return U.NOT_FOUND;

                foreach (var (data, skip, hasMask) in animePatterns)
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
                    if (!U.isSafetyOffset(a + 3, rom)) continue;
                    uint p = rom.u32(a);
                    if (!U.isSafetyPointer(p)) continue;

                    return a;
                }

                return U.NOT_FOUND;
            }
            catch
            {
                return U.NOT_FOUND;
            }
        }

        /// <summary>
        /// Load a 16x16 skill icon for the CSkillSys 0.9.x patch. Unlike the
        /// SkillSystems patch (which stripes all icons after one base address),
        /// CSkillSys 0.9.x stores a per-skill GBA pointer at skill-info entry
        /// offset +0; we dereference that pointer and decode the 4bpp tile at
        /// the resolved offset.
        /// </summary>
        /// <param name="iconGbaPointer">
        /// Raw u32 read from the skill-info entry at +0. The GBA pointer
        /// convention applies (high bit set). We convert via <c>U.toOffset</c>
        /// before decoding.
        /// </param>

        /// <summary>
        /// Find the SkillSystem ASSIGN_CLASS pointer LOCATION (the address
        /// where the per-class skill table's GBA pointer lives). Mirrors
        /// WinForms SkillConfigSkillSystemForm.FindAssignClassSkillPointer().
        /// Returns U.NOT_FOUND on miss. Delegates to the authoritative Core
        /// SkillSystemPatchScanner.
        /// </summary>
        public static uint FindSkillSystemAssignClassPointerLocation()
        {
            return SkillSystemPatchScanner.FindAssignClassSkillPointerLocation(CoreState.ROM);
        }

        /// <summary>
        /// Find the SkillSystem ASSIGN_CLASS base offset (dereferenced pointer).
        /// </summary>
        public static uint FindSkillSystemAssignClassBaseAddress()
        {
            uint loc = FindSkillSystemAssignClassPointerLocation();
            if (loc == U.NOT_FOUND) return 0;
            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null) return 0;
            uint p = rom.u32(loc);
            if (!U.isSafetyPointer(p)) return 0;
            return U.toOffset(p);
        }

        /// <summary>
        /// Find the SkillSystem LEVELUP_CLASS pointer LOCATION (the address
        /// where the per-class level-up skill pointer-table base lives).
        /// Mirrors WinForms FindAssignClassLevelUpSkillPointer().
        /// </summary>
        public static uint FindSkillSystemAssignLevelUpPointerLocation()
        {
            return SkillSystemPatchScanner.FindAssignClassLevelUpSkillPointerLocation(CoreState.ROM);
        }

        /// <summary>
        /// Find the SkillSystem LEVELUP_CLASS base offset (dereferenced pointer).
        /// </summary>
        public static uint FindSkillSystemAssignLevelUpBaseAddress()
        {
            uint loc = FindSkillSystemAssignLevelUpPointerLocation();
            if (loc == U.NOT_FOUND) return 0;
            ROM rom = CoreState.ROM;
            if (rom == null || rom.Data == null) return 0;
            uint p = rom.u32(loc);
            if (!U.isSafetyPointer(p)) return 0;
            return U.toOffset(p);
        }

        public static IImage LoadCSkillSysIcon(uint iconGbaPointer)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || iconGbaPointer == 0) return null;

            try
            {
                uint iconAddr = U.toOffset(iconGbaPointer);
                if (!U.isSafetyOffset(iconAddr, rom)) return null;

                const uint TILE_SIZE = 128; // 16x16 4bpp = 128 bytes
                if (iconAddr + TILE_SIZE > (uint)rom.Data.Length) return null;

                // CSkillSys palette pointer is the same as SkillSystems
                // (mirrors WinForms `SkillPalettePointer = 0x22370`).
                const uint SKILL_PALETTE_POINTER = 0x22370;
                if (!U.isSafetyOffset(SKILL_PALETTE_POINTER + 3, rom)) return null;

                uint palAddr = rom.p32(SKILL_PALETTE_POINTER);
                if (!U.isSafetyOffset(palAddr, rom)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                return CoreState.ImageService?.Decode4bppTiles(rom.Data, (int)iconAddr, 16, 16, palette);
            }
            catch
            {
                return null;
            }
        }

        // -----------------------------------------------------------------
        // FE8N v2 skill expansion helpers (#396)
        //
        // FE8N v2 is a third skill-patch flavour (distinct from SkillSystems
        // and CSkillSys 0.9.x). It uses a byte-pattern scan to locate the
        // skill-info table, but with two material differences vs SkillSystems:
        //   1. The icon storage model is the WF-standard
        //      `rom.p32(RomInfo.icon_pointer) + 128 * (0x100 + id)` — NOT a
        //      separate striped table.
        //   2. The row stride (ICON_LIST_SIZE) is detected at runtime from
        //      the iconPointers array slot[11] (valid range 16..40, multiple
        //      of 4).
        // -----------------------------------------------------------------

        /// <summary>
        /// Cached results from <see cref="ScanFE8NVer2Layout"/> so callers
        /// don't repeat the byte-pattern scan per row. Keyed by ROM filename
        /// to invalidate cleanly when a different ROM is loaded.
        /// </summary>
        static (string romName, uint skillPointerLocation, uint animeBaseAddress, uint iconListSize) _fe8nVer2Cache;

        /// <summary>
        /// Reset the FE8N v2 scan cache. Called when the ROM changes (the
        /// next call to a helper re-runs the scan).
        /// </summary>
        public static void ResetFE8NVer2Cache()
        {
            _fe8nVer2Cache = default;
        }

        /// <summary>
        /// Run the WF byte-pattern scan and populate the cache. Returns the
        /// `iconPointers` base address (the slot[0] of the icon-pointer
        /// array), or <see cref="U.NOT_FOUND"/> on miss.
        /// Mirrors <c>SkillConfigFE8NVer2SkillForm.FindSkillFE8NVer2IconPointersLow</c>.
        /// </summary>
        static uint ScanFE8NVer2Layout()
        {
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return U.NOT_FOUND;

            // Use cached result when the ROM hasn't changed.
            string currentName = rom.Filename ?? "";
            if (_fe8nVer2Cache.romName == currentName && _fe8nVer2Cache.skillPointerLocation != 0)
            {
                return _fe8nVer2Cache.skillPointerLocation - 4 * 5; // back to iconPointers
            }

            try
            {
                // Step 1: iconExPointer at 0x89268+4 must be a safe pointer.
                if (!U.isSafetyOffset(0x89268 + 4 + 3, rom)) return U.NOT_FOUND;
                uint iconExPointer = rom.u32(0x89268 + 4);
                if (!U.isSafetyPointer(iconExPointer)) return U.NOT_FOUND;

                // Step 2: grep for the FE8N v2 marker pattern in 0xE00000..0.
                byte[] need = new byte[] { 0x50, 0x93, 0x08, 0x08, 0x48, 0x93, 0x08, 0x08 };
                // U.Grep with end=0 means scan to end of ROM data; we want
                // start=0xE00000.
                uint patternHit = U.Grep(rom.Data, need, 0xE00000, 0, 4);
                if (patternHit == U.NOT_FOUND) return U.NOT_FOUND;

                // Step 3: back up 4*5 bytes to land on iconPointers[0].
                if (patternHit < 4 * 5) return U.NOT_FOUND;
                uint iconPointers = patternHit - 4 * 5;

                // Step 4: iterate the pointer array, accept slots whose
                // dereferenced offset is >= 0xE00000 (excludes API pointers
                // mixed into the same array). pointer[4] holds the slot
                // whose dereferenced value is the skill table base.
                int validCount = 0;
                uint skillBaseLocation = 0;
                for (uint p = iconPointers; ; p += 4)
                {
                    if (!U.isSafetyOffset(p + 3, rom)) break;
                    uint pp = rom.u32(p);
                    if (!U.isSafetyPointer(pp)) break;
                    pp = U.toOffset(pp);
                    if (pp < 0xE00000) continue;
                    if (validCount == 4) skillBaseLocation = p;
                    validCount++;
                }
                if (validCount <= 4 || skillBaseLocation == 0) return U.NOT_FOUND;

                // Step 5: ICON_LIST_SIZE detection. WF reads u16 @ 0x70B96 as
                // the "build flag" - when 0, the size-detect branch runs.
                uint iconListSize = 16; // default
                uint animeBase = 0;
                if (U.isSafetyOffset(0x70B96 + 1, rom) && rom.u16(0x70B96) == 0)
                {
                    // Try the sizeof-20+ path first (slot[11] holds the
                    // explicit size, slot[8] holds the anime pointer table
                    // location).
                    if (U.isSafetyOffset(iconPointers + 4 * 11 + 3, rom))
                    {
                        uint candidateSize = rom.u32(iconPointers + 4 * 11);
                        if (candidateSize >= 16 && candidateSize <= 40 && (candidateSize % 4 == 0))
                        {
                            iconListSize = candidateSize;
                            // sizeof-20+ path: slot[8] is the anime base pointer location.
                            if (U.isSafetyOffset(iconPointers + 4 * 8 + 3, rom))
                            {
                                uint animePointerLoc = iconPointers + 4 * 8;
                                uint maybeAnimePointer = rom.u32(animePointerLoc);
                                if (U.isSafetyPointer(maybeAnimePointer))
                                {
                                    // WF reads p32 from U.toOffset(p) here, which
                                    // is the offset form of the slot address. The
                                    // result is the anime-table base offset.
                                    uint slotOffset = U.toOffset(animePointerLoc);
                                    if (U.isSafetyOffset(slotOffset + 3, rom))
                                    {
                                        animeBase = rom.p32(slotOffset);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // sizeof-16 path: slot[3] is "nazo_pointer" - get its
                            // offset, then the post-append pointer[5] holds the
                            // dereferenced anime base.
                            if (U.isSafetyOffset(iconPointers + 4 * 3 + 3, rom))
                            {
                                uint nazoPointer = rom.u32(iconPointers + 4 * 3);
                                if (U.isSafetyPointer(nazoPointer))
                                {
                                    uint nazoOff = U.toOffset(nazoPointer);
                                    if (U.isSafetyOffset(nazoOff + 3, rom))
                                    {
                                        animeBase = rom.p32(nazoOff);
                                    }
                                }
                            }
                        }
                    }
                }

                // Cache + return the skill pointer location.
                _fe8nVer2Cache = (currentName, skillBaseLocation, animeBase, iconListSize);
                return iconPointers;
            }
            catch
            {
                return U.NOT_FOUND;
            }
        }

        /// <summary>
        /// Find the FE8N v2 SKILL pointer LOCATION (the address holding the
        /// skill-info-table GBA pointer). Mirrors <c>SkillConfigFE8NVer2SkillForm.g_SkillBaseAddress</c>
        /// before its `Program.ROM.p32` dereference. Returns
        /// <see cref="U.NOT_FOUND"/> if the FE8N v2 patch isn't installed.
        /// </summary>
        public static uint FindSkillFE8NVer2SkillPointerLocation()
        {
            uint iconPointers = ScanFE8NVer2Layout();
            if (iconPointers == U.NOT_FOUND) return U.NOT_FOUND;
            return _fe8nVer2Cache.skillPointerLocation;
        }

        /// <summary>
        /// Find the FE8N v2 SKILL table base offset (dereferenced pointer).
        /// Returns 0 if the patch isn't installed.
        /// </summary>
        public static uint FindSkillFE8NVer2SkillBaseAddress()
        {
            uint loc = FindSkillFE8NVer2SkillPointerLocation();
            if (loc == U.NOT_FOUND) return 0;
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return 0;
            uint p = rom.u32(loc);
            if (!U.isSafetyPointer(p)) return 0;
            return U.toOffset(p);
        }

        /// <summary>
        /// Find the FE8N v2 ANIME table base offset (dereferenced pointer).
        /// Returns 0 if the patch isn't installed or the anime table couldn't
        /// be resolved.
        /// </summary>
        public static uint FindSkillFE8NVer2AnimeBaseAddress()
        {
            uint iconPointers = ScanFE8NVer2Layout();
            if (iconPointers == U.NOT_FOUND) return 0;
            return _fe8nVer2Cache.animeBaseAddress;
        }

        /// <summary>
        /// Returns the detected ICON_LIST_SIZE (row stride) for FE8N v2, in
        /// the range 16..40 (multiple of 4). Returns 0 if the patch isn't
        /// installed or the size couldn't be detected.
        /// </summary>
        public static uint GetFE8NVer2IconListSize()
        {
            uint iconPointers = ScanFE8NVer2Layout();
            if (iconPointers == U.NOT_FOUND) return 0;
            return _fe8nVer2Cache.iconListSize;
        }

        /// <summary>
        /// Load a 16x16 skill icon for FE8N v2. Mirrors
        /// <c>SkillConfigFE8NVer2SkillForm.DrawSkillIconLow(id)</c>:
        ///   iconBaseAddr = rom.p32(RomInfo.icon_pointer)
        ///   iconaddr     = iconBaseAddr + 128 * (0x100 + id)
        ///   palette      = (W2==0) ? rom.p32(RomInfo.system_weapon_icon_palette_pointer)
        ///                          : rom.p32(RomInfo.icon_palette_pointer)
        /// </summary>
        /// <param name="id">Skill index (0-based).</param>
        /// <param name="paletteIndex">Palette field value (W2). When 0, uses
        /// the system weapon icon palette; otherwise the regular icon palette.</param>
        public static IImage LoadFE8NVer2SkillIcon(uint id, uint paletteIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            try
            {
                // Resolve the icon base address from the ROM-info icon pointer.
                uint iconPointerAddr = rom.RomInfo.icon_pointer;
                if (!U.isSafetyOffset(iconPointerAddr + 3, rom)) return null;
                uint iconBaseAddr = rom.p32(iconPointerAddr);
                if (!U.isSafetyOffset(iconBaseAddr, rom)) return null;

                const uint TILE_SIZE = 128; // 16x16 4bpp = 128 bytes
                uint iconAddr = iconBaseAddr + TILE_SIZE * (0x100 + id);
                if (iconAddr + TILE_SIZE > (uint)rom.Data.Length) return null;

                // Palette pointer selection mirrors WF GetSkillPaletteAddress.
                uint palettePointerAddr = (paletteIndex == 0)
                    ? rom.RomInfo.system_weapon_icon_palette_pointer
                    : rom.RomInfo.icon_palette_pointer;
                if (!U.isSafetyOffset(palettePointerAddr + 3, rom)) return null;
                uint palAddr = rom.p32(palettePointerAddr);
                if (!U.isSafetyOffset(palAddr, rom)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                return CoreState.ImageService?.Decode4bppTiles(rom.Data, (int)iconAddr, 16, 16, palette);
            }
            catch
            {
                return null;
            }
        }

        // -----------------------------------------------------------------
        // FE8N v3 skill expansion helpers (#392)
        //
        // FE8N v3 is the fourth skill-patch flavour, sibling of FE8N v2.
        // Unlike v2 (which uses a byte-pattern grep to locate the icon
        // pointer array) v3 uses FIXED ROM OFFSETS:
        //   - rom.u32(0x89268+4) (= 0x8926C) -> iconExPointer sentinel.
        //     Must be a safe GBA pointer; absence means the patch isn't
        //     installed.
        //   - rom.u32(0x892A8+4) (= 0x892AC) -> skill-table GBA pointer.
        //     The WF g_SkillBaseAddress is THIS SLOT ADDRESS (0x892AC),
        //     not its dereferenced offset (because InputFormRef.Init
        //     dereferences a slot-address argument).
        //   - rom.u32(0x892A8+8) (= 0x892B0) -> ICON_LIST_SIZE (row stride).
        //     Valid range 24..100 with size % 4 == 0 (v3 row layout has
        //     fields at D4/D8/D12/D16/D20 so smallest valid stride is 24
        //     - anything less aliases CompositeSkillPointer onto next row).
        //   - rom.u32(0x892A8+20) (= 0x892BC) -> anime-table GBA pointer
        //     (skl_anime_table).
        //
        // Row layout (sizeof-24+):
        //   u16 textId @ +0
        //   u16 palette @ +2
        //   u32 unit-skill-pointer (P4) @ +4
        //   u32 class-skill-pointer (P8) @ +8
        //   u32 item-skill-pointer (P12) @ +12
        //   u32 item2-skill-pointer (P16) @ +16
        //   u32 composite-skill-pointer (P20) @ +20
        //
        // Icon storage mirrors v2 exactly:
        //   rom.p32(RomInfo.icon_pointer) + 128 * (0x100 + id)
        // with palette selection on W2 (==0 -> system_weapon_palette,
        // !=0 -> icon_palette).
        // -----------------------------------------------------------------

        // Sentinel values for fixed-offset reads.
        const uint FE8NVer3_IconExPointer = 0x89268 + 4;     // 0x8926C
        const uint FE8NVer3_SkillPointerLoc = 0x892A8 + 4;   // 0x892AC
        const uint FE8NVer3_IconListSizeLoc = 0x892A8 + 8;   // 0x892B0
        const uint FE8NVer3_AnimePointerLoc = 0x892A8 + 20;  // 0x892BC

        /// <summary>
        /// Cached results from <see cref="ScanFE8NVer3Layout"/> so callers
        /// don't repeat the fixed-offset reads per row. Keyed by ROM
        /// filename to invalidate cleanly when a different ROM is loaded.
        /// resolved == false means the scan was already attempted and the
        /// patch isn't installed (no need to retry).
        /// </summary>
        static (string romName, bool resolved, uint skillPointerLocation, uint skillBaseAddress, uint animeBaseAddress, uint iconListSize) _fe8nVer3Cache;

        /// <summary>
        /// Reset the FE8N v3 scan cache. Called when the ROM changes (the
        /// next call to a helper re-runs the scan).
        /// </summary>
        public static void ResetFE8NVer3Cache()
        {
            _fe8nVer3Cache = default;
        }

        /// <summary>
        /// Read the fixed FE8N v3 offsets and populate the cache. Returns
        /// the skill pointer location (`0x892AC`) on success, or
        /// <see cref="U.NOT_FOUND"/> on miss (patch not installed or
        /// validation failed).
        /// Mirrors <c>SkillConfigFE8NVer3SkillForm.FindSkillFE8NVer3IconPointersLow</c>.
        /// </summary>
        static uint ScanFE8NVer3Layout()
        {
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return U.NOT_FOUND;

            string currentName = rom.Filename ?? "";

            // Use cached result when the ROM hasn't changed.
            if (_fe8nVer3Cache.romName == currentName)
            {
                return _fe8nVer3Cache.resolved
                    ? _fe8nVer3Cache.skillPointerLocation
                    : U.NOT_FOUND;
            }

            try
            {
                // Step 1: iconExPointer at 0x8926C must be a safe pointer.
                if (!U.isSafetyOffset(FE8NVer3_IconExPointer + 3, rom))
                {
                    _fe8nVer3Cache = (currentName, false, 0, 0, 0, 0);
                    return U.NOT_FOUND;
                }
                uint iconExPointer = rom.u32(FE8NVer3_IconExPointer);
                if (!U.isSafetyPointer(iconExPointer))
                {
                    _fe8nVer3Cache = (currentName, false, 0, 0, 0, 0);
                    return U.NOT_FOUND;
                }

                // Step 2: skill-table GBA pointer at 0x892AC must be safe.
                if (!U.isSafetyOffset(FE8NVer3_SkillPointerLoc + 3, rom))
                {
                    _fe8nVer3Cache = (currentName, false, 0, 0, 0, 0);
                    return U.NOT_FOUND;
                }
                uint skillTablePtr = rom.u32(FE8NVer3_SkillPointerLoc);
                if (!U.isSafetyPointer(skillTablePtr))
                {
                    _fe8nVer3Cache = (currentName, false, 0, 0, 0, 0);
                    return U.NOT_FOUND;
                }
                uint skillBaseAddress = U.toOffset(skillTablePtr);

                // Step 3: ICON_LIST_SIZE at 0x892B0 must be 24..100,
                // multiple of 4 (Copilot plan-review #1: 24 is the
                // smallest stride that fits D4/D8/D12/D16/D20).
                if (!U.isSafetyOffset(FE8NVer3_IconListSizeLoc + 3, rom))
                {
                    _fe8nVer3Cache = (currentName, false, 0, 0, 0, 0);
                    return U.NOT_FOUND;
                }
                uint iconListSize = rom.u32(FE8NVer3_IconListSizeLoc);
                if (iconListSize < 24 || iconListSize > 100 || (iconListSize % 4) != 0)
                {
                    _fe8nVer3Cache = (currentName, false, 0, 0, 0, 0);
                    return U.NOT_FOUND;
                }

                // Step 4: anime-table GBA pointer at 0x892BC (optional —
                // 0 means animations aren't available for this ROM).
                uint animeBaseAddress = 0;
                if (U.isSafetyOffset(FE8NVer3_AnimePointerLoc + 3, rom))
                {
                    uint animeTablePtr = rom.u32(FE8NVer3_AnimePointerLoc);
                    if (U.isSafetyPointer(animeTablePtr))
                    {
                        animeBaseAddress = U.toOffset(animeTablePtr);
                    }
                }

                _fe8nVer3Cache = (currentName, true, FE8NVer3_SkillPointerLoc,
                    skillBaseAddress, animeBaseAddress, iconListSize);
                return FE8NVer3_SkillPointerLoc;
            }
            catch
            {
                _fe8nVer3Cache = (currentName, false, 0, 0, 0, 0);
                return U.NOT_FOUND;
            }
        }

        /// <summary>
        /// Find the FE8N v3 SKILL pointer LOCATION (the address holding the
        /// skill-info-table GBA pointer). Mirrors WinForms
        /// <c>SkillConfigFE8NVer3SkillForm.g_SkillBaseAddress</c> which IS
        /// the slot address (0x892AC), NOT the dereferenced offset (the WF
        /// `InputFormRef.Init` dereferences it). Returns
        /// <see cref="U.NOT_FOUND"/> if the patch isn't installed or the
        /// stride is too narrow for the v3 row layout.
        /// </summary>
        public static uint FindSkillFE8NVer3SkillPointerLocation()
        {
            return ScanFE8NVer3Layout();
        }

        /// <summary>
        /// Find the FE8N v3 SKILL table base offset (dereferenced pointer).
        /// Returns 0 if the patch isn't installed.
        /// </summary>
        public static uint FindSkillFE8NVer3SkillBaseAddress()
        {
            if (ScanFE8NVer3Layout() == U.NOT_FOUND) return 0;
            return _fe8nVer3Cache.skillBaseAddress;
        }

        /// <summary>
        /// Find the FE8N v3 ANIME table base offset (dereferenced pointer).
        /// Returns 0 if the patch isn't installed or the anime table
        /// couldn't be resolved.
        /// </summary>
        public static uint FindSkillFE8NVer3AnimeBaseAddress()
        {
            if (ScanFE8NVer3Layout() == U.NOT_FOUND) return 0;
            return _fe8nVer3Cache.animeBaseAddress;
        }

        /// <summary>
        /// Returns the detected ICON_LIST_SIZE (row stride) for FE8N v3,
        /// in the range 24..100 (multiple of 4). Returns 0 if the patch
        /// isn't installed or the size is out of range (Copilot plan-review
        /// #1: strides below 24 alias CompositeSkillPointer onto next row).
        /// </summary>
        public static uint GetFE8NVer3IconListSize()
        {
            if (ScanFE8NVer3Layout() == U.NOT_FOUND) return 0;
            return _fe8nVer3Cache.iconListSize;
        }

        /// <summary>
        /// Load a 16x16 skill icon for FE8N v3. Identical formula to v2
        /// (<see cref="LoadFE8NVer2SkillIcon"/>):
        ///   iconBaseAddr = rom.p32(RomInfo.icon_pointer)
        ///   iconaddr     = iconBaseAddr + 128 * (0x100 + id)
        ///   palette      = (W2==0) ? rom.p32(RomInfo.system_weapon_icon_palette_pointer)
        ///                          : rom.p32(RomInfo.icon_palette_pointer)
        /// Mirrors WF <c>SkillConfigFE8NVer3SkillForm.DrawSkillIconLow</c>.
        /// </summary>
        /// <param name="id">Skill index (0-based).</param>
        /// <param name="paletteIndex">Palette field value (W2). When 0 uses
        /// the system weapon icon palette; otherwise the regular icon palette.</param>
        public static IImage LoadFE8NVer3SkillIcon(uint id, uint paletteIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            try
            {
                uint iconPointerAddr = rom.RomInfo.icon_pointer;
                if (!U.isSafetyOffset(iconPointerAddr + 3, rom)) return null;
                uint iconBaseAddr = rom.p32(iconPointerAddr);
                if (!U.isSafetyOffset(iconBaseAddr, rom)) return null;

                const uint TILE_SIZE = 128; // 16x16 4bpp = 128 bytes
                uint iconAddr = iconBaseAddr + TILE_SIZE * (0x100 + id);
                if (iconAddr + TILE_SIZE > (uint)rom.Data.Length) return null;

                uint palettePointerAddr = (paletteIndex == 0)
                    ? rom.RomInfo.system_weapon_icon_palette_pointer
                    : rom.RomInfo.icon_palette_pointer;
                if (!U.isSafetyOffset(palettePointerAddr + 3, rom)) return null;
                uint palAddr = rom.p32(palettePointerAddr);
                if (!U.isSafetyOffset(palAddr, rom)) return null;

                byte[] palette = ImageUtilCore.GetPalette(palAddr, 16);
                if (palette == null) return null;

                return CoreState.ImageService?.Decode4bppTiles(rom.Data, (int)iconAddr, 16, 16, palette);
            }
            catch
            {
                return null;
            }
        }

        // -----------------------------------------------------------------
        // FE8N v1 (original FE8N / yugudora) skill expansion helpers (#390)
        //
        // FE8N v1 is the ORIGINAL FE8N skill patch flavour (also reused by
        // the `yugudora` patch). It uses a byte-pattern scan to locate the
        // skill-info-table BUT differs from v2 in three material ways:
        //   1. MULTI-POINTER discovery: FindSkillFE8NVer1IconPointersLow
        //      returns uint[] (a list of pointer slot addresses, one per
        //      FE8N table page). The FilterComboBox lets the user choose
        //      which page to load.
        //   2. SCAN OFFSET: pattern bytes `F0 40 00 02 00 3B 00 02`. The
        //      iteration over icon-pointer slots starts at
        //      `iconPointersBase = patternHit + need.Length + 4 + 4 + 4 =
        //      patternHit + 20`. The slot array runs forward from
        //      `patternHit + 20`.
        //   3. ICON ADDRESSING: `iconBase + 128 * W0` where W0 is the row's
        //      u16 icon ID (NOT the row index — differs from v2's
        //      `0x100 + rowIndex`).
        // -----------------------------------------------------------------

        /// <summary>
        /// Cached results from <see cref="FindSkillFE8NVer1IconPointers"/> so
        /// callers don't repeat the byte-pattern scan per row. Keyed by ROM
        /// filename to invalidate cleanly when a different ROM is loaded.
        /// </summary>
        static (string romName, uint[] pointers) _fe8nVer1Cache;

        /// <summary>
        /// Reset the FE8N v1 scan cache. Called when the ROM changes (the
        /// next call to a helper re-runs the scan).
        /// </summary>
        public static void ResetFE8NVer1Cache()
        {
            _fe8nVer1Cache = default;
        }

        /// <summary>
        /// Find all FE8N v1 skill-info-table pointer slot ADDRESSES. Mirrors
        /// WinForms <c>SkillConfigFE8NSkillForm.FindSkillFE8NVer1IconPointersLow</c>
        /// exactly. Returns an empty array when the FE8N v1 (or yugudora)
        /// patch isn't installed.
        /// </summary>
        /// <remarks>
        /// Scan formula (WF parity):
        /// <list type="number">
        ///   <item><description><c>iconExPointer = rom.u32(0x89268 + 4)</c>
        ///   must be a safe GBA pointer (patch sentinel).</description></item>
        ///   <item><description>Byte-pattern grep for
        ///   <c>F0 40 00 02 00 3B 00 02</c> in <c>0xE00000..0</c> with
        ///   block-size 4. Miss = empty array.</description></item>
        ///   <item><description>Pattern hit lies in the header region 20
        ///   bytes BEFORE the start of the slot array. Therefore
        ///   <c>iconPointersBase = patternHit + need.Length + 4 + 4 + 4
        ///   = patternHit + 20</c>.</description></item>
        ///   <item><description>Iterate slots starting at iconPointersBase
        ///   in 4-byte steps. For each slot u32:
        ///     <list type="bullet">
        ///       <item><description>If not <c>isSafetyPointer</c>, BREAK.</description></item>
        ///       <item><description>Convert to offset. If <c>&lt; 0xE00000</c>
        ///       (API pointer), CONTINUE (skip).</description></item>
        ///       <item><description>Otherwise ACCEPT - add the slot ADDRESS
        ///       (NOT the dereferenced value) to the result array.</description></item>
        ///     </list>
        ///   </description></item>
        /// </list>
        /// Empty result is normalized to <c>uint[0]</c> (WF returns null but
        /// the Avalonia callers consistently use empty-array semantics).
        /// </remarks>
        public static uint[] FindSkillFE8NVer1IconPointers()
        {
            ROM rom = CoreState.ROM;
            if (rom?.Data == null) return System.Array.Empty<uint>();

            // Use cached result when the ROM hasn't changed.
            string currentName = rom.Filename ?? "";
            if (_fe8nVer1Cache.romName == currentName && _fe8nVer1Cache.pointers != null)
            {
                return _fe8nVer1Cache.pointers;
            }

            try
            {
                // Step 1: iconExPointer sentinel.
                if (!U.isSafetyOffset(0x89268 + 4 + 3, rom)) return System.Array.Empty<uint>();
                uint iconExPointer = rom.u32(0x89268 + 4);
                if (!U.isSafetyPointer(iconExPointer)) return System.Array.Empty<uint>();

                // Step 2: grep for the FE8N v1 marker pattern in 0xE00000..0.
                byte[] need = new byte[] { 0xF0, 0x40, 0x00, 0x02, 0x00, 0x3B, 0x00, 0x02 };
                uint patternHit = U.Grep(rom.Data, need, 0xE00000, 0, 4);
                if (patternHit == U.NOT_FOUND) return System.Array.Empty<uint>();

                // Step 3: the slot array starts at patternHit + need.Length + 12
                // (= patternHit + 20). The pattern lies in the header region
                // BEFORE the slot array - so the array runs FORWARD from
                // patternHit + 20.
                uint iconPointersBase = patternHit + (uint)need.Length + 4u + 4u + 4u;
                if (!U.isSafetyOffset(iconPointersBase + 3, rom)) return System.Array.Empty<uint>();

                // Step 4: iterate slots (WF semantic).
                var slots = new System.Collections.Generic.List<uint>();
                for (uint p = iconPointersBase; ; p += 4u)
                {
                    if (!U.isSafetyOffset(p + 3, rom)) break;
                    uint pp = rom.u32(p);
                    if (!U.isSafetyPointer(pp)) break;
                    uint off = U.toOffset(pp);
                    if (off < 0xE00000u) continue; // API pointer - skip
                    slots.Add(p);
                }

                uint[] result = slots.ToArray();
                _fe8nVer1Cache = (currentName, result);
                return result;
            }
            catch
            {
                return System.Array.Empty<uint>();
            }
        }

        /// <summary>
        /// Load a 16x16 skill icon for FE8N v1. Mirrors WinForms
        /// <c>ImageItemIconForm.DrawIconWhereID_UsingWeaponPalette_SKILLFE8NVer2(W0)</c>
        /// — note the shared `_SKILLFE8NVer2` helper name despite this being
        /// for v1 (the helper is reused; FE8N v1 and v2 both pass through it).
        /// Resolution:
        /// <code>
        /// iconBaseAddr = rom.p32(RomInfo.icon_pointer)
        /// iconAddr     = iconBaseAddr + 128 * iconId  // iconId = W0 (NOT row index)
        /// palette      = rom.p32(RomInfo.system_weapon_icon_palette_pointer)
        /// </code>
        /// </summary>
        /// <param name="iconId">The row's W0 u16 icon ID (not the row index).</param>
        public static IImage LoadFE8NVer1SkillIcon(uint iconId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            try
            {
                uint iconPointerAddr = rom.RomInfo.icon_pointer;
                if (!U.isSafetyOffset(iconPointerAddr + 3, rom)) return null;
                uint iconBaseAddr = rom.p32(iconPointerAddr);
                if (!U.isSafetyOffset(iconBaseAddr, rom)) return null;

                const uint TILE_SIZE = 128; // 16x16 4bpp = 128 bytes
                uint iconAddr = iconBaseAddr + TILE_SIZE * iconId;
                if (iconAddr + TILE_SIZE > (uint)rom.Data.Length) return null;

                // FE8N v1 always uses the system weapon palette (matches
                // WF DrawIconWhereID_UsingWeaponPalette_SKILLFE8NVer2 contract).
                uint palettePointerAddr = rom.RomInfo.system_weapon_icon_palette_pointer;
                if (!U.isSafetyOffset(palettePointerAddr + 3, rom)) return null;
                uint palAddr = rom.p32(palettePointerAddr);
                if (!U.isSafetyOffset(palAddr, rom)) return null;

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
