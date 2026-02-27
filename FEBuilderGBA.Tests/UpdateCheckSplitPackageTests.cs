using Xunit;
using System;

namespace FEBuilderGBA.Tests
{
    public class UpdateCheckSplitPackageTests
    {
        [Theory]
        [InlineData("https://example.com/FEBuilderGBA_CORE_20260226.15.7z",  "20260226.15")]
        [InlineData("https://example.com/FEBuilderGBA_CORE_20260226.15.zip", "20260226.15")]
        [InlineData("https://example.com/FEBuilderGBA_20260226.00.7z",        "20260226.00")]
        [InlineData("https://example.com/FEBuilderGBA_20260226.00.zip",       "20260226.00")]
        [InlineData("",                                                         "00000000.00")]
        [InlineData(null,                                                       "00000000.00")]
        [InlineData("https://example.com/invalid.7z",                          "00000000.00")]
        public void ExtractVersionFromUrl_ParsesCorrectly(string url, string expected)
        {
            string result = UpdateCheckSplitPackage.ExtractVersionFromUrl(url);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetDownloadUrl_WithOnlyFullUrl_ReturnsFull()
        {
            // Arrange
            var updateInfo = new UpdateInfo
            {
                URL_FULL = "https://example.com/FEBuilderGBA_20260226.00.7z",
                URL_CORE = "",
            };

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.Full, packageType);
        }

        [Fact]
        public void GetDownloadUrl_WithCoreUrl_ReturnsCoreWhenCoreNeedsUpdate()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260225.00");

            updateInfo.URL_CORE = "https://example.com/FEBuilderGBA_CORE_20260226.00.7z";
            updateInfo.URL_FULL = "";

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_CORE_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.CoreOnly, packageType);
        }

        [Fact]
        public void GetDownloadUrl_FallsBackToFullWhenCoreUrlMissing()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260225.00");

            updateInfo.URL_FULL = "https://example.com/FEBuilderGBA_20260226.00.7z";
            updateInfo.URL_CORE = "";

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.Full, packageType);
        }
    }
}
