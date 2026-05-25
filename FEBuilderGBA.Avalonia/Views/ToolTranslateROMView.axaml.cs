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
    /// The action buttons (`Start Translation`, `Export All Texts`,
    /// `Import All Texts`, `Import Font`, `Change` font name) render with
    /// `IsEnabled="False"` plus a ToolTip referencing #536. They become
    /// real handlers once the Core extraction of `ToolTranslateROM` +
    /// `FETextDecode` + `RecycleAddress` + `FindOrignalROMByLang` lands.
    /// </summary>
    public partial class ToolTranslateROMView : TranslatedWindow, IEditorView
    {
        readonly ToolTranslateROMViewModel _vm = new();

        public string ViewTitle => "ROM Translation Tool";
        public bool IsLoaded => _vm.IsLoaded;

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
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.FromRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.SimpleFromRomBrowse: {0}", ex.Message); }
        }

        async void SimpleToRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.ToRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.SimpleToRomBrowse: {0}", ex.Message); }
        }

        async void ExtraFontRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.ExtraFontRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.ExtraFontRomBrowse: {0}", ex.Message); }
        }

        async void TranslateDataBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenFile(this,
                    R._("Translation Data"), "*.txt");
                if (!string.IsNullOrEmpty(path)) _vm.TranslateDataPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.TranslateDataBrowse: {0}", ex.Message); }
        }

        // ---------- Detail tab browse handlers ----------

        async void DetailFromRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.FromRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.DetailFromRomBrowse: {0}", ex.Message); }
        }

        async void DetailToRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.ToRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.DetailToRomBrowse: {0}", ex.Message); }
        }

        async void FontRomBrowse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path)) _vm.FontRomPath = path;
            }
            catch (Exception ex) { Log.Error("ToolTranslateROMView.FontRomBrowse: {0}", ex.Message); }
        }

        // ---------- Real action handlers (#536 wires Core helpers) ----------
        //
        // Every mutating handler wraps its ROM writes in
        // `_vm.UndoService.Begin/Commit/Rollback`. The 8 spy tests in
        // ToolTranslateROMParityTests.UndoService_* assert the call sequence
        // on success and on simulated exception.
        //
        // Known limitations (KnownGap per #536 scope discipline):
        //  - Bitmap font auto-generation requires System.Drawing.Bitmap and
        //    therefore stays WinForms-only. The Avalonia ImportFont handler
        //    surfaces a status dialog instead of silently no-oping.
        //  - The full WipeJP* flow needs WF HowDoYouLikePatchForm popup
        //    orchestration; Avalonia SimpleFire skips the OverrideJpFont
        //    branch and notes this in the user-facing message.

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
                        OverrideJpFont = _vm.SimpleOverrideJpFont,
                    };
                    var recycle = new RecycleAddress();
                    total = ToolTranslateROMCore.SimpleFireTranslate(rom, opts, recycle,
                        _vm.UndoService.GetActiveUndoData(), null);
                });

                _vm.UndoService.Commit();
                string msg = $"Translation complete. {total} text entries written.";
                if (_vm.SimpleOverrideJpFont)
                {
                    msg += "\n\nNote: Override JP Font is checked, but the WipeJP* flow needs " +
                        "WinForms HowDoYouLikePatchForm popups and stays WinForms-only (#536 " +
                        "KnownGap). Use the WinForms tool for full JP-font wiping.";
                }
                await ShowInfo(msg);
            }
            catch (Exception ex)
            {
                _vm.UndoService.Rollback();
                await ShowError("Translation failed: " + ex.Message);
                Log.Error("ToolTranslateROMView.SimpleFire: {0}", ex.Message);
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

            string? outPath = await FileDialogHelper.SaveFile(this,
                R._("Save translation file"), "Text", "*.txt",
                suggestedName: "translation.txt");
            if (string.IsNullOrEmpty(outPath)) return;

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
                int n = await Task.Run(() =>
                    ToolTranslateROMCore.ExportTextsToFile(rom, outPath,
                        _vm.OneLinerCheck, _vm.ModifiedTextOnly,
                        fromLang, toLang, fromPath, toPath,
                        progressCallback: null));
                await ShowInfo($"Exported {n} text entries to:\n{outPath}");
            }
            catch (Exception ex)
            {
                await ShowError("Export failed: " + ex.Message);
                Log.Error("ToolTranslateROMView.ExportAllText: {0}", ex.Message);
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

            string? inPath = await FileDialogHelper.OpenFile(this,
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
                Log.Error("ToolTranslateROMView.ImportAllText: {0}", ex.Message);
            }
        }

        async void ImportFont_Click(object? sender, RoutedEventArgs e)
        {
            // Avalonia ImportFont calls the Core ImportFontFromROMs orchestration
            // which uses FontCore (in Core) + TextSourceListCore (in Core) to
            // port missing glyphs from a source Font ROM (+ optional Extra Font
            // ROM) into the current ROM. The bitmap auto-generation branch
            // (System.Drawing.Bitmap) is NOT exercised - that stays WinForms-only
            // (see #536 Known Limitations / FontAutoGenerate-checked banner below).

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

            if (!fontRomExists && !extraFontExists)
            {
                await ShowInfo("No font source ROM specified. Set Font ROM (or Extra Font ROM) " +
                    "and try again.");
                return;
            }

            _vm.UndoService.Begin("Import Font");
            try
            {
                int ported = 0;
                await Task.Run(() =>
                {
                    var recycle = new RecycleAddress();
                    ported = ToolTranslateROMCore.ImportFontFromROMs(rom,
                        _vm.FontRomPath, _vm.ExtraFontRomPath,
                        recycle, _vm.UndoService.GetActiveUndoData(), null);
                });
                _vm.UndoService.Commit();

                string msg = $"Imported {ported} font glyph(s) from source ROM(s).";
                if (_vm.FontAutoGenerate)
                {
                    msg += "\n\nNote: Font auto-generation is enabled but requires " +
                        "System.Drawing.Bitmap (WinForms-only, #536 KnownGap). " +
                        "Missing fonts that aren't in any source ROM remain unimported. " +
                        "Use the WinForms tool to auto-generate them from a TrueType font.";
                }
                await ShowInfo(msg);
            }
            catch (Exception ex)
            {
                _vm.UndoService.Rollback();
                await ShowError("Import Font failed: " + ex.Message);
                Log.Error("ToolTranslateROMView.ImportFont: {0}", ex.Message);
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

                await dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log.Error("ToolTranslateROMView.UseFontName: {0}", ex.Message);
            }
        }

        async Task ShowInfo(string message)
        {
            try
            {
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
                await dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log.Error("ToolTranslateROMView.ShowInfo: {0}", ex.Message);
            }
        }

        Task ShowError(string message) => ShowInfo(message);

        public void NavigateTo(uint address) { /* tool dialog - nothing to navigate to */ }
        public void SelectFirstItem() { /* tool dialog - no list to seed */ }
    }
}
