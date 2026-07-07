using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Verifies that AutomationProperties.AutomationId is correctly set on
    /// interactive controls across all Avalonia editor views.
    ///
    /// These tests ensure:
    /// 1. Key editors expose expected AutomationIds on critical controls
    /// 2. All AutomationIds follow the naming convention {Editor}_{Field}_{Type}
    /// 3. No duplicate AutomationIds exist within a single view
    /// 4. A minimum coverage threshold is met across the codebase
    ///
    /// Ref #243
    /// </summary>
    public class AutomationIdTests
    {
        private readonly ITestOutputHelper _output;

        public AutomationIdTests(ITestOutputHelper output) => _output = output;

        // Valid suffixes per the naming convention. "_TopBar" is the host id
        // of the unified EditorTopBar composite (#649) — the control derives
        // suffixed inner ids from it (e.g. "{base}_StartAddress_Label",
        // "{base}_Reload_Button") so the host id itself doesn't end in one of
        // the leaf-control suffixes. It IS a valid id-shape; treat it as such.
        private static readonly string[] ValidSuffixes = new[]
        {
            "_Input", "_Combo", "_Button", "_List", "_Check",
            "_Expander", "_TabControl", "_Tab", "_Image", "_Label", "_Control", "_Link",
            "_TopBar",
        };

        /// <summary>
        /// Helper: get the AutomationId from a control.
        /// </summary>
        private static string GetAutomationId(Control control)
        {
            return AutomationProperties.GetAutomationId(control);
        }

        /// <summary>
        /// Helper: collect all controls with AutomationIds from a view's logical tree.
        /// </summary>
        private static List<(Control Control, string AutomationId)> CollectAutomationIds(Control root)
        {
            var result = new List<(Control, string)>();
            foreach (var child in root.GetLogicalDescendants().OfType<Control>())
            {
                var id = GetAutomationId(child);
                if (!string.IsNullOrEmpty(id))
                {
                    result.Add((child, id));
                }
            }
            return result;
        }

        /// <summary>
        /// Helper: instantiate a View type safely.
        /// </summary>
        private static Control TryCreateView(Type viewType)
        {
            try
            {
                return (Control)Activator.CreateInstance(viewType);
            }
            catch
            {
                return null;
            }
        }

        // ===================================================================
        // Test: UnitEditorView exposes expected AutomationIds
        // ===================================================================

        [AvaloniaFact]
        public void UnitEditorView_Has_Expected_AutomationIds()
        {
            var view = new Views.UnitEditorView();
            var ids = CollectAutomationIds(view);
            var idSet = new HashSet<string>(ids.Select(x => x.AutomationId));

            _output.WriteLine($"UnitEditorView: {ids.Count} AutomationIds found");
            foreach (var (_, id) in ids)
                _output.WriteLine($"  {id}");

            // Key controls that must be present
            Assert.Contains("UnitEditor_Unit_List", idSet);
            Assert.Contains("UnitEditor_NameId_Input", idSet);
            Assert.Contains("UnitEditor_ClassId_Combo", idSet);
            Assert.Contains("UnitEditor_HP_Input", idSet);
            Assert.Contains("UnitEditor_Str_Input", idSet);
            Assert.Contains("UnitEditor_Write_Button", idSet);
            Assert.Contains("UnitEditor_Portrait_Image", idSet);
            Assert.Contains("UnitEditor_Identity_Expander", idSet);
            Assert.Contains("UnitEditor_BaseStats_Expander", idSet);
        }

        // ===================================================================
        // Test: ClassEditorView exposes expected AutomationIds
        // ===================================================================

        [AvaloniaFact]
        public void ClassEditorView_Has_Expected_AutomationIds()
        {
            var view = new Views.ClassEditorView();
            var ids = CollectAutomationIds(view);
            var idSet = new HashSet<string>(ids.Select(x => x.AutomationId));

            _output.WriteLine($"ClassEditorView: {ids.Count} AutomationIds found");

            Assert.Contains("ClassEditor_Class_List", idSet);
            Assert.Contains("ClassEditor_NameId_Input", idSet);
            Assert.Contains("ClassEditor_BaseHp_Input", idSet);
            Assert.Contains("ClassEditor_GrowHp_Input", idSet);
            Assert.Contains("ClassEditor_IdentityMisc_Expander", idSet);
        }

        // ===================================================================
        // Test: ItemEditorView exposes expected AutomationIds
        // ===================================================================

        [AvaloniaFact]
        public void ItemEditorView_Has_Expected_AutomationIds()
        {
            var view = new Views.ItemEditorView();
            var ids = CollectAutomationIds(view);
            var idSet = new HashSet<string>(ids.Select(x => x.AutomationId));

            _output.WriteLine($"ItemEditorView: {ids.Count} AutomationIds found");

            Assert.Contains("ItemEditor_Item_List", idSet);
            Assert.Contains("ItemEditor_NameId_Input", idSet);
            Assert.Contains("ItemEditor_BasicInfo_Expander", idSet);
        }

        // ===================================================================
        // Test: MessageBoxWindow exposes expected AutomationIds
        // ===================================================================

        [AvaloniaFact]
        public void MessageBoxWindow_Has_Expected_AutomationIds()
        {
            var view = new Dialogs.MessageBoxWindow();
            var ids = CollectAutomationIds(view);
            var idSet = new HashSet<string>(ids.Select(x => x.AutomationId));

            _output.WriteLine($"MessageBoxWindow: {ids.Count} AutomationIds found");
            foreach (var (_, id) in ids)
                _output.WriteLine($"  {id}");

            Assert.Contains("MessageBoxContent_Ok_Button", idSet);
            Assert.Contains("MessageBoxContent_Yes_Button", idSet);
            Assert.Contains("MessageBoxContent_No_Button", idSet);
            Assert.Contains("MessageBoxContent_Message_Label", idSet);
        }

        // ===================================================================
        // Test: All AutomationIds follow the naming convention
        // ===================================================================

        [AvaloniaFact]
        public void All_AutomationIds_Follow_Naming_Convention()
        {
            var asm = typeof(FEBuilderGBA.Avalonia.App).Assembly;
            var viewTypes = asm.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => typeof(UserControl).IsAssignableFrom(t) || typeof(Window).IsAssignableFrom(t))
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName)
                .ToList();

            int totalIds = 0;
            int validIds = 0;
            var violations = new List<string>();

            foreach (var viewType in viewTypes)
            {
                var view = TryCreateView(viewType);
                if (view == null) continue;

                var ids = CollectAutomationIds(view);
                foreach (var (ctrl, id) in ids)
                {
                    totalIds++;
                    bool hasValidSuffix = ValidSuffixes.Any(s => id.EndsWith(s));
                    bool hasUnderscore = id.Contains('_');

                    if (hasValidSuffix && hasUnderscore)
                    {
                        validIds++;
                    }
                    else
                    {
                        violations.Add($"{viewType.Name}: '{id}' (suffix={!hasValidSuffix}, underscore={!hasUnderscore})");
                    }
                }
            }

            _output.WriteLine($"Total IDs checked: {totalIds}");
            _output.WriteLine($"Valid IDs: {validIds}");
            _output.WriteLine($"Violations: {violations.Count}");

            foreach (var v in violations.Take(20))
                _output.WriteLine($"  VIOLATION: {v}");

            // Allow up to 1% violations for edge cases
            double complianceRate = totalIds > 0 ? (double)validIds / totalIds : 1.0;
            Assert.True(complianceRate >= 0.99,
                $"Naming compliance rate {complianceRate:P1} is below 99% threshold. " +
                $"{violations.Count} violations out of {totalIds} IDs.");
        }

        // ===================================================================
        // Test: No duplicate AutomationIds within any single view
        // ===================================================================

        [AvaloniaFact]
        public void No_Duplicate_AutomationIds_Within_Views()
        {
            var asm = typeof(FEBuilderGBA.Avalonia.App).Assembly;
            var viewTypes = asm.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => typeof(UserControl).IsAssignableFrom(t) || typeof(Window).IsAssignableFrom(t))
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName)
                .ToList();

            var duplicates = new List<string>();

            foreach (var viewType in viewTypes)
            {
                var view = TryCreateView(viewType);
                if (view == null) continue;

                var ids = CollectAutomationIds(view);
                var seen = new HashSet<string>();
                foreach (var (_, id) in ids)
                {
                    if (!seen.Add(id))
                    {
                        duplicates.Add($"{viewType.Name}: duplicate '{id}'");
                    }
                }
            }

            _output.WriteLine($"Duplicates found: {duplicates.Count}");
            foreach (var d in duplicates.Take(20))
                _output.WriteLine($"  {d}");

            Assert.Empty(duplicates);
        }

        // ===================================================================
        // Test: Minimum coverage threshold across all views
        // ===================================================================

        [AvaloniaFact]
        public void AutomationId_Coverage_Meets_Minimum_Threshold()
        {
            var asm = typeof(FEBuilderGBA.Avalonia.App).Assembly;
            var viewTypes = asm.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => typeof(UserControl).IsAssignableFrom(t) || typeof(Window).IsAssignableFrom(t))
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName)
                .ToList();

            int viewsWithIds = 0;
            int viewsWithoutIds = 0;
            int totalIdCount = 0;

            foreach (var viewType in viewTypes)
            {
                var view = TryCreateView(viewType);
                if (view == null) continue;

                var ids = CollectAutomationIds(view);
                if (ids.Count > 0)
                {
                    viewsWithIds++;
                    totalIdCount += ids.Count;
                }
                else
                {
                    viewsWithoutIds++;
                }
            }

            _output.WriteLine($"Views with AutomationIds: {viewsWithIds}");
            _output.WriteLine($"Views without AutomationIds: {viewsWithoutIds}");
            _output.WriteLine($"Total AutomationIds: {totalIdCount}");

            // At least 2000 AutomationIds across all views
            Assert.True(totalIdCount >= 2000,
                $"Expected at least 2000 AutomationIds across all views, found {totalIdCount}");

            // At least 90% of views should have at least one AutomationId
            int totalViews = viewsWithIds + viewsWithoutIds;
            double coverageRate = totalViews > 0 ? (double)viewsWithIds / totalViews : 0;
            Assert.True(coverageRate >= 0.90,
                $"View coverage rate {coverageRate:P1} is below 90% threshold. " +
                $"{viewsWithoutIds} views have no AutomationIds.");
        }

        // ===================================================================
        // Test: .axaml source files have AutomationId attributes (static check)
        // ===================================================================

        [Fact]
        public void AxamlFiles_Contain_AutomationId_Attributes()
        {
            // Find the Avalonia project views directory
            string projectRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", ".."));
            string viewsDir = Path.Combine(projectRoot, "FEBuilderGBA.Avalonia", "Views");

            if (!Directory.Exists(viewsDir))
            {
                _output.WriteLine($"Views directory not found at: {viewsDir}");
                _output.WriteLine("Skipping static .axaml check (directory not found)");
                return;
            }

            var axamlFiles = Directory.GetFiles(viewsDir, "*.axaml", SearchOption.TopDirectoryOnly);
            int filesWithIds = 0;
            int filesWithoutIds = 0;

            foreach (var file in axamlFiles)
            {
                string content = File.ReadAllText(file);
                if (content.Contains("AutomationProperties.AutomationId"))
                {
                    filesWithIds++;
                }
                else
                {
                    filesWithoutIds++;
                    _output.WriteLine($"  No AutomationId: {Path.GetFileName(file)}");
                }
            }

            _output.WriteLine($"\n.axaml files with AutomationIds: {filesWithIds}");
            _output.WriteLine($".axaml files without AutomationIds: {filesWithoutIds}");

            // At least 95% of .axaml files should have AutomationIds
            double coverage = axamlFiles.Length > 0 ? (double)filesWithIds / axamlFiles.Length : 0;
            Assert.True(coverage >= 0.95,
                $"Only {coverage:P1} of .axaml files have AutomationIds " +
                $"({filesWithIds}/{axamlFiles.Length}). Expected >= 95%.");
        }

        // ===================================================================
        // Test: Exempt files do NOT have AutomationIds
        // ===================================================================

        [Fact]
        public void Exempt_Files_Do_Not_Have_AutomationIds()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", ".."));
            string avaloniaDir = Path.Combine(projectRoot, "FEBuilderGBA.Avalonia");

            var exemptFiles = new[]
            {
                Path.Combine(avaloniaDir, "Controls", "BitFlagPanel.axaml"),
                Path.Combine(avaloniaDir, "Controls", "AddressListControl.axaml"),
                Path.Combine(avaloniaDir, "Controls", "GbaImageControl.axaml"),
                Path.Combine(avaloniaDir, "Controls", "IconPreviewControl.axaml"),
                Path.Combine(avaloniaDir, "App.axaml")
            };

            foreach (var file in exemptFiles)
            {
                if (!File.Exists(file))
                {
                    _output.WriteLine($"  Skipping (not found): {file}");
                    continue;
                }

                string content = File.ReadAllText(file);
                bool hasAutomationId = content.Contains("AutomationProperties.AutomationId");
                _output.WriteLine($"  {Path.GetFileName(file)}: hasAutomationId={hasAutomationId}");

                Assert.False(hasAutomationId,
                    $"Exempt file {Path.GetFileName(file)} should NOT contain AutomationProperties.AutomationId");
            }
        }
    }
}
