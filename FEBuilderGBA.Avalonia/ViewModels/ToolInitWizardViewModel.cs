// SPDX-License-Identifier: GPL-3.0-or-later
// ToolInitWizardForm gap-sweep parity (#401 / #374).
//
// Mirrors WinForms ToolInitWizardForm exactly:
//   - 9 hidden tab pages (BeginPage, Step1Page..Step6Page, SettingNowPage, EndPage)
//   - 6 step-mode enums (Path / Download* / DO_NOT_SELECT)
//   - 9 tool-path fields (Emulator, Debugger, ASM, Sappy, EA, gba_mus_riper, sox,
//     midfix4agb, git)
//   - Path-mode file validation matches WF exactly:
//       Step1 -> File.Exists(Emulator)
//       Step2 -> File.Exists(EA)
//       Step3 -> File.Exists(Sappy)
//       Step4 -> File.Exists(Debugger) AND File.Exists(ASM)
//       Step5 -> File.Exists(gba_mus_riper) AND File.Exists(sox)  -- midfix4agb optional
//       Step6 -> GitUtil.ProbeGit(git)
//
// Per Copilot CLI plan-review v2 #2 (atomic Config writes): all changes are
// staged in Pending* properties; Program.Config is only mutated by the single
// ApplyAll() method called from the Finish button. Cancelling the wizard
// mid-way (or backing out via Prev) leaves Program.Config untouched.
//
// This wizard does NOT write ROM bytes. The parity tests assert
//   - DoesNotContain(".SetU*", source)
//   - DoesNotContain(".write_u*", source)
// against both this ViewModel and the view code-behind.
using System.IO;
using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolInitWizardViewModel : ViewModelBase
    {
        // -------------------------------------------------------------------
        // Step-mode enums (mirrors WF private Step{N}_Enum types).
        // -------------------------------------------------------------------

        public enum Step1Mode_Enum
        {
            Path,
            DownloadVBAM,
            DownloadMGBA,
        }

        public enum Step2Mode_Enum
        {
            Path,
            DownloadEA,
            DoNotSelect,
        }

        public enum Step3Mode_Enum
        {
            Path,
            DownloadSappy,
            DownloadGbaMusicStudio,
            DoNotSelect,
        }

        public enum Step4Mode_Enum
        {
            Path,
            DownloadBoth,
            DoNotSelect,
        }

        public enum Step5Mode_Enum
        {
            Path,
            DownloadBoth,
            DoNotSelect,
        }

        public enum Step6Mode_Enum
        {
            Path,
            DownloadGit,
            DoNotSelect,
        }

        // -------------------------------------------------------------------
        // Pending state. Bound from the view via TwoWay. NOT persisted to
        // Program.Config until ApplyAll() is called.
        // -------------------------------------------------------------------

        string _pendingEmulatorPath = "";
        string _pendingDebuggerPath = "";
        string _pendingASMPath = "";
        string _pendingSappyPath = "";
        string _pendingEAPath = "";
        string _pendingGbaMusRiperPath = "";
        string _pendingSoxPath = "";
        string _pendingMidfix4agbPath = "";
        string _pendingGitPath = "";
        string _pendingLanguage = "";
        int _pendingColorTheme;
        int _currentPage;
        bool _isConfiguring;
        bool _isCompletedThroughStep6;
        string _settingStatus = "";

        Step1Mode_Enum _step1Mode = Step1Mode_Enum.Path;
        Step2Mode_Enum _step2Mode = Step2Mode_Enum.Path;
        Step3Mode_Enum _step3Mode = Step3Mode_Enum.Path;
        Step4Mode_Enum _step4Mode = Step4Mode_Enum.Path;
        Step5Mode_Enum _step5Mode = Step5Mode_Enum.Path;
        Step6Mode_Enum _step6Mode = Step6Mode_Enum.Path;

        public string PendingEmulatorPath
        {
            get => _pendingEmulatorPath;
            set => SetField(ref _pendingEmulatorPath, value ?? "");
        }

        public string PendingDebuggerPath
        {
            get => _pendingDebuggerPath;
            set => SetField(ref _pendingDebuggerPath, value ?? "");
        }

        public string PendingASMPath
        {
            get => _pendingASMPath;
            set => SetField(ref _pendingASMPath, value ?? "");
        }

        public string PendingSappyPath
        {
            get => _pendingSappyPath;
            set => SetField(ref _pendingSappyPath, value ?? "");
        }

        public string PendingEAPath
        {
            get => _pendingEAPath;
            set => SetField(ref _pendingEAPath, value ?? "");
        }

        public string PendingGbaMusRiperPath
        {
            get => _pendingGbaMusRiperPath;
            set => SetField(ref _pendingGbaMusRiperPath, value ?? "");
        }

        public string PendingSoxPath
        {
            get => _pendingSoxPath;
            set => SetField(ref _pendingSoxPath, value ?? "");
        }

        public string PendingMidfix4agbPath
        {
            get => _pendingMidfix4agbPath;
            set => SetField(ref _pendingMidfix4agbPath, value ?? "");
        }

        public string PendingGitPath
        {
            get => _pendingGitPath;
            set => SetField(ref _pendingGitPath, value ?? "");
        }

        public string PendingLanguage
        {
            get => _pendingLanguage;
            set => SetField(ref _pendingLanguage, value ?? "");
        }

        public int PendingColorTheme
        {
            get => _pendingColorTheme;
            set => SetField(ref _pendingColorTheme, value);
        }

        public Step1Mode_Enum PendingStep1Mode
        {
            get => _step1Mode;
            set => SetField(ref _step1Mode, value);
        }

        public Step2Mode_Enum PendingStep2Mode
        {
            get => _step2Mode;
            set => SetField(ref _step2Mode, value);
        }

        public Step3Mode_Enum PendingStep3Mode
        {
            get => _step3Mode;
            set => SetField(ref _step3Mode, value);
        }

        public Step4Mode_Enum PendingStep4Mode
        {
            get => _step4Mode;
            set => SetField(ref _step4Mode, value);
        }

        public Step5Mode_Enum PendingStep5Mode
        {
            get => _step5Mode;
            set => SetField(ref _step5Mode, value);
        }

        public Step6Mode_Enum PendingStep6Mode
        {
            get => _step6Mode;
            set => SetField(ref _step6Mode, value);
        }

        /// <summary>
        /// Drives <c>TabControl.SelectedIndex</c>. Pages 0..8 correspond to
        /// BeginPage, Step1..6, SettingNowPage, EndPage. Setter clamps invalid
        /// indices into [0, 8].
        /// </summary>
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                int clamped = value < 0 ? 0 : (value > 8 ? 8 : value);
                SetField(ref _currentPage, clamped);
            }
        }

        public bool IsConfiguring
        {
            get => _isConfiguring;
            set => SetField(ref _isConfiguring, value);
        }

        /// <summary>
        /// Tracks whether the wizard has successfully advanced through StageStep6.
        /// Per Copilot CLI #583 review finding #3: ApplyAll() is GATED on this
        /// flag so that even if a user reaches the EndPage via some other code
        /// path (e.g. direct CurrentPage mutation, or a future regression),
        /// Config is NOT persisted unless the linear Step1..6 flow actually
        /// completed and each step's File.Exists / GitUtil.ProbeGit validation
        /// passed. Set true only by StageStep6() when it returns true. Cleared
        /// on Initialize() and any GoToPage that goes upstream of Step6.
        /// </summary>
        public bool IsCompletedThroughStep6
        {
            get => _isCompletedThroughStep6;
            set => SetField(ref _isCompletedThroughStep6, value);
        }

        public string SettingStatus
        {
            get => _settingStatus;
            set => SetField(ref _settingStatus, value ?? "");
        }

        /// <summary>
        /// Localised tooltip shown on the 8 disabled download/install buttons.
        /// Explains why the button is disabled and points the user at the
        /// Browse (Path-mode) alternative. KnownGap rationale documented at
        /// the file header.
        /// </summary>
        public string DownloadDisabledTooltip { get; } =
            "Download requires WinForms host. Use Browse instead.";

        // -------------------------------------------------------------------
        // Initialise pending state from Program.Config (read-only).
        // -------------------------------------------------------------------

        public void Initialize()
        {
            IsLoading = true;
            try
            {
                var config = CoreState.Config;
                if (config == null)
                    return;

                PendingEmulatorPath = config.at("emulator");
                PendingDebuggerPath = config.at("emulator2");
                PendingASMPath = config.at("devkitpro_eabi");
                PendingSappyPath = config.at("sappy");
                PendingEAPath = config.at("event_assembler");
                PendingGbaMusRiperPath = config.at("gba_mus_riper");
                PendingSoxPath = config.at("sox");
                PendingMidfix4agbPath = config.at("midfix4agb");
                PendingGitPath = config.at("git_path");
                PendingLanguage = config.at("func_lang", "auto");
                PendingColorTheme = 0; // unset; user picks White / Black / Black2
                CurrentPage = 0;
                IsConfiguring = false;
                IsCompletedThroughStep6 = false;
                SettingStatus = "";
            }
            finally
            {
                IsLoading = false;
                MarkClean();
            }
        }

        // -------------------------------------------------------------------
        // Page navigation.
        // -------------------------------------------------------------------

        /// <summary>
        /// Set <see cref="CurrentPage"/> to the given index. Returns false if
        /// the requested index is out of range [0, 8]; in that case the page
        /// stays clamped (matches the safe-no-op behaviour the view depends on).
        /// </summary>
        public bool GoToPage(int index)
        {
            if (index < 0 || index > 8)
                return false;
            // Per Copilot CLI #583 finding #3: clear the completed-through-Step6
            // flag any time the user navigates upstream of Step 6. Going back
            // forces revalidation on the way forward.
            if (index < 6)
                IsCompletedThroughStep6 = false;
            CurrentPage = index;
            return true;
        }

        // -------------------------------------------------------------------
        // Staging (mirrors WF Step{N}NextButton_Click validation, with the
        // crucial difference that nothing is persisted to Program.Config until
        // ApplyAll() is invoked from the Finish handler).
        // -------------------------------------------------------------------

        public bool StageStep1()
        {
            if (PendingStep1Mode == Step1Mode_Enum.Path)
                return File.Exists(PendingEmulatorPath);
            return true;
        }

        public bool StageStep2()
        {
            if (PendingStep2Mode == Step2Mode_Enum.Path)
                return File.Exists(PendingEAPath);
            return true;
        }

        public bool StageStep3()
        {
            if (PendingStep3Mode == Step3Mode_Enum.Path)
                return File.Exists(PendingSappyPath);
            return true;
        }

        public bool StageStep4()
        {
            if (PendingStep4Mode == Step4Mode_Enum.Path)
            {
                // Matches WF Step4NextButton_Click which requires BOTH.
                return File.Exists(PendingDebuggerPath)
                       && File.Exists(PendingASMPath);
            }
            return true;
        }

        public bool StageStep5()
        {
            if (PendingStep5Mode == Step5Mode_Enum.Path)
            {
                // Matches WF Step5NextButton_Click which checks ONLY
                // gba_mus_riper + sox. midfix4agb is intentionally optional.
                return File.Exists(PendingGbaMusRiperPath)
                       && File.Exists(PendingSoxPath);
            }
            return true;
        }

        public bool StageStep6()
        {
            bool ok;
            if (PendingStep6Mode == Step6Mode_Enum.Path)
            {
                // Matches WF Step6NextButton_Click which calls GitUtil.ProbeGit
                // (NOT File.Exists). ProbeGit runs `git --version` to confirm
                // the executable is functional.
                ok = GitUtil.ProbeGit(PendingGitPath);
            }
            else
            {
                // DownloadGit / DoNotSelect modes are valid for completion.
                ok = true;
            }
            // Per Copilot CLI #583 finding #3: only flag the wizard as
            // "completed through Step 6" when validation actually passed.
            // ApplyAll() reads this flag to defence-in-depth gate the write.
            if (ok)
                IsCompletedThroughStep6 = true;
            return ok;
        }

        // -------------------------------------------------------------------
        // ApplyAll: the ONLY method that persists state to Program.Config.
        // -------------------------------------------------------------------

        /// <summary>
        /// Persist all staged paths and step modes into <c>CoreState.Config</c>.
        /// Invoked from the End button after the wizard runs through all 6
        /// pages. Per the WF semantic, modes set to DO_NOT_SELECT skip the
        /// write for the corresponding key (so a previously-set value is
        /// preserved). KnownGap: actual download/extract/install steps are
        /// skipped here (see file-header comment for rationale).
        /// </summary>
        public void ApplyAll()
        {
            // Per Copilot CLI #583 finding #3: hard-gate Config writes behind
            // IsCompletedThroughStep6. If the user reaches EndPage via some
            // path that DIDN'T complete Step 6 validation, ApplyAll is a
            // no-op. Set true only inside StageStep6() on a successful
            // ProbeGit (or DownloadGit / DoNotSelect mode). The defence is
            // belt-and-braces with TabItem.IsHitTestVisible="False" in AXAML.
            if (!IsCompletedThroughStep6)
            {
                SettingStatus = "Wizard not completed through Step 6 — nothing written.";
                return;
            }

            var config = CoreState.Config;
            if (config == null)
                return;

            IsConfiguring = true;
            try
            {
                // Step 1: Emulator path (no DO_NOT_SELECT option in WF).
                if (PendingStep1Mode == Step1Mode_Enum.Path)
                    config["emulator"] = PendingEmulatorPath;

                // Step 2: EA path.
                if (PendingStep2Mode == Step2Mode_Enum.Path)
                    config["event_assembler"] = PendingEAPath;

                // Step 3: Sappy path.
                if (PendingStep3Mode == Step3Mode_Enum.Path)
                    config["sappy"] = PendingSappyPath;

                // Step 4: Debugger + ASM paths.
                if (PendingStep4Mode == Step4Mode_Enum.Path)
                {
                    config["devkitpro_eabi"] = PendingASMPath;
                    config["emulator2"] = PendingDebuggerPath;
                }

                // Step 5: Music tools.
                if (PendingStep5Mode == Step5Mode_Enum.Path)
                {
                    config["sox"] = PendingSoxPath;
                    config["gba_mus_riper"] = PendingGbaMusRiperPath;
                    // midfix4agb is optional in WF, so it may be empty here.
                    config["midfix4agb"] = PendingMidfix4agbPath;
                }

                // Step 6: Git path.
                if (PendingStep6Mode == Step6Mode_Enum.Path)
                    config["git_path"] = PendingGitPath;

                // Language and color preferences (set from BeginPage).
                if (!string.IsNullOrEmpty(PendingLanguage))
                    config["func_lang"] = PendingLanguage;

                if (PendingColorTheme >= 1)
                    config["color_set"] = PendingColorTheme.ToString();

                SettingStatus = "All settings applied.";
            }
            finally
            {
                IsConfiguring = false;
                MarkClean();
            }
        }

        /// <summary>
        /// Probe a candidate git path. If it fails ProbeGit, fall back to
        /// GitUtil.FindGitExecutable. Used by the Browse handler so the user
        /// gets the OS-default git when the picker is opened from an empty
        /// state. Matches WF GotoPage6() discovery semantics.
        /// </summary>
        public static string ProbeGitOrFindFallback(string candidate)
        {
            if (!string.IsNullOrEmpty(candidate) && GitUtil.ProbeGit(candidate))
                return candidate;
            string found = GitUtil.FindGitExecutable();
            return string.IsNullOrEmpty(found) ? (candidate ?? "") : found;
        }
    }
}
