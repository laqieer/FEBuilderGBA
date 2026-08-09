using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolDiffDebugSelectView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolDiffDebugSelectViewModel _vm = new();
        public string ViewTitle => "Comparison Debug Tool";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Comparison Debug Tool", 1177, 875, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolDiffDebugSelectView()
        {
            InitializeComponent();
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
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
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

        async void TestPlay_Click(object? sender, RoutedEventArgs e)
        {
            string? backupPath = _vm.GetSelectedBackupPath();
            if (backupPath == null)
                return;
            _vm.DialogResult = "testplay";

            try
            {
                // Read emulator path from config
                string configuredPath = "";
                var cfg = CoreState.Config;
                configuredPath = OptionsViewModel.GetToolPath(cfg, "emulator", "Emulator_Path");

                if (!TryResolveEmulatorForLaunch(configuredPath, out string emulatorPath))
                {
                    _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window,
                        "Emulator not configured.\n\nPlease set the emulator path in Options first.",
                        "Emulator Not Found", MessageBoxMode.Ok);
                    return;
                }

                var result = await ExternalLauncher.Current.OpenPathAsync(emulatorPath, $"\"{backupPath}\"", useShellExecute: false);
                if (!result.IsSucceeded)
                    await MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window,
                        $"Failed to launch emulator: {result.Message}",
                        "Error", MessageBoxMode.Ok);
            }
            catch (Exception ex)
            {
                Log.Error("ToolDiffDebugSelectView.TestPlay", ex.ToString());
                _ = MessageBoxWindow.Show(TopLevel.GetTopLevel(this) as Window,
                    $"Failed to launch emulator: {ex.Message}",
                    "Error", MessageBoxMode.Ok);
            }
        }

        internal static bool TryResolveEmulatorForLaunch(
            string configuredPath,
            out string executablePath)
        {
            return PathUtil.TryResolveExternalToolExecutable(configuredPath, out executablePath);
        }

        internal static bool TryResolveEmulatorForLaunch(
            string configuredPath,
            bool isMacOS,
            Func<string, bool> executablePredicate,
            out string executablePath)
        {
            return PathUtil.TryResolveExternalToolExecutable(
                configuredPath,
                isMacOS,
                executablePredicate,
                out executablePath);
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
