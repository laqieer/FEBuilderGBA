using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ToolPathResolverTests
    {
        [Fact]
        public void IsColorzCore_DetectsCorrectly()
        {
            // Windows-style .exe
            Assert.True(ToolPathResolver.IsColorzCore(Path.Combine("tools", "ColorzCore.exe")));
            Assert.True(ToolPathResolver.IsColorzCore("/usr/bin/ColorzCore.exe"));
            Assert.True(ToolPathResolver.IsColorzCore("ColorzCore.exe"));
            // Linux/macOS no-extension
            Assert.True(ToolPathResolver.IsColorzCore(Path.Combine("tools", "bin", "ColorzCore")));
            Assert.True(ToolPathResolver.IsColorzCore("/usr/bin/ColorzCore"));
            Assert.True(ToolPathResolver.IsColorzCore("ColorzCore"));
            // Non-ColorzCore
            Assert.False(ToolPathResolver.IsColorzCore(Path.Combine("tools", "Core.exe")));
            Assert.False(ToolPathResolver.IsColorzCore("Core"));
            Assert.False(ToolPathResolver.IsColorzCore(null));
            Assert.False(ToolPathResolver.IsColorzCore(""));
        }

        [Fact]
        public void ResolveEventAssembler_ReturnsNull_WhenNothingConfigured()
        {
            var savedConfig = CoreState.Config;
            var savedBaseDir = CoreState.BaseDirectory;
            CoreState.Config = null;
            CoreState.BaseDirectory = Path.Combine(Path.GetTempPath(), "febuilder-test-empty-" + Path.GetRandomFileName());
            try
            {
                string result = ToolPathResolver.ResolveEventAssembler();
                Assert.Null(result);
            }
            finally
            {
                CoreState.Config = savedConfig;
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        [Fact]
        public void ResolveEventAssembler_FindsBundledColorzCore_BinCorePath()
        {
            // ColorzCore.csproj uses BaseOutputPath=bin/Core, so output is at bin/Core/Release/net6.0/
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string colorzCorePath = Path.Combine(tempDir, "tools", "ColorzCore", "ColorzCore",
                "bin", "Core", "Release", "net6.0", "ColorzCore.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(colorzCorePath));
            File.WriteAllText(colorzCorePath, "mock");
            // Create .git dir to make it look like a repo root
            Directory.CreateDirectory(Path.Combine(tempDir, ".git"));

            var savedConfig = CoreState.Config;
            var savedBaseDir = CoreState.BaseDirectory;
            CoreState.Config = null;
            CoreState.BaseDirectory = tempDir;
            try
            {
                string result = ToolPathResolver.ResolveEventAssembler();
                Assert.NotNull(result);
                Assert.Equal(colorzCorePath, result);
            }
            finally
            {
                CoreState.Config = savedConfig;
                CoreState.BaseDirectory = savedBaseDir;
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ResolveEventAssembler_FindsToolsBinFallback()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string fallbackPath = Path.Combine(tempDir, "tools", "bin", "ColorzCore.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath));
            File.WriteAllText(fallbackPath, "mock");
            Directory.CreateDirectory(Path.Combine(tempDir, ".git"));

            var savedConfig = CoreState.Config;
            var savedBaseDir = CoreState.BaseDirectory;
            CoreState.Config = null;
            CoreState.BaseDirectory = tempDir;
            try
            {
                string result = ToolPathResolver.ResolveEventAssembler();
                Assert.NotNull(result);
                Assert.Equal(fallbackPath, result);
            }
            finally
            {
                CoreState.Config = savedConfig;
                CoreState.BaseDirectory = savedBaseDir;
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ResolveLynExe_FindsLynInToolsDir()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string eaExe = Path.Combine(tempDir, "ea", "ColorzCore.exe");
            string lynPath = Path.Combine(tempDir, "ea", "Tools", "lyn.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(eaExe));
            Directory.CreateDirectory(Path.GetDirectoryName(lynPath));
            File.WriteAllText(eaExe, "mock");
            File.WriteAllText(lynPath, "mock");

            try
            {
                string result = ToolPathResolver.ResolveLynExe(eaExe);
                Assert.NotNull(result);
                Assert.Equal(lynPath, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ResolveLynExe_ReturnsNull_WhenNotFound()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string eaExe = Path.Combine(tempDir, "ea", "ColorzCore.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(eaExe));
            File.WriteAllText(eaExe, "mock");

            var savedBaseDir = CoreState.BaseDirectory;
            CoreState.BaseDirectory = tempDir;
            try
            {
                string result = ToolPathResolver.ResolveLynExe(eaExe);
                Assert.Null(result);
            }
            finally
            {
                CoreState.BaseDirectory = savedBaseDir;
                Directory.Delete(tempDir, true);
            }
        }

        // #1246: on Linux/macOS the lyn binary has no .exe extension; the resolver must
        // find the platform-appropriate name. With the old "lyn.exe"-only search this
        // would fail on the Linux/macOS CI runners (extensionless "lyn" never matched).
        [Fact]
        public void ResolveLynExe_FindsPlatformAwareLynName()
        {
            string lynName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "lyn.exe" : "lyn";
            string tempDir = Path.Combine(Path.GetTempPath(), "febuilder-test-" + Path.GetRandomFileName());
            string eaExe = Path.Combine(tempDir, "ea", "ColorzCore.exe");
            string lynPath = Path.Combine(tempDir, "ea", "Tools", lynName);
            Directory.CreateDirectory(Path.GetDirectoryName(eaExe));
            Directory.CreateDirectory(Path.GetDirectoryName(lynPath));
            File.WriteAllText(eaExe, "mock");
            File.WriteAllText(lynPath, "mock");

            try
            {
                string result = ToolPathResolver.ResolveLynExe(eaExe);
                Assert.NotNull(result);
                Assert.Equal(lynPath, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
