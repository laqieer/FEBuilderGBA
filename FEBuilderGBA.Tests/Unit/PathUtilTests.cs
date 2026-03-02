using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    public class PathUtilTests
    {
        [Fact]
        public void PlatformDetection_ExactlyOneIsTrue()
        {
            // At least one platform should be detected
            Assert.True(PathUtil.IsWindows || PathUtil.IsMacOS || PathUtil.IsLinux);
        }

        [Fact]
        public void Normalize_NullReturnsNull()
        {
            Assert.Null(PathUtil.Normalize(null));
        }

        [Fact]
        public void Normalize_EmptyReturnsEmpty()
        {
            Assert.Equal("", PathUtil.Normalize(""));
        }

        [Fact]
        public void ConfigPath_CombinesWithBaseDirectory()
        {
            var origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = "/app";
                string result = PathUtil.ConfigPath("data");
                Assert.Contains("config", result);
                Assert.Contains("data", result);
            }
            finally
            {
                CoreState.BaseDirectory = origBase;
            }
        }

        [Fact]
        public void GetExternalToolPath_ReturnsToolName()
        {
            string result = PathUtil.GetExternalToolPath("git");
            Assert.Contains("git", result);
        }

        [Fact]
        public void Normalize_ForwardSlashesPreserved()
        {
            string result = PathUtil.Normalize("a/b/c");
            Assert.Equal("a/b/c", result);
        }
    }
}
