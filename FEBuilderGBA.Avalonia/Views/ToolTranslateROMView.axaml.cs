using global::Avalonia;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>ToolTranslateROMForm</c>.
    /// Gap-sweep parity raise (#422) lifts the AXAML control surface from
    /// 3 controls to MEDIUM-verdict density (&gt;=27 of WF's 35 controls) by
    /// reproducing the WF Simple / Detail tab layout, the WF default-checked
    /// state on `useAutoTranslateCheckBox` / `X_MODIFIED_TEXT_ONLY` /
    /// `FontAutoGenelateCheckBox`, and the multibyte-only visibility rule on
    /// the JP-font override checkboxes.
    ///
    /// All action buttons (`Start Translation`, `Export All Texts`,
    /// `Import All Texts`, `Import Font`, `Change` font name) are fully wired to
    /// the Core orchestration (#536). As of #796 the `Import Font` handler also
    /// auto-generates missing glyphs cross-platform via
    /// <see cref="FEBuilderGBA.SkiaSharp.SkiaFontRasterizer"/>, so the former
    /// WinForms-only System.Drawing.Bitmap dependency is gone.
    /// </summary>
    public partial class ToolTranslateROMView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolTranslateROMViewModel _vm = new();

        public string ViewTitle => "ROM Translation Tool";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("ROM Translation Tool", 940, 780);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolTranslateROMView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        // ---------- Simple tab browse handlers ----------

        async void SimpleFromRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path)) _vm.FromRomPath = path;
            }
            catch (Exception ex) { Log.ErrorF("ToolTranslateROMView.SimpleFromRomBrowse: {0}", ex.Message); }
        }

        async void SimpleToRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path)) _vm.ToRomPath = path;
            }
            catch (Exception ex) { Log.ErrorF("ToolTranslateROMView.SimpleToRomBrowse: {0}", ex.Message); }
        }

        async void ExtraFontRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path)) _vm.ExtraFontRomPath = path;
            }
            catch (Exception ex) { Log.ErrorF("ToolTranslateROMView.ExtraFontRomBrowse: {0}", ex.Message); }
        }

        async void TranslateDataBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenFile(TopLevel.GetTopLevel(this),
                    R._("Translation Data"), "*.txt");
                if (!string.IsNullOrEmpty(path)) _vm.TranslateDataPath = path;
            }
            catch (Exception ex) { Log.ErrorF("ToolTranslateROMView.TranslateDataBrowse: {0}", ex.Message); }
        }

        // ---------- Detail tab browse handlers ----------

        async void DetailFromRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path)) _vm.FromRomPath = path;
            }
            catch (Exception ex) { Log.ErrorF("ToolTranslateROMView.DetailFromRomBrowse: {0}", ex.Message); }
        }

        async void DetailToRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path)) _vm.ToRomPath = path;
            }
            catch (Exception ex) { Log.ErrorF("ToolTranslateROMView.DetailToRomBrowse: {0}", ex.Message); }
        }

        async void FontRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path)) _vm.FontRomPath = path;
            }
            catch (Exception ex) { Log.ErrorF("ToolTranslateROMView.FontRomBrowse: {0}", ex.Message); }
        }

        // ---------- Real action handlers (#536 wires Core helpers) ----------
        //
        // Every mutating handler wraps its ROM writes in
        // `_vm.UndoService.Begin/Commit/Rollback`. The 8 spy tests in
        // ToolTranslateROMParityTests.UndoService_* assert the call sequence
        // on success and on simulated exception.
        //
        // Cross-platform note:
        //  - Bitmap font auto-generation is now cross-platform via
        //    SkiaFontRasterizer (#796) — the ImportFont handler rasterizes
        //    missing glyphs through the IFontRasterizer seam, no longer
        //    WinForms-only.
        //  - The full WipeJP* flow (#1029) is now cross-platform via
        //    ToolTranslateROMCore.SimpleFireTranslate (OverrideJpFont); the WF
        //    HowDoYouLikePatchForm ChapterNameText popup is replaced by an
        //    injected precondition delegate evaluated on the UI thread below.

        async void SimpleFire_Click(object? sender, RoutedEventArgs e)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                await ShowInfo("No ROM loaded.");
                return;
            }

            // Parse target language from the TO combo.
            string toLang = ToolTranslateROMCore.ParseLanguageKey(
                ToolTranslateROMViewModel.ToLanguageItemsRaw[
                    Math.Clamp(_vm.ToLanguageIndex, 0, ToolTranslateROMViewModel.ToLanguageItemsRaw.Length - 1)]);

            // Parse FROM / TO languages (TO already parsed above).
            string fromLang = ToolTranslateROMCore.ParseLanguageKey(
                ToolTranslateROMViewModel.FromLanguageItemsRaw[
                    Math.Clamp(_vm.FromLanguageIndex, 0, ToolTranslateROMViewModel.FromLanguageItemsRaw.Length - 1)]);

            // Capture the Override-JP-Font flag ONCE so the UI-thread precondition
            // check, the worker-thread options, and the completion message all read
            // the SAME value even if the checkbox changes mid-run. (A re-read race
            // could otherwise let the worker wipe with OverrideJpFont=true while the
            // precondition stayed at its not-checked default true — Copilot review #1101.)
            bool overrideJpFont = _vm.SimpleOverrideJpFont;

            // Resolve the ChapterNameText precondition on the UI thread (BEFORE
            // the background Task.Run) — Avalonia modal dialogs must not be shown
            // from a worker thread. Mirrors WF
            // HowDoYouLikePatchForm.CheckAndShowPopupDialog(ChapterNameText): the
            // JP chapter-name wipe only proceeds when the ChapterNameToText patch
            // is installed; if it isn't, surface the recommendation — and, when the
            // user clicks Apply, install the patch — then re-check.
            bool chapterNameTextOk = true;
            if (overrideJpFont)
            {
                chapterNameTextOk = PatchDetection.SearchChapterNameToTextPatch(rom);
                if (!chapterNameTextOk && rom.RomInfo.version == 8)
                {
                    await ShowChapterNameTextRecommendation(rom);
                    // Re-check: ShowChapterNameTextRecommendation installs the patch
                    // when the user clicks Apply. The wipe proceeds only if it's now
                    // present.
                    chapterNameTextOk = PatchDetection.SearchChapterNameToTextPatch(rom);
                }
            }

            _vm.UndoService.Begin("Translate ROM");
            try
            {
                int total = 0;
                // Heavy I/O + decode work runs on a background thread so the
                // UI thread stays responsive (mirrors WF AutoPleaseWait /
                // DoEvents which yields control during the loop).
                await Task.Run(() =>
                {
                    var opts = new ToolTranslateROMCore.SimpleFireOptions
                    {
                        FromRomPath = _vm.FromRomPath,
                        ToRomPath = _vm.ToRomPath,
                        ExtraFontRomPath = _vm.ExtraFontRomPath,
                        TranslateDataFilename = _vm.TranslateDataPath,
                        FromLanguage = fromLang,
                        ToLanguage = toLang,
                        OverrideJpFont = overrideJpFont,
                        // Precondition was resolved on the UI thread; pass the
                        // captured value (no dialog on the worker thread).
                        ChapterNameTextPrecondition = () => chapterNameTextOk,
                    };
                    var recycle = new RecycleAddress();
                    total = ToolTranslateROMCore.SimpleFireTranslate(rom, opts, recycle,
                        _vm.UndoService.GetActiveUndoData(), null);
                });

                _vm.UndoService.Commit();
                string msg = $"Translation complete. {total} text entries written.";
                if (overrideJpFont)
                {
                    msg += "\n\n" + R._("Override JP Font: the Japanese font tables were wiped " +
                        "(class-reel font, chapter titles, and item/text fonts) to free space.");
                }
                await ShowInfo(msg);
            }
            catch (Exception ex)
            {
                _vm.UndoService.Rollback();
                await ShowError("Translation failed: " + ex.Message);
                Log.ErrorF("ToolTranslateROMView.SimpleFire: {0}", ex.Message);
            }
        }

        async void ExportAllText_Click(object? sender, RoutedEventArgs e)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                await ShowInfo("No ROM loaded.");
                return;
            }

            // #1639: pick the handle now; the actual single-file write is routed
            // through the SAF bridge below so Android content:// targets work.
            var outFile = await FileDialogHelper.SaveFilePick(TopLevel.GetTopLevel(this),
                R._("Save translation file"), "Text", "*.txt", "translation.txt");
            if (outFile == null) return;

            // Parse Detail-tab translate-from / translate-to (when
            // UseAutoTranslate is checked). Empty otherwise -> no dictionary
            // applied, just plain text export.
            string fromLang = _vm.UseAutoTranslate
                ? ToolTranslateROMCore.ParseLanguageKey(
                    ToolTranslateROMViewModel.FromLanguageItemsRaw[
                        Math.Clamp(_vm.FromLanguageIndex, 0, ToolTranslateROMViewModel.FromLanguageItemsRaw.Length - 1)])
                : string.Empty;
            string toLang = _vm.UseAutoTranslate
                ? ToolTranslateROMCore.ParseLanguageKey(
                    ToolTranslateROMViewModel.ToLanguageItemsRaw[
                        Math.Clamp(_vm.ToLanguageIndex, 0, ToolTranslateROMViewModel.ToLanguageItemsRaw.Length - 1)])
                : string.Empty;
            string fromPath = _vm.UseAutoTranslate ? _vm.FromRomPath : string.Empty;
            string toPath = _vm.UseAutoTranslate ? _vm.ToRomPath : string.Empty;

            // Export is read-only - no UndoService.Begin/Commit. Run on a
            // background thread so the UI stays responsive while we iterate
            // tens of thousands of text entries.
            try
            {
                int n = 0;
                string? written = await FileDialogHelper.WriteViaAsync(outFile, async outPath =>
                {
                    n = await Task.Run(() =>
                        ToolTranslateROMCore.ExportTextsToFile(rom, outPath,
                            _vm.OneLinerCheck, _vm.ModifiedTextOnly,
                            fromLang, toLang, fromPath, toPath,
                            progressCallback: null));
                });
                if (written == null) return;
                await ShowInfo($"Exported {n} text entries to:\n{written}");
            }
            catch (Exception ex)
            {
                await ShowError("Export failed: " + ex.Message);
                Log.ErrorF("ToolTranslateROMView.ExportAllText: {0}", ex.Message);
            }
        }

        async void ImportAllText_Click(object? sender, RoutedEventArgs e)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                await ShowInfo("No ROM loaded.");
                return;
            }

            string? inPath = await FileDialogHelper.OpenFile(TopLevel.GetTopLevel(this),
                R._("Open translation file"), "*.txt");
            if (string.IsNullOrEmpty(inPath)) return;

            string toLang = ToolTranslateROMCore.ParseLanguageKey(
                ToolTranslateROMViewModel.ToLanguageItemsRaw[
                    Math.Clamp(_vm.ToLanguageIndex, 0, ToolTranslateROMViewModel.ToLanguageItemsRaw.Length - 1)]);

            _vm.UndoService.Begin("Import Translation");
            try
            {
                // Heavy decode/write loop runs on a background thread so the
                // UI stays responsive (mirrors WF AutoPleaseWait/DoEvents).
                int n = await Task.Run(() =>
                {
                    // Apply the translate-patch first so the menu width /
                    // status-screen cell match the destination language
                    // (matches WF flow).
                    ToolTranslateROMCore.ApplyTranslatePatch(rom, toLang,
                        _vm.UndoService.GetActiveUndoData());

                    var recycle = new RecycleAddress();
                    return ToolTranslateROMCore.ImportTextsFromFile(rom, inPath,
                        recycle, _vm.UndoService.GetActiveUndoData(), null);
                });

                _vm.UndoService.Commit();
                await ShowInfo($"Imported {n} text entries.");
            }
            catch (Exception ex)
            {
                _vm.UndoService.Rollback();
                await ShowError("Import failed: " + ex.Message);
                Log.ErrorF("ToolTranslateROMView.ImportAllText: {0}", ex.Message);
            }
        }

        async void ImportFont_Click(object? sender, RoutedEventArgs e)
        {
            // Avalonia ImportFont calls the Core ImportFonts orchestration which
            // uses FontCore + TextSourceListCore (both in Core) to port missing
            // glyphs from a source Font ROM (+ optional Extra Font ROM), and —
            // when "Auto-Generate Missing Fonts" is checked — rasterizes any
            // still-missing glyph cross-platform via SkiaFontRasterizer (#796),
            // replacing the old WinForms-only System.Drawing.Bitmap path.

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                await ShowInfo("No ROM loaded.");
                return;
            }

            bool fontRomExists = !string.IsNullOrEmpty(_vm.FontRomPath) &&
                File.Exists(_vm.FontRomPath);
            bool extraFontExists = !string.IsNullOrEmpty(_vm.ExtraFontRomPath) &&
                File.Exists(_vm.ExtraFontRomPath);

            // With auto-generation on we no longer need a source ROM — the
            // rasterizer can synthesize every missing glyph. Only reject when
            // there is neither a source ROM nor auto-generation.
            if (!fontRomExists && !extraFontExists && !_vm.FontAutoGenerate)
            {
                await ShowInfo("No font source ROM specified and auto-generation is off. " +
                    "Set Font ROM (or Extra Font ROM), or enable " +
                    "\"Auto-Generate Missing Fonts\", and try again.");
                return;
            }

            var fontSpec = _vm.BuildAutoGenFontSpec();
            _vm.UndoService.Begin("Import Font");
            try
            {
                ToolTranslateROMCore.ImportFontResult result = default;
                await Task.Run(() =>
                {
                    var recycle = new RecycleAddress();
                    var rasterizer = new FEBuilderGBA.SkiaSharp.SkiaFontRasterizer();
                    result = ToolTranslateROMCore.ImportFonts(rom,
                        _vm.FontRomPath, _vm.ExtraFontRomPath,
                        rasterizer, fontSpec, _vm.FontAutoGenerate,
                        recycle, _vm.UndoService.GetActiveUndoData(), null);
                });
                _vm.UndoService.Commit();

                string msg = $"Font import complete: {result.Generated} generated, " +
                    $"{result.Ported} ported from source ROM(s).";
                if (_vm.FontAutoGenerate)
                {
                    msg += $"\n\nAuto-generated glyphs were rasterized cross-platform " +
                        $"(SkiaSharp) from the \"{fontSpec.FamilyName}\" family at " +
                        $"{fontSpec.Size:0.#}pt. Any character the font cannot render " +
                        $"(e.g. an unsupported codepoint) is skipped.";
                }
                await ShowInfo(msg);
            }
            catch (Exception ex)
            {
                _vm.UndoService.Rollback();
                await ShowError("Import Font failed: " + ex.Message);
                Log.ErrorF("ToolTranslateROMView.ImportFont: {0}", ex.Message);
            }
        }

        async void UseFontName_Click(object? sender, RoutedEventArgs e)
        {
            // Open a simple font-family picker dialog using Avalonia's
            // FontManager. The picked name goes into the read-only
            // UseFontName text box; the WinForms-only auto-gen consumer
            // reads it from there. UI-only - no UndoService.
            try
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                if (owner == null)
                {
                    return;
                }
                var families = global::Avalonia.Media.FontManager.Current
                    .SystemFonts
                    .Select(f => f.Name)
                    .OrderBy(name => name)
                    .ToArray();

                var dlg = new Window
                {
                    Title = R._("Select Font Family"),
                    Width = 360, Height = 480,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                var list = new ListBox { ItemsSource = families, Margin = new global::Avalonia.Thickness(8) };
                list.SelectedItem = _vm.UseFontName;
                var ok = new Button
                {
                    Content = R._("OK"),
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                    Margin = new global::Avalonia.Thickness(8),
                };
                ok.Click += (_, __) =>
                {
                    if (list.SelectedItem is string fam) _vm.UseFontName = fam;
                    dlg.Close();
                };
                var panel = new DockPanel();
                DockPanel.SetDock(ok, Dock.Bottom);
                panel.Children.Add(ok);
                panel.Children.Add(list);
                dlg.Content = panel;

                await dlg.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ToolTranslateROMView.UseFontName: {0}", ex.Message);
            }
        }

        async Task ShowInfo(string message)
        {
            try
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                if (owner == null)
                {
                    return;
                }
                var dlg = new Window
                {
                    Title = ViewTitle,
                    Width = 480, Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                var text = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new global::Avalonia.Thickness(12),
                };
                var ok = new Button
                {
                    Content = R._("OK"),
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                    Margin = new global::Avalonia.Thickness(8),
                };
                ok.Click += (_, __) => dlg.Close();
                var panel = new DockPanel();
                DockPanel.SetDock(ok, Dock.Bottom);
                panel.Children.Add(ok);
                panel.Children.Add(text);
                dlg.Content = panel;
                await dlg.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ToolTranslateROMView.ShowInfo: {0}", ex.Message);
            }
        }

        Task ShowError(string message) => ShowInfo(message);

        /// <summary>
        /// Surface the ChapterNameToText patch recommendation (the Avalonia
        /// counterpart of WF <c>HowDoYouLikePatchForm.CheckAndShowPopupDialog(ChapterNameText)</c>).
        /// When the user clicks Apply, install the ChapterNameToText patch (the WF
        /// dialog's Enable button calls <c>PatchForm.ApplyPatch</c>), so the
        /// caller's re-check can then let the chapter-title wipe proceed. Clicking
        /// Skip leaves the patch absent, and the wipe skips the chapter-title part.
        /// </summary>
        async Task ShowChapterNameTextRecommendation(ROM rom)
        {
            try
            {
                var view = await WindowManager.Instance.OpenModal<HowDoYouLikePatchView>(
                    TopLevel.GetTopLevel(this) as Window,
                    v => v.SetPatchInfo(R._(
                        "To display chapter titles as text (required before wiping the Japanese " +
                        "chapter-title images), the ChapterNameToText patch must be installed. " +
                        "Apply it now?")));
                if (!view.UserApplied) return; // Skip — leave the patch absent.

                // Apply the ChapterNameToText patch (mirrors WF Enable button ->
                // PatchForm.ApplyPatch). On success the caller's re-check of
                // SearchChapterNameToTextPatch passes and the wipe proceeds.
                string result = InstallChapterNameToTextPatch(rom);
                if (!PatchDetection.SearchChapterNameToTextPatch(rom))
                {
                    await ShowInfo(R._("Could not install the ChapterNameToText patch.") +
                        "\n" + result);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ToolTranslateROMView.ShowChapterNameTextRecommendation: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Find and apply the "Convert Chapter Titles to Text" patch for the
        /// current ROM version (the WF ChapterNameToText patch). Returns a status
        /// message. Mirrors <see cref="PatchManagerViewModel.InstallPatch"/>'s
        /// enumerate -&gt; ApplyPatch path. The mutation is recorded in the active
        /// translate undo scope.
        /// </summary>
        string InstallChapterNameToTextPatch(ROM rom)
        {
            try
            {
                string version = rom.RomInfo.VersionToFilename;
                string patchDir = PatchManagerViewModel.ResolvePatchDirectory(version);
                string lang = PatchMetadataCore.GetLanguageSuffix();
                var infos = PatchMetadataCore.EnumeratePatches(patchDir, rom, lang);

                // Match the WF ChapterNameToText installer patch by name.
                var target = infos.FirstOrDefault(p =>
                    p.Name.IndexOf("Convert Chapter Titles to Text",
                        StringComparison.OrdinalIgnoreCase) >= 0);
                if (target == null || string.IsNullOrEmpty(target.PatchFilePath))
                    return "ChapterNameToText patch not found in " + patchDir;

                // The patch install is a distinct user action (the Apply button),
                // so it gets its OWN undo scope — separate from the translate undo
                // begun later in SimpleFire_Click.
                _vm.UndoService.Begin("Install ChapterNameToText");
                try
                {
                    var res = PatchMetadataCore.ApplyPatch(rom, target.PatchFilePath,
                        _vm.UndoService.GetActiveUndoData());
                    _vm.UndoService.Commit();
                    return res.Message ?? string.Empty;
                }
                catch
                {
                    _vm.UndoService.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ToolTranslateROMView.InstallChapterNameToTextPatch: {0}", ex.Message);
                return ex.Message;
            }
        }

        public void NavigateTo(uint address) { /* tool dialog - nothing to navigate to */ }
        public void SelectFirstItem() { /* tool dialog - no list to seed */ }
    }
}
