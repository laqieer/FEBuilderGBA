using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    public class AvaloniaDataVerifyRegressionTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        [Fact]
        public void MainWindow_DataVerifySkipsEditorsWithoutComparableData()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));
            Assert.Contains("DataVerifyComparisonResult.Skip", src);
            Assert.Contains("SKIP (no comparable data)", src);
        }

        [Fact]
        public void StatusUnitsMenuViewModel_DereferencesStatusUnitsPointer()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels", "StatusUnitsMenuViewModel.cs"));
            Assert.Contains("rom.p32(pointer)", src);
        }

        [Fact]
        public void ItemRandomChestViewModel_NoLongerCreatesFakeZeroEntry()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels", "ItemRandomChestViewModel.cs"));
            Assert.DoesNotContain("new AddrResult(0, \"Random Chest Items\", 0)", src);
            Assert.Contains("SetBaseAddress", src);
        }

        [Fact]
        public void TextCharCodeViewModel_LoadsEntriesFromMaskPointer()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels", "TextCharCodeViewModel.cs"));
            Assert.Contains("rom.RomInfo.mask_pointer", src);
            Assert.Contains("CharCodes.Add(", src);
            Assert.Contains("_entryAddresses", src);
        }
    }
}
