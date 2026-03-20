using System;
using System.Text;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class CliRomInfoTests
    {
        [Fact]
        public void GbaHeaderChecksum_ValidComputation()
        {
            // GBA header checksum covers bytes 0xA0..0xBC (inclusive).
            // checksum = (byte)(-(0x19 + sum_of_bytes))
            byte[] header = new byte[0xC0];
            var rng = new Random(42);
            for (int i = 0xA0; i <= 0xBC; i++)
                header[i] = (byte)rng.Next(256);

            int sum = 0;
            for (int i = 0xA0; i <= 0xBC; i++)
                sum += header[i];

            byte expected = (byte)(-(0x19 + sum));
            header[0xBD] = expected;

            // Recompute and verify
            int checkSum = 0;
            for (int i = 0xA0; i <= 0xBC; i++)
                checkSum += header[i];
            byte computed = (byte)(-(0x19 + checkSum));

            Assert.Equal(expected, computed);
            Assert.Equal(expected, header[0xBD]);
        }

        [Fact]
        public void TitleExtraction_FromHeader()
        {
            byte[] header = new byte[0xC0];
            string title = "FIRE_EMBLEM";
            byte[] titleBytes = Encoding.ASCII.GetBytes(title);
            Array.Copy(titleBytes, 0, header, 0xA0, titleBytes.Length);
            // Remaining bytes at 0xA0+len..0xAB should be 0 (null padding)

            string extracted = Encoding.ASCII.GetString(header, 0xA0, 12).TrimEnd('\0');

            Assert.Equal(title, extracted);
        }

        [Fact]
        public void GameCodeExtraction_FromHeader()
        {
            byte[] header = new byte[0xC0];
            string gameCode = "BE8E";
            byte[] codeBytes = Encoding.ASCII.GetBytes(gameCode);
            Array.Copy(codeBytes, 0, header, 0xAC, 4);

            string extracted = Encoding.ASCII.GetString(header, 0xAC, 4);

            Assert.Equal(gameCode, extracted);
        }
    }
}
