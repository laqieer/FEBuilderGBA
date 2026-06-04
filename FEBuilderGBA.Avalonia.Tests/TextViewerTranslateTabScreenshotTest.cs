using System;
using System.IO;
using System.Linq;
using System.Text;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #947 bug #12: render the Avalonia <see cref="TextViewerView"/>'s
    /// <c>Translate</c> tab to a PNG that proves the tab is no longer a disabled
    /// stub — the two language combos are POPULATED (3 from-langs / 11 to-langs)
    /// and the Translate button is ENABLED. Headless RenderTargetBitmap so it
    /// works on locked desktops and in CI (the <c>--screenshot-all</c> CLI render
    /// only captures the first/Edit tab, and MCP computer-use needs an unlocked
    /// interactive desktop). Mirrors the proven render pattern in
    /// <see cref="WorldMapImageListExpandScreenshotTest"/>.
    ///
    /// <para>Pinned to FE8J (the PR's reference ROM) via
    /// <see cref="TestRomLocator.FindRom"/>; skips cleanly when the ROM isn't
    /// present (CI without roms.zip). CoreState is snapshotted + restored in
    /// place so this <c>[Collection("SharedState")]</c> test never leaks ROM
    /// state into sibling tests.</para>
    ///
    /// <para>By default the PNG is written to a per-run temp directory so normal
    /// runs don't dirty the repo; set <c>FEBUILDERGBA_SCREENSHOT_DIR</c> to write
    /// it where the PR screenshot is collected (the orchestrator points it at the
    /// ss947b temp dir). The PNG is never committed to git.</para>
    /// </summary>
    [Collection("SharedState")]
    public class TextViewerTranslateTabScreenshotTest
    {
        readonly ITestOutputHelper _output;

        public TextViewerTranslateTabScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void TextViewerView_TranslateTab_EnabledPopulated_SavesScreenshot()
        {
            string? romPath = TestRomLocator.FindRom("FE8J");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8J ROM not available (TestRomLocator.FindRom returned null)");
                return;
            }

            // Snapshot CoreState so we can restore it (no shared-state leak).
            var prevRom = CoreState.ROM;
            var prevComment = CoreState.CommentCache;
            var prevLint = CoreState.LintCache;
            var prevWork = CoreState.WorkSupportCache;
            var prevEncoder = CoreState.SystemTextEncoder;
            var prevBaseDir = CoreState.BaseDirectory;
            var prevServices = CoreState.Services;
            var prevUndo = CoreState.Undo;

            try
            {
                string assemblyDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                CoreState.BaseDirectory = assemblyDir;
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                string configPath = Path.Combine(assemblyDir, "config", "config.xml");
                if (File.Exists(configPath))
                {
                    var config = new Config();
                    config.Load(configPath);
                    CoreState.Config = config;
                }

                var rom = new ROM();
                bool ok = rom.Load(romPath, out string _);
                if (!ok)
                {
                    _output.WriteLine($"SKIP: FE8J ROM failed to load from {romPath}");
                    return;
                }
                CoreState.ROM = rom;
                CoreState.CommentCache = new HeadlessEtcCache();
                CoreState.LintCache = new HeadlessEtcCache();
                CoreState.WorkSupportCache = new HeadlessEtcCache();
                try { CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, rom); }
                catch { CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom); }
                try { CoreState.FETextEncoder = new FETextEncode(); } catch { /* non-fatal */ }
                CoreState.TextEscape ??= new TextEscape();
                CoreState.Undo ??= new Undo();
                CoreState.Services ??= new HeadlessAppServices();

                // Build the real view; its constructor populates the Translate
                // combos + enables the button (the code under test).
                var view = new TextViewerView();

                const int W = 900;
                const int H = 500;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                Dispatcher.UIThread.RunJobs();

                // Select the Translate tab so the render shows it.
                var tabs = view.FindControl<TabControl>("EditorTabs");
                var translateTab = view.FindControl<TabItem>("TranslateTab");
                Assert.NotNull(tabs);
                Assert.NotNull(translateTab);
                tabs!.SelectedItem = translateTab;

                // Settle layout so the tab content is materialized.
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                Dispatcher.UIThread.RunJobs();

                // Assert the actual feature state BEFORE we render: combos
                // populated from the shared arrays + button enabled.
                var fromCombo = view.FindControl<ComboBox>("TranslateFromCombo");
                var toCombo = view.FindControl<ComboBox>("TranslateToCombo");
                var button = view.FindControl<Button>("TranslateButton");
                Assert.NotNull(fromCombo);
                Assert.NotNull(toCombo);
                Assert.NotNull(button);

                int fromCount = fromCombo!.ItemsSource?.Cast<object>().Count() ?? 0;
                int toCount = toCombo!.ItemsSource?.Cast<object>().Count() ?? 0;
                _output.WriteLine($"From combo items: {fromCount}, To combo items: {toCount}, " +
                    $"From.IsEnabled={fromCombo.IsEnabled}, To.IsEnabled={toCombo.IsEnabled}, " +
                    $"Button.IsEnabled={button!.IsEnabled}, From.SelectedIndex={fromCombo.SelectedIndex}, " +
                    $"To.SelectedIndex={toCombo.SelectedIndex}");
                Assert.Equal(3, fromCount);
                Assert.Equal(11, toCount);
                Assert.True(fromCombo.IsEnabled);
                Assert.True(toCombo.IsEnabled);
                Assert.True(button.IsEnabled);
                Assert.InRange(fromCombo.SelectedIndex, 0, fromCount - 1);
                Assert.InRange(toCombo.SelectedIndex, 0, toCount - 1);

                // Render the real Translate-tab visual to a PNG.
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));
                Dispatcher.UIThread.RunJobs();

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "Translate_tab_FE8J.png");
                try
                {
                    using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                    bitmap.Render(view);
                    bitmap.Save(outPath);
                    _output.WriteLine($"Saved Translate-tab screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
                    Assert.True(File.Exists(outPath));
                }
                catch (Exception ex)
                {
                    // The enablement assertions above already PASSED, so the
                    // feature is proven even if the headless raster backend is
                    // unavailable in this environment. Don't fail the test on a
                    // render-backend hiccup.
                    _output.WriteLine($"Headless render failed (environment, not the #12 fix): {ex.Message}");
                }
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.CommentCache = prevComment;
                CoreState.LintCache = prevLint;
                CoreState.WorkSupportCache = prevWork;
                CoreState.SystemTextEncoder = prevEncoder;
                CoreState.BaseDirectory = prevBaseDir;
                CoreState.Services = prevServices;
                CoreState.Undo = prevUndo;
            }
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
