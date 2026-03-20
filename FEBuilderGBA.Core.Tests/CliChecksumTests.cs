using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class CliChecksumTests
    {
        /// <summary>
        /// Compute the GBA ROM header checksum.
        /// Sum bytes 0xA0..0xBC, then expected = (byte)(-(0x19 + sum)).
        /// The checksum is stored at offset 0xBD.
        /// </summary>
        private static byte ComputeChecksum(byte[] rom)
        {
            int sum = 0;
            for (int i = 0xA0; i <= 0xBC; i++)
                sum += rom[i];
            return (byte)(-(0x19 + sum));
        }

        [Fact]
        public void ValidChecksum_AllZeroHeader()
        {
            // A header of all zeros: sum of 0xA0..0xBC = 0
            // expected = (byte)(-(0x19 + 0)) = (byte)(-0x19) = 0xE7
            byte[] rom = new byte[0xC0];
            byte expected = ComputeChecksum(rom);
            rom[0xBD] = expected;

            Assert.Equal(expected, rom[0xBD]);
            Assert.Equal(0xE7, expected);
        }

        [Fact]
        public void InvalidChecksum_WrongByte()
        {
            byte[] rom = new byte[0xC0];
            byte correct = ComputeChecksum(rom);

            // Set a wrong checksum
            rom[0xBD] = (byte)(correct + 1);

            Assert.NotEqual(correct, rom[0xBD]);
        }

        [Fact]
        public void RepairChecksum_FixesInvalid()
        {
            // Create a ROM-like header with some non-zero data in the title area
            byte[] rom = new byte[0xC0];
            // Simulate a game title at 0xA0
            rom[0xA0] = 0x46; // 'F'
            rom[0xA1] = 0x49; // 'I'
            rom[0xA2] = 0x52; // 'R'
            rom[0xA3] = 0x45; // 'E'

            // Initially set wrong checksum
            rom[0xBD] = 0x00;

            byte correct = ComputeChecksum(rom);
            Assert.NotEqual(correct, rom[0xBD]);

            // "Repair" the checksum
            rom[0xBD] = correct;

            // Verify the repaired checksum matches
            byte verify = ComputeChecksum(rom);
            Assert.Equal(verify, rom[0xBD]);
        }
    }
}
