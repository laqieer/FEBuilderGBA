using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FEBuilderGBA.SkiaSharp;

namespace FEBuilderGBA.Avalonia
{
    public partial class App : Application
    {
        /// <summary>ROM path passed via --rom command line argument.</summary>
        public static string? StartupRomPath { get; set; }

        /// <summary>When true, run editor smoke test and exit.</summary>
        public static bool SmokeTestMode { get; set; }

        /// <summary>When true, run smoke test against ALL editors (not just Unit+Item).</summary>
        public static bool SmokeTestAll { get; set; }

        /// <summary>When true, force detailed editor mode (skip easy mode).</summary>
        public static bool ForceDetailMode { get; set; }

        /// <summary>When true, run data verification mode: open editors, load first item, verify data against raw ROM.</summary>
        public static bool DataVerifyMode { get; set; }

        /// <summary>When true, capture screenshots of all editors and save as PNG.</summary>
        public static bool ScreenshotAllMode { get; set; }

        /// <summary>Directory to save screenshots. Defaults to ./screenshots beside the exe.</summary>
        public static string? ScreenshotDir { get; set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Register code pages for Shift-JIS, etc.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Set up Core state
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            CoreState.BaseDirectory = baseDir;
            CoreState.Services = new AvaloniaAppServices();
            CoreState.ImageService = new SkiaImageService();

            // Wire headless caches
            CoreState.CommentCache = new HeadlessEtcCache();
            CoreState.LintCache = new HeadlessEtcCache();
            CoreState.WorkSupportCache = new HeadlessEtcCache();
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder();

            // Load config
            string configPath = Path.Combine(baseDir, "config", "config.xml");
            if (File.Exists(configPath))
            {
                var config = new Config();
                config.Load(configPath);
                CoreState.Config = config;
            }

            // Parse command line arguments
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                ParseArgs(desktop.Args);
                desktop.MainWindow = new Views.MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        static void ParseArgs(string[]? args)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--rom" && i + 1 < args.Length)
                {
                    StartupRomPath = args[++i];
                }
                else if (args[i] == "--smoke-test")
                {
                    SmokeTestMode = true;
                }
                else if (args[i] == "--smoke-test-all")
                {
                    SmokeTestMode = true;
                    SmokeTestAll = true;
                }
                else if (args[i] == "--force-detail")
                {
                    ForceDetailMode = true;
                }
                else if (args[i] == "--data-verify")
                {
                    SmokeTestMode = true;
                    DataVerifyMode = true;
                }
                else if (args[i] == "--screenshot-all")
                {
                    SmokeTestMode = true;
                    ScreenshotAllMode = true;
                }
                else if (args[i].StartsWith("--screenshot-dir="))
                {
                    ScreenshotDir = args[i].Substring("--screenshot-dir=".Length);
                }
                else if (args[i] == "--lastrom")
                {
                    // Load last ROM from config
                    if (CoreState.Config != null)
                    {
                        string lastRom = CoreState.Config.at("Last_Rom_Filename", "");
                        if (!string.IsNullOrEmpty(lastRom) && File.Exists(lastRom))
                        {
                            StartupRomPath = lastRom;
                        }
                    }
                }
                else if (args[i].StartsWith("--rom="))
                {
                    StartupRomPath = args[i].Substring("--rom=".Length);
                }
            }
        }
    }
}
