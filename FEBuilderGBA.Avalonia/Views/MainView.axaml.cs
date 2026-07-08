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
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Layout;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MainView : UserControl
    {
        readonly AndroidNavigationService _nav;
        INavigationHost Host => _nav;

        // #1905: set when a language switch happens while the user is deeper in the nav
        // stack, so the deferred launcher-root rebuild runs lazily on return to the root.
        bool _launcherLanguageStale;

        public MainView()
        {
            InitializeComponent();

            // Set top-bar button text via R._() (translated; no AXAML literal).
            ApplyTopBarLabels();
            TitleText.Text = "FEBuilderGBA";

            // #1895: attach the overflow "More" menu (Language / Wiki / Discussions /
            // Issue Report). The CoreState.LanguageChanged + Host.StackChanged
            // subscriptions live in OnAttached/OnDetached (below), not the ctor, so a
            // MainView that is constructed but never attached — headless tests, preview
            // tooling — doesn't leak a handler on the static LanguageChanged event.
            MoreButton.Flyout = BuildMoreFlyout();

            // Install the single-view navigation service. Reuse the one already
            // selected by WindowManager when running on Android; otherwise create
            // one (so a desktop host that explicitly shows MainView — e.g. the
            // headless shell render in CI proof — still works).
            _nav = WindowManager.Instance.Service as AndroidNavigationService
                   ?? new AndroidNavigationService();
            WindowManager.Instance.SetService(_nav);

            // Seed the launcher as the root page, then render.
            _nav.SetRoot(BuildLauncher());
            UpdateRomActionState();
            RenderTop();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // Subscribe only while attached so a constructed-but-never-attached MainView
            // (headless tests, preview tooling) can't leak a handler on the static
            // CoreState.LanguageChanged event or the nav host (#1895 review).
            Host.StackChanged += OnStackChanged;
            CoreState.LanguageChanged += OnLanguageChanged;
            RenderTop();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Host.StackChanged -= OnStackChanged;
            CoreState.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        void OnStackChanged()
        {
            // StackChanged can fire from any await continuation; marshal to UI.
            if (Dispatcher.UIThread.CheckAccess()) OnStackChangedCore();
            else Dispatcher.UIThread.Post(OnStackChangedCore);
        }

        void OnStackChangedCore()
        {
            // If a language switch happened while the user was deeper in the stack, the
            // launcher-root rebuild was deferred (SetRoot would have reset the stack).
            // Now that we're back at the root, rebuild it so its R._() labels aren't
            // stale (#1905 review nit). At the root, SetRoot only replaces the root page.
            if (_launcherLanguageStale && !Host.CanGoBack)
            {
                _launcherLanguageStale = false;
                _nav.SetRoot(BuildLauncher());
            }
            RenderTop();
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

#if E2E_HOOKS
        public static void RefreshForLoadedRomForTest()
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is not global::Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime
                {
                    MainView: MainView mv
                })
                return;

            void Refresh()
            {
                mv._nav.SetRoot(mv.BuildLauncher());
                mv.UpdateRomActionState();
                mv.SetStatus(R._("Loaded:") + " " + RomDisplayName());
            }

            if (Dispatcher.UIThread.CheckAccess())
                Refresh();
            else
                Dispatcher.UIThread.Invoke(Refresh);
        }

        public static string OpenLauncherEntryForTest(string key)
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is not global::Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime
                {
                    MainView: MainView mv
                })
                return "";

            string? openedTitle = null;

            void Open()
            {
                var entry = FindLauncherEntry(key);
                if (entry is null)
                    return;

                entry.Open();
                openedTitle = WindowManager.Instance.Service is INavigationHost host
                    ? host.CurrentTitle
                    : null;
            }

            if (Dispatcher.UIThread.CheckAccess())
                Open();
            else
                Dispatcher.UIThread.Invoke(Open);

            return openedTitle ?? "";
        }
