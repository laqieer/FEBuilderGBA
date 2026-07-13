using System;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    public class BuildfileRomFixtureTests
    {
        [Fact]
        public void GetSparseExtensionSize_ObservedFixture_UsesPreferredBoundedExtension()
        {
            const int observedFixtureSize = 17_277_016;

            int extensionSize = BuildfileRomFixture.GetSparseExtensionSize(observedFixtureSize);

            Assert.Equal(BuildfileRomFixture.PreferredExtensionSize, extensionSize);
            Assert.InRange(
                observedFixtureSize + extensionSize,
                0,
                BuildfileRomFixture.MaxRomSize);
        }

        [Theory]
        [InlineData(
            BuildfileRomFixture.MaxRomSize - BuildfileRomFixture.MinimumExtensionSize,
            BuildfileRomFixture.MinimumExtensionSize)]
        [InlineData(
            BuildfileRomFixture.MaxRomSize - BuildfileRomFixture.MinimumExtensionSize + 1,
            0)]
        [InlineData(BuildfileRomFixture.MaxRomSize, 0)]
        public void GetSparseExtensionSize_CapBoundaries_AreExplicit(
            int cleanSize,
            int expectedExtensionSize)
        {
            Assert.Equal(
                expectedExtensionSize,
                BuildfileRomFixture.GetSparseExtensionSize(cleanSize));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(BuildfileRomFixture.MaxRomSize + 1L)]
        public void GetSparseExtensionSize_OutOfContract_Throws(long cleanSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BuildfileRomFixture.GetSparseExtensionSize(cleanSize));
        }

        [Fact]
        public void CreateSparseExtendedCopy_NearCap_PreservesCleanAndFillsTail()
        {
            var clean = new byte[
                BuildfileRomFixture.MaxRomSize - BuildfileRomFixture.MinimumExtensionSize];
            clean[0] = 0x12;
            clean[clean.Length - 1] = 0x34;

            byte[] extended = BuildfileRomFixture.CreateSparseExtendedCopy(clean);

            Assert.Equal(BuildfileRomFixture.MaxRomSize, extended.Length);
            Assert.True(extended.AsSpan(0, clean.Length).SequenceEqual(clean));
            Assert.All(
                extended.AsSpan(clean.Length).ToArray(),
                value => Assert.Equal((byte)0xFF, value));
        }

        [Fact]
        public void CreateSparseExtendedCopy_InsufficientHeadroom_Throws()
        {
            var clean = new byte[
                BuildfileRomFixture.MaxRomSize - BuildfileRomFixture.MinimumExtensionSize + 1];

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => BuildfileRomFixture.CreateSparseExtendedCopy(clean));

            Assert.Contains("distinct extension markers", exception.Message);
        }

        [Fact]
        public void CreateModdedCopy_WritesDisjointEditsAndDistinctTailMarkers()
        {
            var clean = new byte[0x200001];
            clean[1] = 0x5A;

            byte[] modded = BuildfileRomFixture.CreateModdedCopy(clean);

            Assert.Equal(clean.Length + BuildfileRomFixture.PreferredExtensionSize, modded.Length);
            Assert.Equal((byte)(clean[1] ^ 0xFF), modded[1]);
            Assert.Equal((byte)0xA1, modded[0x100000]);
            Assert.Equal((byte)0xA2, modded[0x100001]);
            Assert.Equal((byte)0xB7, modded[0x200000]);
            Assert.Equal((byte)0xFF, modded[clean.Length]);
            Assert.Equal((byte)0x01, modded[clean.Length + 0x10]);
            Assert.Equal((byte)0x02, modded[clean.Length + 0x11]);
            Assert.Equal((byte)0xFF, modded[clean.Length + 0x12]);
            Assert.Equal((byte)0x03, modded[modded.Length - 1]);
        }
    }
}
