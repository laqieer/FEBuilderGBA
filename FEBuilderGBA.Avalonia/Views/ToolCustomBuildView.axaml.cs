using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Custom Build tool (WF <c>ToolCustomBuildForm</c>). Runs a custom build pipeline
    /// for the project — either a user CUSTOM_BUILD.cmd batch script (CMD method,
    /// Windows-only) or an Event Assembler target (EA method) — and loads the
    /// freshly-built ROM via the GUI-free Core helper <see cref="CustomBuildCore"/>
    /// (which reuses <see cref="EventAssemblerCompileCore"/> for the EA path).
    ///
    /// Combo items are added in code via R._() so they pick up ja/zh translations
    /// (ViewTranslationHelper does not translate ComboBoxItem content).
    ///
    /// "Marge and Update" (issue #1248) runs the build, diffs the built ROM against the
    /// vanilla ROM, assembles a CustomBuild patch under
    /// <c>config/patch2/FE8U/skill_CustomBuild</c>, and auto-installs it via
    /// <see cref="PatchInstallCore"/> (the <see cref="ToolCustomBuildViewModel.RunAndMarge"/>
    /// orchestration). Any applied operation can be reverted via Undo.
    /// </summary>
    public partial class ToolCustomBuildView : TranslatedWindow, IEditorView
    {
        readonly ToolCustomBuildViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Custom Build";
        public bool IsLoaded => true;

        public ToolCustomBuildView()
        {
            InitializeComponent();
            DataContext = _vm;

            // Build methods (default index 0 = auto by extension).
            BuildMethodCombo.Items.Add(R._("Auto (by file extension)"));
            BuildMethodCombo.Items.Add(R._("CUSTOM_BUILD.cmd (Windows batch)"));
            BuildMethodCombo.Items.Add(R._("Event Assembler target"));
            BuildMethodCombo.SelectedIndex = _vm.BuildMethodIndex;

            // Take-over-skill-assignment (WF TakeoverSkillAssignmentComboBox; default
            // index 1 = carry over the parent patch's skill assignment).
            TakeoverSkillCombo.Items.Add(R._("Do not carry over the skill assignment"));
            TakeoverSkillCombo.Items.Add(R._("Carry over the skill assignment"));
            TakeoverSkillCombo.SelectedIndex = _vm.TakeoverSkillAssignmentIndex;

            Opened += (_, _) =>
            {
                // Prefill the original-ROM field from the current ROM, like the WF form.
                _vm.PrefillOriginalRom();

                // Surface a clear up-front note when the CMD build path is unavailable
                // on this OS (a .cmd batch script is Windows-only).
                if (!CustomBuildCore.IsWindows)
                    _vm.StatusMessage = CustomBuildCore.GetCmdWindowsOnlyMessage();
            };
        }

        async void BrowseTarget_Click(object? sender, RoutedEventArgs e)
        {
            await BrowseForTargetAsync();
        }

        /// <summary>
        /// Show the build-target picker and store the chosen path on the VM. Returns true
        /// if the user picked a usable file. Awaitable so Run can pick-then-continue in the
        /// same invocation (mirrors WF RunButton auto-run when a file is dropped).
        /// </summary>
        async Task<bool> BrowseForTargetAsync()
        {
            var storage = GetTopLevel(this)?.StorageProvider;
            if (storage == null) return false;

            try
            {
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Please select a build target."),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType(R._("build target"))
                            { Patterns = new[] { "*.cmd", "*.event", "*.txt" } },
                        new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } },
                    }
                });

                if (files.Count > 0)
                {
                    string? path = files[0].TryGetLocalPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        _vm.TargetPath = path;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolCustomBuildView.BrowseTarget failed: " + ex.ToString());
                _vm.StatusMessage = ex.Message;
            }
            return false;
        }

        async void BrowseOriginalRom_Click(object? sender, RoutedEventArgs e)
        {
            var storage = GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            try
            {
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Please select the un-modded base ROM."),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType(R._("GBA ROM")) { Patterns = new[] { "*.gba" } },
                        new FilePickerFileType(R._("Binary file")) { Patterns = new[] { "*.bin" } },
                        new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } },
                    }
                });

                if (files.Count > 0)
                {
                    string? path = files[0].TryGetLocalPath();
                    if (!string.IsNullOrEmpty(path))
                        _vm.OriginalRomPath = path;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolCustomBuildView.BrowseOriginalRom failed: " + ex.ToString());
                _vm.StatusMessage = ex.Message;
            }
        }

        async void Run_Click(object? sender, RoutedEventArgs e)
        {
            // Prompt for a target if none chosen yet, then CONTINUE the run in the same
            // action once a file is picked (mirrors WF RunButton auto-run).
            if (!_vm.TargetExists)
            {
                if (!await BrowseForTargetAsync() || !_vm.TargetExists)
                    return; // user cancelled / no usable file
            }
            if (!_vm.OriginalRomExists)
            {
                _vm.StatusMessage = R._("無改造ROM({0})が見つかりませんでした", _vm.OriginalRomPath);
                return;
            }

            RunButton.IsEnabled = false;
            _vm.StatusMessage = R._("Building...");

            // Use an EXPLICIT UndoData passed through to the Core helper rather than the
            // thread-local ambient ROM.BeginUndoScope: the build runs on a background
            // thread (Task.Run), and the ambient scope is thread-local to the UI thread.
            // The Core helper records diffs directly into this passed UndoData, so undo
            // capture stays correct and thread-consistent. We push it (UI thread) only
            // after a successful build, via UndoService.CommitExternal which also
            // refreshes the dirty bit.
            var undo = (CoreState.Undo ??= new Undo()).NewUndoData("Custom Build");

            try
            {
                // A custom build can take several seconds — run off the UI thread.
                var result = await Task.Run(() => _vm.Run(undo));

                if (result.Success)
                {
                    bool mutated = undo.list.Count > 0;
                    if (mutated && _undoService.CommitExternal(undo))
                        _vm.CanUndo = true;

                    string msg = R._("Build successful.");
                    if (!string.IsNullOrEmpty(result.BuiltRomPath))
                        msg += "\r\n" + R._("Built ROM: {0}", result.BuiltRomPath);
                    if (!string.IsNullOrEmpty(result.SymbolText.Trim()))
                        msg += "\r\n" + R._("Symbols loaded.");
                    if (!string.IsNullOrEmpty(result.Output.Trim()))
                        msg += "\r\n" + result.Output.Trim();
                    _vm.StatusMessage = msg;
                }
                else
                {
                    // Build failed → nothing was applied (fault-safe helper), so there is
                    // nothing to undo; just surface the error.
                    _vm.StatusMessage = R._("Build failed.") + "\r\n" + result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolCustomBuildView.Run failed: " + ex.ToString());
                _vm.StatusMessage = R._("Build failed.") + "\r\n" + ex.ToString();
            }
            finally
            {
                RunButton.IsEnabled = true;
            }
        }

        async void MargeUpdate_Click(object? sender, RoutedEventArgs e)
        {
            // Prompt for a target if none chosen yet, then CONTINUE in the same action.
            if (!_vm.TargetExists)
            {
                if (!await BrowseForTargetAsync() || !_vm.TargetExists)
                    return; // user cancelled / no usable file
            }
            if (!_vm.OriginalRomExists)
            {
                _vm.StatusMessage = R._("無改造ROM({0})が見つかりませんでした", _vm.OriginalRomPath);
                return;
            }

            RunButton.IsEnabled = false;
            MargeUpdateButton.IsEnabled = false;
            // Disable Undo during the background build+install — a click mid-mutation
            // would race the bg-thread CoreState.Undo/ROM writes and corrupt state.
            // Restored to its prior state in the finally below.
            bool undoWasEnabled = UndoButton.IsEnabled;
            UndoButton.IsEnabled = false;
            _vm.StatusMessage = R._("Building...");

            // Same EXPLICIT-UndoData posture as Run_Click: the build AND the patch
            // install run on a background thread (Task.Run), where the thread-local
            // ambient undo scope is null. Both phases record into this passed UndoData,
            // which we commit (UI thread) via UndoService.CommitExternal only on success.
            var undo = (CoreState.Undo ??= new Undo()).NewUndoData("Custom Build (Marge and Update)");

            try
            {
                // No onProgress callback: it would fire on the background thread and a
                // bound StatusMessage mutation must stay on the UI thread (Run_Click uses
                // the same no-progress posture). The final status is set below on the UI
                // thread once the awaited Task completes.
                var (build, marge) = await Task.Run(() => _vm.RunAndMarge(undo));

                if (!build.Success)
                {
                    // Build failed → nothing applied (fault-safe helper); surface the error.
                    _vm.StatusMessage = R._("Build failed.") + "\r\n" + build.ErrorMessage;
                    return;
                }

                if (marge != null && !marge.Success)
                {
                    // The build wrote into the ROM before the marge step failed, so there
                    // ARE recorded writes — commit them so the user can Undo, then report.
                    bool builtMutated = undo.list.Count > 0;
                    if (builtMutated && _undoService.CommitExternal(undo))
                        _vm.CanUndo = true;
                    _vm.StatusMessage = R._("Marge and Update failed.") + "\r\n" + marge.ErrorMessage;
                    return;
                }

                bool mutated = undo.list.Count > 0;
                if (mutated && _undoService.CommitExternal(undo))
                    _vm.CanUndo = true;

                string msg = (marge != null)
                    ? R._("Marge and Update successful.")
                    : R._("Build successful.");
                if (marge != null && !string.IsNullOrEmpty(marge.PatchPath))
                    msg += "\r\n" + R._("Patch: {0}", marge.PatchPath);
                else if (!string.IsNullOrEmpty(build.BuiltRomPath))
                    msg += "\r\n" + R._("Built ROM: {0}", build.BuiltRomPath);
                if (!string.IsNullOrEmpty(build.Output.Trim()))
                    msg += "\r\n" + build.Output.Trim();
                _vm.StatusMessage = msg;
            }
            catch (Exception ex)
            {
                Log.Error("ToolCustomBuildView.MargeUpdate failed: " + ex.ToString());
                _vm.StatusMessage = R._("Marge and Update failed.") + "\r\n" + ex.ToString();
            }
            finally
            {
                RunButton.IsEnabled = true;
                MargeUpdateButton.IsEnabled = true;
                // Restore Undo (success or failure); a successful commit above set
                // CanUndo=true which drives IsVisible.
                UndoButton.IsEnabled = undoWasEnabled;
            }
        }

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo == null) return;
                CoreState.Undo.RunUndo();
                _vm.CanUndo = false;
                _vm.StatusMessage = R._("The last operation has been undone.");
            }
            catch (Exception ex)
            {
                Log.Error("ToolCustomBuildView.Undo failed: " + ex.ToString());
                _vm.StatusMessage = ex.Message;
            }
        }

        // This tool has no entry list (it is a build form like EventAssemblerView).
        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
