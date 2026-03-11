using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform.Storage;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        readonly MainWindowViewModel _vm = new();

        public MainWindow()
        {
            InitializeComponent();
            WindowManager.Instance.MainWindow = this;
            Opened += MainWindow_Opened;
            Closing += MainWindow_Closing;
            FilterTextBox.TextChanged += FilterTextBox_TextChanged;
        }

        private async void MainWindow_Opened(object? sender, EventArgs e)
        {
            // Auto-load ROM if --rom was specified
            if (!string.IsNullOrEmpty(App.StartupRomPath))
            {
                bool ok = LoadRomFile(App.StartupRomPath);
                if (!ok)
                {
                    if (App.SmokeTestMode)
                    {
                        Environment.ExitCode = 2;
                        Close();
                        return;
                    }
                    await MessageBoxWindow.Show(this, $"Failed to load ROM: {App.StartupRomPath}", "Error", MessageBoxMode.Ok);
                    return;
                }

                // If smoke test mode, run the editor selection test
                if (App.SmokeTestMode)
                {
                    // Use a short delay so the window is fully initialized
                    await Task.Delay(200);
                    RunSmokeTest();
                    return;
                }
            }
        }

        /// <summary>
        /// Load a ROM file and initialize all subsystems.
        /// Returns true on success.
        /// </summary>
        public bool LoadRomFile(string path)
        {
            ROM rom = new ROM();
            bool ok = rom.Load(path, out string version);
            if (!ok) return false;

            CoreState.ROM = rom;

            // Wire headless caches so Core code doesn't NullRef
            CoreState.CommentCache ??= new HeadlessEtcCache();
            CoreState.LintCache ??= new HeadlessEtcCache();
            CoreState.WorkSupportCache ??= new HeadlessEtcCache();

            // Wire text encoder — with HeadlessSystemTextEncoder fallback
            if (CoreState.SystemTextEncoder == null || CoreState.SystemTextEncoder is HeadlessSystemTextEncoder)
            {
                try
                {
                    CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, CoreState.ROM);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to init SystemTextEncoder, using headless fallback: {0}", ex.Message);
                    // Use ROM-aware fallback so JP ROMs get Shift_JIS, not ISO-8859-1
                    CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(CoreState.ROM);
                }
            }

            // Init Huffman text encoder
            if (CoreState.FETextEncoder == null)
            {
                try { CoreState.FETextEncoder = new FETextEncode(); }
                catch (Exception ex) { Log.Error("Failed to init FETextEncode: {0}", ex.Message); }
            }

            // Init text escape
            CoreState.TextEscape ??= new TextEscape();

            // Init flag cache
            if (CoreState.FlagCache == null)
            {
                try { CoreState.FlagCache = new EtcCacheFLag(); }
                catch (Exception ex) { Log.Error("Failed to init FlagCache: {0}", ex.Message); }
            }

            // Init export function + undo
            CoreState.ExportFunction ??= new ExportFunction();
            CoreState.Undo ??= new Undo();

            // Init event scripts
            try
            {
                if (CoreState.EventScript == null)
                {
                    CoreState.EventScript = new EventScript();
                    CoreState.EventScript.Load(EventScript.EventScriptType.Event);
                }
                if (CoreState.ProcsScript == null)
                {
                    CoreState.ProcsScript = new EventScript();
                    CoreState.ProcsScript.Load(EventScript.EventScriptType.Procs);
                }
                if (CoreState.AIScript == null)
                {
                    CoreState.AIScript = new EventScript();
                    CoreState.AIScript.Load(EventScript.EventScriptType.AI);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to init EventScripts: {0}", ex.Message);
            }

            // Update UI
            _vm.UpdateFromRom();
            StatusText.Text = _vm.StatusText;
            NoRomLabel.IsVisible = false;
            EditorPanel.IsVisible = true;
            SearchPanel.IsVisible = true;
            SaveMenuItem.IsEnabled = true;
            SaveAsMenuItem.IsEnabled = true;
            UndoMenuItem.IsEnabled = true;
            LintMenuItem.IsEnabled = true;
            DataExportMenuItem.IsEnabled = true;

            // Filter editor buttons for loaded ROM version
            UpdateEditorVisibility();

            // Show detected ROM version in status bar
            try
            {
                string ver = CoreState.ROM.RomInfo?.VersionToFilename ?? "Unknown";
                VersionDetectionLabel.Text = $"ROM: {ver}";
            }
            catch { VersionDetectionLabel.Text = ""; }

            // Remember last opened ROM
            if (CoreState.Config != null)
            {
                CoreState.Config["Last_Rom_Filename"] = path;
                CoreState.Config.Save();
            }

            return true;
        }

        private void RunSmokeTest()
        {
            if (App.ValidatePaletteMode)
            {
                RunValidatePalette();
                return;
            }

            if (App.ValidateImportMode)
            {
                RunValidateImport();
                return;
            }

            if (App.ExportEditorImagesMode)
            {
                RunExportEditorImages();
                return;
            }

            if (App.ScreenshotAllMode)
            {
                RunScreenshotAll();
                return;
            }

            if (App.DataVerifyMode)
            {
                RunDataVerify();
                return;
            }

            if (App.SmokeTestAll)
            {
                RunSmokeTestAll();
                return;
            }

            try
            {
                // Test 1: Open Unit Editor and select the first item
                var unitEditor = WindowManager.Instance.Open<UnitEditorView>();

                // Test 2: Open Item Editor and select the first item
                var itemEditor = WindowManager.Instance.Open<ItemEditorView>();

                // Give the windows time to load their lists, then select items
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        await Task.Delay(500);

                        // Select first item in each editor (index 0)
                        unitEditor.SelectFirstItem();
                        await Task.Delay(200);

                        itemEditor.SelectFirstItem();
                        await Task.Delay(200);

                        // If we got here without crashing, the smoke test passed
                        Environment.ExitCode = 0;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Smoke test failed: {0}", ex.Message);
                        Environment.ExitCode = 1;
                    }
                    finally
                    {
                        WindowManager.Instance.CloseAll();
                        Close();
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Error("Smoke test failed: {0}", ex.Message);
                Environment.ExitCode = 1;
                Close();
            }
        }

        /// <summary>
        /// Smoke test that opens every editor accessible from MainWindow.
        /// Opens each editor, lets it initialize, then closes it before moving on.
        /// Reports per-editor pass/fail to stdout.
        /// </summary>
        private void RunSmokeTestAll()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                int passed = 0;
                int failed = 0;
                var failures = new List<string>();

                var editors = GetAllEditorFactories();
                Console.WriteLine($"SMOKE: Testing {editors.Count} editors...");

                foreach (var (name, factory) in editors)
                {
                    try
                    {
                        var window = factory();
                        await Task.Delay(50);  // Let it initialize
                        try { window.Close(); } catch { }
                        passed++;
                        Console.WriteLine($"SMOKE: {name} ... OK");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add(name);
                        Console.WriteLine($"SMOKE: {name} ... FAIL: {ex.Message}");
                    }
                }

                WindowManager.Instance.CloseAll();

                Console.WriteLine($"SMOKE: Results: {passed} passed, {failed} failed out of {editors.Count}");
                if (failures.Count > 0)
                    Console.WriteLine($"SMOKE: Failures: {string.Join(", ", failures)}");

                Environment.ExitCode = failed > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Screenshot mode: opens every editor, captures a screenshot via RenderTargetBitmap,
        /// and saves it as PNG. Reports per-editor status to stdout.
        /// </summary>
        private void RunScreenshotAll()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                int captured = 0;
                int failed = 0;
                var failures = new List<string>();

                string screenshotDir = App.ScreenshotDir
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
                Directory.CreateDirectory(screenshotDir);

                string romVersion = CoreState.ROM?.RomInfo?.VersionToFilename ?? "Unknown";

                var editors = GetAllEditorFactories();
                Console.WriteLine($"SCREENSHOT: Capturing {editors.Count} editors...");

                foreach (var (name, factory) in editors)
                {
                    Window? window = null;
                    try
                    {
                        window = factory();
                        await Task.Delay(300); // Let the window initialize and render

                        // Try to select first item for richer screenshots
                        try
                        {
                            var method = window.GetType().GetMethod("SelectFirstItem");
                            method?.Invoke(window, null);
                            if (method != null)
                                await Task.Delay(100); // Let selection handler run
                        }
                        catch { /* Not all editors have SelectFirstItem */ }

                        // Capture screenshot via RenderTargetBitmap
                        var pixelSize = new PixelSize(
                            Math.Max((int)window.Width, 100),
                            Math.Max((int)window.Height, 100));
                        var dpi = new Vector(96, 96);
                        using var rtb = new RenderTargetBitmap(pixelSize, dpi);
                        rtb.Render(window);

                        string fileName = $"Avalonia_{name}_{romVersion}.png";
                        string filePath = Path.Combine(screenshotDir, fileName);
                        rtb.Save(filePath);

                        captured++;
                        Console.WriteLine($"SCREENSHOT: {name} ... OK ({filePath})");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add(name);
                        Console.WriteLine($"SCREENSHOT: {name} ... FAIL: {ex.Message}");
                    }
                    finally
                    {
                        try { window?.Close(); } catch { }
                    }
                }

                WindowManager.Instance.CloseAll();

                Console.WriteLine($"SCREENSHOT: Results: {captured} captured, {failed} failed out of {editors.Count}");
                if (failures.Count > 0)
                    Console.WriteLine($"SCREENSHOT: Failures: {string.Join(", ", failures)}");

                Environment.ExitCode = failed > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Run export→import→export roundtrip validation for all graphics editors.
        /// </summary>
        private void RunValidateImport()
        {
            Dispatcher.UIThread.Post(() =>
            {
                int failures = ImageImportValidator.RunAll();
                Environment.ExitCode = failures > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Run palette export→import→export roundtrip validation.
        /// </summary>
        private void RunValidatePalette()
        {
            Dispatcher.UIThread.Post(() =>
            {
                int failures = ImageImportValidator.RunAllPalette();
                Environment.ExitCode = failures > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Export decoded graphics editor images as PNG files.
        /// Unlike --screenshot-all (which captures full form screenshots), this exports
        /// only the decoded image data from each graphics ViewModel for pixel-level comparison.
        /// </summary>
        private void RunExportEditorImages()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                int exported = 0;
                int failed = 0;
                var failures = new List<string>();

                string outputDir = App.ScreenshotDir
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "editor_images");
                Directory.CreateDirectory(outputDir);

                string romVersion = CoreState.ROM?.RomInfo?.VersionToFilename ?? "Unknown";
                Console.WriteLine($"EXPORT_IMAGES: Exporting graphics editor images for {romVersion}...");

                // Define graphics editor exporters: (name, loadList, loadItem, getImage)
                var exporters = GetGraphicsEditorExporters();

                foreach (var (name, exporter) in exporters)
                {
                    try
                    {
                        var list = exporter.LoadList();
                        if (list == null || list.Count == 0)
                        {
                            Console.WriteLine($"EXPORT_IMAGES: {name} ... SKIP (empty list)");
                            continue;
                        }

                        // Export first 3 entries (or fewer if list is shorter)
                        int count = Math.Min(3, list.Count);
                        for (int i = 0; i < count; i++)
                        {
                            exporter.LoadItem(list[i].addr);
                            IImage image = exporter.GetImage();
                            if (image == null)
                            {
                                Console.WriteLine($"EXPORT_IMAGES: {name}_{i:D4} ... SKIP (null image)");
                                continue;
                            }

                            string fileName = $"Avalonia_{name}_{i:D4}_{romVersion}.png";
                            string filePath = Path.Combine(outputDir, fileName);
                            image.Save(filePath);
                            exported++;
                            Console.WriteLine($"EXPORT_IMAGES: {name}_{i:D4} ... OK ({filePath})");

                            if (image is IDisposable disposable) disposable.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add(name);
                        Console.WriteLine($"EXPORT_IMAGES: {name} ... FAIL: {ex.Message}");
                    }
                }

                Console.WriteLine($"EXPORT_IMAGES: Results: {exported} exported, {failed} failed");
                if (failures.Count > 0)
                    Console.WriteLine($"EXPORT_IMAGES: Failures: {string.Join(", ", failures)}");

                Environment.ExitCode = failed > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Helper record for graphics editor image export.
        /// </summary>
        record GraphicsExporter(
            Func<List<AddrResult>> LoadList,
            Action<uint> LoadItem,
            Func<IImage> GetImage);

        /// <summary>
        /// Returns named graphics editor exporters that can load lists, select items, and get images.
        /// </summary>
        static List<(string Name, GraphicsExporter Exporter)> GetGraphicsEditorExporters()
        {
            var result = new List<(string, GraphicsExporter)>();

            // Portrait
            var portrait = new PortraitViewerViewModel();
            result.Add(("PortraitViewer", new GraphicsExporter(
                () => portrait.LoadPortraitList(),
                addr => portrait.LoadPortrait(addr),
                () => portrait.TryLoadPortraitImage())));

            // Battle BG
            var battleBG = new BattleBGViewerViewModel();
            result.Add(("BattleBGViewer", new GraphicsExporter(
                () => battleBG.LoadBattleBGList(),
                addr => battleBG.LoadBattleBG(addr),
                () => battleBG.TryLoadImage())));

            // Battle Terrain
            var battleTerrain = new BattleTerrainViewerViewModel();
            result.Add(("BattleTerrainViewer", new GraphicsExporter(
                () => battleTerrain.LoadBattleTerrainList(),
                addr => battleTerrain.LoadBattleTerrain(addr),
                () => battleTerrain.TryLoadImage())));

            // Big CG
            var bigCG = new BigCGViewerViewModel();
            result.Add(("BigCGViewer", new GraphicsExporter(
                () => bigCG.LoadBigCGList(),
                addr => bigCG.LoadBigCG(addr),
                () => bigCG.TryLoadImage())));

            // Chapter Title
            var chapterTitle = new ChapterTitleViewerViewModel();
            result.Add(("ChapterTitleViewer", new GraphicsExporter(
                () => chapterTitle.LoadChapterTitleList(),
                addr => chapterTitle.LoadChapterTitle(addr),
                () => chapterTitle.TryLoadImage())));

            // Chapter Title FE7
            var chapterTitleFE7 = new ImageChapterTitleFE7ViewModel();
            result.Add(("ChapterTitleFE7Viewer", new GraphicsExporter(
                () => chapterTitleFE7.LoadList(),
                addr => chapterTitleFE7.LoadEntry(addr),
                () => chapterTitleFE7.TryLoadImage())));

            // Item Icon
            var itemIcon = new ItemIconViewerViewModel();
            result.Add(("ItemIconViewer", new GraphicsExporter(
                () => itemIcon.LoadItemIconList(),
                addr => itemIcon.LoadItemIcon(addr),
                () => itemIcon.TryLoadImage())));

            // System Icon
            var systemIcon = new SystemIconViewerViewModel();
            result.Add(("SystemIconViewer", new GraphicsExporter(
                () => systemIcon.LoadSystemIconList(),
                addr => systemIcon.LoadSystemIcon(addr),
                () => systemIcon.TryLoadImage())));

            // OP Class Font
            var opClassFont = new OPClassFontViewerViewModel();
            result.Add(("OPClassFontViewer", new GraphicsExporter(
                () => opClassFont.LoadOPClassFontList(),
                addr => opClassFont.LoadOPClassFont(addr),
                () => opClassFont.TryLoadImage())));

            // OP Prologue
            var opPrologue = new OPPrologueViewerViewModel();
            result.Add(("OPPrologueViewer", new GraphicsExporter(
                () => opPrologue.LoadOPPrologueList(),
                addr => opPrologue.LoadOPPrologue(addr),
                () => opPrologue.TryLoadImage())));

            // Portrait FE6
            var portraitFE6 = new ImagePortraitFE6ViewModel();
            result.Add(("ImagePortraitFE6", new GraphicsExporter(
                () => portraitFE6.LoadList(),
                addr => portraitFE6.LoadEntry(addr),
                () => portraitFE6.TryLoadImage())));

            // Background Image
            var imageBG = new ImageBGViewModel();
            result.Add(("ImageBG", new GraphicsExporter(
                () => imageBG.LoadList(),
                addr => imageBG.LoadEntry(addr),
                () => imageBG.TryLoadImage())));

            // CG Image
            var imageCG = new ImageCGViewModel();
            result.Add(("ImageCG", new GraphicsExporter(
                () => imageCG.LoadList(),
                addr => imageCG.LoadEntry(addr),
                () => imageCG.TryLoadImage())));

            // CG FE7U
            var imageCGFE7U = new ImageCGFE7UViewModel();
            result.Add(("ImageCGFE7U", new GraphicsExporter(
                () => imageCGFE7U.LoadList(),
                addr => imageCGFE7U.LoadEntry(addr),
                () => imageCGFE7U.TryLoadImage())));

            // TSA Animation
            var imageTSAAnime = new ImageTSAAnimeViewModel();
            result.Add(("ImageTSAAnime", new GraphicsExporter(
                () => imageTSAAnime.LoadList(),
                addr => imageTSAAnime.LoadEntry(addr),
                () => imageTSAAnime.TryLoadImage())));

            // Battle BG (per-entry editor)
            var imageBattleBG = new ImageBattleBGViewModel();
            result.Add(("ImageBattleBG", new GraphicsExporter(
                () => imageBattleBG.LoadList(),
                addr => imageBattleBG.LoadEntry(addr),
                () => imageBattleBG.TryLoadImage())));

            return result;
        }

        /// <summary>
        /// Data verification mode: opens each editor that implements IDataVerifiable,
        /// selects the first item, reads the ViewModel data report, cross-checks
        /// against raw ROM bytes, and prints structured results to stdout.
        /// </summary>
        private void RunDataVerify()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                int verified = 0;
                int failed = 0;
                int skipped = 0;
                var failures = new List<string>();

                var editors = GetAllEditorFactories();
                Console.WriteLine($"DATAVERIFY: Testing {editors.Count} editors...");

                foreach (var (name, factory) in editors)
                {
                    Window? window = null;
                    try
                    {
                        window = factory();
                        await Task.Delay(100); // Let it initialize

                        // Check if this view implements IDataVerifiableView
                        if (window is IDataVerifiableView verifiableView)
                        {
                            var vm = verifiableView.DataViewModel;
                            if (vm is IDataVerifiable verifiable)
                            {
                                // Select first item if possible
                                if (window is UnitEditorView uev) uev.SelectFirstItem();
                                else if (window is ItemEditorView iev) iev.SelectFirstItem();
                                else if (window is ClassEditorView cev) cev.SelectFirstItem();
                                else
                                {
                                    // Generic: try calling SelectFirstItem via reflection
                                    var method = window.GetType().GetMethod("SelectFirstItem");
                                    method?.Invoke(window, null);
                                }

                                await Task.Delay(100); // Let selection handler run

                                int listCount = verifiable.GetListCount();
                                var dataReport = verifiable.GetDataReport();
                                var rawReport = verifiable.GetRawRomReport();

                                // Print VERIFY line
                                var verifyParts = new List<string> { $"listCount={listCount}" };
                                foreach (var kv in dataReport)
                                    verifyParts.Add($"{kv.Key}={kv.Value}");
                                Console.WriteLine($"VERIFY: {name}|{string.Join("|", verifyParts)}");

                                // Print RAWROM line
                                var rawParts = new List<string>();
                                foreach (var kv in rawReport)
                                    rawParts.Add($"{kv.Key}={kv.Value}");
                                Console.WriteLine($"RAWROM: {name}|{string.Join("|", rawParts)}");

                                // Cross-check: compare data fields with raw ROM values
                                bool match = CrossCheckDataReport(name, dataReport, rawReport);

                                // UI check: verify NumericUpDown controls display values
                                // Only check when data was loaded (listCount > 0) — some editors
                                // have no data for certain ROM versions (e.g. CCBranch on FE6)
                                bool uiOk = listCount > 0
                                    ? CheckNumericUpDownsDisplayValues(name, window)
                                    : true;

                                if (match && uiOk)
                                {
                                    verified++;
                                    Console.WriteLine($"DATAVERIFY: {name} ... VERIFIED");
                                }
                                else
                                {
                                    failed++;
                                    failures.Add(name);
                                    if (!match) Console.WriteLine($"DATAVERIFY: {name} ... MISMATCH");
                                    if (!uiOk) Console.WriteLine($"DATAVERIFY: {name} ... UI_EMPTY");
                                }
                            }
                            else
                            {
                                skipped++;
                                Console.WriteLine($"DATAVERIFY: {name} ... SKIP (VM not IDataVerifiable)");
                            }
                        }
                        else
                        {
                            skipped++;
                            Console.WriteLine($"DATAVERIFY: {name} ... SKIP (no IDataVerifiableView)");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add(name);
                        Console.WriteLine($"DATAVERIFY: {name} ... FAIL: {ex.Message}");
                    }
                    finally
                    {
                        try { window?.Close(); } catch { }
                    }
                }

                WindowManager.Instance.CloseAll();

                // Text encoding verification: decode first text entry and check for garbled output
                VerifyTextEncoding();

                Console.WriteLine($"DATAVERIFY: Results: {verified} verified, {failed} failed, {skipped} skipped out of {editors.Count}");
                if (failures.Count > 0)
                    Console.WriteLine($"DATAVERIFY: Failures: {string.Join(", ", failures)}");

                Environment.ExitCode = failed > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Cross-checks the ViewModel data report against the raw ROM report.
        /// Returns true if all comparable fields match.
        /// </summary>
        static bool CrossCheckDataReport(string viewName,
            Dictionary<string, string> dataReport,
            Dictionary<string, string> rawReport)
        {
            if (dataReport.Count == 0 || rawReport.Count == 0) return false;

            // The data report has field names; the raw report has "u8@0x04" style keys.
            // The cross-check is: both should report the same "addr" value, and
            // the data values should be internally consistent (non-empty).
            if (dataReport.TryGetValue("addr", out string? dataAddr) &&
                rawReport.TryGetValue("addr", out string? rawAddr))
            {
                if (dataAddr != rawAddr)
                {
                    Console.WriteLine($"DATAVERIFY: {viewName} addr mismatch: data={dataAddr} raw={rawAddr}");
                    return false;
                }
            }

            // Verify all data report values are non-empty (they were loaded)
            foreach (var kv in dataReport)
            {
                if (string.IsNullOrEmpty(kv.Value))
                {
                    Console.WriteLine($"DATAVERIFY: {viewName} empty field: {kv.Key}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks that all NumericUpDown controls in the window display non-empty text.
        /// Returns true if no NumericUpDown has empty/null Value after data loading.
        /// This catches rendering bugs like FormatString="X" on decimal-typed NumericUpDown.
        /// </summary>
        static bool CheckNumericUpDownsDisplayValues(string viewName, Window window)
        {
            var nuds = new List<NumericUpDown>();
            foreach (var descendant in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window))
            {
                if (descendant is NumericUpDown nud)
                    nuds.Add(nud);
            }

            if (nuds.Count == 0) return true; // No NumericUpDown controls → pass

            int emptyCount = 0;
            var emptyNames = new List<string>();
            foreach (var nud in nuds)
            {
                if (nud.Value == null)
                {
                    emptyCount++;
                    string nudName = nud.Name ?? "(unnamed)";
                    emptyNames.Add(nudName);
                }
            }

            if (emptyCount > 0)
            {
                Console.WriteLine($"UIVERIFY: {viewName}|emptyNUDs={emptyCount}|names={string.Join(",", emptyNames)}");
                return false;
            }

            Console.WriteLine($"UIVERIFY: {viewName}|allNUDs={nuds.Count}|OK");
            return true;
        }

        /// <summary>
        /// Verifies text encoding is correctly initialized by decoding text ID 1
        /// and checking the result doesn't contain replacement characters (U+FFFD).
        /// Prints TEXTVERIFY lines for E2E test parsing.
        /// </summary>
        static void VerifyTextEncoding()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            string encoderType = CoreState.SystemTextEncoder?.GetType().Name ?? "null";
            string encoderDetail = "";
            if (CoreState.SystemTextEncoder is HeadlessSystemTextEncoder hse)
                encoderDetail = $"|encoding={hse.EncodingName}";

            bool isMultibyte = rom.RomInfo.is_multibyte;
            Console.WriteLine($"TEXTVERIFY: encoder={encoderType}{encoderDetail}|is_multibyte={isMultibyte}|version={rom.RomInfo.version}");

            try
            {
                // Decode text ID 1 (typically the first character's name)
                string decoded = FETextDecode.Direct(1);
                bool hasReplacement = decoded.Contains('\uFFFD');
                bool isEmpty = string.IsNullOrEmpty(decoded) || decoded == "???" || decoded == "(empty)";

                // For JP ROMs (is_multibyte), text should contain CJK characters (U+3000-U+FF00 range)
                bool hasCJK = false;
                if (isMultibyte && !isEmpty)
                {
                    foreach (char c in decoded)
                    {
                        if (c >= 0x3000 && c <= 0xFF00)
                        {
                            hasCJK = true;
                            break;
                        }
                    }
                }

                string status = "OK";
                if (hasReplacement) status = "REPLACEMENT_CHARS";
                else if (isEmpty) status = "EMPTY";
                else if (isMultibyte && !hasCJK) status = "NO_CJK";

                Console.WriteLine($"TEXTVERIFY: textId=1|decoded={EscapeForLog(decoded)}|status={status}|hasCJK={hasCJK}|hasReplacement={hasReplacement}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TEXTVERIFY: textId=1|error={ex.Message}");
            }
        }

        static string EscapeForLog(string s)
        {
            if (s == null) return "(null)";
            // Keep it short and escape control characters
            if (s.Length > 60) s = s.Substring(0, 60) + "...";
            return s.Replace("\r", "").Replace("\n", "\\n").Replace("|", "\\|");
        }

        /// <summary>
        /// Returns a list of (name, factory) pairs for all editors opened from MainWindow.
        /// </summary>
        static List<(string Name, Func<Window> Factory)> GetAllEditorFactories()
        {
            var wm = WindowManager.Instance;
            return new List<(string, Func<Window>)>
            {
                // Data Editors
                ("UnitEditorView", () => wm.Open<UnitEditorView>()),
                ("ItemEditorView", () => wm.Open<ItemEditorView>()),
                ("ClassEditorView", () => wm.Open<ClassEditorView>()),
                ("ClassFE6View", () => wm.Open<ClassFE6View>()),
                ("CCBranchEditorView", () => wm.Open<CCBranchEditorView>()),
                ("MoveCostEditorView", () => wm.Open<MoveCostEditorView>()),
                ("TerrainNameEditorView", () => wm.Open<TerrainNameEditorView>()),
                ("SupportUnitEditorView", () => wm.Open<SupportUnitEditorView>()),
                ("SupportAttributeView", () => wm.Open<SupportAttributeView>()),
                ("SupportTalkView", () => wm.Open<SupportTalkView>()),
                ("UnitFE6View", () => wm.Open<UnitFE6View>()),
                ("UnitActionPointerView", () => wm.Open<UnitActionPointerView>()),
                ("UnitCustomBattleAnimeView", () => wm.Open<UnitCustomBattleAnimeView>()),
                ("UnitIncreaseHeightView", () => wm.Open<UnitIncreaseHeightView>()),
                ("UnitPaletteView", () => wm.Open<UnitPaletteView>()),
                ("ClassOPDemoView", () => wm.Open<ClassOPDemoView>()),
                ("ClassOPFontView", () => wm.Open<ClassOPFontView>()),
                ("ExtraUnitView", () => wm.Open<ExtraUnitView>()),
                ("ExtraUnitFE8UView", () => wm.Open<ExtraUnitFE8UView>()),

                // Item Editors
                ("ItemWeaponEffectViewerView", () => wm.Open<ItemWeaponEffectViewerView>()),
                ("ItemStatBonusesViewerView", () => wm.Open<ItemStatBonusesViewerView>()),
                ("ItemEffectivenessViewerView", () => wm.Open<ItemEffectivenessViewerView>()),
                ("ItemPromotionViewerView", () => wm.Open<ItemPromotionViewerView>()),
                ("ItemShopViewerView", () => wm.Open<ItemShopViewerView>()),
                ("ItemWeaponTriangleViewerView", () => wm.Open<ItemWeaponTriangleViewerView>()),
                ("ItemUsagePointerViewerView", () => wm.Open<ItemUsagePointerViewerView>()),
                ("ItemEffectPointerViewerView", () => wm.Open<ItemEffectPointerViewerView>()),
                ("ItemIconViewerView", () => wm.Open<ItemIconViewerView>()),

                // Map Editors
                ("MapSettingView", () => wm.Open<MapSettingView>()),
                ("MapChangeView", () => wm.Open<MapChangeView>()),
                ("MapExitPointView", () => wm.Open<MapExitPointView>()),
                ("MapPointerView", () => wm.Open<MapPointerView>()),
                ("MapTileAnimationView", () => wm.Open<MapTileAnimationView>()),
                ("MapEditorView", () => wm.Open<MapEditorView>()),
                ("MapSettingFE6View", () => wm.Open<MapSettingFE6View>()),
                ("MapSettingFE7View", () => wm.Open<MapSettingFE7View>()),
                ("MapSettingFE7UView", () => wm.Open<MapSettingFE7UView>()),
                ("MapSettingDifficultyView", () => wm.Open<MapSettingDifficultyView>()),
                ("MapStyleEditorView", () => wm.Open<MapStyleEditorView>()),
                ("MapTerrainBGLookupView", () => wm.Open<MapTerrainBGLookupView>()),
                ("MapTerrainFloorLookupView", () => wm.Open<MapTerrainFloorLookupView>()),
                ("MapMiniMapTerrainImageView", () => wm.Open<MapMiniMapTerrainImageView>()),
                ("MapTileAnimation1View", () => wm.Open<MapTileAnimation1View>()),
                ("MapTileAnimation2View", () => wm.Open<MapTileAnimation2View>()),
                ("MapLoadFunctionView", () => wm.Open<MapLoadFunctionView>()),
                ("MapTerrainNameEngView", () => wm.Open<MapTerrainNameEngView>()),

                // Event Script Editors
                ("EventCondView", () => wm.Open<EventCondView>()),
                ("EventScriptView", () => wm.Open<EventScriptView>()),
                ("EventUnitView", () => wm.Open<EventUnitView>()),
                ("EventUnitFE6View", () => wm.Open<EventUnitFE6View>()),
                ("EventUnitFE7View", () => wm.Open<EventUnitFE7View>()),
                ("EventUnitColorView", () => wm.Open<EventUnitColorView>()),
                ("EventUnitItemDropView", () => wm.Open<EventUnitItemDropView>()),
                ("EventUnitNewAllocView", () => wm.Open<EventUnitNewAllocView>()),
                ("EventBattleTalkView", () => wm.Open<EventBattleTalkView>()),
                ("EventBattleTalkFE6View", () => wm.Open<EventBattleTalkFE6View>()),
                ("EventBattleTalkFE7View", () => wm.Open<EventBattleTalkFE7View>()),
                ("EventBattleDataFE7View", () => wm.Open<EventBattleDataFE7View>()),
                ("EventHaikuView", () => wm.Open<EventHaikuView>()),
                ("EventHaikuFE6View", () => wm.Open<EventHaikuFE6View>()),
                ("EventHaikuFE7View", () => wm.Open<EventHaikuFE7View>()),
                ("EventMapChangeView", () => wm.Open<EventMapChangeView>()),
                ("EventForceSortieView", () => wm.Open<EventForceSortieView>()),
                ("EventForceSortieFE7View", () => wm.Open<EventForceSortieFE7View>()),
                ("EventFunctionPointerView", () => wm.Open<EventFunctionPointerView>()),
                ("EventFunctionPointerFE7View", () => wm.Open<EventFunctionPointerFE7View>()),
                ("EventAssemblerView", () => wm.Open<EventAssemblerView>()),
                ("ProcsScriptView", () => wm.Open<ProcsScriptView>()),
                ("EventScriptTemplateView", () => wm.Open<EventScriptTemplateView>()),

                // AI Script Editors
                ("AIScriptView", () => wm.Open<AIScriptView>()),
                ("AIASMCALLTALKView", () => wm.Open<AIASMCALLTALKView>()),
                ("AIASMCoordinateView", () => wm.Open<AIASMCoordinateView>()),
                ("AIASMRangeView", () => wm.Open<AIASMRangeView>()),
                ("AIMapSettingView", () => wm.Open<AIMapSettingView>()),
                ("AIPerformItemView", () => wm.Open<AIPerformItemView>()),
                ("AIPerformStaffView", () => wm.Open<AIPerformStaffView>()),
                ("AIStealItemView", () => wm.Open<AIStealItemView>()),
                ("AITargetView", () => wm.Open<AITargetView>()),
                ("AITilesView", () => wm.Open<AITilesView>()),
                ("AIUnitsView", () => wm.Open<AIUnitsView>()),
                ("AOERANGEView", () => wm.Open<AOERANGEView>()),

                // Image Editors
                ("ImageViewerView", () => wm.Open<ImageViewerView>()),
                ("PortraitViewerView", () => wm.Open<PortraitViewerView>()),
                ("ImagePortraitView", () => wm.Open<ImagePortraitView>()),
                ("ImagePortraitFE6View", () => wm.Open<ImagePortraitFE6View>()),
                ("ImagePortraitImporterView", () => wm.Open<ImagePortraitImporterView>()),
                ("ImageBGView", () => wm.Open<ImageBGView>()),
                ("ImageBattleAnimeView", () => wm.Open<ImageBattleAnimeView>()),
                ("ImageBattleAnimePalletView", () => wm.Open<ImageBattleAnimePalletView>()),
                ("ImageBattleBGView", () => wm.Open<ImageBattleBGView>()),
                ("ImageBattleScreenView", () => wm.Open<ImageBattleScreenView>()),
                ("ImageCGView", () => wm.Open<ImageCGView>()),
                ("ImageCGFE7UView", () => wm.Open<ImageCGFE7UView>()),
                ("ImageUnitPaletteView", () => wm.Open<ImageUnitPaletteView>()),
                ("ImageUnitWaitIconView", () => wm.Open<ImageUnitWaitIconView>()),
                ("ImageUnitMoveIconView", () => wm.Open<ImageUnitMoveIconView>()),
                ("ImageSystemAreaView", () => wm.Open<ImageSystemAreaView>()),
                ("ImageGenericEnemyPortraitView", () => wm.Open<ImageGenericEnemyPortraitView>()),
                ("ImageRomAnimeView", () => wm.Open<ImageRomAnimeView>()),
                ("ImageTSAEditorView", () => wm.Open<ImageTSAEditorView>()),
                ("ImageTSAAnimeView", () => wm.Open<ImageTSAAnimeView>()),
                ("ImageTSAAnime2View", () => wm.Open<ImageTSAAnime2View>()),
                ("ImagePalletView", () => wm.Open<ImagePalletView>()),
                ("ImageMagicFEditorView", () => wm.Open<ImageMagicFEditorView>()),
                ("ImageMagicCSACreatorView", () => wm.Open<ImageMagicCSACreatorView>()),
                ("ImageMapActionAnimationView", () => wm.Open<ImageMapActionAnimationView>()),
                ("DecreaseColorTSAToolView", () => wm.Open<DecreaseColorTSAToolView>()),
                ("SystemIconViewerView", () => wm.Open<SystemIconViewerView>()),
                ("SystemHoverColorViewerView", () => wm.Open<SystemHoverColorViewerView>()),
                ("BattleBGViewerView", () => wm.Open<BattleBGViewerView>()),
                ("BattleTerrainViewerView", () => wm.Open<BattleTerrainViewerView>()),
                ("ChapterTitleViewerView", () => wm.Open<ChapterTitleViewerView>()),
                ("ImageChapterTitleFE7View", () => wm.Open<ImageChapterTitleFE7View>()),
                ("BigCGViewerView", () => wm.Open<BigCGViewerView>()),
                ("OPClassDemoViewerView", () => wm.Open<OPClassDemoViewerView>()),
                ("OPClassFontViewerView", () => wm.Open<OPClassFontViewerView>()),
                ("OPPrologueViewerView", () => wm.Open<OPPrologueViewerView>()),

                // Audio Editors
                ("SongTableView", () => wm.Open<SongTableView>()),
                ("SongTrackView", () => wm.Open<SongTrackView>()),
                ("SongInstrumentView", () => wm.Open<SongInstrumentView>()),
                ("SongInstrumentDirectSoundView", () => wm.Open<SongInstrumentDirectSoundView>()),
                ("SongInstrumentImportWaveView", () => wm.Open<SongInstrumentImportWaveView>()),
                ("SongTrackImportMidiView", () => wm.Open<SongTrackImportMidiView>()),
                ("SongExchangeView", () => wm.Open<SongExchangeView>()),
                ("SoundBossBGMViewerView", () => wm.Open<SoundBossBGMViewerView>()),
                ("SoundFootStepsViewerView", () => wm.Open<SoundFootStepsViewerView>()),
                ("SoundRoomViewerView", () => wm.Open<SoundRoomViewerView>()),
                ("SoundRoomFE6View", () => wm.Open<SoundRoomFE6View>()),
                ("SoundRoomCGView", () => wm.Open<SoundRoomCGView>()),

                // Arena / Monster / Summon Editors
                ("ArenaClassViewerView", () => wm.Open<ArenaClassViewerView>()),
                ("ArenaEnemyWeaponViewerView", () => wm.Open<ArenaEnemyWeaponViewerView>()),
                ("LinkArenaDenyUnitViewerView", () => wm.Open<LinkArenaDenyUnitViewerView>()),
                ("MonsterProbabilityViewerView", () => wm.Open<MonsterProbabilityViewerView>()),
                ("MonsterItemViewerView", () => wm.Open<MonsterItemViewerView>()),
                ("MonsterWMapProbabilityViewerView", () => wm.Open<MonsterWMapProbabilityViewerView>()),
                ("SummonUnitViewerView", () => wm.Open<SummonUnitViewerView>()),
                ("SummonsDemonKingViewerView", () => wm.Open<SummonsDemonKingViewerView>()),

                // Menu / ED / World Map Editors
                ("MenuDefinitionView", () => wm.Open<MenuDefinitionView>()),
                ("MenuCommandView", () => wm.Open<MenuCommandView>()),
                ("EDView", () => wm.Open<EDView>()),
                ("EDStaffRollView", () => wm.Open<EDStaffRollView>()),
                ("WorldMapPointView", () => wm.Open<WorldMapPointView>()),
                ("WorldMapBGMView", () => wm.Open<WorldMapBGMView>()),
                ("WorldMapEventPointerView", () => wm.Open<WorldMapEventPointerView>()),
                ("WorldMapPathView", () => wm.Open<WorldMapPathView>()),
                ("WorldMapPathEditorView", () => wm.Open<WorldMapPathEditorView>()),
                ("WorldMapImageView", () => wm.Open<WorldMapImageView>()),
                ("WorldMapImageFE6View", () => wm.Open<WorldMapImageFE6View>()),
                ("WorldMapImageFE7View", () => wm.Open<WorldMapImageFE7View>()),
                ("WorldMapEventPointerFE6View", () => wm.Open<WorldMapEventPointerFE6View>()),
                ("WorldMapEventPointerFE7View", () => wm.Open<WorldMapEventPointerFE7View>()),

                // Text / Translation Editors
                ("TextViewerView", () => wm.Open<TextViewerView>()),
                ("TextMainView", () => wm.Open<TextMainView>()),
                ("OtherTextView", () => wm.Open<OtherTextView>()),
                ("CStringView", () => wm.Open<CStringView>()),
                ("FontEditorView", () => wm.Open<FontEditorView>()),
                ("FontZHView", () => wm.Open<FontZHView>()),
                ("DevTranslateView", () => wm.Open<DevTranslateView>()),
                ("ToolTranslateROMView", () => wm.Open<ToolTranslateROMView>()),
                ("TextEscapeEditorView", () => wm.Open<TextEscapeEditorView>()),

                // Structural Data
                ("Command85PointerView", () => wm.Open<Command85PointerView>()),
                ("FE8SpellMenuExtendsView", () => wm.Open<FE8SpellMenuExtendsView>()),
                ("StatusOptionView", () => wm.Open<StatusOptionView>()),
                ("OAMSPView", () => wm.Open<OAMSPView>()),
                ("DumpStructSelectDialogView", () => wm.Open<DumpStructSelectDialogView>()),

                // Patch / Skill Systems
                ("PatchManagerView", () => wm.Open<PatchManagerView>()),
                ("ToolCustomBuildView", () => wm.Open<ToolCustomBuildView>()),
                ("SkillAssignmentUnitSkillSystemView", () => wm.Open<SkillAssignmentUnitSkillSystemView>()),
                ("SkillAssignmentClassSkillSystemView", () => wm.Open<SkillAssignmentClassSkillSystemView>()),
                ("SkillConfigSkillSystemView", () => wm.Open<SkillConfigSkillSystemView>()),

                // Tools
                ("ToolUndoView", () => wm.Open<ToolUndoView>()),
                ("ToolFELintView", () => wm.Open<ToolFELintView>()),
                ("ToolROMRebuildView", () => wm.Open<ToolROMRebuildView>()),
                ("ToolLZ77View", () => wm.Open<ToolLZ77View>()),
                ("ToolDiffView", () => wm.Open<ToolDiffView>()),
                ("ToolUPSPatchSimpleView", () => wm.Open<ToolUPSPatchSimpleView>()),
                ("ToolUPSOpenSimpleView", () => wm.Open<ToolUPSOpenSimpleView>()),
                ("ToolFlagNameView", () => wm.Open<ToolFlagNameView>()),
                ("ToolUseFlagView", () => wm.Open<ToolUseFlagView>()),
                ("ToolUnitTalkGroupView", () => wm.Open<ToolUnitTalkGroupView>()),
                ("ToolASMInsertView", () => wm.Open<ToolASMInsertView>()),
                ("HexEditorView", () => wm.Open<HexEditorView>()),
                ("DisASMView", () => wm.Open<DisASMView>()),
                ("LogViewerView", () => wm.Open<LogViewerView>()),
                ("GrowSimulatorView", () => wm.Open<GrowSimulatorView>()),
                ("OptionsView", () => wm.Open<OptionsView>()),

                // === GAP CLOSURE: New editors added by gap analysis ===

                // Status Screen Editors (WU2)
                ("StatusParamView", () => wm.Open<StatusParamView>()),
                ("StatusRMenuView", () => wm.Open<StatusRMenuView>()),
                ("StatusUnitsMenuView", () => wm.Open<StatusUnitsMenuView>()),
                ("StatusOptionOrderView", () => wm.Open<StatusOptionOrderView>()),

                // Skill System Editors (WU3)
                ("SkillAssignmentUnitCSkillSysView", () => wm.Open<SkillAssignmentUnitCSkillSysView>()),
                ("SkillAssignmentClassCSkillSysView", () => wm.Open<SkillAssignmentClassCSkillSysView>()),
                ("SkillAssignmentUnitFE8NView", () => wm.Open<SkillAssignmentUnitFE8NView>()),
                ("SkillConfigFE8NSkillView", () => wm.Open<SkillConfigFE8NSkillView>()),
                ("SkillConfigFE8NVer2SkillView", () => wm.Open<SkillConfigFE8NVer2SkillView>()),
                ("SkillConfigFE8NVer3SkillView", () => wm.Open<SkillConfigFE8NVer3SkillView>()),
                ("SkillConfigFE8UCSkillSys09xView", () => wm.Open<SkillConfigFE8UCSkillSys09xView>()),
                ("SkillSystemsEffectivenessReworkClassTypeView", () => wm.Open<SkillSystemsEffectivenessReworkClassTypeView>()),

                // Song/Audio Dialogs (WU4)
                ("ToolBGMMuteDialogView", () => wm.Open<ToolBGMMuteDialogView>()),

                // Event/Text Sub-forms (WU5)
                ("EventScriptCategorySelectView", () => wm.Open<EventScriptCategorySelectView>()),
                ("EventScriptPopupView", () => wm.Open<EventScriptPopupView>()),
                ("ProcsScriptCategorySelectView", () => wm.Open<ProcsScriptCategorySelectView>()),
                ("AIScriptCategorySelectView", () => wm.Open<AIScriptCategorySelectView>()),
                ("TextScriptCategorySelectView", () => wm.Open<TextScriptCategorySelectView>()),
                ("TextDicView", () => wm.Open<TextDicView>()),
                ("TextCharCodeView", () => wm.Open<TextCharCodeView>()),
                ("TextBadCharPopupView", () => wm.Open<TextBadCharPopupView>()),
                ("TextRefAddDialogView", () => wm.Open<TextRefAddDialogView>()),
                ("TextToSpeechView", () => wm.Open<TextToSpeechView>()),

                // Graphics Tool Forms (WU6)
                ("GraphicsToolView", () => wm.Open<GraphicsToolView>()),
                ("GraphicsToolPatchMakerView", () => wm.Open<GraphicsToolPatchMakerView>()),
                ("PaletteChangeColorsView", () => wm.Open<PaletteChangeColorsView>()),
                ("PaletteClipboardView", () => wm.Open<PaletteClipboardView>()),
                ("PaletteSwapView", () => wm.Open<PaletteSwapView>()),
                ("ImageBGSelectPopupView", () => wm.Open<ImageBGSelectPopupView>()),

                // Map Sub-dialog Forms (WU7)
                ("MapEditorAddMapChangeDialogView", () => wm.Open<MapEditorAddMapChangeDialogView>()),
                ("MapEditorMarSizeDialogView", () => wm.Open<MapEditorMarSizeDialogView>()),
                ("MapEditorResizeDialogView", () => wm.Open<MapEditorResizeDialogView>()),
                ("MapPointerNewPLISTPopupView", () => wm.Open<MapPointerNewPLISTPopupView>()),
                ("MapStyleEditorAppendPopupView", () => wm.Open<MapStyleEditorAppendPopupView>()),
                ("MapStyleEditorWarningOverrideView", () => wm.Open<MapStyleEditorWarningOverrideView>()),
                ("MapStyleEditorImportImageOptionView", () => wm.Open<MapStyleEditorImportImageOptionView>()),
                ("MapSettingDifficultyDialogView", () => wm.Open<MapSettingDifficultyDialogView>()),

                // Tool/Utility Forms Part 1 (WU8)
                ("DisASMDumpAllView", () => wm.Open<DisASMDumpAllView>()),
                ("DisASMDumpAllArgGrepView", () => wm.Open<DisASMDumpAllArgGrepView>()),
                ("HexEditorJumpView", () => wm.Open<HexEditorJumpView>()),
                ("HexEditorMarkView", () => wm.Open<HexEditorMarkView>()),
                ("HexEditorSearchView", () => wm.Open<HexEditorSearchView>()),
                ("PointerToolView", () => wm.Open<PointerToolView>()),
                ("PointerToolBatchInputView", () => wm.Open<PointerToolBatchInputView>()),
                ("PointerToolCopyToView", () => wm.Open<PointerToolCopyToView>()),
                ("PackedMemorySlotView", () => wm.Open<PackedMemorySlotView>()),
                ("EmulatorMemoryView", () => wm.Open<EmulatorMemoryView>()),

                // Tool/Utility Forms Part 2 (WU9)
                ("RAMRewriteToolMAPView", () => wm.Open<RAMRewriteToolMAPView>()),
                ("ToolAnimationCreatorView", () => wm.Open<ToolAnimationCreatorView>()),
                ("ToolThreeMargeView", () => wm.Open<ToolThreeMargeView>()),
                ("ToolASMEditView", () => wm.Open<ToolASMEditView>()),
                ("ToolExportEAEventView", () => wm.Open<ToolExportEAEventView>()),
                ("ToolDecompileResultView", () => wm.Open<ToolDecompileResultView>()),
                ("ToolChangeProjectnameView", () => wm.Open<ToolChangeProjectnameView>()),
                ("ToolAutomaticRecoveryROMHeaderView", () => wm.Open<ToolAutomaticRecoveryROMHeaderView>()),
                ("MoveToFreeSpaceView", () => wm.Open<MoveToFreeSpaceView>()),
                ("ToolSubtitleOverlayView", () => wm.Open<ToolSubtitleOverlayView>()),
                ("ToolSubtitleSettingDialogView", () => wm.Open<ToolSubtitleSettingDialogView>()),

                // Error/Dialog Forms (WU10)
                ("ErrorReportView", () => wm.Open<ErrorReportView>()),
                ("ErrorPaletteMissMatchView", () => wm.Open<ErrorPaletteMissMatchView>()),
                ("ErrorPaletteShowView", () => wm.Open<ErrorPaletteShowView>()),
                ("ErrorPaletteTransparentView", () => wm.Open<ErrorPaletteTransparentView>()),
                ("ErrorTSAErrorView", () => wm.Open<ErrorTSAErrorView>()),
                ("ErrorLongMessageDialogView", () => wm.Open<ErrorLongMessageDialogView>()),
                ("ErrorUnknownROMView", () => wm.Open<ErrorUnknownROMView>()),
                ("DumpStructSelectToTextDialogView", () => wm.Open<DumpStructSelectToTextDialogView>()),
                ("HowDoYouLikePatchView", () => wm.Open<HowDoYouLikePatchView>()),
                ("HowDoYouLikePatch2View", () => wm.Open<HowDoYouLikePatch2View>()),
                ("PatchFilterExView", () => wm.Open<PatchFilterExView>()),
                ("PatchFormUninstallDialogView", () => wm.Open<PatchFormUninstallDialogView>()),

                // Version-Specific / Specialized Forms (WU11)
                ("ItemFE6View", () => wm.Open<ItemFE6View>()),
                ("MoveCostFE6View", () => wm.Open<MoveCostFE6View>()),
                ("SupportUnitFE6View", () => wm.Open<SupportUnitFE6View>()),
                ("SupportTalkFE6View", () => wm.Open<SupportTalkFE6View>()),
                ("SupportTalkFE7View", () => wm.Open<SupportTalkFE7View>()),
                ("UnitFE7View", () => wm.Open<UnitFE7View>()),
                ("OPClassDemoFE7View", () => wm.Open<OPClassDemoFE7View>()),
                ("OPClassDemoFE7UView", () => wm.Open<OPClassDemoFE7UView>()),
                ("OPClassDemoFE8UView", () => wm.Open<OPClassDemoFE8UView>()),
                ("OPClassFontFE8UView", () => wm.Open<OPClassFontFE8UView>()),
                ("OPClassAlphaNameView", () => wm.Open<OPClassAlphaNameView>()),
                ("OPClassAlphaNameFE6View", () => wm.Open<OPClassAlphaNameFE6View>()),
                ("SomeClassListView", () => wm.Open<SomeClassListView>()),
                ("VennouWeaponLockView", () => wm.Open<VennouWeaponLockView>()),
                ("UnitsShortTextView", () => wm.Open<UnitsShortTextView>()),
                ("UbyteBitFlagView", () => wm.Open<UbyteBitFlagView>()),
                ("UshortBitFlagView", () => wm.Open<UshortBitFlagView>()),
                ("UwordBitFlagView", () => wm.Open<UwordBitFlagView>()),

                // === Event Templates (WU15) ===
                ("EventTemplate1View", () => wm.Open<EventTemplate1View>()),
                ("EventTemplate2View", () => wm.Open<EventTemplate2View>()),
                ("EventTemplate3View", () => wm.Open<EventTemplate3View>()),
                ("EventTemplate4View", () => wm.Open<EventTemplate4View>()),
                ("EventTemplate5View", () => wm.Open<EventTemplate5View>()),
                ("EventTemplate6View", () => wm.Open<EventTemplate6View>()),
                ("EventTemplateImplView", () => wm.Open<EventTemplateImplView>()),
                ("EventFinalSerifFE7View", () => wm.Open<EventFinalSerifFE7View>()),
                ("EventMoveDataFE7View", () => wm.Open<EventMoveDataFE7View>()),
                ("EventTalkGroupFE7View", () => wm.Open<EventTalkGroupFE7View>()),

                // === Audio Sub-Forms (WU16) ===
                ("SongTrackChangeTrackView", () => wm.Open<SongTrackChangeTrackView>()),
                ("SongTrackAllChangeTrackView", () => wm.Open<SongTrackAllChangeTrackView>()),
                ("SongTrackImportSelectInstrumentView", () => wm.Open<SongTrackImportSelectInstrumentView>()),
                ("SongTrackImportWaveView", () => wm.Open<SongTrackImportWaveView>()),

                // === ED/Credits + Item Variants (WU17) ===
                ("EDFE6View", () => wm.Open<EDFE6View>()),
                ("EDFE7View", () => wm.Open<EDFE7View>()),
                ("EDSensekiCommentView", () => wm.Open<EDSensekiCommentView>()),
                ("ItemStatBonusesSkillSystemsView", () => wm.Open<ItemStatBonusesSkillSystemsView>()),
                ("ItemStatBonusesVennoView", () => wm.Open<ItemStatBonusesVennoView>()),
                ("ItemEffectivenessSkillSystemsReworkView", () => wm.Open<ItemEffectivenessSkillSystemsReworkView>()),
                ("ItemRandomChestView", () => wm.Open<ItemRandomChestView>()),
                ("MenuExtendSplitMenuView", () => wm.Open<MenuExtendSplitMenuView>()),

                // === App Infrastructure (WU18) ===
                ("VersionView", () => wm.Open<VersionView>()),
                ("WelcomeView", () => wm.Open<WelcomeView>()),
                ("ResourceView", () => wm.Open<ResourceView>()),
                ("ToolInitWizardView", () => wm.Open<ToolInitWizardView>()),
                ("ToolUndoPopupDialogView", () => wm.Open<ToolUndoPopupDialogView>()),
                ("OpenLastSelectedFileView", () => wm.Open<OpenLastSelectedFileView>()),
                ("ToolUpdateDialogView", () => wm.Open<ToolUpdateDialogView>()),

                // === Previously Unregistered On-Disk Views (WU19) ===
                ("ToolAllWorkSupportView", () => wm.Open<ToolAllWorkSupportView>()),
                ("ToolProblemReportView", () => wm.Open<ToolProblemReportView>()),
                ("WorldMapPathMoveEditorView", () => wm.Open<WorldMapPathMoveEditorView>()),
                ("MantAnimationView", () => wm.Open<MantAnimationView>()),
                ("RAMRewriteToolView", () => wm.Open<RAMRewriteToolView>()),
                ("MainSimpleMenuView", () => wm.Open<MainSimpleMenuView>()),
                ("MainSimpleMenuEventErrorView", () => wm.Open<MainSimpleMenuEventErrorView>()),
                ("MainSimpleMenuImageSubView", () => wm.Open<MainSimpleMenuImageSubView>()),

                // === Small Dialog/Message Views (WU19-dialogs) ===
                ("ToolEmulatorSetupMessageView", () => wm.Open<ToolEmulatorSetupMessageView>()),
                ("ToolThreeMargeCloseAlertView", () => wm.Open<ToolThreeMargeCloseAlertView>()),
                ("ToolClickWriteFloatControlPanelButtonView", () => wm.Open<ToolClickWriteFloatControlPanelButtonView>()),
                ("ToolWorkSupport_UpdateQuestionDialogView", () => wm.Open<ToolWorkSupport_UpdateQuestionDialogView>()),
                ("MainSimpleMenuEventErrorIgnoreErrorView", () => wm.Open<MainSimpleMenuEventErrorIgnoreErrorView>()),
                ("ToolProblemReportSearchBackupView", () => wm.Open<ToolProblemReportSearchBackupView>()),
                ("ToolProblemReportSearchSavView", () => wm.Open<ToolProblemReportSearchSavView>()),

                // === Tool Support Views (WU19-tools) ===
                ("ToolWorkSupportView", () => wm.Open<ToolWorkSupportView>()),
                ("ToolWorkSupport_SelectUPSView", () => wm.Open<ToolWorkSupport_SelectUPSView>()),
                ("ToolDiffDebugSelectView", () => wm.Open<ToolDiffDebugSelectView>()),

                // === Specialized Views (WU19-specialized) ===
                ("SMEPromoListView", () => wm.Open<SMEPromoListView>()),
                ("ToolRunHintMessageView", () => wm.Open<ToolRunHintMessageView>()),
            };
        }

        private async void OpenRom_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (string.IsNullOrEmpty(path)) return;

            bool ok = LoadRomFile(path);
            if (!ok)
            {
                await MessageBoxWindow.Show(this, "Failed to load ROM.", "Error", MessageBoxMode.Ok);
            }
        }

        private void SaveRom_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;
            CoreState.ROM.Save(CoreState.ROM.Filename, false);
            CoreState.Services.ShowInfo("ROM saved.");
        }

        private async void SaveAsRom_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;

            string suggestedName = Path.GetFileName(CoreState.ROM.Filename ?? "rom.gba");
            var path = await FileDialogHelper.SaveRomFile(this, suggestedName);
            if (string.IsNullOrEmpty(path)) return;

            CoreState.ROM.Save(path, false);
            CoreState.Services.ShowInfo($"ROM saved as: {Path.GetFileName(path)}");
        }

        private void OpenLastRom_Click(object? sender, RoutedEventArgs e)
        {
            string lastPath = CoreState.Config?.at("Last_Rom_Filename", "") ?? "";
            if (string.IsNullOrEmpty(lastPath) || !File.Exists(lastPath))
            {
                _ = MessageBoxWindow.Show(this, "No recent ROM found.", "Open Last ROM", MessageBoxMode.Ok);
                return;
            }

            bool ok = LoadRomFile(lastPath);
            if (!ok)
            {
                _ = MessageBoxWindow.Show(this, $"Failed to load ROM: {lastPath}", "Error", MessageBoxMode.Ok);
            }
        }

        private void Undo_Click(object? sender, RoutedEventArgs e)
        {
            CoreState.Undo?.RunUndo();
        }

        private void Exit_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        // ===== Search / Filter =====

        private void FilterTextBox_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
        {
            ApplyFilter(FilterTextBox.Text ?? "");
        }

        private void ClearFilter_Click(object? sender, RoutedEventArgs e)
        {
            FilterTextBox.Text = "";
        }

        /// <summary>
        /// Show/hide buttons and section headers based on the filter text.
        /// Case-insensitive substring match on button Content.
        /// </summary>
        void ApplyFilter(string filter)
        {
            bool hasFilter = !string.IsNullOrWhiteSpace(filter);
            filter = filter.Trim();
            int matchCount = 0;

            foreach (var child in EditorPanel.Children)
            {
                if (child is not WrapPanel wp) continue;
                foreach (var item in wp.Children)
                {
                    if (item is not Button btn) continue;
                    string content = btn.Content?.ToString() ?? "";

                    // Respect version-hidden buttons (Tag == false means version-hidden)
                    if (btn.Tag is bool versionVisible && !versionVisible)
                    {
                        btn.IsVisible = false;
                        continue;
                    }

                    if (hasFilter)
                    {
                        bool match = content.Contains(filter, StringComparison.OrdinalIgnoreCase);
                        btn.IsVisible = match;
                        if (match) matchCount++;
                    }
                    else
                    {
                        btn.IsVisible = true;
                    }
                }
            }

            // Auto-hide section headers when no buttons are visible
            AutoHideEmptySections(EditorPanel);

            FilterMatchLabel.IsVisible = hasFilter;
            if (hasFilter)
                FilterMatchLabel.Text = $"{matchCount} editor(s) matching \"{filter}\"";
        }

        // ===== Closing Dirty Check =====

        private async void MainWindow_Closing(object? sender, global::Avalonia.Controls.WindowClosingEventArgs e)
        {
            // Check if ROM has unsaved changes via undo buffer
            var undo = CoreState.Undo;
            if (undo == null || CoreState.ROM == null) return;

            bool hasUnsavedChanges = undo.Postion != undo.PostionWhenFileSaving;
            if (!hasUnsavedChanges) return;

            // Cancel close, show prompt, then re-close if confirmed
            e.Cancel = true;
            var result = await MessageBoxWindow.Show(this,
                "You have unsaved changes. Close without saving?",
                "Unsaved Changes",
                MessageBoxMode.YesNo);

            if (result == MessageBoxResult.Yes)
            {
                // Detach handler to prevent re-entry, then close
                Closing -= MainWindow_Closing;
                Close();
            }
        }

        // ===== Editor Open Handlers =====

        private void OpenUnits_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<UnitEditorView>();
        }

        private void OpenItems_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemEditorView>();
        }

        private void OpenImageViewer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ImageViewerView>();
        }

        private void OpenClasses_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ClassEditorView>();
        }

        private void OpenMapSettings_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapSettingView>();
        }

        private void OpenTextViewer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<TextViewerView>();
        }

        private void OpenCCBranch_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<CCBranchEditorView>();
        }

        private void OpenTerrainNames_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<TerrainNameEditorView>();
        }

        private void OpenSongTable_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SongTableView>();
        }

        private void OpenPortraits_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<PortraitViewerView>();
        }

        private void OpenMoveCost_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MoveCostEditorView>();
        }

        private void OpenSupportUnits_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SupportUnitEditorView>();
        }

        private void OpenSupportAttribute_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SupportAttributeView>();
        }

        private void OpenSupportTalk_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SupportTalkView>();
        }

        private void OpenItemWeaponEffect_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemWeaponEffectViewerView>();
        }

        private void OpenItemStatBonuses_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemStatBonusesViewerView>();
        }

        private void OpenItemEffectiveness_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemEffectivenessViewerView>();
        }

        private void OpenItemPromotion_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemPromotionViewerView>();
        }

        private void OpenItemShop_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemShopViewerView>();
        }

        private void OpenItemWeaponTriangle_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemWeaponTriangleViewerView>();
        }

        private void OpenItemUsagePointer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemUsagePointerViewerView>();
        }

        private void OpenItemEffectPointer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemEffectPointerViewerView>();
        }

        private void OpenEventCond_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<EventCondView>();
        }

        private void OpenMapChange_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapChangeView>();
        }

        private void OpenMapExitPoint_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapExitPointView>();
        }

        private void OpenMapPointer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapPointerView>();
        }

        private void OpenMapTileAnimation_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapTileAnimationView>();
        }

        private void OpenSystemIcon_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SystemIconViewerView>();
        }

        private void OpenItemIcon_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemIconViewerView>();
        }

        private void OpenSystemHoverColor_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SystemHoverColorViewerView>();
        }

        private void OpenBattleBG_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<BattleBGViewerView>();
        }

        private void OpenBattleTerrain_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<BattleTerrainViewerView>();
        }

        private void OpenChapterTitle_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ChapterTitleViewerView>();
        }

        private void OpenBigCG_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<BigCGViewerView>();
        }

        private void OpenOPClassDemo_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<OPClassDemoViewerView>();
        }

        private void OpenOPClassFont_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<OPClassFontViewerView>();
        }

        private void OpenOPPrologue_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<OPPrologueViewerView>();
        }

        private void OpenSoundBossBGM_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SoundBossBGMViewerView>();
        }

        private void OpenSoundFootSteps_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SoundFootStepsViewerView>();
        }

        private void OpenSoundRoom_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SoundRoomViewerView>();
        }

        private void OpenArenaClass_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ArenaClassViewerView>();
        }

        private void OpenArenaEnemyWeapon_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ArenaEnemyWeaponViewerView>();
        }

        private void OpenLinkArenaDenyUnit_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<LinkArenaDenyUnitViewerView>();
        }

        private void OpenMonsterProbability_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MonsterProbabilityViewerView>();
        }

        private void OpenMonsterItem_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MonsterItemViewerView>();
        }

        private void OpenMonsterWMapProbability_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MonsterWMapProbabilityViewerView>();
        }

        private void OpenSummonUnit_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SummonUnitViewerView>();
        }

        private void OpenSummonsDemonKing_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SummonsDemonKingViewerView>();
        }

        private void OpenMenuDefinition_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MenuDefinitionView>();
        }

        private void OpenMenuCommand_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MenuCommandView>();
        }

        private void OpenED_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<EDView>();
        }

        private void OpenEDStaffRoll_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<EDStaffRollView>();
        }

        private void OpenWorldMapPoint_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<WorldMapPointView>();
        }

        private void OpenWorldMapBGM_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<WorldMapBGMView>();
        }

        private void OpenWorldMapEventPointer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<WorldMapEventPointerView>();
        }

        private void Lint_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;

            var scanner = new FELintScanner();
            var errors = scanner.Scan();

            if (errors.Count == 0)
            {
                _ = MessageBoxWindow.Show(this, "Lint: No errors found.", "Lint Results", MessageBoxMode.Ok);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{errors.Count} issue(s) found:");
                foreach (var err in errors)
                {
                    string severity = err.Severity == FELintCore.ErrorType.ERROR ? "ERROR" : "WARNING";
                    sb.AppendLine($"[{severity}] 0x{err.Addr:X08}: {err.ErrorMessage}");
                }
                _ = MessageBoxWindow.Show(this, sb.ToString(), "Lint Results", MessageBoxMode.Ok);
            }
        }

        private async void DataExport_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;
            var dialog = new DataExportView();
            await dialog.ShowDialog(this);
        }

        private async void About_Click(object? sender, RoutedEventArgs e)
        {
            await MessageBoxWindow.Show(this,
                "FEBuilderGBA\nAvalonia Cross-Platform Preview\nCopyright 2017- GPLv3",
                "About", MessageBoxMode.Ok);
        }

        // ===================== Image Editors =====================
        private void OpenImagePortrait_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImagePortraitView>();
        private void OpenImagePortraitFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImagePortraitFE6View>();
        private void OpenImagePortraitImporter_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImagePortraitImporterView>();
        private void OpenImageBG_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBGView>();
        private void OpenImageBattleAnime_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBattleAnimeView>();
        private void OpenImageBattleAnimePallet_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBattleAnimePalletView>();
        private void OpenImageBattleBG_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBattleBGView>();
        private void OpenImageBattleScreen_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBattleScreenView>();
        private void OpenImageCG_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageCGView>();
        private void OpenImageCGFE7U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageCGFE7UView>();
        private void OpenImageUnitPalette_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageUnitPaletteView>();
        private void OpenImageUnitWaitIcon_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageUnitWaitIconView>();
        private void OpenImageUnitMoveIcon_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageUnitMoveIconView>();
        private void OpenImageSystemArea_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageSystemAreaView>();
        private void OpenImageGenericEnemyPortrait_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageGenericEnemyPortraitView>();
        private void OpenImageRomAnime_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageRomAnimeView>();
        private void OpenImageTSAEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageTSAEditorView>();
        private void OpenImageTSAAnime_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageTSAAnimeView>();
        private void OpenImageTSAAnime2_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageTSAAnime2View>();
        private void OpenImagePallet_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImagePalletView>();
        private void OpenImageMagicFEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageMagicFEditorView>();
        private void OpenImageMagicCSACreator_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageMagicCSACreatorView>();
        private void OpenImageMapActionAnimation_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageMapActionAnimationView>();
        private void OpenDecreaseColorTSATool_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<DecreaseColorTSAToolView>();

        // ===================== Event Script Editors =====================
        private void OpenEventScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventScriptView>();
        private void OpenEventUnit_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitView>();
        private void OpenEventUnitFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitFE6View>();
        private void OpenEventUnitFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitFE7View>();
        private void OpenEventUnitColor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitColorView>();
        private void OpenEventUnitItemDrop_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitItemDropView>();
        private void OpenEventUnitNewAlloc_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitNewAllocView>();
        private void OpenEventBattleTalk_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventBattleTalkView>();
        private void OpenEventBattleTalkFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventBattleTalkFE6View>();
        private void OpenEventBattleTalkFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventBattleTalkFE7View>();
        private void OpenEventBattleDataFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventBattleDataFE7View>();
        private void OpenEventHaiku_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventHaikuView>();
        private void OpenEventHaikuFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventHaikuFE6View>();
        private void OpenEventHaikuFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventHaikuFE7View>();
        private void OpenEventMapChange_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventMapChangeView>();
        private void OpenEventForceSortie_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventForceSortieView>();
        private void OpenEventForceSortieFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventForceSortieFE7View>();
        private void OpenEventFunctionPointer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventFunctionPointerView>();
        private void OpenEventFunctionPointerFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventFunctionPointerFE7View>();
        private void OpenEventAssembler_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventAssemblerView>();
        private void OpenProcsScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ProcsScriptView>();
        private void OpenEventScriptTemplate_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventScriptTemplateView>();

        // ===================== AI Script Editors =====================
        private void OpenAIScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIScriptView>();
        private void OpenAIASMCALLTALK_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIASMCALLTALKView>();
        private void OpenAIASMCoordinate_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIASMCoordinateView>();
        private void OpenAIASMRange_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIASMRangeView>();
        private void OpenAIMapSetting_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIMapSettingView>();
        private void OpenAIPerformItem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIPerformItemView>();
        private void OpenAIPerformStaff_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIPerformStaffView>();
        private void OpenAIStealItem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIStealItemView>();
        private void OpenAITarget_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AITargetView>();
        private void OpenAITiles_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AITilesView>();
        private void OpenAIUnits_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIUnitsView>();
        private void OpenAOERANGE_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AOERANGEView>();

        // ===================== Map Editors =====================
        private void OpenMapEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapEditorView>();
        private void OpenMapSettingFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingFE6View>();
        private void OpenMapSettingFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingFE7View>();
        private void OpenMapSettingFE7U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingFE7UView>();
        private void OpenMapSettingDifficulty_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingDifficultyView>();
        private void OpenMapStyleEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapStyleEditorView>();
        private void OpenMapTerrainBGLookup_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTerrainBGLookupView>();
        private void OpenMapTerrainFloorLookup_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTerrainFloorLookupView>();
        private void OpenMapMiniMapTerrainImage_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapMiniMapTerrainImageView>();
        private void OpenMapTileAnimation1_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTileAnimation1View>();
        private void OpenMapTileAnimation2_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTileAnimation2View>();
        private void OpenMapLoadFunction_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapLoadFunctionView>();
        private void OpenMapTerrainNameEng_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTerrainNameEngView>();

        // ===================== Audio Editors =====================
        private void OpenSongTrack_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackView>();
        private void OpenSongInstrument_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongInstrumentView>();
        private void OpenSongInstrumentDirectSound_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongInstrumentDirectSoundView>();
        private void OpenSongInstrumentImportWave_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongInstrumentImportWaveView>();
        private void OpenSongTrackImportMidi_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackImportMidiView>();
        private void OpenSongExchange_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongExchangeView>();
        private void OpenSoundRoomFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SoundRoomFE6View>();
        private void OpenSoundRoomCG_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SoundRoomCGView>();

        // ===================== Unit/Class Specialized =====================
        private void OpenUnitFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitFE6View>();
        private void OpenUnitActionPointer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitActionPointerView>();
        private void OpenUnitCustomBattleAnime_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitCustomBattleAnimeView>();
        private void OpenUnitIncreaseHeight_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitIncreaseHeightView>();
        private void OpenUnitPalette_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitPaletteView>();
        private void OpenClassFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ClassFE6View>();
        private void OpenClassOPDemo_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ClassOPDemoView>();
        private void OpenClassOPFont_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ClassOPFontView>();
        private void OpenExtraUnit_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ExtraUnitView>();
        private void OpenExtraUnitFE8U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ExtraUnitFE8UView>();

        // ===================== Text/Translation =====================
        private void OpenTextMain_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<TextMainView>();
        private void OpenOtherText_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OtherTextView>();
        private void OpenCString_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<CStringView>();
        private void OpenFontEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<FontEditorView>();
        private void OpenFontZH_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<FontZHView>();
        private void OpenDevTranslate_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<DevTranslateView>();
        private void OpenToolTranslateROM_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolTranslateROMView>();
        private void OpenTextEscapeEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<TextEscapeEditorView>();

        // ===================== Patch/Mod =====================
        private void OpenPatchManager_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<PatchManagerView>();
        private void OpenToolCustomBuild_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolCustomBuildView>();

        // ===================== Skill Systems =====================
        private void OpenSkillAssignmentUnitSkillSystem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentUnitSkillSystemView>();
        private void OpenSkillAssignmentClassSkillSystem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentClassSkillSystemView>();
        private void OpenSkillConfigSkillSystem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigSkillSystemView>();

        // ===================== World Map =====================
        private void OpenWorldMapPath_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapPathView>();
        private void OpenWorldMapPathEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapPathEditorView>();
        private void OpenWorldMapImage_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapImageView>();
        private void OpenWorldMapImageFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapImageFE6View>();
        private void OpenWorldMapImageFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapImageFE7View>();
        private void OpenWorldMapEventPointerFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapEventPointerFE6View>();
        private void OpenWorldMapEventPointerFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapEventPointerFE7View>();

        // ===================== Structural Data =====================
        private void OpenCommand85Pointer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<Command85PointerView>();
        private void OpenFE8SpellMenuExtends_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<FE8SpellMenuExtendsView>();
        private void OpenStatusOption_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusOptionView>();
        private void OpenOAMSP_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OAMSPView>();
        private void OpenDumpStructSelectDialog_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<DumpStructSelectDialogView>();

        // ===================== Tools =====================
        private void OpenToolUndo_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUndoView>();
        private void OpenToolFELint_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolFELintView>();
        private void OpenToolROMRebuild_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolROMRebuildView>();
        private void OpenToolLZ77_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolLZ77View>();
        private void OpenToolDiff_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolDiffView>();
        private void OpenToolUPSPatchSimple_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUPSPatchSimpleView>();
        private void OpenToolUPSOpenSimple_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUPSOpenSimpleView>();
        private void OpenToolFlagName_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolFlagNameView>();
        private void OpenToolUseFlag_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUseFlagView>();
        private void OpenToolUnitTalkGroup_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUnitTalkGroupView>();
        private void OpenToolASMInsert_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolASMInsertView>();
        private void OpenHexEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<HexEditorView>();
        private void OpenDisASM_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<DisASMView>();
        private void OpenLogViewer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<LogViewerView>();
        private void OpenGrowSimulator_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<GrowSimulatorView>();
        private void OpenOptions_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OptionsView>();

        // ===================== Gap Closure: New Editors =====================

        // Status Screen (WU2)
        private void OpenStatusParam_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusParamView>();
        private void OpenStatusRMenu_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusRMenuView>();
        private void OpenStatusUnitsMenu_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusUnitsMenuView>();
        private void OpenStatusOptionOrder_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusOptionOrderView>();

        // Skill Systems Extended (WU3)
        private void OpenSkillAssignmentUnitCSkillSys_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentUnitCSkillSysView>();
        private void OpenSkillAssignmentClassCSkillSys_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentClassCSkillSysView>();
        private void OpenSkillAssignmentUnitFE8N_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentUnitFE8NView>();
        private void OpenSkillConfigFE8N_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigFE8NSkillView>();
        private void OpenSkillConfigFE8NVer2_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigFE8NVer2SkillView>();
        private void OpenSkillConfigFE8NVer3_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigFE8NVer3SkillView>();
        private void OpenSkillConfigFE8UCSkillSys09x_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigFE8UCSkillSys09xView>();
        private void OpenSkillSystemsEffectivenessRework_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillSystemsEffectivenessReworkClassTypeView>();

        // Audio (WU4)
        private void OpenToolBGMMuteDialog_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolBGMMuteDialogView>();

        // Tools (WU6, WU8, WU9)
        private void OpenGraphicsTool_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<GraphicsToolView>();
        private void OpenPointerTool_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<PointerToolView>();
        private void OpenMoveToFreeSpace_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MoveToFreeSpaceView>();

        // Version-Specific (WU11)
        private void OpenItemFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemFE6View>();
        private void OpenMoveCostFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MoveCostFE6View>();
        private void OpenSupportUnitFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SupportUnitFE6View>();
        private void OpenSupportTalkFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SupportTalkFE6View>();
        private void OpenSupportTalkFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SupportTalkFE7View>();
        private void OpenUnitFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitFE7View>();
        private void OpenOPClassDemoFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassDemoFE7View>();
        private void OpenOPClassDemoFE7U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassDemoFE7UView>();
        private void OpenOPClassDemoFE8U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassDemoFE8UView>();
        private void OpenOPClassFontFE8U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassFontFE8UView>();
        private void OpenOPClassAlphaName_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassAlphaNameView>();
        private void OpenOPClassAlphaNameFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassAlphaNameFE6View>();
        private void OpenSomeClassList_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SomeClassListView>();
        private void OpenVennouWeaponLock_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<VennouWeaponLockView>();
        private void OpenUnitsShortText_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitsShortTextView>();

        // ===================== WU15: Event Templates =====================
        private void OpenEventTemplate1_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate1View>();
        private void OpenEventTemplate2_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate2View>();
        private void OpenEventTemplate3_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate3View>();
        private void OpenEventTemplate4_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate4View>();
        private void OpenEventTemplate5_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate5View>();
        private void OpenEventTemplate6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate6View>();
        private void OpenEventFinalSerifFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventFinalSerifFE7View>();
        private void OpenEventMoveDataFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventMoveDataFE7View>();
        private void OpenEventTalkGroupFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTalkGroupFE7View>();

        // ===================== WU16: Audio Sub-Forms =====================
        private void OpenSongTrackChangeTrack_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackChangeTrackView>();
        private void OpenSongTrackAllChangeTrack_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackAllChangeTrackView>();
        private void OpenSongTrackImportSelectInstrument_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackImportSelectInstrumentView>();
        private void OpenSongTrackImportWave_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackImportWaveView>();

        // ===================== WU17: ED/Item Variants =====================
        private void OpenEDFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EDFE6View>();
        private void OpenEDFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EDFE7View>();
        private void OpenEDSensekiComment_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EDSensekiCommentView>();
        private void OpenItemStatBonusesSkillSystems_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemStatBonusesSkillSystemsView>();
        private void OpenItemStatBonusesVenno_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemStatBonusesVennoView>();
        private void OpenItemEffectivenessSkillSystemsRework_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemEffectivenessSkillSystemsReworkView>();
        private void OpenItemRandomChest_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemRandomChestView>();
        private void OpenMenuExtendSplitMenu_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MenuExtendSplitMenuView>();

        // ===================== WU19: Help & External Tools =====================
        private void OnlineManual_Click(object? sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/laqieer/FEBuilderGBA/wiki") { UseShellExecute = true }); }
            catch { }
        }

        private void Discord_Click(object? sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://discord.gg/febuilder") { UseShellExecute = true }); }
            catch { }
        }

        private async void RunEmulator_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("Emulator_Path", "emulator");
        }

        private async void RunBinaryEditor_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("BinaryEditor_Path", "binary editor");
        }

        private async void RunSappy_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("Sappy_Path", "Sappy");
        }

        private async void RunCustomTool_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("CustomTool_Path", "custom tool");
        }

        private async System.Threading.Tasks.Task RunExternalTool(string configKey, string toolName)
        {
            string path = CoreState.Config?.at(configKey, "") ?? "";
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                await MessageBoxWindow.Show(this,
                    $"No {toolName} configured. Set the path in Options first.",
                    "External Tool", MessageBoxMode.Ok);
                return;
            }
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
                if (CoreState.ROM != null && !string.IsNullOrEmpty(CoreState.ROM.Filename))
                    psi.Arguments = $"\"{CoreState.ROM.Filename}\"";
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                await MessageBoxWindow.Show(this, $"Failed to run {toolName}: {ex.Message}", "Error", MessageBoxMode.Ok);
            }
        }
        // ==================================================================
        // Editor visibility filtering — hide buttons that don't match the
        // loaded ROM version so the UI is not cluttered with irrelevant editors.
        // ==================================================================

        /// <summary>
        /// Orchestrator: reset all visibility, then hide mismatched buttons/sections.
        /// Called from LoadRomFile() after EditorPanel becomes visible.
        /// </summary>
        void UpdateEditorVisibility()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            int ver = rom.RomInfo.version;
            bool isMultibyte = rom.RomInfo.is_multibyte;

            ResetAllButtonVisibility(EditorPanel);

            HideVersionMismatchedButtons(EditorPanel, ver, isMultibyte);

            // FE8-only whole sections
            bool isFE8 = ver == 8;
            SetSectionVisible(MonstersHeader, MonstersPanel, isFE8);
            SetSectionVisible(SummonsHeader, SummonsPanel, isFE8);
            SetSectionVisible(SkillsHeader, SkillsPanel, isFE8);
            SetSectionVisible(SkillsExtHeader, SkillsExtPanel, isFE8);

            // Senseki Comment is FE7-only (no version suffix in its Content)
            SensekiCommentButton.IsVisible = ver == 7;

            AutoHideEmptySections(EditorPanel);
        }

        /// <summary>
        /// Reset all buttons and section headers inside the panel to visible.
        /// </summary>
        static void ResetAllButtonVisibility(StackPanel panel)
        {
            foreach (var child in panel.Children)
            {
                child.IsVisible = true;
                if (child is WrapPanel wp)
                {
                    foreach (var btn in wp.Children)
                    {
                        btn.IsVisible = true;
                        if (btn is Button b) b.Tag = null; // clear version tag
                    }
                }
            }
        }

        /// <summary>
        /// Walk all WrapPanels inside the editor panel, check each Button's Content
        /// text for a version tag, and hide buttons that don't match.
        /// Also stores version visibility in Tag property so the search filter can respect it.
        /// </summary>
        static void HideVersionMismatchedButtons(StackPanel panel, int ver, bool isMultibyte)
        {
            foreach (var child in panel.Children)
            {
                if (child is not WrapPanel wp) continue;
                foreach (var item in wp.Children)
                {
                    if (item is not Button btn) continue;
                    string content = btn.Content?.ToString() ?? "";
                    bool? visible = GetVersionVisibility(content, ver, isMultibyte);
                    if (visible.HasValue)
                    {
                        btn.IsVisible = visible.Value;
                        if (!visible.Value)
                            btn.Tag = false; // mark as version-hidden for filter
                    }
                }
            }
        }

        /// <summary>
        /// Pure logic: returns true if the button should be shown, false if hidden,
        /// null if no version tag was found (always show).
        /// Order matters: check region-specific tags (FE7U, FE8U) before generic (FE7, FE8).
        /// </summary>
        internal static bool? GetVersionVisibility(string content, int ver, bool isMultibyte)
        {
            if (content.Contains("(FE7U)"))
                return ver == 7 && !isMultibyte;
            if (content.Contains("(FE8U)"))
                return ver == 8 && !isMultibyte;
            if (content.Contains("(FE6)"))
                return ver == 6;
            if (content.Contains("(FE7)"))
                return ver == 7;
            if (content.Contains("(FE8)"))
                return ver == 8;
            return null; // no tag — always show
        }

        /// <summary>Show or hide a section header + its WrapPanel together.</summary>
        static void SetSectionVisible(TextBlock header, WrapPanel panel, bool visible)
        {
            header.IsVisible = visible;
            panel.IsVisible = visible;
        }

        /// <summary>
        /// After filtering, if all buttons inside a WrapPanel are hidden,
        /// also hide the preceding TextBlock header.
        /// </summary>
        static void AutoHideEmptySections(StackPanel panel)
        {
            TextBlock? lastHeader = null;
            foreach (var child in panel.Children)
            {
                if (child is TextBlock tb)
                {
                    lastHeader = tb;
                    continue;
                }
                if (child is WrapPanel wp)
                {
                    bool anyVisible = false;
                    foreach (var btn in wp.Children)
                    {
                        if (btn.IsVisible) { anyVisible = true; break; }
                    }
                    if (!anyVisible)
                    {
                        wp.IsVisible = false;
                        if (lastHeader != null) lastHeader.IsVisible = false;
                    }
                    lastHeader = null;
                }
            }
        }
    }
}
