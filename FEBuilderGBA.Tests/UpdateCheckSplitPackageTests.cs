using Xunit;
using System;

namespace FEBuilderGBA.Tests
{
    public class UpdateCheckSplitPackageTests
    {
        [Theory]
        [InlineData("https://example.com/FEBuilderGBA_FULL_20260226.00_20260225.12.7z", 0, "20260226.00")]
        [InlineData("https://example.com/FEBuilderGBA_FULL_20260226.00_20260225.12.7z", 1, "20260225.12")]
        [InlineData("https://example.com/FEBuilderGBA_CORE_20260226.15.7z", 0, "20260226.15")]
        [InlineData("https://example.com/FEBuilderGBA_CORE_20260226.15.7z", 1, "20260226.15")]
        [InlineData("https://example.com/FEBuilderGBA_PATCH2_20260227.00.7z", 0, "20260227.00")]
        [InlineData("https://example.com/FEBuilderGBA_PATCH2_20260227.00.7z", 1, "20260227.00")]
        [InlineData("https://example.com/FEBuilderGBA_20260226.00.7z", 0, "20260226.00")]
        [InlineData("https://example.com/FEBuilderGBA_20260226.00.7z", 1, "20260226.00")]
        [InlineData("", 0, "00000000.00")]
        [InlineData(null, 0, "00000000.00")]
        [InlineData("https://example.com/invalid.7z", 0, "00000000.00")]
        public void ExtractVersionFromUrl_ParsesCorrectly(string url, int versionIndex, string expected)
        {
            // Use reflection to access private method
            var method = typeof(UpdateCheckSplitPackage).GetMethod("ExtractVersionFromUrl",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(method);

            // Act
            string result = (string)method.Invoke(null, new object[] { url, versionIndex });

            // Assert
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
                URL_PATCH2 = ""
            };

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.Full, packageType);
        }

        [Fact]
        public void GetDownloadUrl_WithSplitPackages_PrefersCoreWhenOnlyCoreNeedsUpdate()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            // Set current versions via reflection
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260225.00");
            typeof(UpdateInfo).GetProperty("VERSION_PATCH2").SetValue(updateInfo, "20260226.00");

            updateInfo.URL_FULL = "https://example.com/FEBuilderGBA_FULL_20260226.00_20260226.00.7z";
            updateInfo.URL_CORE = "https://example.com/FEBuilderGBA_CORE_20260226.00.7z";
            updateInfo.URL_PATCH2 = "https://example.com/FEBuilderGBA_PATCH2_20260226.00.7z";

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_CORE_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.CoreOnly, packageType);
        }

        [Fact]
        public void GetDownloadUrl_WithSplitPackages_PrefersPatch2WhenOnlyPatch2NeedsUpdate()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            // Set current versions via reflection
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260226.00");
            typeof(UpdateInfo).GetProperty("VERSION_PATCH2").SetValue(updateInfo, "20260225.00");

            updateInfo.URL_FULL = "https://example.com/FEBuilderGBA_FULL_20260226.00_20260226.00.7z";
            updateInfo.URL_CORE = "https://example.com/FEBuilderGBA_CORE_20260226.00.7z";
            updateInfo.URL_PATCH2 = "https://example.com/FEBuilderGBA_PATCH2_20260226.00.7z";

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_PATCH2_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.Patch2Only, packageType);
        }

        [Fact]
        public void GetDownloadUrl_WithSplitPackages_PrefersFullWhenBothNeedUpdate()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            // Set current versions via reflection
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260225.00");
            typeof(UpdateInfo).GetProperty("VERSION_PATCH2").SetValue(updateInfo, "20260225.00");

            updateInfo.URL_FULL = "https://example.com/FEBuilderGBA_FULL_20260226.00_20260226.00.7z";
            updateInfo.URL_CORE = "https://example.com/FEBuilderGBA_CORE_20260226.00.7z";
            updateInfo.URL_PATCH2 = "https://example.com/FEBuilderGBA_PATCH2_20260226.00.7z";

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_FULL_20260226.00_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.Full, packageType);
        }

        [Fact]
        public void GetDownloadUrl_WithMissingCorePackage_FallsBackToFull()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            // Set current versions via reflection
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260225.00");
            typeof(UpdateInfo).GetProperty("VERSION_PATCH2").SetValue(updateInfo, "20260226.00");

            updateInfo.URL_FULL = "https://example.com/FEBuilderGBA_FULL_20260226.00_20260226.00.7z";
            updateInfo.URL_CORE = ""; // Missing CORE package
            updateInfo.URL_PATCH2 = "https://example.com/FEBuilderGBA_PATCH2_20260226.00.7z";

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_FULL_20260226.00_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.Full, packageType);
        }

        [Fact]
        public void GetDownloadUrl_WithMissingPatch2Package_FallsBackToFull()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            // Set current versions via reflection
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260226.00");
            typeof(UpdateInfo).GetProperty("VERSION_PATCH2").SetValue(updateInfo, "20260225.00");

            updateInfo.URL_FULL = "https://example.com/FEBuilderGBA_FULL_20260226.00_20260226.00.7z";
            updateInfo.URL_CORE = "https://example.com/FEBuilderGBA_CORE_20260226.00.7z";
            updateInfo.URL_PATCH2 = ""; // Missing PATCH2 package

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_FULL_20260226.00_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.Full, packageType);
        }
    }
}
