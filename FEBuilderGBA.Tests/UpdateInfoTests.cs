using Xunit;
using System;
using System.IO;
using System.Reflection;

namespace FEBuilderGBA.Tests
{
    public class UpdateInfoTests
    {
        private static void SetVersionCore(UpdateInfo updateInfo, string version)
        {
            PropertyInfo? versionCoreProperty = typeof(UpdateInfo).GetProperty(nameof(UpdateInfo.VERSION_CORE));
            Assert.True(versionCoreProperty is not null, $"{nameof(UpdateInfo.VERSION_CORE)} property should exist.");
            if (versionCoreProperty is null)
                throw new InvalidOperationException($"{nameof(UpdateInfo.VERSION_CORE)} property should exist.");

            Assert.True(typeof(string).IsAssignableFrom(versionCoreProperty.PropertyType), $"{nameof(UpdateInfo.VERSION_CORE)} should accept string values.");
            versionCoreProperty.SetValue(updateInfo, version);
        }

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
        [InlineData(null, null, 0)]
        [InlineData(null, "20260226.00", -1)]
        [InlineData("20260226.00", null, 1)]
        public void CompareVersions_NullInputs_TreatedAsZero(string? v1, string? v2, int expected)
        {
            int result = UpdateInfo.CompareVersions(v1, v2);

            if (expected < 0)
                Assert.True(result < 0, $"Expected {v1 ?? "<null>"} < {v2 ?? "<null>"}");
            else if (expected > 0)
                Assert.True(result > 0, $"Expected {v1 ?? "<null>"} > {v2 ?? "<null>"}");
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
        public void IsValidVersion_ValidatesFormat(string? version, bool expected)
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
            SetVersionCore(updateInfo, localCore);

            // Act
            var result = updateInfo.DetermineUpdateType(remoteCore);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void DetermineUpdateType_NullRemoteVersion_DoesNotThrowAndReturnsNone()
        {
            var updateInfo = new UpdateInfo();
            SetVersionCore(updateInfo, "20260226.00");

            Assert.Equal(UpdateInfo.PackageType.None, updateInfo.DetermineUpdateType(null));
        }

        [Fact]
        public void UpdateInfo_WinFormsSourceCopy_MatchesCoreSource()
        {
            string root = FindRepoRoot();
            string core = File.ReadAllText(Path.Combine(root, "FEBuilderGBA.Core", "UpdateInfo.cs"));
            string winForms = File.ReadAllText(Path.Combine(root, "FEBuilderGBA", "UpdateInfo.cs"));

            Assert.Equal(core, winForms);
        }

        [Fact]
        public void GetDownloadUrl_ReturnsCorrectUrl()
        {
            // Arrange
            var updateInfo = new UpdateInfo
            {
                URL_CORE = "http://example.com/core.7z",
            };

            // Act & Assert
            Assert.Equal("http://example.com/core.7z", updateInfo.GetDownloadUrl(UpdateInfo.PackageType.CoreOnly));
            Assert.Equal("",                            updateInfo.GetDownloadUrl(UpdateInfo.PackageType.None));
            Assert.Equal("",                            updateInfo.GetDownloadUrl(UpdateInfo.PackageType.Unknown));
        }

        [Fact]
        public void HasUrl_ReturnsTrueForValidUrls()
        {
            // Arrange
            var updateInfo = new UpdateInfo
            {
                URL_CORE = "http://example.com/core.7z",
            };

            // Act & Assert
            Assert.True(updateInfo.HasUrl(UpdateInfo.PackageType.CoreOnly));
            Assert.False(updateInfo.HasUrl(UpdateInfo.PackageType.None));
        }

        [Fact]
        public void GetVersionDisplay_ShowsCore()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            SetVersionCore(updateInfo, "20260226.00");

            // Act
            string display = updateInfo.GetVersionDisplay();

            // Assert
            Assert.Contains("20260226.00", display);
            Assert.Contains("Core:", display);
        }

        // ---- #1805 / #1803: ver_ release-tag prefix handling ----
        // getAppVersion() returns the stamped release tag "ver_YYYYMMDD.NN"; the raw
        // U.atof would parse a leading 'v' as 0 and make every remote look newer.

        [Theory]
        [InlineData("ver_20260704.04", "20260704.04", 0)]   // local tag == remote numeric → up to date (no false positive)
        [InlineData("ver_20260704.04", "20260704.05", -1)]  // genuinely older
        [InlineData("ver_20260704.05", "20260704.04", 1)]   // genuinely newer
        [InlineData("ver_20260704.04", "ver_20260704.04", 0)]
        [InlineData("20260704.04", "ver_20260704.05", -1)]
        public void CompareVersions_TolerantOfVerPrefix(string v1, string v2, int expected)
        {
            int result = UpdateInfo.CompareVersions(v1, v2);
            if (expected < 0) Assert.True(result < 0, $"Expected {v1} < {v2}");
            else if (expected > 0) Assert.True(result > 0, $"Expected {v1} > {v2}");
            else Assert.Equal(0, result);
        }

        [Theory]
        [InlineData("ver_20260704.04", 20260704.04)]
        [InlineData("20260704.04", 20260704.04)]
        [InlineData("  ver_20260704.04  ", 20260704.04)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        public void ParseVersion_StripsVerPrefix(string? version, double expected)
        {
            Assert.Equal(expected, UpdateInfo.ParseVersion(version), 3);
        }

        [Theory]
        [InlineData("ver_20260226.00", true)]
        [InlineData("20260226.00", true)]
        [InlineData("ver_20260226.0", false)]   // missing hour digit even with prefix
        [InlineData("ver_2026.00", false)]       // wrong width
        public void IsValidVersion_AcceptsOptionalVerPrefix(string? version, bool expected)
        {
            Assert.Equal(expected, UpdateInfo.IsValidVersion(version));
        }

        [Fact]
        public void DetermineUpdateType_VerPrefixedLocal_NotFalsePositive()
        {
            // #1805: the installed WinForms build is stamped with the release tag; the
            // latest GitHub release exposes the numeric version. Must NOT claim an update.
            var updateInfo = new UpdateInfo();
            SetVersionCore(updateInfo, "ver_20260704.04");
            Assert.Equal(UpdateInfo.PackageType.None, updateInfo.DetermineUpdateType("20260704.04"));
            // A genuinely newer remote is still detected.
            Assert.Equal(UpdateInfo.PackageType.CoreOnly, updateInfo.DetermineUpdateType("20260704.05"));
        }

        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;

                string? parent = Directory.GetParent(dir)?.FullName;
                if (parent == dir)
                    break;
                dir = parent ?? "";
            }

            return Directory.GetCurrentDirectory();
        }
    }
}
