using System;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageUtilMapActionAnimationCoreTests
    {
        // isSafetyOffset requires address >= 0x200, so test data must be at >= 0x200
        const int FRAME_BASE = 0x1000;
        const int ROM_SIZE = 0x2000;

        // ---- Helpers for the RenderFrameImage seam (#1024) ----------------

        // Offsets inside the synthetic ROM for the compressed OBJ + raw palette.
        const int OBJ_OFFSET = 0x400;   // LZ77-compressed 64x64 4bpp tiles
        const int PAL_OFFSET = 0x900;   // raw 0x20-byte (16-color) palette

        // 64x64 px @ 4bpp = (64*64)/2 = 2048 uncompressed bytes.
        const int OBJ_UNCOMPRESSED_LEN = (64 * 64) / 2;

        /// <summary>
        /// Build a synthetic ROM holding an LZ77-compressed 64x64 4bpp OBJ payload
        /// at <see cref="OBJ_OFFSET"/> and a 0x20-byte palette at
        /// <see cref="PAL_OFFSET"/>. Returns the ROM; out-params expose the
        /// GBA pointers (0x08000000-based) so callers can pass either form.
        /// </summary>
        static ROM BuildRomWithObjAndPalette(out uint objPointer, out uint palPointer)
        {
            byte[] data = new byte[ROM_SIZE];

            // Uncompressed OBJ tile bytes (any deterministic pattern is fine).
            byte[] raw = new byte[OBJ_UNCOMPRESSED_LEN];
            for (int i = 0; i < raw.Length; i++)
                raw[i] = (byte)(i & 0xFF);

            byte[] compressed = LZ77.compress(raw);
            Assert.NotNull(compressed);
            Assert.True(OBJ_OFFSET + compressed.Length <= PAL_OFFSET,
                "compressed OBJ must fit before the palette region");
            Array.Copy(compressed, 0, data, OBJ_OFFSET, compressed.Length);

            // Raw 16-color palette (0x20 bytes); leave as zeros (valid palette).
            // PAL_OFFSET + 0x20 must be in range.
            Assert.True(PAL_OFFSET + 0x20 <= ROM_SIZE);

            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);

            objPointer = U.toPointer((uint)OBJ_OFFSET);
            palPointer = U.toPointer((uint)PAL_OFFSET);
            return rom;
        }

        [Fact]
        public void DrawFrame_WithNoRom_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var result = ImageUtilMapActionAnimationCore.DrawFrame(0x1000, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void DrawFrame_WithNoImageService_ReturnsNull()
        {
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                var result = ImageUtilMapActionAnimationCore.DrawFrame(0x1000, 0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void DrawFrame_InvalidAddress_ReturnsNull()
        {
            var result = ImageUtilMapActionAnimationCore.DrawFrame(0xFFFFFFFF, 0);
            Assert.Null(result);
        }

        [Fact]
        public void CountFrames_WithNoRom_ReturnsZero()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                int count = ImageUtilMapActionAnimationCore.CountFrames(0x1000);
                Assert.Equal(0, count);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void CountFrames_InvalidAddress_ReturnsZero()
        {
            int count = ImageUtilMapActionAnimationCore.CountFrames(0xFFFFFFFF);
            Assert.Equal(0, count);
        }

        [Fact]
        public void CountFrames_EmptyAnimation_ReturnsZero()
        {
            var origRom = CoreState.ROM;
            try
            {
                // All zeros at FRAME_BASE = immediate terminator
                byte[] data = new byte[ROM_SIZE];
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                int count = ImageUtilMapActionAnimationCore.CountFrames((uint)FRAME_BASE);
                Assert.Equal(0, count);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void CountFrames_WithFrameData_CountsCorrectly()
        {
            var origRom = CoreState.ROM;
            try
            {
                byte[] data = new byte[ROM_SIZE];
                int b = FRAME_BASE;

                // Frame 0: wait=5, sound=0, img=0x08001800, pal=0x08001900
                data[b + 0] = 5;        // wait (makes term1 non-zero)
                data[b + 4] = 0x00;      // img pointer low byte
                data[b + 5] = 0x18;
                data[b + 6] = 0x00;
                data[b + 7] = 0x08;      // GBA pointer prefix
                data[b + 8] = 0x00;
                data[b + 9] = 0x19;
                data[b + 10] = 0x00;
                data[b + 11] = 0x08;

                // Frame 1: wait=3
                data[b + 12] = 3;
                data[b + 16] = 0x00;
                data[b + 17] = 0x18;
                data[b + 18] = 0x00;
                data[b + 19] = 0x08;
                data[b + 20] = 0x00;
                data[b + 21] = 0x19;
                data[b + 22] = 0x00;
                data[b + 23] = 0x08;

                // Terminator at b+24: all zeros (default)

                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                int count = ImageUtilMapActionAnimationCore.CountFrames((uint)FRAME_BASE);
                Assert.Equal(2, count);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void DrawFrame_FrameIndexOutOfRange_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            try
            {
                byte[] data = new byte[ROM_SIZE];
                int b = FRAME_BASE;

                // One frame
                data[b + 0] = 5;
                data[b + 4] = 0x00;
                data[b + 5] = 0x18;
                data[b + 6] = 0x00;
                data[b + 7] = 0x08;
                data[b + 8] = 0x00;
                data[b + 9] = 0x19;
                data[b + 10] = 0x00;
                data[b + 11] = 0x08;
                // Terminator at b+12

                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                // Request frame index 5, but only 1 frame exists
                var result = ImageUtilMapActionAnimationCore.DrawFrame((uint)FRAME_BASE, 5);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void CountFrames_SingleFrame_ReturnsOne()
        {
            var origRom = CoreState.ROM;
            try
            {
                byte[] data = new byte[ROM_SIZE];
                int b = FRAME_BASE;

                // One frame with non-zero data
                data[b + 0] = 1;        // wait (makes term1 non-zero)
                data[b + 4] = 0x00;
                data[b + 5] = 0x18;
                data[b + 6] = 0x00;
                data[b + 7] = 0x08;
                data[b + 8] = 0x00;
                data[b + 9] = 0x19;
                data[b + 10] = 0x00;
                data[b + 11] = 0x08;
                // Terminator at b+12: all zeros

                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                int count = ImageUtilMapActionAnimationCore.CountFrames((uint)FRAME_BASE);
                Assert.Equal(1, count);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        // ---- RenderFrameImage seam tests (#1024) --------------------------

        [Fact]
        public void RenderFrameImage_ValidObjAndPalette_Returns64x64()
        {
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var rom = BuildRomWithObjAndPalette(out uint objPtr, out uint palPtr);
                CoreState.ROM = rom; // RenderFrameImage is rom-aware, but set anyway.

                using (var img = ImageUtilMapActionAnimationCore.RenderFrameImage(rom, objPtr, palPtr))
                {
                    Assert.NotNull(img);
                    Assert.Equal(64, img.Width);
                    Assert.Equal(64, img.Height);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void RenderFrameImage_AcceptsRawOffsets()
        {
            // U.toOffset is idempotent for already-converted offsets, so passing
            // the raw ROM offset (not the GBA pointer) must also work — this is
            // exactly how the refactored DrawFrameImage forwards rom.p32 results.
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var rom = BuildRomWithObjAndPalette(out _, out _);
                CoreState.ROM = rom;

                using (var img = ImageUtilMapActionAnimationCore.RenderFrameImage(
                    rom, (uint)OBJ_OFFSET, (uint)PAL_OFFSET))
                {
                    Assert.NotNull(img);
                    Assert.Equal(64, img.Width);
                    Assert.Equal(64, img.Height);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void RenderFrameImage_NullRom_ReturnsNull()
        {
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var result = ImageUtilMapActionAnimationCore.RenderFrameImage(null, 0x400, 0x900);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void RenderFrameImage_NullImageService_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = null;
                var rom = BuildRomWithObjAndPalette(out uint objPtr, out uint palPtr);
                CoreState.ROM = rom;

                var result = ImageUtilMapActionAnimationCore.RenderFrameImage(rom, objPtr, palPtr);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void RenderFrameImage_ZeroImagePointer_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var rom = BuildRomWithObjAndPalette(out _, out uint palPtr);
                CoreState.ROM = rom;

                // Zero image pointer fails the safety-offset guard (< 0x200).
                var result = ImageUtilMapActionAnimationCore.RenderFrameImage(rom, 0, palPtr);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void RenderFrameImage_OutOfRangePointer_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();
                var rom = BuildRomWithObjAndPalette(out uint objPtr, out _);
                CoreState.ROM = rom;

                // Out-of-range pointer must be rejected without throwing.
                var result = ImageUtilMapActionAnimationCore.RenderFrameImage(rom, objPtr, 0xFFFFFFFF);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void DrawFrame_ParityThroughRenderFrameImage_Returns64x64()
        {
            // Build a 12-byte frame row whose img/pal pointers reference the same
            // compressed OBJ + palette, then assert DrawFrame (ambient CoreState.ROM)
            // renders through the shared RenderFrameImage helper.
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();

                byte[] data = new byte[ROM_SIZE];

                // OBJ payload (LZ77-compressed 64x64 4bpp).
                byte[] raw = new byte[OBJ_UNCOMPRESSED_LEN];
                for (int i = 0; i < raw.Length; i++)
                    raw[i] = (byte)(i & 0xFF);
                byte[] compressed = LZ77.compress(raw);
                Array.Copy(compressed, 0, data, OBJ_OFFSET, compressed.Length);

                // Frame row at FRAME_BASE: wait=5, img -> OBJ_OFFSET, pal -> PAL_OFFSET.
                int b = FRAME_BASE;
                data[b + 0] = 5;
                uint imgPtr = U.toPointer((uint)OBJ_OFFSET);
                uint palPtr = U.toPointer((uint)PAL_OFFSET);
                U.write_u32(data, (uint)(b + 4), imgPtr);
                U.write_u32(data, (uint)(b + 8), palPtr);
                // Terminator at b+12 = all zeros (default).

                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                using (var img = ImageUtilMapActionAnimationCore.DrawFrame((uint)FRAME_BASE, 0))
                {
                    Assert.NotNull(img);
                    Assert.Equal(64, img.Width);
                    Assert.Equal(64, img.Height);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void DrawFrame_NearEofFrameRow_ReturnsNullNoThrow()
        {
            // Place a frame row so that frame + 12 > rom.Data.Length: the explicit
            // 12-byte bounds guard must return null WITHOUT throwing.
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                CoreState.ImageService = new StubImageService();

                byte[] data = new byte[ROM_SIZE];
                // Non-zero leading bytes near EOF so the frame isn't an immediate
                // terminator; FindFrame returns this row, DrawFrameImage rejects it.
                data[ROM_SIZE - 4] = 5;
                data[ROM_SIZE - 3] = 1;

                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                // animeAddress = ROM_SIZE - 4 → frame at ROM_SIZE-4, +12 overflows EOF.
                IImage img = null;
                var ex = Record.Exception(() =>
                {
                    img = ImageUtilMapActionAnimationCore.DrawFrame((uint)(ROM_SIZE - 4), 0);
                });
                Assert.Null(ex);
                using (img)
                {
                    Assert.Null(img);
                }
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }
    }
}
