using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class WriteValidatorTests
    {
        [Theory]
        [InlineData(0u, true)]
        [InlineData(0xFFu, true)]
        [InlineData(0x100u, false)]
        [InlineData(0xFFFFu, false)]
        public void ValidateU8(uint value, bool expected)
        {
            var (isValid, _) = WriteValidator.ValidateU8(value);
            Assert.Equal(expected, isValid);
        }

        [Theory]
        [InlineData(0u, true)]
        [InlineData(0xFFFFu, true)]
        [InlineData(0x10000u, false)]
        public void ValidateU16(uint value, bool expected)
        {
            var (isValid, _) = WriteValidator.ValidateU16(value);
            Assert.Equal(expected, isValid);
        }

        [Fact]
        public void ValidateU32_AlwaysValid()
        {
            var (isValid, _) = WriteValidator.ValidateU32(0xFFFFFFFF);
            Assert.True(isValid);
        }

        [Fact]
        public void ValidatePointer_NullIsValid()
        {
            var (isValid, _) = WriteValidator.ValidatePointer(0);
            Assert.True(isValid);
        }

        [Theory]
        [InlineData(0x08000000u, true)]
        [InlineData(0x07FFFFFFu, false)]
        [InlineData(0x0A000000u, false)]
        [InlineData(0x12345678u, false)]
        public void ValidatePointer_Range(uint value, bool expected)
        {
            // Clear ROM so pointer validation only checks range, not ROM size
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                var (isValid, _) = WriteValidator.ValidatePointer(value);
                Assert.Equal(expected, isValid);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void ValidateNotProtectedId_ZeroBlocked()
        {
            var (isValid, error) = WriteValidator.ValidateNotProtectedId(0);
            Assert.False(isValid);
            Assert.Contains("reserved", error!);
        }

        [Fact]
        public void ValidateNotProtectedId_NonZeroAllowed()
        {
            var (isValid, _) = WriteValidator.ValidateNotProtectedId(1);
            Assert.True(isValid);
        }

        [Fact]
        public void ValidateAddress_NoRom_Fails()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null!;
                var (isValid, _) = WriteValidator.ValidateAddress(0, 4);
                Assert.False(isValid);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void ValidateAddress_WithRom_ChecksBounds()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[256]);
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var (valid1, _) = WriteValidator.ValidateAddress(0, 256);
                Assert.True(valid1);

                var (valid2, _) = WriteValidator.ValidateAddress(0, 257);
                Assert.False(valid2);

                var (valid3, _) = WriteValidator.ValidateAddress(200, 100);
                Assert.False(valid3);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void ValidateU8_ErrorContainsFieldName()
        {
            var (_, error) = WriteValidator.ValidateU8(999, "HP");
            Assert.Contains("HP", error!);
        }
    }
}
