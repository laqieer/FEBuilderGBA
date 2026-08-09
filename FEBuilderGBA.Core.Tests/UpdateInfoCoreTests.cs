using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class UpdateInfoCoreTests
    {
        [Theory]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData("  ver_20260704.04  ", 20260704.04)]
        [InlineData("20260704.04", 20260704.04)]
        public void ParseVersion_ToleratesNullEmptyAndVerPrefix(string? version, double expected)
        {
            Assert.Equal(expected, UpdateInfo.ParseVersion(version), 3);
        }

        [Theory]
        [InlineData(null, null, 0)]
        [InlineData(null, "20260226.00", -1)]
        [InlineData("20260226.00", null, 1)]
        [InlineData("ver_20260704.04", "20260704.04", 0)]
        public void CompareVersions_UsesParseVersionForNullableInputs(string? v1, string? v2, int expected)
        {
            int result = UpdateInfo.CompareVersions(v1, v2);

            if (expected < 0)
                Assert.True(result < 0, $"Expected {v1 ?? "<null>"} < {v2 ?? "<null>"}");
            else if (expected > 0)
                Assert.True(result > 0, $"Expected {v1 ?? "<null>"} > {v2 ?? "<null>"}");
            else
                Assert.Equal(0, result);
        }

        [Fact]
        public void DetermineUpdateType_NullRemoteVersion_DoesNotThrowAndReturnsNone()
        {
            var updateInfo = new UpdateInfo();

            Assert.Equal(UpdateInfo.PackageType.None, updateInfo.DetermineUpdateType(null));
        }
    }
}
