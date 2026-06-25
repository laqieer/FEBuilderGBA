using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1421: the v2 "Import PNG" handler must write the coupled IMAGE/PALETTE/TSA
    /// trio off the SHARED header, not just a raw TSA blob. These tests lock the
    /// VM's address derivation (so the offsets can't silently drift away from the
    /// WinForms ImageTSAAnime2Form layout) and verify the 240->256 right-pad and
    /// the raw header-wrapped TSA reader.
    /// </summary>
    [Collection("SharedState")]
    public class ImageTSAAnime2ImportTests
    {
        // The VM stores CurrentAddr = dataAddr + 20 (the first 12-byte entry).
        // WinForms layout off the header base (dataAddr):
        //   palette @ dataAddr + 4, image @ dataAddr + 16, entry-0 TSA @ dataAddr + 20 + 8.
        [Fact]
        public void PointerDerivation_MatchesWinFormsLayout()
        {
            uint dataAddr = 0x00500000u; // header base
            var vm = new ImageTSAAnime2ViewModel { CurrentAddr = dataAddr + ImageTSAAnime2ViewModel.HEADER_SIZE };

            Assert.Equal(dataAddr, vm.HeaderBase);
            Assert.Equal(dataAddr + 4u, vm.PalettePointerAddr);   // WF dataAddr+4
            Assert.Equal(dataAddr + 16u, vm.ImagePointerAddr);    // WF dataAddr+16
            Assert.Equal(dataAddr + 20u + 8u, vm.TSAPointerAddr); // WF entry-0 TSA slot
        }

        [Fact]
        public void HeaderBase_IsZero_WhenAddrTooSmall()
        {
            var vm = new ImageTSAAnime2ViewModel { CurrentAddr = 4 };
            // Guards the underflow case the import handler also rejects.
            Assert.Equal(0u, vm.HeaderBase);
        }

        [Fact]
        public void PadIndexedWidth_240To256_InsertsTransparentRightMargin()
        {
            const int srcW = 240, dstW = 256, h = 2;
            var src = new byte[srcW * h];
            // Fill every source pixel with a non-zero marker so we can detect padding.
            for (int i = 0; i < src.Length; i++) src[i] = 7;

            byte[] padded = ImageTSAAnime2ViewModel.PadIndexedWidth(src, srcW, h, dstW);

            Assert.Equal(dstW * h, padded.Length);
            for (int y = 0; y < h; y++)
            {
                // First srcW columns copied verbatim...
                for (int x = 0; x < srcW; x++)
                    Assert.Equal((byte)7, padded[y * dstW + x]);
                // ...trailing 16 columns are index-0 (transparent) margin.
                for (int x = srcW; x < dstW; x++)
                    Assert.Equal((byte)0, padded[y * dstW + x]);
            }
        }

        [Fact]
        public void PadIndexedWidth_SameWidth_ReturnsOriginal()
        {
            var src = new byte[256 * 4];
            var padded = ImageTSAAnime2ViewModel.PadIndexedWidth(src, 256, 4, 256);
            Assert.Same(src, padded);
        }

        [Fact]
        public void ReadHeaderTSABytes_ReadsExactHeaderLength()
        {
            // Build a raw header-wrapped TSA: mhx=29 (0x1D), mhy=19 (0x13).
            // length = 2 + (29+1)*(19+1)*2 = 1202.
            int mhx = 0x1D, mhy = 0x13;
            int len = 2 + (mhx + 1) * (mhy + 1) * 2;
            var data = new byte[len + 64]; // extra trailing bytes that must NOT be read
            data[0] = (byte)mhx;
            data[1] = (byte)mhy;

            byte[] tsa = ImageTSAAnime2ViewModel.ReadHeaderTSABytes(data, 0);
            Assert.NotNull(tsa);
            Assert.Equal(len, tsa.Length);
            Assert.Equal(1202, tsa.Length);
        }

        [Fact]
        public void ReadHeaderTSABytes_ReturnsNull_WhenOutOfRange()
        {
            var data = new byte[10];
            data[0] = 0xFF; // huge header -> length exceeds buffer
            data[1] = 0xFF;
            Assert.Null(ImageTSAAnime2ViewModel.ReadHeaderTSABytes(data, 0));
            Assert.Null(ImageTSAAnime2ViewModel.ReadHeaderTSABytes(data, 9)); // offset+2 > length
            Assert.Null(ImageTSAAnime2ViewModel.ReadHeaderTSABytes(null, 0));
        }
    }
}
