using Xunit;
using System;

namespace FEBuilderGBA.Tests
{
    public class UpdateCheckSplitPackageTests
    {
        [Theory]
        [InlineData("https://example.com/FEBuilderGBA_20260226.15.7z",  "20260226.15")]
        [InlineData("https://example.com/FEBuilderGBA_20260226.15.zip", "20260226.15")]
        [InlineData("https://example.com/FEBuilderGBA_ver_20260704.04.7z",  "20260704.04")]
        [InlineData("https://example.com/FEBuilderGBA_ver_20260704.04.zip", "20260704.04")]
        [InlineData("",                                                   "00000000.00")]
        [InlineData(null,                                                 "00000000.00")]
        [InlineData("https://example.com/invalid.7z",                    "00000000.00")]
        public void ExtractVersionFromUrl_ParsesCorrectly(string url, string expected)
        {
            string result = UpdateCheckSplitPackage.ExtractVersionFromUrl(url);
            Assert.Equal(expected, result);
        }

        // #1803: a real release lists FEBuilderGBA-android-apk.zip (and the avalonia/cli
        // bundles) BEFORE the WinForms desktop package. The legacy updater grabbed the
        // FIRST browser_download_url (the APK). SelectCoreAssetUrl must pick the desktop
        // FEBuilderGBA_ asset (underscore separator), never a platform bundle (hyphen).
        const string ReleaseJson = @"{
  ""assets"": [
    { ""browser_download_url"": ""https://github.com/laqieer/FEBuilderGBA/releases/download/ver_20260704.04/FEBuilderGBA-android-apk.zip"" },
    { ""browser_download_url"": ""https://github.com/laqieer/FEBuilderGBA/releases/download/ver_20260704.04/FEBuilderGBA-avalonia-win-x64.zip"" },
    { ""browser_download_url"": ""https://github.com/laqieer/FEBuilderGBA/releases/download/ver_20260704.04/FEBuilderGBA-cli-win-x64.zip"" },
    { ""browser_download_url"": ""https://github.com/laqieer/FEBuilderGBA/releases/download/ver_20260704.04/FEBuilderGBA_ver_20260704.04.zip"" }
  ]
}";

        [Fact]
        public void SelectCoreAssetUrl_PicksDesktopAsset_NotAndroidApk()
        {
            string url = UpdateCheckSplitPackage.SelectCoreAssetUrl(ReleaseJson);
            Assert.EndsWith("FEBuilderGBA_ver_20260704.04.zip", url);
            Assert.DoesNotContain("android", url);
            Assert.DoesNotContain("avalonia", url);
            Assert.DoesNotContain("-cli-", url);
        }

        [Fact]
        public void SelectCoreAssetUrl_NumericAsset_Matches()
        {
            string json = @"{ ""assets"": [ { ""browser_download_url"": ""https://x/FEBuilderGBA_20260226.00.7z"" } ] }";
            Assert.EndsWith("FEBuilderGBA_20260226.00.7z", UpdateCheckSplitPackage.SelectCoreAssetUrl(json));
        }

        [Fact]
        public void SelectCoreAssetUrl_OnlyPlatformBundles_ReturnsEmpty()
        {
            string json = @"{ ""assets"": [ { ""browser_download_url"": ""https://x/FEBuilderGBA-android-apk.zip"" } ] }";
            Assert.Equal("", UpdateCheckSplitPackage.SelectCoreAssetUrl(json));
        }

        [Fact]
        public void GetDownloadUrl_WithCoreUrl_ReturnsCoreWhenCoreNeedsUpdate()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260225.00");

            updateInfo.URL_CORE = "https://example.com/FEBuilderGBA_20260226.00.7z";

            // Act
            UpdateInfo.PackageType packageType;
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out packageType);

            // Assert
            Assert.Equal("https://example.com/FEBuilderGBA_20260226.00.7z", url);
            Assert.Equal(UpdateInfo.PackageType.CoreOnly, packageType);
        }
    }
}
