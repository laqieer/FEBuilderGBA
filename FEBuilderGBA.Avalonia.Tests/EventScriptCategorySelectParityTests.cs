// SPDX-License-Identifier: GPL-3.0-or-later
// Event Script category-picker parity tests (#1443).
//
// Proves the Avalonia "Event Script Category Select" dialog is no longer a
// dead stub: its ViewModel now
//   1. loads REAL categories from config/data/event_category_*.txt (not the 8
//      invented English labels) and SKIPS the WinForms-only {TEMPLATE} category
//      (whose EventTemplateImpl insertion path has no Avalonia consumer);
//   2. lists REAL EventScript.Scripts commands via makeCommandComboText,
//      narrowed by the selected category token + the text filter + the
//      ShowLowCommand toggle;
//   3. returns a chosen EventScript.Script via ConfirmSelection() /
//      SelectedScript.
//
// Mirrors the AIScript / Procs / Text sibling category-picker pattern. The
// ROM-based tests build a real FE8U EventScript so CoreState.EventScript is
// populated with real command templates. Marked [Collection("SharedState")]
// because the suite mutates CoreState.ROM / EventScript / CommentCache /
// BaseDirectory; the env saves and restores each on Dispose.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class EventScriptCategorySelectParityTests
    {
        // ================================================================
        // ViewModel behavior (real FE8U EventScript env).
        // ================================================================

        [Fact]
        public void Load_DoesNotEmitTheFakeHardcodedCategories()
        {
            using var env = new EvDisasmEnv();

            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();
            Assert.True(vm.IsLoaded);

            // The old stub's 8 invented English labels must be gone.
            string[] fakeLabels =
            {
                "Chapter Events", "World Map Events", "Battle Events",
                "Talk Events", "Turn Events", "Location Events", "Misc Events",
            };
            foreach (var fake in fakeLabels)
                Assert.DoesNotContain(fake, vm.Categories);
        }

        [Fact]
        public void Load_LoadsRealCategoriesFromConfig()
        {
            using var env = new EvDisasmEnv();

            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();

            // Real FE8 event_category_FE8.en.txt display names.
            Assert.NotEmpty(vm.Categories);
            Assert.Contains("Show all", vm.Categories);
            Assert.Contains("Text", vm.Categories);
        }

        [Fact]
        public void Load_SkipsTemplateCategory_NoDeadCategory()
        {
            using var env = new EvDisasmEnv();

            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();

            // #1443: {TEMPLATE} / "Event Template" is intentionally NOT shown —
            // its EventTemplateImpl insertion path has no Avalonia consumer.
            Assert.DoesNotContain("Event Template", vm.Categories);
            Assert.DoesNotContain(EventScriptCategorySelectViewModel.TemplateCategoryToken, vm.Categories);
        }

        [Fact]
        public void Load_ListsRealScriptCommands_ForShowAll()
        {
            using var env = new EvDisasmEnv();

            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();

            // "Show all" ({}) must yield a non-empty real command list.
            vm.SelectedCategory = "Show all";
            Assert.NotEmpty(vm.ScriptNames);
        }

        [Fact]
        public void CategoryFilter_NarrowsTheCommandList()
        {
            using var env = new EvDisasmEnv();

            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();

            vm.SelectedCategory = "Show all";
            int allCount = vm.ScriptNames.Count;
            Assert.True(allCount > 0);

            // A specific category must list FEWER commands than "Show all"
            // (and still some), proving the {TOKEN} filter is applied.
            vm.SelectedCategory = "Text";
            int textCount = vm.ScriptNames.Count;
            Assert.True(textCount > 0, "Text category should list at least one command");
            Assert.True(textCount < allCount,
                $"Text category ({textCount}) must list fewer than Show all ({allCount})");
        }

        [Fact]
        public void TextFilter_NarrowsTheCommandList()
        {
            using var env = new EvDisasmEnv();

            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();
            vm.SelectedCategory = "Show all";
            int allCount = vm.ScriptNames.Count;
            Assert.True(allCount > 0);

            // A filter that matches at least one command name must narrow the
            // list. Use the first command's leading token as a guaranteed hit.
            string firstName = vm.ScriptNames[0];
            string token = firstName.Split(' ', '(')[0];
            Assert.False(string.IsNullOrWhiteSpace(token));

            vm.FilterText = token;
            Assert.NotEmpty(vm.ScriptNames);
            Assert.True(vm.ScriptNames.Count <= allCount);
            Assert.Contains(vm.ScriptNames, n => n.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);

            // A junk filter that cannot match must empty the list.
            vm.FilterText = "zzz_no_such_command_zzz";
            Assert.Empty(vm.ScriptNames);
        }

        [Fact]
        public void ConfirmSelection_ReturnsRealScript()
        {
            using var env = new EvDisasmEnv();

            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();
            vm.SelectedCategory = "Show all";
            Assert.NotEmpty(vm.ScriptNames);

            // No selection: refuses, SelectedScript null.
            Assert.False(vm.ConfirmSelection());
            Assert.Null(vm.SelectedScript);

            // Select index 0: SelectedScript resolves to a real Script and
            // ConfirmSelection() returns true.
            vm.SelectedScriptIndex = 0;
            Assert.NotNull(vm.SelectedScript);
            Assert.True(vm.ConfirmSelection());
            Assert.NotNull(vm.SelectedScript);

            // The chosen command renders a non-empty combo text.
            string name = EventScript.makeCommandComboText(vm.SelectedScript!, true);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }

        [Fact]
        public void ClearedSelection_DoesNotConfirm()
        {
            using var env = new EvDisasmEnv();

            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();
            vm.SelectedCategory = "Show all";
            Assert.NotEmpty(vm.ScriptNames);

            vm.SelectedScriptIndex = 0;
            Assert.NotNull(vm.SelectedScript);

            vm.SelectedScriptIndex = -1;
            Assert.Null(vm.SelectedScript);
            Assert.False(vm.ConfirmSelection());
        }

        [Fact]
        public void SelectedScript_SetsInfoText()
        {
            using var env = new EvDisasmEnv();

            var vm = new EventScriptCategorySelectViewModel();
            vm.Load();
            vm.SelectedCategory = "Show all";
            Assert.NotEmpty(vm.ScriptNames);

            vm.SelectedScriptIndex = 0;
            Assert.False(string.IsNullOrEmpty(vm.InfoText));
        }

        // ================================================================
        // Fallback path (no config / headless).
        // ================================================================

        [Fact]
        public void Load_NoConfig_FallsBackToAllEvents()
        {
            // No BaseDirectory => config cannot resolve; the VM must still
            // produce a single fallback "All Events" category and not throw.
            string? prevBase = CoreState.BaseDirectory;
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            try
            {
                CoreState.BaseDirectory = null;
                CoreState.ROM = null;
                CoreState.EventScript = null;

                var vm = new EventScriptCategorySelectViewModel();
                vm.Load();

                Assert.True(vm.IsLoaded);
                Assert.Single(vm.Categories);
                Assert.Equal("All Events", vm.Categories[0]);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
            }
        }

        // ================================================================
        // AXAML / code-behind surface (Roslyn-static).
        // ================================================================

        [Fact]
        public void View_HasCategoryFilterScriptInfoSurface()
        {
            string axaml = ReadAxaml();
            Assert.Contains("AutomationId=\"EventScriptCategorySelect_Category_List\"", axaml);
            Assert.Contains("AutomationId=\"EventScriptCategorySelect_Script_List\"", axaml);
            Assert.Contains("AutomationId=\"EventScriptCategorySelect_Filter_Input\"", axaml);
            Assert.Contains("AutomationId=\"EventScriptCategorySelect_Info_Label\"", axaml);
            Assert.Contains("AutomationId=\"EventScriptCategorySelect_ShowLow_Check\"", axaml);
            Assert.Contains("AutomationId=\"EventScriptCategorySelect_OK_Button\"", axaml);
        }

        [Fact]
        public void View_OkReturnsRealScript_NotABareCategoryString()
        {
            string code = ReadCodeBehind();
            // OK confirms a selection and returns the chosen Script.
            Assert.Contains("_vm.ConfirmSelection()", code);
            Assert.Contains("Close(_vm.SelectedScript)", code);
            // The old stub returned the category string — that must be gone.
            Assert.DoesNotContain("Close(_vm.SelectedCategory)", code);
        }

        [Fact]
        public void Picker_IsOpenableStandalone()
        {
            // After #1510 rebuilt EventScriptView into a structural editor with
            // its own command catalog combo, the picker is no longer launched
            // from EventScriptView. It remains a real, openable dialog wired
            // through the window registry + list-parity surface — so the #1443
            // fix (a functional picker that returns a real Script) still stands.
            string repoRoot = FindRepoRoot();

            string mainWindow = File.ReadAllText(Path.Combine(repoRoot,
                "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));
            Assert.Contains("Open<EventScriptCategorySelectView>", mainWindow);

            string listParity = File.ReadAllText(Path.Combine(repoRoot,
                "FEBuilderGBA.Avalonia", "Services", "ListParityHelper.cs"));
            Assert.Contains("EventScriptCategorySelectView", listParity);
        }

        [Fact]
        public void ViewModel_NoLongerHasInventedHardcodedNames()
        {
            string repoRoot = FindRepoRoot();
            string vmPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
                "ViewModels", "EventScriptCategorySelectViewModel.cs");
            string code = File.ReadAllText(vmPath);
            // The invented stub literals must be gone from the source.
            Assert.DoesNotContain("\"Chapter Events\"", code);
            Assert.DoesNotContain("\"World Map Events\"", code);
            Assert.DoesNotContain("\"Talk Events\"", code);
            // Real config prefix must be referenced.
            Assert.Contains("event_category_", code);
        }

        // ================================================================
        // Helpers
        // ================================================================

        static string AxamlPath()
        {
            string repoRoot = FindRepoRoot();
            return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "EventScriptCategorySelectView.axaml");
        }

        static string CodeBehindPath()
        {
            string repoRoot = FindRepoRoot();
            return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "EventScriptCategorySelectView.axaml.cs");
        }

        static string ReadAxaml() => File.ReadAllText(AxamlPath());
        static string ReadCodeBehind() => File.ReadAllText(CodeBehindPath());

        static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
            return dir;
        }

        /// <summary>
        /// Isolated FE8U Event-script environment. Loads a real width-default
        /// FE8 Event EventScript so CoreState.EventScript.Scripts is populated
        /// with real command templates, and points BaseDirectory at the test
        /// output dir so config/data/event_category_FE8.*.txt resolves. Saves
        /// and restores all touched CoreState on Dispose (#1443 review #3).
        /// </summary>
        sealed class EvDisasmEnv : IDisposable
        {
            readonly ROM? _prevRom;
            readonly EventScript? _prevEvent;
            readonly IEtcCache? _prevComment;
            readonly string? _prevBaseDir;

            public ROM Rom { get; }

            public EvDisasmEnv()
            {
                _prevRom = CoreState.ROM;
                _prevEvent = CoreState.EventScript;
                _prevComment = CoreState.CommentCache;
                _prevBaseDir = CoreState.BaseDirectory;

                string asmDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                CoreState.BaseDirectory = asmDir;

                Rom = new ROM();
                Rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U
                CoreState.ROM = Rom;
                CoreState.CommentCache = new HeadlessEtcCache();

                var es = new EventScript();
                es.Load(EventScript.EventScriptType.Event);
                CoreState.EventScript = es;
            }

            public void Dispose()
            {
                CoreState.ROM = _prevRom;
                CoreState.EventScript = _prevEvent;
                CoreState.CommentCache = _prevComment;
                CoreState.BaseDirectory = _prevBaseDir;
            }
        }
    }
}
