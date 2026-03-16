using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class GraphicsToolViewModelTests
    {
        [Fact]
        public void GetPalette_WithMinimalRom_ReturnsPalette()
        {
            var origRom = CoreState.ROM;
            try
            {
                // Create ROM with known palette data at offset 0
                byte[] data = new byte[256];
                // Write 16 colors (32 bytes) of palette at offset 0
                for (int i = 0; i < 32; i++)
                    data[i] = (byte)(i * 4);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                byte[]? palette = ImageUtilCore.GetPalette(0, 16);
                Assert.NotNull(palette);
                Assert.Equal(32, palette.Length);
                Assert.Equal(0, palette[0]);
                Assert.Equal(4, palette[1]);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void GetPalette_BeyondRom_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            try
            {
                byte[] data = new byte[16];
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                // Palette at offset 100 is beyond the 16-byte ROM
                byte[]? palette = ImageUtilCore.GetPalette(100, 16);
                Assert.Null(palette);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void GetCompressedPalette_WithNoRom_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                byte[]? result = ImageUtilCore.GetCompressedPalette(0);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void LoadROMTiles4bpp_BeyondRom_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                byte[] data = new byte[16];
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;
                // Leave ImageService as-is (might be null)

                // 8x8 tiles need 32 bytes each; requesting at offset 0 with 2x2 tiles = 128 bytes > 16
                var image = ImageUtilCore.LoadROMTiles4bpp(0, new byte[32], 2, 2);
                Assert.Null(image);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }

        [Fact]
        public void LoadROMTiles8bpp_BeyondRom_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            var origService = CoreState.ImageService;
            try
            {
                byte[] data = new byte[16];
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                // 8bpp: 64 bytes per tile, 2x2 = 256 bytes > 16
                var image = ImageUtilCore.LoadROMTiles8bpp(0, new byte[512], 2, 2);
                Assert.Null(image);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.ImageService = origService;
            }
        }
    }
}
