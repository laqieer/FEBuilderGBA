// SPDX-License-Identifier: GPL-3.0-or-later
// ToolInitWizardForm gap-sweep parity (#401 / #374).
//
// Code-behind for ToolInitWizardView.axaml. Wires:
//   - 6 language/color buttons (Begin page) -> set _vm.PendingLanguage /
//     PendingColorTheme. Final write deferred to ApplyAll.
//   - 9 Browse buttons -> Avalonia file picker, updates _vm.Pending*Path.
//   - 12 Step{N}Prev/Step{N}Next click handlers -> _vm.GoToPage / StageStep{N}.
//   - 8 disabled download/install buttons -> set SettingStatus only
//     (IsEnabled="False" in AXAML; handler is registered defensively).
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
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolInitWizardView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ToolInitWizardViewModel _vm = new();
        public string ViewTitle => "Setup Wizard";
        public bool IsLoaded { get; private set; }

        // Set true by the explicit Next/Prev/Skip handlers when they intend
        // to advance via _vm.GoToPage(). Cleared after the selection-change
        // gate has accepted the new tab. SelectionChanged consults this flag
        // to distinguish a programmatic page advance (allowed) from a direct
        // tab-header click (rejected).
        bool _allowProgrammaticPageChange;

        public ToolInitWizardView()
        {
            InitializeComponent();
            DataContext = _vm;
            Opened += OnOpened;
        }

        void OnOpened(object? sender, EventArgs e)
        {
            _vm.Initialize();
            IsLoaded = true;
        }

        /// <summary>
        /// Intercept tab strip clicks. Per Copilot bot review #583 round-3:
        /// IsHitTestVisible cannot be set on TabItem (it also disables the
        /// content). Instead, watch the TabControl's SelectionChanged event
        /// and revert any change that wasn't initiated by an explicit
        /// Next/Prev/Skip handler. This keeps the wizard's pages reachable
        /// only through validated transitions while leaving all controls
        /// inside each page fully interactive.
        /// </summary>
        void OnMainTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            if (_allowProgrammaticPageChange)
            {
                _allowProgrammaticPageChange = false;
                return;
            }
            // SelectionChanged also fires once during initial bind when
            // CurrentPage = 0 — ignore that.
            if (e.RemovedItems == null || e.RemovedItems.Count == 0)
                return;
            if (sender is not TabControl tab) return;
            // Revert to the previous tab. CurrentPage in the VM is the
            // source of truth — re-applying it cancels the user-initiated
            // click.
            tab.SelectedIndex = _vm.CurrentPage;
        }

        /// <summary>
        /// Helper used by every Next/Prev/Skip click handler to advance
        /// the wizard programmatically (i.e. through validated state).
        /// Sets the gate flag, calls _vm.GoToPage, and clears the flag in
        /// case GoToPage was a no-op.
        /// </summary>
        void NavigateToPage(int index)
        {
            _allowProgrammaticPageChange = true;
            bool ok = _vm.GoToPage(index);
            if (!ok)
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

        void OnDownloadVBAM_Click(object? sender, RoutedEventArgs e)
        {
            // KnownGap: actual download is WinForms-coupled. The button is
            // disabled in AXAML; this handler is registered defensively.
            _vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.DownloadVBAM;
            _vm.SettingStatus = _vm.DownloadDisabledTooltip;
        }

        void OnDownloadMGBA_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.DownloadMGBA;
            _vm.SettingStatus = _vm.DownloadDisabledTooltip;
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

        void OnDownloadEA_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep2Mode = ToolInitWizardViewModel.Step2Mode_Enum.DownloadEA;
            _vm.SettingStatus = _vm.DownloadDisabledTooltip;
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

        void OnDownloadSappy_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep3Mode = ToolInitWizardViewModel.Step3Mode_Enum.DownloadSappy;
            _vm.SettingStatus = _vm.DownloadDisabledTooltip;
        }

        void OnDownloadVGMusicStudio_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep3Mode = ToolInitWizardViewModel.Step3Mode_Enum.DownloadGbaMusicStudio;
            _vm.SettingStatus = _vm.DownloadDisabledTooltip;
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

        void OnDownloadASM_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep4Mode = ToolInitWizardViewModel.Step4Mode_Enum.DownloadBoth;
            _vm.SettingStatus = _vm.DownloadDisabledTooltip;
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

        void OnDownloadMusicTool_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep5Mode = ToolInitWizardViewModel.Step5Mode_Enum.DownloadBoth;
            _vm.SettingStatus = _vm.DownloadDisabledTooltip;
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

        void OnDownloadGit_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PendingStep6Mode = ToolInitWizardViewModel.Step6Mode_Enum.DownloadGit;
            _vm.SettingStatus = _vm.DownloadDisabledTooltip;
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
            this.Close();
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
            var storage = StorageProvider;
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
            if (files.Count > 0)
                return files[0].TryGetLocalPath();
            return null;
        }
    }
}
