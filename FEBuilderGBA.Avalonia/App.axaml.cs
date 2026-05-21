using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using FEBuilderGBA.Avalonia.GapSweep;
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

        /// <summary>When true, run full data verification: iterate ALL list items per editor, not just the first.</summary>
        public static bool DataVerifyFullMode { get; set; }

        /// <summary>When true, capture screenshots of all editors and save as PNG.</summary>
        public static bool ScreenshotAllMode { get; set; }

        /// <summary>When true, export decoded editor images (not full screenshots) as PNG.</summary>
        public static bool ExportEditorImagesMode { get; set; }

        /// <summary>When true, run export→import→export roundtrip validation for graphics editors.</summary>
        public static bool ValidateImportMode { get; set; }

        /// <summary>When true, run palette export→import→export roundtrip validation.</summary>
        public static bool ValidatePaletteMode { get; set; }

        /// <summary>When true, run list parity comparison between Avalonia and WinForms editors.</summary>
        public static bool ListParityMode { get; set; }

        /// <summary>Directory to save screenshots. Defaults to ./screenshots beside the exe.</summary>
        public static string? ScreenshotDir { get; set; }

        /// <summary>Gap-sweep mode (Phase 1 density, Phase 2 labels, …) or null if no gap-sweep flag was passed.</summary>
        public static string? GapSweepMode { get; set; }

        /// <summary>Output path for gap-sweep reports. Required when GapSweepMode != null.</summary>
        public static string? GapSweepOut { get; set; }

        /// <summary>When true, gap-sweep writes only the YAML front-matter header (no body).</summary>
        public static bool GapSweepDryRun { get; set; }

        /// <summary>
        /// Repo root override for gap-sweep scanning. Defaults to walking up from the
        /// executable directory until a `FEBuilderGBA.sln` is found, or the current
        /// directory if no solution file is found.
        /// </summary>
        public static string? GapSweepRepoRoot { get; set; }

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

            // Initialize language from config (backward-compatible: try "Language", then "func_lang")
            if (CoreState.Config != null)
            {
                string lang = CoreState.Config.at("Language", CoreState.Config.at("func_lang", "auto"));
                CoreState.Language = lang;
            }
            ViewModels.OptionsViewModel.ReloadTranslations();

            // Apply saved theme preference
            ApplySavedTheme();

            // Parse command line arguments
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                ParseArgs(desktop.Args);

                // Gap-sweep flags are pure static-analysis (no ROM, no UI required).
                // Execute them BEFORE creating MainWindow and exit immediately so
                // they're fast (~1-2 seconds total) and don't pop a window in CI.
                if (GapSweepMode != null)
                {
                    int code = RunGapSweep();
                    Environment.Exit(code);
                    return;
                }

                desktop.MainWindow = new Views.MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Execute the gap-sweep flow chosen by <see cref="GapSweepMode"/>. Returns
        /// the process exit code (0 success, 2 missing required argument, 1 fatal).
        /// Wraps each scanner in an outer try/catch so a fatal exception terminates
        /// the run with code 1 rather than crashing the entire app. NOTE: the per-
        /// file scanners themselves currently swallow individual file-level parse
        /// / I/O failures and record 0 for the affected count; Phase 7 will add
        /// explicit error rows to the report so silent zero-counts don't masquerade
        /// as real migration gaps.
        /// </summary>
        static int RunGapSweep()
        {
            if (string.IsNullOrEmpty(GapSweepOut))
            {
                Console.Error.WriteLine("--gap-sweep-* requires --out=<path>");
                return 2;
            }

            string repoRoot = GapSweepRepoRoot ?? FindRepoRoot();
            Console.WriteLine($"GAPSWEEP[{GapSweepMode}]: repo-root={repoRoot} out={GapSweepOut} dry-run={GapSweepDryRun}");

            try
            {
                switch (GapSweepMode)
                {
                    case "density":
                        return RunDensitySweep(repoRoot, GapSweepOut!, GapSweepDryRun);

                    case "labels":
                    case "jumps":
                    case "undo":
                    case "l10n":
                    case "all":
                        // Phases 2-7 land in follow-up PRs. For Phase 0 we just
                        // write a header-only stub so callers can wire up CI now
                        // and have a file to look at when the phase ships.
                        var phaseInfo = new Dictionary<string, string>
                        {
                            ["status"] = "not-yet-implemented",
                        };
                        ReportWriter.WriteReport(
                            GapSweepOut!,
                            GapSweepMode!,
                            sections: new[]
                            {
                                $"# Gap-sweep `{GapSweepMode}` — not yet implemented",
                                "",
                                $"This sweep mode is a Phase 2+ follow-up to the Phase 1 density sweep.",
                                "Track the rollout on issue [#374](https://github.com/laqieer/FEBuilderGBA/issues/374).",
                            },
                            extraFrontMatter: phaseInfo,
                            gitWorkingDir: repoRoot);
                        Console.WriteLine($"GAPSWEEP[{GapSweepMode}]: stub report written (phase pending).");
                        return 0;

                    default:
                        Console.Error.WriteLine($"Unknown gap-sweep mode: {GapSweepMode}");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"GAPSWEEP[{GapSweepMode}]: fatal: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        /// <summary>
        /// Phase 1: control-density delta sweep. Returns 0 on success.
        /// In dry-run mode we still discover pairs (cheap) so we can log a count,
        /// but we write a truly header-only file (no markdown body, just the YAML
        /// front-matter) — callers use this to verify file-system permissions and
        /// the CLI plumbing without paying the Roslyn / XML scan cost.
        /// </summary>
        static int RunDensitySweep(string repoRoot, string outPath, bool dryRun)
        {
            var pairs = PairMatcher.DiscoverAll(repoRoot);
            Console.WriteLine($"GAPSWEEP[density]: discovered {pairs.Count} editor pairs.");

            if (dryRun)
            {
                // Header-only: no body sections. The front-matter alone proves
                // the writer can reach the path and emits valid YAML; that is the
                // entirety of what dry-run is meant to test.
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, ReportWriter.BuildFrontMatter("density", gitWorkingDir: repoRoot));
                Console.WriteLine("GAPSWEEP[density]: dry-run header written.");
                return 0;
            }

            var rows = ControlDensityScanner.Scan(pairs, repoRoot);
            Console.WriteLine($"GAPSWEEP[density]: scanned {rows.Count} non-empty rows.");

            string body = ControlDensityScanner.FormatReport(rows);
            ReportWriter.WriteReport(outPath, "density", new[] { body }, gitWorkingDir: repoRoot);
            Console.WriteLine($"GAPSWEEP[density]: report written to {outPath}");
            return 0;
        }

        /// <summary>
        /// Walk up from the executable directory looking for `FEBuilderGBA.sln`.
        /// Falls back to the current working directory if no solution is found
        /// (e.g. running a published binary outside the source tree).
        /// </summary>
        static string FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return Directory.GetCurrentDirectory();
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
                else if (args[i] == "--data-verify-full")
                {
                    SmokeTestMode = true;
                    DataVerifyMode = true;
                    DataVerifyFullMode = true;
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
                else if (args[i] == "--list-parity")
                {
                    SmokeTestMode = true;
                    ListParityMode = true;
                }
                else if (args[i] == "--gap-sweep-density")
                {
                    SmokeTestMode = true;
                    GapSweepMode = "density";
                }
                else if (args[i] == "--gap-sweep-labels")
                {
                    SmokeTestMode = true;
                    GapSweepMode = "labels";
                }
                else if (args[i] == "--gap-sweep-jumps")
                {
                    SmokeTestMode = true;
                    GapSweepMode = "jumps";
                }
                else if (args[i] == "--gap-sweep-undo")
                {
                    SmokeTestMode = true;
                    GapSweepMode = "undo";
                }
                else if (args[i] == "--gap-sweep-l10n")
                {
                    SmokeTestMode = true;
                    GapSweepMode = "l10n";
                }
                else if (args[i] == "--gap-sweep-all")
                {
                    SmokeTestMode = true;
                    GapSweepMode = "all";
                }
                else if (args[i] == "--dry-run")
                {
                    GapSweepDryRun = true;
                }
                else if (args[i].StartsWith("--out="))
                {
                    GapSweepOut = args[i].Substring("--out=".Length);
                }
                else if (args[i].StartsWith("--repo-root="))
                {
                    GapSweepRepoRoot = args[i].Substring("--repo-root=".Length);
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
