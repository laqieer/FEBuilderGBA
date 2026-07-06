using System;
using System.IO;
using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;
using FEBuilderGBA;

namespace FEBuilderGBA.iOS
{
    /// <summary>
    /// iOS entry-point delegate for the FEBuilderGBA Avalonia GUI (issue #1859) — the iOS
    /// counterpart of the Android <c>FEBuilderGBA.Android/MainActivity.cs</c>.
    ///
    /// <para>
    /// On iOS there is no classic desktop lifetime: <see cref="AvaloniaAppDelegate{TApp}"/>
    /// hosts the shared <see cref="global::FEBuilderGBA.Avalonia.App"/> under an
    /// <c>ISingleViewApplicationLifetime</c> (one root view). The shared
    /// <c>App.OnFrameworkInitializationCompleted</c> single-view branch sets
    /// <c>singleView.MainView = new Views.MainView()</c> (added for Android in #1122), so the
    /// iOS app presents the same editor-launcher shell — no App change was needed for iOS.
    /// </para>
    ///
    /// <para>
    /// CONFIG BOOTSTRAP (#1859): <see cref="FinishedLaunching"/> extracts the bundled
    /// <c>config/</c> <c>&lt;BundleResource&gt;</c> tree into an app-private writable dir on
    /// first run (version-stamped + idempotent, via the pure, desktop-unit-tested
    /// <see cref="AndroidConfigExtractorCore"/> + <see cref="DirectoryAssetSource"/>) and
    /// points <c>App.BaseDirectoryOverride</c> at it BEFORE the Avalonia app boots
    /// (<c>base.FinishedLaunching</c>), so Core's <c>PathUtil.ConfigPath</c> resolves
    /// <c>config/&lt;sub&gt;</c> on iOS. The iOS app bundle is read-only, so — like Android's
    /// <c>Context.FilesDir</c> extraction — config must live in a writable location for the
    /// app to persist <c>config.xml</c> and side files. <c>config/patch2</c> is NOT bundled
    /// (deferred — see docs/IOS.md).
    /// </para>
    ///
    /// <para>PREVIEW: the iOS runtime (touch UX, SAF-equivalent file pickers, editors) is
    /// build-validated only — see docs/IOS.md.</para>
    /// </summary>
    [Register("AppDelegate")]
    public partial class AppDelegate : AvaloniaAppDelegate<global::FEBuilderGBA.Avalonia.App>
    {
        const string LogCategory = "FEBuilderGBA";

        /// <summary>Dedicated app-private root under Library/ where config/ is extracted.</summary>
        const string AppDataFolderName = "febuildergba";

        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            // Extract the bundled config/ assets BEFORE the Avalonia app boots
            // (base.FinishedLaunching). The shared App.OnFrameworkInitializationCompleted
            // reads CoreState.BaseDirectory immediately to locate config/config.xml, so
            // BaseDirectoryOverride must already be set when it runs — mirrors the android
            // head's MainActivity.OnCreate ordering (extract, then base.OnCreate).
            ExtractConfigAndWireBaseDirectory();
            return base.FinishedLaunching(application, launchOptions);
        }

        /// <summary>
        /// First-run, version-stamped extraction of the bundled <c>config/</c> BundleResources
        /// into an app-private writable dir (#1859). On success, points
        /// <c>App.BaseDirectoryOverride</c> at the extracted root. On failure we log and
        /// RETHROW (fail fast): a booted app with missing/wrong config is worse than a
        /// visible crash, and the override is left null so desktop-style resolution does not
        /// silently mask the problem.
        /// </summary>
        void ExtractConfigAndWireBaseDirectory()
        {
            try
            {
                // The .app bundle is a real, read-only directory at runtime; config/ ships
                // under it (BundleResource LogicalName=config/...). Extract into a writable
                // app-private dir (Library/febuildergba) — Library is app-private + backed up,
                // the iOS analog of Android's Context.FilesDir.
                string bundleRoot = NSBundle.MainBundle.BundlePath;
                string libraryDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string targetRoot = Path.Combine(libraryDir, AppDataFolderName);
                Directory.CreateDirectory(targetRoot);

                string version = GetAppVersionString();

                var source = new DirectoryAssetSource(bundleRoot, "config");
                AndroidConfigExtractorCore.ExtractionResult result =
                    AndroidConfigExtractorCore.EnsureExtracted(source, targetRoot, version);

                Console.WriteLine($"[{LogCategory}] config extraction: {result} (version={version}, target={targetRoot})");

                // Only wire BaseDirectory AFTER a successful extraction.
                global::FEBuilderGBA.Avalonia.App.BaseDirectoryOverride = targetRoot;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{LogCategory}] config extraction FAILED: " + ex);
                throw; // fail fast — do not boot with missing config
            }
        }

        /// <summary>
        /// App version string used as the extraction stamp. A bump forces a clean re-extract.
        /// Reads <c>CFBundleVersion</c> from the bundle Info dictionary (populated by the
        /// csproj <c>ApplicationVersion</c>); falls back to <c>"unknown"</c> if absent — the
        /// same defensive contract as android's <c>MainActivity.GetAppVersionString</c>.
        /// </summary>
        static string GetAppVersionString()
        {
            NSObject? shortVersion = NSBundle.MainBundle.InfoDictionary?["CFBundleShortVersionString"];
            NSObject? build = NSBundle.MainBundle.InfoDictionary?["CFBundleVersion"];
            string shortStr = shortVersion?.ToString() ?? "0";
            string buildStr = build?.ToString() ?? "unknown";
            return shortStr + "-" + buildStr;
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // NOTE: the shared App.axaml already declares a <FluentTheme/>, so no extra
            // font/theme wiring is needed here. (The Avalonia template's .WithInterFont()
            // needs the separate Avalonia.Fonts.Inter package and is intentionally omitted,
            // mirroring the android head's minimal CustomizeAppBuilder.)
            return base.CustomizeAppBuilder(builder)
                .LogToTrace();
        }
    }
}
