using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class HostedEditorLocalizationTests
{
    [AvaloniaTheory]
    [InlineData("Item Editor")]
    [InlineData("Unit Editor")]
    [InlineData("Class Editor")]
    [InlineData("Data Address Editor")]
    public void Translated_editor_desktop_title_retains_descriptor_key(string key)
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("zh");
        IEmbeddableEditor editor = key switch
        {
            "Item Editor" => new ItemEditorView(),
            "Unit Editor" => new UnitEditorView(),
            "Class Editor" => new ClassEditorView(),
            "Data Address Editor" => new DumpStructSelectDialogView(),
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        var host = new EditorHostWindow(editor);
        try
        {
            host.Show();
            foreach (string language in new[] { "zh", "en", "zh", "en" })
            {
                state.ApplyLanguage(language);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(key, editor.TitleKey);
                Assert.Equal(key, editor.Descriptor.Title);
                Assert.Equal(R._(key), editor.ViewTitle);
                Assert.Equal(R._(key), host.Title);
                Assert.Same(editor, host.Content);
                if (language == "zh") Assert.NotEqual(key, host.Title);
            }
        }
        finally { host.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Desktop_modal_caption_round_trip_dispatches_and_unsubscribes(bool numeric)
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("zh");
        string key = numeric ? "List Expansion" : "Error";
        Window dialog = numeric
            ? new NumberInputDialog("Count prompt", key, 7, 1, 10, titleIsKey: true)
            : new MessageBoxWindow("Detailed error", key, MessageBoxMode.Ok, selectable: false, titleIsKey: true);
        try
        {
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(R._(key), dialog.Title);
            int updates = 0;
            dialog.PropertyChanged += (_, e) =>
            {
                if (e.Property != Window.TitleProperty) return;
                Assert.True(Dispatcher.UIThread.CheckAccess());
                updates++;
            };
            Task.Run(() => state.ApplyLanguage("en")).GetAwaiter().GetResult();
            Assert.NotEqual(key, dialog.Title);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(key, dialog.Title);
            Assert.Equal(1, updates);
            Assert.Equal(1, GuiLocalizationState.SubscriberCount(dialog));

            Task.Run(() => state.ApplyLanguage("zh")).GetAwaiter().GetResult();
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(key, dialog.Title);
            Assert.Equal(0, GuiLocalizationState.SubscriberCount(dialog));
        }
        finally { dialog.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("ja", "Options", "\u30aa\u30d7\u30b7\u30e7\u30f3")]
    [InlineData("zh", "Options", "\u8bbe\u7f6e")]
    [InlineData("ja", "Version Information", "\u30d0\u30fc\u30b8\u30e7\u30f3 \u60c5\u5831")]
    [InlineData("zh", "Version Information", "\u7248\u672c \u4fe1\u606f")]
    public void Initial_title_uses_shipped_catalog_without_changing_descriptor(
        string language, string key, string expected)
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage(language);
        IEmbeddableEditor editor = key == "Options" ? new OptionsView() : new VersionView();
        var host = new EditorHostWindow(editor);
        try
        {
            Assert.NotEqual(key, expected);
            Assert.Equal(expected, host.Title);
            host.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, host.Title);
            Assert.Equal(key, editor.Descriptor.Title);
            Assert.Equal(key, editor.ViewTitle);
            Assert.Same(editor, host.Content);
        }
        finally { host.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Options", "\u8bbe\u7f6e")]
    [InlineData("Version Information", "\u7248\u672c \u4fe1\u606f")]
    public void Language_round_trip_retains_original_title_key(string key, string translated)
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("en");
        var editor = new CatalogTitleEditor(key);
        var host = new EditorHostWindow(editor);
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(key, host.Title);

            state.ApplyLanguage("zh");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(translated, host.Title);
            Assert.Equal(key, editor.Descriptor.Title);

            state.ApplyLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(key, host.Title);
            Assert.Equal(key, editor.ViewTitle);
            Assert.Same(editor, host.Content);
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void Opening_refreshes_title_if_language_changed_after_construction()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("en");
        var host = new EditorHostWindow(new CatalogTitleEditor("Options"));
        try
        {
            Assert.Equal(0, GuiLocalizationState.SubscriberCount(host));
            state.ApplyLanguage("zh");
            host.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("\u8bbe\u7f6e", host.Title);
            Assert.Equal(1, GuiLocalizationState.SubscriberCount(host));
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void Opening_subscribes_before_initial_title_refresh()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("en");
        var host = new EditorHostWindow(new CatalogTitleEditor("Options"));
        bool refreshed = false;
        host.PropertyChanged += (_, e) =>
        {
            if (e.Property != Window.TitleProperty) return;
            refreshed = true;
            Assert.True(Dispatcher.UIThread.CheckAccess());
            Assert.Equal(1, GuiLocalizationState.SubscriberCount(host));
        };
        try
        {
            state.ApplyLanguage("zh");
            host.Show();
            Assert.True(refreshed);
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void Worker_language_notification_dispatches_entire_title_update()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("en");
        var host = new EditorHostWindow(new CatalogTitleEditor("Options"));
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            int titleUpdates = 0;
            host.PropertyChanged += (_, e) =>
            {
                if (e.Property != Window.TitleProperty) return;
                Assert.True(Dispatcher.UIThread.CheckAccess());
                titleUpdates++;
            };

            Task.Run(() => state.ApplyLanguage("zh")).GetAwaiter().GetResult();
            Assert.Equal("Options", host.Title);
            Assert.Equal(0, titleUpdates);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("\u8bbe\u7f6e", host.Title);
            Assert.Equal(1, titleUpdates);
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void Host_does_not_rescan_already_translated_child_controls()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("zh");
        var editor = new CatalogTitleEditor("Options");
        var host = new EditorHostWindow(editor);
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("\u8bbe\u7f6e", editor.Label.Text);

            state.ApplyLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Options", editor.Label.Text);
            Assert.Equal("Options", host.Title);

            state.ApplyLanguage("ja");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("\u30aa\u30d7\u30b7\u30e7\u30f3", editor.Label.Text);
            Assert.Equal("\u30aa\u30d7\u30b7\u30e7\u30f3", host.Title);
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void Close_unsubscribes_and_discards_already_posted_title_update()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("en");
        var host = new EditorHostWindow(new CatalogTitleEditor("Options"));
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, GuiLocalizationState.SubscriberCount(host));
            Task.Run(() => state.ApplyLanguage("zh")).GetAwaiter().GetResult();
            Assert.Equal("Options", host.Title);

            host.Close();
            Assert.Equal(0, GuiLocalizationState.SubscriberCount(host));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Options", host.Title);

            host.Title = "Closed title";
            state.ApplyLanguage("ja");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Closed title", host.Title);
            Assert.Equal(0, GuiLocalizationState.SubscriberCount(host));
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void Immediate_close_cannot_leave_a_deferred_subscription()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("zh");
        var host = new EditorHostWindow(new CatalogTitleEditor("Options"));
        host.Show();
        host.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, GuiLocalizationState.SubscriberCount(host));

        host.Title = "Closed title";
        state.ApplyLanguage("en");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Closed title", host.Title);
    }

    [AvaloniaFact]
    public void Hide_and_show_does_not_duplicate_language_subscription()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("en");
        var host = new EditorHostWindow(new CatalogTitleEditor("Options"));
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            host.Hide();
            host.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, GuiLocalizationState.SubscriberCount(host));
            state.ApplyLanguage("zh");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("\u8bbe\u7f6e", host.Title);
        }
        finally { host.Close(); }
        Assert.Equal(0, GuiLocalizationState.SubscriberCount(host));
    }
}

