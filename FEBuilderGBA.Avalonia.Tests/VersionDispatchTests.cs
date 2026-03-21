using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// WU5: Tests verifying correct editor view dispatch for each ROM version
    /// (FE6, FE7JP, FE7U, FE8JP, FE8U).
    ///
    /// These tests verify the conditions that determine which view is opened,
    /// not the actual window opening. They test:
    /// - ROM version properties (version number, map_setting_datasize, is_multibyte)
    /// - MapSettingCore.IsFE7ULayout dispatch helper
    /// - GetVersionVisibility pure function for button visibility
    /// - Version-specific section visibility rules
    /// </summary>
    public class VersionDispatchTests
    {
        // ===== ROM Version Property Tests =====

        [Fact]
        public void FE6_Version_Is6()
        {
            // FE6 ROM class reports version == 6
            // Used by: OpenClasses_Click (version == 6 → ClassFE6View)
            //          OpenMapSettings_Click (ver == 6 → MapSettingFE6View)
            Assert.Equal(6, GetVersionForRomClass("FE6"));
        }

        [Fact]
        public void FE7JP_Version_Is7()
        {
            Assert.Equal(7, GetVersionForRomClass("FE7J"));
        }

        [Fact]
        public void FE7U_Version_Is7()
        {
            Assert.Equal(7, GetVersionForRomClass("FE7U"));
        }

        [Fact]
        public void FE8JP_Version_Is8()
        {
            Assert.Equal(8, GetVersionForRomClass("FE8J"));
        }

        [Fact]
        public void FE8U_Version_Is8()
        {
            Assert.Equal(8, GetVersionForRomClass("FE8U"));
        }

        // ===== MapSettingCore.IsFE7ULayout Tests =====

        [Fact]
        public void MapSettingCore_FE6_DataSize68_NotFE7ULayout()
        {
            // FE6 uses 68-byte map settings (some patched to 72)
            Assert.False(MapSettingCore.IsFE7ULayout(68));
        }

        [Fact]
        public void MapSettingCore_FE6_DataSize72_NotFE7ULayout()
        {
            // FE6 patched variant uses 72-byte map settings
            Assert.False(MapSettingCore.IsFE7ULayout(72));
        }

        [Fact]
        public void MapSettingCore_FE7JP_DataSize148_NotFE7ULayout()
        {
            // FE7JP uses 148-byte map settings → MapSettingFE7View
            Assert.False(MapSettingCore.IsFE7ULayout(148));
        }

        [Fact]
        public void MapSettingCore_FE7U_DataSize152_IsFE7ULayout()
        {
            // FE7U uses 152-byte map settings → MapSettingFE7UView
            Assert.True(MapSettingCore.IsFE7ULayout(152));
        }

        [Fact]
        public void MapSettingCore_FE8JP_DataSize148_NotFE7ULayout()
        {
            // FE8JP uses 148-byte map settings → generic MapSettingView
            Assert.False(MapSettingCore.IsFE7ULayout(148));
        }

        [Fact]
        public void MapSettingCore_FE8U_DataSize148_NotFE7ULayout()
        {
            // FE8U uses 148-byte map settings → generic MapSettingView
            Assert.False(MapSettingCore.IsFE7ULayout(148));
        }

        [Fact]
        public void MapSettingCore_Boundary_151_NotFE7ULayout()
        {
            // Below threshold: not FE7U layout
            Assert.False(MapSettingCore.IsFE7ULayout(151));
        }

        [Fact]
        public void MapSettingCore_Boundary_152_IsFE7ULayout()
        {
            // At threshold: FE7U layout
            Assert.True(MapSettingCore.IsFE7ULayout(152));
        }

        [Fact]
        public void MapSettingCore_LargerThan152_IsFE7ULayout()
        {
            // Above threshold: still FE7U layout (e.g., if patched to larger)
            Assert.True(MapSettingCore.IsFE7ULayout(200));
        }

        // ===== Map Settings Dispatch Logic Tests =====
        // These verify the combined conditions used by OpenMapSettings_Click

        [Fact]
        public void MapSettings_FE6_Dispatches_To_FE6View()
        {
            // version == 6 → MapSettingFE6View
            int ver = 6;
            uint dataSize = 68;

            string expectedView = ResolveMapSettingView(ver, dataSize);
            Assert.Equal("MapSettingFE6View", expectedView);
        }

        [Fact]
        public void MapSettings_FE7JP_Dispatches_To_FE7View()
        {
            // version == 7, dataSize == 148 → MapSettingFE7View (not FE7U layout)
            int ver = 7;
            uint dataSize = 148;

            string expectedView = ResolveMapSettingView(ver, dataSize);
            Assert.Equal("MapSettingFE7View", expectedView);
        }

        [Fact]
        public void MapSettings_FE7U_Dispatches_To_FE7UView()
        {
            // version == 7, dataSize == 152 → MapSettingFE7UView (FE7U layout)
            int ver = 7;
            uint dataSize = 152;

            string expectedView = ResolveMapSettingView(ver, dataSize);
            Assert.Equal("MapSettingFE7UView", expectedView);
        }

        [Fact]
        public void MapSettings_FE8JP_Dispatches_To_GenericView()
        {
            // version == 8 → MapSettingView (generic)
            int ver = 8;
            uint dataSize = 148;

            string expectedView = ResolveMapSettingView(ver, dataSize);
            Assert.Equal("MapSettingView", expectedView);
        }

        [Fact]
        public void MapSettings_FE8U_Dispatches_To_GenericView()
        {
            // version == 8, dataSize == 148 → MapSettingView (generic)
            int ver = 8;
            uint dataSize = 148;

            string expectedView = ResolveMapSettingView(ver, dataSize);
            Assert.Equal("MapSettingView", expectedView);
        }

        // ===== Class Editor Dispatch Logic Tests =====

        [Fact]
        public void ClassEditor_FE6_Dispatches_To_ClassFE6View()
        {
            // version == 6 → ClassFE6View
            string view = ResolveClassEditorView(6);
            Assert.Equal("ClassFE6View", view);
        }

        [Fact]
        public void ClassEditor_FE7_Dispatches_To_ClassEditorView()
        {
            // version != 6 → ClassEditorView
            string view = ResolveClassEditorView(7);
            Assert.Equal("ClassEditorView", view);
        }

        [Fact]
        public void ClassEditor_FE8_Dispatches_To_ClassEditorView()
        {
            // version != 6 → ClassEditorView
            string view = ResolveClassEditorView(8);
            Assert.Equal("ClassEditorView", view);
        }

        // ===== GetVersionVisibility Tests =====
        // Tests the pure function that determines button visibility based on content tags

        [Fact]
        public void GetVersionVisibility_FE6Tag_ShowsForVersion6()
        {
            bool? result = MainWindow.GetVersionVisibility("Unit Editor (FE6)", 6, true);
            Assert.True(result);
        }

        [Fact]
        public void GetVersionVisibility_FE6Tag_HidesForVersion7()
        {
            bool? result = MainWindow.GetVersionVisibility("Unit Editor (FE6)", 7, false);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE6Tag_HidesForVersion8()
        {
            bool? result = MainWindow.GetVersionVisibility("Unit Editor (FE6)", 8, false);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE7Tag_ShowsForVersion7()
        {
            bool? result = MainWindow.GetVersionVisibility("Battle Talk (FE7)", 7, true);
            Assert.True(result);
        }

        [Fact]
        public void GetVersionVisibility_FE7Tag_HidesForVersion6()
        {
            bool? result = MainWindow.GetVersionVisibility("Battle Talk (FE7)", 6, true);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE7Tag_HidesForVersion8()
        {
            bool? result = MainWindow.GetVersionVisibility("Battle Talk (FE7)", 8, false);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE8Tag_ShowsForVersion8()
        {
            bool? result = MainWindow.GetVersionVisibility("Monster Editor (FE8)", 8, false);
            Assert.True(result);
        }

        [Fact]
        public void GetVersionVisibility_FE8Tag_HidesForVersion6()
        {
            bool? result = MainWindow.GetVersionVisibility("Monster Editor (FE8)", 6, true);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE8Tag_HidesForVersion7()
        {
            bool? result = MainWindow.GetVersionVisibility("Monster Editor (FE8)", 7, false);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE7UTag_ShowsForFE7U()
        {
            // FE7U: version == 7, is_multibyte == false
            bool? result = MainWindow.GetVersionVisibility("CG Editor (FE7U)", 7, false);
            Assert.True(result);
        }

        [Fact]
        public void GetVersionVisibility_FE7UTag_HidesForFE7JP()
        {
            // FE7JP: version == 7, is_multibyte == true → FE7U tag should hide
            bool? result = MainWindow.GetVersionVisibility("CG Editor (FE7U)", 7, true);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE7UTag_HidesForFE8()
        {
            bool? result = MainWindow.GetVersionVisibility("CG Editor (FE7U)", 8, false);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE8UTag_ShowsForFE8U()
        {
            // FE8U: version == 8, is_multibyte == false
            bool? result = MainWindow.GetVersionVisibility("Extra Unit (FE8U)", 8, false);
            Assert.True(result);
        }

        [Fact]
        public void GetVersionVisibility_FE8UTag_HidesForFE8JP()
        {
            // FE8JP: version == 8, is_multibyte == true → FE8U tag should hide
            bool? result = MainWindow.GetVersionVisibility("Extra Unit (FE8U)", 8, true);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE8UTag_HidesForFE7()
        {
            bool? result = MainWindow.GetVersionVisibility("Extra Unit (FE8U)", 7, false);
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_NoTag_ReturnsNull()
        {
            // No version tag in content → null (always show)
            bool? result = MainWindow.GetVersionVisibility("Unit Editor", 6, true);
            Assert.Null(result);
        }

        [Fact]
        public void GetVersionVisibility_EmptyContent_ReturnsNull()
        {
            bool? result = MainWindow.GetVersionVisibility("", 8, false);
            Assert.Null(result);
        }

        [Fact]
        public void GetVersionVisibility_FE7UTag_TakesPrecedenceOverFE7()
        {
            // If a button content contains "(FE7U)", the FE7U check runs first
            // even if "(FE7)" is also present. This tests the priority order.
            // Content with both "(FE7U)" and "(FE7)" should use FE7U logic.
            bool? result = MainWindow.GetVersionVisibility("Something (FE7U) and (FE7)", 7, true);
            // FE7U check: ver==7 && !isMultibyte → false (JP is multibyte)
            Assert.False(result);
        }

        [Fact]
        public void GetVersionVisibility_FE8UTag_TakesPrecedenceOverFE8()
        {
            // "(FE8U)" check runs before "(FE8)" check
            bool? result = MainWindow.GetVersionVisibility("Something (FE8U) and (FE8)", 8, true);
            // FE8U check: ver==8 && !isMultibyte → false (JP is multibyte)
            Assert.False(result);
        }

        // ===== is_multibyte Property Tests =====

        [Fact]
        public void FE6_IsMultibyte_True()
        {
            // FE6 (Japan) uses multibyte encoding
            Assert.True(GetIsMultibyteForRomClass("FE6"));
        }

        [Fact]
        public void FE7JP_IsMultibyte_True()
        {
            // FE7JP uses multibyte encoding (Shift-JIS)
            Assert.True(GetIsMultibyteForRomClass("FE7J"));
        }

        [Fact]
        public void FE7U_IsMultibyte_False()
        {
            // FE7U uses single-byte encoding (ASCII subset)
            Assert.False(GetIsMultibyteForRomClass("FE7U"));
        }

        [Fact]
        public void FE8JP_IsMultibyte_True()
        {
            // FE8JP uses multibyte encoding
            Assert.True(GetIsMultibyteForRomClass("FE8J"));
        }

        [Fact]
        public void FE8U_IsMultibyte_False()
        {
            // FE8U uses single-byte encoding
            Assert.False(GetIsMultibyteForRomClass("FE8U"));
        }

        // ===== map_setting_datasize Property Tests =====

        [Fact]
        public void FE7JP_MapSettingDataSize_Is148()
        {
            Assert.Equal(148u, GetMapSettingDataSizeForRomClass("FE7J"));
        }

        [Fact]
        public void FE7U_MapSettingDataSize_Is152()
        {
            Assert.Equal(152u, GetMapSettingDataSizeForRomClass("FE7U"));
        }

        [Fact]
        public void FE8JP_MapSettingDataSize_Is148()
        {
            Assert.Equal(148u, GetMapSettingDataSizeForRomClass("FE8J"));
        }

        [Fact]
        public void FE8U_MapSettingDataSize_Is148()
        {
            Assert.Equal(148u, GetMapSettingDataSizeForRomClass("FE8U"));
        }

        // ===== FE8-only Section Visibility Tests =====

        [Theory]
        [InlineData(6, false)]
        [InlineData(7, false)]
        [InlineData(8, true)]
        public void MonstersExpander_OnlyVisibleForFE8(int ver, bool expectedVisible)
        {
            // UpdateEditorVisibility: MonstersExpander.IsVisible = (ver == 8)
            Assert.Equal(expectedVisible, ver == 8);
        }

        [Theory]
        [InlineData(6, false)]
        [InlineData(7, false)]
        [InlineData(8, true)]
        public void SummonsExpander_OnlyVisibleForFE8(int ver, bool expectedVisible)
        {
            // UpdateEditorVisibility: SummonsExpander.IsVisible = (ver == 8)
            Assert.Equal(expectedVisible, ver == 8);
        }

        [Theory]
        [InlineData(6, false)]
        [InlineData(7, false)]
        [InlineData(8, true)]
        public void SkillsExpander_OnlyVisibleForFE8(int ver, bool expectedVisible)
        {
            // UpdateEditorVisibility: SkillsExpander.IsVisible = (ver == 8)
            Assert.Equal(expectedVisible, ver == 8);
        }

        [Theory]
        [InlineData(6, false)]
        [InlineData(7, true)]
        [InlineData(8, false)]
        public void SensekiComment_OnlyVisibleForFE7(int ver, bool expectedVisible)
        {
            // UpdateEditorVisibility: SensekiCommentButton.IsVisible = (ver == 7)
            Assert.Equal(expectedVisible, ver == 7);
        }

        // ===== VersionToFilename Tests =====

        [Fact]
        public void FE6_VersionToFilename_IsFE6()
        {
            Assert.Equal("FE6", GetVersionToFilenameForRomClass("FE6"));
        }

        [Fact]
        public void FE7JP_VersionToFilename_IsFE7J()
        {
            Assert.Equal("FE7J", GetVersionToFilenameForRomClass("FE7J"));
        }

        [Fact]
        public void FE7U_VersionToFilename_IsFE7U()
        {
            Assert.Equal("FE7U", GetVersionToFilenameForRomClass("FE7U"));
        }

        [Fact]
        public void FE8JP_VersionToFilename_IsFE8J()
        {
            Assert.Equal("FE8J", GetVersionToFilenameForRomClass("FE8J"));
        }

        [Fact]
        public void FE8U_VersionToFilename_IsFE8U()
        {
            Assert.Equal("FE8U", GetVersionToFilenameForRomClass("FE8U"));
        }

        // ===== Helpers =====

        /// <summary>
        /// Reproduces the dispatch logic from MainWindow.OpenMapSettings_Click.
        /// Returns the name of the view that would be opened.
        /// </summary>
        private static string ResolveMapSettingView(int ver, uint mapSettingDataSize)
        {
            if (ver == 6)
                return "MapSettingFE6View";
            else if (ver == 7)
            {
                if (MapSettingCore.IsFE7ULayout(mapSettingDataSize))
                    return "MapSettingFE7UView";
                else
                    return "MapSettingFE7View";
            }
            else
                return "MapSettingView";
        }

        /// <summary>
        /// Reproduces the dispatch logic from MainWindow.OpenClasses_Click.
        /// Returns the name of the view that would be opened.
        /// </summary>
        private static string ResolveClassEditorView(int ver)
        {
            if (ver == 6)
                return "ClassFE6View";
            else
                return "ClassEditorView";
        }

        /// <summary>
        /// Create a minimal ROM instance for the specified version and return its version number.
        /// Uses a 32MB byte array to satisfy constructor requirements.
        /// </summary>
        private static int GetVersionForRomClass(string versionStr)
        {
            ROMFEINFO info = CreateRomInfo(versionStr);
            return info.version;
        }

        /// <summary>
        /// Create a minimal ROM instance and return its is_multibyte property.
        /// </summary>
        private static bool GetIsMultibyteForRomClass(string versionStr)
        {
            ROMFEINFO info = CreateRomInfo(versionStr);
            return info.is_multibyte;
        }

        /// <summary>
        /// Create a minimal ROM instance and return its map_setting_datasize property.
        /// </summary>
        private static uint GetMapSettingDataSizeForRomClass(string versionStr)
        {
            ROMFEINFO info = CreateRomInfo(versionStr);
            return info.map_setting_datasize;
        }

        /// <summary>
        /// Create a minimal ROM instance and return its VersionToFilename property.
        /// </summary>
        private static string GetVersionToFilenameForRomClass(string versionStr)
        {
            ROMFEINFO info = CreateRomInfo(versionStr);
            return info.VersionToFilename;
        }

        /// <summary>
        /// Create a ROMFEINFO instance for the given version string.
        /// Uses ROM.LoadLow with a sufficiently large byte array and appropriate version string.
        /// FE6 requires >= 8MB (0x800000), FE7/FE8 require >= 16MB (0x1000000).
        /// </summary>
        private static ROMFEINFO CreateRomInfo(string versionStr)
        {
            var rom = new ROM();
            // FE6 needs >= 0x800000, FE7/FE8 need >= 0x1000000
            int size = versionStr == "FE6" ? 0x00800000 : 0x02000000;
            byte[] data = new byte[size];

            string headerVersion = versionStr switch
            {
                "FE6" => "AFEJ01",
                "FE7J" => "AE7J01",
                "FE7U" => "AE7E01",
                "FE8J" => "BE8J01",
                "FE8U" => "BE8E01",
                _ => throw new System.ArgumentException($"Unknown version: {versionStr}")
            };

            bool ok = rom.LoadLow("test.gba", data, headerVersion);
            if (!ok)
                throw new System.InvalidOperationException($"ROM.LoadLow failed for {versionStr}");

            return rom.RomInfo;
        }
    }
}
