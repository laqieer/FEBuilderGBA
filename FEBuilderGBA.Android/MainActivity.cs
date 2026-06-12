using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace FEBuilderGBA.Android
{
    /// <summary>
    /// Android entry-point activity for the FEBuilderGBA Avalonia GUI
    /// (exploration skeleton — epic #1070).
    ///
    /// <para>
    /// This is the Android equivalent of the desktop
    /// <c>FEBuilderGBA.Avalonia/Program.cs</c> <c>Main</c> +
    /// <c>StartWithClassicDesktopLifetime</c>. On Android there is no classic
    /// desktop lifetime: <see cref="AvaloniaMainActivity{TApp}"/> hosts the
    /// shared <see cref="global::FEBuilderGBA.Avalonia.App"/> under a
    /// <c>ISingleViewApplicationLifetime</c> (one Activity, one root view).
    /// </para>
    ///
    /// <para>
    /// HONEST SKELETON LIMITATION: the shared <c>App.OnFrameworkInitializationCompleted</c>
    /// only builds its UI inside an <c>if (ApplicationLifetime is
    /// IClassicDesktopStyleApplicationLifetime desktop)</c> branch (it sets
    /// <c>desktop.MainWindow = new MainWindow()</c>). Under the single-view
    /// Android lifetime that branch is NOT entered, so this skeleton boots the
    /// Avalonia runtime but does NOT yet present the editor UI. Wiring an
    /// <c>ISingleViewApplicationLifetime.MainView</c> + porting the
    /// <c>WindowManager</c> multi-window model to page navigation is the
    /// largest follow-up sub-issue (single-activity navigation model). The
    /// CoreState/config/storage bootstrap (config extraction to FilesDir, SAF
    /// ROM IO) are separate follow-up sub-issues. See docs/ANDROID.md.
    /// </para>
    /// </summary>
    // NOTE: no Theme/Icon resource is referenced — this skeleton ships no
    // Android resource (drawable/style) files, so referencing @drawable/icon or
    // a custom @style/ would fail aapt. A real port adds an app icon + theme.
    [Activity(
        Label = "FEBuilderGBA",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity<global::FEBuilderGBA.Avalonia.App>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // NOTE: the shared App.axaml already declares a <FluentTheme/>, so
            // no extra font/theme wiring is needed here. (The Avalonia
            // template's .WithInterFont() needs the separate Avalonia.Fonts.Inter
            // package and is intentionally omitted from this skeleton.)
            return base.CustomizeAppBuilder(builder)
                .LogToTrace();
        }
    }
}
