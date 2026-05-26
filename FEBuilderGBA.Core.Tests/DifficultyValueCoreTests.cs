using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class DifficultyValueCoreTests
    {
        // -- Pack ---------------------------------------------------------------

        [Fact]
        public void Pack_Sample_0x0123_DecomposesToCorrectNibbles()
        {
            // WinForms encoding: Hard << 4 | Normal << 8 | Easy << 0.
            // So Hard=2 (>> 4 = 0x02), Normal=1 (>> 8 = 0x01), Easy=3 (= 0x03)
            // packs to 0x0123.
            ushort packed = DifficultyValueCore.Pack(hardBoost: 2, normalPenalty: 1, easyPenalty: 3);
            Assert.Equal((ushort)0x0123, packed);
        }

        [Fact]
        public void Pack_AllZero_IsZero()
        {
            Assert.Equal((ushort)0x0000, DifficultyValueCore.Pack(0, 0, 0));
        }

        [Fact]
        public void Pack_AllMax_IsExpected()
        {
            // 0xF << 8 | 0xF << 4 | 0xF = 0x0FFF
            Assert.Equal((ushort)0x0FFF, DifficultyValueCore.Pack(0xF, 0xF, 0xF));
        }

        [Fact]
        public void Pack_OutOfRange_IsClampedTo15()
        {
            // Values above 15 clamp to 15 (not masked — so 0x12 becomes 0xF, not 0x2).
            Assert.Equal((ushort)0x0FFF, DifficultyValueCore.Pack(0x12, 0x12, 0x12));
        }

        [Fact]
        public void Pack_NegativeInputs_ClampedTo0()
        {
            // Negative values clamp to 0 (would wrap to 0xF if we just masked).
            Assert.Equal((ushort)0x0000, DifficultyValueCore.Pack(-1, -5, -100));
        }

        // -- Unpack -------------------------------------------------------------

        [Fact]
        public void Unpack_Sample_0x0123_ReturnsExpectedNibbles()
        {
            var (h, n, e) = DifficultyValueCore.Unpack(0x0123);
            Assert.Equal(2, h);
            Assert.Equal(1, n);
            Assert.Equal(3, e);
        }

        [Fact]
        public void Unpack_AllMaxNibbles()
        {
            var (h, n, e) = DifficultyValueCore.Unpack(0x0FFF);
            Assert.Equal(0xF, h);
            Assert.Equal(0xF, n);
            Assert.Equal(0xF, e);
        }

        // -- Roundtrip ----------------------------------------------------------

        [Theory]
        [InlineData(0x0000)]
        [InlineData(0x0123)]
        [InlineData(0x0F00)]
        [InlineData(0x0010)]
        [InlineData(0x0001)]
        [InlineData(0x0FFF)]
        public void Roundtrip_PackUnpack_PreservesLow12Bits(int value)
        {
            ushort v = (ushort)value;
            var (h, n, e) = DifficultyValueCore.Unpack(v);
            ushort packed = DifficultyValueCore.Pack(h, n, e);
            Assert.Equal((ushort)(v & 0x0FFF), packed);
        }

        // -- PackPreservingReserved --------------------------------------------

        [Fact]
        public void PackPreservingReserved_KeepsHighNibble()
        {
            // 0xA000 reserved | new packed 0x0123 = 0xA123
            ushort packed = DifficultyValueCore.PackPreservingReserved(
                hardBoost: 2, normalPenalty: 1, easyPenalty: 3, originalValue: 0xA000);
            Assert.Equal((ushort)0xA123, packed);
        }

        [Fact]
        public void PackPreservingReserved_ZeroOriginal_BehavesAsPack()
        {
            ushort plain = DifficultyValueCore.Pack(2, 1, 3);
            ushort preserved = DifficultyValueCore.PackPreservingReserved(2, 1, 3, 0x0000);
            Assert.Equal(plain, preserved);
        }

        // -- Format -------------------------------------------------------------

        [Fact]
        public void Format_MatchesWinFormsPattern()
        {
            string s = DifficultyValueCore.Format(0x0123);
            Assert.Equal("Hard:+2 Normal:-1 Easy:-3", s);
        }

        // -- IsSupported --------------------------------------------------------

        [Fact]
        public void IsSupported_NullRom_ReturnsFalse()
        {
            Assert.False(DifficultyValueCore.IsSupported(null));
        }

        [Fact]
        public void IsSupported_FE8U_ReturnsTrue()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
            Assert.True(DifficultyValueCore.IsSupported(rom));
        }

        [Fact]
        public void IsSupported_FE6_ReturnsFalse()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
            Assert.False(DifficultyValueCore.IsSupported(rom));
        }
    }
}
