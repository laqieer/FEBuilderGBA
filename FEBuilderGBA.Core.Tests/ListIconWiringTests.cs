using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Source-code verification tests for list icon wiring in Avalonia views.
    /// Ensures all batch 2 views use SetItemsWithIcons with the correct loader.
    /// </summary>
    public class ListIconWiringTests
    {
        static string FindSolutionRoot()
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string? dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("Cannot find solution root");
        }

        static string ReadViewSource(string viewFileName)
        {
            string root = FindSolutionRoot();
            string path = Path.Combine(root, "FEBuilderGBA.Avalonia", "Views", viewFileName);
            return File.ReadAllText(path);
        }

        static string ReadServiceSource(string serviceFileName)
        {
            string root = FindSolutionRoot();
            string path = Path.Combine(root, "FEBuilderGBA.Avalonia", "Services", serviceFileName);
            return File.ReadAllText(path);
        }

        // ---- ListIconLoaders has all new methods ----

        [Theory]
        [InlineData("ItemIconFromAddrU16Loader")]
        [InlineData("ItemIconFromAddrU8Loader")]
        [InlineData("UnitPortraitFromAddrU16Loader")]
        [InlineData("WaitIconDirectLoader")]
        [InlineData("MoveIconLoader")]
        [InlineData("ColorSwatchLoader")]
        [InlineData("BattleAnimeLoader")]
        [InlineData("BGThumbnailLoader")]
        [InlineData("CGThumbnailLoader")]
        [InlineData("CGFE7UThumbnailLoader")]
        [InlineData("SoundRoomCGThumbnailLoader")]
        [InlineData("AttributeIconLoader")]
        [InlineData("SkillIconLoader")]
        public void ListIconLoaders_HasMethod(string methodName)
        {
            string src = ReadServiceSource("ListIconLoaders.cs");
            Assert.Contains(methodName, src);
        }

        // ---- PreviewIconHelper has all new methods ----

        [Theory]
        [InlineData("LoadMoveIcon")]
        [InlineData("CreateColorSwatch")]
        [InlineData("LoadBattleAnimeThumbnail")]
        [InlineData("LoadBGThumbnail")]
        [InlineData("LoadCGThumbnail")]
        [InlineData("LoadCGFE7UThumbnail")]
        [InlineData("LoadItemIconWithWeaponPalette")]
        [InlineData("LoadSkillIcon")]
        [InlineData("FindSkillSystemIconBaseAddress")]
        public void PreviewIconHelper_HasMethod(string methodName)
        {
            string src = ReadServiceSource("PreviewIconHelper.cs");
            Assert.Contains(methodName, src);
        }

        // ---- Group A: Item icons from ROM addr ----

        [Theory]
        [InlineData("AIPerformItemView.axaml.cs", "ItemIconFromAddrU16Loader")]
        [InlineData("AIPerformStaffView.axaml.cs", "ItemIconFromAddrU16Loader")]
        [InlineData("AIStealItemView.axaml.cs", "ItemIconFromAddrU8Loader")]
        [InlineData("ArenaEnemyWeaponViewerView.axaml.cs", "ItemIconFromAddrU8Loader")]
        public void View_UsesItemIconLoader(string viewFile, string loaderName)
        {
            string src = ReadViewSource(viewFile);
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains(loaderName, src);
        }

        // ---- Group B: Class icon ----

        [Fact]
        public void CCBranchEditorView_UsesClassIconLoader()
        {
            string src = ReadViewSource("CCBranchEditorView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("ClassIconLoader", src);
        }

        // ---- Group C: Unit portrait ----

        [Fact]
        public void AIUnitsView_UsesUnitPortraitFromAddrU8Loader()
        {
            string src = ReadViewSource("AIUnitsView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("UnitPortraitFromAddrU8Loader", src);
        }

        // SkillAssignmentUnitSkillSystemView uses plain SetItems (VM returns single placeholder entry)

        // ---- Group D: Unit portrait from addr ----

        [Theory]
        [InlineData("EventBattleTalkView.axaml.cs", "UnitPortraitFromAddrU16Loader")]  // FE8: W0 = u16 unit ID
        [InlineData("EventBattleTalkFE6View.axaml.cs", "UnitPortraitFromAddrU8Loader")] // FE6: B0 = u8 unit ID
        [InlineData("EventBattleTalkFE7View.axaml.cs", "UnitPortraitFromAddrU8Loader")] // FE7: B0 = u8 unit ID
        // Issue #361: Support Talk views now use the PAIR loader so both
        // partner portraits render side-by-side. Each version supplies the
        // version-specific second-partner offset:
        //   - FE8     : partner 2 at addr+2
        //   - FE6/FE7 : partner 2 at addr+1
        [InlineData("SupportTalkView.axaml.cs", "UnitPortraitPairFromAddrU8Loader")]    // FE8
        [InlineData("SupportTalkFE6View.axaml.cs", "UnitPortraitPairFromAddrU8Loader")] // FE6
        [InlineData("SupportTalkFE7View.axaml.cs", "UnitPortraitPairFromAddrU8Loader")] // FE7
        public void View_UsesUnitPortraitFromAddrLoader(string viewFile, string loaderName)
        {
            string src = ReadViewSource(viewFile);
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains(loaderName, src);
        }

        /// <summary>
        /// Issue #361: each Support Talk view must pass the correct version-
        /// specific <c>unit2Offset</c> to <see cref="ListIconLoaders.UnitPortraitPairFromAddrU8Loader"/>.
        /// FE8 uses offset 2 (uid1@0, uid2@2); FE6/FE7 use offset 1 (uid1@0, uid2@1).
        /// Locked here so future refactors can't silently regress to the
        /// wrong offset (which would show the WRONG second portrait).
        /// </summary>
        [Theory]
        [InlineData("SupportTalkView.axaml.cs", "unit2Offset: 2")]    // FE8: addr+2
        [InlineData("SupportTalkFE6View.axaml.cs", "unit2Offset: 1")] // FE6: addr+1
        [InlineData("SupportTalkFE7View.axaml.cs", "unit2Offset: 1")] // FE7: addr+1
        public void SupportTalkView_PassesCorrectUnit2Offset(string viewFile, string expectedOffsetArg)
        {
            string src = ReadViewSource(viewFile);
            Assert.Contains("UnitPortraitPairFromAddrU8Loader", src);
            Assert.Contains(expectedOffsetArg, src);
        }

        // ---- Group E: Wait/Move icons ----

        [Fact]
        public void ImageUnitWaitIconView_UsesWaitIconDirectLoader()
        {
            string src = ReadViewSource("ImageUnitWaitIconView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("WaitIconDirectLoader", src);
        }

        [Fact]
        public void ImageUnitMoveIconView_UsesMoveIconLoader()
        {
            string src = ReadViewSource("ImageUnitMoveIconView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("MoveIconLoader", src);
        }

        // ---- Group F: Color swatches ----

        [Theory]
        [InlineData("SystemHoverColorViewerView.axaml.cs")]
        [InlineData("ImageSystemAreaView.axaml.cs")]
        [InlineData("MapTileAnimation2View.axaml.cs")]
        public void View_UsesColorSwatchLoader(string viewFile)
        {
            string src = ReadViewSource(viewFile);
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("ColorSwatchLoader", src);
        }

        // ---- Group G: Battle animation ----

        [Theory]
        [InlineData("ImageBattleAnimeView.axaml.cs", "BattleAnimeLoader")]
        [InlineData("MantAnimationView.axaml.cs", "BattleAnimeTextLoader")]
        public void View_UsesBattleAnimeLoader(string viewFile, string loaderName)
        {
            string src = ReadViewSource(viewFile);
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains(loaderName, src);
        }

        // ---- Group H: BG/CG thumbnails ----

        [Fact]
        public void ImageBGView_UsesBGThumbnailLoader()
        {
            string src = ReadViewSource("ImageBGView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("BGThumbnailLoader", src);
        }

        [Fact]
        public void ImageCGView_UsesCGThumbnailLoader()
        {
            string src = ReadViewSource("ImageCGView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("CGThumbnailLoader", src);
        }

        [Fact]
        public void ImageCGFE7UView_UsesCGFE7UThumbnailLoader()
        {
            string src = ReadViewSource("ImageCGFE7UView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("CGFE7UThumbnailLoader", src);
        }

        // ---- Group I: SoundRoomCG ----

        [Fact]
        public void SoundRoomCGView_UsesSoundRoomCGThumbnailLoader()
        {
            string src = ReadViewSource("SoundRoomCGView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("SoundRoomCGThumbnailLoader", src);
        }
        // ---- Group J: Attribute icons ----

        [Fact]
        public void SupportAttributeView_UsesAttributeIconLoader()
        {
            string src = ReadViewSource("SupportAttributeView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("AttributeIconLoader", src);
        }

        // ---- Group K: Skill icons ----

        [Fact]
        public void SkillConfigSkillSystemView_UsesSkillIconLoader()
        {
            string src = ReadViewSource("SkillConfigSkillSystemView.axaml.cs");
            Assert.Contains("SetItemsWithIcons(items", src);
            Assert.Contains("SkillIconLoader", src);
        }
    }
}