[Collection("SharedState")]
public class MainWindowMaintenanceLocalizationTests
{
    [AvaloniaTheory]
    [InlineData("ja", "\u30d0\u30fc\u30b8\u30e7\u30f3 \u60c5\u5831(_V)")]
    [InlineData("zh", "\u7248\u672c \u4fe1\u606f(_V)")]
    public void Initial_version_menu_header_uses_shipped_catalog(string language, string expected)
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage(language);
        var window = new MainWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var item = window.FindControl<MenuItem>("VersionMenuItem");
            Assert.NotNull(item);
            Assert.Equal(expected, item.Header);
            Assert.NotEqual("_Version Information", item.Header);
            Assert.True(item.IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Version_menu_worker_language_round_trip_keeps_control_and_mnemonic()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("en");
        var window = new MainWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var item = window.FindControl<MenuItem>("VersionMenuItem")!;
            Assert.Equal("_Version Information", item.Header);
            int headerUpdates = 0;
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property != MenuItem.HeaderProperty) return;
                Assert.True(Dispatcher.UIThread.CheckAccess());
                headerUpdates++;
            };

            Task.Run(() => state.ApplyLanguage("zh")).GetAwaiter().GetResult();
            Assert.Equal("_Version Information", item.Header);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("\u7248\u672c \u4fe1\u606f(_V)", item.Header);

            state.ApplyLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("_Version Information", item.Header);
            Assert.Equal(2, headerUpdates);
            Assert.Same(item, window.FindControl<MenuItem>("VersionMenuItem"));
        }
        finally { window.Close(); }
    }
}

