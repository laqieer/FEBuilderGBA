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
    public partial class FontEditorView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly FontEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Font Editor";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

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
            Opened += (_, _) => LoadList();
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
            LoadList();
            ClearPreview();
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

        // ---- Per-glyph PNG export/import ----

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError(R._("No glyph selected.")); return; }
                string suggested = (_vm.IsItemFont ? "Item_" : "Serif_") + U.ToHexString(_vm.CurrentMoji) + ".png";
                await GlyphImage.ExportPng(this, suggested);
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

                // Remap the PNG onto the 4-color font palette (colorCount=4 so the
                // quantized indices stay 0..3, NEVER the zeroed entries 4..15).
                byte[] fontPal = FontGlyphRenderCore.GetFontPaletteGBA(_vm.IsItemFont);
                var result = await ImageImportService.LoadAndRemapToExistingPalette(this,
                    FontGlyphRenderCore.GLYPH_W, FontGlyphRenderCore.GLYPH_H, fontPal, 4, strictSize: true);
                if (result == null) return;                                  // cancelled
                if (!result.Success) { CoreState.Services?.ShowError(result.Error); return; } // BEFORE the undo scope

                _undoService.Begin("Import Font Glyph");
                try
                {
                    string err = FontGlyphRenderCore.ImportGlyph(rom, _vm.IsItemFont, _vm.CurrentMoji,
                        result.IndexedPixels, result.Width, result.Height);
                    if (!string.IsNullOrEmpty(err)) { _undoService.Rollback(); CoreState.Services?.ShowError(err); return; }
                    _undoService.Commit();
                }
                catch { _undoService.Rollback(); throw; }

                // Reload so the row's address + preview refresh from the new glyph.
                LoadList();
                EntryList.SelectAddress(_vm.CurrentAddr);
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

                string? path = await FileDialogHelper.SaveFile(this,
                    R._("Export All Fonts"), "fontall.txt", "*.fontall.txt", "font.fontall.txt");
                if (string.IsNullOrEmpty(path)) return;

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

                string? path = await FileDialogHelper.OpenFile(this,
                    R._("Import All Fonts"), "*.fontall.txt");
                if (string.IsNullOrEmpty(path)) return;

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
