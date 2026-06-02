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
// _undoService.Begin/Commit/Rollback. The Clipboard / MainImageImport /
// MainImageExport buttons are explicit IsEnabled=False stubs whose Click
// handlers short-circuit — covered by KnownGap markers in the AXAML.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageTSAEditorView : TranslatedWindow, IEditorView
    {
        readonly ImageTSAEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "TSA Tile Editor";
        public bool IsLoaded => _vm.IsLoaded;

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
            // Redo is unsupported by Core.Undo (RunRedo doesn't exist), so
            // disable both Redo entry points up front. Their Click handlers
            // remain wired only for the View_AllButtons_AreWiredOrExplicitlyInert
            // audit; user-visible they render disabled rather than silently
            // logging a no-op (Copilot review feedback).
            RedoButton.IsEnabled = false;
            PaletteRedoButton.IsEnabled = false;
            Opened += (_, _) => LoadList();
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
                Log.Error("ImageTSAEditorView.Init failed: {0}", ex.Message);
            }

            // Populate the 16x3 R/G/B grid from the existing ROM palette so
            // the user sees current ROM data before clicking Write / Palette
            // Write — prevents accidentally overwriting a slot with zeros.
            ReloadPaletteIntoGrid();

            UpdateInfoLabel();
            RefreshBattleCanvas();
            RefreshChipList();
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
                Log.Error("ImageTSAEditorView.ReloadPaletteIntoGrid failed: {0}", ex.Message);
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
                Log.Error("ImageTSAEditorView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ImageTSAEditorView.OnSelected failed: {0}", ex.Message);
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
                Log.Error("ImageTSAEditorView.RefreshBattleCanvas failed: {0}", ex.Message);
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
                Log.Error("ImageTSAEditorView.RefreshChipList failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        // -----------------------------------------------------------------
        // Click handlers — Write / PaletteWrite go through UndoService.
        // -----------------------------------------------------------------

        /// <summary>
        /// Main Write button (top toolbar). In WinForms this writes TSA +
        /// palette in one shot via ImageFormRef.WriteImageData. The TSA-
        /// byte-write path is deferred (KnownGap: TSAByteWrite), so this
        /// button writes the active palette only. Disabled until Init().
        /// </summary>
        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsContextLoaded) return;

            _undoService.Begin("TSA Editor Write");
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
                CoreState.Services.ShowError($"TSA Write failed: {ex.Message}");
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
                CoreState.Services.ShowError($"Palette Write failed: {ex.Message}");
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
                CoreState.Services.ShowError($"Undo failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Redo button. Core Undo currently exposes only RunUndo (the
        /// global redo stack is a WinForms-only extension), so the button
        /// is disabled in the constructor (IsEnabled=false) and this handler
        /// is reachable only via the audit-only Click wiring. A future Core
        /// Undo.RunRedo will replace this stub and let us re-enable the
        /// button. Defensive guard mirrors PaletteRedo_Click.
        /// </summary>
        void Redo_Click(object? sender, RoutedEventArgs e)
        {
            // No-op: button is disabled at runtime. We surface a one-line
            // info dialog if the handler is ever invoked programmatically.
            CoreState.Services.ShowInfo("Redo is not yet available in the Avalonia TSA editor (deferred until Core.Undo.RunRedo lands).");
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
        // KnownGap inert stubs — IsEnabled=False in AXAML, Click handler
        // is present so the View_AllButtons_AreWiredOrExplicitlyInert test
        // passes (one or the other is required by the audit; we have both).
        // -----------------------------------------------------------------

        /// <summary>
        /// KnownGap: PaletteToClipboard. WinForms uses
        /// System.Windows.Forms.Clipboard which is WinForms-only; the
        /// Avalonia equivalent (TopLevel.Clipboard async API) is a
        /// separate cross-cutting refactor not in scope here.
        /// </summary>
        void PaletteClipboard_Click(object? sender, RoutedEventArgs e)
        {
            Log.Notify("ImageTSAEditor PaletteClipboard - deferred (KnownGap: PaletteToClipboard)");
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
                    "TSA Main Image Import: could not determine the existing tilesheet size.");
                return;
            }

            // Read the editor's active palette so the imported image is remapped
            // to the current colors (import never writes the palette).
            uint paletteAddr = _vm.ResolveActivePaletteAddress();
            byte[]? existingPalette = ReadActivePaletteBytes(rom, paletteAddr);
            if (existingPalette == null)
            {
                CoreState.Services.ShowError(
                    "TSA Main Image Import: could not read the active palette.");
                return;
            }

            string? filePath = await FEBuilderGBA.Avalonia.Dialogs.FileDialogHelper.OpenImageFile(this);
            if (string.IsNullOrEmpty(filePath)) return;

            var loadResult = ImageImportService.LoadAndRemapFromFile(
                filePath, widthPx, heightPx, existingPalette, 16, strictSize: true);
            if (loadResult == null || !loadResult.Success)
            {
                string err = loadResult?.Error ?? "Unknown error";
                CoreState.Services.ShowError($"TSA Main Image Import failed: {err}");
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
                CoreState.Services.ShowError($"TSA Main Image Import failed: {ex.Message}");
                return;
            }

            if (!string.IsNullOrEmpty(writeError))
            {
                _undoService.Rollback();
                RefreshBattleCanvas();
                RefreshChipList();
                CoreState.Services.ShowError($"TSA Main Image Import failed: {writeError}");
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
        /// KnownGap: MainImageExport. Mirrors WF image1_Export (the raw-
        /// tilesheet PNG export) — would call into ImageFormRef.ExportImageHandler
        /// which is WinForms-only. NOTE: the read-only TSA-composited Export PNG
        /// (#808, BattleExportPng_Click) is a DIFFERENT, already-wired export;
        /// this raw-tilesheet export stays deferred.
        /// </summary>
        void MainImageExport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Notify("ImageTSAEditor MainImageExport - deferred (KnownGap: MainImageExport)");
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
                await BattlePreview.ExportPng(this, "tsa_main_image");
            }
            catch (Exception ex)
            {
                Log.Error("ImageTSAEditorView.BattleExportPng failed: {0}", ex.Message);
            }
        }
    }
}
