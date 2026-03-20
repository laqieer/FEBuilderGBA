using System;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class CliPaletteTests
    {
        /// <summary>Create a 16-color GBA palette (32 bytes) with deterministic pseudo-random BGR555 values.</summary>
        static byte[] MakeTestPalette(int seed = 123)
        {
            var rng = new Random(seed);
            byte[] palette = new byte[32]; // 16 colors * 2 bytes
            for (int i = 0; i < 16; i++)
            {
                // BGR555: 5 bits per channel, max 0x7FFF
                ushort color = (ushort)rng.Next(0x8000);
                palette[i * 2] = (byte)(color & 0xFF);
                palette[i * 2 + 1] = (byte)(color >> 8);
            }
            return palette;
        }

        [Fact]
        public void RoundTrip_JascPal()
        {
            byte[] original = MakeTestPalette(1);
            byte[] exported = PaletteFormatConverter.ExportToFormat(original, PaletteFormat.JascPal);
            byte[] imported = PaletteFormatConverter.ImportFromFormat(exported, PaletteFormat.JascPal);
            Assert.Equal(original, imported);
        }

        [Fact]
        public void RoundTrip_AdobeAct()
        {
            byte[] original = MakeTestPalette(2);
            byte[] exported = PaletteFormatConverter.ExportToFormat(original, PaletteFormat.AdobeAct);
            byte[] imported = PaletteFormatConverter.ImportFromFormat(exported, PaletteFormat.AdobeAct);
            Assert.Equal(original, imported);
        }

        [Fact]
        public void RoundTrip_GimpGpl()
        {
            byte[] original = MakeTestPalette(3);
            byte[] exported = PaletteFormatConverter.ExportToFormat(original, PaletteFormat.GimpGpl);
            byte[] imported = PaletteFormatConverter.ImportFromFormat(exported, PaletteFormat.GimpGpl);
            Assert.Equal(original, imported);
        }

        [Fact]
        public void RoundTrip_HexText()
        {
            byte[] original = MakeTestPalette(4);
            byte[] exported = PaletteFormatConverter.ExportToFormat(original, PaletteFormat.HexText);
            byte[] imported = PaletteFormatConverter.ImportFromFormat(exported, PaletteFormat.HexText);
            Assert.Equal(original, imported);
        }

        [Fact]
        public void FormatFromExtension_Pal_ReturnsJascPal()
        {
            Assert.Equal(PaletteFormat.JascPal, PaletteFormatConverter.FormatFromExtension(".pal"));
        }

        [Fact]
        public void FormatFromExtension_Act_ReturnsAdobeAct()
        {
            Assert.Equal(PaletteFormat.AdobeAct, PaletteFormatConverter.FormatFromExtension(".act"));
        }

        [Fact]
        public void DetectFormat_JascHeader_ReturnsJascPal()
        {
            byte[] jascData = System.Text.Encoding.UTF8.GetBytes("JASC-PAL\r\n0100\r\n16\r\n0 0 0\r\n");
            var detected = PaletteFormatConverter.DetectFormat(jascData, ".pal");
            Assert.Equal(PaletteFormat.JascPal, detected);
        }

        [Fact]
        public void ExportToFormat_EmptyPalette_Throws()
        {
            byte[] empty = Array.Empty<byte>();
            Assert.Throws<ArgumentException>(() =>
                PaletteFormatConverter.ExportToFormat(empty, PaletteFormat.JascPal));
        }
    }
}
