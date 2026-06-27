// SPDX-License-Identifier: GPL-3.0-or-later
// ImageBattleScreenView -- Avalonia parity rebuild for #393. Mirrors the WF
// `ImageBattleScreenForm` 5-tab battle-screen layout editor. Uses the
// `ImageBattleScreenCore` helper (which delegates palette I/O to PaletteCore)
// for the TSA + palette + image-pointer write paths under the ambient
// UndoService scope.
// Per-image Import/Export wired in #872.
using System;
using System.Globalization;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBattleScreenView : TranslatedWindow, IEditorView
    {
        readonly ImageBattleScreenViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Battle Screen Layout";
        public bool IsLoaded => _vm.IsLoaded;

        // Cached references to the 48 numeric cells + 16 swatch borders so
        // we don't have to walk the visual tree on every reload.
        readonly NumericUpDown[] _rBoxes = new NumericUpDown[16];
        readonly NumericUpDown[] _gBoxes = new NumericUpDown[16];
        readonly NumericUpDown[] _bBoxes = new NumericUpDown[16];
        readonly Border[] _swatchBoxes = new Border[16];

        bool _suppressSpinnerEvents;

        public ImageBattleScreenView()
        {
            InitializeComponent();

            // Populate combos via R._() so they pick up ja/zh translations.
            PaletteIndexCombo.Items.Add(R._("Palette 1"));
            PaletteIndexCombo.Items.Add(R._("Palette 2"));
            PaletteIndexCombo.Items.Add(R._("Palette 3"));
            PaletteIndexCombo.Items.Add(R._("Palette 4"));
            PaletteIndexCombo.SelectedIndex = 0;

            ZoomCombo.Items.Add(R._("1x Zoom"));
            ZoomCombo.Items.Add(R._("2x Zoom"));
            ZoomCombo.Items.Add(R._("3x Zoom"));
            ZoomCombo.Items.Add(R._("4x Zoom"));
            // Set the default 2x selection HERE (not in AXAML) so the
            // Zoom_SelectionChanged handler fires now -- with BattlePreview
            // already constructed -- and applies zoom=2 to the live preview.
            // An AXAML SelectedIndex="1" would run before BattlePreview exists
            // (no-op zoom), leaving the combo at 2x but the preview at 1x
            // (#802 PR #804 review fix).
            ZoomCombo.SelectedIndex = 1;

            CachePaletteCells();
            InitializeSwatches();
            WireSpinnerHandlers();

            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void CachePaletteCells()
        {
            for (int i = 0; i < 16; i++)
            {
                int n = i + 1;
                _rBoxes[i] = this.FindControl<NumericUpDown>($"R{n}");
                _gBoxes[i] = this.FindControl<NumericUpDown>($"G{n}");
                _bBoxes[i] = this.FindControl<NumericUpDown>($"B{n}");
                _swatchBoxes[i] = this.FindControl<Border>($"Swatch{n}");
            }
        }

        void InitializeSwatches()
        {
            var defaultBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0));
            for (int i = 0; i < 16; i++)
            {
                if (_swatchBoxes[i] != null) _swatchBoxes[i].Background = defaultBrush;
            }
        }

        void WireSpinnerHandlers()
        {
            for (int i = 0; i < 16; i++)
            {
                int idx = i;
                if (_rBoxes[i] != null) _rBoxes[i].ValueChanged += (s, e) => OnRgbChanged(idx, 'R');
                if (_gBoxes[i] != null) _gBoxes[i].ValueChanged += (s, e) => OnRgbChanged(idx, 'G');
                if (_bBoxes[i] != null) _bBoxes[i].ValueChanged += (s, e) => OnRgbChanged(idx, 'B');
            }
        }

        void OnRgbChanged(int index, char channel)
        {
            if (_suppressSpinnerEvents) return;
            NumericUpDown box = channel == 'R' ? _rBoxes[index]
                              : channel == 'G' ? _gBoxes[index]
                              : _bBoxes[index];
            if (box == null) return;
            byte rawValue = (byte)((int)(box.Value ?? 0));
            switch (channel)
            {
                case 'R': _vm.SetR(index, rawValue); break;
                case 'G': _vm.SetG(index, rawValue); break;
                case 'B': _vm.SetB(index, rawValue); break;
            }
            // Snap to 5-bit (multiples of 8); push snapped value back.
            byte snappedValue = channel == 'R' ? _vm.GetR(index)
                              : channel == 'G' ? _vm.GetG(index)
                              : _vm.GetB(index);
            if (rawValue != snappedValue)
            {
                _suppressSpinnerEvents = true;
                try { box.Value = snappedValue; }
                finally { _suppressSpinnerEvents = false; }
            }
            UpdateSwatch(index);
        }

        void UpdateSwatch(int index)
        {
            if (_swatchBoxes[index] == null) return;
            byte r = _vm.GetR(index);
            byte g = _vm.GetG(index);
            byte b = _vm.GetB(index);
            _swatchBoxes[index].Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        void PopulateUI()
        {
            _suppressSpinnerEvents = true;
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    if (_rBoxes[i] != null) _rBoxes[i].Value = _vm.GetR(i);
                    if (_gBoxes[i] != null) _gBoxes[i].Value = _vm.GetG(i);
                    if (_bBoxes[i] != null) _bBoxes[i].Value = _vm.GetB(i);
                    UpdateSwatch(i);
                }
                PaletteAddressBox.Value = _vm.PaletteAddress;
                // AddrLabel shows TSA1 address (under the "TSA1 Address" bar
                // in AXAML). Per Copilot bot PR #594 inline review: the
                // earlier wiring incorrectly showed the palette address here.
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.TSA1Address);
                Image1ZImage.Value = _vm.Image1Pointer;
                Image2ZImage.Value = _vm.Image2Pointer;
                Image3ZImage.Value = _vm.Image3Pointer;
                Image4ZImage.Value = _vm.Image4Pointer;
                Image5ZImage.Value = _vm.Image5Pointer;
            }
            finally
            {
                _suppressSpinnerEvents = false;
            }
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry();
                PopulateUI();
                RefreshBattlePreview();
                RefreshChipsetPreview();
                RefreshImagePreviews();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.OnSelected failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Render the live battle-screen preview (#802) into the
        /// <c>BattlePreview</c> GbaImageControl. Null-safe: a corrupt/missing
        /// required source returns <c>null</c> from the Core helper, which
        /// <c>SetImage(null)</c> turns into a blank surface (no crash).
        /// </summary>
        void RefreshBattlePreview()
        {
            try
            {
                BattlePreview.SetImage(_vm.RenderBattlePreview());
                // Export PNG is gated on a successful render: HasImage is true
                // only when SetImage received a non-null IImage. CanExportBattle
                // is the VM-level source of truth (for headless tests); the
                // button IsEnabled mirrors it (no DataContext on this view).
                _vm.CanExportBattle = BattlePreview.HasImage;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.RefreshBattlePreview failed: {0}", ex.Message);
                BattlePreview.SetImage(null);
                _vm.CanExportBattle = false;
            }
            if (BattleExportPngButton != null)
            {
                BattleExportPngButton.IsEnabled = _vm.CanExportBattle;
            }
            // Bulk Export (#988) is the SAME composited-preview PNG path, so it is
            // gated on the SAME render-success state (CORRECTION 4). Bulk Import
            // (#988) also requires a valid loaded entry to read the strips/TSA, so
            // it tracks the same render-success state — corrupt source data can't
            // present an importable action.
            if (BulkExportButton != null)
            {
                BulkExportButton.IsEnabled = _vm.CanExportBattle;
            }
            if (BulkImportButton != null)
            {
                BulkImportButton.IsEnabled = _vm.CanExportBattle;
            }
        }

        /// <summary>
        /// Export the composited battle-screen preview to a PNG file via a save
        /// dialog. Mirrors WF <c>ImageBattleScreenForm.ExportButton_Click</c>
        /// (which exports the full <c>DrawBitmap</c>). Read-only — no ROM write.
        /// Enabled only when a render succeeded (<see cref="RefreshBattlePreview"/>
        /// set <c>CanExportBattle</c>/<c>HasImage</c>).
        /// </summary>
        async void BattleExportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                await BattlePreview.ExportPng(this, "battle_screen");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.BattleExportPng failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Render the chipset chip-list preview (#805) into the
        /// <c>ChipsetPreview</c> GbaImageControl. Mirrors the WF
        /// <c>MakeCHIPLIST()</c> flip/palette-bank permutation grid. Null-safe:
        /// a corrupt/missing required source returns <c>null</c> from the Core
        /// helper, which <c>SetImage(null)</c> turns into a blank surface
        /// (no crash) -- same pattern as <see cref="RefreshBattlePreview"/>.
        /// </summary>
        void RefreshChipsetPreview()
        {
            try
            {
                ChipsetPreview.SetImage(_vm.RenderChipsetPreview());
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.RefreshChipsetPreview failed: {0}", ex.Message);
                ChipsetPreview.SetImage(null);
            }
        }

        /// <summary>
        /// Render all 5 per-image previews (#816) into their
        /// <c>Image{1..5}Preview</c> GbaImageControls. Each image strip is
        /// decoded from its OWN LZ77 stream at its WF per-image dimensions
        /// (image1 = natural W x H, image2..image5 = liner-width x 8px), palette
        /// bank 0, index 0 opaque. Null-safe per control: a bad index or
        /// corrupt/missing strip returns <c>null</c> from the Core helper, which
        /// <c>SetImage(null)</c> turns into a blank surface (no crash) -- same
        /// pattern as <see cref="RefreshChipsetPreview"/>. Called on entry load
        /// and after every write/undo so the strips track the ROM.
        /// </summary>
        void RefreshImagePreviews()
        {
            var controls = new[] { Image1Preview, Image2Preview, Image3Preview, Image4Preview, Image5Preview };
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] == null) continue;
                try
                {
                    controls[i].SetImage(_vm.RenderImagePreview(i));
                }
                catch (Exception ex)
                {
                    Log.Error("ImageBattleScreenView.RefreshImagePreviews index ", i.ToString(), " failed: ", ex.Message);
                    controls[i].SetImage(null);
                }
            }
        }

        /// <summary>
        /// Drive the live preview's zoom from the top-bar Zoom combo
        /// (1x..4x). The GbaImageControl also supports its own
        /// toolbar/mouse-wheel zoom; this keeps the combo in sync (#802).
        /// </summary>
        void Zoom_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // Defensive: the handler can be reached during XAML init before
                // the BattlePreview field is assigned. Guard so we never NRE;
                // the constructor sets the initial selection AFTER all named
                // controls exist, so the real zoom-apply runs then (#802).
                if (BattlePreview == null || ZoomCombo == null) return;
                int idx = ZoomCombo.SelectedIndex;
                if (idx < 0) return;
                BattlePreview.Zoom = idx + 1; // combo 0..3 -> zoom 1..4
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.Zoom_SelectionChanged failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Write paths - all wrap the VM call in _undoService.Begin/Commit/Rollback
        // so the ambient ROM.BeginUndoScope captures every rom.write_* call
        // inside the Core helper (Plan v2 Finding #2).
        // -----------------------------------------------------------------
        void WriteButton_Click(object sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // Per Copilot bot PR #594 round 5 thread #2: refuse to write if
            // no entry has been loaded -- default zeros would wipe TSA
            // regions and write 0x08000000 into image pointer slots.
            if (!_vm.IsLoaded)
            {
                Log.Notify("WriteButton_Click: no entry loaded; refusing to write defaults.");
                return;
            }

            // Pull the ZIMAGE numerics back into the VM so they are part of
            // the write batch. Per Copilot bot PR #594 round 6 thread #1:
            // match the cast pattern from other Avalonia views
            // (e.g. AIASMCALLTALKView.axaml.cs uses (uint)(Box.Value ?? 0))
            // instead of the surprising double round-trip.
            _vm.Image1Pointer = (uint)(Image1ZImage.Value ?? 0);
            _vm.Image2Pointer = (uint)(Image2ZImage.Value ?? 0);
            _vm.Image3Pointer = (uint)(Image3ZImage.Value ?? 0);
            _vm.Image4Pointer = (uint)(Image4ZImage.Value ?? 0);
            _vm.Image5Pointer = (uint)(Image5ZImage.Value ?? 0);

            _undoService.Begin("Edit Battle Screen");
            bool ok;
            try
            {
                ok = _vm.Write();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.Write threw: {0}", ex.Message);
                _undoService.Rollback();
                return;
            }

            if (!ok)
            {
                _undoService.Rollback();
                Log.Notify("WriteButton_Click: write failed; rollback applied.");
                return;
            }

            _undoService.Commit();
            // Re-render BOTH live previews from the freshly-written ROM
            // (#802 battle preview + #805 chipset chip list -- image pointer
            // edits change the tileset the chip list renders).
            RefreshBattlePreview();
            RefreshChipsetPreview();
            RefreshImagePreviews();
        }

        void PaletteWrite_Click(object sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // Per Copilot bot PR #594 round 5 thread #3: refuse to write if
            // no entry has been loaded -- all-zero R/G/B arrays would
            // write a black palette over the existing one.
            if (!_vm.IsLoaded || _vm.PaletteAddress == 0)
            {
                Log.Notify("PaletteWrite_Click: no entry loaded; refusing to write defaults.");
                return;
            }

            _undoService.Begin("Edit Battle Screen Palette");
            bool ok;
            try
            {
                ok = _vm.WritePalette();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.WritePalette threw: {0}", ex.Message);
                _undoService.Rollback();
                return;
            }

            if (!ok)
            {
                _undoService.Rollback();
                Log.Notify("PaletteWrite_Click: write failed; rollback applied.");
                return;
            }

            _undoService.Commit();
            // Palette edits change the rendered colors in BOTH previews --
            // refresh battle preview (#802) and chipset chip list (#805,
            // both palette banks are shown in the chip-list columns).
            RefreshBattlePreview();
            RefreshChipsetPreview();
            RefreshImagePreviews();
        }

        void PaletteIndex_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                int newIndex = PaletteIndexCombo.SelectedIndex;
                if (newIndex < 0 || newIndex == _vm.PaletteIndex) return;
                _vm.PaletteIndex = newIndex;
                // Re-read ONLY the palette block at the new index so pending
                // image-pointer edits are preserved. Per Copilot CLI PR review
                // round 1 finding #2: WF PaletteFormRef.MakePaletteROMToUI only
                // reloads the palette UI on palette-index changes, not the
                // image pointer fields.
                _vm.LoadPalette();
                PopulatePaletteUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.PaletteIndex_SelectionChanged failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Populate ONLY the palette R/G/B spinners + swatches from the VM.
        /// Does NOT touch the image-pointer NumericUpDowns -- preserves any
        /// in-flight edits when the user switches palette types (Copilot CLI
        /// PR review round 1 finding #2).
        /// </summary>
        void PopulatePaletteUI()
        {
            _suppressSpinnerEvents = true;
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    if (_rBoxes[i] != null) _rBoxes[i].Value = _vm.GetR(i);
                    if (_gBoxes[i] != null) _gBoxes[i].Value = _vm.GetG(i);
                    if (_bBoxes[i] != null) _bBoxes[i].Value = _vm.GetB(i);
                    UpdateSwatch(i);
                }
            }
            finally
            {
                _suppressSpinnerEvents = false;
            }
        }

        void PaletteClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Build "RRGGBB,RRGGBB,..." line of 16 colors and copy.
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 16; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X2}",
                        _vm.GetR(i), _vm.GetG(i), _vm.GetB(i));
                }
                if (TopLevel.GetTopLevel(this) is { Clipboard: { } clipboard })
                {
                    _ = clipboard.SetTextAsync(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("PaletteClipboard_Click failed: {0}", ex.Message);
            }
        }

        void PaletteUndo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CoreState.Undo?.RunUndo();
                // Reload so the spinners reflect the rolled-back palette.
                _vm.LoadEntry();
                PopulateUI();
                RefreshBattlePreview();
                RefreshChipsetPreview();
                RefreshImagePreviews();
            }
            catch (Exception ex)
            {
                Log.ErrorF("PaletteUndo_Click failed: {0}", ex.Message);
            }
        }

        void PaletteRedo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo == null || !CoreState.Undo.CanRedo)
                {
                    CoreState.Services.ShowInfo("Nothing to redo.");
                    return;
                }
                if (!CoreState.Undo.RunRedo())
                {
                    CoreState.Services.ShowError("Redo failed.");
                    return;
                }
                // This DELIBERATELY mirrors PaletteUndo_Click's full LoadEntry reload
                // (redo = exact inverse of undo, WF parity) rather than the
                // palette-only LoadPalette() refresh used for palette-index switches.
                _vm.LoadEntry();
                PopulateUI();
                RefreshBattlePreview();
                RefreshChipsetPreview();
                RefreshImagePreviews();
            }
            catch (Exception ex)
            {
                Log.ErrorF("PaletteRedo_Click failed: {0}", ex.Message);
            }
        }

        void BulkUndo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CoreState.Undo?.RunUndo();
                _vm.LoadEntry();
                PopulateUI();
                RefreshBattlePreview();
                RefreshChipsetPreview();
                RefreshImagePreviews();
            }
            catch (Exception ex)
            {
                Log.ErrorF("BulkUndo_Click failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Bulk Import/Export handlers (#988).
        //
        // Export: the SAME composited-preview PNG path as the top "Export PNG"
        //   button (BattlePreview.ExportPng). Read-only; no ROM write; no undo.
        //   Gated on CanExportBattle/HasImage (CORRECTION 4).
        //
        // Import: load one 256x160 indexed image (SINGLE palette bank, <=16
        //   colors per the #989 SAFE policy), keep the existing TSA layout, and
        //   rewrite the tilesheet (split into the 5 strips) + bank 0 of the
        //   palette via the validate-all-before-mutate Core seam
        //   ImageBattleScreenCore.ImportBattleScreenBulk, under one UndoService
        //   scope (CORRECTION 2/3). A >16-color source is rejected (no mutation).
        //   On any returned error, rollback and surface the message.
        // -----------------------------------------------------------------

        /// <summary>
        /// Export the composited battle screen as a PNG — the same path as the
        /// top-bar Export PNG button (BattlePreview.ExportPng). Read-only; no ROM
        /// write. Enabled only when a render succeeded (CORRECTION 4).
        /// </summary>
        async void BulkExport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                await BattlePreview.ExportPng(this, "battle_screen");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleScreenView.BulkExport failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Bulk-import a 256x160 indexed image (#988; #989 SAFE single-bank
        /// policy). Loads + quantizes the user's image to a SINGLE palette bank
        /// (&lt;=16 colors), then calls the validate-all-before-mutate Core seam
        /// under one UndoService scope. A &gt;16-color source is REJECTED with a
        /// localized error and NO mutation (full multi-bank import is a documented
        /// follow-up). On any returned error string, rollback + log it. Refreshes
        /// all previews on success.
        /// </summary>
        async void BulkImport_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null) return;
            if (!_vm.IsLoaded)
            {
                Log.Notify("BulkImport_Click: no entry loaded; aborting.");
                return;
            }

            // Open file dialog and load+quantize. We quantize with a cap of
            // (BULK_MAX_COLORS + 1) so we can DETECT a source that genuinely
            // needs more than one palette bank (ColorCount > BULK_MAX_COLORS) and
            // reject it cleanly -- rather than silently downgrading a multi-bank
            // image to a single bank with the wrong colors (#989).
            string? filePath = await FileDialogHelper.OpenImageFile(this);
            if (string.IsNullOrEmpty(filePath)) return;

            var loadResult = ImageImportService.LoadAndQuantizeFromFile(
                filePath,
                ImageBattleScreenCore.BULK_WIDTH, ImageBattleScreenCore.BULK_HEIGHT,
                maxColors: ImageBattleScreenCore.BULK_MAX_COLORS + 1, strictSize: true);
            if (loadResult == null || !loadResult.Success)
            {
                string err = loadResult?.Error ?? "Unknown error";
                Log.ErrorF("BulkImport_Click: load/quantize failed: {0}", err);
                return;
            }

            // SAFE single-bank guard (#989): reject a source needing >16 colors.
            // The Core seam also enforces this (palette length + index range), but
            // surfacing it here gives the user a clear, localized message.
            if (loadResult.ColorCount > ImageBattleScreenCore.BULK_MAX_COLORS)
            {
                // R._ performs the {0} substitution and returns the localized
                // string; pass the single result to Log.Error (which concatenates
                // its string[] args).
                Log.Error(R._(
                    "Battle-screen bulk import supports a single palette bank (max {0} colors). Reduce the image to {0} colors first.",
                    ImageBattleScreenCore.BULK_MAX_COLORS.ToString()));
                return;
            }

            _undoService.Begin("Bulk Import Battle Screen");
            string error;
            try
            {
                error = ImageBattleScreenCore.ImportBattleScreenBulk(
                    rom, loadResult.IndexedPixels, loadResult.GBAPalette);
            }
            catch (Exception ex)
            {
                Log.ErrorF("BulkImport_Click: ImportBattleScreenBulk threw: {0}", ex.Message);
                _undoService.Rollback();
                return;
            }

            if (!string.IsNullOrEmpty(error))
            {
                Log.ErrorF("BulkImport_Click: {0}; rolling back.", error);
                _undoService.Rollback();
                return;
            }

            _undoService.Commit();

            // Reload so the VM reflects the new strip pointers + palette.
            _vm.LoadEntry();
            PopulateUI();
            RefreshBattlePreview();
            RefreshChipsetPreview();
            RefreshImagePreviews();
        }

        // -----------------------------------------------------------------
        // Per-image Import/Export handlers (Image1..Image5, #872).
        //
        // Export: render the image strip via ImageBattleScreenCore
        //   → GbaImageControl.ExportPng → save PNG.
        //   Read-only; no ROM write; no undo scope needed.
        //
        // Import: open file dialog → LoadAndQuantizeFromFile (maps user pixels
        //   to existing shared palette) → ImageBattleScreenCore.WritePerImageStrip
        //   (EncodeDirectTiles4bpp → LZ77 → free-space write + repoint) → refresh
        //   all previews. Wrapped in _undoService.Begin/Commit/Rollback so a
        //   failed write leaves the ROM in the pre-import state.
        //
        // The shared palette is read-only during per-image import (it is shared
        // across all 5 image strips -- mirroring the WF RevChipImage path which
        // never writes the palette). The user must use the Palette tab for that.
        // -----------------------------------------------------------------

        // Image1
        async void Image1Export_Click(object? sender, RoutedEventArgs e) => await ImageExport_Click(0, Image1Preview, "battle_screen_image1");
        async void Image1Import_Click(object? sender, RoutedEventArgs e) => await ImageImport_Click(0);

        // Image2
        async void Image2Export_Click(object? sender, RoutedEventArgs e) => await ImageExport_Click(1, Image2Preview, "battle_screen_image2");
        async void Image2Import_Click(object? sender, RoutedEventArgs e) => await ImageImport_Click(1);

        // Image3
        async void Image3Export_Click(object? sender, RoutedEventArgs e) => await ImageExport_Click(2, Image3Preview, "battle_screen_image3");
        async void Image3Import_Click(object? sender, RoutedEventArgs e) => await ImageImport_Click(2);

        // Image4
        async void Image4Export_Click(object? sender, RoutedEventArgs e) => await ImageExport_Click(3, Image4Preview, "battle_screen_image4");
        async void Image4Import_Click(object? sender, RoutedEventArgs e) => await ImageImport_Click(3);

        // Image5
        async void Image5Export_Click(object? sender, RoutedEventArgs e) => await ImageExport_Click(4, Image5Preview, "battle_screen_image5");
        async void Image5Import_Click(object? sender, RoutedEventArgs e) => await ImageImport_Click(4);

        /// <summary>
        /// Export one per-image strip as PNG. Reads the current rendered IImage
        /// from the GbaImageControl (same surface shown in the preview, so what
        /// the user sees is what gets exported). Read-only; no ROM write.
        /// Mirrors <see cref="BattleExportPng_Click"/>: awaits ExportPng and
        /// logs/surfaces any File.Create / bitmap.Save exception so it is never
        /// swallowed as an unobserved task exception (#874 review fix).
        /// </summary>
        internal async System.Threading.Tasks.Task ImageExport_Click(int imageIndex, Controls.GbaImageControl previewControl, string suggestedName)
        {
            if (previewControl == null) return;
            if (!previewControl.HasImage)
            {
                Log.Notify($"ImageExport_Click({imageIndex}): no image rendered; cannot export.");
                return;
            }
            try
            {
                await previewControl.ExportPng(this, suggestedName);
            }
            catch (Exception ex)
            {
                Log.Error($"ImageBattleScreenView.ImageExport_Click({imageIndex}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Import a PNG/BMP file as one per-image strip (#872).
        ///
        /// Pipeline:
        ///   1. Open file dialog.
        ///   2. Read current strip dimensions from Core so the user-supplied
        ///      image can be validated (strictSize=true).
        ///   3. Quantize against the existing shared battle-screen palette
        ///      (LoadAndRemapFromFile) -- import NEVER writes the palette.
        ///   4. WritePerImageStrip under one UndoService scope.
        ///   5. Reload the VM entry + refresh all previews so the new tiles
        ///      appear immediately.
        ///   6. On any failure, Rollback the scope and revert the VM state
        ///      (snapshot-restore per the #871 lesson).
        /// </summary>
        internal async System.Threading.Tasks.Task ImageImport_Click(int imageIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null) return;
            if (!_vm.IsLoaded)
            {
                Log.Notify($"ImageImport_Click({imageIndex}): no entry loaded; aborting.");
                return;
            }

            // Resolve expected dimensions from Core (same as the preview render path).
            if (!ImageBattleScreenCore.TryLoadSingleImageStrip(rom, imageIndex, out _, out int widthPx, out int heightPx))
            {
                Log.Notify($"ImageImport_Click({imageIndex}): could not determine strip dimensions; aborting.");
                return;
            }

            // Read the shared battle-screen palette for quantization remapping.
            // Returns null if the palette pointer is corrupt/out-of-bounds.
            if (!ImageBattleScreenCore.TryLoadRawPalettePublic(rom, out byte[] gbaPalette))
            {
                Log.Notify($"ImageImport_Click({imageIndex}): could not read shared palette; aborting.");
                return;
            }

            // Open file dialog and load+quantize (remap to existing palette so
            // the user's image is forced to use the current battle-screen colors).
            string? filePath = await FileDialogHelper.OpenImageFile(this);
            if (string.IsNullOrEmpty(filePath)) return;

            var loadResult = ImageImportService.LoadAndRemapFromFile(
                filePath, widthPx, heightPx, gbaPalette, 16, strictSize: true);
            if (loadResult == null || !loadResult.Success)
            {
                string err = loadResult?.Error ?? "Unknown error";
                Log.Error($"ImageImport_Click({imageIndex}): load/quantize failed: {err}");
                return;
            }

            // Snapshot VM state for rollback on write failure (per the #871 lesson:
            // never leave the UI showing an image that wasn't persisted to ROM).
            uint[] prevPointers = new uint[]
            {
                _vm.Image1Pointer, _vm.Image2Pointer, _vm.Image3Pointer,
                _vm.Image4Pointer, _vm.Image5Pointer,
            };

            _undoService.Begin($"Import Image{imageIndex + 1}");
            bool ok;
            try
            {
                ok = ImageBattleScreenCore.WritePerImageStrip(rom, imageIndex,
                    loadResult.IndexedPixels, widthPx, heightPx);
            }
            catch (Exception ex)
            {
                Log.Error($"ImageImport_Click({imageIndex}): WritePerImageStrip threw: {ex.Message}");
                _undoService.Rollback();
                RestoreVmPointers(prevPointers);
                return;
            }

            if (!ok)
            {
                Log.Notify($"ImageImport_Click({imageIndex}): write failed; rolling back.");
                _undoService.Rollback();
                RestoreVmPointers(prevPointers);
                return;
            }

            _undoService.Commit();

            // Reload so the VM reflects the new pointer written by WritePerImageStrip.
            _vm.LoadEntry();
            PopulateUI();
            RefreshBattlePreview();
            RefreshChipsetPreview();
            RefreshImagePreviews();
        }

        /// <summary>
        /// Restore VM image-pointer properties from a snapshot (snapshot-restore
        /// per the #871 lesson: a failed write must not leave the UI showing an
        /// unpersisted image). Called from the import failure paths.
        /// </summary>
        void RestoreVmPointers(uint[] snapshot)
        {
            if (snapshot == null || snapshot.Length < 5) return;
            _vm.Image1Pointer = snapshot[0];
            _vm.Image2Pointer = snapshot[1];
            _vm.Image3Pointer = snapshot[2];
            _vm.Image4Pointer = snapshot[3];
            _vm.Image5Pointer = snapshot[4];
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
