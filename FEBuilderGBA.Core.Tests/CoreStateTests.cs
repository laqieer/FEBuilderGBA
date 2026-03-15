using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
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

        [Fact]
        public void RaiseLanguageChanged_InvokesSubscribers()
        {
            int callCount = 0;
            void handler() => callCount++;
            CoreState.LanguageChanged += handler;
            try
            {
                CoreState.RaiseLanguageChanged();
                Assert.Equal(1, callCount);
                CoreState.RaiseLanguageChanged();
                Assert.Equal(2, callCount);
            }
            finally
            {
                CoreState.LanguageChanged -= handler;
            }
        }

        [Fact]
        public void RaiseLanguageChanged_NoSubscribers_DoesNotThrow()
        {
            // Should not throw even with no subscribers
            CoreState.RaiseLanguageChanged();
        }
    }
}
