// SPDX-License-Identifier: GPL-3.0-or-later
// ToolInitWizardForm gap-sweep parity (#401 / #374).
//
// Code-behind for ToolInitWizardView.axaml. Wires:
//   - 6 language/color buttons (Begin page) -> set _vm.PendingLanguage /
//     PendingColorTheme. Final write deferred to ApplyAll.
//   - 9 Browse buttons -> Avalonia file picker, updates _vm.Pending*Path.
//   - 12 Step{N}Prev/Step{N}Next click handlers -> _vm.GoToPage / StageStep{N}.
//   - 8 download/install buttons (#1031) -> confirmation dialog naming the
//     source URL + elevation note -> DownloadInstallCore under a progress
//     dialog -> Browse-mode validation (File.Exists / GitUtil.ProbeGit) ->
//     stage the resolved path. Windows-only (disabled-with-tooltip elsewhere).
//   - 5 skip buttons (Step2-Step6) -> set Pending Step{N}Mode = DO_NOT_SELECT.
//     Step6 skip ALSO routes through StageStep6() so the IsCompletedThroughStep6
//     gate flips true (otherwise a valid Skip-Git completion would no-op
//     ApplyAll and lose Step1..5 paths — Copilot CLI #583 round-2 review).
//   - 2 Finish actions: Step6 "Finish setup" (after successful StageStep6) +
//     EndPage "Finish" (idempotent re-apply + Close).
//
// This view writes ZERO ROM bytes. The parity test asserts this with
//   Assert.DoesNotContain(".SetU", source)
//   Assert.DoesNotContain(".write_u", source)
// against this file.
using System;
using global::Avalonia;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolInitWizardView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        public EditorDescriptor Descriptor => new("Setup Wizard", 892, 666);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        readonly ToolInitWizardViewModel _vm = new();
        public string ViewTitle => "Setup Wizard";
        public new bool IsLoaded { get; private set; }

        // Set true by the explicit Next/Prev/Skip handlers when they intend
        // to advance via _vm.GoToPage(). Cleared after the selection-change
        // gate has accepted the new tab. SelectionChanged consults this flag
        // to distinguish a programmatic page advance (allowed) from a direct
        // tab-header click (rejected).
        bool _allowProgrammaticPageChange;

        // Last-known-good page index. Updated by NavigateToPage every time
        // it accepts a programmatic change. SelectionChanged uses this as
        // the authoritative source for reverting illegal direct-tab clicks
        // (the TwoWay binding pushes the illegal target into _vm.CurrentPage
        // BEFORE this handler can revert, so we can't rely on the VM's value).
        int _lastValidPageIndex;
        bool _loadedOnce;

        public ToolInitWizardView()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_loadedOnce) return;
            _loadedOnce = true;
            LoadWizard();
        }

        void LoadWizard()
        {
            _vm.Initialize();
            IsLoaded = true;
        }

        /// <summary>
        /// Intercept tab strip clicks. Per Copilot bot review #583 round-3
        /// and round-4: IsHitTestVisible cannot be set on TabItem (it also
        /// disables content). Instead, watch SelectionChanged and revert.
        ///
        /// CRITICAL detail (Copilot CLI #583 round-4 finding): the binding
        /// `SelectedIndex="{Binding CurrentPage, Mode=TwoWay}"` updates the
        /// VM BEFORE this handler fires. So `_vm.CurrentPage` already
        /// equals the illegal target. The fix captures the index of the
        /// previous TabItem from e.RemovedItems and restores BOTH the
        /// TabControl.SelectedIndex AND the VM's CurrentPage via the
        /// gate-aware NavigateToPage helper.
        /// </summary>
        void OnMainTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            if (sender is not TabControl tab) return;

            if (_allowProgrammaticPageChange)
            {
                // The Next/Prev/Skip handler that fired this set both
                // _lastValidPageIndex and _vm.CurrentPage already. Just
                // clear the gate and accept.
                _allowProgrammaticPageChange = false;
                _lastValidPageIndex = tab.SelectedIndex;
                return;
            }

            // SelectionChanged also fires once during initial bind. Ignore
            // any event with no previous selection.
            if (e.RemovedItems == null || e.RemovedItems.Count == 0)
                return;

            // This is a direct user click — revert. The TwoWay binding
            // already pushed the illegal target into _vm.CurrentPage, so
            // restore BOTH the TabControl selection and the VM page from
            // _lastValidPageIndex.
            _allowProgrammaticPageChange = true;
            tab.SelectedIndex = _lastValidPageIndex;
            _vm.CurrentPage = _lastValidPageIndex;
        }

        /// <summary>
        /// Helper used by every Next/Prev/Skip click handler to advance
        /// the wizard programmatically (i.e. through validated state).
        /// Sets the gate flag, calls _vm.GoToPage, and updates
        /// _lastValidPageIndex so SelectionChanged knows the "good" state
        /// for future reverts. Clears the gate if GoToPage was a no-op.
        /// </summary>
        void NavigateToPage(int index)
        {
            _allowProgrammaticPageChange = true;
            bool ok = _vm.GoToPage(index);
            if (ok)
                _lastValidPageIndex = index;
            else
                _allowProgrammaticPageChange = false;
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;

        // ===================================================================
        // BeginPage (page 0) — Language + Color + Start.
        // ===================================================================

        void OnLangEN_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingLanguage = "en";
        }

        void OnLangJP_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingLanguage = "ja";
        }

        void OnLangZH_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingLanguage = "zh";
        }

        void OnWhiteBG_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingColorTheme = 1;
        }

        void OnBlackBG_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingColorTheme = 2;
        }

        void OnBlackBG2_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingColorTheme = 3;
        }

        void OnStartButton_Click(object? sender, RoutedEventArgs e)
        {
            NavigateToPage(1);
        }

        // ===================================================================
        // Step1Page — Emulator.
        // ===================================================================

        void OnStep1Prev_Click(object? sender, RoutedEventArgs e)
        {
            NavigateToPage(0);
        }

        void OnStep1Next_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.Path;
            if (_vm.StageStep1())
                NavigateToPage(2);
            // else: stay on Step1Page so the user can fix the path.
        }

        async void OnRefEmulator_Click(object? sender, RoutedEventArgs e)
        {
            string? picked = await PickExeFileAsync("Select Emulator");
            if (picked != null)
                _vm.PendingEmulatorPath = picked;
        }

        async void OnDownloadVBAM_Click(object? sender, RoutedEventArgs e)
        {
            string? exe = await DownloadSingleAsync(DownloadInstallCore.ResourceId.VbaM);
            if (exe != null)
                _vm.StageDownloadedEmulator(exe);
        }

        async void OnDownloadMGBA_Click(object? sender, RoutedEventArgs e)
        {
            string? exe = await DownloadSingleAsync(DownloadInstallCore.ResourceId.MGba);
            if (exe != null)
                _vm.StageDownloadedEmulator(exe);
        }

        // ===================================================================
        // Step2Page — EA.
        // ===================================================================

        void OnStep2Prev_Click(object? sender, RoutedEventArgs e)
        {
            NavigateToPage(1);
        }

        void OnStep2Next_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep2Mode = ToolInitWizardViewModel.Step2Mode_Enum.Path;
            if (_vm.StageStep2())
                NavigateToPage(3);
        }

        async void OnRefEA_Click(object? sender, RoutedEventArgs e)
        {
            string? picked = await PickExeFileAsync("Select Event Assembler");
            if (picked != null)
                _vm.PendingEAPath = picked;
        }

        async void OnDownloadEA_Click(object? sender, RoutedEventArgs e)
        {
            // Bundled: Event Assembler (Core.exe) + lyn.exe, all-or-none.
            // EA is hosted on Dropbox and is therefore best-effort — on the
            // (likely) Dropbox failure the user is told to use Browse.
            string[] resolved = await DownloadBundleAsync(
                "Event Assembler",
                new[] { DownloadInstallCore.ResourceId.EA, DownloadInstallCore.ResourceId.Lyn });
            if (resolved != null)
            {
                // resolved[0] = Core.exe (the EA path persisted); lyn is a
                // sibling tool placed alongside, not a config key.
                _vm.StageDownloadedEA(resolved[0]);
            }
        }

        void OnSkipStep2_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep2Mode = ToolInitWizardViewModel.Step2Mode_Enum.DoNotSelect;
            NavigateToPage(3);
        }

        // ===================================================================
        // Step3Page — Sappy / VGMS.
        // ===================================================================

        void OnStep3Prev_Click(object? sender, RoutedEventArgs e)
        {
            NavigateToPage(2);
        }

        void OnStep3Next_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep3Mode = ToolInitWizardViewModel.Step3Mode_Enum.Path;
            if (_vm.StageStep3())
                NavigateToPage(4);
        }

        async void OnRefSappy_Click(object? sender, RoutedEventArgs e)
        {
            string? picked = await PickExeFileAsync("Select Sappy");
            if (picked != null)
                _vm.PendingSappyPath = picked;
        }

        async void OnDownloadSappy_Click(object? sender, RoutedEventArgs e)
        {
            // Sappy is hosted on Dropbox — best-effort; Browse fallback on fail.
            string? exe = await DownloadSingleAsync(DownloadInstallCore.ResourceId.Sappy);
            if (exe != null)
                _vm.StageDownloadedSappy(exe);
        }

        async void OnDownloadVGMusicStudio_Click(object? sender, RoutedEventArgs e)
        {
            // VG Music Studio (DirectPlayS) is GitHub-hosted and robust. WF also
            // side-downloads Sappy here, but that is Dropbox-brittle, so it is
            // best-effort only and does NOT fail the VGMS install.
            string? exe = await DownloadSingleAsync(DownloadInstallCore.ResourceId.GbaMusicStudio);
            if (exe != null)
                _vm.StageDownloadedSappy(exe);
        }

        void OnSkipStep3_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep3Mode = ToolInitWizardViewModel.Step3Mode_Enum.DoNotSelect;
            NavigateToPage(4);
        }

        // ===================================================================
        // Step4Page — Debugger + ASM.
        // ===================================================================

        void OnStep4Prev_Click(object? sender, RoutedEventArgs e)
        {
            NavigateToPage(3);
        }

        void OnStep4Next_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep4Mode = ToolInitWizardViewModel.Step4Mode_Enum.Path;
            if (_vm.StageStep4())
                NavigateToPage(5);
        }

        async void OnRefDebugger_Click(object? sender, RoutedEventArgs e)
        {
            string? picked = await PickExeFileAsync("Select Debugger");
            if (picked != null)
                _vm.PendingDebuggerPath = picked;
        }

        async void OnRefASM_Click(object? sender, RoutedEventArgs e)
        {
            string? picked = await PickExeFileAsync("Select Assembler");
            if (picked != null)
                _vm.PendingASMPath = picked;
        }

        async void OnDownloadASM_Click(object? sender, RoutedEventArgs e)
        {
            // Bundled: no$gba (debugger) + arm-none-eabi-as (assembler), all-or-none.
            string[] resolved = await DownloadBundleAsync(
                "no$gba + arm-none-eabi-as",
                new[] { DownloadInstallCore.ResourceId.NoGba, DownloadInstallCore.ResourceId.ArmAs });
            if (resolved != null)
                _vm.StageDownloadedDebuggerAndASM(resolved[0], resolved[1]);
        }

        void OnSkipStep4_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep4Mode = ToolInitWizardViewModel.Step4Mode_Enum.DoNotSelect;
            NavigateToPage(5);
        }

        // ===================================================================
        // Step5Page — Music tools (gba_mus_riper / sox / midfix4agb).
        // ===================================================================

        void OnStep5Prev_Click(object? sender, RoutedEventArgs e)
        {
            NavigateToPage(4);
        }

        void OnStep5Next_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep5Mode = ToolInitWizardViewModel.Step5Mode_Enum.Path;
            if (_vm.StageStep5())
                NavigateToPage(6);
        }

        async void OnRefGbaMusRiper_Click(object? sender, RoutedEventArgs e)
        {
            string? picked = await PickExeFileAsync("Select gba_mus_riper");
            if (picked != null)
                _vm.PendingGbaMusRiperPath = picked;
        }

        async void OnRefSox_Click(object? sender, RoutedEventArgs e)
        {
            string? picked = await PickExeFileAsync("Select sox");
            if (picked != null)
                _vm.PendingSoxPath = picked;
        }

        async void OnRefMidfix4agb_Click(object? sender, RoutedEventArgs e)
        {
            string? picked = await PickExeFileAsync("Select midfix4agb");
            if (picked != null)
                _vm.PendingMidfix4agbPath = picked;
        }

        async void OnDownloadMusicTool_Click(object? sender, RoutedEventArgs e)
        {
            // Bundled: gba_mus_riper + sox + midfix4agb, all-or-none.
            string[] resolved = await DownloadBundleAsync(
                "gba_mus_riper + sox + midfix4agb",
                new[]
                {
                    DownloadInstallCore.ResourceId.GbaMusRiper,
                    DownloadInstallCore.ResourceId.Sox,
                    DownloadInstallCore.ResourceId.Midfix4agb,
                });
            if (resolved != null)
                _vm.StageDownloadedMusicTools(resolved[0], resolved[1], resolved[2]);
        }

        void OnSkipStep5_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep5Mode = ToolInitWizardViewModel.Step5Mode_Enum.DoNotSelect;
            NavigateToPage(6);
        }

        // ===================================================================
        // Step6Page — Git.
        // ===================================================================

        void OnStep6Prev_Click(object? sender, RoutedEventArgs e)
        {
            NavigateToPage(5);
        }

        void OnStep6Next_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep6Mode = ToolInitWizardViewModel.Step6Mode_Enum.Path;
            if (_vm.StageStep6())
            {
                _vm.ApplyAll();
                NavigateToPage(8); // jump straight to EndPage
            }
        }

        async void OnRefGit_Click(object? sender, RoutedEventArgs e)
        {
            string? picked = await PickExeFileAsync("Select git");
            if (picked != null)
                _vm.PendingGitPath = ToolInitWizardViewModel.ProbeGitOrFindFallback(picked);
        }

        async void OnDownloadGit_Click(object? sender, RoutedEventArgs e)
        {
            if (!GuardWindowsOnly())
                return;

            // Resolve the actual installer URL BEFORE the confirmation so the
            // dialog can name the real source. If it can't be resolved, name the
            // releases API source instead (the Core helper also re-checks).
            string installerUrl = await Task.Run(() => GitInstaller.GetLatestInstallerUrl());
            string source = string.IsNullOrEmpty(installerUrl)
                ? DownloadInstallCore.GitSourceLabel
                : installerUrl;

            bool ok = await ConfirmDownloadAsync(
                FEBuilderGBA.R._("Download and install Git"),
                FEBuilderGBA.R._(
                    "This will download the official Git for Windows installer from:\n{0}\n\nIt will RUN the installer, which may request administrator (UAC) elevation.\n\nContinue?",
                    source));
            if (!ok)
                return;

            string? gitPath = null;
            string error = "";
            try
            {
                await ProgressDialogService.RunWithProgress(this,
                    FEBuilderGBA.R._("Download and install Git"),
                    async (progress, _) =>
                    {
                        var gitResult = await DownloadInstallCore.DownloadGitAsync(
                            msg => progress.Report(new ProgressInfo { Message = msg, PercentComplete = -1 }));
                        gitPath = gitResult.Path;
                        if (gitPath == null)
                            error = gitResult.Error;
                    });
            }
            catch (Exception ex)
            {
                gitPath = null;
                error = ex.Message;
            }

            if (gitPath == null)
            {
                ShowDownloadError(string.IsNullOrEmpty(error)
                    ? FEBuilderGBA.R._("Git download/install failed. Use Browse to set the path manually.")
                    : error);
                return;
            }

            // Browse-mode validation: Step 6 uses GitUtil.ProbeGit (NOT File.Exists).
            if (!GitUtil.ProbeGit(gitPath))
            {
                ShowDownloadError(FEBuilderGBA.R._(
                    "Git was installed but failed validation. Use Browse to set the path manually."));
                return;
            }

            _vm.StageDownloadedGit(gitPath);
            _vm.SettingStatus = FEBuilderGBA.R._("Git installed: {0}", gitPath);
        }

        void OnSkipStep6_Click(object? sender, RoutedEventArgs e)
        {
            // Per Copilot CLI #583 re-review: route Skip through StageStep6()
            // so the IsCompletedThroughStep6 gate gets set (DoNotSelect mode
            // is valid for completion). Without this, ApplyAll() would be a
            // no-op and previously-staged Step1..5 settings would be lost.
            _vm.PendingStep6Mode = ToolInitWizardViewModel.Step6Mode_Enum.DoNotSelect;
            if (_vm.StageStep6())
            {
                _vm.ApplyAll();
                NavigateToPage(8);
            }
        }

        // ===================================================================
        // EndPage — Finish.
        // ===================================================================

        void OnEndButton_Click(object? sender, RoutedEventArgs e)
        {
            // ApplyAll is idempotent — calling it again on Finish guarantees
            // the wizard state is persisted even if the user reached EndPage
            // by other paths.
            _vm.ApplyAll();
            RequestClose();
        }

        // ===================================================================
        // EndPage — optional FEMapCreator setup row (#1978 Slice 4).
        //
        // Neither handler below is invoked from construction, page display,
        // or Finish — only an explicit user click reaches this code. Neither
        // launches/discovers/searches PATH for FEMapCreator itself, and
        // neither mutates FEMapCreator config; the actual executable/assets
        // path form and per-fingerprint discovery/mapping action live only in
        // Options (Plan v4 §4/§7), never duplicated here.
        // ===================================================================

        /// <summary>
        /// Opens the fixed upstream FEMapCreator project/setup page in the
        /// user's system browser. This only starts the OS browser at a fixed
        /// URL — it never downloads, installs, or executes FEMapCreator
        /// itself, and this repository never fetches/vendors that page's
        /// content.
        /// </summary>
        void OnOpenFEMapCreatorProjectPage_Click(object? sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/laqieer/FEMapCreator") { UseShellExecute = true }); }
            catch (Exception ex) { FEBuilderGBA.Log.ErrorF("ToolInitWizardView.OnOpenFEMapCreatorProjectPage_Click launch browser: {0}", ex.Message); }
        }

        /// <summary>
        /// Navigates to the existing Options FEMapCreator section — the sole
        /// owner (Plan v4 §4/§7, #1978 Slice 3 review finding #5) of
        /// executable/assets-root setup, explicit discovery, and
        /// per-current-fingerprint mapping. This wizard has no loaded map, so
        /// no fingerprint context is passed; Options honestly explains that a
        /// Map Editor shortcut is needed for per-map mapping rather than
        /// fabricating one.
        /// </summary>
        void OnOpenFEMapCreatorOptions_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<OptionsView>();
        }

        // ===================================================================
        // Download helpers (#1031).
        // ===================================================================

        /// <summary>
        /// Defensive Windows-only guard. The buttons are already disabled on
        /// non-Windows via DownloadEnabledOnThisPlatform, but a click handler
        /// could still be raised (e.g. by a test or an accessibility tool), so
        /// re-check here and route the user to Browse.
        /// </summary>
        bool GuardWindowsOnly()
        {
            if (_vm.IsWindowsPlatform)
                return true;
            ShowDownloadError(_vm.NonWindowsDownloadTooltip);
            return false;
        }

        /// <summary>
        /// Show a Yes/No confirmation before any download/run/place. Returns
        /// true if the user confirmed.
        /// </summary>
        async Task<bool> ConfirmDownloadAsync(string title, string message)
        {
            var result = await MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window, message, title, MessageBoxMode.YesNo);
            return result == MessageBoxResult.Yes;
        }

        void ShowDownloadError(string message)
        {
            _vm.SettingStatus = message;
            CoreState.Services?.ShowError(message);
        }

        static string BaseDir => CoreState.BaseDirectory ?? AppContext.BaseDirectory;

        /// <summary>
        /// Build the consent message for one resource, naming the source URL and
        /// noting the executable download.
        /// </summary>
        static string SingleConsentMessage(DownloadInstallCore.DownloadSpec spec)
        {
            return FEBuilderGBA.R._(
                "This will download an executable from:\n{0}\n\nThe downloaded program will be placed under the app folder.\n\nContinue?",
                spec.Url);
        }

        /// <summary>
        /// Confirm + download a SINGLE resource under a progress dialog, then
        /// validate the resolved exe with the same Browse-mode File.Exists check.
        /// Returns the resolved exe path on success, or null (error already
        /// surfaced) on cancel/failure.
        /// </summary>
        async Task<string?> DownloadSingleAsync(DownloadInstallCore.ResourceId id)
        {
            if (!GuardWindowsOnly())
                return null;

            var spec = DownloadInstallCore.GetSpec(id);
            bool ok = await ConfirmDownloadAsync(
                FEBuilderGBA.R._("Download"), SingleConsentMessage(spec));
            if (!ok)
                return null;

            string? resolved = null;
            string error = "";
            try
            {
                await ProgressDialogService.RunWithProgress(this,
                    FEBuilderGBA.R._("Downloading"),
                    (progress, _) => Task.Run(() =>
                    {
                        resolved = DownloadInstallCore.Download(id, BaseDir,
                            msg => progress.Report(new ProgressInfo { Message = msg, PercentComplete = -1 }),
                            out error);
                    }));
            }
            catch (Exception ex)
            {
                resolved = null;
                error = ex.Message;
            }

            if (resolved == null || !System.IO.File.Exists(resolved))
            {
                ShowDownloadError(string.IsNullOrEmpty(error)
                    ? FEBuilderGBA.R._("Download failed. Use Browse to set the path manually.")
                    : error);
                return null;
            }

            _vm.SettingStatus = FEBuilderGBA.R._("Downloaded: {0}", resolved);
            return resolved;
        }

        /// <summary>
        /// Confirm + download a BUNDLE of resources all-or-none under a single
        /// progress dialog. Each sub-resource downloads + validates (File.Exists)
        /// to its own staging/target; if ANY fails, NOTHING is staged (the
        /// caller does not touch config). Returns the resolved exe paths (one per
        /// id, in order) on full success, or null on cancel/any-failure.
        /// </summary>
        async Task<string[]?> DownloadBundleAsync(
            string bundleLabel, DownloadInstallCore.ResourceId[] ids)
        {
            if (!GuardWindowsOnly())
                return null;

            // Consent names every source URL in the bundle.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(FEBuilderGBA.R._(
                "This will download executables for {0} from:", bundleLabel));
            foreach (var id in ids)
                sb.AppendLine("  " + DownloadInstallCore.GetSpec(id).Url);
            sb.AppendLine();
            sb.AppendLine(FEBuilderGBA.R._(
                "The downloaded programs will be placed under the app folder.\n\nContinue?"));

            bool ok = await ConfirmDownloadAsync(
                FEBuilderGBA.R._("Download"), sb.ToString());
            if (!ok)
                return null;

            string[]? results = null;
            string error = "";
            try
            {
                await ProgressDialogService.RunWithProgress(this,
                    FEBuilderGBA.R._("Downloading"),
                    (progress, _) => Task.Run(() =>
                    {
                        // True all-or-none (Copilot #1102): STAGE every member to
                        // its own temp dir first (places NOTHING into the app
                        // folder), then CommitBundle commits them transactionally —
                        // if any member's commit fails, every already-committed
                        // member is rolled back. A failure at ANY phase therefore
                        // leaves NO partial install.
                        var staged = new List<DownloadInstallCore.StagedDownload>();
                        try
                        {
                            foreach (var id in ids)
                            {
                                var s = DownloadInstallCore.Stage(id, BaseDir,
                                    msg => progress.Report(new ProgressInfo { Message = msg, PercentComplete = -1 }),
                                    out error);
                                if (s == null)
                                    return; // results stays null; finally disposes staged
                                staged.Add(s);
                            }

                            // All staged OK — commit every member atomically.
                            results = DownloadInstallCore.CommitBundle(staged, ref error);
                        }
                        finally
                        {
                            foreach (var s in staged)
                                s.Dispose();
                        }
                    }));
            }
            catch (Exception ex)
            {
                results = null;
                error = ex.Message;
            }

            if (results == null)
            {
                ShowDownloadError(string.IsNullOrEmpty(error)
                    ? FEBuilderGBA.R._("Download failed. Use Browse to set the path manually.")
                    : error);
                return null;
            }

            _vm.SettingStatus = FEBuilderGBA.R._("Downloaded {0} tool(s).", results.Length);
            return results;
        }

        // ===================================================================
        // Helpers.
        // ===================================================================

        /// <summary>
        /// Avalonia file picker. Returns the local path of the chosen file,
        /// or null if cancelled / no local path. Matches the pattern used by
        /// OptionsView.BrowseFile_Click — file-type labels and title are
        /// localised via R._() so the dialog reads correctly in ja/zh.
        /// </summary>
        async System.Threading.Tasks.Task<string?> PickExeFileAsync(string title)
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null)
                return null;
            var allFiles = new FilePickerFileType(FEBuilderGBA.R._("All Files")) { Patterns = new[] { "*" } };
            var exeFiles = new FilePickerFileType(FEBuilderGBA.R._("Executables")) { Patterns = new[] { "*.exe", "*" } };
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = FEBuilderGBA.R._(title),
                AllowMultiple = false,
                FileTypeFilter = new[] { exeFiles, allFiles },
            });
            // #1639: this picks an EXECUTABLE whose path is later launched as an
            // external process (devkitARM / EA). External tools need a real
            // filesystem path and have no meaning under Android scoped storage,
            // so the path-only result is intentional here — a SAF pick (no local
            // path) simply leaves the configured tool path unchanged.
            if (files.Count > 0)
                return files[0].TryGetLocalPath();
            return null;
        }
    }
}