[Collection("WindowManagerSerial")]
public class SingleViewTitleLocalizationTests
{
    [AvaloniaTheory]
    [InlineData("Item Editor")]
    [InlineData("Unit Editor")]
    [InlineData("Class Editor")]
    [InlineData("Data Address Editor")]
    public void Translated_editor_single_view_title_retains_descriptor_key(string key)
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("zh");
        var previousService = WindowManager.Instance.Service;
        var navigation = new AndroidNavigationService();
        Window? window = null;
        try
        {
            WindowManager.Instance.SetService(navigation);
            var shell = new MainView();
            window = new Window { Content = shell };
            window.Show();
            IEmbeddableEditor OpenEditor() => key switch
            {
                "Item Editor" => navigation.Open<ItemEditorView>(),
                "Unit Editor" => navigation.Open<UnitEditorView>(),
                "Class Editor" => navigation.Open<ClassEditorView>(),
                "Data Address Editor" => navigation.Open<DumpStructSelectDialogView>(),
                _ => throw new ArgumentOutOfRangeException(nameof(key))
            };
            var editor = OpenEditor();
            var title = shell.FindControl<TextBlock>("TitleText")!;
            int stackChanges = 0;
            navigation.StackChanged += () => stackChanges++;
            foreach (string language in new[] { "zh", "en", "zh", "en" })
            {
                state.ApplyLanguage(language);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(key, editor.TitleKey);
                Assert.Equal(key, editor.Descriptor.Title);
                Assert.Equal(R._(key), editor.ViewTitle);
                Assert.Equal(R._(key), title.Text);
                Assert.Equal(editor.ViewTitle, navigation.CurrentTitle);
                Assert.Same(editor, navigation.CurrentContent);
                Assert.True(navigation.CanGoBack);
                Assert.Equal(0, stackChanges);
                if (language == "zh") Assert.NotEqual(key, title.Text);
            }
            Assert.Same(editor, OpenEditor());
            Assert.True(navigation.GoBack());
            Assert.False(navigation.CanGoBack);
            Assert.Null(navigation.CurrentTitle);
            Assert.Equal("FEBuilderGBA", title.Text);
        }
        finally
        {
            navigation.CloseAll();
            window?.Close();
            WindowManager.Instance.SetService(previousService);
        }
    }

    [AvaloniaFact]
    public void Real_missing_recent_file_message_retains_error_key_across_language_change()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("zh");
        var previousService = WindowManager.Instance.Service;
        var navigation = new AndroidNavigationService();
        Window? shellWindow = null;
        MainWindow? caller = null;
        try
        {
            WindowManager.Instance.SetService(navigation);
            var shell = new MainView();
            shellWindow = new Window { Content = shell };
            shellWindow.Show();
            caller = new MainWindow();
            typeof(MainWindow).GetMethod("RecentFileItem_Click",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(caller,
                    new object[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gba") });
            Dispatcher.UIThread.RunJobs();
            var content = Assert.IsType<MessageBoxContent>(navigation.CurrentContent);
            var title = shell.FindControl<TextBlock>("TitleText")!;
            Assert.Equal(R._("Error"), title.Text);

            state.ApplyLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Error", title.Text);
            Assert.Equal("Error", content.Descriptor.Title);
            Assert.Equal("Error", navigation.CurrentTitle);
            Assert.Same(content, navigation.CurrentContent);
            Assert.True(navigation.GoBack());
            Assert.False(navigation.CanGoBack);
        }
        finally
        {
            navigation.CloseAll();
            caller?.Close();
            shellWindow?.Close();
            WindowManager.Instance.SetService(previousService);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, "Options")]
    [InlineData(true, "Options")]
    [InlineData(false, "\u8bbe\u7f6e")]
    [InlineData(true, "\u8bbe\u7f6e")]
    public async Task Literal_modal_titles_are_not_catalog_keys(bool numeric, string literal)
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("zh");
        var previousService = WindowManager.Instance.Service;
        var navigation = new AndroidNavigationService();
        Window? window = null;
        try
        {
            WindowManager.Instance.SetService(navigation);
            var shell = new MainView();
            window = new Window { Content = shell };
            window.Show();
            Task modal = numeric
                ? NumberInputDialog.Show(window, "ROM/user prompt", literal, 7, 1, 10)
                : MessageBoxWindow.Show(window, "ROM/user message", literal, MessageBoxMode.Ok);
            Dispatcher.UIThread.RunJobs();
            var content = Assert.IsAssignableFrom<IEmbeddableEditor>(navigation.CurrentContent);
            var title = shell.FindControl<TextBlock>("TitleText")!;
            Assert.Null(content.TitleKey);
            Assert.Equal(literal, title.Text);
            state.ApplyLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(literal, title.Text);
            Assert.Equal(literal, content.Descriptor.Title);
            Assert.Null(content.TitleKey);
            Assert.True(navigation.GoBack());
            await modal;
        }
        finally
        {
            navigation.CloseAll();
            window?.Close();
            WindowManager.Instance.SetService(previousService);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_modal_titles_retain_keys_from_configure(bool numeric)
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("zh");
        var previousService = WindowManager.Instance.Service;
        var navigation = new AndroidNavigationService();
        Window? window = null;
        try
        {
            WindowManager.Instance.SetService(navigation);
            var shell = new MainView();
            window = new Window { Content = shell };
            window.Show();
            string key = numeric ? "List Expansion" : "Error";
            Task modal = numeric
                ? NumberInputDialog.Show(window, "Count prompt", key, 7, 1, 10, titleIsKey: true)
                : MessageBoxWindow.ShowSelectable(window, "Detailed error", key, MessageBoxMode.Ok, titleIsKey: true);
            Dispatcher.UIThread.RunJobs();
            var content = Assert.IsAssignableFrom<IEmbeddableEditor>(navigation.CurrentContent);
            var title = shell.FindControl<TextBlock>("TitleText")!;
            Assert.Equal(key, content.TitleKey);
            Assert.NotEqual(key, R._(key));
            Assert.Equal(R._(key), title.Text);
            state.ApplyLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(key, title.Text);
            Assert.Equal(key, content.Descriptor.Title);
            Assert.Equal(key, content.TitleKey);
            Assert.Equal(key, navigation.CurrentTitle);
            Assert.Same(content, navigation.CurrentContent);
            if (numeric)
                Assert.Equal(7, Assert.IsType<NumberInputContent>(content)
                    .FindControl<NumericUpDown>("ValueBox")!.Value);
            Assert.True(navigation.GoBack());
            await modal;
        }
        finally
        {
            navigation.CloseAll();
            window?.Close();
            WindowManager.Instance.SetService(previousService);
        }
    }

    [AvaloniaTheory]
    [InlineData("Options", "\u8bbe\u7f6e")]
    [InlineData("Version Information", "\u7248\u672c \u4fe1\u606f")]
    public void Single_view_translates_presentation_without_changing_navigation_keys(
        string key, string translated)
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("en");
        var previousService = WindowManager.Instance.Service;
        var navigation = new AndroidNavigationService();
        Window? window = null;
        try
        {
            WindowManager.Instance.SetService(navigation);
            var shell = new MainView();
            window = new Window { Content = shell };
            window.Show();
            IEmbeddableEditor editor = key == "Options"
                ? navigation.Open<OptionsView>()
                : navigation.Open<VersionView>();
            Dispatcher.UIThread.RunJobs();
            var title = shell.FindControl<TextBlock>("TitleText")!;
            Assert.Equal(key, title.Text);

            state.ApplyLanguage("zh");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(translated, title.Text);
            Assert.Equal(key, navigation.CurrentTitle);
            Assert.Equal(key, editor.Descriptor.Title);
            Assert.Same(editor, navigation.CurrentContent);
            Assert.True(navigation.CanGoBack);

            state.ApplyLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(key, title.Text);
            Assert.Same(editor, navigation.CurrentContent);
            Assert.True(navigation.GoBack());
            Assert.False(navigation.CanGoBack);
            Assert.Equal("FEBuilderGBA", title.Text);
        }
        finally
        {
            navigation.CloseAll();
            window?.Close();
            WindowManager.Instance.SetService(previousService);
        }
    }
}

