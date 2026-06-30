using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1722: Move Cost editor terrain names must not contain control / non-printable
    /// characters (they render as tofu (box) on macOS). FE8U's terrain-name table
    /// decodes most names with a trailing U+001F byte and empty slots as U+007F U+007F.
    /// <see cref="MoveCostEditorViewModel.LoadTerrainNames"/> now sanitizes the
    /// decoded name via <c>U.ToOneLineCaption</c> before composing the display label.
    ///
    /// Loads a real ROM via <see cref="RomTestHelper"/>; skips cleanly (no asserts)
    /// when no ROM is available (CI without roms.zip).
    /// </summary>
    [Collection("SharedState")]
    public class MoveCostTerrainNameSanitizeTests
    {
        [Theory]
        [InlineData("FE8U")]
        [InlineData("FE7U")]
        [InlineData("FE8J")]
        [InlineData("FE7J")]
        [InlineData("FE6")]
        public void TerrainNames_ContainNoControlChars(string version)
        {
            RomTestHelper.WithRom(version, () =>
            {
                var vm = new MoveCostEditorViewModel();
                vm.LoadTerrainNames();

                Assert.NotNull(vm.TerrainNames);
                Assert.Equal(MoveCostEditorViewModel.TerrainCount, vm.TerrainNames.Length);

                for (int i = 0; i < vm.TerrainNames.Length; i++)
                {
                    string name = vm.TerrainNames[i] ?? "";
                    foreach (char c in name)
                    {
                        Assert.False(char.IsControl(c),
                            $"[{version}] terrain name #{i} (\"{name}\") contains control char " +
                            $"U+{((int)c):X4} which renders as tofu on macOS (#1722).");
                    }
                }
            });
        }
    }
}
