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
    }
}
