// SPDX-License-Identifier: GPL-3.0-or-later
// ImageBattleAnimePalletView — Avalonia parity rebuild for #399. Mirrors
// `ImageBattleAnimePalletForm` (panel1: 16 R/G/B + swatches + write +
// zoom + clipboard + import/export + undo/redo). Uses the
// `ImageBattleAnimePaletteCore` helper for the LZ77 decompress / splice /
// recompress / pointer-rewrite write path under the ambient UndoService
// scope.
using System;
using System.Collections.Generic;
using System.Globalization;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBattleAnimePalletView : TranslatedWindow, IEditorView
    {
        readonly ImageBattleAnimePalletViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Battle Animation Palette";
        public bool IsLoaded => _vm.IsLoaded;

        // Cached references to the 48 numeric cells + 16 swatch borders so
        // we don't have to walk the visual tree on every reload.
        readonly NumericUpDown[] _rBoxes = new NumericUpDown[16];
        readonly NumericUpDown[] _gBoxes = new NumericUpDown[16];
        readonly NumericUpDown[] _bBoxes = new NumericUpDown[16];
        readonly Border[] _swatchBoxes = new Border[16];

        bool _suppressSpinnerEvents;

        public ImageBattleAnimePalletView()
        {
            InitializeComponent();

            // Populate combos via R._() so they pick up ja/zh translations
            // (ComboBoxItem.Content is not touched by ViewTranslationHelper —
            // PR #571 Copilot bot review #6 pattern).
            PaletteIndexCombo.Items.Add(R._("Player"));
            PaletteIndexCombo.Items.Add(R._("Enemy"));
            PaletteIndexCombo.Items.Add(R._("Other"));
            PaletteIndexCombo.Items.Add(R._("4th Army"));
            PaletteIndexCombo.SelectedIndex = 0;

            ZoomCombo.Items.Add(R._("Window Size"));
            ZoomCombo.Items.Add(R._("Image Size"));
            ZoomCombo.Items.Add(R._("2x Zoom"));
            ZoomCombo.Items.Add(R._("3x Zoom"));
            ZoomCombo.Items.Add(R._("4x Zoom"));
            ZoomCombo.SelectedIndex = 0;

            CachePaletteCells();
            InitializeSwatches();
            WireSpinnerHandlers();

            PaletteIndexCombo.SelectionChanged += PaletteIndexCombo_SelectionChanged;
            EntryList.SelectedAddressChanged += OnSelectedEntry;

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
            // Default swatches to black (set via code-behind rather than
            // hardcoded XAML to satisfy AvaloniaDarkModeTests).
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
            // Per PR #589 Copilot bot review #1: the VM snaps to 5-bit
            // (multiples of 8). Push the snapped value back into the
            // spinner so the displayed number matches what will be
            // written to ROM. Suppress recursive ValueChanged to avoid
            // a loop.
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

        void PopulateAllSpinnersAndSwatches()
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
                AddressBox.Value = _vm.PaletteAddress;
                SourceSlotLabel.Text = _vm.SourcePointerSlotDisplay;
                Warning32ColorBorder.IsVisible = _vm.WarningVisible;
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
                Log.Error("ImageBattleAnimePalletView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelectedEntry(uint addr)
        {
            try
            {
                // Per PR #589 Copilot bot review #2: pull the matching
                // AddrResult directly from AddressListControl.SelectedItem
                // (which carries both `.addr` and `.tag`). The previous
                // re-walk via _vm.LoadList() was O(N) and could pick the
                // wrong row when two animations share the same palette
                // pointer (the second one's source slot would be lost).
                var selected = EntryList.SelectedItem;
                uint sourceSlot = selected != null ? selected.tag : 0;
                _vm.LoadEntry(addr, sourceSlot, _vm.PaletteTypeIndex);
                PopulateAllSpinnersAndSwatches();
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimePalletView.OnSelectedEntry failed: {0}", ex.Message);
            }
        }

        void PaletteIndexCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                int newIndex = PaletteIndexCombo.SelectedIndex;
                if (newIndex < 0 || newIndex == _vm.PaletteTypeIndex) return;
                _vm.PaletteTypeIndex = newIndex;

                // Reload the current entry to display the newly selected slot.
                if (_vm.PaletteAddress != 0)
                {
                    _vm.LoadEntry(U.toOffset(_vm.PaletteAddress), _vm.SourcePointerSlot, _vm.PaletteTypeIndex);
                    PopulateAllSpinnersAndSwatches();
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimePalletView.PaletteIndexCombo_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void Zoom_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.ZoomIndex = ZoomCombo.SelectedIndex;
            // Sample preview rendering is honestly deferred — no actual
            // bitmap to scale. See SamplePreviewLabel for the deferred note.
        }

        // -----------------------------------------------------------------
        // Write path — wraps the VM Write() in the UndoService scope so
        // ALL `rom.write_*` calls inside the Core helper are tracked, and
        // a failure triggers a true Rollback (per Plan v8 Finding #3).
        // -----------------------------------------------------------------
        void PaletteWrite_Click(object sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (_vm.PaletteAddress == 0)
            {
                Log.Notify("PaletteWrite_Click: no palette loaded.");
                return;
            }

            _undoService.Begin("Edit Battle Anime Palette");
            uint newOffset;
            try
            {
                newOffset = _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimePalletView.Write threw: {0}", ex.Message);
                _undoService.Rollback();
                return;
            }

            if (newOffset == U.NOT_FOUND)
            {
                _undoService.Rollback();
                Log.Notify("PaletteWrite_Click: write failed; rollback applied.");
                return;
            }

            _undoService.Commit();
            // Re-display the (possibly relocated) address.
            AddressBox.Value = _vm.PaletteAddress;
            // Per PR #589 Copilot bot review #5: if the palette block
            // relocated, the master list's cached AddrResult.addr values
            // are now stale (they were the old palette offsets before
            // the rewrite). Reload the list and re-select the row that
            // currently matches the VM's new source pointer slot so
            // subsequent selections load from the new pointers.
            if (newOffset != U.NOT_FOUND)
            {
                RefreshListPreservingSelection();
            }
        }

        void RefreshListPreservingSelection()
        {
            try
            {
                uint preservedSlot = _vm.SourcePointerSlot;
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                if (preservedSlot != 0)
                {
                    // Find the row whose tag matches the (still-stable)
                    // source pointer slot and re-select it.
                    foreach (var ar in items)
                    {
                        if (ar.tag == preservedSlot)
                        {
                            EntryList.SelectAddress(ar.addr);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleAnimePalletView.RefreshListPreservingSelection failed: {0}", ex.Message);
            }
        }

        void Clipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Build "RRGGBB,RRGGBB,..." line of 16 colors and copy to clipboard.
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
                Log.Error("Clipboard_Click failed: {0}", ex.Message);
            }
        }

        void Undo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CoreState.Undo?.RunUndo();
                // Reload the current entry so the UI reflects the rolled-back state.
                if (_vm.PaletteAddress != 0)
                {
                    _vm.LoadEntry(U.toOffset(_vm.PaletteAddress), _vm.SourcePointerSlot, _vm.PaletteTypeIndex);
                    PopulateAllSpinnersAndSwatches();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Undo_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
