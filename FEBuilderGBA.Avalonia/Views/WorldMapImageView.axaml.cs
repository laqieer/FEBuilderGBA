// SPDX-License-Identifier: GPL-3.0-or-later
// WorldMapImageView code-behind — gap-sweep #395 parity raise.
//
// Wires the six Avalonia tabs (Main / Event / Mini / PointIcon / Border /
// IconData) to the WorldMapImageViewModel.  Three distinct UndoService
// scopes:
//   * "Write World Map Pointers" — the top WriteAll button persisting all
//     13 canonical pointer slots in one transaction (Copilot CLI plan
//     review v1->v2 finding C1).
//   * "Write World Map Border" — per-record Border write.
//   * "Write World Map Icon"   — per-record Icon write.
//
// A single top-level Undo button is present (Copilot CLI plan review C3 —
// WinForms has no per-tab Undo; we don't introduce them either).
using System;
using System.Diagnostics;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapImageView : TranslatedWindow, IEditorView
    {
        readonly WorldMapImageViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "World Map Image";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapImageView()
        {
            InitializeComponent();
            // #843: the five reuse-based preview "Export PNG" buttons bind their
            // IsEnabled to the VM CanExport* gates, so the view needs a
            // DataContext pointing at the VM. (The 13 pointer NUDs + two
            // AddressLists are still pushed/read manually — only the new export
            // gates use bindings.)
            DataContext = _vm;
            Border_EntryList.SelectedAddressChanged += OnBorderSelected;
            IconData_EntryList.SelectedAddressChanged += OnIconSelected;
            Opened += (_, _) => LoadAll();
        }

        // ===================================================================
        // Common load entry-point
        // ===================================================================

        void LoadAll()
        {
            // Read-only initial load — wrap in an IsLoading scope so the VM's
            // automatic SetField -> IsDirty propagation does NOT flip the
            // dirty bit when the 13 pointer NUDs and two AddressLists
            // populate (Copilot bot inline review #1, #2, #3, #4 on PR #592).
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                _vm.LoadAll();
                RefreshTopRowNuds();
                LoadBorderList();
                LoadIconList();
                RefreshPreviews();
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapImageView.LoadAll failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = prevLoading;
                _vm.MarkClean();
            }
        }

        void RefreshTopRowNuds()
        {
            MainImageBox.Value = _vm.MainImagePtr;
            MainPaletteBox.Value = _vm.MainPalettePtr;
            MainDarkPaletteBox.Value = _vm.MainDarkPalettePtr;
            MainPaletteMapBox.Value = _vm.MainPaletteMapPtr;
            EventImageBox.Value = _vm.EventImagePtr;
            EventPaletteBox.Value = _vm.EventPalettePtr;
            EventTsaBox.Value = _vm.EventTsaPtr;
            MiniImageBox.Value = _vm.MiniImagePtr;
            MiniPaletteBox.Value = _vm.MiniPalettePtr;
            Point1ImageBox.Value = _vm.Point1ImagePtr;
            Point2ImageBox.Value = _vm.Point2ImagePtr;
            RoadImageBox.Value = _vm.RoadImagePtr;
            IconPaletteBox.Value = _vm.IconPalettePtr;
        }

        void ReadNudsIntoVm()
        {
            _vm.MainImagePtr = NudU32(MainImageBox);
            _vm.MainPalettePtr = NudU32(MainPaletteBox);
            _vm.MainDarkPalettePtr = NudU32(MainDarkPaletteBox);
            _vm.MainPaletteMapPtr = NudU32(MainPaletteMapBox);
            _vm.EventImagePtr = NudU32(EventImageBox);
            _vm.EventPalettePtr = NudU32(EventPaletteBox);
            _vm.EventTsaPtr = NudU32(EventTsaBox);
            _vm.MiniImagePtr = NudU32(MiniImageBox);
            _vm.MiniPalettePtr = NudU32(MiniPaletteBox);
            _vm.Point1ImagePtr = NudU32(Point1ImageBox);
            _vm.Point2ImagePtr = NudU32(Point2ImageBox);
            _vm.RoadImagePtr = NudU32(RoadImageBox);
            _vm.IconPalettePtr = NudU32(IconPaletteBox);
        }

        // ===================================================================
        // Top WriteAll button — all 13 canonical pointer slots
        // ===================================================================

        void WriteAll_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Write World Map Pointers");
            try
            {
                ReadNudsIntoVm();
                bool ok = _vm.WriteAllPointers();
                if (!ok)
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
                // Reset the dirty bit after a successful save (Copilot bot
                // inline review #4 on PR #592). ReadNudsIntoVm did flip the
                // VM SetField paths above, so the VM IsDirty would remain
                // true unless we explicitly clear it here.
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("WorldMapImageView.WriteAll failed: {0}", ex.Message);
            }
        }

        // ===================================================================
        // Border tab
        // ===================================================================

        void LoadBorderList()
        {
            // Reload is a read-only action — keep IsLoading high so the
            // selection-change side-effects don't flip IsDirty.
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadBorderList();
                Border_EntryList.SetItems(items);
                // Mirror WF InputFormRef BaseAddress + DataCount in the
                // read panel (Copilot bot inline review on PR #592 round 2).
                if (Border_TopBar != null)
                {
                    Border_TopBar.StartAddressText = $"0x{_vm.BorderReadStartAddress:X08}";
                    Border_TopBar.ReadCountText = _vm.BorderReadCount.ToString();
                }
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
        }

        void OnBorderSelected(uint addr)
        {
            // Save/restore the prior IsLoading state so a selection during
            // initial load doesn't end the outer load scope early
            // (Copilot bot inline review #2 on PR #592).
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                _vm.LoadBorderEntry(addr);
                Border_AddressBox.Value = addr;
                Border_SelectAddressLabel.Content = $"0x{addr:X08}";
                Border_P0Box.Value = _vm.BorderP0;
                Border_P4Box.Value = _vm.BorderP4;
                Border_W8Box.Value = _vm.BorderW8;
                Border_W10Box.Value = _vm.BorderW10;
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapImageView.OnBorderSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
            // #849 NV5c: render the border AP preview after loading the record.
            RenderBorderPreview();
            // #1064 PR2: the border import gate depends on a selected record
            // (BorderCurrentAddr is now set), so refresh it after selection.
            _vm.RefreshImportGates();
        }

        /// <summary>
        /// Render the border AP preview into <c>BorderDrawSampleImage</c> and
        /// update the <c>CanExportBorder</c> binding gate. Mirrors
        /// <see cref="RenderInto"/> but uses the dedicated border VM method.
        /// </summary>
        void RenderBorderPreview()
        {
            RenderInto(BorderDrawSampleImage, _vm.TryRenderBorder,
                v => _vm.CanExportBorder = v, "Border");
        }

        void BorderWrite_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Write World Map Border");
            try
            {
                _vm.BorderP0 = NudU32(Border_P0Box);
                _vm.BorderP4 = NudU32(Border_P4Box);
                _vm.BorderW8 = NudU32(Border_W8Box);
                _vm.BorderW10 = NudU32(Border_W10Box);
                bool ok = _vm.WriteBorder();
                if (!ok)
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
                // Reset dirty after successful Border save (Copilot bot
                // inline review #4 on PR #592).
                _vm.MarkClean();
                // #849 NV5c: re-render preview after write (field values may have
                // changed).
                RenderBorderPreview();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("WorldMapImageView.BorderWrite failed: {0}", ex.Message);
            }
        }

        /// <summary>Export the border AP preview as PNG.</summary>
        async void BorderExport_Click(object? sender, RoutedEventArgs e)
            => await ExportPreview(BorderDrawSampleImage, "worldmap_border");

        // ===================================================================
        // #1064 PR2: County-border OAM/AP image import.
        //
        // The border graphic is a parts-sheet image + an AP-OAM block addressed
        // by the selected 12-byte border record (P0 = image pointer, P4 = AP
        // pointer). The import:
        //   1. file dialog for the main sheet -> resolve the {name}_NAME{ext}
        //      companion in the same folder (error if missing).
        //   2. Read the EXISTING 16-color border palette via the guarded Core
        //      helper, then REMAP both sheets onto it (image+AP only — the palette
        //      is NOT written, matching WF + the Copilot plan-review blocking fix:
        //      encoded tile indices use existing-ROM palette indices, never a
        //      discarded reducer palette).
        //   3. _undoService.Begin -> VM.ImportBorder (Core assembles + writes P0/P4
        //      with a byte-identical fault restore) -> Commit + re-render / Rollback
        //      + ShowError. FE8-only + selected-record gate via CanImportBorder.
        // ===================================================================

        async void BorderImport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenImageFile(this);
                if (path == null) return;
                await DoBorderImport(path);
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapImageView.BorderImport_Click failed: {ex}");
            }
        }

        /// <summary>
        /// Injectable border-import driver (testable without UI). Resolves the
        /// <c>_NAME</c> companion, remaps both 248×160 sheets onto the EXISTING ROM
        /// border palette (image+AP only), then the Core
        /// <see cref="ImageWorldMapCore.ImportBorder"/> (via the VM) assembles the
        /// seat + AP block and writes the selected record's P0/P4 under one undo
        /// scope with a byte-identical fault restore.
        /// </summary>
        // NOTE: this driver does no I/O await of its own (the file dialog is
        // awaited by BorderImport_Click; the load/remap + VM import are
        // synchronous), so it returns a completed Task rather than being `async`
        // (avoids CS1998 — Copilot bot review on PR #1099). It stays Task-returning
        // so BorderImport_Click can `await` it and headless tests can drive it.
        public System.Threading.Tasks.Task DoBorderImport(string sheetPath)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return System.Threading.Tasks.Task.CompletedTask;

            // 1. Resolve the {name}_NAME{ext} companion in the same folder.
            string namePath = MakeBorderNameImageFileName(sheetPath);
            if (!File.Exists(namePath))
            {
                CoreState.Services?.ShowError(R._(
                    "The companion name image is missing.\r\nExpected: {0}", namePath));
                return System.Threading.Tasks.Task.CompletedTask;
            }

            // 2. Read the EXISTING 16-color border palette (guarded; pointer-to-
            //    pointer slot). FE6/FE7 have this as 0 -> the gate already disables
            //    the button, but re-check here for the injectable entry point.
            if (!ImageWorldMapCore.TryGetStripPalette(
                    rom, rom.RomInfo.worldmap_county_border_palette_pointer, out byte[] palette16))
            {
                CoreState.Services?.ShowError(R._("The world map border palette is invalid."));
                return System.Threading.Tasks.Task.CompletedTask;
            }

            // 3. Remap BOTH sheets onto the existing palette (strict 248x160,
            //    nearest-color). The palette is NOT written — only the indices.
            var sheet = ImageImportService.LoadAndRemapFromFile(
                sheetPath, ImageUtilBorderAPCore.SRC_WIDTH, ImageUtilBorderAPCore.SRC_HEIGHT,
                palette16, 16, strictSize: true);
            if (sheet == null || !sheet.Success)
            {
                CoreState.Services?.ShowError(sheet?.Error ?? R._("Failed to load the border image."));
                return System.Threading.Tasks.Task.CompletedTask;
            }
            var name = ImageImportService.LoadAndRemapFromFile(
                namePath, ImageUtilBorderAPCore.SRC_WIDTH, ImageUtilBorderAPCore.SRC_HEIGHT,
                palette16, 16, strictSize: true);
            if (name == null || !name.Success)
            {
                CoreState.Services?.ShowError(name?.Error ?? R._("Failed to load the border name image."));
                return System.Threading.Tasks.Task.CompletedTask;
            }

            // 4. Assemble + write under one undo scope. The origin comes from the
            //    record's current W8/W10 NUDs (clamped inside the Core; W8/W10 are
            //    NOT persisted — WF parity).
            uint originX = NudU32(Border_W8Box);
            uint originY = NudU32(Border_W10Box);

            _undoService.Begin("Import World Map Border");
            try
            {
                string? err = _vm.ImportBorder(
                    sheet.IndexedPixels, name.IndexedPixels, palette16, originX, originY);
                if (!string.IsNullOrEmpty(err))
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(err);
                    return System.Threading.Tasks.Task.CompletedTask;
                }
                _undoService.Commit();
                // Reload the record so P0/P4 reflect the freshly-written pointers,
                // then re-render the border AP preview from the new streams. Wrap
                // the reload in an IsLoading scope so LoadBorderEntry's SetField
                // writes don't flip IsDirty back true after the committed import
                // (Copilot PR #1099 review); MarkClean AFTER the reload.
                bool prevLoading = _vm.IsLoading;
                try
                {
                    _vm.IsLoading = true;
                    _vm.LoadBorderEntry(_vm.BorderCurrentAddr);
                    Border_P0Box.Value = _vm.BorderP0;
                    Border_P4Box.Value = _vm.BorderP4;
                }
                finally { _vm.IsLoading = prevLoading; }
                _vm.MarkClean();
                RenderBorderPreview();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"WorldMapImageView.DoBorderImport write failed: {ex}");
                CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
            }
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// Resolve the border name-image companion path: same directory,
        /// <c>{base}_NAME{ext}</c>. Mirrors WF
        /// <c>ImageUtilBorderAP.MakeBorderNameImageFileName</c>.
        /// </summary>
        static string MakeBorderNameImageFileName(string borderFilename)
        {
            string baseDir = Path.GetDirectoryName(borderFilename) ?? "";
            string name = Path.GetFileNameWithoutExtension(borderFilename);
            string ext = Path.GetExtension(borderFilename);
            return Path.Combine(baseDir, name + "_NAME" + ext);
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnBorderTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadBorderList(); }
            catch (Exception ex) { Log.ErrorF("BorderReload failed: {0}", ex.Message); }
        }

        // #825: list-expand for the border table (worldmap_county_border_pointer,
        // 12-byte records). Prompt -> ExpandTableTo + RepointAllReferences ->
        // refresh honoring the new count. Mirrors ImageMapActionAnimationView.
        // ListExpand_Click but with the all-reference repoint (the canonical
        // ptr is fixed; ExpandTableTo single-slot-repoints it, then
        // RepointAllReferences repoints any raw/LDR secondary refs — a return of
        // 0 is success per NOTE A).
        async void BorderListExpand_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }
                if (_vm.BorderReadCount == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: list is empty."));
                    return;
                }

                // Default = current + 1, max 255 (mirrors WF
                // AddressListExpandsButton_255 convention).
                uint current = (uint)_vm.BorderReadCount;
                uint defaultCount = current + 1;
                if (defaultCount > 255) defaultCount = 255;
                uint? chosen = await NumberInputDialog.Show(
                    this,
                    R._("Enter the new entry count for the world map border list (current: {0}, max: 255).", current),
                    R._("List Expansion"),
                    defaultCount,
                    current,
                    255);
                if (chosen == null) return; // cancelled
                uint newCount = chosen.Value;
                if (newCount == current)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count equals current count."));
                    return;
                }

                _undoService.Begin("Expand World Map Border List");
                try
                {
                    string err = _vm.ExpandBorderList(newCount, _undoService.GetActiveUndoData());
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();

                    // NOTE B: render the grown list directly from the new base +
                    // new count (the VM already set BorderReadCount/StartAddress
                    // from the ExpandResult). Re-scanning would stop at the first
                    // zero-filled new row and show the OLD count.
                    RefreshBorderListFromReadConfig();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded world map border list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.ErrorF("WorldMapImageView.BorderListExpand inner failed: {0}", inner.Message);
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapImageView.BorderListExpand failed: {0}", ex.Message);
            }
        }

        // Render the border AddressList from the VM's post-expand read-config
        // (BorderReadStartAddress = encoded GBA pointer of the new base;
        // BorderReadCount = new row count) WITHOUT re-scanning (NOTE B).
        void RefreshBorderListFromReadConfig()
        {
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                uint baseAddr = U.toOffset(_vm.BorderReadStartAddress);
                var items = _vm.BuildBorderListForCount(baseAddr, _vm.BorderReadCount);
                Border_EntryList.SetItems(items);
                if (Border_TopBar != null)
                {
                    Border_TopBar.StartAddressText = $"0x{_vm.BorderReadStartAddress:X08}";
                    Border_TopBar.ReadCountText = _vm.BorderReadCount.ToString();
                }
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
        }

        // ===================================================================
        // Icon-data tab
        // ===================================================================

        void LoadIconList()
        {
            // Reload is a read-only action — keep IsLoading high so the
            // selection-change side-effects don't flip IsDirty.
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadIconList();
                IconData_EntryList.SetItems(items);
                // Mirror WF InputFormRef BaseAddress + DataCount in the
                // read panel (Copilot bot inline review on PR #592 round 2).
                if (IconData_TopBar != null)
                {
                    IconData_TopBar.StartAddressText = $"0x{_vm.IconReadStartAddress:X08}";
                    IconData_TopBar.ReadCountText = _vm.IconReadCount.ToString();
                }
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
        }

        void OnIconSelected(uint addr)
        {
            // Save/restore the prior IsLoading state so a selection during
            // initial load doesn't end the outer load scope early
            // (Copilot bot inline review #3 on PR #592).
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                _vm.LoadIconEntry(addr);
                IconData_AddressBox.Value = addr;
                IconData_SelectAddressLabel.Content = $"0x{addr:X08}";
                IconData_B0Box.Value = _vm.IconB0;
                IconData_B1Box.Value = _vm.IconB1;
                IconData_B2Box.Value = _vm.IconB2;
                IconData_B3Box.Value = _vm.IconB3;
                IconData_P4Box.Value = _vm.IconP4;
                IconData_B8Box.Value = _vm.IconB8;
                IconData_B9Box.Value = _vm.IconB9;
                IconData_B10Box.Value = _vm.IconB10;
                IconData_B11Box.Value = _vm.IconB11;
                IconData_B12Box.Value = _vm.IconB12;
                IconData_B13Box.Value = _vm.IconB13;
                IconData_W14Box.Value = _vm.IconW14;
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapImageView.OnIconSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
        }

        void IconWrite_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Write World Map Icon");
            try
            {
                _vm.IconB0 = NudByte(IconData_B0Box);
                _vm.IconB1 = NudByte(IconData_B1Box);
                _vm.IconB2 = NudByte(IconData_B2Box);
                _vm.IconB3 = NudByte(IconData_B3Box);
                _vm.IconP4 = NudU32(IconData_P4Box);
                _vm.IconB8 = NudByte(IconData_B8Box);
                _vm.IconB9 = NudByte(IconData_B9Box);
                _vm.IconB10 = NudByte(IconData_B10Box);
                _vm.IconB11 = NudByte(IconData_B11Box);
                _vm.IconB12 = NudByte(IconData_B12Box);
                _vm.IconB13 = NudByte(IconData_B13Box);
                _vm.IconW14 = NudU16(IconData_W14Box);
                bool ok = _vm.WriteIcon();
                if (!ok)
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
                // Reset dirty after successful Icon save (Copilot bot
                // inline review #4 on PR #592).
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("WorldMapImageView.IconWrite failed: {0}", ex.Message);
            }
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnIconDataTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadIconList(); }
            catch (Exception ex) { Log.ErrorF("IconDataReload failed: {0}", ex.Message); }
        }

        // #825: list-expand for the icon-data table (worldmap_icon_data_pointer,
        // 16-byte records). Same prompt -> ExpandTableTo + RepointAllReferences
        // -> refresh-without-rescan flow as the border tab.
        async void IconDataListExpand_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }
                if (_vm.IconReadCount == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: list is empty."));
                    return;
                }

                uint current = (uint)_vm.IconReadCount;
                uint defaultCount = current + 1;
                if (defaultCount > 255) defaultCount = 255;
                uint? chosen = await NumberInputDialog.Show(
                    this,
                    R._("Enter the new entry count for the world map icon-data list (current: {0}, max: 255).", current),
                    R._("List Expansion"),
                    defaultCount,
                    current,
                    255);
                if (chosen == null) return; // cancelled
                uint newCount = chosen.Value;
                if (newCount == current)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count equals current count."));
                    return;
                }

                _undoService.Begin("Expand World Map Icon List");
                try
                {
                    string err = _vm.ExpandIconList(newCount, _undoService.GetActiveUndoData());
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();

                    // NOTE B: render from the new base + new count, no re-scan.
                    RefreshIconListFromReadConfig();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded world map icon-data list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.ErrorF("WorldMapImageView.IconDataListExpand inner failed: {0}", inner.Message);
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapImageView.IconDataListExpand failed: {0}", ex.Message);
            }
        }

        // IconData counterpart of RefreshBorderListFromReadConfig (NOTE B).
        void RefreshIconListFromReadConfig()
        {
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                uint baseAddr = U.toOffset(_vm.IconReadStartAddress);
                var items = _vm.BuildIconListForCount(baseAddr, _vm.IconReadCount);
                IconData_EntryList.SetItems(items);
                if (IconData_TopBar != null)
                {
                    IconData_TopBar.StartAddressText = $"0x{_vm.IconReadStartAddress:X08}";
                    IconData_TopBar.ReadCountText = _vm.IconReadCount.ToString();
                }
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
        }

        // ===================================================================
        // Live previews — main field map (#846 NV5b) + event / mini / point1 /
        // point2 / road (#843 NV5a). Each renders via the matching
        // ImageWorldMapCore resolver+decode into a GbaImageControl and gates its
        // read-only Export PNG button. On any failure (no ROM, unset/corrupt/
        // truncated pointer, or — for the main field map — a non-FE8 ROM) the
        // preview is cleared and the export gate is set false. Never throws.
        // Mirrors ImageTSAEditorView.RefreshBattleCanvas. The county border
        // (NV5c) is deliberately NOT rendered here.
        // ===================================================================

        void RefreshPreviews()
        {
            // Main field map: FE8-only via the new ByteToImage16TilePaletteMap
            // primitive (FE6/FE7/incomplete -> null -> blank, export disabled).
            RenderInto(MainFieldMapPreview, _vm.TryRenderMainFieldMap, v => _vm.CanExportMain = v, "MainFieldMap");
            RenderInto(EventPreviewImage, _vm.TryRenderEvent, v => _vm.CanExportEvent = v, "Event");
            RenderInto(MiniPreviewImage, _vm.TryRenderMini, v => _vm.CanExportMini = v, "Mini");
            RenderInto(Point1PreviewImage, _vm.TryRenderPoint1, v => _vm.CanExportPoint1 = v, "Point1");
            RenderInto(Point2PreviewImage, _vm.TryRenderPoint2, v => _vm.CanExportPoint2 = v, "Point2");
            RenderInto(RoadPreviewImage, _vm.TryRenderRoad, v => _vm.CanExportRoad = v, "Road");
            // #875: dark field map preview (renders but no separate GbaImageControl —
            // the export button alone gates on CanExportDark).
            try
            {
                IImage? dark = _vm.TryRenderDarkFieldMap();
                _vm.CanExportDark = dark != null;
                _darkFieldMapImage = dark;
            }
            catch (Exception ex)
            {
                _vm.CanExportDark = false;
                _darkFieldMapImage = null;
                Log.Error($"WorldMapImageView.RenderDarkFieldMap failed: {ex}");
            }
            // Refresh the import gates (FE8-only enable).
            _vm.RefreshImportGates();
        }

        // Cached dark field map render — reused by DarkExport_Click.
        IImage? _darkFieldMapImage;

        void RenderInto(GbaImageControl target, Func<IImage?> render,
            Action<bool> setGate, string label)
        {
            try
            {
                IImage? img = render();
                target.SetImage(img);
                setGate(img != null);
            }
            catch (Exception ex)
            {
                target.SetImage(null);
                setGate(false);
                // Core Log.Error(params string[]) string.Joins its args (no {0}/{1}
                // substitution), so pass a single interpolated string and include
                // the full exception (stack trace) rather than just ex.Message.
                Log.Error($"WorldMapImageView.Render{label} failed: {ex}");
            }
        }

        // ===================================================================
        // #875: Main Import / Dark Import / Dark Export
        // ===================================================================

        /// <summary>
        /// Open a PNG, remap to the current ROM palette, validate mono-sub-palette
        /// per tile, then write image + palette + palette-map (LZ77) in-place under
        /// one undo scope. Refreshes previews on success.
        /// Injectable test entry point: <see cref="DoMainImport"/>.
        /// </summary>
        async void MainImport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenImageFile(this);
                if (path == null) return;
                await DoMainImport(path);
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapImageView.MainImport_Click failed: {ex}");
            }
        }

        /// <summary>Injectable main-import driver (testable without UI).</summary>
        /// <remarks>
        /// FIX A (WF parity): derive the 4x16-color GBA palette FROM the imported PNG,
        /// not from the existing ROM palette. WF ImportButton_Click calls
        /// ImageToPalette(bitmap, 4) which reads the indexed bitmap Palette.Entries[0..63]
        /// directly. Since SkiaSharp LoadImage decodes to RGBA (not indexed), we
        /// reconstruct the 4x16-color palette by sampling unique RGBA colors per
        /// tile-group and converting via RGBAToGBAColor -- same color-set WF reads.
        /// The derived palette is written to ROM, so importing a PNG with different
        /// colors changes the ROM colors (WF parity). The indexed pixels are derived
        /// from the SAME palette via RgbaToIndexed.
        /// </remarks>
        public async System.Threading.Tasks.Task DoMainImport(string imagePath)
        {
            // 1. Load image via IImageService (returns RGBA IImage).
            IImage? img = CoreState.ImageService?.LoadImage(imagePath);
            if (img == null)
            {
                CoreState.Services?.ShowError(R._("Failed to load image from: {0}", imagePath));
                return;
            }
            try
            {
                // 2. Validate dimensions.
                if (img.Width != 480 || img.Height != 320)
                {
                    CoreState.Services?.ShowError(R.Error(
                        "Image dimensions must be 480×320.\r\n\r\nSelected image: {0}×{1}.",
                        img.Width, img.Height));
                    return;
                }

                // 3. Read RGBA pixels.
                byte[] rgba = img.GetPixelData();
                if (rgba == null || rgba.Length < 480 * 320 * 4)
                {
                    CoreState.Services?.ShowError(R._("Image pixel data is missing or too short."));
                    return;
                }

                // 4. FIX A -- derive the 4-sub-palette (128-byte) GBA palette FROM the
                //    PNG own RGBA pixels. WF reads bitmap.Palette.Entries[0..63]
                //    (indexed PNG); cross-platform equivalent: sample unique RGBA colors
                //    per tile-strip and convert to GBA BGR555. Result is written to ROM
                //    so the PNG palette replaces the existing one (WF parity).
                byte[] gbaPalette128 = BuildGbaPaletteFromRgba(rgba, 480, 320, 4);

                // 5. Remap RGBA to indexed using the DERIVED palette (so indices match
                //    the palette we are about to write, not the old ROM palette).
                var (indexedPixels, remapErr) = WorldMapImageViewModel.RgbaToIndexed(rgba, 480, 320, gbaPalette128);
                if (indexedPixels == null)
                {
                    CoreState.Services?.ShowError(R.Error("Remap to indexed failed: {0}", remapErr));
                    return;
                }

                // 6. Validate mono-sub-palette per tile (WF format check).
                string tileErr = ImageWorldMapCore.ValidateTileMonoPalette(indexedPixels, 480, 320);
                if (!string.IsNullOrEmpty(tileErr))
                {
                    CoreState.Services?.ShowError(tileErr);
                    return;
                }

                // 7. Write under one undo scope.
                _undoService.Begin("Import World Map Main Field Map");
                try
                {
                    string? err = _vm.DoMainImport(indexedPixels, gbaPalette128);
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    // #1013: record the imported source-file path under the WF
                    // "WorldMap_" ResourceCache key so Open Source File / Folder
                    // become available. WF ImportButton_Click records ONLY on the
                    // MAIN import (not the dark import).
                    _vm.RecordSourceFile(imagePath);
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error($"WorldMapImageView.DoMainImport write failed: {ex}");
                    CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
                    return;
                }
            }
            finally
            {
                // FIX C: dispose the loaded IImage to release Skia unmanaged resources.
                img.Dispose();
            }

            // 8. Refresh previews.
            RefreshPreviews();
        }

        /// <summary>
        /// Open a PNG, validate 480×320 + ≤4 sub-palettes, write ONLY the 128-byte
        /// dark palette in-place under one undo scope.
        /// Injectable test entry point: <see cref="DoDarkImport"/>.
        /// </summary>
        async void DarkImport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenImageFile(this);
                if (path == null) return;
                await DoDarkImport(path);
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapImageView.DarkImport_Click failed: {ex}");
            }
        }

        /// <summary>Injectable dark-import driver (testable without UI).</summary>
        /// <remarks>
        /// FIX A (WF parity) for Dark Import: derive the 4x16-color GBA palette FROM
        /// the imported PNG RGBA pixels via BuildGbaPaletteFromRgba -- same rule as
        /// WF DarkMAPImportButton_Click: ImageToPalette(bitmap, 4). FIX D: remove the
        /// dead RemapToMultiPalette call that passed an all-zero palette and produced
        /// an unused result.
        /// </remarks>
        public async System.Threading.Tasks.Task DoDarkImport(string imagePath)
        {
            // 1. Load image via IImageService.
            IImage? img = CoreState.ImageService?.LoadImage(imagePath);
            if (img == null)
            {
                CoreState.Services?.ShowError(R._("Failed to load image from: {0}", imagePath));
                return;
            }
            try
            {
                // 2. Validate 480x320.
                if (img.Width != 480 || img.Height != 320)
                {
                    CoreState.Services?.ShowError(R.Error(
                        "Image dimensions must be 480×320.\r\n\r\nSelected image: {0}×{1}.",
                        img.Width, img.Height));
                    return;
                }

                // 3. Read RGBA pixels.
                byte[] rgba = img.GetPixelData();
                if (rgba == null || rgba.Length < 480 * 320 * 4)
                {
                    CoreState.Services?.ShowError(R._("Image pixel data is missing or too short."));
                    return;
                }

                // 4. FIX A -- derive the dark GBA palette FROM the PNG RGBA pixels.
                //    WF DarkMAPImportButton_Click calls ImageToPalette(bitmap, 4)
                //    which reads the indexed bitmap Palette.Entries[0..63].
                //    Cross-platform: sample unique RGBA colors per tile-strip and
                //    convert to GBA BGR555. FIX D: the previous dead RemapToMultiPalette
                //    call (with all-zero palette) is removed -- it produced no usable
                //    palette and its result was discarded.
                byte[] darkPalette128 = BuildGbaPaletteFromRgba(rgba, 480, 320, 4);

                // 5. Write under one undo scope.
                _undoService.Begin("Import World Map Dark Palette");
                try
                {
                    string? err = _vm.DoDarkImport(darkPalette128);
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error($"WorldMapImageView.DoDarkImport write failed: {ex}");
                    CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
                    return;
                }
            }
            finally
            {
                // FIX C: dispose the loaded IImage to release Skia unmanaged resources.
                img.Dispose();
            }

            // 6. Refresh dark preview gate.
            RefreshPreviews();
        }

        /// <summary>
        /// Build a flat 128-byte GBA palette (4 sub-palettes x 16 colors x 2 bytes)
        /// from RGBA pixels by sampling the first 16 unique colors per 8x8 tile group.
        /// This is the cross-platform equivalent of WF ImageToPalette(bitmap, 4):
        /// WF reads bitmap.Palette.Entries[0..63] directly (indexed PNG);
        /// we reconstruct the same color-set from RGBA by sampling per tile-strip.
        /// The derived palette is written to ROM (FIX A WF parity).
        /// </summary>
        static byte[] BuildGbaPaletteFromRgba(byte[] rgba, int width, int height, int subPalCount)
        {
            byte[] result = new byte[subPalCount * 16 * 2];
            if (CoreState.ImageService == null) return result;
            int tilesX = width / 8;
            int tilesY = height / 8;
            int tilesPerSubPal = (tilesX * tilesY + subPalCount - 1) / subPalCount;

            for (int sp = 0; sp < subPalCount; sp++)
            {
                // Collect unique RGBA colors from the tiles in this sub-palette group.
                var seen = new System.Collections.Generic.List<uint>(16);
                int startTile = sp * tilesPerSubPal;
                int endTile = System.Math.Min(startTile + tilesPerSubPal, tilesX * tilesY);
                for (int ti = startTile; ti < endTile && seen.Count < 16; ti++)
                {
                    int ty = (ti / tilesX) * 8;
                    int tx = (ti % tilesX) * 8;
                    for (int py = 0; py < 8 && seen.Count < 16; py++)
                    {
                        for (int px = 0; px < 8 && seen.Count < 16; px++)
                        {
                            int idx = ((ty + py) * width + (tx + px)) * 4;
                            if (idx + 3 >= rgba.Length) continue;
                            byte r = rgba[idx]; byte g = rgba[idx + 1]; byte b = rgba[idx + 2];
                            uint c = (uint)(r | (g << 8) | (b << 16));
                            if (!seen.Contains(c)) seen.Add(c);
                        }
                    }
                }
                // Write the collected colors as GBA BGR555.
                for (int ci = 0; ci < seen.Count && ci < 16; ci++)
                {
                    byte r = (byte)(seen[ci] & 0xFF);
                    byte g = (byte)((seen[ci] >> 8) & 0xFF);
                    byte b2 = (byte)((seen[ci] >> 16) & 0xFF);
                    ushort gba = CoreState.ImageService.RGBAToGBAColor(r, g, b2);
                    int off = (sp * 16 + ci) * 2;
                    result[off] = (byte)(gba & 0xFF);
                    result[off + 1] = (byte)(gba >> 8);
                }
            }
            return result;
        }

        // ===================================================================
        // #1000: Strip image imports (Mini / Point1 / Point2 / Road).
        //
        // Each strip is a single LZ77 image pointer + a 16-color palette. The
        // import is IMAGE-ONLY: open a PNG, strict-size remap onto the EXISTING
        // strip palette (TryGetStripPalette), then ImageWorldMapCore.ImportIconStrip
        // 4bpp-encodes + LZ77-writes + repoints the single image pointer under one
        // undo scope. The shared palette is NOT written. Mirrors MainImport_Click's
        // CoreState.Services?.ShowError / ShowInfo mechanism. FE8-only + nonzero-
        // pointer gates keep FE6/FE7 disabled (their strip pointers are 0).
        // ===================================================================

        async void MiniImport_Click(object? sender, RoutedEventArgs e)
            => await DoStripImport(
                "World map mini palette is invalid.",
                rom => rom.RomInfo.worldmap_mini_palette_pointer,
                rom => rom.RomInfo.worldmap_mini_image_pointer,
                64, 64,
                "Import World Map Mini",
                "Imported world map mini image.");

        async void Point1Import_Click(object? sender, RoutedEventArgs e)
            => await DoStripImport(
                "World map icon palette is invalid.",
                rom => rom.RomInfo.worldmap_icon_palette_pointer,
                rom => rom.RomInfo.worldmap_icon1_pointer,
                256, 64,
                "Import World Map Point1",
                "Imported world map point1 image.");

        async void Point2Import_Click(object? sender, RoutedEventArgs e)
            => await DoStripImport(
                "World map icon palette is invalid.",
                rom => rom.RomInfo.worldmap_icon_palette_pointer,
                rom => rom.RomInfo.worldmap_icon2_pointer,
                96, 32,
                "Import World Map Point2",
                "Imported world map point2 image.");

        async void RoadImport_Click(object? sender, RoutedEventArgs e)
            => await DoStripImport(
                "World map icon palette is invalid.",
                rom => rom.RomInfo.worldmap_icon_palette_pointer,
                rom => rom.RomInfo.worldmap_road_tile_pointer,
                8, 120,
                "Import World Map Road",
                "Imported world map road image.");

        /// <summary>
        /// Shared driver for the four single-LZ77-stream strip imports. Mirrors
        /// <see cref="MainImport_Click"/>: read the existing strip palette through
        /// the guarded Core helper, strict-size remap the opened PNG onto it, then
        /// <see cref="ImageWorldMapCore.ImportIconStrip"/> (image-only, shared
        /// palette untouched) under one undo scope. Refreshes previews on success.
        /// </summary>
        async System.Threading.Tasks.Task DoStripImport(
            string paletteErrorMessage,
            Func<ROM, uint> palettePointer,
            Func<ROM, uint> imagePointer,
            int widthPx, int heightPx,
            string undoLabel, string successMessage)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                if (!ImageWorldMapCore.TryGetStripPalette(rom, palettePointer(rom), out byte[] palette))
                {
                    CoreState.Services?.ShowError(paletteErrorMessage);
                    return;
                }
                var result = await ImageImportService.LoadAndRemapToExistingPalette(
                    this, widthPx, heightPx, palette, 16, strictSize: true);
                if (result == null) return;
                if (!result.Success) { CoreState.Services?.ShowError(result.Error); return; }

                _undoService.Begin(undoLabel);
                try
                {
                    var r = ImageWorldMapCore.ImportIconStrip(
                        rom, imagePointer(rom), result.IndexedPixels, widthPx, heightPx);
                    if (!r.Success) { _undoService.Rollback(); CoreState.Services?.ShowError(r.Error); return; }
                    _undoService.Commit();
                    RefreshPreviews();
                    CoreState.Services?.ShowInfo(successMessage);
                }
                catch { _undoService.Rollback(); throw; }
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError("Import failed: " + ex.Message);
            }
        }

        // ===================================================================
        // #1064 PR1: Event two-stream image import.
        //
        // The event graphic is TWO LZ77 streams (ZIMAGE deduplicated tiles +
        // ZHEADERTSA header-TSA) + a fixed RAW 64-color / 4-bank palette. Unlike
        // the strip imports (which remap onto a fixed 16-color palette first), the
        // event import auto-reduces the RAW source RGBA to 4 banks via
        // DecreaseColorConvertCore (method-4 "World Map (event)"), so the View
        // passes the loaded RGBA straight to the Core driver (mirrors DoMainImport
        // reading img.GetPixelData()). The source MUST be the 240x160 visible map;
        // the Core reduce adds the 16px right margin to reach the 256x160 canvas.
        // ===================================================================

        async void EventImport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? path = await FileDialogHelper.OpenImageFile(this);
                if (path == null) return;
                await DoEventImport(path);
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapImageView.EventImport_Click failed: {ex}");
            }
        }

        /// <summary>Injectable event-import driver (testable without UI). Loads the
        /// RGBA, then ImageWorldMapCore.ImportEvent (via the VM) auto-reduces +
        /// writes the two LZ77 streams + raw palette under one undo scope. The Core
        /// validates 240x160 + ≤4 banks + one-bank-per-tile + unique-tile ≤1024 and
        /// rejects with NO mutation.</summary>
        public async System.Threading.Tasks.Task DoEventImport(string imagePath)
        {
            IImage? img = CoreState.ImageService?.LoadImage(imagePath);
            if (img == null)
            {
                CoreState.Services?.ShowError(R._("Failed to load image from: {0}", imagePath));
                return;
            }
            try
            {
                int w = img.Width;
                int h = img.Height;
                byte[] rgba = img.GetPixelData();
                if (rgba == null || rgba.Length < (long)w * h * 4)
                {
                    CoreState.Services?.ShowError(R._("Image pixel data is missing or too short."));
                    return;
                }

                _undoService.Begin("Import World Map Event Image");
                try
                {
                    // The Core validates dims (240x160) + reduce-to-256x160 + banks
                    // + unique-tiles and returns a clear message on any rejection
                    // with ZERO ROM mutation.
                    string? err = _vm.ImportEvent(rgba, w, h);
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error($"WorldMapImageView.DoEventImport write failed: {ex}");
                    CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
                    return;
                }
            }
            finally
            {
                img.Dispose();
            }

            // Re-render the event preview from the freshly-written streams.
            RefreshPreviews();
        }

        /// <summary>
        /// Export the dark field map preview as PNG. The cached dark render
        /// (<see cref="_darkFieldMapImage"/>) is saved via a save-file dialog.
        /// Mirrors how GbaImageControl.ExportPng works but for the dark image
        /// that has no dedicated GbaImageControl panel.
        /// </summary>
        async void DarkExport_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                IImage? dark = _darkFieldMapImage ?? _vm.TryRenderDarkFieldMap();
                if (dark == null)
                {
                    CoreState.Services?.ShowError(R._("Dark field map is not available (FE8 only)."));
                    return;
                }
                string? path = await FileDialogHelper.SaveImageFile(this, "worldmap_darkfieldmap");
                if (path == null) return;
                // Convert IImage → WriteableBitmap → save as PNG.
                // FIX C: wrap bmp in using so the WriteableBitmap is disposed after save.
                using var bmp = FEBuilderGBA.Avalonia.Controls.IconBitmapBuilder.FromImage(dark);
                if (bmp == null)
                {
                    CoreState.Services?.ShowError(R._("Failed to convert dark map image to bitmap."));
                    return;
                }
                using var stream = System.IO.File.Create(path);
                bmp.Save(stream);
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapImageView.DarkExport_Click failed: {ex}");
            }
        }

        async void MainExport_Click(object? sender, RoutedEventArgs e)
            => await ExportPreview(MainFieldMapPreview, "worldmap_mainfieldmap");

        async void EventExport_Click(object? sender, RoutedEventArgs e)
            => await ExportPreview(EventPreviewImage, "worldmap_event");

        async void MiniExport_Click(object? sender, RoutedEventArgs e)
            => await ExportPreview(MiniPreviewImage, "worldmap_mini");

        async void Point1Export_Click(object? sender, RoutedEventArgs e)
            => await ExportPreview(Point1PreviewImage, "worldmap_point1");

        async void Point2Export_Click(object? sender, RoutedEventArgs e)
            => await ExportPreview(Point2PreviewImage, "worldmap_point2");

        async void RoadExport_Click(object? sender, RoutedEventArgs e)
            => await ExportPreview(RoadPreviewImage, "worldmap_road");

        async System.Threading.Tasks.Task ExportPreview(GbaImageControl target, string suggestedName)
        {
            try
            {
                await target.ExportPng(this, suggestedName);
            }
            catch (Exception ex)
            {
                // Single interpolated string + full exception (Core Log.Error does
                // not apply {0}/{1} substitution — see RenderInto).
                Log.Error($"WorldMapImageView.ExportPreview({suggestedName}) failed: {ex}");
            }
        }

        // ===================================================================
        // #1013: Decrease Color Tool launchers (Main + Event tabs)
        // ===================================================================

        void MainDecreaseColor_Click(object? sender, RoutedEventArgs e)
        {
            // WF WorldMapImageForm.DecreaseColorTSAToolButton_Click → InitMethod(3) (world map main).
            WindowManager.Instance.Open<DecreaseColorTSAToolView>().InitMethod(3);
        }

        void EventDecreaseColor_Click(object? sender, RoutedEventArgs e)
        {
            // WF DecreaseColorTSAToolForWorldmapEventButton_Click → InitMethod(4) (world map event).
            WindowManager.Instance.Open<DecreaseColorTSAToolView>().InitMethod(4);
        }

        // ===================================================================
        // #1013: Open Source File / Open Source Folder (Main tab)
        // Mirrors ImageBGView.OpenSource_Click / SelectSource_Click. The
        // recorded path lives in CoreState.ResourceCache under the FIXED WF
        // "WorldMap_" key (see WorldMapImageViewModel). Buttons are gated
        // visible only when IsSourceFileAvailable.
        // ===================================================================

        void OpenSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_vm.SourceFilePath))
                {
                    CoreState.Services?.ShowError("Source file is not recorded.");
                    return;
                }
                if (!File.Exists(_vm.SourceFilePath))
                {
                    // The recorded path no longer exists — clear availability so the
                    // buttons hide (IsVisible binding) and report the real cause.
                    _vm.IsSourceFileAvailable = false;
                    CoreState.Services?.ShowError($"Source file not found: {_vm.SourceFilePath}");
                    return;
                }
                var psi = new ProcessStartInfo(_vm.SourceFilePath) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapImageView.OpenSource_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Failed to open source file: {ex.Message}");
            }
        }

        void SelectSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_vm.SourceFilePath))
                {
                    CoreState.Services?.ShowError("Source file is not recorded.");
                    return;
                }
                if (!File.Exists(_vm.SourceFilePath))
                {
                    _vm.IsSourceFileAvailable = false;
                    CoreState.Services?.ShowError($"Source file not found: {_vm.SourceFilePath}");
                    return;
                }
                if (OperatingSystem.IsWindows())
                {
                    var psi = new ProcessStartInfo("explorer.exe",
                        $"/select,\"{_vm.SourceFilePath}\"")
                        { UseShellExecute = true };
                    Process.Start(psi);
                }
                else
                {
                    string? folder = Path.GetDirectoryName(_vm.SourceFilePath);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        var psi = new ProcessStartInfo(folder) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapImageView.SelectSource_Click: {0}", ex.Message);
                CoreState.Services?.ShowError($"Failed to open source folder: {ex.Message}");
            }
        }

        // ===================================================================
        // Undo (single top-level button)
        // ===================================================================

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                CoreState.Undo?.RunUndo();
                LoadAll();
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapImageView.Undo failed: {0}", ex.Message);
            }
        }

        // ===================================================================
        // Legacy surface (kept so existing ListParityHelper / navigation
        // callers continue to compile; defaults to the Border tab).
        // ===================================================================

        public void NavigateTo(uint address) => Border_EntryList.SelectAddress(address);
        public void SelectFirstItem() => Border_EntryList.SelectFirst();

        // ===================================================================
        // Helpers
        // ===================================================================

        static uint NudU32(NumericUpDown nud)
        {
            if (nud.Value is decimal d) return (uint)d;
            return 0u;
        }

        static byte NudByte(NumericUpDown nud)
        {
            if (nud.Value is decimal d) return (byte)((uint)d & 0xFFu);
            return 0;
        }

        static ushort NudU16(NumericUpDown nud)
        {
            if (nud.Value is decimal d) return (ushort)((uint)d & 0xFFFFu);
            return 0;
        }
    }
}
