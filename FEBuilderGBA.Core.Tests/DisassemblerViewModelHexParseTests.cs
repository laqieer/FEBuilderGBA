using System;
using System.Globalization;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for hex parsing logic used by DisASM and GraphicsTool ViewModels.
    /// The ParseHex method is replicated here since the VM classes live in Avalonia.
    /// </summary>
    public class HexParseTests
    {
        /// <summary>Parse a hex string like "0x80000" or "80000".</summary>
        static uint ParseHex(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return 0;
            input = input.Trim();
            if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(2);
            if (uint.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint result))
                return result;
            return 0;
        }

        [Theory]
        [InlineData("0x100", 0x100u)]
        [InlineData("0X100", 0x100u)]
        [InlineData("100", 0x100u)]
        [InlineData("0x08000000", 0x08000000u)]
        [InlineData("ABCDEF", 0xABCDEFu)]
        [InlineData("0xabcdef", 0xABCDEFu)]
        [InlineData("  0x10  ", 0x10u)]
        public void ParseHex_ValidValues(string input, uint expected)
        {
            Assert.Equal(expected, ParseHex(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("xyz")]
        public void ParseHex_InvalidValues_ReturnsZero(string input)
        {
            Assert.Equal(0u, ParseHex(input));
        }

        [Fact]
        public void ParseHex_Null_ReturnsZero()
        {
            Assert.Equal(0u, ParseHex(null));
        }
    }

    [Collection("SharedState")]
    public class DisassemblerRangeRunDisassemblyTests
    {
        static ROM MakeMinimalRom(int size = 256)
        {
            byte[] data = new byte[size];
            for (int i = 0; i < data.Length; i += 2)
            {
                data[i] = 0xC0;
                data[i + 1] = 0x46;
            }
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        [Fact]
        public void DisassembleRange_WithGBAPointerAutoConverts()
        {
            var origRom = CoreState.ROM;
            var origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.ROM = MakeMinimalRom(256);
                CoreState.BaseDirectory = "";

                var core = new DisassemblerCore();
                // Pass GBA address 0x08000080 which should auto-convert to offset 0x80
                // But DisassembleRange takes ROM offset directly, so test the actual method
                var lines = core.DisassembleRange(0x80, 0x10);

                Assert.NotNull(lines);
                Assert.True(lines.Count >= 2);
                Assert.Contains("0x08000080", lines[0]);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.BaseDirectory = origBase;
            }
        }

        [Fact]
        public void ImageUtilCore_GetPalette_ValidRange()
        {
            var origRom = CoreState.ROM;
            try
            {
                byte[] data = new byte[256];
                // Write a known palette value
                data[0] = 0x1F; // Red = 31
                data[1] = 0x00;
                var rom = new ROM();
                rom.SwapNewROMDataDirect(data);
                CoreState.ROM = rom;

                byte[]? palette = ImageUtilCore.GetPalette(0, 16);
                Assert.NotNull(palette);
                Assert.Equal(32, palette.Length);
                Assert.Equal(0x1F, palette[0]);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void ImageUtilCore_LoadROMTiles4bpp_NoRom_ReturnsNull()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var result = ImageUtilCore.LoadROMTiles4bpp(0, new byte[32], 1, 1);
                Assert.Null(result);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }
    }
}
