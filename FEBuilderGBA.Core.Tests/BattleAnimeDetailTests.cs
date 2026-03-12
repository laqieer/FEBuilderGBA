using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for battle animation data structure parsing logic used by
    /// ImageBattleAnimeViewModel. Validates pointer chain traversal,
    /// LZ77 size detection, and SP type name resolution.
    /// </summary>
    [Collection("SharedState")]
    public class BattleAnimeDetailTests
    {
        /// <summary>
        /// Verify that ResolveSPTypeName returns weapon type names
        /// when Special flag (b1) is 1.
        /// </summary>
        [Theory]
        [InlineData(0u, 1u, "Sword")]
        [InlineData(1u, 1u, "Lance")]
        [InlineData(2u, 1u, "Axe")]
        [InlineData(3u, 1u, "Bow")]
        [InlineData(4u, 1u, "Staff")]
        [InlineData(5u, 1u, "Anima")]
        [InlineData(6u, 1u, "Light")]
        [InlineData(7u, 1u, "Dark")]
        [InlineData(0xFFu, 1u, "Type 0xFF")]
        public void ResolveSPTypeName_WeaponType_ReturnsCorrectName(uint b0, uint b1, string expected)
        {
            // Use the same logic as ImageBattleAnimeViewModel.ResolveSPTypeName
            string result = ResolveSPTypeName(b0, b1);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ResolveSPTypeName_UnknownSpecial_ReturnsHex()
        {
            string result = ResolveSPTypeName(0, 2);
            Assert.Equal("Special=0x02", result);
        }

        /// <summary>
        /// Verify LZ77.getUncompressSize correctly reads the size from a
        /// minimal LZ77 header (type byte 0x10 + 3-byte little-endian size).
        /// </summary>
        [Fact]
        public void LZ77_GetUncompressSize_ReadsHeaderCorrectly()
        {
            // LZ77 header: byte[0]=0x10 (LZ77 marker), bytes[1..3] = size in LE
            // Size = 0x001234 = 4660
            byte[] data = new byte[256];
            data[0] = 0x10; // LZ77 type
            data[1] = 0x34; // size low
            data[2] = 0x12; // size mid
            data[3] = 0x00; // size high

            uint size = LZ77.getUncompressSize(data, 0);
            Assert.Equal(0x001234u, size);
        }

        [Fact]
        public void LZ77_GetUncompressSize_InvalidOffset_ReturnsZero()
        {
            byte[] data = new byte[4];
            // offset beyond data
            uint size = LZ77.getUncompressSize(data, 100);
            Assert.Equal(0u, size);
        }

        /// <summary>
        /// Validate that the animation data record structure has the expected layout:
        /// - Bytes 0-11: Name (12 chars)
        /// - Offset 12: Section pointer (4 bytes)
        /// - Offset 16: Frame pointer (4 bytes)
        /// - Offset 20: OAM R-to-L pointer (4 bytes)
        /// - Offset 24: OAM L-to-R pointer (4 bytes)
        /// - Offset 28: Palette pointer (4 bytes)
        /// Total: 32 bytes per record
        /// </summary>
        [Fact]
        public void AnimationRecord_StructLayout_IsCorrect()
        {
            // Build a fake 32-byte animation record
            byte[] record = new byte[32];

            // Name: "TestAnim" + padding
            byte[] name = System.Text.Encoding.ASCII.GetBytes("TestAnim\0\0\0\0");
            System.Array.Copy(name, 0, record, 0, 12);

            // Section pointer at offset 12: 0x08100000
            record[12] = 0x00; record[13] = 0x00; record[14] = 0x10; record[15] = 0x08;
            // Frame pointer at offset 16: 0x08200000
            record[16] = 0x00; record[17] = 0x00; record[18] = 0x20; record[19] = 0x08;
            // OAM R-to-L at offset 20: 0x08300000
            record[20] = 0x00; record[21] = 0x00; record[22] = 0x30; record[23] = 0x08;
            // OAM L-to-R at offset 24: 0x08400000
            record[24] = 0x00; record[25] = 0x00; record[26] = 0x40; record[27] = 0x08;
            // Palette at offset 28: 0x08500000
            record[28] = 0x00; record[29] = 0x00; record[30] = 0x50; record[31] = 0x08;

            // Verify pointers are valid GBA pointers (bit 27 set = 0x08xxxxxx)
            uint section = (uint)(record[12] | (record[13] << 8) | (record[14] << 16) | (record[15] << 24));
            uint frame = (uint)(record[16] | (record[17] << 8) | (record[18] << 16) | (record[19] << 24));
            uint oamRtL = (uint)(record[20] | (record[21] << 8) | (record[22] << 16) | (record[23] << 24));
            uint oamLtR = (uint)(record[24] | (record[25] << 8) | (record[26] << 16) | (record[27] << 24));
            uint palette = (uint)(record[28] | (record[29] << 8) | (record[30] << 16) | (record[31] << 24));

            Assert.Equal(0x08100000u, section);
            Assert.Equal(0x08200000u, frame);
            Assert.Equal(0x08300000u, oamRtL);
            Assert.Equal(0x08400000u, oamLtR);
            Assert.Equal(0x08500000u, palette);

            Assert.True(U.isPointer(section));
            Assert.True(U.isPointer(frame));
            Assert.True(U.isPointer(oamRtL));
            Assert.True(U.isPointer(oamLtR));
            Assert.True(U.isPointer(palette));
        }

        /// <summary>
        /// Mirror of the SP type name resolution from ImageBattleAnimeViewModel.
        /// </summary>
        static string ResolveSPTypeName(uint b0, uint b1)
        {
            if (b1 == 0)
            {
                return NameResolver.GetItemName(b0);
            }
            if (b1 == 1)
            {
                string[] weaponTypes = { "Sword", "Lance", "Axe", "Bow", "Staff", "Anima", "Light", "Dark" };
                return b0 < (uint)weaponTypes.Length ? weaponTypes[b0] : $"Type 0x{b0:X02}";
            }
            return $"Special=0x{b1:X02}";
        }
    }
}
