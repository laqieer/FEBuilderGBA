using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageUnitPaletteView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ImageUnitPaletteViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        // Per-swatch control references for fast UI updates.
        NumericUpDown[] _rBoxes = Array.Empty<NumericUpDown>();
        NumericUpDown[] _gBoxes = Array.Empty<NumericUpDown>();
        NumericUpDown[] _bBoxes = Array.Empty<NumericUpDown>();
        Border[] _swatches = Array.Empty<Border>();

        // #1022: the 16 R/G/B spinners always edit sub-palette BLOCK 0 — the VM's
        // LoadPaletteFromROM decodes the FIRST 16 colors (raw[i*2] for i in 0..15)
        // into _r/_g/_b, and no combo SelectionChanged reloads them from another
        // block (PaletteTypeCombo is only read by the Write path; the preview combo
        // only drives PaletteTypeIndex). So the live-edited block aligns with the
        // previewed sub-palette ONLY when PaletteTypeIndex == 0.
        const int EditableBlockIndex = 0;

        public string ViewTitle => "Unit Palette Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Unit Palette Editor", 1305, 911, SizeToContent: true);
        public event EventHandler? CloseRequested;

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

            // #1006: populate the Zoom combo in code via R._() (same reason —
            // ComboBoxItem.Content is not scanned by ViewTranslationHelper, so a
            // XAML "Actual size" literal would never localize). Index 0 = 1x
            // ("Actual size"), index i = (i+1)x; Zoom_SelectionChanged maps
            // SelectedIndex + 1 -> GbaImageControl.Zoom (clamped 1..8).
            ZoomCombo.Items.Clear();
            ZoomCombo.Items.Add(R._("Actual size"));
            for (int z = 2; z <= 8; z++) ZoomCombo.Items.Add(z + "x");
            ZoomCombo.SelectedIndex = 0;

            // #840: the class selector + sub-palette combo both re-render the
            // sample preview (the cross-platform equivalent of WF
            // X_DISPLAY_CLASS_ValueChanged / PaletteIndexComboBox change ->
            // DrawSample). Null-safe via RefreshSamplePreview.
            ClassBox.ValueChanged += OnClassChanged;
            PreviewPaletteTypeCombo.SelectionChanged += OnPreviewPaletteTypeChanged;

        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                CacheSwatchControls();
                LoadList();
            }
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
                Log.ErrorF("ImageUnitPaletteView.OnClassChanged failed: {0}", ex.Message);
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
                Log.ErrorF("ImageUnitPaletteView.OnPreviewPaletteTypeChanged failed: {0}", ex.Message);
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
                // #1022: every USER R/G/B change repaints the single swatch AND
                // live-recolors the sample battle-anime preview (the WF
                // OnChangeColor live-recolor). Suppressed during programmatic bulk
                // loads (UpdateUI / ApplyImportedChannels set _vm.IsLoading = true
                // around their 48-spinner writes) so a single entry-load/import
                // fires ZERO per-spinner renders — the load path runs ONE final
                // RefreshSamplePreview() itself.
                if (_rBoxes[i] != null) _rBoxes[i].ValueChanged += (_, _) => { RefreshSwatch(captureIdx); if (!_vm.IsLoading) RefreshSamplePreview(); };
                if (_gBoxes[i] != null) _gBoxes[i].ValueChanged += (_, _) => { RefreshSwatch(captureIdx); if (!_vm.IsLoading) RefreshSamplePreview(); };
                if (_bBoxes[i] != null) _bBoxes[i].ValueChanged += (_, _) => { RefreshSwatch(captureIdx); if (!_vm.IsLoading) RefreshSamplePreview(); };
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
                Log.ErrorF("ImageUnitPaletteView.LoadList failed: {0}", ex.Message);
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

                // #985: resolve which class actually uses this palette slot so the
                // Edit-tab Battle Animation id + sample preview populate (WinForms
                // ImageUnitPaletteForm.MakeClassList). SelectedPaletteSlot is the
                // 1-based slot; the resolver wants the 0-based AddressList index.
                // Runs on EVERY selection so switching entries updates the class.
                uint cls = FEBuilderGBA.Core.UnitPaletteClassResolverCore
                    .ResolveDefaultPreviewClass(CoreState.ROM, _vm.SelectedPaletteSlot - 1);
                if (cls == 0)
                    cls = FEBuilderGBA.Core.UnitPaletteClassResolverCore
                        .FindFirstClassWithAnime(CoreState.ROM);
                if (cls != 0)
                {
                    _vm.ClassID = cls;
                    _vm.ClassName = NameResolver.GetClassName(cls);
                }
                else
                {
                    // Both the slot resolver AND the anime fallback found nothing
                    // for THIS selection — clear so the UI/preview don't show the
                    // previous entry's stale class/anime/preview (WF rebuilds the
                    // class list per selection; empty == no class for this slot).
                    _vm.ClassID = 0;
                    _vm.ClassName = "";
                }

                UpdateUI();
                RefreshSamplePreview();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageUnitPaletteView.OnSelected failed: {0}", ex.Message);
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
                Log.ErrorF("ImageUnitPaletteView.RefreshSamplePreview (anime id) failed: {0}", ex.Message);
                BattleAnimeBox.Text = "";
            }

            // Render the 12-cell sample grid with the unit-palette override.
            try
            {
                // #1022: live-recolor — feed the in-memory R/G/B spinners as the
                // EXACT 32-byte override ONLY when the previewed sub-palette index
                // equals the editable block (the spinners edit block 0). The
                // alignment guard prevents an edit from recoloring a sub-palette
                // the spinners are NOT editing (e.g. previewing Enemy while the
                // spinners hold the Player block). When they don't align, pass null
                // so the SAVED on-ROM palette renders.
                byte[] edited = (_vm.PaletteTypeIndex == EditableBlockIndex)
                    ? BuildEditedPaletteBlock()
                    : null;
                // IImage is IDisposable (Skia-backed). GbaImageControl.SetImage
                // copies the pixels into an independent WriteableBitmap and does
                // NOT take ownership, so dispose the freshly-rendered grid after
                // SetImage has copied it (also covers the null case -> clear).
                using IImage grid = _vm.RenderClassSamplePreview(
                    (int)_vm.ClassID, _vm.SelectedPaletteSlot, _vm.PaletteTypeIndex, edited);
                SamplePreview.SetImage(grid);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageUnitPaletteView.RefreshSamplePreview failed: {0}", ex.Message);
                SamplePreview.SetImage(null);
            }
        }

        /// <summary>
        /// #1022: pack the 16 in-memory R/G/B NumericUpDowns into the EXACT 32-byte
        /// little-endian RGB555 block (the live-edited sub-palette) via
        /// <see cref="UnitPaletteWriteCore.PackRgb555"/> — the same encoder the
        /// ROM write path uses. Reuses the Write path's spinner read so the
        /// preview override is byte-for-byte what a Write would persist. Returns
        /// exactly 32 bytes (16 colors x RGB555).
        /// </summary>
        byte[] BuildEditedPaletteBlock()
        {
            var r = new uint[16];
            var g = new uint[16];
            var b = new uint[16];
            for (int i = 0; i < 16; i++)
            {
                r[i] = (uint)(_rBoxes.Length > i ? (_rBoxes[i]?.Value ?? 0) : 0);
                g[i] = (uint)(_gBoxes.Length > i ? (_gBoxes[i]?.Value ?? 0) : 0);
                b[i] = (uint)(_bBoxes.Length > i ? (_bBoxes[i]?.Value ?? 0) : 0);
            }
            return UnitPaletteWriteCore.PackRgb555(r, g, b);
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
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("ImageUnitPaletteView.Write: {0}", ex.Message); }
        }

        /// <summary>Palette Write button handler — delegates to
        /// <see cref="PerformPaletteWrite"/>.</summary>
        void PaletteWrite_Click(object? sender, RoutedEventArgs e) => PerformPaletteWrite();

        /// <summary>
        /// Functional palette write-back using <see cref="UnitPaletteWriteCore"/>.
        /// Reads the 16 RGB NumericUpDowns, compresses the new palette via LZ77,
        /// writes in-place when it fits or reallocates at ROM end and patches
        /// the P12 slot under the same (single) undo scope.
        ///
        /// Sender/args-free so it can be reused programmatically (e.g. from the
        /// import path) WITHOUT invoking the event handler with null
        /// <see cref="RoutedEventArgs"/> (the #906 review fix). Returns
        /// <c>true</c> on a successful write; <c>false</c> when there is no
        /// selected entry or the core write failed/rolled back.
        /// </summary>
        bool PerformPaletteWrite()
        {
            if (_vm.CurrentAddr == 0)
            {
                Log.Error("ImageUnitPaletteView.PaletteWrite: no selected entry.");
                return false;
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
                    return false;
                }
                _vm.PalettePointer = newP12;
                PalettePointerBox.Text = $"0x{newP12:X08}";
                PaletteAddressBox.Text = $"0x{newP12:X08}";
                _undoService.Commit();
                _vm.MarkClean();
                return true;
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ImageUnitPaletteView.PaletteWrite: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// #1067: "New Palette Allocation" — allocate a FRESH free-space palette
        /// block for the selected row and repoint its P12 pointer, giving the
        /// slot its OWN independent palette (no longer sharing the previous
        /// block). Mirrors <see cref="PerformPaletteWrite"/> exactly: same
        /// spinner read, same paletteIndex/override-all read, same single undo
        /// scope (Begin / Commit / Rollback) + reload + MarkClean. The only
        /// difference is the Core seam — <see cref="UnitPaletteWriteCore.AllocNewPalette"/>
        /// always appends + repoints (never in-place), and leaves the OLD block
        /// untouched for shared-palette safety.
        /// </summary>
        void NewAlloc_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.CurrentAddr == 0)
            {
                Log.Error("ImageUnitPaletteView.NewAlloc: no selected entry.");
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
            _undoService.Begin("New Unit Palette Allocation");
            try
            {
                uint newP12 = UnitPaletteWriteCore.AllocNewPalette(
                    CoreState.ROM,
                    _vm.CurrentAddr + 12,
                    r, g, b,
                    paletteIndex,
                    isOverrideAll);
                if (newP12 == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    Log.Error("ImageUnitPaletteView.NewAlloc: AllocNewPalette returned NOT_FOUND (no free space or write fault).");
                    CoreState.Services?.ShowError(R._("Failed to allocate a new palette block. Check ROM free space."));
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
                Log.ErrorF("ImageUnitPaletteView.NewAlloc: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Failed to allocate a new palette block. Check ROM free space."));
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
                await SamplePreview.ExportPng(TopLevel.GetTopLevel(this) as Window);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageUnitPaletteView.ExportImage: {0}", ex.Message);
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

                string? path = await Dialogs.FileDialogHelper.OpenImageFile(TopLevel.GetTopLevel(this) as Window);
                if (string.IsNullOrEmpty(path)) return; // user cancelled

                ImportFromFile(path);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageUnitPaletteView.ImportImage: {0}", ex.Message);
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

            // Reuse the existing ROM write (owns its single undo scope +
            // LZ77/repoint). #906 review: call the sender/args-free core method,
            // NOT the event handler with null RoutedEventArgs.
            PerformPaletteWrite();

            // Refresh the sample preview to reflect the new palette.
            RefreshSamplePreview();
            return true;
        }

        /// <summary>
        /// Push 16 index-ordered RGB555 channel triples into the swatch
        /// NumericUpDowns (mirrors the <see cref="UpdateUI"/> swatch loop) and
        /// repaint each swatch. UI-only; no undo scope.
        ///
        /// #1022: wrapped in <c>_vm.IsLoading = true</c> so the 16 bulk spinner
        /// writes do NOT each fire <see cref="RefreshSamplePreview"/> (the per-box
        /// ValueChanged is suppressed while loading) — the import path renders the
        /// preview ONCE itself after the write.
        /// </summary>
        void ApplyImportedChannels(uint[] r, uint[] g, uint[] b)
        {
            _vm.IsLoading = true;
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    if (_rBoxes.Length > i && _rBoxes[i] != null) _rBoxes[i].Value = r[i];
                    if (_gBoxes.Length > i && _gBoxes[i] != null) _gBoxes[i].Value = g[i];
                    if (_bBoxes.Length > i && _bBoxes[i] != null) _bBoxes[i].Value = b[i];
                    RefreshSwatch(i);
                }
            }
            finally { _vm.IsLoading = false; }
        }

        void Reload_Click(object? sender, RoutedEventArgs e) => LoadList();

        /// <summary>
        /// #1078: "Expand List" handler. Prompts for a new entry count, then
        /// delegates to <see cref="ImageUnitPaletteViewModel.ExpandList"/> inside
        /// an <see cref="UndoService"/> scope, and reloads the list. Mirrors
        /// <see cref="ImageMapActionAnimationView.ListExpand_Click"/> (prompt ->
        /// expand -> repoint -> reload). The VM's ExpandList is PREDICATE-AWARE:
        /// it uses the real (sentinel-excluded) row count, FIRST-fills new rows
        /// from a non-empty template row + clears each new P12 so the rows are
        /// scan-visible, writes a FULL all-zero terminator row, and repoints all
        /// raw + LDR-literal references via
        /// <see cref="DataExpansionCore.RepointAllReferences"/>.
        /// </summary>
        async void Expand_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }

                // Real (sentinel-excluded) current row count.
                int listCount = _vm.GetListCount();
                int realCount = listCount - 1;
                if (realCount < 1)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: the unit-palette list has no rows."));
                    return;
                }
                uint currentCount = (uint)realCount;

                // Default = current count + 1, max = 512 (LoadList's scan bound).
                uint defaultCount = currentCount + 1;
                if (defaultCount > 512) defaultCount = 512;
                uint? chosen = await NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the new entry count for the unit-palette list (current: {0}, max: 512).",
                        currentCount),
                    R._("List Expansion"),
                    defaultCount,
                    currentCount,
                    512);
                if (chosen == null) return; // user cancelled
                uint newCount = chosen.Value;
                if (newCount == currentCount)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count equals current count."));
                    return;
                }

                _undoService.Begin("Expand Unit Palette List");
                try
                {
                    string err = _vm.ExpandList(newCount, _undoService.GetActiveUndoData());
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    LoadList();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded unit-palette list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.ErrorF("ImageUnitPaletteView.Expand_Click inner failed: {0}", inner.Message);
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                // Defensively roll back any undo scope left open by an exception
                // thrown before the inner try (e.g. from the dialog or the
                // count-prep path) and surface the error to the user (Copilot
                // review on PR #1080 — the outer catch previously only logged).
                try { _undoService.Rollback(); } catch { /* no active scope — ignore */ }
                Log.ErrorF("ImageUnitPaletteView.Expand_Click failed: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("List expansion failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// #1006: Palette-to-clipboard. Mirrors the #974 TSA-editor pattern
        /// (<see cref="ImageTSAEditorView.PaletteClipboard_Click"/>): pack the 16
        /// current palette entries to GBA 5-5-5 big-endian hex (4 chars/entry,
        /// 64 chars total) and copy via the Avalonia <c>IClipboard.SetTextAsync</c>
        /// async API. Read-only: never writes ROM.
        ///
        /// The Unit Palette R/G/B NumericUpDowns hold 5-BIT channels (0..31),
        /// whereas <see cref="ImageTSAEditorViewModel.BuildPaletteClipboardHex"/>
        /// expects 8-BIT R/G/B (it <c>&gt;&gt;3</c>s internally back to 5-5-5). So
        /// each channel is expanded 5-bit -&gt; 8-bit via <c>(c&lt;&lt;3)|(c&gt;&gt;2)</c>
        /// (replicate the top 3 bits in the low nibble) before packing, so the
        /// round-trip preserves the original 5-bit value (e.g. channel 31 -&gt; 0x1F,
        /// NOT 3).
        /// </summary>
        async void Clipboard_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rgb = new (byte R, byte G, byte B)[16];
                for (int i = 0; i < 16; i++)
                {
                    byte Exp(decimal? v) { int c = (int)(v ?? 0) & 0x1F; return (byte)((c << 3) | (c >> 2)); }
                    rgb[i] = (Exp(_rBoxes[i]?.Value), Exp(_gBoxes[i]?.Value), Exp(_bBoxes[i]?.Value));
                }
                string hex = ImageTSAEditorViewModel.BuildPaletteClipboardHex(rgb);

                var cb = global::Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
                if (cb == null) { CoreState.Services?.ShowError(R._("Clipboard is not available.")); return; }
                await cb.SetTextAsync(hex);
                Log.Notify($"ImageUnitPalette: palette copied to clipboard ({hex}).");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageUnitPaletteView.Clipboard_Click failed: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Failed to copy palette to clipboard."));
            }
        }

        /// <summary>
        /// #1006: Zoom the sample-preview <see cref="Controls.GbaImageControl"/>.
        /// The combo items are explicit integer factors (index 0 = "Actual size"
        /// = 1x, index 1 = "2x", … index 7 = "8x"), so the factor is
        /// <c>SelectedIndex + 1</c>. <see cref="Controls.GbaImageControl.Zoom"/>
        /// clamps to 1..8 internally. Guarded during a programmatic load so a
        /// SelectedIndex change made while loading doesn't fire.
        /// </summary>
        void Zoom_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            try
            {
                int factor = (ZoomCombo.SelectedIndex < 0) ? 1 : ZoomCombo.SelectedIndex + 1;
                SamplePreview.Zoom = factor;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageUnitPaletteView.Zoom_SelectionChanged failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #1006: Undo the last ROM change. Mirrors
        /// <see cref="ImageBattleAnimePalletView"/>: <c>CoreState.Undo.RunUndo()</c>
        /// is void and tolerates an empty stack (no-op), then re-read the reverted
        /// ROM into the UI via <see cref="ReloadAfterUndoRedo"/>.
        /// </summary>
        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo == null) return;
                // RunUndo() is void and no-ops on an empty stack. Only reload (which
                // overwrites the spinners + marks clean) when the undo position
                // actually moved — otherwise an empty-stack click would silently
                // discard unsaved spinner edits (Copilot bot review on PR #1068).
                int before = CoreState.Undo.Postion;
                CoreState.Undo.RunUndo();
                if (CoreState.Undo.Postion != before)
                    ReloadAfterUndoRedo();
            }
            catch (Exception ex) { Log.ErrorF("ImageUnitPaletteView.Undo_Click failed: {0}", ex.Message); }
        }

        /// <summary>
        /// #1006: Redo the last undone ROM change. Mirrors
        /// <see cref="ImageBattleAnimePalletView.Redo_Click"/>:
        /// <c>CoreState.Undo.RunRedo()</c> returns <c>bool</c>; an empty redo stack
        /// returns <c>false</c> -&gt; no-op (no reload). On success, re-read the ROM.
        /// </summary>
        void Redo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo == null || !CoreState.Undo.RunRedo()) return;   // empty -> no-op
                ReloadAfterUndoRedo();
            }
            catch (Exception ex) { Log.ErrorF("ImageUnitPaletteView.Redo_Click failed: {0}", ex.Message); }
        }

        /// <summary>
        /// #1006: After an Undo/Redo reverts the ROM bytes, re-read the current
        /// entry (P12 + the decompressed 16 R/G/B channels) and push the reverted
        /// values back into the 48 spinners. Reuses the exact same entry-load calls
        /// the editor runs on selection (<see cref="ImageUnitPaletteViewModel.LoadEntry"/>
        /// -&gt; <see cref="UpdateUI"/> -&gt; <see cref="RefreshSamplePreview"/>),
        /// wrapped in <c>IsLoading</c> so the 48 bulk spinner writes don't each
        /// fire a swatch/preview render, then <c>MarkClean()</c> since SetField
        /// during the reload would otherwise leave the VM marked dirty.
        /// </summary>
        void ReloadAfterUndoRedo()
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(_vm.CurrentAddr);   // re-read P12 + decompress spinners from the reverted ROM
                UpdateUI();                       // push VM channels back to the 16 spinners
                RefreshSamplePreview();
            }
            finally { _vm.IsLoading = false; }
            _vm.MarkClean();                      // SetField during reload would leave it dirty
        }

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
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
