using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the window title computation logic used by MainWindowViewModel.
    /// Reproduces the computation locally to avoid Avalonia dependency.
    /// </summary>
    public class WindowTitleTests
    {
        /// <summary>Compute the window title the same way MainWindowViewModel does.</summary>
        static string ComputeWindowTitle(string? romFilename, bool hasUnsavedChanges)
        {
            if (string.IsNullOrEmpty(romFilename))
                return "FEBuilderGBA";
            return hasUnsavedChanges
                ? $"FEBuilderGBA - {romFilename} *"
                : $"FEBuilderGBA - {romFilename}";
        }

        [Fact]
        public void NoRomLoaded_ReturnsDefaultTitle()
        {
            Assert.Equal("FEBuilderGBA", ComputeWindowTitle(null, false));
            Assert.Equal("FEBuilderGBA", ComputeWindowTitle("", false));
            Assert.Equal("FEBuilderGBA", ComputeWindowTitle(null, true));
        }

        [Fact]
        public void RomLoaded_Clean_ShowsFilename()
        {
            Assert.Equal("FEBuilderGBA - test.gba", ComputeWindowTitle("test.gba", false));
        }

        [Fact]
        public void RomLoaded_Dirty_ShowsFilenameWithAsterisk()
        {
            Assert.Equal("FEBuilderGBA - test.gba *", ComputeWindowTitle("test.gba", true));
        }

        [Fact]
        public void DifferentFilenames_ShowCorrectly()
        {
            Assert.Equal("FEBuilderGBA - fe8u.gba", ComputeWindowTitle("fe8u.gba", false));
            Assert.Equal("FEBuilderGBA - fe8u.gba *", ComputeWindowTitle("fe8u.gba", true));
            Assert.Equal("FEBuilderGBA - MyHack.gba", ComputeWindowTitle("MyHack.gba", false));
        }
    }
}
