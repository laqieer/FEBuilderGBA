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
// SCOPE (#1122; rest carved to #1070): this ships the navigation model + a
// reachable editor launcher. The full desktop MainWindow shell (ROM open/save
// actions, recent files, undo UI, menu commands) is NOT reproduced here — that
// is a separate shell-controller extraction tracked under #1070. The desktop
// MainWindow is unchanged.
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
            // Prefer a navigated editor's title when the page carries one.
            if (Host.CurrentContent?.DataContext is IEditorView ev && !string.IsNullOrEmpty(ev.ViewTitle))
                return ev.ViewTitle;
            return "FEBuilderGBA";
        }

        void Back_Click(object? sender, RoutedEventArgs e) => Host.GoBack();

        void Home_Click(object? sender, RoutedEventArgs e)
        {
            // Drop back to the launcher root, cancelling any pending pick/modal.
            WindowManager.Instance.CloseAll();
        }

        /// <summary>
        /// Build the root launcher page: a scrollable column of buttons that
        /// open the common editors via WindowManager (which now routes to the
        /// single-view host). Kept deliberately small — the full editor catalog
        /// + ROM actions live in the carved-out shell controller (#1070).
        /// </summary>
        Control BuildLauncher()
        {
            var panel = new StackPanel { Spacing = 6, Margin = new global::Avalonia.Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = R._("Editors"),
                FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
                Margin = new global::Avalonia.Thickness(0, 0, 0, 6),
            });

            foreach (var (label, open) in LauncherEntries())
            {
                var btn = new Button
                {
                    Content = R._(label),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                btn.Click += (_, _) =>
                {
                    // Log.Error takes params string[] joined with spaces (no
                    // composite formatting), so pass the message as its own arg.
                    try { open(); }
                    catch (Exception ex) { Log.Error("MainView launcher open failed: ", ex.Message); }
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
        /// variants are a shell-controller concern carved to #1070.
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
