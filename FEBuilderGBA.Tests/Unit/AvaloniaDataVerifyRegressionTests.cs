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

        // ---- #48: listCount==0 early skip guard ----

        [Fact]
        public void MainWindow_DataVerifySkipsListCountZero()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));
            Assert.Contains("SKIP (listCount=0)", src);
            Assert.Contains("if (listCount == 0)", src);
        }

        // ---- #50: bit-flag popups are not IDataVerifiable ----

        [Fact]
        public void UbyteBitFlagViewModel_NotIDataVerifiable()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels", "UbyteBitFlagViewModel.cs"));
            Assert.DoesNotContain("IDataVerifiable", src);
            Assert.DoesNotContain("GetListCount", src);
            Assert.DoesNotContain("GetDataReport", src);
            Assert.DoesNotContain("GetRawRomReport", src);
        }

        [Fact]
        public void UshortBitFlagViewModel_NotIDataVerifiable()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels", "UshortBitFlagViewModel.cs"));
            Assert.DoesNotContain("IDataVerifiable", src);
            Assert.DoesNotContain("GetListCount", src);
            Assert.DoesNotContain("GetDataReport", src);
            Assert.DoesNotContain("GetRawRomReport", src);
        }

        [Fact]
        public void UwordBitFlagViewModel_NotIDataVerifiable()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels", "UwordBitFlagViewModel.cs"));
            Assert.DoesNotContain("IDataVerifiable", src);
            Assert.DoesNotContain("GetListCount", src);
            Assert.DoesNotContain("GetDataReport", src);
            Assert.DoesNotContain("GetRawRomReport", src);
        }

        // ---- #51: ItemRandomChest cleanly skips via listCount==0 ----

        [Fact]
        public void ItemRandomChestViewModel_ReturnsZeroListCountWithoutBaseAddr()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "ViewModels", "ItemRandomChestViewModel.cs"));
            // LoadList returns empty when _baseAddr == 0
            Assert.Contains("if (_baseAddr == 0) return new List<AddrResult>()", src);
        }

        // ---- #49: ROM-backed editors have Opened handlers that load data ----

        [Fact]
        public void MapSettingView_LoadsOnOpen()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MapSettingView.axaml.cs"));
            Assert.Contains("Opened += ", src);
            Assert.Contains("LoadList()", src);
            Assert.Contains("SelectFirstItem()", src);
        }

        [Fact]
        public void EventCondView_LoadsOnOpen()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "EventCondView.axaml.cs"));
            Assert.Contains("Opened += ", src);
            Assert.Contains("SelectFirstItem()", src);
        }

        [Fact]
        public void WorldMapEventPointerView_LoadsOnOpen()
        {
            string src = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "WorldMapEventPointerView.axaml.cs"));
            Assert.Contains("Opened += ", src);
            Assert.Contains("LoadList()", src);
            Assert.Contains("SelectFirstItem()", src);
        }
    }
}
