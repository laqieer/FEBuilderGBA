using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        readonly MainWindowViewModel _vm = new();

        public MainWindow()
        {
            InitializeComponent();
            WindowManager.Instance.MainWindow = this;
            Opened += MainWindow_Opened;
        }

        private async void MainWindow_Opened(object? sender, EventArgs e)
        {
            // Auto-load ROM if --rom was specified
            if (!string.IsNullOrEmpty(App.StartupRomPath))
            {
                bool ok = LoadRomFile(App.StartupRomPath);
                if (!ok)
                {
                    if (App.SmokeTestMode)
                    {
                        Environment.ExitCode = 2;
                        Close();
                        return;
                    }
                    await MessageBoxWindow.Show(this, $"Failed to load ROM: {App.StartupRomPath}", "Error", MessageBoxMode.Ok);
                    return;
                }

                // If smoke test mode, run the editor selection test
                if (App.SmokeTestMode)
                {
                    // Use a short delay so the window is fully initialized
                    await Task.Delay(200);
                    RunSmokeTest();
                    return;
                }
            }
        }

        /// <summary>
        /// Load a ROM file and initialize all subsystems.
        /// Returns true on success.
        /// </summary>
        public bool LoadRomFile(string path)
        {
            ROM rom = new ROM();
            bool ok = rom.Load(path, out string version);
            if (!ok) return false;

            CoreState.ROM = rom;

            try
            {
                if (CoreState.SystemTextEncoder is HeadlessSystemTextEncoder)
                    CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, CoreState.ROM);
                CoreState.FETextEncoder ??= new FETextEncode();
                CoreState.TextEscape ??= new TextEscape();
                CoreState.FlagCache ??= new EtcCacheFLag();
                CoreState.ExportFunction ??= new ExportFunction();
                CoreState.Undo ??= new Undo();

                CoreState.EventScript ??= new EventScript();
                CoreState.EventScript.Load(EventScript.EventScriptType.Event);
                CoreState.ProcsScript ??= new EventScript();
                CoreState.ProcsScript.Load(EventScript.EventScriptType.Procs);
                CoreState.AIScript ??= new EventScript();
                CoreState.AIScript.Load(EventScript.EventScriptType.AI);
            }
            catch (Exception ex)
            {
                Log.Error("Init error: {0}", ex.Message);
            }

            // Update UI
            _vm.UpdateFromRom();
            StatusText.Text = _vm.StatusText;
            NoRomLabel.IsVisible = false;
            EditorPanel.IsVisible = true;
            SaveMenuItem.IsEnabled = true;
            LintMenuItem.IsEnabled = true;

            return true;
        }

        private void RunSmokeTest()
        {
            try
            {
                // Test 1: Open Unit Editor and select the first item
                var unitEditor = WindowManager.Instance.Open<UnitEditorView>();

                // Test 2: Open Item Editor and select the first item
                var itemEditor = WindowManager.Instance.Open<ItemEditorView>();

                // Give the windows time to load their lists, then select items
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        await Task.Delay(500);

                        // Select first item in each editor (index 0)
                        unitEditor.SelectFirstItem();
                        await Task.Delay(200);

                        itemEditor.SelectFirstItem();
                        await Task.Delay(200);

                        // If we got here without crashing, the smoke test passed
                        Environment.ExitCode = 0;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Smoke test failed: {0}", ex.Message);
                        Environment.ExitCode = 1;
                    }
                    finally
                    {
                        WindowManager.Instance.CloseAll();
                        Close();
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Error("Smoke test failed: {0}", ex.Message);
                Environment.ExitCode = 1;
                Close();
            }
        }

        private async void OpenRom_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (string.IsNullOrEmpty(path)) return;

            bool ok = LoadRomFile(path);
            if (!ok)
            {
                await MessageBoxWindow.Show(this, "Failed to load ROM.", "Error", MessageBoxMode.Ok);
            }
        }

        private void SaveRom_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;
            CoreState.ROM.Save(CoreState.ROM.Filename, false);
            CoreState.Services.ShowInfo("ROM saved.");
        }

        private void Exit_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenUnits_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<UnitEditorView>();
        }

        private void OpenItems_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemEditorView>();
        }

        private void OpenImageViewer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ImageViewerView>();
        }

        private void Lint_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;

            var scanner = new FELintScanner();
            var errors = scanner.Scan();

            if (errors.Count == 0)
            {
                _ = MessageBoxWindow.Show(this, "Lint: No errors found.", "Lint Results", MessageBoxMode.Ok);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{errors.Count} issue(s) found:");
                foreach (var err in errors)
                {
                    string severity = err.Severity == FELintCore.ErrorType.ERROR ? "ERROR" : "WARNING";
                    sb.AppendLine($"[{severity}] 0x{err.Addr:X08}: {err.ErrorMessage}");
                }
                _ = MessageBoxWindow.Show(this, sb.ToString(), "Lint Results", MessageBoxMode.Ok);
            }
        }

        private async void About_Click(object? sender, RoutedEventArgs e)
        {
            await MessageBoxWindow.Show(this,
                "FEBuilderGBA\nAvalonia Cross-Platform Preview\nCopyright 2017- GPLv3",
                "About", MessageBoxMode.Ok);
        }
    }
}
