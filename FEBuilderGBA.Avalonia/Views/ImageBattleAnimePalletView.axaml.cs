// SPDX-License-Identifier: GPL-3.0-or-later
// ImageBattleAnimePalletView — Avalonia parity rebuild for #399. Mirrors
// `ImageBattleAnimePalletForm` (panel1: 16 R/G/B + swatches + write +
// zoom + clipboard + import/export + undo/redo). Uses the
// `ImageBattleAnimePaletteCore` helper for the LZ77 decompress / splice /
// recompress / pointer-rewrite write path under the ambient UndoService
// scope.
using global::Avalonia;
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
    public partial class ImageBattleAnimePalletView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ImageBattleAnimePalletViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Battle Animation Palette";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Battle Animation Palette", 1280, 780, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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
                Log.ErrorF("ImageBattleAnimePalletView.LoadList failed: {0}", ex.Message);
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
                RefreshSamplePreview();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleAnimePalletView.OnSelectedEntry failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Re-render the battle-animation sample-preview grid for the current
        /// entry + active palette type and push it into the GbaImageControl.
        /// Mirrors WF DrawSample being re-invoked on load + on PaletteIndex
        /// change. Null-safe: a null render clears the preview (SetImage(null)).
        /// </summary>
        void RefreshSamplePreview()
        {
            try
            {
                // IImage is IDisposable (Skia-backed). GbaImageControl.SetImage
                // (via IconBitmapBuilder.FromImage) copies the pixels into an
                // independent WriteableBitmap synchronously and does NOT take
                // ownership of the IImage, so dispose the freshly-rendered grid
                // AFTER SetImage has copied it. The `using` also covers the null
                // case (SetImage(null) clears the preview).
                using IImage grid = _vm.RenderSampleBattleAnime();
                SamplePreview.SetImage(grid);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleAnimePalletView.RefreshSamplePreview failed: {0}", ex.Message);
                SamplePreview.SetImage(null);
            }
            // #828: gate the Export Image button on a successful render.
            // HasImage is true only when SetImage received a non-null IImage
            // (a no-resolvable-anime / blank record clears the preview). Mirrors
            // ImageBattleScreenView's `BattleExportPngButton.IsEnabled =
            // _vm.CanExportBattle`. ExportPng is null-safe regardless, but this
            // makes the disabled state visible instead of a silent no-op.
            if (ExportButton != null)
            {
                ExportButton.IsEnabled = SamplePreview.HasImage;
            }
        }

        /// <summary>
        /// Export the rendered battle-animation sample grid to a PNG file via a
        /// non-modal save dialog. #828: reuses <c>GbaImageControl.ExportPng</c>
        /// — the identical read-only PNG primitive the merged battle-screen
        /// (#810) and TSA (#810/#815) editors use. Read-only — no ROM write.
        /// Enabled only when a render succeeded (<see cref="RefreshSamplePreview"/>
        /// set <c>HasImage</c>); <c>ExportPng</c> early-returns on a null bitmap.
        /// </summary>
        async void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SamplePreview.ExportPng(TopLevel.GetTopLevel(this) as Window, ExportSuggestedName());
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleAnimePalletView.Export_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Build a sensible suggested PNG filename for the current animation
        /// sample, e.g. <c>anime_sample_03</c> for the row whose list index is
        /// 3. The list index is the WF battle-animation id (the VM names rows
        /// "{i:X2} BattleAnime"). Falls back to <c>anime_sample</c> when no row
        /// is selected.
        /// </summary>
        string ExportSuggestedName()
        {
            int idx = EntryList.SelectedOriginalIndex;
            return idx >= 0 ? $"anime_sample_{idx:X2}" : "anime_sample";
        }

        void PaletteIndexCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                int newIndex = PaletteIndexCombo.SelectedIndex;
                if (newIndex < 0 || newIndex == _vm.PaletteTypeIndex) return;
                _vm.PaletteTypeIndex = newIndex;

                // Per PR #589 Copilot bot review round 3 #4: reload via
                // the AUTHORITATIVE back-pointer slot (rom.p32(SourcePointerSlot))
                // instead of the VM's cached _vm.PaletteAddress, which can be
                // stale after a ROM-level Undo that restored the old pointer
                // in the source slot without flowing back to the VM.
                ReloadFromAuthoritativeSlot();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleAnimePalletView.PaletteIndexCombo_SelectionChanged failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Reload the palette using the back-pointer slot as the authoritative
        /// source of truth (rom.p32(SourcePointerSlot)). The VM's cached
        /// _vm.PaletteAddress can diverge from the on-ROM pointer when
        /// external operations (ROM-level Undo, Redo, third-party edits)
        /// rewrite the slot without flowing the change back to the VM.
        /// Per PR #589 Copilot bot review round 3 #4 + #5.
        /// </summary>
        void ReloadFromAuthoritativeSlot()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            uint slot = _vm.SourcePointerSlot;
            if (slot == 0)
            {
                // No back-pointer slot known -- fall back to the cached
                // VM offset (initial-load case before any selection).
                if (_vm.PaletteAddress != 0)
                {
                    _vm.LoadEntry(U.toOffset(_vm.PaletteAddress), 0, _vm.PaletteTypeIndex);
                    PopulateAllSpinnersAndSwatches();
                    RefreshSamplePreview();
                }
                return;
            }
            // Re-resolve the palette offset from the source pointer slot.
            uint authoritativePaletteOffset = rom.p32(slot);
            if (authoritativePaletteOffset == 0) return;
            _vm.LoadEntry(authoritativePaletteOffset, slot, _vm.PaletteTypeIndex);
            PopulateAllSpinnersAndSwatches();
            // Re-render the sample preview so a palette-type (PaletteIndex)
            // change immediately re-tints the grid with the new sub-palette
            // (the cross-platform equivalent of WF DrawSample being re-invoked
            // from PaletteIndexComboBox_SelectedIndexChanged → DrawSample).
            RefreshSamplePreview();
        }

        void Zoom_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.ZoomIndex = ZoomCombo.SelectedIndex;
            // The sample preview now renders (#822). It is hosted in a
            // ScrollViewer at native size; zoom-scaling the GbaImageControl
            // is not part of the Phase-1 parity render (WF's zoom combo only
            // scales the editor's own X_PIC, which is a separate follow-up).
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
                Log.ErrorF("ImageBattleAnimePalletView.Write threw: {0}", ex.Message);
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
                    // Per PR #589 Copilot bot review round 2: select by
                    // INDEX rather than by palette address, because two
                    // battle-animation rows can legitimately share the
                    // same palette pointer (the bug the stable
                    // source-pointer-slot tag was meant to fix).
                    // SelectByIndex selects exactly the matched row.
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].tag == preservedSlot)
                        {
                            EntryList.SelectByIndex(i);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageBattleAnimePalletView.RefreshListPreservingSelection failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Import path — mirrors WF PaletteFormRef.MakePaletteBitmapToUIEx
        // (file-picker → quantize → load palette into VM → write to ROM).
        // No dependency on DrawBattleAnime — the WF tooltip was wrong.
        // -----------------------------------------------------------------

        /// <summary>
        /// Open a file picker, load and quantize the chosen image to 16 GBA
        /// RGB555 colors, apply them to the VM, then write to ROM under a
        /// single <see cref="UndoService"/> scope. Mirrors WinForms
        /// <c>ImportButton_Click</c>: PaletteFormRef.MakePaletteBitmapToUIEx
        /// (file open → palette extract) + PaletteWriteButton.PerformClick().
        /// </summary>
        async void Import_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.PaletteAddress == 0)
            {
                Log.Notify("Import_Click: no palette loaded.");
                return;
            }

            // Open file dialog — mirrors WF ImageFormRef.OpenFilenameDialogFullColor.
            string filePath = await FileDialogHelper.OpenImageFile(TopLevel.GetTopLevel(this));
            if (string.IsNullOrEmpty(filePath))
            {
                return; // User cancelled.
            }

            DoImportFromFile(filePath);
        }

        /// <summary>
        /// Load, quantize, apply, and write the palette from
        /// <paramref name="filePath"/>. Extracted so tests can inject a path
        /// without a file dialog (injectable seam — calls <see cref="_vm.DoImport"/>
        /// which is the VM-level seam).
        /// </summary>
        internal void DoImportFromFile(string filePath)
        {
            // Quantize to 16 GBA colors — no dimension validation needed;
            // only the palette matters for this editor.
            // Use a 1x1 dummy size to skip the "must be multiples of 8" guard
            // by passing 0 for expected dimensions (ImageImportService skips
            // the strict-size check when strictSize=false and expectedWidth/Height=0).
            var loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath,
                expectedWidth: 0, expectedHeight: 0,
                maxColors: ImageBattleAnimePaletteCore.ColorsPerSlot,
                strictSize: false,
                requireTileMultiple: false); // FIX 1 (#871): palette-only accepts any image size

            if (loadResult == null || !loadResult.Success)
            {
                string err = loadResult?.Error ?? "Unknown error";
                Log.ErrorF("Import_Click: image load/quantize failed: {0}", err);
                return;
            }

            // Pad to exactly 32 bytes if fewer than 16 colors quantized.
            byte[] gbaPalette = PadGBAPaletteTo16(loadResult.GBAPalette);

            // FIX 2 (#871): snapshot VM palette BEFORE DoImport so we can
            // restore the VM/UI if the ROM write later fails or returns
            // U.NOT_FOUND. Without this, the ROM is rolled back by
            // UndoService but the VM still shows the imported (unpersisted)
            // palette -- an inconsistent UI state.
            byte[] rSnap = new byte[16], gSnap = new byte[16], bSnap = new byte[16];
            for (int si = 0; si < 16; si++)
            {
                rSnap[si] = _vm.GetR(si);
                gSnap[si] = _vm.GetG(si);
                bSnap[si] = _vm.GetB(si);
            }

            bool applied = _vm.DoImport(gbaPalette);
            if (!applied)
            {
                Log.Error("Import_Click: DoImport rejected palette bytes (null or < 32 bytes)");
                return;
            }

            // Sync spinners + swatches to the new VM colors before writing.
            PopulateAllSpinnersAndSwatches();

            // Write to ROM under a single undo scope — same flow as PaletteWrite_Click.
            _undoService.Begin("Import Battle Anime Palette");
            uint newOffset;
            try
            {
                newOffset = _vm.Write();
            }
            catch (Exception ex)
            {
                Log.ErrorF("Import_Click: Write threw: {0}", ex.Message);
                _undoService.Rollback();
                // FIX 2: restore pre-import VM state so the UI matches the rolled-back ROM.
                RestorePaletteSnapshot(rSnap, gSnap, bSnap);
                return;
            }

            if (newOffset == U.NOT_FOUND)
            {
                _undoService.Rollback();
                Log.Notify("Import_Click: write failed; rollback applied.");
                // FIX 2: restore pre-import VM state so the UI matches the rolled-back ROM.
                RestorePaletteSnapshot(rSnap, gSnap, bSnap);
                return;
            }

            _undoService.Commit();
            AddressBox.Value = _vm.PaletteAddress;
            if (newOffset != U.NOT_FOUND)
            {
                RefreshListPreservingSelection();
            }
        }

        /// <summary>
        /// Restore the VM R/G/B arrays from a pre-import snapshot and refresh
        /// the spinners/swatches. Called when a ROM write fails so the UI stays
        /// in sync with the rolled-back ROM state. FIX 2 (#871).
        /// </summary>
        void RestorePaletteSnapshot(byte[] rSnap, byte[] gSnap, byte[] bSnap)
        {
            for (int i = 0; i < 16; i++)
            {
                _vm.SetR(i, rSnap[i]);
                _vm.SetG(i, gSnap[i]);
                _vm.SetB(i, bSnap[i]);
            }
            PopulateAllSpinnersAndSwatches();
        }

        /// <summary>
        /// Pad <em>or truncate</em> a GBA palette byte array to exactly
        /// <see cref="ImageBattleAnimePaletteCore.SlotByteSize"/> (32) bytes.
        /// Required because <see cref="DecreaseColorCore.Quantize"/> may
        /// return fewer than 16 colors when the source image has few
        /// distinct hues; extra colors beyond 16 are dropped. FIX 3 (#871).
        /// </summary>
        static byte[] PadGBAPaletteTo16(byte[] gbaPalette)
        {
            int needed = ImageBattleAnimePaletteCore.SlotByteSize; // 32
            // FIX 3 (#871): always copy into a fresh 32-byte buffer (truncate when longer,
            // zero-fill when shorter or null). Prior impl returned array unchanged >= 32 bytes.
            byte[] padded = new byte[needed];
            if (gbaPalette != null)
            {
                System.Array.Copy(gbaPalette, padded, Math.Min(gbaPalette.Length, needed));
            }
            return padded;
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
                Log.ErrorF("Clipboard_Click failed: {0}", ex.Message);
            }
        }

        void Undo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CoreState.Undo?.RunUndo();
                // Per PR #589 Copilot bot review round 3 #5: reload via
                // the AUTHORITATIVE back-pointer slot. After undo, the
                // source slot in ROM has been restored to the old palette
                // pointer; the VM's cached _vm.PaletteAddress is stale
                // (still points to the post-relocate value).
                ReloadFromAuthoritativeSlot();
                // Also refresh the master list so any post-undo address
                // shifts are reflected in the entry list (a relocate may
                // have been reversed, so the listed addr changes back).
                RefreshListPreservingSelection();
            }
            catch (Exception ex)
            {
                Log.ErrorF("Undo_Click failed: {0}", ex.Message);
            }
        }

        void Redo_Click(object sender, RoutedEventArgs e)
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
                // Reload via the authoritative back-pointer slot (mirrors Undo_Click).
                ReloadFromAuthoritativeSlot();
                // Refresh the entry list so any post-redo address shifts are reflected.
                RefreshListPreservingSelection();
            }
            catch (Exception ex)
            {
                Log.ErrorF("Redo_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
