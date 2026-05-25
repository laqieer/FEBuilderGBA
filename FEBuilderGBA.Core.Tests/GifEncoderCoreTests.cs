// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for GifEncoderCore and the U.GameFrameSecToGifFrameSec helper that
// powers it. Both pieces are required by the #499 Map Action Animation
// Export/Import parity work.
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class GifEncoderCoreTests
    {
        [Theory]
        [InlineData(0u, 0)]
        [InlineData(1u, 2)]
        [InlineData(2u, 3)]
        [InlineData(3u, 5)]
        [InlineData(60u, 100)]
        public void GameFrameSecToGifFrameSec_RoundingMatchesWinForms(uint fps60, int expectedCs)
        {
            ushort actual = U.GameFrameSecToGifFrameSec(fps60);
            Assert.Equal((ushort)expectedCs, actual);
        }

        [Fact]
        public void Encode_ProducesValidGif89aMagic_AndTrailer()
        {
            var frames = new System.Collections.Generic.List<GifEncoderCore.GifFrame>
            {
                MakeSolidFrame(8, 8, 255, 0, 0, 5),
                MakeSolidFrame(8, 8, 0, 255, 0, 5),
                MakeSolidFrame(8, 8, 0, 0, 255, 5),
            };

            string tmpPath = Path.Combine(Path.GetTempPath(), $"gif_test_{System.Guid.NewGuid():N}.gif");
            try
            {
                GifEncoderCore.Encode(frames, tmpPath);
                byte[] data = File.ReadAllBytes(tmpPath);

                Assert.True(data.Length > 13, $"GIF too short ({data.Length} bytes)");
                Assert.Equal((byte)'G', data[0]);
                Assert.Equal((byte)'I', data[1]);
                Assert.Equal((byte)'F', data[2]);
                Assert.Equal((byte)'8', data[3]);
                Assert.Equal((byte)'9', data[4]);
                Assert.Equal((byte)'a', data[5]);
                Assert.Equal((byte)0x3B, data[data.Length - 1]);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        [Fact]
        public void Encode_EmptyFrameList_DoesNotThrow_AndCreatesNoFile()
        {
            var frames = new System.Collections.Generic.List<GifEncoderCore.GifFrame>();
            string tmpPath = Path.Combine(Path.GetTempPath(), $"gif_empty_{System.Guid.NewGuid():N}.gif");
            try
            {
                GifEncoderCore.Encode(frames, tmpPath);
                Assert.False(File.Exists(tmpPath));
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        [Fact]
        public void IndexedToRgba_FollowsPaletteRgba()
        {
            byte[] indexed = new byte[] { 1, 2 };
            byte[] paletteRgba = new byte[]
            {
                0,   0,   0,   0,
                255, 0,   0,   255,
                0,   255, 0,   255,
                0,   0,   255, 255,
            };

            byte[] rgba = GifEncoderCore.IndexedToRgba(indexed, paletteRgba, 2, 1);
            Assert.Equal(8, rgba.Length);
            Assert.Equal((byte)255, rgba[0]);
            Assert.Equal((byte)0,   rgba[1]);
            Assert.Equal((byte)0,   rgba[2]);
            Assert.Equal((byte)255, rgba[3]);
            Assert.Equal((byte)0,   rgba[4]);
            Assert.Equal((byte)255, rgba[5]);
            Assert.Equal((byte)0,   rgba[6]);
            Assert.Equal((byte)255, rgba[7]);
        }

        [Fact]
        public void IndexedToRgba_TransparentPixelsBecomeAlphaZero()
        {
            byte[] indexed = new byte[] { 0, 1, 0 };
            byte[] paletteRgba = new byte[]
            {
                0,   0,   0,   0,
                100, 100, 100, 255,
            };
            byte[] rgba = GifEncoderCore.IndexedToRgba(indexed, paletteRgba, 3, 1);
            Assert.Equal((byte)0, rgba[3]);
            Assert.Equal((byte)255, rgba[7]);
            Assert.Equal((byte)0, rgba[11]);
        }

        static GifEncoderCore.GifFrame MakeSolidFrame(int w, int h, byte r, byte g, byte b, int delayCs)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4 + 0] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = 255;
            }
            return new GifEncoderCore.GifFrame
            {
                Width = w,
                Height = h,
                RgbaPixels = rgba,
                DelayCs = delayCs,
            };
        }
    }
}
