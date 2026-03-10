using System;
using System.Text;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class PaletteFormatConverterTests
    {
        // Helper: create a simple GBA palette with known colors
        static byte[] MakeGbaPalette(params ushort[] colors)
        {
            byte[] pal = new byte[colors.Length * 2];
            for (int i = 0; i < colors.Length; i++)
            {
                pal[i * 2] = (byte)(colors[i] & 0xFF);
                pal[i * 2 + 1] = (byte)(colors[i] >> 8);
            }
            return pal;
        }

        // Black = 0x0000, White = 0x7FFF, Red(GBA) = 0x001F, Green(GBA) = 0x03E0, Blue(GBA) = 0x7C00
        static readonly byte[] TestPalette = MakeGbaPalette(0x0000, 0x7FFF, 0x001F, 0x03E0, 0x7C00);

        // ===================== Roundtrip Tests =====================

        [Theory]
        [InlineData(PaletteFormat.JascPal)]
        [InlineData(PaletteFormat.AdobeAct)]
        [InlineData(PaletteFormat.GimpGpl)]
        [InlineData(PaletteFormat.HexText)]
        [InlineData(PaletteFormat.GbaRaw)]
        public void Roundtrip_AllFormats_Lossless(PaletteFormat format)
        {
            byte[] exported = PaletteFormatConverter.ExportToFormat(TestPalette, format);
            byte[] imported = PaletteFormatConverter.ImportFromFormat(exported, format);

            // GBA values should be identical (5-bit precision preserved)
            Assert.Equal(TestPalette.Length, imported.Length);
            for (int i = 0; i < TestPalette.Length; i++)
                Assert.Equal(TestPalette[i], imported[i]);
        }

        // ===================== Known Colors =====================

        [Fact]
        public void JascPal_Export_ContainsHeader()
        {
            byte[] data = PaletteFormatConverter.ExportToFormat(TestPalette, PaletteFormat.JascPal);
            string text = Encoding.UTF8.GetString(data);
            Assert.StartsWith("JASC-PAL", text);
            Assert.Contains("0100", text);
            Assert.Contains("5", text); // 5 colors
        }

        [Fact]
        public void JascPal_Export_BlackAndWhite()
        {
            byte[] pal = MakeGbaPalette(0x0000, 0x7FFF);
            byte[] data = PaletteFormatConverter.ExportToFormat(pal, PaletteFormat.JascPal);
            string text = Encoding.UTF8.GetString(data);
            Assert.Contains("0 0 0", text); // black
            Assert.Contains("248 248 248", text); // white (31 << 3 = 248)
        }

        [Fact]
        public void AdobeAct_Export_Has772Bytes()
        {
            byte[] data = PaletteFormatConverter.ExportToFormat(TestPalette, PaletteFormat.AdobeAct);
            Assert.Equal(772, data.Length);
        }

        [Fact]
        public void AdobeAct_Export_ColorCountInFooter()
        {
            byte[] data = PaletteFormatConverter.ExportToFormat(TestPalette, PaletteFormat.AdobeAct);
            int count = (data[768] << 8) | data[769];
            Assert.Equal(5, count);
        }

        [Fact]
        public void GimpGpl_Export_ContainsHeader()
        {
            byte[] data = PaletteFormatConverter.ExportToFormat(TestPalette, PaletteFormat.GimpGpl);
            string text = Encoding.UTF8.GetString(data);
            Assert.StartsWith("GIMP Palette", text);
            Assert.Contains("Name:", text);
        }

        [Fact]
        public void HexText_Export_Format()
        {
            byte[] pal = MakeGbaPalette(0x0000); // black
            byte[] data = PaletteFormatConverter.ExportToFormat(pal, PaletteFormat.HexText);
            string text = Encoding.UTF8.GetString(data).Trim();
            Assert.Equal("000000", text);
        }

        [Fact]
        public void HexText_Roundtrip_White()
        {
            byte[] pal = MakeGbaPalette(0x7FFF); // white (248,248,248)
            byte[] data = PaletteFormatConverter.ExportToFormat(pal, PaletteFormat.HexText);
            string text = Encoding.UTF8.GetString(data).Trim();
            Assert.Equal("F8F8F8", text);
            byte[] back = PaletteFormatConverter.ImportFromFormat(data, PaletteFormat.HexText);
            Assert.Equal(pal, back);
        }

        // ===================== Format Detection =====================

        [Fact]
        public void DetectFormat_JascPalHeader()
        {
            byte[] data = Encoding.UTF8.GetBytes("JASC-PAL\r\n0100\r\n16\r\n0 0 0\r\n");
            Assert.Equal(PaletteFormat.JascPal, PaletteFormatConverter.DetectFormat(data, ".pal"));
        }

        [Fact]
        public void DetectFormat_GimpGplHeader()
        {
            byte[] data = Encoding.UTF8.GetBytes("GIMP Palette\nName: test\n#\n0 0 0\tBlack\n");
            Assert.Equal(PaletteFormat.GimpGpl, PaletteFormatConverter.DetectFormat(data, ".gpl"));
        }

        [Fact]
        public void DetectFormat_AdobeActByExtension()
        {
            byte[] data = new byte[768]; // valid ACT size
            Assert.Equal(PaletteFormat.AdobeAct, PaletteFormatConverter.DetectFormat(data, ".act"));
        }

        [Fact]
        public void DetectFormat_HexTextByContent()
        {
            byte[] data = Encoding.UTF8.GetBytes("FF0000\n00FF00\n0000FF\n");
            Assert.Equal(PaletteFormat.HexText, PaletteFormatConverter.DetectFormat(data, ".txt"));
        }

        [Fact]
        public void DetectFormat_GbaRawFallback()
        {
            byte[] data = new byte[] { 0x00, 0x00, 0xFF, 0x7F }; // 2 GBA colors
            Assert.Equal(PaletteFormat.GbaRaw, PaletteFormatConverter.DetectFormat(data, ".pal"));
        }

        // ===================== FormatFromExtension (Export) =====================

        [Theory]
        [InlineData(".pal", PaletteFormat.JascPal)]
        [InlineData(".act", PaletteFormat.AdobeAct)]
        [InlineData(".gpl", PaletteFormat.GimpGpl)]
        [InlineData(".txt", PaletteFormat.HexText)]
        [InlineData(".gbapal", PaletteFormat.GbaRaw)]
        public void FormatFromExtension_Maps(string ext, PaletteFormat expected)
        {
            Assert.Equal(expected, PaletteFormatConverter.FormatFromExtension(ext));
        }

        // ===================== Edge Cases =====================

        [Fact]
        public void Import_JascPal_IgnoresExtraLines()
        {
            string text = "JASC-PAL\r\n0100\r\n2\r\n255 0 0\r\n0 255 0\r\nExtra garbage\r\n";
            byte[] data = Encoding.UTF8.GetBytes(text);
            byte[] gba = PaletteFormatConverter.ImportFromFormat(data, PaletteFormat.JascPal);
            Assert.Equal(4, gba.Length); // 2 colors * 2 bytes
        }

        [Fact]
        public void Import_GimpGpl_SkipsComments()
        {
            string text = "GIMP Palette\nName: Test\nColumns: 16\n#\n# Comment\n128 64 32\tColor\n";
            byte[] data = Encoding.UTF8.GetBytes(text);
            byte[] gba = PaletteFormatConverter.ImportFromFormat(data, PaletteFormat.GimpGpl);
            Assert.Equal(2, gba.Length); // 1 color
        }

        [Fact]
        public void Import_HexText_SupportsHashPrefix()
        {
            string text = "#FF0000\n#00FF00\n";
            byte[] data = Encoding.UTF8.GetBytes(text);
            byte[] gba = PaletteFormatConverter.ImportFromFormat(data, PaletteFormat.HexText);
            Assert.Equal(4, gba.Length); // 2 colors
        }

        [Fact]
        public void Import_AdobeAct_768ByteFile()
        {
            // 768 bytes = 256 colors, no footer
            byte[] act = new byte[768];
            act[0] = 255; act[1] = 0; act[2] = 0; // first color = red
            byte[] gba = PaletteFormatConverter.ImportFromFormat(act, PaletteFormat.AdobeAct);
            Assert.Equal(512, gba.Length); // 256 colors * 2 bytes
        }

        [Fact]
        public void Export_NullPalette_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                PaletteFormatConverter.ExportToFormat(null!, PaletteFormat.JascPal));
        }

        [Fact]
        public void Export_EmptyPalette_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                PaletteFormatConverter.ExportToFormat(new byte[0], PaletteFormat.JascPal));
        }

        [Fact]
        public void Import_InvalidJascHeader_Throws()
        {
            byte[] data = Encoding.UTF8.GetBytes("NOT-JASC\n");
            Assert.Throws<FormatException>(() =>
                PaletteFormatConverter.ImportFromFormat(data, PaletteFormat.JascPal));
        }

        [Fact]
        public void Import_TooSmallAct_Throws()
        {
            Assert.Throws<FormatException>(() =>
                PaletteFormatConverter.ImportFromFormat(new byte[10], PaletteFormat.AdobeAct));
        }

        // ===================== 16-Color GBA Palette Roundtrip =====================

        [Fact]
        public void Roundtrip_16ColorPalette_AllFormats()
        {
            // Standard 16-color GBA palette (32 bytes)
            ushort[] colors = new ushort[16];
            for (int i = 0; i < 16; i++)
                colors[i] = (ushort)(i * 0x0421); // gradient
            byte[] pal = MakeGbaPalette(colors);

            foreach (PaletteFormat fmt in Enum.GetValues(typeof(PaletteFormat)))
            {
                byte[] exported = PaletteFormatConverter.ExportToFormat(pal, fmt);
                byte[] imported = PaletteFormatConverter.ImportFromFormat(exported, fmt);
                Assert.Equal(pal, imported);
            }
        }

        // ===================== DefaultExtension =====================

        [Theory]
        [InlineData(PaletteFormat.GbaRaw, ".gbapal")]
        [InlineData(PaletteFormat.JascPal, ".pal")]
        [InlineData(PaletteFormat.AdobeAct, ".act")]
        [InlineData(PaletteFormat.GimpGpl, ".gpl")]
        [InlineData(PaletteFormat.HexText, ".txt")]
        public void DefaultExtension_Maps(PaletteFormat fmt, string expected)
        {
            Assert.Equal(expected, PaletteFormatConverter.DefaultExtension(fmt));
        }
    }
}
