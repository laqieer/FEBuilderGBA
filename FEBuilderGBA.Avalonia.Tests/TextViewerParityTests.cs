// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep parity tests for TextViewerView (#404).
//
// Asserts the new widgets added to close the WF-only label backlog (mirrors
// the ClassEditorView parity pattern from PR #570):
//   - ~31 new widgets across 6 tabs (Edit, Search Tools, Import/Export,
//     Translate, References, Conversation Viewer).
//   - Inline Pointer + Refs displays on the Edit tab.
//   - Search Tools tab: address-bar widgets (Read Start Address / Read Count /
//     Size / Filter / Reload), Search Free Area button, status label.
//   - Import/Export tab: filter combo + checkbox + limit text (DISABLED stubs).
//   - Translate tab: from/to combos populated from the shared ToolTranslateROM
//     language arrays + ENABLED Translate button (#947 bug #12).
//   - References tab: cross-references list, Add Reference button (DISABLED),
//     status label.
//   - Empty navigation manifest (no WF-side outgoing jumps wired by this PR).
//
// Density check asserts TextForm reaches Verdict.Low (|delta%| < 25.0) after
// the controls are added. L10n check asserts ja+zh have zero untranslated
// literals for TextViewerView.axaml.
//
// ROM-skippable tests use RomFixture (skipped if no ROM in roms/).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class TextViewerParityTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public TextViewerParityTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
        {
            foreach (var descendant in root.GetLogicalDescendants())
            {
                if (descendant is T candidate)
                {
                    var aid = AutomationProperties.GetAutomationId(candidate);
                    if (aid == automationId) return candidate;
                }
            }
            return null;
        }

        static Control? FindByAutomationIdAny(Control root, string automationId)
        {
            foreach (var descendant in root.GetLogicalDescendants())
            {
                if (descendant is Control c)
                {
                    var aid = AutomationProperties.GetAutomationId(c);
                    if (aid == automationId) return c;
                }
            }
            return null;
        }

        // ============================================================
        // WU1 — Widget existence (per AutomationId)
        // ============================================================

        // ---- Edit tab inline Pointer + Refs displays ----

        [AvaloniaFact]
        public void View_Hosts_Pointer_Label_And_TextBox()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_PointerPrefix_Label"));
            Assert.NotNull(FindByAutomationId<TextBox>(view, "TextViewer_PointerValue_Input"));
        }

        [AvaloniaFact]
        public void View_Hosts_Refs_Label_And_TextBox()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_RefsPrefix_Label"));
            Assert.NotNull(FindByAutomationId<TextBox>(view, "TextViewer_RefsCount_Input"));
        }

        // ---- Search Tools tab ----

        [AvaloniaFact]
        public void View_Hosts_SearchTools_Tab()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_SearchTools_Tab"));
        }

        [AvaloniaFact]
        public void View_Hosts_AddressBar_FilterLabel_AndTextBox()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_FilterPrefix_Label"));
            Assert.NotNull(FindByAutomationId<TextBox>(view, "TextViewer_FilterValue_Input"));
        }

        [AvaloniaFact]
        public void View_Hosts_AddressBar_ReadStartAddress()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_ReadStartAddressPrefix_Label"));
            Assert.NotNull(FindByAutomationId<TextBox>(view, "TextViewer_ReadStartAddress_Input"));
        }

        [AvaloniaFact]
        public void View_Hosts_AddressBar_ReadCount()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_ReadCountPrefix_Label"));
            Assert.NotNull(FindByAutomationId<TextBox>(view, "TextViewer_ReadCount_Input"));
        }

        [AvaloniaFact]
        public void View_Hosts_AddressBar_Size()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_SizePrefix_Label"));
            Assert.NotNull(FindByAutomationId<TextBox>(view, "TextViewer_Size_Input"));
        }

        [AvaloniaFact]
        public void View_Hosts_SearchFreeArea_Button()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationId<Button>(view, "TextViewer_SearchFreeArea_Button"));
        }

        [AvaloniaFact]
        public void View_Hosts_Reload_Button()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationId<Button>(view, "TextViewer_Reload_Button"));
        }

        [AvaloniaFact]
        public void View_Hosts_FreeAreaStatus_Label()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_FreeAreaStatus_Label"));
        }

        // ---- Import/Export tab ----

        [AvaloniaFact]
        public void View_Hosts_ImportExport_Tab()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_ImportExport_Tab"));
        }

        [AvaloniaFact]
        public void View_Hosts_ExportFilter_Combo_DisabledByDefault()
        {
            var view = new TextViewerView();
            var combo = FindByAutomationId<ComboBox>(view, "TextViewer_ExportFilter_Combo");
            Assert.NotNull(combo);
            Assert.False(combo!.IsEnabled);
        }

        [AvaloniaFact]
        public void View_Hosts_ExportFilter_PrefixLabel()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_ExportFilterPrefix_Label"));
        }

        [AvaloniaFact]
        public void View_Hosts_IncludeAIHints_Check_Enabled()
        {
            // #1028 Slice C enabled the "Include AI Hints" checkbox: when checked,
            // the TSV export appends per-entry unit translate-info hints.
            var view = new TextViewerView();
            var check = FindByAutomationId<CheckBox>(view, "TextViewer_IncludeAIHints_Check");
            Assert.NotNull(check);
            Assert.True(check!.IsEnabled);
        }

        [AvaloniaFact]
        public void View_Hosts_ExportLimit_TextBox()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_ExportLimitPrefix_Label"));
            Assert.NotNull(FindByAutomationId<TextBox>(view, "TextViewer_ExportLimit_Input"));
        }

        // ---- Translate tab ----

        [AvaloniaFact]
        public void View_Hosts_Translate_Tab()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_Translate_Tab"));
        }

        // #947 bug #12: the Translate tab is no longer a disabled stub — the
        // combos are enabled + populated from the shared ToolTranslateROM
        // language arrays and the Translate button is enabled.

        [AvaloniaFact]
        public void View_Hosts_TranslateFrom_Combo_EnabledAndPopulated()
        {
            var view = new TextViewerView();
            var combo = FindByAutomationId<ComboBox>(view, "TextViewer_TranslateFrom_Combo");
            Assert.NotNull(combo);
            Assert.True(combo!.IsEnabled);
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_TranslateFromPrefix_Label"));

            // Populated from the SHARED FromLanguageItemsRaw (3 entries) — never
            // a duplicated local list.
            var items = combo.ItemsSource?.Cast<object>().ToList();
            Assert.NotNull(items);
            Assert.Equal(ToolTranslateROMViewModel.FromLanguageItemsRaw.Length, items!.Count);
            Assert.Equal(3, items.Count);
            // Default index resolves to a real selection.
            Assert.InRange(combo.SelectedIndex, 0, items.Count - 1);
        }

        [AvaloniaFact]
        public void View_Hosts_TranslateTo_Combo_EnabledAndPopulated()
        {
            var view = new TextViewerView();
            var combo = FindByAutomationId<ComboBox>(view, "TextViewer_TranslateTo_Combo");
            Assert.NotNull(combo);
            Assert.True(combo!.IsEnabled);
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_TranslateToPrefix_Label"));

            // Populated from the SHARED ToLanguageItemsRaw (11 entries).
            var items = combo.ItemsSource?.Cast<object>().ToList();
            Assert.NotNull(items);
            Assert.Equal(ToolTranslateROMViewModel.ToLanguageItemsRaw.Length, items!.Count);
            Assert.Equal(11, items.Count);
            Assert.InRange(combo.SelectedIndex, 0, items.Count - 1);
        }

        [AvaloniaFact]
        public void View_Hosts_Translate_Button_Enabled()
        {
            var view = new TextViewerView();
            var btn = FindByAutomationId<Button>(view, "TextViewer_Translate_Button");
            Assert.NotNull(btn);
            Assert.True(btn!.IsEnabled);
        }

        [AvaloniaFact]
        public void View_Hosts_TranslateStatus_Label()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_TranslateStatus_Label"));
        }

        [Fact]
        public void TranslateCombos_Use_Shared_RawArrays_With_ParseableCodes()
        {
            // The view MUST reuse the same canonical language arrays as the
            // ROM↔ROM translate tool (single source of truth). Verify the
            // expected counts AND that ParseLanguageKey extracts the expected
            // language code from a sample item's `code=label` prefix.
            Assert.Equal(3, ToolTranslateROMViewModel.FromLanguageItemsRaw.Length);
            Assert.Equal(11, ToolTranslateROMViewModel.ToLanguageItemsRaw.Length);

            Assert.Equal("ja", ToolTranslateROMCore.ParseLanguageKey(ToolTranslateROMViewModel.FromLanguageItemsRaw[0]));
            Assert.Equal("en", ToolTranslateROMCore.ParseLanguageKey(ToolTranslateROMViewModel.FromLanguageItemsRaw[1]));
            Assert.Equal("zh-CN", ToolTranslateROMCore.ParseLanguageKey(ToolTranslateROMViewModel.FromLanguageItemsRaw[2]));

            Assert.Equal("ja", ToolTranslateROMCore.ParseLanguageKey(ToolTranslateROMViewModel.ToLanguageItemsRaw[0]));
            Assert.Equal("zh-TW", ToolTranslateROMCore.ParseLanguageKey(ToolTranslateROMViewModel.ToLanguageItemsRaw[3]));
        }

        [Fact]
        public void TranslateDefaultIndexes_Resolve_InRange()
        {
            // The view selects defaults via the SAME CalcDefaultLanguageIndexes
            // logic. Verify the resolved indexes are valid for both arrays.
            var (from, to) = ToolTranslateROMViewModel.CalcDefaultLanguageIndexes(
                isMultibyte: false, CoreState.TextEncoding, CoreState.Language ?? "en");
            Assert.InRange(from, 0, ToolTranslateROMViewModel.FromLanguageItemsRaw.Length - 1);
            Assert.InRange(to, 0, ToolTranslateROMViewModel.ToLanguageItemsRaw.Length - 1);
        }

        // ---- References tab ----

        [AvaloniaFact]
        public void View_Hosts_References_Tab()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_References_Tab"));
        }

        [AvaloniaFact]
        public void View_Hosts_ReferencesList_AndPrefixLabel()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_ReferencesPrefix_Label"));
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_ReferencesTab_List"));
        }

        // #1028 Slice A: the Add Reference button is now ENABLED and wired to the
        // OnAddReferenceClick handler (opens TextRefAddDialog; persists via the
        // ITextIDCache Core seam). Previously a disabled out-of-scope stub.
        [AvaloniaFact]
        public void View_Hosts_AddReference_Button_EnabledAndWired()
        {
            var view = new TextViewerView();
            var btn = FindByAutomationId<Button>(view, "TextViewer_AddReference_Button");
            Assert.NotNull(btn);
            Assert.True(btn!.IsEnabled);

            // The Click handler must be wired. Avalonia's Button raises Click as a
            // routed event; assert a handler subscription exists via the click
            // command being absent but the routed Click handler attached. We can't
            // read XAML-attached handlers via public API, so assert the handler
            // method exists on the view type (compile-time + reflection guard).
            var handler = typeof(TextViewerView).GetMethod(
                "OnAddReferenceClick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(handler);
        }

        [AvaloniaFact]
        public void View_Hosts_ReferencesTabStatus_Label()
        {
            var view = new TextViewerView();
            Assert.NotNull(FindByAutomationIdAny(view, "TextViewer_ReferencesTabStatus_Label"));
        }

        // ---- #1028 Slice A: TextRefAddDialog Init + GetComment WF blank convention ----

        [Fact]
        public void TextRefAddDialogVm_Init_SeedsIdAndComment_AndLocks()
        {
            var vm = new TextRefAddDialogViewModel();
            vm.Init(0x42, "existing");
            Assert.Equal(0x42, vm.RefId);
            Assert.Equal("existing", vm.Comment);
            Assert.True(vm.IsTextIdLocked);
        }

        [Fact]
        public void TextRefAddDialogVm_GetComment_NewBlankEntry_StoresSingleSpace()
        {
            // WF parity (TextRefAddDialogForm.GetComment): a blank comment on a NEW
            // entry (no original) is kept as a single space so the ref is retained.
            var vm = new TextRefAddDialogViewModel();
            vm.Init(0x1, "");      // new entry, no existing comment
            vm.Comment = "";       // user leaves it blank
            Assert.Equal(" ", vm.GetComment());
        }

        [Fact]
        public void TextRefAddDialogVm_GetComment_ClearExistingEntry_ReturnsEmpty()
        {
            // Clearing an EXISTING entry returns "" so Update removes it (WF parity).
            var vm = new TextRefAddDialogViewModel();
            vm.Init(0x1, "was here"); // existing comment
            vm.Comment = "";          // user clears it
            Assert.Equal("", vm.GetComment());
        }

        [Fact]
        public void TextRefAddDialogVm_GetComment_NonEmpty_PassesThrough()
        {
            var vm = new TextRefAddDialogViewModel();
            vm.Init(0x1, "old");
            vm.Comment = "new comment";
            Assert.Equal("new comment", vm.GetComment());
        }

        // ---- Tab count ----

        [AvaloniaFact]
        public void View_Has_6_TabItems()
        {
            var view = new TextViewerView();
            // Walk the LogicalTree and count TabItem children of the main
            // TabControl named EditorTabs.
            TabControl? tabs = null;
            foreach (var d in view.GetLogicalDescendants())
            {
                if (d is TabControl tc && tc.Name == "EditorTabs") { tabs = tc; break; }
            }
            Assert.NotNull(tabs);
            int tabCount = 0;
            foreach (var item in tabs!.Items)
            {
                if (item is TabItem) tabCount++;
            }
            Assert.Equal(6, tabCount);
        }

        // ============================================================
        // WU2 — Navigation manifest (empty)
        // ============================================================

        [Fact]
        public void View_HasNavigationTargetManifest_With_AddReference_And_BadCharPopup()
        {
            // #1028 Slice A wired the References-tab "Add Reference" modal-dialog
            // jump (OnAddReference -> TextRefAddDialogView). #1028 Slice D wires the
            // bad-character popup jump (OnWriteText -> TextBadCharPopupView) since
            // PatchDetection.SearchAntiHuffmanPatch now exists. The remaining 3 WF
            // jumps stay blocked on Core extractions (see the manifest file).
            var vm = new TextViewerViewModel();
            Assert.IsAssignableFrom<INavigationTargetSource>(vm);
            var targets = ((INavigationTargetSource)vm).GetNavigationTargets();
            Assert.NotNull(targets);
            Assert.Equal(2, targets.Count);

            var addRef = targets.Single(t => t.CommandName == "OnAddReference");
            Assert.Equal(typeof(TextRefAddDialogView), addRef.TargetViewType);
            Assert.Null(addRef.TargetAddress);

            var badChar = targets.Single(t => t.CommandName == "OnWriteText");
            Assert.Equal(typeof(TextBadCharPopupView), badChar.TargetViewType);
            Assert.Null(badChar.TargetAddress);
        }

        // ============================================================
        // WU3 — ResolveTextTableBase + FindApproximatelyUnreferencedTexts
        // ============================================================

        [Fact]
        public void ResolveTextTableBase_NullRom_ReturnsZero()
        {
            // Save & null out CoreState.ROM temporarily.
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new TextViewerViewModel();
                Assert.Equal(0u, vm.ResolveTextTableBase());
            }
            finally
            {
                CoreState.ROM = prev;
            }
        }

        [Fact]
        public void ResolveTextTableBase_ValidPointer_ReturnsDereffedBase()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            var vm = new TextViewerViewModel();
            uint baseAddr = vm.ResolveTextTableBase();
            // Real ROMs always have a valid text pointer that dereferences to a
            // safe ROM offset; the recovery fallback fires only on corrupted
            // pointers. Just verify the base is non-zero and safe.
            Assert.NotEqual(0u, baseAddr);
            Assert.True(U.isSafetyOffset(baseAddr, _fixture.ROM!),
                $"text table base 0x{baseAddr:X08} must be a safe ROM offset");
        }

        [Fact]
        public void FindApproximatelyUnreferencedTexts_RomLoaded_ReturnsList()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            var vm = new TextViewerViewModel();
            var free = vm.FindApproximatelyUnreferencedTexts();
            Assert.NotNull(free);
            // Most ROMs have at least SOME unreferenced text slots (system
            // reserved or unused IDs) — but the strict invariant is that the
            // method never throws and returns a list.
            _output.WriteLine($"Free-area scan returned {free.Count} entries");
        }

        [Fact]
        public void FindApproximatelyUnreferencedTexts_ExcludesReferencedIds()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            // The first unit's name text id IS referenced (by the unit table)
            // so it must NOT appear in the free-area list.
            ROM rom = CoreState.ROM!;
            var info = rom.RomInfo;
            uint unitBase = NameResolver.DerefPointer(rom, info.unit_pointer);
            if (unitBase == 0)
            {
                _output.WriteLine("Unit pointer not safe; skipping.");
                return;
            }
            uint scanLimit = info.unit_maxcount != 0 ? info.unit_maxcount : 0x100u;
            uint unitSize = info.unit_datasize;
            uint knownReferencedTextId = 0;
            for (uint i = 1; i < scanLimit; i++)
            {
                uint entryAddr = unitBase + i * unitSize;
                if (entryAddr + 2 > (uint)rom.Data.Length) break;
                uint tid = rom.u16(entryAddr);
                if (tid != 0) { knownReferencedTextId = tid; break; }
            }
            Assert.NotEqual(0u, knownReferencedTextId);

            var vm = new TextViewerViewModel();
            var free = vm.FindApproximatelyUnreferencedTexts();
            Assert.DoesNotContain(free, ar => ar.tag == knownReferencedTextId);
        }

        [Fact]
        public void FindApproximatelyUnreferencedTexts_AllTagsAreInLoadedRange()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            var vm = new TextViewerViewModel();
            int loadedCount = vm.LoadTextList().Count;
            Assert.True(loadedCount > 0);

            var free = vm.FindApproximatelyUnreferencedTexts();
            foreach (var ar in free)
            {
                Assert.True(ar.tag < (uint)loadedCount,
                    $"Free-area result tag {ar.tag} >= loadedCount {loadedCount} — would break TextList.SelectAddress dispatch");
            }
        }

        [Fact]
        public void FindApproximatelyUnreferencedTexts_AddrMatchesLoadTextListConvention()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            var vm = new TextViewerViewModel();
            uint textBase = vm.ResolveTextTableBase();
            Assert.NotEqual(0u, textBase);

            var free = vm.FindApproximatelyUnreferencedTexts();
            foreach (var ar in free)
            {
                Assert.Equal(textBase + ar.tag * 4u, ar.addr);
            }
        }

        // ============================================================
        // WU5 — Density verdict + l10n coverage
        // ============================================================

        [Fact]
        public void DensityVerdict_TextForm_IsLow()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            var pairs = PairMatcher.DiscoverAll(repoRoot);
            var pair = pairs.FirstOrDefault(p => p.WfFormName == "TextForm");
            Assert.NotNull(pair);

            var row = ControlDensityScanner.Scan(new[] { pair! }, repoRoot).FirstOrDefault();
            Assert.NotNull(row);
            // CI runner where WF Designer.cs unreachable -> skip strict assert
            if (row!.WfControlCount == 0) return;
            Assert.True(
                Math.Abs(row.DeltaPct) < 25.0,
                $"TextForm density delta is {row.DeltaPct:F1}% (WF {row.WfControlCount} / AV {row.AvControlCount}); expected |delta| < 25.0 for Verdict.Low.");
            Assert.Equal(Verdict.Low, row.Verdict);
        }

        [Fact]
        public void L10nCoverage_TextViewerView_HasNoUntranslated_InJaZh()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            // ja+zh only — ko remains a project-wide gap per merged-PR precedent
            // (#406/#407/#409/#410).
            var findings = L10nScanner.Scan(repoRoot, new[] { "ja", "zh" })
                .Where(f => f.AxamlPath.EndsWith("TextViewerView.axaml", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(findings);

            var untranslated = findings.Where(f => f.Verdict == L10nVerdict.Untranslated).ToList();
            Assert.True(
                untranslated.Count == 0,
                "TextViewerView.axaml has untranslated literals in ja+zh:\n" +
                string.Join("\n", untranslated.Select(f => $"  line {f.LineNumber} [{f.AttributeName}]: {f.Literal}")));
        }

        // ============================================================
        // Helpers
        // ============================================================

        static string? FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return null;
        }
    }
}
