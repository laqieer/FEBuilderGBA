// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — Android single-activity navigation model.
//
// The Android single-view shell. Under ISingleViewApplicationLifetime, App sets
// MainView = new MainView(). This control:
//   1. installs the AndroidNavigationService on WindowManager.Instance, so every
//      `WindowManager.Instance.Open<T>()` call across the ~356 call sites routes
//      to the single-view page/view-stack host instead of opening an OS window;
//   2. seeds a root launcher page (the editor menu) so editors are reachable;
//   3. renders the nav host's current top page into NavHost, and wires Back /
//      Home + a back-button title.
//
// SCOPE (#1122): this ships the single-view navigation model + a reachable
// editor launcher. ROM open/save was added here for #1870 (PR #1872). The rest
// of the desktop MainWindow shell parity (recent files, undo UI, menu commands)
// and single-view editor hosting are not reproduced here yet — both tracked
// under the #1873 single-view-parity umbrella. The desktop MainWindow is unchanged.
using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Layout;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MainView : UserControl
    {
        readonly AndroidNavigationService _nav;
        INavigationHost Host => _nav;

        public MainView()
        {
            InitializeComponent();

            // Set top-bar button text via R._() (translated; no AXAML literal).
            BackButton.Content = "‹ " + R._("Back");
            HomeButton.Content = R._("Home");
            OpenRomButton.Content = R._("Open ROM");
            SaveRomButton.Content = R._("Save ROM");
            TitleText.Text = "FEBuilderGBA";

            // Install the single-view navigation service. Reuse the one already
            // selected by WindowManager when running on Android; otherwise create
            // one (so a desktop host that explicitly shows MainView — e.g. the
            // headless shell render in CI proof — still works).
            _nav = WindowManager.Instance.Service as AndroidNavigationService
                   ?? new AndroidNavigationService();
            WindowManager.Instance.SetService(_nav);

            // Seed the launcher as the root page, then render.
            _nav.SetRoot(BuildLauncher());
            Host.StackChanged += OnStackChanged;
            UpdateRomActionState();
            RenderTop();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            Host.StackChanged -= OnStackChanged;
            base.OnUnloaded(e);
        }

        void OnStackChanged()
        {
            // StackChanged can fire from any await continuation; marshal to UI.
            if (Dispatcher.UIThread.CheckAccess()) RenderTop();
            else Dispatcher.UIThread.Post(RenderTop);
        }

        void RenderTop()
        {
            NavHost.Content = Host.CurrentContent;
            BackButton.IsEnabled = Host.CanGoBack;
            TitleText.Text = DescribeTop();
        }

        string DescribeTop()
        {
            // The page title comes from the OWNING view Window's ViewTitle, which
            // the nav host resolves via its page->Window map (IEditorView is
            // implemented by the Window subclass, NOT the page content's
            // DataContext). Falls back to the app name for the root/untitled page.
            return Host.CurrentTitle ?? "FEBuilderGBA";
        }

        void Back_Click(object? sender, RoutedEventArgs e) => Host.GoBack();

        void Home_Click(object? sender, RoutedEventArgs e)
        {
            // Drop back to the launcher root, cancelling any pending pick/modal.
            WindowManager.Instance.CloseAll();
        }

        /// <summary>
        /// Open a ROM via the shared <see cref="RomFileService"/> (#1870). On
        /// success, rebuild the launcher root so the editor list reflects the new
        /// ROM and any stale editor pages from a previous ROM are dropped.
        /// </summary>
        async void OpenRom_Click(object? sender, RoutedEventArgs e)
        {
            RomOpenOutcome outcome;
            try
            {
                outcome = await RomFileService.OpenRomAsync(this);
            }
            catch (Exception ex)
            {
                Log.Error("MainView OpenRom failed: ", ex.ToString());
                SetStatus(R._("Failed to load ROM."));
                return;
            }

            switch (outcome)
            {
                case RomOpenOutcome.Loaded:
                    // SetRoot -> NavigationStack.Reset: refreshes the editor list
                    // for the new ROM AND drops any stale editor pages / pending
                    // picks left over from a previously loaded ROM.
                    _nav.SetRoot(BuildLauncher());
                    UpdateRomActionState();
                    SetStatus(R._("Loaded:") + " " + RomDisplayName());
                    break;
                case RomOpenOutcome.Failed:
                    SetStatus(R._("Failed to load ROM."));
                    break;
                default: // Cancelled — no change.
                    break;
            }
        }

        /// <summary>Save the current ROM via the shared <see cref="RomFileService"/> (#1870).</summary>
        async void SaveRom_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null)
            {
                SetStatus(R._("Open a ROM first."));
                return;
            }

            if (CoreState.IsDecompMode)
            {
                // Parity with desktop SaveRom_Click: a decomp preview ROM is
                // source-backed and read-only — surface the same message instead
                // of overwriting the build preview.
                SetStatus(R._("This is a source-backed decomp project. The built ROM is a preview and cannot be saved over. Edit the source and rebuild instead."));
                return;
            }

            string? saved;
            try
            {
                saved = await RomFileService.SaveRomAsync(this);
            }
            catch (Exception ex)
            {
                Log.Error("MainView SaveRom failed: ", ex.ToString());
                SetStatus(R._("Failed to save ROM."));
                return;
            }

            if (saved != null)
                SetStatus(R._("ROM saved as:") + " " + System.IO.Path.GetFileName(saved));
        }

        /// <summary>Enable the Save button only when a writable ROM is loaded.</summary>
        void UpdateRomActionState()
        {
            // Match the desktop save guards: no Save for a decomp preview ROM
            // (read-only) even though the single-view shell can't enter decomp mode.
            SaveRomButton.IsEnabled = CoreState.ROM != null && !CoreState.IsDecompMode;
        }

        /// <summary>Show a message in the ROM status strip (or hide it when empty).</summary>
        void SetStatus(string message)
        {
            RomStatusText.Text = message;
            RomStatusBorder.IsVisible = !string.IsNullOrEmpty(message);
        }

        /// <summary>Friendly "name (version)" label for the loaded ROM.</summary>
        static string RomDisplayName()
        {
            var rom = CoreState.ROM;
            if (rom == null) return "";
            string name = System.IO.Path.GetFileName(rom.Filename ?? "");
            string ver = rom.RomInfo?.VersionToFilename ?? "";
            if (!string.IsNullOrEmpty(ver) && !string.IsNullOrEmpty(name)) return name + " (" + ver + ")";
            return string.IsNullOrEmpty(name) ? ver : name;
        }

        /// <summary>
        /// Build the root launcher page: a scrollable column of buttons that
        /// open the common editors via WindowManager (which now routes to the
        /// single-view host). Kept deliberately small — the full editor catalog
        /// lives in the carved-out shell controller (#1873); ROM open/save is on
        /// the top app bar (added for #1870 in PR #1872).
        /// </summary>
        Control BuildLauncher()
        {
            var panel = new StackPanel { Spacing = 6, Margin = new global::Avalonia.Thickness(12) };
            // Editors read CoreState.ROM. Until a ROM is loaded, disable the
            // editor buttons and show a hint pointing at the Open ROM action so
            // the launcher can't open an empty editor (#1870).
            bool hasRom = CoreState.ROM != null;
            panel.Children.Add(new TextBlock
            {
                Text = hasRom
                    ? R._("Editors")
                    : R._("No ROM loaded. Tap \"Open ROM\" above to begin."),
                FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                Margin = new global::Avalonia.Thickness(0, 0, 0, 6),
            });

            foreach (var (label, open) in LauncherEntries())
            {
                var btn = new Button
                {
                    Content = R._(label),
                    IsEnabled = hasRom,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                btn.Click += (_, _) =>
                {
                    try
                    {
                        open();
                    }
                    catch (Exception ex)
                    {
                        // Editor views are Window subclasses; on the browser
                        // single-view host `new Window()` throws
                        // NotSupportedException ("Browser doesn't support
                        // windowing platform"). Hosting editor Windows in the
                        // single-view shell is the architectural rewrite tracked
                        // by #1873. Log the full stack AND surface a friendly
                        // message — Log.Error alone is not visible on wasm.
                        Log.Error("MainView launcher open failed: ", ex.ToString());
                        var baseEx = ex.GetBaseException();
                        if (baseEx is NotSupportedException)
                            SetStatus(R._("Editors aren't available in this browser build yet."));
                        else
                            SetStatus(R._("Couldn't open editor:") + " " + baseEx.Message);
                    }
                };
                panel.Children.Add(btn);
            }

            var scroller = new ScrollViewer { Content = panel };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(scroller, "Main_AndroidLauncher_Control");
            return scroller;
        }

        /// <summary>
        /// The launcher entries: (label, open-action). Each open-action goes
        /// through WindowManager so it routes to the single-view host. We open
        /// FE8U-shaped editors by default (the most common); version-specific
        /// variants are a shell-controller concern carved to #1873.
        /// </summary>
        static IEnumerable<(string Label, Action Open)> LauncherEntries()
        {
            yield return ("Unit Editor", () => WindowManager.Instance.Open<UnitEditorView>());
            yield return ("Class Editor", () => WindowManager.Instance.Open<ClassEditorView>());
            yield return ("Item Editor", () => WindowManager.Instance.Open<ItemEditorView>());
            yield return ("Portrait Editor", () => WindowManager.Instance.Open<PortraitViewerView>());
            yield return ("Text Editor", () => WindowManager.Instance.Open<TextViewerView>());
            yield return ("Map Settings", () => WindowManager.Instance.Open<MapSettingView>());
            yield return ("Song Table", () => WindowManager.Instance.Open<SongTableView>());
            yield return ("Image Viewer", () => WindowManager.Instance.Open<ImageViewerView>());
        }
    }
}
