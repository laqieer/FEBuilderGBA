using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class PatchDatabaseStartupServiceTests
{
    const string FailureTemplate = "Startup failed. The application could not finish starting.\r\n{0}";

    [AvaloniaTheory]
    [InlineData("ja", false)]
    [InlineData("ja", true)]
    [InlineData("zh", false)]
    [InlineData("zh", true)]
    public async Task ConfiguredInteractiveStartupLocalizesRealLoadingAndFailureControls(string language, bool desktop)
    {
        using var fixture = new StartupFixture(language);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var app = fixture.Start(desktop, _ =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            Interlocked.Increment(ref calls);
            entered.SetResult();
            release.Wait();
            throw new IOException("owned recovery failure");
        });
        var status = fixture.Status;
        var startup = app.StartupTask!;
        try
        {
            Assert.Equal(language == "ja" ? "パッチデータベースを復旧しています…" : "正在恢复补丁数据库…", status.Text);
            Assert.Equal(language, CoreState.Language);
            Assert.Equal(fixture.ConfigPath, CoreState.Config.ConfigFilename);
            fixture.AssertNoConsumers();
            app.OnFrameworkInitializationCompleted();
            Assert.Same(startup, app.StartupTask);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            bool heartbeat = false;
            await Dispatcher.UIThread.InvokeAsync(() => heartbeat = true);
            Assert.True(heartbeat);
            Assert.False(startup.IsCompleted);
        }
        finally { release.Set(); await startup; }
        Assert.Equal(1, calls);
        Assert.Same(status, fixture.Status);
        Assert.Equal(language == "ja"
            ? "起動に失敗しました。アプリケーションの起動を完了できませんでした。\r\nowned recovery failure"
            : "启动失败。应用程序未能完成启动。\r\nowned recovery failure", status.Text);
        fixture.AssertNoConsumers();
        Assert.True(startup.IsCompletedSuccessfully);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogFailureStopsBeforeRecoveryAndKeepsAnObservedInertFailureRoot(bool desktop)
    {
        using var fixture = new StartupFixture("ja");
        using var locked = new FileStream(fixture.CatalogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        int calls = 0;
        var app = fixture.Start(desktop, _ => { calls++; throw new IOException("unexpected recovery"); });
        await app.StartupTask!;
        Assert.Equal(0, calls);
        Assert.StartsWith("Startup failed. The application could not finish starting.", fixture.Status.Text);
        Assert.True(app.StartupTask!.IsCompletedSuccessfully);
        fixture.AssertNoConsumers();
        app.OnFrameworkInitializationCompleted();
        Assert.Equal(0, calls);
    }

    [AvaloniaFact]
    public async Task MalformedStartupFailureTranslationRetainsDiagnosticWithoutFaultingTask()
    {
        using var fixture = new StartupFixture("ja");
        File.AppendAllText(fixture.CatalogPath, "\n:" + FailureTemplate.Replace("\r\n", "\\r\\n") + "\ninvalid {1}\n");
        var app = fixture.Start(false, _ => throw new IOException("owned diagnostic"));
        await app.StartupTask!;
        Assert.Equal(string.Format(FailureTemplate, "owned diagnostic"), fixture.Status.Text);
        fixture.AssertNoConsumers();
        Assert.True(app.StartupTask!.IsCompletedSuccessfully);
    }

    [AvaloniaTheory]
    [InlineData("precedence")]
    [InlineData("legacy")]
    [InlineData("auto")]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("blocked-directory")]
    [InlineData("read-only")]
    [InlineData("unreadable")]
    [InlineData("missing-catalog")]
    public async Task EarlyConfigBootstrapPreservesDefaultsFallbackAndNoSaveOrRepair(string scenario)
    {
        using var fixture = new StartupFixture("ja");
        string expectedLanguage = "ja", expectedText = "パッチデータベースを復旧しています…";
        if (scenario == "precedence")
            File.WriteAllText(fixture.ConfigPath, "<root><item><key>func_lang</key><value>zh</value></item><item><key>Language</key><value>ja</value></item></root>");
        if (scenario == "legacy")
        {
            File.WriteAllText(fixture.ConfigPath, "<root><item><key>func_lang</key><value>zh</value></item></root>");
            expectedLanguage = "zh";
            expectedText = "正在恢复补丁数据库…";
        }
        if (scenario == "auto")
        {
            File.WriteAllText(fixture.ConfigPath, "<root/>");
            System.Globalization.CultureInfo.CurrentCulture = new("zh-CN");
            expectedLanguage = "auto";
            expectedText = "正在恢复补丁数据库…";
        }
        if (scenario is "missing" or "blocked-directory")
        {
            Directory.Delete(Path.Combine(fixture.Root, "config"), true);
            if (scenario == "blocked-directory") File.WriteAllText(Path.Combine(fixture.Root, "config"), "owned obstruction");
        }
        if (scenario == "malformed") File.WriteAllText(fixture.ConfigPath, "<root><item>");
        if (scenario == "read-only") File.SetAttributes(fixture.ConfigPath, FileAttributes.ReadOnly);
        if (scenario == "missing-catalog") File.Delete(fixture.CatalogPath);
        if (scenario is "missing" or "malformed" or "blocked-directory" or "unreadable" or "missing-catalog")
        {
            expectedLanguage = scenario == "missing-catalog" ? "ja" : "auto";
            expectedText = "Recovering patch database…";
        }
        byte[]? original = File.Exists(fixture.ConfigPath) ? File.ReadAllBytes(fixture.ConfigPath) : null;
        FileAttributes? attributes = original == null ? null : File.GetAttributes(fixture.ConfigPath);
        FileStream? locked = scenario == "unreadable"
            ? new FileStream(fixture.ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        int languageEvents = 0;
        void Changed() => languageEvents++;
        CoreState.LanguageChanged += Changed;
        try
        {
            var app = fixture.Start(false, _ => throw new IOException("owned recovery stop"));
            Assert.Equal(expectedText, fixture.Status.Text);
            Assert.Equal(expectedLanguage, CoreState.Language);
            Assert.Equal(fixture.ConfigPath, CoreState.Config.ConfigFilename);
            await app.StartupTask!;
            fixture.AssertNoConsumers();
            Assert.Equal(0, languageEvents);
        }
        finally { locked?.Dispose(); CoreState.LanguageChanged -= Changed; }
        if (original == null) Assert.False(File.Exists(fixture.ConfigPath));
        else
        {
            Assert.Equal(original, File.ReadAllBytes(fixture.ConfigPath));
            Assert.Equal(attributes, File.GetAttributes(fixture.ConfigPath));
        }
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "config", "patch2")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, ".patch2-import")));
    }

    [AvaloniaFact]
    public async Task SuccessfulRecoveryRetainsEarlyCatalogAndStartsConsumersOnlyAfterRelease()
    {
        using var fixture = new StartupFixture("ja");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = fixture.Start(false, _ =>
        {
            entered.SetResult();
            release.Wait();
            return new PatchDatabaseImportCore.Result { Success = true };
        });
        var config = CoreState.Config;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.AssertNoConsumers();
            File.WriteAllText(fixture.ConfigPath, "<root><item><key>Language</key><value>en</value></item></root>");
            File.WriteAllText(fixture.CatalogPath, ":Recovering patch database…\nchanged catalog\n");
        }
        finally { release.Set(); await app.StartupTask!; }
        Assert.IsType<Views.MainView>(((ISingleViewApplicationLifetime)app.ApplicationLifetime!).MainView);
        Assert.NotNull(CoreState.CommentCache);
        Assert.NotNull(CoreState.SystemTextEncoder);
        Assert.NotNull(CoreState.AppendBinaryData);
        Assert.Same(config, CoreState.Config);
        Assert.Equal("ja", CoreState.Language);
        Assert.Equal("パッチデータベースを復旧しています…", R._("Recovering patch database…"));
        var consumers = CoreState.CommentCache;
        app.OnFrameworkInitializationCompleted();
        Assert.Same(consumers, CoreState.CommentCache);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalizedLoadingRootCloseOrReplacementSuppressesLateConsumers(bool desktop)
    {
        using var fixture = new StartupFixture("zh");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = fixture.Start(desktop, _ =>
        {
            entered.SetResult();
            release.Wait();
            return new PatchDatabaseImportCore.Result { Success = true };
        });
        var status = fixture.Status;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime) lifetime.MainWindow!.Close();
            else ((ISingleViewApplicationLifetime)app.ApplicationLifetime!).MainView = new Border();
        }
        finally { release.Set(); await app.StartupTask!; }
        fixture.AssertNoConsumers();
        Assert.Equal("正在恢复补丁数据库…", status.Text);
        app.OnFrameworkInitializationCompleted();
        fixture.AssertNoConsumers();
    }

    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("retained")]
    [InlineData("fault")]
    [InlineData("closed")]
    [InlineData("replaced")]
    public async Task LoadingLifetimePublishesOnlyAfterRecoveryAndSuppressesLateContinuation(string outcome)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool owns = true;
        int consumers = 0, failures = 0;
        var receipt = new PatchDatabaseImportCore.Result { Success = outcome == "success", RecoveryRequired = outcome == "retained" };
        var service = new PatchDatabaseStartupService(_ =>
        {
            entered.Set();
            release.Wait();
            if (outcome == "fault") throw new IOException("observed recovery fault");
            return receipt;
        });
        var task = service.CompleteAsync("captured", () => owns,
            result => { Assert.Same(receipt, result); consumers++; },
            ex => { Assert.Contains("observed recovery", ex.Message); failures++; });
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(0, consumers);
            Assert.False(task.IsCompleted);
            if (outcome is "closed" or "replaced") owns = false;
        }
        finally { release.Set(); await task; }
        Assert.Equal(outcome is "success" or "retained" ? 1 : 0, consumers);
        Assert.Equal(outcome == "fault" ? 1 : 0, failures);
    }

    [AvaloniaFact]
    public async Task RecoveryRunsOffDispatcherAndIsStartedExactlyOnce()
    {
        int ui = Environment.CurrentManagedThreadId;
        int worker = 0, calls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var receipt = new PatchDatabaseImportCore.Result { Success = true };
        var service = new PatchDatabaseStartupService(_ =>
        {
            worker = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref calls);
            entered.Set();
            // The baseline synchronous implementation fails without blocking the UI forever.
            if (worker != ui) release.Wait();
            return receipt;
        });
        Task<PatchDatabaseImportCore.Result>? task = null;
        try
        {
            task = service.StartAsync("captured-base");
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotEqual(ui, worker);
            Assert.Same(task, service.StartAsync("reattachment"));
            bool heartbeat = false;
            await Dispatcher.UIThread.InvokeAsync(() => heartbeat = true);
            Assert.True(heartbeat);
            Assert.False(task.IsCompleted);
        }
        finally
        {
            release.Set();
            if (task != null) Assert.Same(receipt, await task);
        }
        Assert.Equal(1, calls);
    }

    public class SingleViewLifetimeProxy : DispatchProxy
    {
        Control? mainView;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "get_MainView") return mainView;
            if (targetMethod.Name == "set_MainView") { mainView = (Control?)args![0]; return null; }
            throw new NotSupportedException(targetMethod.Name);
        }
    }

    sealed class StartupFixture : IDisposable
    {
        static readonly FieldInfo Resource = typeof(MyTranslateResource).GetField("Resource", BindingFlags.NonPublic | BindingFlags.Static)!;
        readonly object? savedResource = Resource.GetValue(null);
        readonly System.Globalization.CultureInfo savedCulture = System.Globalization.CultureInfo.CurrentCulture;
        readonly object? savedRecovery = typeof(App).GetField("patchDatabaseRecoveryState", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        readonly INavigationService savedNavigation = WindowManager.Instance.Service;
        readonly Window? savedMainWindow = WindowManager.Instance.MainWindow;
        readonly Dictionary<PropertyInfo, object?> coreState = typeof(CoreState).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.CanWrite).ToDictionary(p => p, p => p.GetValue(null));
        readonly Dictionary<PropertyInfo, object?> appState = typeof(App).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.CanWrite).ToDictionary(p => p, p => p.GetValue(null));
        App? app;
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "startup_locale_" + Guid.NewGuid().ToString("N"));
        internal string ConfigPath => Path.Combine(Root, "config", "config.xml");
        internal string CatalogPath { get; }
        internal TextBlock Status
        {
            get
            {
                var root = app!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow!.Content : ((ISingleViewApplicationLifetime)app.ApplicationLifetime!).MainView;
                return Assert.IsType<TextBlock>(Assert.IsType<Border>(root).Child);
            }
        }

        internal StartupFixture(string language)
        {
            string translate = Path.Combine(Root, "config", "translate");
            Directory.CreateDirectory(translate);
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo != null && !File.Exists(Path.Combine(repo.FullName, "FEBuilderGBA.sln"))) repo = repo.Parent;
            Assert.NotNull(repo);
            foreach (string code in new[] { "en", "ja", "zh" })
                File.Copy(Path.Combine(repo!.FullName, "config", "translate", code + ".txt"), Path.Combine(translate, code + ".txt"));
            CatalogPath = Path.Combine(translate, language + ".txt");
            File.WriteAllText(ConfigPath, "<root><item><key>Language</key><value>" + language + "</value></item></root>");
            foreach (var property in appState.Keys) property.SetValue(null, property.PropertyType == typeof(bool) ? false : null);
            App.BaseDirectoryOverride = Root;
            Resource.SetValue(null, new MyTranslateResourceLow());
            System.Globalization.CultureInfo.CurrentCulture = new("en-US");
            CoreState.Language = "en";
            CoreState.Config = null;
            CoreState.ROM = null;
            CoreState.CommentCache = null;
            CoreState.LintCache = null;
            CoreState.WorkSupportCache = null;
            CoreState.SystemTextEncoder = null;
            CoreState.AsmMapFileAsmCache = null;
            CoreState.ResourceCache = null;
            CoreState.AppendBinaryData = null;
        }

        internal App Start(bool desktop, Func<string, PatchDatabaseImportCore.Result> recover)
        {
            app = new App
            {
                ApplicationLifetime = desktop
                    ? new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnMainWindowClose }
                    : DispatchProxy.Create<ISingleViewApplicationLifetime, SingleViewLifetimeProxy>(),
            };
            typeof(App).GetField("patchDatabaseStartup", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(app, new PatchDatabaseStartupService(recover));
            app.OnFrameworkInitializationCompleted();
            return app;
        }

        internal void AssertNoConsumers()
        {
            Assert.Null(CoreState.ROM);
            Assert.Null(CoreState.CommentCache);
            Assert.Null(CoreState.LintCache);
            Assert.Null(CoreState.WorkSupportCache);
            Assert.Null(CoreState.SystemTextEncoder);
            Assert.Null(CoreState.AsmMapFileAsmCache);
            Assert.Null(CoreState.ResourceCache);
            Assert.Null(CoreState.AppendBinaryData);
        }

        public void Dispose()
        {
            if (app?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow?.Close();
            foreach (var entry in coreState) entry.Key.SetValue(null, entry.Value);
            foreach (var entry in appState) entry.Key.SetValue(null, entry.Value);
            Resource.SetValue(null, savedResource);
            System.Globalization.CultureInfo.CurrentCulture = savedCulture;
            typeof(App).GetField("patchDatabaseRecoveryState", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, savedRecovery);
            WindowManager.Instance.SetService(savedNavigation);
            WindowManager.Instance.MainWindow = savedMainWindow;
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
}
