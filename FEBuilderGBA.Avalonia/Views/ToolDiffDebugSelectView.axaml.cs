using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolDiffDebugSelectView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly ToolDiffDebugSelectViewModel _vm = new();
        public string ViewTitle => "Comparison Debug Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolDiffDebugSelectView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        async void BrowseOriginal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path))
                {
                    _vm.OriginalFilename = path;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolDiffDebugSelectView", ex.ToString());
            }
        }

        void Compare_Click(object? sender, RoutedEventArgs e)
        {
            string? backupPath = _vm.GetSelectedBackupPath();
            if (backupPath == null)
                return;
            _vm.DialogResult = "compare";

            try
            {
                // Open the three-way merge view with:
                //   Original = vanilla ROM, Mine = current ROM, Theirs = selected backup
                var mergeView = WindowManager.Instance.Open<ToolThreeMargeView>();
                if (mergeView.DataContext is ToolThreeMargeViewViewModel mergeVm)
                {
                    mergeVm.OriginalPath = _vm.OriginalFilename;
                    mergeVm.MyPath = CoreState.ROM?.Filename ?? "";
                    mergeVm.TheirsPath = backupPath;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolDiffDebugSelectView.Compare", ex.ToString());
            }
        }

        void TestPlay_Click(object? sender, RoutedEventArgs e)
        {
            string? backupPath = _vm.GetSelectedBackupPath();
            if (backupPath == null)
                return;
            _vm.DialogResult = "testplay";

            try
            {
                // Read emulator path from config
                string emulatorPath = "";
                var cfg = CoreState.Config;
                emulatorPath = OptionsViewModel.GetToolPath(cfg, "emulator", "Emulator_Path");

                if (string.IsNullOrEmpty(emulatorPath) || !System.IO.File.Exists(emulatorPath))
                {
                    _ = MessageBoxWindow.Show(this,
                        "Emulator not configured.\n\nPlease set the emulator path in Options first.",
                        "Emulator Not Found", MessageBoxMode.Ok);
                    return;
                }

                var psi = new System.Diagnostics.ProcessStartInfo(emulatorPath, $"\"{backupPath}\"")
                {
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.Error("ToolDiffDebugSelectView.TestPlay", ex.ToString());
                _ = MessageBoxWindow.Show(this,
                    $"Failed to launch emulator: {ex.Message}",
                    "Error", MessageBoxMode.Ok);
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
