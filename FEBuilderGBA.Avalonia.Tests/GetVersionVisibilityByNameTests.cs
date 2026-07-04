// SPDX-License-Identifier: GPL-3.0-or-later
// #1798: version gating must key off the STABLE, version-encoding button Name, not the
// translatable Content. In Japanese, config/translate/ja.txt renders "Unit (FE6)" as
// "ユニット（FE6）" with FULL-WIDTH parens （ ）, so the old ASCII "(FE6)" Content check
// returned null (no tag) and left FE6/FE7/FE7U/FE8U buttons visible on FE8J.
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class GetVersionVisibilityByNameTests
    {
        // Representative version-encoding names vs every ROM version (ver, isMultibyte):
        // FE6 (6,true JP-only), FE7J (7,true), FE7U (7,false), FE8J (8,true), FE8U (8,false).
        [Theory]
        // FE6 button — visible only on FE6
        [InlineData("UnitFE6Button", 6, true, true)]
        [InlineData("UnitFE6Button", 7, false, false)]
        [InlineData("UnitFE6Button", 8, true, false)]   // FE8J — hidden (the reported bug)
        [InlineData("UnitFE6Button", 8, false, false)]  // FE8U — hidden
        // FE7 (generic) button — visible on both FE7J and FE7U, hidden elsewhere
        [InlineData("SupTalkFE7Button", 7, true, true)]
        [InlineData("SupTalkFE7Button", 7, false, true)]
        [InlineData("SupTalkFE7Button", 8, true, false)]
        // FE7U (region) button — visible ONLY on FE7U (ver==7 && !isMultibyte)
        [InlineData("MapSettingsFE7UButton", 7, false, true)]
        [InlineData("MapSettingsFE7UButton", 7, true, false)]  // FE7J — hidden
        [InlineData("MapSettingsFE7UButton", 8, false, false)]
        // FE8U (region) button — visible ONLY on FE8U (ver==8 && !isMultibyte)
        [InlineData("OPDemoFE8UButton", 8, false, true)]
        [InlineData("OPDemoFE8UButton", 8, true, false)]       // FE8J — hidden
        [InlineData("ExtraFE8UButton", 8, false, true)]
        [InlineData("ExtraFE8UButton", 8, true, false)]
        public void GetVersionVisibilityByName_GatesByStableName(string name, int ver, bool isMultibyte, bool expectedVisible)
        {
            bool? actual = MainWindow.GetVersionVisibilityByName(name, ver, isMultibyte);
            Assert.True(actual.HasValue, $"{name} should carry a version tag");
            Assert.Equal(expectedVisible, actual!.Value);
        }

        // False-positive guard: skill-system "FE8N" buttons contain "FE8" but are NOT
        // FE8/FE8U region-version buttons — they must NOT be gated by this rule (return
        // null so the caller's section/Content handling applies), or they'd be wrongly
        // hidden. Likewise a generic (untagged) name returns null.
        [Theory]
        [InlineData("UnitFE8NButton")]      // skill-system (FE8N), NOT ROM-version FE8
        [InlineData("ConfigFE8NButton")]
        [InlineData("ConfigFE8Nv2Button")]
        [InlineData("ConfigFE8Nv3Button")]
        [InlineData("UnitCSkillSysButton")] // skill-system, gated by patch presence
        [InlineData("ConfigCSkill09xButton")]
        [InlineData("UnitsButton")]
        [InlineData("ClassesButton")]
        [InlineData("")]
        [InlineData(null)]
        public void GetVersionVisibilityByName_NoVersionSuffix_ReturnsNull(string? name)
        {
            Assert.Null(MainWindow.GetVersionVisibilityByName(name, 8, true));
        }

        // Documents the #1798 root cause AND why the fix is robust: version gating must
        // not depend on the (localized) Content. The Name-based gate hides the FE6 button
        // on FE8J regardless of whether Content uses ASCII "(FE6)" or Japanese full-width
        // "（FE6）" — it never reads Content at all. The old Content-based check only
        // recognizes ASCII parens (asserted here as the stable contract); a future
        // improvement could teach it full-width parens, but the Name-based gate — the
        // actual fix — does not depend on that, so this test won't regress if it does.
        [Fact]
        public void NameGating_IsContentIndependent_UnlikeContentGating()
        {
            // Name-based gate (the fix): FE6 button hidden on FE8J for ANY Content shape.
            Assert.Equal(false, MainWindow.GetVersionVisibilityByName("UnitFE6Button", 8, true));

            // Content-based gate recognizes ASCII parens (stable, asserted contract)...
            Assert.Equal(false, MainWindow.GetVersionVisibility("Unit (FE6)", 8, true));
            // ...but the Name-based gate is what guarantees correctness in localized UIs,
            // where Content becomes "ユニット（FE6）" with full-width parens.
        }
    }
}
