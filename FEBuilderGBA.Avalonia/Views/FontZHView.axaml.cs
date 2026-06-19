using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Chinese (ZH) game-font glyph editor (#1166). Structural twin of
    /// <see cref="FontEditorView"/> (the #1165 main-font editor): an address-list of
    /// glyph rows (each rendered as its 16x13 glyph icon) plus a per-glyph PNG
    /// export/import flow. Only available on a Chinese ROM (FontGlyphZHCore.IsZHRom).
    /// </summary>
    public partial class FontZHView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly FontZHViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Font Editor (Chinese)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public FontZHView()
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
                // glyph grid (mirrors the #1165 Font editor). The loader captures the
                // current font type.
                bool isItemFont = _vm.IsItemFont;
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.FontGlyphZHLoader(items, i, isItemFont));
            }
            catch (Exception ex)
            {
                Log.Error("FontZHView.LoadList failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void FontTypeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            // LoadList -> SetItemsWithIcons -> SelectFirst -> OnSelected already loads
            // the new font's first glyph + preview, so only clear when the new font is
            // empty (OnSelected isn't raised in that case).
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
                Log.Error("FontZHView.OnSelected failed: " + ex.ToString());
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

                // Capture the edited glyph's char code BEFORE reload — LoadList()
                // auto-selects the first row (overwriting _vm.CurrentMoji/Addr), so we
                // re-select by the original moji afterward.
                uint importedMoji = _vm.CurrentMoji;

                // Remap the PNG onto the 4-color ZH font palette (colorCount=4 so the
                // quantized indices stay 0..3). ZH glyphs are 16x13.
                byte[] fontPal = FontGlyphZHCore.GetFontPaletteGBA(_vm.IsItemFont);
                var result = await ImageImportService.LoadAndRemapToExistingPalette(this,
                    FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H, fontPal, 4, strictSize: true);
                if (result == null) return;                                  // cancelled
                if (!result.Success) { CoreState.Services?.ShowError(result.Error); return; } // BEFORE the undo scope

                _undoService.Begin("Import Chinese Font Glyph");
                try
                {
                    string err = FontGlyphZHCore.ImportGlyphZH(rom, _vm.IsItemFont, importedMoji,
                        result.IndexedPixels, result.Width, result.Height);
                    if (!string.IsNullOrEmpty(err)) { _undoService.Rollback(); CoreState.Services?.ShowError(err); return; }
                    _undoService.Commit();
                }
                catch { _undoService.Rollback(); throw; }

                // Reload, then re-select the JUST-IMPORTED glyph by its char code.
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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);

        /// <summary>
        /// Select the first glyph that actually has visible pixels (width &gt; 0) so
        /// the preview shows a real glyph rather than a blank slot. Falls back to the
        /// first row when none qualify.
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
                        if (!U.isSafetyOffset(a, rom) || (ulong)a + 2 > (ulong)rom.Data.Length) continue;
                        if (rom.u8(a + 1) > 0) { EntryList.SelectAddress(a); return; }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("FontZHView.SelectFirstItem failed: " + ex.ToString());
            }
            EntryList.SelectFirst();
        }
    }
}
