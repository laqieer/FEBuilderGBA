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
            limiter = (uint)Math.Min(limiter, rom.Data.Length);

            for (uint n = animeAddress; n < limiter; n += 12)
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

            for (uint n = animeAddress; n < limiter; n += 12)
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

            uint objOffset = rom.p32(frame + 4);
            uint palOffset = rom.p32(frame + 8);

            if (!U.isSafetyOffset(objOffset))
                return null;
            if (!U.isSafetyOffset(palOffset))
                return null;

            // OBJ data is LZ77-compressed
            byte[] obj = LZ77.decompress(rom.Data, objOffset);
            if (obj == null || obj.Length == 0)
                return null;

            // PAL is 0x20 bytes (16 colors, 2 bytes each) uncompressed
            byte[] palette = ImageUtilCore.GetPalette(palOffset, 16);
            if (palette == null)
                return null;

            return CoreState.ImageService.Decode4bppTiles(obj, 0, SCREEN_WIDTH, SCREEN_HEIGHT, palette);
        }
    }
}
