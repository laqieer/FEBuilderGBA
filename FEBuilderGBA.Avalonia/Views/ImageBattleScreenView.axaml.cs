// SPDX-License-Identifier: GPL-3.0-or-later
// ImageBattleScreenView -- Avalonia parity rebuild for #393. Mirrors the WF
// `ImageBattleScreenForm` 5-tab battle-screen layout editor. Uses the
// `ImageBattleScreenCore` helper (which delegates palette I/O to PaletteCore)
// for the TSA + palette + image-pointer write paths under the ambient
// UndoService scope.
using System;
using System.Globalization;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
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
                Log.Error("ImageBattleScreenView.LoadList failed: {0}", ex.Message);
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
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleScreenView.OnSelected failed: {0}", ex.Message);
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
                Log.Error("ImageBattleScreenView.RefreshBattlePreview failed: {0}", ex.Message);
                BattlePreview.SetImage(null);
                _vm.CanExportBattle = false;
            }
            if (BattleExportPngButton != null)
            {
                BattleExportPngButton.IsEnabled = _vm.CanExportBattle;
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
                Log.Error("ImageBattleScreenView.BattleExportPng failed: {0}", ex.Message);
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
                Log.Error("ImageBattleScreenView.RefreshChipsetPreview failed: {0}", ex.Message);
                ChipsetPreview.SetImage(null);
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
                Log.Error("ImageBattleScreenView.Zoom_SelectionChanged failed: {0}", ex.Message);
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
                Log.Error("ImageBattleScreenView.Write threw: {0}", ex.Message);
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
                Log.Error("ImageBattleScreenView.WritePalette threw: {0}", ex.Message);
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
                Log.Error("ImageBattleScreenView.PaletteIndex_SelectionChanged failed: {0}", ex.Message);
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
                Log.Error("PaletteClipboard_Click failed: {0}", ex.Message);
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
            }
            catch (Exception ex)
            {
                Log.Error("PaletteUndo_Click failed: {0}", ex.Message);
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
            }
            catch (Exception ex)
            {
                Log.Error("BulkUndo_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