sealed class CatalogTitleEditor : TranslatedUserControl, IEmbeddableEditor
{
    public TextBlock Label { get; } = new() { Text = "Options" };
    public EditorDescriptor Descriptor { get; }
    public string ViewTitle => Descriptor.Title;
    public new bool IsLoaded => true;
    public event EventHandler? CloseRequested;

    public CatalogTitleEditor(string title)
    {
        Descriptor = new(title, 320, 200);
        Content = Label;
    }

    public void NavigateTo(uint address) { }
    public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
}

sealed class GuiLocalizationState : IDisposable
{
    static readonly FieldInfo TranslationField = typeof(MyTranslateResource).GetField(
        "Resource", BindingFlags.Static | BindingFlags.NonPublic)!;
    static readonly FieldInfo LanguageEventField = typeof(CoreState).GetField(
        "LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic)!;
    readonly object? previousTranslations = TranslationField.GetValue(null);
    readonly string? previousLanguage = CoreState.Language;
    readonly string? previousBaseDirectory = CoreState.BaseDirectory;
    readonly Config? previousConfig = CoreState.Config;
    readonly ROM? previousRom = CoreState.ROM;
    readonly bool previousSmokeTestMode = App.SmokeTestMode;
    readonly string? previousStartupRom = App.StartupRomPath;
    readonly string? previousStartupProject = App.StartupProjectDir;
    readonly Window? previousMainWindow = WindowManager.Instance.MainWindow;

    public GuiLocalizationState()
    {
        TranslationField.SetValue(null, new MyTranslateResourceLow());
        CoreState.BaseDirectory = PatchDatabaseImportServiceTests.FindRepoRoot();
        CoreStateTestState.RestoreConfig(null);
        CoreStateTestState.RestoreRom(null);
        App.SmokeTestMode = true;
        App.StartupRomPath = null;
        App.StartupProjectDir = null;
    }

    public void ApplyLanguage(string language)
        => OptionsViewModel.ApplyLanguage(language, persist: false);

    public static int SubscriberCount(object target)
        => ((Delegate?)LanguageEventField.GetValue(null))?.GetInvocationList()
            .Count(handler => ReferenceEquals(handler.Target, target)) ?? 0;

    public void Dispose()
    {
        Dispatcher.UIThread.RunJobs();
        TranslationField.SetValue(null, previousTranslations);
        CoreStateTestState.RestoreLanguage(previousLanguage);
        CoreStateTestState.RestoreBaseDirectory(previousBaseDirectory);
        CoreStateTestState.RestoreConfig(previousConfig);
        CoreStateTestState.RestoreRom(previousRom);
        App.SmokeTestMode = previousSmokeTestMode;
        App.StartupRomPath = previousStartupRom;
        App.StartupProjectDir = previousStartupProject;
        WindowManager.Instance.MainWindow = previousMainWindow;
    }
}
