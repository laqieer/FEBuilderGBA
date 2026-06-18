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
                return await FileDialogHelper.OpenProjectFolder(this);
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

        // Called from the background scan thread; marshal the status update onto
        // the UI thread (mirrors the WF InputFormRef.DoEvents progress pump).
        void ReportProgress(string filename)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _vm.StatusMessage = R._("Scanning: ") + filename;
            });
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
