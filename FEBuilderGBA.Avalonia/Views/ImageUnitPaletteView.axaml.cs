using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageUnitPaletteView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageUnitPaletteViewModel _vm = new();
        readonly UndoService _undoService = new();

        // Per-swatch control references for fast UI updates.
        NumericUpDown[] _rBoxes = Array.Empty<NumericUpDown>();
        NumericUpDown[] _gBoxes = Array.Empty<NumericUpDown>();
        NumericUpDown[] _bBoxes = Array.Empty<NumericUpDown>();
        Border[] _swatches = Array.Empty<Border>();

        public string ViewTitle => "Unit Palette Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageUnitPaletteView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;

            // Populate the preview sub-palette combo via R._() so it picks up
            // ja/zh translations (ComboBoxItem.Content is not touched by
            // ViewTranslationHelper — the #822/#571 pattern). Replace the XAML
            // placeholder items with translated ones.
            PreviewPaletteTypeCombo.Items.Clear();
            PreviewPaletteTypeCombo.Items.Add(R._("Player"));
            PreviewPaletteTypeCombo.Items.Add(R._("Enemy"));
            PreviewPaletteTypeCombo.Items.Add(R._("Other"));
            PreviewPaletteTypeCombo.Items.Add(R._("4th Army"));
            PreviewPaletteTypeCombo.SelectedIndex = 0;

            // #840: the class selector + sub-palette combo both re-render the
            // sample preview (the cross-platform equivalent of WF
            // X_DISPLAY_CLASS_ValueChanged / PaletteIndexComboBox change ->
            // DrawSample). Null-safe via RefreshSamplePreview.
            ClassBox.ValueChanged += OnClassChanged;
            PreviewPaletteTypeCombo.SelectionChanged += OnPreviewPaletteTypeChanged;

            Opened += (_, _) =>
            {
                CacheSwatchControls();
                LoadList();
            };
        }

        void OnClassChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            try
            {
                _vm.ClassID = (uint)(ClassBox.Value ?? 0);
                _vm.ClassName = NameResolver.GetClassName(_vm.ClassID);
                ClassNameLabel.Text = _vm.ClassName;
                RefreshSamplePreview();
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.OnClassChanged failed: {0}", ex.Message);
            }
        }

        void OnPreviewPaletteTypeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            try
            {
                int idx = PreviewPaletteTypeCombo.SelectedIndex;
                if (idx < 0) idx = 0;
                _vm.PaletteTypeIndex = idx;
                RefreshSamplePreview();
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.OnPreviewPaletteTypeChanged failed: {0}", ex.Message);
            }
        }

        void CacheSwatchControls()
        {
            _rBoxes = new NumericUpDown[16];
            _gBoxes = new NumericUpDown[16];
            _bBoxes = new NumericUpDown[16];
            _swatches = new Border[16];
            for (int i = 0; i < 16; i++)
            {
                int idx = i + 1;
                _rBoxes[i] = this.FindControl<NumericUpDown>($"R{idx}Box")!;
                _gBoxes[i] = this.FindControl<NumericUpDown>($"G{idx}Box")!;
                _bBoxes[i] = this.FindControl<NumericUpDown>($"B{idx}Box")!;
                _swatches[i] = this.FindControl<Border>($"Swatch{idx}")!;
                int captureIdx = i;
                if (_rBoxes[i] != null) _rBoxes[i].ValueChanged += (_, _) => RefreshSwatch(captureIdx);
                if (_gBoxes[i] != null) _gBoxes[i].ValueChanged += (_, _) => RefreshSwatch(captureIdx);
                if (_bBoxes[i] != null) _bBoxes[i].ValueChanged += (_, _) => RefreshSwatch(captureIdx);
            }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                ReadStartAddressBox.Text = _vm.LoadListBaseAddress();
                // The VM appends a trailing "Unit Palette Editor" sentinel row
                // (addr=0) at the end; exclude it from the displayed count so
                // the count matches the actual table-row scan (Copilot bot #585
                // off-by-one ask).
                int realCount = items.Count;
                if (realCount > 0 && items[realCount - 1].addr == 0)
                    realCount--;
                ReadCountBox.Text = realCount.ToString();
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);

                // #840: the selected entry's stored tag is its 0-based row index
                // (LoadList emits `new AddrResult(addr, name, (uint)i)`). The WF
                // custompalette override is `paletteno = AddressList.SelectedIndex
                // + 1`, so the unit-palette slot is rowIndex + 1.
                var selected = EntryList.SelectedItem;
                _vm.SelectedPaletteSlot = selected != null && selected.addr != 0
                    ? (int)selected.tag + 1
                    : 0;

                UpdateUI();
                RefreshSamplePreview();
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SelectedAddressBox.Text = $"0x{_vm.CurrentAddr:X08}";
            IdentNameLabel.Text = _vm.IdentifierName;
            Id0Box.Value = _vm.Id0;
            Id1Box.Value = _vm.Id1;
            Id2Box.Value = _vm.Id2;
            Id3Box.Value = _vm.Id3;
            Id4Box.Value = _vm.Id4;
            Id5Box.Value = _vm.Id5;
            Id6Box.Value = _vm.Id6;
            Id7Box.Value = _vm.Id7;
            Id8Box.Value = _vm.Id8;
            Id9Box.Value = _vm.Id9;
            Id10Box.Value = _vm.Id10;
            Id11Box.Value = _vm.Id11;
            PalettePointerBox.Text = $"0x{_vm.PalettePointer:X08}";
            PaletteAddressBox.Text = $"0x{_vm.PalettePointer:X08}";

            // Seed the Class selector NumericUpDown so it is never left at
            // Avalonia's decimal? default (null) — the production data-verify UI
            // check (MainWindow.CheckNumericUpDownsDisplayValues) flags an
            // IsEffectivelyVisible NumericUpDown with a null Value as UI_EMPTY
            // (the same trap the old BattleAnimeBox hit, #612/#613/#616/#623/#625).
            // ClassBox.ValueChanged -> OnClassChanged then re-renders the preview.
            ClassBox.Value = _vm.ClassID;
            ClassNameLabel.Text = _vm.ClassName;

            // Push RGB channels from VM to the 16 swatch inputs.
            for (int i = 0; i < 16; i++)
            {
                if (_rBoxes.Length > i && _rBoxes[i] != null) _rBoxes[i].Value = _vm.RChannel[i];
                if (_gBoxes.Length > i && _gBoxes[i] != null) _gBoxes[i].Value = _vm.GChannel[i];
                if (_bBoxes.Length > i && _bBoxes[i] != null) _bBoxes[i].Value = _vm.BChannel[i];
                RefreshSwatch(i);
            }
        }

        /// <summary>Repaint a swatch from the current (R, G, B) NumericUpDown values.</summary>
        void RefreshSwatch(int i)
        {
            if (_swatches.Length <= i || _swatches[i] == null) return;
            uint r = (uint)(_rBoxes[i]?.Value ?? 0);
            uint g = (uint)(_gBoxes[i]?.Value ?? 0);
            uint b = (uint)(_bBoxes[i]?.Value ?? 0);
            // RGB555 -> RGB888 expand-to-8bit (replicate top 3 bits in low nibble)
            byte r8 = (byte)((r << 3) | (r >> 2));
            byte g8 = (byte)((g << 3) | (g >> 2));
            byte b8 = (byte)((b << 3) | (b >> 2));
            _swatches[i].Background = new SolidColorBrush(Color.FromRgb(r8, g8, b8));
        }

        /// <summary>
        /// #840: re-render the class battle-anime sample preview for the selected
        /// unit-palette slot + class + sub-palette and push it into the
        /// GbaImageControl. The render uses the UNIT palette as the base (the
        /// palette-override path, mirroring WF <c>DrawBattleAnime custompalette</c>),
        /// NOT the anime's own palette. Also refreshes the resolved
        /// battle-anime-ID display. Null-safe: a null render clears the preview
        /// (<c>SetImage(null)</c>) and blanks the anime-ID field.
        /// </summary>
        void RefreshSamplePreview()
        {
            // Surface the resolved battle-anime ID (informational, read-only).
            try
            {
                ROM rom = CoreState.ROM;
                if (rom != null && _vm.ClassID > 0)
                {
                    uint animeId = FEBuilderGBA.Core.ClassFormCore.GetAnimeIDByClassID(rom, (int)_vm.ClassID);
                    BattleAnimeBox.Text = animeId > 0 ? $"0x{animeId:X02}" : "";
                }
                else
                {
                    BattleAnimeBox.Text = "";
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.RefreshSamplePreview (anime id) failed: {0}", ex.Message);
                BattleAnimeBox.Text = "";
            }

            // Render the 12-cell sample grid with the unit-palette override.
            try
            {
                // IImage is IDisposable (Skia-backed). GbaImageControl.SetImage
                // copies the pixels into an independent WriteableBitmap and does
                // NOT take ownership, so dispose the freshly-rendered grid after
                // SetImage has copied it (also covers the null case -> clear).
                using IImage grid = _vm.RenderClassSamplePreview();
                SamplePreview.SetImage(grid);
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.RefreshSamplePreview failed: {0}", ex.Message);
                SamplePreview.SetImage(null);
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Unit Palette");
            try
            {
                _vm.Id0 = (uint)(Id0Box.Value ?? 0);
                _vm.Id1 = (uint)(Id1Box.Value ?? 0);
                _vm.Id2 = (uint)(Id2Box.Value ?? 0);
                _vm.Id3 = (uint)(Id3Box.Value ?? 0);
                _vm.Id4 = (uint)(Id4Box.Value ?? 0);
                _vm.Id5 = (uint)(Id5Box.Value ?? 0);
                _vm.Id6 = (uint)(Id6Box.Value ?? 0);
                _vm.Id7 = (uint)(Id7Box.Value ?? 0);
                _vm.Id8 = (uint)(Id8Box.Value ?? 0);
                _vm.Id9 = (uint)(Id9Box.Value ?? 0);
                _vm.Id10 = (uint)(Id10Box.Value ?? 0);
                _vm.Id11 = (uint)(Id11Box.Value ?? 0);
                _vm.PalettePointer = ParseHexText(PalettePointerBox.Text);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ImageUnitPaletteView.Write: {0}", ex.Message); }
        }

        /// <summary>
        /// Functional palette write-back using <see cref="UnitPaletteWriteCore"/>.
        /// Reads the 16 RGB NumericUpDowns, compresses the new palette via LZ77,
        /// writes in-place when it fits or reallocates at ROM end and patches
        /// the P12 slot under the same undo scope.
        /// </summary>
        void PaletteWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.CurrentAddr == 0)
            {
                Log.Error("ImageUnitPaletteView.PaletteWrite: no selected entry.");
                return;
            }
            var r = new uint[16];
            var g = new uint[16];
            var b = new uint[16];
            for (int i = 0; i < 16; i++)
            {
                r[i] = (uint)(_rBoxes[i]?.Value ?? 0);
                g[i] = (uint)(_gBoxes[i]?.Value ?? 0);
                b[i] = (uint)(_bBoxes[i]?.Value ?? 0);
            }
            int paletteIndex = PaletteTypeCombo.SelectedIndex;
            if (paletteIndex < 0) paletteIndex = 0;
            bool isOverrideAll = PaletteOverrideAllCheck.IsChecked ?? false;
            _undoService.Begin("Write Unit Palette");
            try
            {
                // `undo: null` — the ambient scope opened by `_undoService.Begin`
                // takes care of all ROM writes. Passing the active UndoData
                // through the explicit (addr, value, undo) overloads would
                // double-record every entry (Copilot bot #585 caught this).
                uint newP12 = UnitPaletteWriteCore.WritePalette(
                    CoreState.ROM,
                    _vm.CurrentAddr + 12,
                    r, g, b,
                    paletteIndex,
                    isOverrideAll,
                    undo: null);
                if (newP12 == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    Log.Error("ImageUnitPaletteView.PaletteWrite: WritePalette returned NOT_FOUND (invalid pointer or LZ77 stream).");
                    return;
                }
                _vm.PalettePointer = newP12;
                PalettePointerBox.Text = $"0x{newP12:X08}";
                PaletteAddressBox.Text = $"0x{newP12:X08}";
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ImageUnitPaletteView.PaletteWrite: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #904: Export the rendered class battle-anime SAMPLE GRID preview as a
        /// PNG. Delegates to <see cref="Controls.GbaImageControl.ExportPng"/> on
        /// the SamplePreview control — that helper owns the Skia IImage backing
        /// store, the save dialog, and the null-guard (no-ops when no preview is
        /// rendered). Read-only: never touches the ROM. Mirrors WinForms
        /// ImageUnitPaletteForm.ExportButton_Click (which exports DrawBitmap, the
        /// same recolored sample grid).
        /// </summary>
        async void ExportImage_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                await SamplePreview.ExportPng(this);
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.ExportImage: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #904: Import a ≤16-color image's palette into the 16 R/G/B
        /// NumericUpDowns, then write it back to ROM via the existing
        /// <see cref="PaletteWrite_Click"/> path. Mirrors WinForms
        /// ImageUnitPaletteForm.ImportButton_Click
        /// (MakePaletteBitmapToUIEx -> PaletteWrite).
        ///
        /// CORRECTION 3a: rejects (with a localized error, NO change) any image
        /// with more than 16 distinct colors — no quantization, which would
        /// scramble the semantic index order.
        /// CORRECTION 3b: extracts the 16 entries IN INDEX ORDER (index 0 =
        /// transparent/backdrop) via <see cref="UnitPaletteImportCore"/>.
        /// CORRECTION 2: populates the NumericUpDown controls (the source of
        /// truth PaletteWrite_Click reads), NOT the VM channels.
        /// </summary>
        async void ImportImage_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Guard: require a selected palette entry (mirrors PaletteWrite).
                if (_vm.CurrentAddr == 0)
                {
                    CoreState.Services?.ShowError(R._("Select a palette entry first."));
                    return;
                }
                if (CoreState.ImageService == null)
                {
                    CoreState.Services?.ShowError(R._("Image service not initialized."));
                    return;
                }

                string? path = await Dialogs.FileDialogHelper.OpenImageFile(this);
                if (string.IsNullOrEmpty(path)) return; // user cancelled

                ImportFromFile(path);
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitPaletteView.ImportImage: {0}", ex.Message);
                CoreState.Services?.ShowError($"{R._("Import failed:")} {ex.Message}");
            }
        }

        /// <summary>
        /// Dialog-free import core (testable seam): load the image, run the
        /// ≤16-color guard + ordered extraction, populate the NumericUpDowns,
        /// then reuse the ROM write. Returns <c>true</c> when the palette was
        /// applied + written; <c>false</c> when the image was rejected (no UI
        /// or ROM change). The <see cref="ImportImage_Click"/> handler owns the
        /// pre-checks (selected entry, image service) + file dialog.
        /// </summary>
        internal bool ImportFromFile(string path)
        {
            if (_vm.CurrentAddr == 0) return false;
            var imgService = CoreState.ImageService;
            if (imgService == null) return false;

            byte[] gbaPalette;
            byte[] rgbaPixels;
            using (IImage image = imgService.LoadImage(path))
            {
                // Prefer a loader-preserved indexed palette; fall back to the
                // RGBA pixels (the SkiaSharp loader decodes to RGBA, so this is
                // the common path). The Core helper handles both.
                gbaPalette = image.IsIndexed ? image.GetPaletteGBA() : System.Array.Empty<byte>();
                rgbaPixels = image.IsIndexed ? System.Array.Empty<byte>() : image.GetPixelData();
            }

            // CORRECTION 3a + 3b: ≤16-color guard + ordered extraction.
            if (!UnitPaletteImportCore.TryExtractIndexOrdered(
                    gbaPalette, rgbaPixels, out uint[] r, out uint[] g, out uint[] b))
            {
                CoreState.Services?.ShowError(
                    R._("The image must use 16 colors or fewer. Reduce its color count and try again."));
                return false; // NO change
            }

            // CORRECTION 2: populate the NumericUpDowns (NOT the VM channels).
            // PaletteWrite_Click reads _rBoxes/_gBoxes/_bBoxes, so seeding the VM
            // would cause a stale write. UI-only updates — no undo scope here
            // (PaletteWrite_Click owns its own).
            ApplyImportedChannels(r, g, b);

            // Reuse the existing ROM write (owns its undo scope + LZ77/repoint).
            PaletteWrite_Click(null, null);

            // Refresh the sample preview to reflect the new palette.
            RefreshSamplePreview();
            return true;
        }

        /// <summary>
        /// Push 16 index-ordered RGB555 channel triples into the swatch
        /// NumericUpDowns (mirrors the <see cref="UpdateUI"/> swatch loop) and
        /// repaint each swatch. UI-only; no undo scope.
        /// </summary>
        void ApplyImportedChannels(uint[] r, uint[] g, uint[] b)
        {
            for (int i = 0; i < 16; i++)
            {
                if (_rBoxes.Length > i && _rBoxes[i] != null) _rBoxes[i].Value = r[i];
                if (_gBoxes.Length > i && _gBoxes[i] != null) _gBoxes[i].Value = g[i];
                if (_bBoxes.Length > i && _bBoxes[i] != null) _bBoxes[i].Value = b[i];
                RefreshSwatch(i);
            }
        }

        void Reload_Click(object? sender, RoutedEventArgs e) => LoadList();

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
