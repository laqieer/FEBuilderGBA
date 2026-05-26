// SPDX-License-Identifier: GPL-3.0-or-later
namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Source-code verification for the unified Avalonia editor top-bar control
    /// (issue #649). These tests mirror the verification style of
    /// AvaloniaEditorTests — they grep the AXAML / code-behind source so we
    /// can assert structural invariants without spinning up Avalonia from a
    /// xUnit test harness (which is fragile across CI environments).
    /// </summary>
    public class EditorTopBarTests
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

        private string AvaloniaDir => Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia");

        // -----------------------------------------------------------------
        // EditorTopBar control itself
        // -----------------------------------------------------------------

        [Fact]
        public void EditorTopBar_FilesExist()
        {
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml")));
            Assert.True(File.Exists(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml.cs")));
        }

        [Fact]
        public void EditorTopBar_ExposesDisplayTextProperties()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml.cs"));
            Assert.Contains("StartAddressTextProperty", src);
            Assert.Contains("ReadCountTextProperty", src);
            Assert.Contains("SizeTextProperty", src);
            Assert.Contains("FilterTextProperty", src);
        }

        [Fact]
        public void EditorTopBar_ExposesSlotVisibilityProperties()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml.cs"));
            Assert.Contains("ShowStartAddressProperty", src);
            Assert.Contains("ShowReadCountProperty", src);
            Assert.Contains("ShowSizeProperty", src);
            Assert.Contains("ShowFilterProperty", src);
            Assert.Contains("ShowReloadProperty", src);
        }

        [Fact]
        public void EditorTopBar_ExposesLabelOverrideProperties()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml.cs"));
            Assert.Contains("StartAddressLabelProperty", src);
            Assert.Contains("ReadCountLabelProperty", src);
            Assert.Contains("SizeLabelProperty", src);
            Assert.Contains("FilterLabelProperty", src);
            Assert.Contains("ReloadButtonTextProperty", src);
        }

        [Fact]
        public void EditorTopBar_ExposesAutomationIdOverrides()
        {
            // Back-compat: each inner control can take an explicit AutomationId
            // so a migrated host can preserve its legacy ids (e.g. UnitEditor_Reload_Button).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml.cs"));
            Assert.Contains("StartAddressAutomationIdProperty", src);
            Assert.Contains("ReadCountAutomationIdProperty", src);
            Assert.Contains("SizeAutomationIdProperty", src);
            Assert.Contains("FilterAutomationIdProperty", src);
            Assert.Contains("ReloadAutomationIdProperty", src);
        }

        [Fact]
        public void EditorTopBar_RaisesReloadAndFilterRoutedEvents()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml.cs"));
            Assert.Contains("ReloadRequestedEvent", src);
            Assert.Contains("FilterTextChangedEvent", src);
            // Raise sites
            Assert.Contains("RaiseEvent(new RoutedEventArgs(ReloadRequestedEvent))", src);
            Assert.Contains("RaiseEvent(new EditorTopBarFilterChangedEventArgs(", src);
        }

        [Fact]
        public void EditorTopBar_FilterTextChangedEventArgsExposesOldAndNewText()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml.cs"));
            Assert.Contains("public string NewText", src);
            Assert.Contains("public string OldText", src);
        }

        [Fact]
        public void EditorTopBar_DerivesAutomationIdsFromHostId()
        {
            // The PropagateInnerAutomationIds method should append
            // suffixes when an explicit override isn't set.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml.cs"));
            Assert.Contains("PropagateInnerAutomationIds", src);
            Assert.Contains("_StartAddress_Label", src);
            Assert.Contains("_ReadCount_Label", src);
            Assert.Contains("_Size_Label", src);
            Assert.Contains("_Filter_Input", src);
            Assert.Contains("_Reload_Button", src);
        }

        [Fact]
        public void EditorTopBar_AutomationIdDerivationStripsTopBarSuffix()
        {
            // Idempotent re-attach: "Foo_TopBar" -> baseId "Foo" so derived
            // ids stay stable across visual-tree re-attaches.
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml.cs"));
            Assert.Contains("\"_TopBar\"", src);
            Assert.Contains("EndsWith(\"_TopBar\"", src);
        }

        [Fact]
        public void EditorTopBar_AxamlDefinesFilterStartAddressReadCountSizeReloadSlots()
        {
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml"));
            Assert.Contains("Name=\"FilterSlot\"", src);
            Assert.Contains("Name=\"StartAddressSlot\"", src);
            Assert.Contains("Name=\"ReadCountSlot\"", src);
            Assert.Contains("Name=\"SizeSlot\"", src);
            Assert.Contains("Name=\"ReloadButton\"", src);
        }

        [Fact]
        public void EditorTopBar_FilterSlotHiddenByDefault()
        {
            // Filter is the only slot hidden by default (most editors don't have one).
            var src = File.ReadAllText(Path.Combine(AvaloniaDir, "Controls", "EditorTopBar.axaml"));
            Assert.Contains("Name=\"FilterSlot\"", src);
            // Look for the IsVisible="False" in the FilterSlot block.
            var filterStart = src.IndexOf("Name=\"FilterSlot\"");
            Assert.True(filterStart >= 0);
            // Search within ~400 chars of declaration for IsVisible="False".
            int windowEnd = Math.Min(filterStart + 400, src.Length);
            string block = src.Substring(filterStart, windowEnd - filterStart);
            Assert.Contains("IsVisible=\"False\"", block);
        }

        // -----------------------------------------------------------------
        // Migration: 10 representative editors must use EditorTopBar
        // -----------------------------------------------------------------

        public static IEnumerable<object[]> MigratedEditorAxaml => new[]
        {
            new object[] { "UnitEditorView.axaml" },
            new object[] { "ClassEditorView.axaml" },
            new object[] { "ItemFE6View.axaml" },
            new object[] { "UnitFE6View.axaml" },
            new object[] { "UnitFE7View.axaml" },
            new object[] { "ImagePortraitView.axaml" },
            new object[] { "ClassFE6View.axaml" },
            new object[] { "ImagePortraitFE6View.axaml" },
            new object[] { "ItemEffectivenessViewerView.axaml" },
            new object[] { "ItemPromotionViewerView.axaml" },
        };

        [Theory]
        [MemberData(nameof(MigratedEditorAxaml))]
        public void MigratedEditor_ReferencesEditorTopBar(string axamlFile)
        {
            string path = Path.Combine(AvaloniaDir, "Views", axamlFile);
            Assert.True(File.Exists(path), $"View not found: {path}");
            string src = File.ReadAllText(path);
            Assert.Contains("controls:EditorTopBar", src);
        }

        public static IEnumerable<object[]> MigratedEditorCodeBehind => new[]
        {
            new object[] { "UnitEditorView.axaml.cs" },
            new object[] { "ClassEditorView.axaml.cs" },
            new object[] { "ItemFE6View.axaml.cs" },
            new object[] { "UnitFE6View.axaml.cs" },
            new object[] { "UnitFE7View.axaml.cs" },
            new object[] { "ImagePortraitView.axaml.cs" },
            new object[] { "ClassFE6View.axaml.cs" },
            new object[] { "ImagePortraitFE6View.axaml.cs" },
            new object[] { "ItemEffectivenessViewerView.axaml.cs" },
            new object[] { "ItemPromotionViewerView.axaml.cs" },
        };

        [Theory]
        [MemberData(nameof(MigratedEditorCodeBehind))]
        public void MigratedEditor_WiresOnTopBarReloadRequested(string codeBehindFile)
        {
            string path = Path.Combine(AvaloniaDir, "Views", codeBehindFile);
            Assert.True(File.Exists(path), $"Code-behind not found: {path}");
            string src = File.ReadAllText(path);
            Assert.Contains("OnTopBarReloadRequested", src);
        }

        // -----------------------------------------------------------------
        // Legacy AutomationIds preserved for E2E test back-compat
        // -----------------------------------------------------------------

        [Fact]
        public void UnitEditorView_PreservesReloadButtonAutomationId()
        {
            string src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml"));
            Assert.Contains("ReloadAutomationId=\"UnitEditor_Reload_Button\"", src);
        }

        [Fact]
        public void UnitEditorView_PreservesReadStartAddressAutomationId()
        {
            string src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml"));
            Assert.Contains("StartAddressAutomationId=\"UnitEditor_ReadStartAddress_Label\"", src);
        }

        [Fact]
        public void ClassEditorView_PreservesReloadButtonAutomationId()
        {
            string src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml"));
            Assert.Contains("ReloadAutomationId=\"ClassEditor_Reload_Button\"", src);
        }

        [Fact]
        public void ItemFE6View_PreservesReloadButtonAutomationIdAndFilterId()
        {
            string src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemFE6View.axaml"));
            Assert.Contains("ReloadAutomationId=\"ItemFE6_Reload_Button\"", src);
            Assert.Contains("FilterAutomationId=\"ItemFE6_Filter_Input\"", src);
            Assert.Contains("ShowFilter=\"True\"", src);
        }

        [Fact]
        public void ImagePortraitView_PreservesReloadAndSizeAutomationIds()
        {
            string src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ImagePortraitView.axaml"));
            Assert.Contains("ReloadAutomationId=\"ImagePortrait_ReloadList_Button\"", src);
            Assert.Contains("SizeAutomationId=\"ImagePortrait_BlockSize_Label\"", src);
        }

        // -----------------------------------------------------------------
        // No regressions: migrated editors must not still reference the
        // old per-view top-bar control names. Sanity check that the
        // inline labels were actually removed (not just shadowed).
        // -----------------------------------------------------------------

        [Fact]
        public void UnitEditorView_HasNoStandaloneReadCountLabel()
        {
            // The old <TextBlock Name="ReadCountLabel"> should be gone.
            string src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "UnitEditorView.axaml"));
            Assert.DoesNotContain("Name=\"ReadCountLabel\"", src);
            Assert.DoesNotContain("Name=\"ReadStartAddressLabel\"", src);
        }

        [Fact]
        public void ClassEditorView_HasNoStandaloneReadStartAddressLabel()
        {
            string src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ClassEditorView.axaml"));
            Assert.DoesNotContain("Name=\"ReadStartAddressLabel\"", src);
            Assert.DoesNotContain("Name=\"ReadCountLabel\"", src);
        }

        [Fact]
        public void ImagePortraitView_HasNoStandaloneReadCountLabel()
        {
            string src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ImagePortraitView.axaml"));
            Assert.DoesNotContain("Name=\"ReadCountLabel\"", src);
            Assert.DoesNotContain("Name=\"BlockSizeLabel\"", src);
            Assert.DoesNotContain("Name=\"ReloadListButton\"", src);
        }

        [Fact]
        public void ItemFE6View_HasNoStandaloneFilterBox()
        {
            // Filter is now hosted inside the EditorTopBar; the old standalone
            // FilterBox / ReloadButton should not exist anymore.
            string src = File.ReadAllText(Path.Combine(AvaloniaDir, "Views", "ItemFE6View.axaml"));
            Assert.DoesNotContain("Name=\"FilterBox\"", src);
            Assert.DoesNotContain("Name=\"ReloadButton\"", src);
        }
    }
}
