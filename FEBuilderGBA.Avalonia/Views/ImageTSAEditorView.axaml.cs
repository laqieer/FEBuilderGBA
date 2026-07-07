// SPDX-License-Identifier: GPL-3.0-or-later
// ImageTSAEditorView code-behind — Avalonia parity raise (gap-sweep #398).
//
// The view exposes the WinForms Init(...) entry point so future callers
// (TSAEditor button on a future GraphicsToolView equivalent) can wire up
// width/height + pointers. Until Init is invoked, Write / PaletteWrite are
// IsEnabled=False (bound to _vm.IsContextLoaded) so the default standalone
// open path stays harmless.
//
// All ROM-write handlers (Write / PaletteWrite) go through
// _undoService.Begin/Commit/Rollback. The Write button persists BOTH the edited
// per-cell TSA (non-header; _vm.WriteTsa -> ImageTSAEditorCore.WriteTsaCells) and
// the palette under ONE undo scope (#1005). The MainImageImport / MainImageExport
// buttons are fully wired (#901/#974) and gated on IsContextLoaded; the Clipboard
// button is wired and always enabled (read-only — copies the grid to the system
// clipboard, no ROM context needed). None are IsEnabled=False stubs. Header-TSA
// per-cell editing is RESOLVED (#1071): the cell panel enables for a valid header
// decode, the Cell X/Y maxima are constrained to the header region, and Write
// branches to _vm.WriteTsa -> ImageTSAEditorCore.WriteHeaderTsaCells.
using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input.Platform;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageTSAEditorView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ImageTSAEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        // Suppresses the Cell X / Cell Y ValueChanged -> field-load reentrancy
        // while we programmatically push a cell's values into the editor fields
        // (#1005). Without this, seeding the fields would re-trigger the loader.
        bool _suppressCellLoad;

        public string ViewTitle => "TSA Tile Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("TSA Tile Editor", 1411, 938, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ImageTSAEditorView()
        {
            InitializeComponent();

            // IsEnabled bindings (`IsEnabled="{Binding IsContextLoaded}"`)
            // need a DataContext that points at the ViewModel. The view
            // itself is the binding source target, but the bindings refer
            // to ViewModel properties — so DataContext = _vm.
            DataContext = _vm;

            // Populate the Zoom combo through R._() so the items are localised
            // via the Core translation facade (ViewTranslationHelper does not
            // walk ComboBoxItem.Content — Copilot review feedback).
            string[] zoomKeys = { "No Zoom", "2x Zoom", "3x Zoom", "4x Zoom" };
            foreach (string key in zoomKeys)
            {
                ZoomCombo.Items.Add(new ComboBoxItem { Content = R._(key) });
            }

            EntryList.SelectedAddressChanged += OnSelected;
            // Selecting a different palette slot must repopulate the R/G/B
            // grid with that slot's existing ROM bytes so the user sees the
            // real palette before clicking Write/Palette Write (otherwise a
            // user could accidentally overwrite a slot with default zeros).
            PaletteIndexCombo.SelectionChanged += (_, _) => ReloadPaletteIntoGrid();
            // Same for changes to the Palette Address spinner — the spinner
            // is the WF-equivalent of the editable PaletteAddress field.
            PaletteAddressBox.ValueChanged += (_, _) => ReloadPaletteIntoGrid();
            // TSA Cell selectors (#1005): changing Cell X / Cell Y loads that
            // cell's current values into the Tile ID / flip / bank fields. The
            // _suppressCellLoad guard keeps the programmatic field-seed from
            // re-triggering this handler.
            TsaCellXBox.ValueChanged += (_, _) => LoadSelectedCellIntoFields();
            TsaCellYBox.ValueChanged += (_, _) => LoadSelectedCellIntoFields();
            // Redo is now wired to the global Core Undo stack (#974):
            // CoreState.Undo.RunRedo() + CanRedo already exist (the Map Style
            // editor's Redo_Click uses the same pattern). The buttons stay
            // enabled and Redo_Click guards on CanRedo at runtime.
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

        /// <summary>
        /// Caller-facing entry point that mirrors WinForms
        /// ImageTSAEditorForm.Init. After Init returns, IsContextLoaded
        /// is true and Write / PaletteWrite become enabled.
        ///
        /// This is the surface a future Avalonia caller (TSAEditor button
        /// on a GraphicsToolView equivalent) would invoke after constructing
        /// the view, mirroring the WinForms InputFormRef.JumpForm + f.Init
        /// pattern at ImageFormRef.cs:1001 and GraphicsToolForm.cs:980.
        /// </summary>
        public void Init(uint width8,
                         uint height8,
                         uint zimgPointer,
                         bool isHeaderTSA,
                         bool isLZ77TSA,
                         uint tsaPointer,
                         uint palettePointer,
                         uint paletteAddress,
                         int paletteCount)
        {
            _vm.Init(width8, height8, zimgPointer, isHeaderTSA, isLZ77TSA,
                     tsaPointer, palettePointer, paletteAddress, paletteCount);

            // Clamp the palette-index combo to the caller-supplied paletteCount
            // so the user cannot select an out-of-range slot and accidentally
            // write into an unintended palette block. Mirrors WF behavior
            // where PaletteCount is the explicit upper bound.
            ClampPaletteIndexComboToCount(paletteCount);

            // Seed the palette address spinner from the resolved context
            // so the Palette tab is ready for the first read.
            try
            {
                uint paletteResolved = _vm.ResolveActivePaletteAddress();
                PaletteAddressBox.Value = paletteResolved;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageTSAEditorView.Init failed: {0}", ex.Message);
            }

            // Populate the 16x3 R/G/B grid from the existing ROM palette so
            // the user sees current ROM data before clicking Write / Palette
            // Write — prevents accidentally overwriting a slot with zeros.
            ReloadPaletteIntoGrid();

            UpdateInfoLabel();
            RefreshBattleCanvas();
            RefreshChipList();

            // Configure the TSA Cell editor (#1005). The panel auto-disables via
            // CanEditCells for header-TSA; for non-header TSA we set the X/Y/Tile
            // ID ranges and seed the fields from cell (0,0).
            ConfigureTsaCellEditor();
        }

        /// <summary>
        /// Set the TSA Cell editor's X/Y/Tile ID NumericUpDown ranges from the
        /// decoded cell grid and seed the fields from cell (0,0) (#1005/#1071).
        /// No-op of the seed when there are no editable cells (corrupt TSA / a
        /// corrupt header), in which case the panel is already disabled via
        /// CanEditCells.
        ///
        /// For HEADER-TSA the X/Y maxima are constrained to the header region
        /// (<see cref="ImageTSAEditorViewModel.HeaderMaxX"/> /
        /// <see cref="ImageTSAEditorViewModel.HeaderMaxY"/>) so cells OUTSIDE the
        /// header region of the min-clamped canvas are non-selectable — they
        /// are visibly unreachable, not silently-ignored edits (#1071). For
        /// non-header TSA HeaderMaxX/Y are CellCols-1 / CellRows-1, so the whole
        /// row-major grid stays editable exactly as in #1005.
        /// </summary>
        void ConfigureTsaCellEditor()
        {
            if (!_vm.CanEditCells) return;

            _suppressCellLoad = true;
            try
            {
                TsaCellXBox.Minimum = 0;
                TsaCellXBox.Maximum = Math.Max(0, _vm.HeaderMaxX);
                TsaCellXBox.Value = 0;

                TsaCellYBox.Minimum = 0;
                TsaCellYBox.Maximum = Math.Max(0, _vm.HeaderMaxY);
                TsaCellYBox.Value = 0;

                TsaCellTileIdBox.Minimum = 0;
                TsaCellTileIdBox.Maximum = Math.Max(0, _vm.MaxTileId);
            }
            finally { _suppressCellLoad = false; }

            // Seed the Tile ID / flip / bank fields from cell (0,0).
            LoadSelectedCellIntoFields();
        }

        /// <summary>
        /// Decode the cell at the current (Cell X, Cell Y) selection and push
        /// its tile id / H-flip / V-flip / palette bank into the editor fields
        /// (#1005). Guarded by <c>_suppressCellLoad</c> so the programmatic
        /// field-seed cannot re-enter via the X/Y ValueChanged handlers.
        /// </summary>
        void LoadSelectedCellIntoFields()
        {
            if (_suppressCellLoad) return;
            if (!_vm.CanEditCells) return;

            int x = (int)(TsaCellXBox.Value ?? 0m);
            int y = (int)(TsaCellYBox.Value ?? 0m);
            ushort entry = _vm.GetCell(x, y);

            int tileId = entry & 0x3FF;
            bool hflip = (entry & 0x400) != 0;
            bool vflip = (entry & 0x800) != 0;
            int bank = (entry >> 12) & 0xF;

            _suppressCellLoad = true;
            try
            {
                TsaCellTileIdBox.Value = Math.Min(tileId, _vm.MaxTileId);
                TsaCellHFlipCheck.IsChecked = hflip;
                TsaCellVFlipCheck.IsChecked = vflip;
                TsaCellBankBox.Value = bank;
            }
            finally { _suppressCellLoad = false; }
        }

        /// <summary>
        /// "Apply to Cell" button (#1005/#1071). Bit-packs the editor fields into
        /// the selected cell via <see cref="ImageTSAEditorViewModel.SetCell"/>
        /// then re-renders the BattlePreview so the canvas reflects the in-memory
        /// edit immediately (the ROM is written only on the Write button).
        ///
        /// For HEADER-TSA an explicit
        /// <see cref="ImageTSAEditorViewModel.IsCellEditable"/> guard rejects an
        /// out-of-header cell (defense in depth — the X/Y maxima already prevent
        /// selecting one), so an out-of-header cell can never be edited (#1071).
        /// </summary>
        void TsaCellApply_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanEditCells) return;

            int x = (int)(TsaCellXBox.Value ?? 0m);
            int y = (int)(TsaCellYBox.Value ?? 0m);
            // Out-of-header cells are display-only; SetCell already no-ops them,
            // but bail early so the preview is not needlessly re-rendered.
            if (!_vm.IsCellEditable(x, y)) return;

            int tileId = (int)(TsaCellTileIdBox.Value ?? 0m);
            bool hflip = TsaCellHFlipCheck.IsChecked == true;
            bool vflip = TsaCellVFlipCheck.IsChecked == true;
            int bank = (int)(TsaCellBankBox.Value ?? 0m);

            _vm.SetCell(x, y, tileId, hflip, vflip, bank);

            // Re-render the TSA-composited canvas from the in-memory cells.
            BattlePreview.SetImage(_vm.RenderMainImage());
        }

        /// <summary>
        /// Rebuild PaletteIndexCombo to expose only the slots the caller
        /// declared via <c>paletteCount</c>. The AXAML stub has all 16
        /// items hard-wired so the View_HasTopToolbar audit can find the
        /// AutomationId on first inspection; Init then trims down so the
        /// runtime user sees only the in-range slots.
        ///
        /// When paletteCount is &lt;=0 (default / standalone open path), we
        /// leave the original 0..F list intact — the AXAML-baked items —
        /// because Write/PaletteWrite is gated on IsContextLoaded anyway.
        /// </summary>
        void ClampPaletteIndexComboToCount(int paletteCount)
        {
            if (paletteCount <= 0) return;
            int max = Math.Min(paletteCount, 16);
            string[] hexLabels = { "0", "1", "2", "3", "4", "5", "6", "7",
                                   "8", "9", "A", "B", "C", "D", "E", "F" };
            PaletteIndexCombo.Items.Clear();
            for (int i = 0; i < max; i++)
            {
                PaletteIndexCombo.Items.Add(new ComboBoxItem { Content = hexLabels[i] });
            }
            PaletteIndexCombo.SelectedIndex = 0;
        }

        /// <summary>
        /// Read the currently-selected palette slot from ROM and push the
        /// 16 RGB triplets into the AXAML NumericUpDown grid. Invoked from
        /// Init and from the PaletteIndexCombo SelectionChanged handler so
        /// the grid never lingers at default zeros (Copilot review feedback).
        /// </summary>
        void ReloadPaletteIntoGrid()
        {
            try
            {
                if (!_vm.IsContextLoaded) return;
                if (CoreState.ROM == null) return;

                uint paletteAddr = (uint)(PaletteAddressBox.Value ?? 0m);
                int paletteIndex = Math.Max(0, PaletteIndexCombo.SelectedIndex);

                if (!_vm.TryLoadPalette(paletteAddr, paletteIndex, out var rgb))
                {
                    // Out-of-range read — leave the grid showing zeros and
                    // surface a non-blocking note. The user can adjust the
                    // address or palette index without crashing.
                    Log.Notify($"TSA Editor: palette range out of bounds at " +
                               $"addr=0x{paletteAddr:X}, idx={paletteIndex}; grid reset to zeros.");
                }

                WritePaletteToUI(rgb);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageTSAEditorView.ReloadPaletteIntoGrid failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Push 16 RGB triplets into the AXAML NumericUpDown grid (the
        /// inverse of <see cref="ReadPaletteFromUI"/>).
        /// </summary>
        void WritePaletteToUI((byte R, byte G, byte B)[] rgb)
        {
            if (rgb == null || rgb.Length != 16) return;
            NumericUpDown[] rs = { PaletteR1, PaletteR2, PaletteR3, PaletteR4,
                                   PaletteR5, PaletteR6, PaletteR7, PaletteR8,
                                   PaletteR9, PaletteR10, PaletteR11, PaletteR12,
                                   PaletteR13, PaletteR14, PaletteR15, PaletteR16 };
            NumericUpDown[] gs = { PaletteG1, PaletteG2, PaletteG3, PaletteG4,
                                   PaletteG5, PaletteG6, PaletteG7, PaletteG8,
                                   PaletteG9, PaletteG10, PaletteG11, PaletteG12,
                                   PaletteG13, PaletteG14, PaletteG15, PaletteG16 };
            NumericUpDown[] bs = { PaletteB1, PaletteB2, PaletteB3, PaletteB4,
                                   PaletteB5, PaletteB6, PaletteB7, PaletteB8,
                                   PaletteB9, PaletteB10, PaletteB11, PaletteB12,
                                   PaletteB13, PaletteB14, PaletteB15, PaletteB16 };
            for (int i = 0; i < 16; i++)
            {
                rs[i].Value = rgb[i].R;
                gs[i].Value = rgb[i].G;
                bs[i].Value = rgb[i].B;
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
                Log.ErrorF("ImageTSAEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateInfoLabel();
                RefreshBattleCanvas();
                RefreshChipList();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageTSAEditorView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateInfoLabel()
        {
            if (_vm.IsContextLoaded)
            {
                InfoBox.Text = string.Format("{0}x{1}",
                    _vm.Width8 * 8, _vm.Height8 * 8);
            }
            else
            {
                InfoBox.Text = string.Empty;
            }
        }

        /// <summary>
        /// Render the TSA-composited main image (#808) into the BattlePreview
        /// control and gate the Export PNG button. On a successful render the
        /// image is shown and <c>CanExportBattle</c> is set true; on any failure
        /// (no context, unset pointers, corrupt data) the preview is cleared and
        /// the Export button stays disabled. Never throws.
        /// </summary>
        void RefreshBattleCanvas()
        {
            try
            {
                IImage img = _vm.RenderMainImage();
                BattlePreview.SetImage(img);
                _vm.CanExportBattle = img != null;
            }
            catch (Exception ex)
            {
                BattlePreview.SetImage(null);
                _vm.CanExportBattle = false;
                Log.ErrorF("ImageTSAEditorView.RefreshBattleCanvas failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Render the chip-list thumbnail (#819) into the ChipListPreview
        /// control — the main-image tiles in WF MakeCHIPLIST's 4-column
        /// (orig / Hflip / Vflip / HVflip) single-bank-0 strip. Mirrors
        /// <see cref="RefreshBattleCanvas"/>: on a successful render the image
        /// is shown; on any failure (no context, unset pointers, corrupt data)
        /// the preview is cleared (blank). Never throws. Invoked on entry-load
        /// (Init / OnSelected) and after a successful palette write so the
        /// thumbnail tracks palette-color changes.
        /// </summary>
        void RefreshChipList()
        {
            try
            {
                IImage img = _vm.RenderChipList();
                ChipListPreview.SetImage(img);
            }
            catch (Exception ex)
            {
                ChipListPreview.SetImage(null);
                Log.ErrorF("ImageTSAEditorView.RefreshChipList failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        // -----------------------------------------------------------------
        // Click handlers — Write / PaletteWrite go through UndoService.
        // -----------------------------------------------------------------

        /// <summary>
        /// Main Write button (top toolbar). In WinForms this writes TSA +
        /// palette in one shot via ImageFormRef.WriteImageData. We now write
        /// BOTH under ONE undo scope (#1005/#1071): the edited TSA cells (when
        /// CanEditCells — non-header row-major #1005 OR header 32-wide stride
        /// #1071, routed inside _vm.WriteTsa) followed by the active palette. A
        /// failed TSA write rolls the whole transaction back so neither half
        /// persists. When no editable cell grid decoded (e.g. a corrupt header),
        /// CanEditCells is false and only the palette is written. Disabled until
        /// Init().
        /// </summary>
        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsContextLoaded) return;

            _undoService.Begin("Write TSA");
            try
            {
                // 1. TSA cells (non-header #1005 OR header #1071 — _vm.WriteTsa
                //    routes by IsHeaderCells). On a non-empty error string, roll
                //    back the whole scope before the palette half runs.
                if (_vm.CanEditCells)
                {
                    string err = _vm.WriteTsa();
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                }

                // 2. Palette half (existing). Owns no undo scope of its own —
                //    the outer scope above is the single transaction.
                PerformPaletteWrite();
                _undoService.Commit();
                _vm.MarkClean();
                // The TSA / palette changed -> re-render both previews so they
                // track the persisted bytes.
                RefreshBattleCanvas();
                RefreshChipList();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError(R._("TSA Write failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Palette Write button (Palette tab). Same as Write_Click but
        /// scoped to the palette tab's caption.
        /// </summary>
        void PaletteWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsContextLoaded) return;

            _undoService.Begin("TSA Editor Palette Write");
            try
            {
                PerformPaletteWrite();
                _undoService.Commit();
                _vm.MarkClean();
                // A palette write changes the rendered colors -> refresh the
                // read-only chip-list thumbnail so it tracks the new palette.
                RefreshChipList();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError(R._("Palette Write failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Shared core of Write_Click / PaletteWrite_Click. Pulls the 16
        /// RGB triplets from the AXAML NumericUpDown grid, packs 5-5-5,
        /// and writes 16 ushorts to (paletteAddr + paletteIndex*0x20).
        /// </summary>
        void PerformPaletteWrite()
        {
            uint paletteAddr = (uint)(PaletteAddressBox.Value ?? 0m);
            int paletteIndex = Math.Max(0, PaletteIndexCombo.SelectedIndex);
            var rgb = ReadPaletteFromUI();
            _vm.WritePalette(paletteAddr, paletteIndex, rgb);
        }

        /// <summary>
        /// Collect the 16 R/G/B NumericUpDown values from the AXAML grid
        /// into a 16-entry tuple array suitable for WritePalette.
        /// </summary>
        (byte R, byte G, byte B)[] ReadPaletteFromUI()
        {
            var rgb = new (byte R, byte G, byte B)[16];
            // Unrolled lookup — the AXAML grid declares 16 named pairs.
            NumericUpDown[] rs = { PaletteR1, PaletteR2, PaletteR3, PaletteR4,
                                   PaletteR5, PaletteR6, PaletteR7, PaletteR8,
                                   PaletteR9, PaletteR10, PaletteR11, PaletteR12,
                                   PaletteR13, PaletteR14, PaletteR15, PaletteR16 };
            NumericUpDown[] gs = { PaletteG1, PaletteG2, PaletteG3, PaletteG4,
                                   PaletteG5, PaletteG6, PaletteG7, PaletteG8,
                                   PaletteG9, PaletteG10, PaletteG11, PaletteG12,
                                   PaletteG13, PaletteG14, PaletteG15, PaletteG16 };
            NumericUpDown[] bs = { PaletteB1, PaletteB2, PaletteB3, PaletteB4,
                                   PaletteB5, PaletteB6, PaletteB7, PaletteB8,
                                   PaletteB9, PaletteB10, PaletteB11, PaletteB12,
                                   PaletteB13, PaletteB14, PaletteB15, PaletteB16 };
            for (int i = 0; i < 16; i++)
            {
                rgb[i] = (
                    (byte)Math.Clamp((int)(rs[i].Value ?? 0m), 0, 255),
                    (byte)Math.Clamp((int)(gs[i].Value ?? 0m), 0, 255),
                    (byte)Math.Clamp((int)(bs[i].Value ?? 0m), 0, 255)
                );
            }
            return rgb;
        }

        // -----------------------------------------------------------------
        // Undo / Redo (top toolbar) — global Core.Undo navigation.
        // -----------------------------------------------------------------

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo != null)
                {
                    CoreState.Undo.RunUndo();
                    // RunUndo reverts ROM bytes (e.g. an undone palette write),
                    // so reload the affected state and re-render the previews --
                    // otherwise the spinners + ChipListPreview / BattlePreview
                    // keep showing the pre-undo colors (mirrors how
                    // ImageBattleScreenView refreshes after undo). Null-safe.
                    ReloadPaletteIntoGrid();
                    RefreshBattleCanvas();
                    RefreshChipList();
                }
            }
            catch (Exception ex)
            {
                CoreState.Services.ShowError(R._("Undo failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Redo button (#974). Runs <see cref="Undo.RunRedo"/> on the global
        /// <see cref="CoreState.Undo"/> stack — the SAME pattern as the Map
        /// Style editor's Redo_Click. Mirrors <see cref="Undo_Click"/>: guards
        /// on <see cref="Undo.CanRedo"/>, verifies the redo actually advanced
        /// (RunRedo's bool surfaces silent rollback failures), then reloads the
        /// palette grid + re-renders the read-only previews so the spinners /
        /// ChipListPreview / BattlePreview track the rolled-forward ROM bytes.
        /// </summary>
        void Redo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo == null || !CoreState.Undo.CanRedo)
                {
                    CoreState.Services.ShowInfo(R._("Nothing to redo."));
                    return;
                }
                if (!CoreState.Undo.RunRedo())
                {
                    CoreState.Services.ShowError(R._("Redo failed."));
                    return;
                }
                // RunRedo rolls ROM bytes forward (e.g. a redone palette write),
                // so reload the affected state and re-render the previews --
                // otherwise the spinners + ChipListPreview / BattlePreview keep
                // showing the pre-redo colors (mirrors Undo_Click).
                ReloadPaletteIntoGrid();
                RefreshBattleCanvas();
                RefreshChipList();
            }
            catch (Exception ex)
            {
                CoreState.Services.ShowError(R._("Redo failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Palette tab UNDO sub-button. Mirrors WF UndoPlaetteButton_Click
        /// which calls PFR.RunUndo (the palette-only undo buffer the WF
        /// PaletteFormRef maintains). In Avalonia we route to the global
        /// Core Undo stack for now — the per-tab palette undo is part of
        /// the deferred KnownGap: PaletteToClipboard cluster.
        /// </summary>
        void PaletteUndo_Click(object? sender, RoutedEventArgs e) => Undo_Click(sender, e);

        /// <summary>
        /// Palette REDO sub-button. Same short-circuit as Redo_Click —
        /// Core Undo.RunRedo is a deferred surface.
        /// </summary>
        void PaletteRedo_Click(object? sender, RoutedEventArgs e) => Redo_Click(sender, e);

        // -----------------------------------------------------------------
        // Wired button handlers — Clipboard / MainImage Import / Export.
        // -----------------------------------------------------------------

        /// <summary>
        /// Palette-to-clipboard (#974). Mirrors WinForms
        /// <c>PaletteFormRef.PALETTE_TO_CLIPBOARD_BUTTON_Click</c>: pack the 16
        /// current palette entries to GBA 5-5-5 big-endian hex (4 chars/entry,
        /// 64 chars total) and copy to the system clipboard via the Avalonia
        /// <c>IClipboard.SetTextAsync</c> async API. The grid is always
        /// populated, so this is enabled unconditionally; it never writes ROM.
        /// </summary>
        async void PaletteClipboard_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rgb = ReadPaletteFromUI();
                string hex = ImageTSAEditorViewModel.BuildPaletteClipboardHex(rgb);

                IClipboard? clipboard = global::Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null)
                {
                    CoreState.Services.ShowError(R._("Clipboard is not available."));
                    return;
                }
                await clipboard.SetTextAsync(hex);
                Log.Notify($"ImageTSAEditor: palette copied to clipboard ({hex}).");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageTSAEditorView.PaletteClipboard failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Palette to clipboard failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Main Image import (#901) — tilesheet-only, mirrors WF image1_Import.
        ///
        /// The WF TSA editor builds its main-image ImageFormRef with
        /// tsa_pointer = 0 and only the ZIMAGE control wired, so Import encodes
        /// the SAME-SIZE PNG to plain 4bpp tiles (ImageToByte16Tile), LZ77-
        /// compresses, and repoints ONLY the ZImg pointer — TSA + palette are
        /// never touched. We mirror that exactly:
        ///   1. File dialog -> LoadAndRemapFromFile(strictSize) against the
        ///      editor's active palette (import never writes the palette).
        ///   2. TSAImageImportCore.ImportTSAImage under one UndoService scope.
        ///   3. On error: Rollback + ShowError + restore the rendered previews.
        ///   4. On success: Commit + MarkClean + refresh the previews.
        /// Disabled until Init() (IsContextLoaded).
        /// </summary>
        async void MainImageImport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsContextLoaded) return;

            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null) return;
            if (_vm.ZImgPointer == U.NOT_FOUND) return;

            // SAME-SIZE: the tilesheet dimensions are the natural ZImg size, NOT
            // the (header-bumped) editor canvas. Derive them from Core so the
            // file-dialog strict-size check matches what ImportTSAImage enforces.
            if (!TSAImageImportCore.TryCalcTilesheetSize(rom, _vm.ZImgPointer,
                    out int widthPx, out int heightPx))
            {
                CoreState.Services.ShowError(
                    R._("TSA Main Image Import: could not determine the existing tilesheet size."));
                return;
            }

            // Read the editor's active palette so the imported image is remapped
            // to the current colors (import never writes the palette).
            uint paletteAddr = _vm.ResolveActivePaletteAddress();
            byte[]? existingPalette = ReadActivePaletteBytes(rom, paletteAddr);
            if (existingPalette == null)
            {
                CoreState.Services.ShowError(
                    R._("TSA Main Image Import: could not read the active palette."));
                return;
            }

            string? filePath = await FEBuilderGBA.Avalonia.Dialogs.FileDialogHelper.OpenImageFile(TopLevel.GetTopLevel(this) as Window);
            if (string.IsNullOrEmpty(filePath)) return;

            var loadResult = ImageImportService.LoadAndRemapFromFile(
                filePath, widthPx, heightPx, existingPalette, 16, strictSize: true);
            if (loadResult == null || !loadResult.Success)
            {
                string err = loadResult?.Error ?? R._("Unknown error");
                CoreState.Services.ShowError(R._("TSA Main Image Import failed: {0}", err));
                return;
            }

            _undoService.Begin("TSA Main Image Import");
            string writeError;
            try
            {
                writeError = TSAImageImportCore.ImportTSAImage(
                    rom, loadResult.IndexedPixels, widthPx, heightPx, _vm.ZImgPointer);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                // Snapshot-restore the rendered previews so a failed write never
                // leaves the UI showing an unpersisted image (#871 lesson).
                RefreshBattleCanvas();
                RefreshChipList();
                CoreState.Services.ShowError(R._("TSA Main Image Import failed: {0}", ex.Message));
                return;
            }

            if (!string.IsNullOrEmpty(writeError))
            {
                _undoService.Rollback();
                RefreshBattleCanvas();
                RefreshChipList();
                CoreState.Services.ShowError(R._("TSA Main Image Import failed: {0}", writeError));
                return;
            }

            _undoService.Commit();
            _vm.MarkClean();
            // The tilesheet changed -> re-render both read-only previews.
            RefreshBattleCanvas();
            RefreshChipList();
        }

        /// <summary>
        /// Read the 16-color (32-byte) palette slice the active palette address
        /// points at, in RAW GBA BGR555 LE bytes, for LoadAndRemapFromFile's
        /// closest-color remap. Returns null (no throw) when the range is
        /// out of bounds.
        /// </summary>
        static byte[]? ReadActivePaletteBytes(ROM rom, uint paletteAddr)
        {
            if (rom == null || rom.Data == null) return null;
            if (!U.isSafetyOffset(paletteAddr, rom)) return null;
            if ((ulong)paletteAddr + 0x20UL > (ulong)rom.Data.Length) return null;
            byte[] pal = new byte[0x20];
            Array.Copy(rom.Data, (int)paletteAddr, pal, 0, 0x20);
            return pal;
        }

        /// <summary>
        /// Raw tilesheet export (#974). Mirrors WF <c>image1_Export</c>: the
        /// LZ77-decompressed ZImg tiles laid out as an 8-tile-wide 4bpp strip
        /// (NOT the TSA-composited #808 canvas). Renders via the read-only
        /// <see cref="ImageTSAEditorViewModel.RenderRawTilesheet"/> Core seam
        /// into the Main Image tab's preview control, then saves it to PNG via
        /// the shared <see cref="Controls.GbaImageControl.ExportPng"/> save-file
        /// dialog. Read-only — no UndoService. Gated on IsContextLoaded.
        /// </summary>
        async void MainImageExport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsContextLoaded) return;

            try
            {
                IImage img = _vm.RenderRawTilesheet();
                if (img == null)
                {
                    CoreState.Services.ShowError(
                        R._("TSA Image Export: could not decode the main tilesheet."));
                    return;
                }
                // Show the rendered tilesheet in the Main Image preview so the
                // user sees what is being exported, then save it to PNG.
                MainImagePreview.SetImage(img);
                await MainImagePreview.ExportPng(TopLevel.GetTopLevel(this) as Window, "tsa_tilesheet");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageTSAEditorView.MainImageExport failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("TSA Image Export failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Read-only Export PNG of the TSA-composited main image (#808). Saves
        /// the BattlePreview image via the shared GbaImageControl save-file
        /// dialog. Enabled only when CanExportBattle (a successful render).
        /// </summary>
        async void BattleExportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                await BattlePreview.ExportPng(TopLevel.GetTopLevel(this) as Window, "tsa_main_image");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageTSAEditorView.BattleExportPng failed: {0}", ex.Message);
            }
        }
    }
}
