using System;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Developer Translation Tool — builds the app's own translation resources
    /// from source (WF <c>DevTranslateForm</c> / Core <c>MyTranslateBuild</c>).
    /// Three operations: Translate (full build scan → config/translate/&lt;lang&gt;.txt),
    /// DesignStringConvert and DesignStringReverse (*.Designer.cs). READS source
    /// files, WRITES translation .txt files. NO ROM mutation, NO Undo, NO address
    /// list — a read-no-ROM-bytes tool (orphan by the data-verification contract).
    /// </summary>
    public partial class DevTranslateView : TranslatedWindow, IEditorView
    {
        readonly DevTranslateViewModel _vm = new();

        public string ViewTitle => "Developer Translation Tool";
        public bool IsLoaded => true;

        public DevTranslateView()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        async void BuildSourceBrowse_Click(object? sender, RoutedEventArgs e)
        {
            string? path = await PickSourceFolder();
            if (!string.IsNullOrEmpty(path))
                _vm.BuildSourcePath = path;
        }

        async void DesignSourceBrowse_Click(object? sender, RoutedEventArgs e)
        {
            string? path = await PickSourceFolder();
            if (!string.IsNullOrEmpty(path))
                _vm.DesignSourcePath = path;
        }

        async Task<string?> PickSourceFolder()
        {
            try
            {
                // #1639: the Dev Translate source is a folder TREE walked by path,
                // so it needs a real local directory. OpenProjectFolder returns
                // null on Android SAF (no local path) → surface a clear message
                // instead of silently doing nothing.
                string? folder = await FileDialogHelper.OpenProjectFolder(this);
                if (string.IsNullOrEmpty(folder) && OperatingSystem.IsAndroid())
                    _vm.StatusMessage = R._("Dev Translate reads a folder tree and requires desktop file-system access; it is not available on this device.");
                return folder;
            }
            catch (Exception ex)
            {
                Log.Error("DevTranslateView.PickSourceFolder failed: " + ex.ToString());
                _vm.StatusMessage = R._("Could not open the folder picker.");
                return null;
            }
        }

        async void Build_Click(object? sender, RoutedEventArgs e)
        {
            string lang = _vm.SelectedBuildLanguage?.Code ?? "";
            string source = _vm.BuildSourcePath;
            bool englishBase = _vm.EnglishBase;

            if (!ValidateLanguage(lang)) return;
            if (!ValidateSource(source)) return;

            SetButtonsEnabled(false);
            ResetProgress();
            _vm.StatusMessage = R._("Translating... This may take a while.");
            try
            {
                bool ok = await Task.Run(() => _vm.RunBuild(lang, source, englishBase, ReportProgress));
                _vm.StatusMessage = ok
                    ? R._("Translation complete. Please restart the tool.")
                    : R._("Translation failed. See the log for details.");
            }
            catch (Exception ex)
            {
                Log.Error("DevTranslateView.Build failed: " + ex.ToString());
                _vm.StatusMessage = R._("Translation failed. See the log for details.");
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        async void DesignConvert_Click(object? sender, RoutedEventArgs e)
        {
            string lang = _vm.SelectedDesignLanguage?.Code ?? "";
            string source = _vm.DesignSourcePath;

            if (!ValidateLanguage(lang)) return;
            if (!ValidateSource(source)) return;

            SetButtonsEnabled(false);
            ResetProgress();
            _vm.StatusMessage = R._("Converting designer strings...");
            try
            {
                bool ok = await Task.Run(() => _vm.RunDesignConvert(lang, source, ReportProgress));
                _vm.StatusMessage = ok
                    ? R._("Designer string conversion complete.")
                    : R._("Translation failed. See the log for details.");
            }
            catch (Exception ex)
            {
                Log.Error("DevTranslateView.DesignConvert failed: " + ex.ToString());
                _vm.StatusMessage = R._("Translation failed. See the log for details.");
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        async void DesignReverse_Click(object? sender, RoutedEventArgs e)
        {
            string lang = _vm.SelectedDesignLanguage?.Code ?? "";
            string source = _vm.DesignSourcePath;

            if (!ValidateLanguage(lang)) return;
            if (!ValidateSource(source)) return;

            SetButtonsEnabled(false);
            ResetProgress();
            _vm.StatusMessage = R._("Reversing designer strings...");
            try
            {
                bool ok = await Task.Run(() => _vm.RunDesignReverse(lang, source, ReportProgress));
                _vm.StatusMessage = ok
                    ? R._("Designer string reverse complete.")
                    : R._("Translation failed. See the log for details.");
            }
            catch (Exception ex)
            {
                Log.Error("DevTranslateView.DesignReverse failed: " + ex.ToString());
                _vm.StatusMessage = R._("Translation failed. See the log for details.");
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        // Min interval between status-label UI posts during a scan. MyTranslateBuild
        // fires onProgress per file AND per untranslated string target, which can be
        // many thousands of events for a large scan — posting every one would
        // saturate the UI thread. We coalesce to at most one post per interval.
        const long ProgressThrottleMs = 150;
        // Max status-label length; progress targets can be very long multiline text.
        const int ProgressMaxLength = 120;

        long _lastProgressPostTicks;

        // Reset the throttle gate so the first progress event of a fresh scan always
        // shows (otherwise a quick second scan could swallow its first update).
        void ResetProgress() => _lastProgressPostTicks = 0;

        // Called from the background scan thread; marshal a sanitized + throttled
        // status update onto the UI thread (mirrors the WF InputFormRef.DoEvents
        // progress pump). The trailing completion message set after the awaited scan
        // always overwrites the last in-flight progress, so the final state is exact.
        void ReportProgress(string target)
        {
            long now = Environment.TickCount64;
            // Best-effort throttle: a stale read here only means one extra/skipped
            // post, which is harmless for a progress indicator (no lock needed).
            if (now - _lastProgressPostTicks < ProgressThrottleMs)
                return;
            _lastProgressPostTicks = now;

            string text = R._("Scanning: ") + SanitizeProgress(target);
            Dispatcher.UIThread.Post(() => _vm.StatusMessage = text);
        }

        // Collapse a progress target to a single readable line: turn any newline /
        // tab / carriage-return into a space, collapse runs of whitespace, trim, and
        // truncate over-long values so the status label stays one short line.
        internal static string SanitizeProgress(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var sb = new System.Text.StringBuilder(value.Length);
            bool prevSpace = false;
            foreach (char c in value)
            {
                char ch = (c == '\r' || c == '\n' || c == '\t') ? ' ' : c;
                bool isSpace = ch == ' ';
                if (isSpace && prevSpace)
                    continue;
                sb.Append(ch);
                prevSpace = isSpace;
            }

            string collapsed = sb.ToString().Trim();
            if (collapsed.Length > ProgressMaxLength)
                collapsed = collapsed.Substring(0, ProgressMaxLength) + "…";
            return collapsed;
        }

        bool ValidateLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang) || lang == "auto" || lang == "ja")
            {
                _vm.StatusMessage = R._("Please select a target language (ja and auto cannot be selected).");
                return false;
            }
            return true;
        }

        bool ValidateSource(string source)
        {
            if (string.IsNullOrEmpty(source) || !Directory.Exists(source))
            {
                _vm.StatusMessage = R._("Please select a valid source code folder.");
                return false;
            }
            return true;
        }

        void SetButtonsEnabled(bool enabled)
        {
            BuildButton.IsEnabled = enabled;
            DesignConvertButton.IsEnabled = enabled;
            DesignReverseButton.IsEnabled = enabled;
        }

        // IEditorView — this tool has no navigable address list; these are no-ops.
        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
