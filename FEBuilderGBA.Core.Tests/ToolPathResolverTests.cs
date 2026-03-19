using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class ToolPathResolverTests
    {
        [Fact]
        public void IsColorzCore_DetectsCorrectly()
        {
            // Use Path.Combine for cross-platform paths
            Assert.True(ToolPathResolver.IsColorzCore(
                System.IO.Path.Combine("tools", "ColorzCore.exe")));
            Assert.True(ToolPathResolver.IsColorzCore("/usr/bin/ColorzCore.exe"));
            Assert.False(ToolPathResolver.IsColorzCore(
                System.IO.Path.Combine("tools", "Core.exe")));
            Assert.False(ToolPathResolver.IsColorzCore(null));
            Assert.False(ToolPathResolver.IsColorzCore(""));
            // Just the filename itself
            Assert.True(ToolPathResolver.IsColorzCore("ColorzCore.exe"));
            Assert.False(ToolPathResolver.IsColorzCore("Core.exe"));
        }

        [Fact]
        public void ResolveEventAssembler_ReturnsNull_WhenNothingConfigured()
        {
            // With no config and no bundled tools, should return null
            var savedConfig = CoreState.Config;
            CoreState.Config = null;
            try
            {
                string result = ToolPathResolver.ResolveEventAssembler();
                Assert.Null(result);
            }
            finally
            {
                CoreState.Config = savedConfig;
            }
        }
    }
}
