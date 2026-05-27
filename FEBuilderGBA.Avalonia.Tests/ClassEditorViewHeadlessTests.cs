using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless Avalonia tests for ClassEditorView.
    /// Verifies control labels, visibility, format strings, and AutomationId coverage.
    /// Ref #185
    /// </summary>
    public class ClassEditorViewHeadlessTests
    {
        private readonly ITestOutputHelper _output;

        public ClassEditorViewHeadlessTests(ITestOutputHelper output) => _output = output;

        /// <summary>Helper: collect all controls with AutomationIds.</summary>
        private static List<(Control Control, string AutomationId)> CollectAutomationIds(Control root)
        {
            var result = new List<(Control, string)>();
            foreach (var child in root.GetLogicalDescendants().OfType<Control>())
            {
                var id = AutomationProperties.GetAutomationId(child);
                if (!string.IsNullOrEmpty(id))
                    result.Add((child, id));
            }
            return result;
        }

        // ===================================================================
        // Control instantiation
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_CanInstantiate()
        {
            var view = new ClassEditorView();
            Assert.NotNull(view);
        }

        // ===================================================================
        // Key named controls exist
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_Has_ClassList_Control()
        {
            var view = new ClassEditorView();
            var list = view.FindControl<AddressListControl>("ClassList");
            Assert.NotNull(list);
        }

        [AvaloniaFact]
        public void ClassEditorView_Has_AddrLabel()
        {
            var view = new ClassEditorView();
            var label = view.FindControl<TextBlock>("AddrLabel");
            Assert.NotNull(label);
        }

        [AvaloniaFact]
        public void ClassEditorView_Has_NameLabel()
        {
            var view = new ClassEditorView();
            var label = view.FindControl<TextBlock>("NameLabel");
            Assert.NotNull(label);
        }

        // ===================================================================
        // NumericUpDown format strings
        // ===================================================================

        [AvaloniaTheory]
        [InlineData("NameIdBox")]
        [InlineData("DescIdBox")]
        [InlineData("ClassNumberBox")]
        [InlineData("PromotionLevelBox")]
        [InlineData("WaitIconBox")]
        [InlineData("WalkSpeedBox")]
        [InlineData("PortraitIdBox")]
        [InlineData("SortOrderBox")]
        public void IdentityFields_Have_DecimalFormat(string controlName)
        {
            // Avalonia NumericUpDown uses decimal internally; hex FormatString
            // (X2/X4) causes FormatException since decimal.ToString("X4") is
            // invalid. NumericUpDown fields must use decimal format "0" because
            // Avalonia NumericUpDown uses decimal internally and hex format
            // specifiers (X2/X4) cause FormatException. See #253.
            var view = new ClassEditorView();
            var nud = view.FindControl<NumericUpDown>(controlName);
            Assert.NotNull(nud);
            Assert.Equal("0", nud!.FormatString);
            _output.WriteLine($"{controlName}: FormatString={nud.FormatString} (OK)");
        }

        [AvaloniaTheory]
        [InlineData("BaseHpBox")]
        [InlineData("BaseStrBox")]
        [InlineData("BaseSklBox")]
        [InlineData("BaseSpdBox")]
        [InlineData("BaseDefBox")]
        [InlineData("BaseResBox")]
        [InlineData("BaseMovBox")]
        [InlineData("BaseConBox")]
        public void BaseStatFields_Have_DecimalFormat(string controlName)
        {
            var view = new ClassEditorView();
            var nud = view.FindControl<NumericUpDown>(controlName);
            Assert.NotNull(nud);
            Assert.Equal("0", nud!.FormatString);
        }

        [AvaloniaTheory]
        [InlineData("GrowHpBox")]
        [InlineData("GrowStrBox")]
        [InlineData("GrowSklBox")]
        [InlineData("GrowSpdBox")]
        [InlineData("GrowDefBox")]
        [InlineData("GrowResBox")]
        [InlineData("GrowLckBox")]
        public void GrowthFields_Have_DecimalFormat(string controlName)
        {
            var view = new ClassEditorView();
            var nud = view.FindControl<NumericUpDown>(controlName);
            Assert.NotNull(nud);
            Assert.Equal("0", nud!.FormatString);
        }

        // ===================================================================
        // Stat cap signed range
        // ===================================================================

        [AvaloniaTheory]
        [InlineData("MaxHpBox")]
        [InlineData("MaxStrBox")]
        [InlineData("MaxSklBox")]
        [InlineData("MaxSpdBox")]
        [InlineData("MaxDefBox")]
        [InlineData("MaxResBox")]
        public void StatCapFields_Allow_UnsignedRange(string controlName)
        {
            var view = new ClassEditorView();
            var nud = view.FindControl<NumericUpDown>(controlName);
            Assert.NotNull(nud);
            Assert.Equal(0, nud!.Minimum);
            Assert.Equal(255, nud!.Maximum);
        }

        // ===================================================================
        // FE6-conditional controls exist (initially visible for FE7/8 default)
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_MaxSkl_Visible_ByDefault()
        {
            var view = new ClassEditorView();
            var box = view.FindControl<NumericUpDown>("MaxSklBox");
            Assert.NotNull(box);
            // Default (no ROM loaded) should show FE7/8 fields
            Assert.True(box!.IsVisible);
        }

        [AvaloniaFact]
        public void ClassEditorView_TerrainRow_Visible_ByDefault()
        {
            var view = new ClassEditorView();
            var row = view.FindControl<StackPanel>("TerrainRow");
            Assert.NotNull(row);
            Assert.True(row!.IsVisible);
        }

        [AvaloniaFact]
        public void ClassEditorView_D80Row_Visible_ByDefault()
        {
            var view = new ClassEditorView();
            var row = view.FindControl<StackPanel>("D80Row");
            Assert.NotNull(row);
            Assert.True(row!.IsVisible);
        }

        // ===================================================================
        // Weapon rank label defaults (FE7/8)
        // ===================================================================

        [AvaloniaTheory]
        [InlineData("WepRankSwordLabel", "Sword (B44):")]
        [InlineData("WepRankLanceLabel", "Lance (B45):")]
        [InlineData("WepRankAxeLabel", "Axe (B46):")]
        [InlineData("WepRankBowLabel", "Bow (B47):")]
        [InlineData("WepRankStaffLabel", "Staff (B48):")]
        [InlineData("WepRankAnimaLabel", "Anima (B49):")]
        [InlineData("WepRankLightLabel", "Light (B50):")]
        [InlineData("WepRankDarkLabel", "Dark (B51):")]
        public void WeaponRankLabels_Default_To_FE78_Offsets(string controlName, string expectedText)
        {
            var view = new ClassEditorView();
            var label = view.FindControl<TextBlock>(controlName);
            Assert.NotNull(label);
            Assert.Equal(expectedText, label!.Text);
        }

        // ===================================================================
        // Pointer label defaults (FE7/8)
        // ===================================================================

        [AvaloniaTheory]
        [InlineData("BattleAnimeLabel", "Battle Anime (P52):")]
        [InlineData("MoveCostLabel", "Move Cost (P56):")]
        [InlineData("MoveCostRainLabel", "Move Cost Rain (P60):")]
        [InlineData("MoveCostSnowLabel", "Move Cost Snow (P64):")]
        public void PointerLabels_Default_To_FE78_Offsets(string controlName, string expectedText)
        {
            var view = new ClassEditorView();
            var label = view.FindControl<TextBlock>(controlName);
            Assert.NotNull(label);
            Assert.Equal(expectedText, label!.Text);
        }

        // ===================================================================
        // AutomationId coverage
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_Has_No_Duplicate_AutomationIds()
        {
            var view = new ClassEditorView();
            var ids = CollectAutomationIds(view);
            var seen = new HashSet<string>();
            var duplicates = new List<string>();

            foreach (var (_, id) in ids)
            {
                if (!seen.Add(id))
                    duplicates.Add(id);
            }

            _output.WriteLine($"Total AutomationIds: {ids.Count}");
            foreach (var d in duplicates)
                _output.WriteLine($"  DUPLICATE: {d}");

            Assert.Empty(duplicates);
        }

        [AvaloniaFact]
        public void ClassEditorView_All_AutomationIds_Follow_Convention()
        {
            var validSuffixes = new[]
            {
                "_Input", "_Combo", "_Button", "_List", "_Check",
                "_Expander", "_TabControl", "_Tab", "_Image", "_Label", "_Control", "_Link",
                // "_TopBar" is the host id of the unified EditorTopBar
                // composite (#649); its inner controls receive the standard
                // suffixed ids ("ClassEditor_ReadStartAddress_Label" etc.).
                "_TopBar",
            };

            var view = new ClassEditorView();
            var ids = CollectAutomationIds(view);
            var violations = new List<string>();

            foreach (var (_, id) in ids)
            {
                bool startsCorrectly = id.StartsWith("ClassEditor_");
                bool hasValidSuffix = validSuffixes.Any(s => id.EndsWith(s));

                if (!startsCorrectly || !hasValidSuffix)
                    violations.Add(id);
            }

            _output.WriteLine($"Checked {ids.Count} IDs, violations: {violations.Count}");
            foreach (var v in violations)
                _output.WriteLine($"  VIOLATION: {v}");

            Assert.Empty(violations);
        }

        [AvaloniaFact]
        public void ClassEditorView_Has_Minimum_AutomationId_Count()
        {
            var view = new ClassEditorView();
            var ids = CollectAutomationIds(view);

            _output.WriteLine($"ClassEditorView AutomationId count: {ids.Count}");

            // Should have at least 100 AutomationIds (115 expected after adding the 16 missing ones)
            Assert.True(ids.Count >= 100,
                $"Expected >= 100 AutomationIds, found {ids.Count}");
        }

        // ===================================================================
        // Key AutomationIds from the newly added set
        // ===================================================================

        [AvaloniaTheory]
        [InlineData("ClassEditor_WepRankSwordLabel_Label")]
        [InlineData("ClassEditor_WepRankLanceLabel_Label")]
        [InlineData("ClassEditor_WepRankAxeLabel_Label")]
        [InlineData("ClassEditor_WepRankBowLabel_Label")]
        [InlineData("ClassEditor_WepRankStaffLabel_Label")]
        [InlineData("ClassEditor_WepRankAnimaLabel_Label")]
        [InlineData("ClassEditor_WepRankLightLabel_Label")]
        [InlineData("ClassEditor_WepRankDarkLabel_Label")]
        [InlineData("ClassEditor_BattleAnimeLabel_Label")]
        [InlineData("ClassEditor_MoveCostLabel_Label")]
        [InlineData("ClassEditor_MoveCostRainLabel_Label")]
        [InlineData("ClassEditor_MoveCostSnowLabel_Label")]
        [InlineData("ClassEditor_MaxSkl_Input")]
        [InlineData("ClassEditor_MaxSpd_Input")]
        [InlineData("ClassEditor_MaxDef_Input")]
        [InlineData("ClassEditor_MaxRes_Input")]
        public void ClassEditorView_Has_NewlyAdded_AutomationId(string expectedId)
        {
            var view = new ClassEditorView();
            var ids = CollectAutomationIds(view);
            var idSet = new HashSet<string>(ids.Select(x => x.AutomationId));

            Assert.Contains(expectedId, idSet);
        }

        // ===================================================================
        // Growth simulator controls
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_Has_GrowthSimulator_Controls()
        {
            var view = new ClassEditorView();

            var simLevel = view.FindControl<NumericUpDown>("SimLevelBox");
            Assert.NotNull(simLevel);
            Assert.Equal(1, simLevel!.Minimum);
            Assert.Equal(99, simLevel!.Maximum);
            Assert.Equal(20, simLevel!.Value);

            var growthLabel = view.FindControl<TextBlock>("GrowthSimLabel");
            Assert.NotNull(growthLabel);
        }

        // ===================================================================
        // EditSkills button initially hidden
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_EditSkills_Initially_Hidden()
        {
            var view = new ClassEditorView();
            var btn = view.FindControl<Button>("EditSkillsButton");
            Assert.NotNull(btn);
            Assert.False(btn!.IsVisible);
        }
    }
}
