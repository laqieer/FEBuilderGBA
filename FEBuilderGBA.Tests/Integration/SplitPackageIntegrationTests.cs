using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Tests.Integration
{
    /// <summary>
    /// Integration tests for the core update system.
    /// Patch2 is versioned and updated via git, not via build artifacts.
    /// </summary>
    public class SplitPackageIntegrationTests
    {
        private readonly string _testBaseDir;

        public SplitPackageIntegrationTests()
        {
            _testBaseDir = Path.Combine(Path.GetTempPath(), "FEBuilderGBA_IntegrationTests_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testBaseDir);
        }

        ~SplitPackageIntegrationTests()
        {
            try
            {
                if (Directory.Exists(_testBaseDir))
                    Directory.Delete(_testBaseDir, true);
            }
            catch { }
        }

        [Fact(Skip = "Requires static Program.BaseDirectory manipulation which is difficult to isolate in tests")]
        public void UpdateInfo_ReadsVersionFromFileSystem()
        {
            // Skipped — version.txt no longer used; patch2 is git-managed.
        }

        [Fact(Skip = "Requires static Program.BaseDirectory manipulation which is difficult to isolate in tests")]
        public void UpdateInfo_HandlesMinimalVersion_WhenFileContainsOnlyVersion()
        {
            // Skipped — version.txt no longer used; patch2 is git-managed.
        }

        [Theory]
        [InlineData("20260226.00", "20260227.00", UpdateInfo.PackageType.CoreOnly)]
        [InlineData("20260227.00", "20260227.00", UpdateInfo.PackageType.None)]
        [InlineData("20260227.00", "20260226.00", UpdateInfo.PackageType.None)]
        public void UpdateInfo_DeterminesCorrectPackageType_BasedOnCoreVersion(
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
        public void UpdateCheckSplitPackage_ExtractsVersionFromPackageUrl()
        {
            // Arrange
            string url = "https://github.com/laqieer/FEBuilderGBA/releases/download/20260226.00/FEBuilderGBA_20260226.00.7z";

            // Act
            string version = UpdateCheckSplitPackage.ExtractVersionFromUrl(url);

            // Assert
            Assert.Equal("20260226.00", version);
        }

        [Fact]
        public void UpdateCheckSplitPackage_SelectsCorePackage_WhenCoreNeedsUpdate()
        {
            // Arrange
            var updateInfo = new UpdateInfo();
            typeof(UpdateInfo).GetProperty("VERSION_CORE").SetValue(updateInfo, "20260226.00");

            updateInfo.URL_CORE = "https://example.com/FEBuilderGBA_20260227.00.7z";

            // Act
            string url = UpdateCheckSplitPackage.GetDownloadUrl(updateInfo, out var packageType);

            // Assert
            Assert.Equal(UpdateInfo.PackageType.CoreOnly, packageType);
            Assert.Equal(updateInfo.URL_CORE, url);
        }

        [Fact]
        public void U_DirectoryCopy_CopiesAllFiles()
        {
            // Arrange
            string sourceDir = Path.Combine(_testBaseDir, "source");
            string destDir   = Path.Combine(_testBaseDir, "dest");
            Directory.CreateDirectory(sourceDir);

            File.WriteAllText(Path.Combine(sourceDir, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(sourceDir, "file2.txt"), "content2");

            // Act
            U.DirectoryCopy(sourceDir, destDir, false);

            // Assert
            Assert.True(File.Exists(Path.Combine(destDir, "file1.txt")));
            Assert.True(File.Exists(Path.Combine(destDir, "file2.txt")));
            Assert.Equal("content1", File.ReadAllText(Path.Combine(destDir, "file1.txt")));
            Assert.Equal("content2", File.ReadAllText(Path.Combine(destDir, "file2.txt")));
        }

        [Fact]
        public void U_DirectoryCopy_CopiesSubdirectoriesRecursively()
        {
            // Arrange
            string sourceDir = Path.Combine(_testBaseDir, "source");
            string destDir   = Path.Combine(_testBaseDir, "dest");
            string subDir    = Path.Combine(sourceDir, "subdir");
            Directory.CreateDirectory(subDir);

            File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "root");
            File.WriteAllText(Path.Combine(subDir, "sub.txt"), "sub");

            // Act
            U.DirectoryCopy(sourceDir, destDir, true);

            // Assert
            Assert.True(File.Exists(Path.Combine(destDir, "root.txt")));
            Assert.True(File.Exists(Path.Combine(destDir, "subdir", "sub.txt")));
            Assert.Equal("sub", File.ReadAllText(Path.Combine(destDir, "subdir", "sub.txt")));
        }

        [Fact]
        public void U_DirectoryCopy_OverwritesExistingFiles()
        {
            // Arrange
            string sourceDir = Path.Combine(_testBaseDir, "source");
            string destDir   = Path.Combine(_testBaseDir, "dest");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);

            File.WriteAllText(Path.Combine(sourceDir, "file.txt"), "new content");
            File.WriteAllText(Path.Combine(destDir,   "file.txt"), "old content");

            // Act
            U.DirectoryCopy(sourceDir, destDir, false);

            // Assert
            Assert.Equal("new content", File.ReadAllText(Path.Combine(destDir, "file.txt")));
        }

        [Fact]
        public void U_DirectoryCopy_ThrowsException_WhenSourceDoesNotExist()
        {
            // Arrange
            string sourceDir = Path.Combine(_testBaseDir, "nonexistent");
            string destDir   = Path.Combine(_testBaseDir, "dest");

            // Act & Assert
            Assert.Throws<DirectoryNotFoundException>(() => U.DirectoryCopy(sourceDir, destDir, false));
        }
    }
}
