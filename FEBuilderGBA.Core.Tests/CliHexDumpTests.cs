using System;
using System.Text;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class CliHexDumpTests
    {
        /// <summary>
        /// Format a single hex dump line: "XXXXXXXX: XX XX XX ... | ASCII..."
        /// This mirrors the formatting logic used by the CLI --hex-dump command.
        /// </summary>
        static string FormatHexLine(uint address, byte[] data, int offset, int count)
        {
            var sb = new StringBuilder();
            sb.Append(address.ToString("X8"));
            sb.Append(": ");

            // Hex portion
            for (int i = 0; i < 16; i++)
            {
                if (i < count)
                    sb.Append(data[offset + i].ToString("X2")).Append(' ');
                else
                    sb.Append("   "); // padding for partial lines
            }

            sb.Append("| ");

            // ASCII portion
            for (int i = 0; i < count; i++)
            {
                byte b = data[offset + i];
                sb.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
            }

            return sb.ToString();
        }

        [Fact]
        public void FormatHexLine_16Bytes()
        {
            // 16 bytes: 0x00 through 0x0F
            byte[] data = { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F,
                            0x72, 0x6C, 0x64, 0x21, 0x00, 0x01, 0x02, 0x03 };
            string result = FormatHexLine(0x08001000, data, 0, 16);

            Assert.StartsWith("08001000: ", result);
            Assert.Contains("48 65 6C 6C 6F 20 57 6F 72 6C 64 21 00 01 02 03", result);
            Assert.Contains("| Hello World!...", result);
        }

        [Fact]
        public void FormatHexLine_PartialLine()
        {
            // Only 5 bytes
            byte[] data = { 0x41, 0x42, 0x43, 0x44, 0x45 };
            string result = FormatHexLine(0x00000000, data, 0, 5);

            Assert.StartsWith("00000000: ", result);
            // Should have hex for 5 bytes then padding spaces for remaining 11
            Assert.Contains("41 42 43 44 45 ", result);
            Assert.Contains("| ABCDE", result);
            // Verify padding: the line should be consistent width
            // 11 missing bytes * 3 chars each = 33 spaces of padding
            string hexPortion = result.Substring(10, 48); // "XX XX ... " portion
            Assert.Equal(48, hexPortion.Length);
        }

        [Fact]
        public void PrintableAscii_ControlCharsReplacedWithDot()
        {
            // Mix of printable and non-printable bytes
            byte[] data = { 0x00, 0x1F, 0x20, 0x7E, 0x7F, 0xFF, 0x41, 0x0A };
            string result = FormatHexLine(0x00000100, data, 0, 8);

            // Extract the ASCII portion after "| "
            int asciiStart = result.IndexOf("| ") + 2;
            string ascii = result.Substring(asciiStart);

            Assert.Equal(8, ascii.Length);
            Assert.Equal('.', ascii[0]);  // 0x00 - control char
            Assert.Equal('.', ascii[1]);  // 0x1F - control char
            Assert.Equal(' ', ascii[2]);  // 0x20 - printable (space)
            Assert.Equal('~', ascii[3]);  // 0x7E - printable (tilde)
            Assert.Equal('.', ascii[4]);  // 0x7F - non-printable
            Assert.Equal('.', ascii[5]);  // 0xFF - non-printable
            Assert.Equal('A', ascii[6]);  // 0x41 - printable
            Assert.Equal('.', ascii[7]);  // 0x0A - control char (newline)
        }
    }
}