#endif

        /// <summary>
        /// Resolve a launcher key to its catalog entry: an EXACT, unique catalog Key match wins;
        /// otherwise fall back to the (possibly non-unique) label, so legacy keys like "MoveCost"
        /// (→ label "Move Cost") still resolve. Key-first prevents a duplicate label (e.g. the two
        /// distinct "Unit Palette" editors, ImageUnitPaletteView vs UnitPaletteView) from resolving
        /// to the wrong entry. Internal test seam (via InternalsVisibleTo).
        /// </summary>
        internal static EditorEntry? FindLauncherEntry(string key)
        {
            string nk = NormalizeLauncherKey(key);
            EditorEntry? labelHit = null;
            foreach (var e in EditorCatalog.AllEntries)
            {
                if (NormalizeLauncherKey(e.Key) == nk)
                    return e; // exact, unique key wins
                if (labelHit is null && NormalizeLauncherKey(e.Label) == nk)
                    labelHit = e;
            }
            return labelHit;
        }

        internal static string NormalizeLauncherKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            Span<char> buffer = value.Length <= 128
                ? stackalloc char[value.Length]
                : new char[value.Length];
            int count = 0;
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    buffer[count++] = char.ToUpperInvariant(ch);
            }
            string normalized = new(buffer[..count]);
            // Stable, translation-independent key: strip non-alphanumerics + uppercase. Catalog
            // Keys are derived from view type names, so "MoveCost" and the label "Move Cost" both
            // fold to "MOVECOST" here (no special-case remap needed).
            return normalized;
        }

        void Back_Click(object? sender, RoutedEventArgs e) => Host.GoBack();

        void Home_Click(object? sender, RoutedEventArgs e)
        {
            // Drop back to the launcher root, cancelling any pending pick/modal.
            WindowManager.Instance.CloseAll();
        }

        /// <summary>Set all top-bar button labels via R._() (called on init + on language change).</summary>
        void ApplyTopBarLabels()
        {
            BackButton.Content = "‹ " + R._("Back");
            HomeButton.Content = R._("Home");
            OpenRomButton.Content = R._("Open ROM");
            SaveRomButton.Content = R._("Save ROM");
            MoreButton.Content = R._("More");
        }

        // #1895: web-app home-page links. Static testable data (mirrors LauncherEntries()).
        // Issue Report uses /issues/new/choose because the fork disables blank issues
        // (.github/ISSUE_TEMPLATE/config.yml).
        internal const string WikiUrl = "https://github.com/laqieer/FEBuilderGBA/wiki";
        internal const string DiscussionsUrl = "https://github.com/laqieer/FEBuilderGBA/discussions";
        internal const string IssueReportUrl = "https://github.com/laqieer/FEBuilderGBA/issues/new/choose";

        internal static IEnumerable<(string Label, string Url)> HomePageLinks => new[]
        {
            ("Wiki", WikiUrl),
            ("Discussions", DiscussionsUrl),
            ("Issue Report", IssueReportUrl),
        };

        /// <summary>
        /// Build the overflow "More" MenuFlyout: a Language submenu (live switch via
        /// <see cref="OptionsViewModel.ApplyLanguage"/>) plus the external-link items.
        /// Rebuilt on every language change so the item headers relocalize.
        /// </summary>
        MenuFlyout BuildMoreFlyout()
        {
            var flyout = new MenuFlyout();

            var languageItem = new MenuItem { Header = R._("Language") };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(languageItem, "Main_AndroidLanguage_Button");
            foreach (string display in OptionsViewModel.EnumerateLanguages())
            {
                string code = OptionsViewModel.ExtractLanguageCode(display);
                var child = new MenuItem { Header = display };
                child.Click += (_, _) => OptionsViewModel.ApplyLanguage(code);
                languageItem.Items.Add(child);
            }
            flyout.Items.Add(languageItem);
            flyout.Items.Add(new Separator());

            foreach (var (label, url) in HomePageLinks)
            {
                var item = new MenuItem { Header = R._(label) };
                global::Avalonia.Automation.AutomationProperties.SetAutomationId(item, "Main_Android" + label.Replace(" ", "") + "_Button");
                item.Click += (_, _) => OpenUrl(url);
                flyout.Items.Add(item);
            }

            return flyout;
        }

        /// <summary>Open a URL with Avalonia's cross-platform launcher (window.open on wasm).</summary>
        async void OpenUrl(string url)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                SetStatus(R._("Couldn't open link:") + " " + url);
                return;
            }
            try
            {
                // Called from the menu-item click (a user gesture), so the browser
                // won't popup-block window.open.
                bool ok = await top.Launcher.LaunchUriAsync(new Uri(url));
                if (!ok)
                    SetStatus(R._("Couldn't open link:") + " " + url);
            }
            catch (Exception ex)
            {
                Log.Error("MainView OpenUrl failed: ", ex.ToString());
                SetStatus(R._("Couldn't open link:") + " " + url);
            }
        }

        /// <summary>
        /// #1895 / B1: relocalize the whole shell after a live language switch. MainView
        /// sets its labels only in the ctor, so without this a language pick would reload
        /// translations but leave the visible UI in the old language.
        /// </summary>
        void OnLanguageChanged()
        {
            // LanguageChanged can fire from any thread; marshal to the UI thread.
            if (Dispatcher.UIThread.CheckAccess()) RelocalizeShell();
            else Dispatcher.UIThread.Post(RelocalizeShell);
        }

        void RelocalizeShell()
        {
            ApplyTopBarLabels();
            // Rebuild the More menu so its item headers pick up the new language.
            MoreButton.Flyout = BuildMoreFlyout();
            // Rebuild the launcher root so its R._()-based labels relocalize — but ONLY
            // when it IS the current page. SetRoot resets the whole nav stack, so doing
            // it while the user is deeper (an editor / options page — which DO open as
            // pages on the single-view host, #1873) would close that page and lose their
            // context. Open pages relocalize themselves via their own LanguageChanged hook.
            if (!Host.CanGoBack)
            {
                // At the root: rebuild it now with the new language.
                _launcherLanguageStale = false;
                _nav.SetRoot(BuildLauncher());
            }
            else
            {
                // Deeper in the stack: don't SetRoot (it would reset the whole stack and
                // close the open page). Defer the launcher rebuild until we return to root
                // (handled in OnStackChangedCore), so the user's context isn't lost.
                _launcherLanguageStale = true;
            }
            RenderTop();
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
        /// Build the root launcher page: a filterable, category-grouped list of every editor the
        /// desktop home page exposes (the shared <see cref="EditorCatalog"/>), so the single-view
        /// (WebAssembly / Android) app shows the FULL editor set instead of the handful it used to
        /// (#1891). Editors are the shared embeddable UserControls opened via WindowManager, which
        /// routes to the single-view host — #1873 (single-view editor hosting) is complete, so the
        /// old "editors are Windows that throw here" limitation no longer applies. Version-specific
        /// editors are gated to the loaded ROM exactly as the desktop MainWindow does
        /// (<see cref="EditorCatalog.AppliesTo"/>). ROM open/save is on the top app bar (#1870).
        /// </summary>
        Control BuildLauncher()
        {
            bool hasRom = CoreState.ROM != null;

            var root = new DockPanel();
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(root, "Main_AndroidLauncher_Control");

            var header = new TextBlock
            {
                Text = hasRom
                    ? R._("Editors")
                    : R._("No ROM loaded. Tap \"Open ROM\" above to begin."),
                FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                Margin = new global::Avalonia.Thickness(12, 12, 12, 6),
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Editors read CoreState.ROM; the version gate needs a loaded ROM. Until one is loaded
            // show only the hint pointing at the Open ROM action (#1870).
            if (!hasRom)
                return root;

            var rom = CoreState.ROM!;
            int version = rom.RomInfo?.version ?? 0;
            bool multibyte = rom.RomInfo?.is_multibyte ?? false;

            // Filter box — built in code-behind, so the AutomationId validator (which only scans
            // .axaml) does not apply; the id still follows the MainView "Main_" convention.
            var filter = new TextBox
            {
                Watermark = R._("Type to filter editors..."),
                Margin = new global::Avalonia.Thickness(12, 0, 12, 6),
            };
            global::Avalonia.Automation.AutomationProperties.SetAutomationId(filter, "Main_LauncherFilter_Input");
            DockPanel.SetDock(filter, Dock.Top);
            root.Children.Add(filter);

            var catPanel = new StackPanel { Spacing = 4, Margin = new global::Avalonia.Thickness(12, 0, 12, 12) };
            var cats = new List<(Expander Exp, List<(Button Btn, string Label)> Buttons)>();

            foreach (var group in EditorCatalog.Categories)
            {
                var applicable = group.Where(e => EditorCatalog.AppliesTo(e, version, multibyte)).ToList();
                if (applicable.Count == 0)
                    continue;

                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                var buttons = new List<(Button, string)>();
                foreach (var entry in applicable)
                {
                    string label = R._(entry.Label);
                    var btn = new Button
                    {
                        Content = label,
                        Margin = new global::Avalonia.Thickness(4),
                        MinWidth = 100,
                    };
                    global::Avalonia.Automation.AutomationProperties.SetAutomationId(btn, "Main_Launcher_" + entry.Key + "_Button");
                    var open = entry.Open;
                    btn.Click += (_, _) => OpenFromLauncher(open);
                    wrap.Children.Add(btn);
                    buttons.Add((btn, label));
                }

                var exp = new Expander
                {
                    Header = R._(group.Key) + " (" + applicable.Count + ")",
                    Content = wrap,
                    // Collapsed by default: ~200 editors across 28 categories would be a huge scroll
                    // otherwise. The filter box auto-expands matching categories.
                    IsExpanded = false,
                };
                catPanel.Children.Add(exp);
                cats.Add((exp, buttons));
            }

            filter.TextChanged += (_, _) => ApplyLauncherFilter(cats, filter.Text);

            var scroller = new ScrollViewer { Content = catPanel };
            root.Children.Add(scroller); // fills the space under the docked header + filter
            return root;
        }

        /// <summary>
        /// Open an editor from the launcher, surfacing failures as a status message instead of
        /// crashing the shell. All catalog editors are embeddable UserControls (asserted by
        /// EditorCatalogTests), so they host in the single-view navigation host; this catch is
        /// defense-in-depth for an individual editor whose ctor/load fails (e.g. a wasm-unsupported
        /// dependency). Log.Error alone is not visible on wasm, so we also set a status message.
        /// </summary>
        void OpenFromLauncher(Action open)
        {
            try
            {
                open();
            }
            catch (Exception ex)
            {
                Log.Error("MainView launcher open failed: ", ex.ToString());
                SetStatus(R._("Couldn't open editor:") + " " + ex.GetBaseException().Message);
            }
        }

        /// <summary>Filter launcher buttons by label; hides empty categories and auto-expands matches.</summary>
        static void ApplyLauncherFilter(List<(Expander Exp, List<(Button Btn, string Label)> Buttons)> cats, string? text)
        {
            string q = (text ?? "").Trim();
            bool hasFilter = q.Length > 0;
            foreach (var (exp, buttons) in cats)
            {
                int visible = 0;
                foreach (var (btn, label) in buttons)
                {
                    bool match = !hasFilter || label.Contains(q, StringComparison.OrdinalIgnoreCase);
                    btn.IsVisible = match;
                    if (match) visible++;
                }
                exp.IsVisible = visible > 0;
                if (hasFilter)
                    exp.IsExpanded = visible > 0;
            }
        }
    }
}
