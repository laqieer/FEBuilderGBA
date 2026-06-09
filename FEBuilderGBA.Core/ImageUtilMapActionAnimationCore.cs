using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Platform-independent renderer for Map Action Animations.
    /// Ported from WinForms ImageUtilMapActionAnimation.Draw.
    ///
    /// Frame data format (12 bytes per entry):
    ///   byte   wait;
    ///   byte   00;
    ///   ushort sound;
    ///   void*  img;   // GBA pointer to LZ77-compressed 4bpp tile data
    ///   void*  pal;   // GBA pointer to raw palette (0x20 bytes = 16 colors)
    /// Terminated by two consecutive zero u32s.
    /// Rendered as 64x64 pixel (8x8 tiles) 4bpp image.
    /// </summary>
    public static class ImageUtilMapActionAnimationCore
    {
        const int SCREEN_WIDTH = 64;
        const int SCREEN_HEIGHT = 64;

        /// <summary>
        /// Draw a single frame of a map action animation.
        /// </summary>
        /// <param name="animeAddress">ROM address of the animation frame table (may be a GBA pointer).</param>
        /// <param name="frameIndex">0-based index of the frame to render.</param>
        /// <returns>Rendered IImage (64x64), or null on failure.</returns>
        public static IImage DrawFrame(uint animeAddress, uint frameIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null)
                return null;

            animeAddress = U.toOffset(animeAddress);
            if (!U.isSafetyOffset(animeAddress))
                return null;

            uint frame = FindFrame(frameIndex, animeAddress, rom.Data);
            if (frame == U.NOT_FOUND)
                return null;

            return DrawFrameImage(frame);
        }

        /// <summary>
        /// Count the number of frames in the animation at the given address.
        /// </summary>
        public static int CountFrames(uint animeAddress)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return 0;

            animeAddress = U.toOffset(animeAddress);
            if (!U.isSafetyOffset(animeAddress))
                return 0;

            int count = 0;
            uint limiter = animeAddress + 1024 * 1024;
            // Cap so a full 12-byte row (read up to n+8..n+11) never overruns EOF.
            limiter = (uint)Math.Min(limiter, Math.Max(0, rom.Data.Length - 12 + 1));

            for (uint n = animeAddress; n + 12 <= (uint)rom.Data.Length && n < limiter; n += 12)
            {
                uint term1 = U.u32(rom.Data, n);
                uint term2 = U.u32(rom.Data, n + 4);
                if (term1 == 0 && term2 == 0)
                    break;
                count++;
            }
            return count;
        }

        static uint FindFrame(uint frameIndex, uint animeAddress, byte[] data)
        {
            uint frameI = 0;

            // Safety limiter: stop after scanning 1 MB of uncompressed data
            uint limiter = animeAddress + 1024 * 1024;
            limiter = (uint)Math.Min(limiter, data.Length);

            // Stop before a partial 12-byte row would overrun EOF (n+4..n+11 read).
            for (uint n = animeAddress; n + 12 <= (uint)data.Length && n < limiter; n += 12)
            {
                uint term1 = U.u32(data, n);
                uint term2 = U.u32(data, n + 4);
                if (term1 == 0 && term2 == 0)
                    break;

                if (frameIndex == frameI)
                    return n;

                frameI++;
            }
            return U.NOT_FOUND;
        }

        static IImage DrawFrameImage(uint frame)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || !U.isSafetyOffset(frame))
                return null;

            // struct Frame {
            //   byte   wait;
            //   byte   00;
            //   ushort sound;
            //   void*  img;   // offset +4
            //   void*  pal;   // offset +8
            // } // sizeof() == 12

            // Explicit 12-byte frame-row bounds guard so the no-throw contract is
            // self-evident and does not merely rely on rom.p32's EOF tolerance.
            if ((long)frame + 12 > rom.Data.Length)
                return null;

            uint objOffset = rom.p32(frame + 4);
            uint palOffset = rom.p32(frame + 8);

            // rom.p32 already returns a ROM offset (post-toOffset, 0 if OOR);
            // RenderFrameImage re-applies U.toOffset which is a no-op for an
            // already-converted offset (< 0x08000000), so passing the offset is safe.
            return RenderFrameImage(rom, objOffset, palOffset);
        }

        /// <summary>
        /// ROM-aware renderer for a single map-action animation frame's image.
        /// Decodes the LZ77-compressed 4bpp OBJ tiles pointed to by
        /// <paramref name="imagePointer"/> using the 16-color palette pointed to by
        /// <paramref name="palettePointer"/>, producing a 64x64 (8x8 tiles) IImage.
        ///
        /// Does NOT use the ambient <see cref="CoreState.ROM"/> — reads exclusively
        /// from the supplied <paramref name="rom"/>. Both pointers may be GBA
        /// pointers or already-converted ROM offsets (<see cref="U.toOffset"/> is
        /// idempotent for offsets). READ-ONLY: never mutates the ROM.
        ///
        /// Never throws — any bad pointer, short/corrupt LZ77 stream, missing
        /// palette, or unexpected exception returns null (blank-dummy contract,
        /// mirroring WaitIconRenderCore / SkillSystemsAnimeExportCore).
        /// </summary>
        /// <param name="rom">ROM to read from (not the ambient CoreState.ROM).</param>
        /// <param name="imagePointer">GBA pointer / offset to LZ77-compressed 4bpp OBJ tiles.</param>
        /// <param name="palettePointer">GBA pointer / offset to the raw 0x20-byte palette.</param>
        /// <returns>Rendered 64x64 IImage, or null on any failure.</returns>
        public static IImage RenderFrameImage(ROM rom, uint imagePointer, uint palettePointer)
        {
            if (rom == null || rom.Data == null)
                return null;
            if (CoreState.ImageService == null)
                return null;

            uint objOffset = U.toOffset(imagePointer);
            uint palOffset = U.toOffset(palettePointer);

            if (!U.isSafetyOffset(objOffset, rom))
                return null;
            if (!U.isSafetyOffset(palOffset, rom))
                return null;

            try
            {
                // Guard a corrupt LZ77 header that advertises a huge uncompressed
                // size: getUncompressSize enforces MAX_UNCOMP_DATA_LIMIT (raw
                // LZ77.decompress does NOT), so a garbage/corrupt OBJ pointer can't
                // force an oversized allocation that stalls the UI preview. Mirrors
                // ClassOPDemoFontRenderCore. Copilot review on PR #1077.
                if (LZ77.getUncompressSize(rom.Data, objOffset) == 0)
                    return null;

                // OBJ data is LZ77-compressed.
                byte[] obj = LZ77.decompress(rom.Data, objOffset);
                if (obj == null || obj.Length == 0)
                    return null;

                // PAL is 0x20 bytes (16 colors, 2 bytes each) uncompressed.
                byte[] palette = ImageUtilCore.GetPalette(rom, palOffset, 16);
                if (palette == null)
                    return null;

                return CoreState.ImageService.Decode4bppTiles(obj, 0, SCREEN_WIDTH, SCREEN_HEIGHT, palette);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
