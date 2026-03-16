using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using FEBuilderGBA.SkiaSharp;

namespace FEBuilderGBA.Avalonia
{
    public partial class App : Application
    {
        /// <summary>Config key used to persist the theme preference.</summary>
        internal const string ThemeConfigKey = "Avalonia_Theme";

        /// <summary>Returns true when the app is currently using dark mode.</summary>
        public bool IsDarkMode => RequestedThemeVariant == ThemeVariant.Dark;

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

        /// <summary>When true, export decoded editor images (not full screenshots) as PNG.</summary>
        public static bool ExportEditorImagesMode { get; set; }

        /// <summary>When true, run export→import→export roundtrip validation for graphics editors.</summary>
        public static bool ValidateImportMode { get; set; }

        /// <summary>When true, run palette export→import→export roundtrip validation.</summary>
        public static bool ValidatePaletteMode { get; set; }

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

            // Apply saved theme preference
            ApplySavedTheme();

            // Parse command line arguments
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                ParseArgs(desktop.Args);
                desktop.MainWindow = new Views.MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>Toggle between light and dark mode, updating dynamic resources and persisting the choice.</summary>
        public void ToggleTheme()
        {
            bool switchToDark = !IsDarkMode;
            RequestedThemeVariant = switchToDark ? ThemeVariant.Dark : ThemeVariant.Light;
            ApplyThemeResources(switchToDark);

            // Persist
            if (CoreState.Config != null)
            {
                CoreState.Config[ThemeConfigKey] = switchToDark ? "Dark" : "Light";
                CoreState.Config.Save();
            }
        }

        void ApplySavedTheme()
        {
            bool dark = false;
            if (CoreState.Config != null)
            {
                string pref = CoreState.Config.at(ThemeConfigKey, "Light");
                dark = string.Equals(pref, "Dark", StringComparison.OrdinalIgnoreCase);
            }
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            ApplyThemeResources(dark);
        }

        void ApplyThemeResources(bool dark)
        {
            if (Resources == null) return;

            if (dark)
            {
                SetBrush("StatusBarBackgroundBrush", "#2D2D2D");
                SetBrush("ToolbarBackgroundBrush", "#333333");
                SetBrush("ToolbarBorderBrush", "#444444");
                SetBrush("SectionHeadingBrush", "#6B9BD2");
                SetBrush("CardBorderBrush", "#555555");
                SetBrush("InfoBannerBackgroundBrush", "#1A3A4A");
                SetBrush("InfoBannerBorderBrush", "#2A8A9A");
                SetBrush("InfoBannerTextBrush", "#8ECFDB");
                SetBrush("SubtlePanelBackgroundBrush", "#2A2A2A");
                SetBrush("SubtlePanelBorderBrush", "#444444");
                SetBrush("WarningBackgroundBrush", "#3D3520");
                SetBrush("WarningBorderBrush", "#AA8800");
                SetBrush("WarningTextBrush", "#D4A830");
                SetBrush("WarningTextSecondaryBrush", "#C4A040");
                SetBrush("DependencyWarningBackgroundBrush", "#3D3820");
                SetBrush("DependencyWarningBorderBrush", "#AA8800");
                SetBrush("AccentOverlayBrush", "#33FF8C00");
                SetBrush("ScriptCategoryButtonBrush", "#5070B0");
                SetBrush("ScriptCategoryOverlayBrush", "#30808080");
                SetBrush("ErrorDialogBackgroundBrush", "#3A2020");
                SetBrush("UndoInfoBackgroundBrush", "#1A2A3A");
                SetBrush("UndoInfoBorderBrush", "#406090");
                SetBrush("EmulatorWarningBackgroundBrush", "#3A3520");
                SetBrush("CharCodeHeaderBrush", "#4A6080");
                SetBrush("CharCodeCellBrush", "#3A3A3A");
                SetBrush("WelcomeBannerBrush", "#2A2A3A");
                SetBrush("WelcomeBannerBorderBrush", "#3A3A5A");
            }
            else
            {
                SetBrush("StatusBarBackgroundBrush", "#E8E8E8");
                SetBrush("ToolbarBackgroundBrush", "#F0F0F0");
                SetBrush("ToolbarBorderBrush", "#DDDDDD");
                SetBrush("SectionHeadingBrush", "#2B579A");
                SetBrush("CardBorderBrush", "#CCCCCC");
                SetBrush("InfoBannerBackgroundBrush", "#D1ECF1");
                SetBrush("InfoBannerBorderBrush", "#17A2B8");
                SetBrush("InfoBannerTextBrush", "#0C5460");
                SetBrush("SubtlePanelBackgroundBrush", "#F8F9FA");
                SetBrush("SubtlePanelBorderBrush", "#DEE2E6");
                SetBrush("WarningBackgroundBrush", "#FFF3CD");
                SetBrush("WarningBorderBrush", "#FFC107");
                SetBrush("WarningTextBrush", "#B8860B");
                SetBrush("WarningTextSecondaryBrush", "#8B6914");
                SetBrush("DependencyWarningBackgroundBrush", "#FFFDE8");
                SetBrush("DependencyWarningBorderBrush", "#F0C000");
                SetBrush("AccentOverlayBrush", "#33FF8C00");
                SetBrush("ScriptCategoryButtonBrush", "#4060A0");
                SetBrush("ScriptCategoryOverlayBrush", "#20808080");
                SetBrush("ErrorDialogBackgroundBrush", "#FFF8F8");
                SetBrush("UndoInfoBackgroundBrush", "#E3F2FD");
                SetBrush("UndoInfoBorderBrush", "#90CAF9");
                SetBrush("EmulatorWarningBackgroundBrush", "#FFF8E1");
                SetBrush("CharCodeHeaderBrush", "#FF6888A8");
                SetBrush("CharCodeCellBrush", "#FFE0E0E0");
                SetBrush("WelcomeBannerBrush", "#E8EAF6");
                SetBrush("WelcomeBannerBorderBrush", "#C5CAE9");
            }
        }

        void SetBrush(string key, string color)
        {
            if (Resources.ContainsKey(key) && Resources[key] is SolidColorBrush brush)
            {
                brush.Color = Color.Parse(color);
            }
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
                else if (args[i] == "--export-editor-images")
                {
                    SmokeTestMode = true;
                    ExportEditorImagesMode = true;
                }
                else if (args[i] == "--validate-import")
                {
                    SmokeTestMode = true;
                    ValidateImportMode = true;
                }
                else if (args[i] == "--validate-palette")
                {
                    SmokeTestMode = true;
                    ValidatePaletteMode = true;
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
