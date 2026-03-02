using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class CoreStateTests
    {
        [Fact]
        public void BaseDirectory_CanSetAndGet()
        {
            var orig = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = "/test/path";
                Assert.Equal("/test/path", CoreState.BaseDirectory);
            }
            finally
            {
                CoreState.BaseDirectory = orig;
            }
        }

        [Fact]
        public void Services_DefaultIsHeadless()
        {
            Assert.NotNull(CoreState.Services);
            Assert.IsType<HeadlessAppServices>(CoreState.Services);
        }

        [Fact]
        public void Language_DefaultIsEnglish()
        {
            Assert.Equal("en", CoreState.Language);
        }
    }
}
