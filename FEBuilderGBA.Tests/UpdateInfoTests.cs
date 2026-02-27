using Xunit;
using System;

namespace FEBuilderGBA.Tests
{
    public class UpdateInfoTests
    {
        [Fact]
        public void Constructor_InitializesVersionCore()
        {
            // Arrange & Act
            var updateInfo = new UpdateInfo();

            // Assert
            Assert.NotNull(updateInfo.VERSION_CORE);
            Assert.True(UpdateInfo.IsValidVersion(updateInfo.VERSION_CORE) || updateInfo.VERSION_CORE == "00000000.00");
        }

        [Theory]
        [InlineData("20260226.00", "20260227.00", -1)]
        [InlineData("20260227.00", "20260226.00", 1)]
        [InlineData("20260226.00", "20260226.00", 0)]
        [InlineData("20260226.15", "20260226.16", -1)]
        [InlineData("20250101.00", "20260101.00", -1)]
        public void CompareVersions_ReturnsCorrectComparison(string v1, string v2, int expected)
        {
            // Act
            int result = UpdateInfo.CompareVersions(v1, v2);

            // Assert
            if (expected < 0)
                Assert.True(result < 0, $"Expected {v1} < {v2}");
            else if (expected > 0)
                Assert.True(result > 0, $"Expected {v1} > {v2}");
            else
                Assert.Equal(0, result);
        }

        [Theory]
        [InlineData("20260226.00", true)]
        [InlineData("20250101.23", true)]
        [InlineData("00000000.00", true)]
        [InlineData("2026022.00", false)]   // Too short
        [InlineData("202602266.00", false)]  // Too long
        [InlineData("20260226.0", false)]    // Missing hour digit
        [InlineData("20260226", false)]      // Missing .HH
        [InlineData("abcd1234.00", false)]   // Invalid characters
        [InlineData("", false)]              // Empty
        [InlineData(null, false)]            // Null
        public void IsValidVersion_ValidatesFormat(string version, bool expected)
        {
            // Act
            bool result = UpdateInfo.IsValidVersion(version);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("20260226.00", "20260227.00", UpdateInfo.PackageType.CoreOnly)]
        [InlineData("20260227.00", "20260226.00", UpdateInfo.PackageType.None)]
        [InlineData("20260227.00", "20260227.00", UpdateInfo.PackageType.None)]
        public void DetermineUpdateType_ReturnsCorrectType(
            string localCore, string remoteCore, UpdateInfo.PackageType expected)
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, localCore);

            // Act
            var result = updateInfo.DetermineUpdateType(remoteCore);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetDownloadUrl_ReturnsCorrectUrl()
        {
            // Arrange
            var updateInfo = new UpdateInfo
            {
                URL_FULL = "http://example.com/full.7z",
                URL_CORE = "http://example.com/core.7z",
            };

            // Act & Assert
            Assert.Equal("http://example.com/full.7z",  updateInfo.GetDownloadUrl(UpdateInfo.PackageType.Full));
            Assert.Equal("http://example.com/core.7z",  updateInfo.GetDownloadUrl(UpdateInfo.PackageType.CoreOnly));
            Assert.Equal("",                             updateInfo.GetDownloadUrl(UpdateInfo.PackageType.None));
            Assert.Equal("",                             updateInfo.GetDownloadUrl(UpdateInfo.PackageType.Unknown));
        }

        [Fact]
        public void HasUrl_ReturnsTrueForValidUrls()
        {
            // Arrange
            var updateInfo = new UpdateInfo
            {
                URL_FULL = "http://example.com/full.7z",
                URL_CORE = "",
            };

            // Act & Assert
            Assert.True(updateInfo.HasUrl(UpdateInfo.PackageType.Full));
            Assert.False(updateInfo.HasUrl(UpdateInfo.PackageType.CoreOnly));
        }

        [Fact]
        public void GetVersionDisplay_ShowsCore()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260226.00");

            // Act
            string display = updateInfo.GetVersionDisplay();

            // Assert
            Assert.Contains("20260226.00", display);
            Assert.Contains("Core:", display);
        }
    }
}
