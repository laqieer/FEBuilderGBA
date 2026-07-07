using global::Avalonia;
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class FontEditorView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly FontEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        // Path of the desktop .ttf/.otf loaded for Auto-Generate (#1232). Empty
        // => fall back to the typed font family. Set by LoadFont_Click.
        string _fontFilePath = "";

        public string ViewTitle => "Font Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Font Editor", 640, 560);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public FontEditorView()
        {
            InitializeComponent();
            // Item / Serif selector — populated in code-behind so R._() applies
            // (ComboBoxItem content is not touched by ViewTranslationHelper).
            FontTypeCombo.Items.Add(R._("Item Font"));
            FontTypeCombo.Items.Add(R._("Serif Font"));
            FontTypeCombo.SelectedIndex = 0;
            FontTypeCombo.SelectionChanged += FontTypeCombo_SelectionChanged;

            EntryList.SelectedAddressChanged += OnSelected;

            // Default font family (a data value, not a UI label — set here so it
            // isn't a scanned AXAML literal the L10n gate would flag as
            // untranslated). AutoGen_Click also falls back to "Arial" when empty.
            FontFamilyInput.Text = "Arial";
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                _vm.FontTypeIndex = FontTypeCombo.SelectedIndex < 0 ? 0 : FontTypeCombo.SelectedIndex;
                var items = _vm.LoadList();
                // Render each glyph as the row icon so the list reads as a visual
                // glyph grid (#1165). The loader captures the current font type.
                bool isItemFont = _vm.IsItemFont;
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.FontGlyphLoader(items, i, isItemFont));
            }
            catch (Exception ex)
            {
                Log.Error("FontEditorView.LoadList failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void FontTypeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            // LoadList -> SetItemsWithIcons -> SelectFirst -> OnSelected already
            // loads the new font's first glyph + preview, so do NOT ClearPreview()
            // here (that would wipe the freshly-loaded preview). When the new font
            // is empty, OnSelected isn't raised, so clear only in that case.
            LoadList();
            if (EntryList.SelectedItem == null) ClearPreview();
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                var item = EntryList.SelectedItem;
                uint moji = item?.tag ?? 0;
                _vm.LoadEntry(addr, moji);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex)
            {
                Log.Error("FontEditorView.OnSelected failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            CharLabel.Text = EntryList.SelectedItem?.name ?? "";
            WidthLabel.Text = _vm.CurrentWidth.ToString();
        }

        void LoadImage()
        {
            try
            {
                using IImage? img = _vm.TryRenderGlyph();
                GlyphImage.SetImage(img);
            }
            catch { GlyphImage.SetImage(null); }
        }

        void ClearPreview()
        {
            AddrLabel.Text = "";
            CharLabel.Text = "";
            WidthLabel.Text = "";
            GlyphImage.SetImage(null);
        }

        // Re-select the list row whose tag (char code) == moji, after a reload.
        // Falls back to leaving the auto-selected first row if not found.
        void SelectByMoji(uint moji)
        {
            uint addr = _vm.FindAddrByMoji(moji);
            if (addr != 0) EntryList.SelectAddress(addr);
        }

        // ---- Per-glyph PNG export/import ----

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError(R._("No glyph selected.")); return; }
                string suggested = (_vm.IsItemFont ? "Item_" : "Serif_") + U.ToHexString(_vm.CurrentMoji) + ".png";
                await GlyphImage.ExportPng(TopLevel.GetTopLevel(this) as Window, suggested);
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Export failed: {0}", ex.Message));
            }
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError(R._("No glyph selected.")); return; }

                // Capture the edited glyph's char code BEFORE reload — LoadList()
                // auto-selects the first row (overwriting _vm.CurrentMoji/Addr), so
                // we re-select by the original moji afterward (the address may also
                // move when a brand-new glyph is appended).
                uint importedMoji = _vm.CurrentMoji;

                // Remap the PNG onto the 4-color font palette (colorCount=4 so the
                // quantized indices stay 0..3, NEVER the zeroed entries 4..15).
                byte[] fontPal = FontGlyphRenderCore.GetFontPaletteGBA(_vm.IsItemFont);
                var result = await ImageImportService.LoadAndRemapToExistingPalette(TopLevel.GetTopLevel(this) as Window,
                    FontGlyphRenderCore.GLYPH_W, FontGlyphRenderCore.GLYPH_H, fontPal, 4, strictSize: true);
                if (result == null) return;                                  // cancelled
                if (!result.Success) { CoreState.Services?.ShowError(result.Error); return; } // BEFORE the undo scope

                _undoService.Begin("Import Font Glyph");
                try
                {
                    string err = FontGlyphRenderCore.ImportGlyph(rom, _vm.IsItemFont, importedMoji,
                        result.IndexedPixels, result.Width, result.Height);
                    if (!string.IsNullOrEmpty(err)) { _undoService.Rollback(); CoreState.Services?.ShowError(err); return; }
                    _undoService.Commit();
                }
                catch { _undoService.Rollback(); throw; }

                // Reload, then re-select the JUST-IMPORTED glyph by its char code
                // (not _vm.CurrentAddr, which LoadList reset to the first row).
                LoadList();
                SelectByMoji(importedMoji);
                _vm.MarkClean();
                CoreState.Services?.ShowInfo(R._("Glyph imported successfully."));
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
            }
        }

        // ---- Bulk export / import ----

        async void ExportAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;

                // #1639: ExportAll writes sibling glyph PNGs next to the manifest,
                // so require a real local path; a SAF pick (no local path) cannot
                // place siblings → message on Android, never silent.
                string? path = await FileDialogHelper.SaveFile(TopLevel.GetTopLevel(this),
                    R._("Export All Fonts"), "fontall.txt", "*.fontall.txt", "font.fontall.txt");
                if (string.IsNullOrEmpty(path))
                {
                    if (OperatingSystem.IsAndroid())
                        CoreState.Services?.ShowError(R._("Exporting all fonts writes sibling glyph PNGs and requires desktop file-system access; it is not available on this device."));
                    return;
                }

                string? dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) { CoreState.Services?.ShowError(R._("Invalid output folder.")); return; }

                string manifest = FontBulkExportCore.ExportAll(rom, userFontOnly: false, (img, pngName) =>
                {
                    try { img.Save(Path.Combine(dir, pngName)); return true; }
                    catch (Exception ex) { Log.Error("FontEditorView.ExportAll writePng failed: " + ex.ToString()); return false; }
                });

                File.WriteAllText(path, manifest);
                CoreState.Services?.ShowInfo(R._("Fonts exported successfully."));
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Export failed: {0}", ex.Message));
            }
        }

        async void ImportAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;

                // #1639: ImportAll resolves sibling glyph PNGs from the manifest's
                // own directory, so require a real local path; a SAF pick (no local
                // path) cannot resolve siblings → message on Android, never silent.
                string? path = await FileDialogHelper.OpenFile(TopLevel.GetTopLevel(this),
                    R._("Import All Fonts"), "*.fontall.txt", requireLocalPath: true);
                if (string.IsNullOrEmpty(path))
                {
                    if (OperatingSystem.IsAndroid())
                        CoreState.Services?.ShowError(R._("Importing all fonts reads sibling glyph PNGs and requires desktop file-system access; it is not available on this device."));
                    return;
                }

                string? dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) { CoreState.Services?.ShowError(R._("Invalid input folder.")); return; }

                string manifest = File.ReadAllText(path);

                _undoService.Begin("Import All Fonts");
                try
                {
                    string err = FontBulkImportCore.ImportAll(rom, manifest, (pngName, type) =>
                    {
                        bool isItemFont = (type == "item");
                        byte[] fontPal = FontGlyphRenderCore.GetFontPaletteGBA(isItemFont);
                        var r = ImageImportService.LoadAndRemapFromFile(Path.Combine(dir, pngName),
                            FontGlyphRenderCore.GLYPH_W, FontGlyphRenderCore.GLYPH_H, fontPal, 4, strictSize: true);
                        if (r == null || !r.Success) return null;
                        return new FontGlyphPixels { Indexed = r.IndexedPixels, Width = r.Width, Height = r.Height };
                    });
                    if (!string.IsNullOrEmpty(err)) { _undoService.Rollback(); CoreState.Services?.ShowError(err); return; }
                    _undoService.Commit();
                }
                catch { _undoService.Rollback(); throw; }

                LoadList();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo(R._("Fonts imported successfully."));
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
            }
        }

        // ---- Auto-generate from a desktop .ttf/.otf font (#1232) ----

        async void LoadFont_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenFile(TopLevel.GetTopLevel(this),
                    R._("Load Font File"), new[] { "*.ttf", "*.otf" });
                if (string.IsNullOrEmpty(path)) return;
                _fontFilePath = path;
                FontFileLabel.Text = Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Load failed: {0}", ex.Message));
            }
        }

        void ClearFont_Click(object? sender, RoutedEventArgs e)
        {
            _fontFilePath = "";
            FontFileLabel.Text = "";
        }

        void AutoGen_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError(R._("No glyph selected.")); return; }

                // The character to rasterize is the selected row's decoded glyph
                // (the label is "0xXX <char>"); the moji is the target slot.
                string character = GlyphCharacterOfSelected();
                if (string.IsNullOrEmpty(character)) { CoreState.Services?.ShowError(R._("This glyph has no renderable character.")); return; }

                // Build the cross-platform font selector: a loaded .ttf/.otf file
                // wins; otherwise the typed family (resolved by SKTypeface).
                var font = new FontSpec
                {
                    FamilyName = string.IsNullOrWhiteSpace(FontFamilyInput.Text) ? "Arial" : FontFamilyInput.Text,
                    Size = (float)(FontSizeInput.Value is { } sz && sz > 0 ? sz : 12m),
                    FontFilePath = string.IsNullOrEmpty(_fontFilePath) ? null : _fontFilePath,
                };
                int vOffset = (int)(VOffsetInput.Value ?? 0m);

                // Capture the target char code BEFORE reload (LoadList re-selects
                // the first row), so we can re-select the just-edited glyph.
                uint targetMoji = _vm.CurrentMoji;

                var rasterizer = new FEBuilderGBA.SkiaSharp.SkiaFontRasterizer();

                _undoService.Begin("Auto-Generate Font Glyph");
                try
                {
                    string err = FontAutoGenCore.AutoGenerateGlyph(rom, rasterizer, font,
                        character, targetMoji, _vm.IsItemFont, vOffset);
                    if (!string.IsNullOrEmpty(err)) { _undoService.Rollback(); CoreState.Services?.ShowError(err); return; }
                    _undoService.Commit();
                }
                catch { _undoService.Rollback(); throw; }

                LoadList();
                SelectByMoji(targetMoji);
                _vm.MarkClean();
                CoreState.Services?.ShowInfo(R._("Glyph generated successfully."));
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Auto-generate failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// The renderable character of the selected glyph row. The list label is
        /// "0xXX &lt;char&gt;" (hex code + decoded char); take the part after the
        /// first space. An "@hex" fallback name (an undecodable code) yields "".
        /// </summary>
        string GlyphCharacterOfSelected()
        {
            string label = EntryList.SelectedItem?.name ?? "";
            int sp = label.IndexOf(' ');
            string ch = sp >= 0 ? label.Substring(sp + 1) : "";
            if (string.IsNullOrEmpty(ch)) return "";
            // Reject ONLY the FontGlyphRenderCore "@hex" fallback for an
            // undecodable code: '@' followed by one or more hex digits (e.g.
            // "@4140", "@A0"). A real '@' character (moji 0x40) decodes to the
            // lone "@" and MUST rasterize normally — so don't blanket-reject
            // everything starting with '@'.
            if (IsAtHexFallback(ch)) return "";
            return ch;
        }

        /// <summary>
        /// True when <paramref name="s"/> is the "@hex" fallback marker emitted by
        /// FontGlyphRenderCore.FontChar / FontCharUTF8 for an undecodable code:
        /// a literal '@' followed by one or more hex digits and nothing else. A
        /// bare "@" (the real at-sign glyph, moji 0x40) is NOT a fallback.
        /// </summary>
        internal static bool IsAtHexFallback(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 2 || s[0] != '@') return false;
            for (int i = 1; i < s.Length; i++)
            {
                if (!Uri.IsHexDigit(s[i])) return false;
            }
            return true;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);

        /// <summary>
        /// Select the first glyph that actually has visible pixels (width &gt; 0)
        /// so the preview shows a real glyph rather than a control-code blank
        /// (e.g. 0x1F). Falls back to the first row when none qualify.
        /// </summary>
        public void SelectFirstItem()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom != null)
                {
                    foreach (var item in EntryList.GetItems())
                    {
                        if (item == null) continue;
                        uint a = item.addr;
                        if (!U.isSafetyOffset(a, rom) || (ulong)a + 6 > (ulong)rom.Data.Length) continue;
                        if (rom.u8(a + 5) > 0) { EntryList.SelectAddress(a); return; }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("FontEditorView.SelectFirstItem failed: " + ex.ToString());
            }
            EntryList.SelectFirst();
        }
    }
}
