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

            // Wire headless caches so Core code doesn't NullRef
            CoreState.CommentCache ??= new HeadlessEtcCache();
            CoreState.LintCache ??= new HeadlessEtcCache();
            CoreState.WorkSupportCache ??= new HeadlessEtcCache();

            // Wire text encoder — with HeadlessSystemTextEncoder fallback
            if (CoreState.SystemTextEncoder == null || CoreState.SystemTextEncoder is HeadlessSystemTextEncoder)
            {
                try
                {
                    CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, CoreState.ROM);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to init SystemTextEncoder, using headless fallback: {0}", ex.Message);
                    CoreState.SystemTextEncoder ??= new HeadlessSystemTextEncoder();
                }
            }

            // Init Huffman text encoder
            if (CoreState.FETextEncoder == null)
            {
                try { CoreState.FETextEncoder = new FETextEncode(); }
                catch (Exception ex) { Log.Error("Failed to init FETextEncode: {0}", ex.Message); }
            }

            // Init text escape
            CoreState.TextEscape ??= new TextEscape();

            // Init flag cache
            if (CoreState.FlagCache == null)
            {
                try { CoreState.FlagCache = new EtcCacheFLag(); }
                catch (Exception ex) { Log.Error("Failed to init FlagCache: {0}", ex.Message); }
            }

            // Init export function + undo
            CoreState.ExportFunction ??= new ExportFunction();
            CoreState.Undo ??= new Undo();

            // Init event scripts
            try
            {
                if (CoreState.EventScript == null)
                {
                    CoreState.EventScript = new EventScript();
                    CoreState.EventScript.Load(EventScript.EventScriptType.Event);
                }
                if (CoreState.ProcsScript == null)
                {
                    CoreState.ProcsScript = new EventScript();
                    CoreState.ProcsScript.Load(EventScript.EventScriptType.Procs);
                }
                if (CoreState.AIScript == null)
                {
                    CoreState.AIScript = new EventScript();
                    CoreState.AIScript.Load(EventScript.EventScriptType.AI);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to init EventScripts: {0}", ex.Message);
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
