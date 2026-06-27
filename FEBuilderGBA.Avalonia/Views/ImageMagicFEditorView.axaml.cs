// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia counterpart of WinForms ImageMagicFEditorForm. Gap-sweep
// fix (#418) - rebuilds this view from a 3-control stub into a full
// editor surface mirroring the WF panel3 / panel5 / panel8 /
// DragTargetPanel layout.
// #878 PR1: Export + OpenSource/SelectSource wired. Import is PR2.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageMagicFEditorView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageMagicFEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        // Window title shown in editor docks + accessed by MCP /
        // UIAutomation screenshot tooling.
        public string ViewTitle => "Magic Effect Editor (FEditor)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public ImageMagicFEditorView()
        {
            InitializeComponent();
            // Populate ComboBox items via R._() so ja/zh translations
            // apply (ViewTranslationHelper doesn't translate
            // ComboBoxItem.Content). Copilot bot review #3/#4 on PR #554.
            ZoomCombo.ItemsSource = new[]
            {
                R._("Zoom and Draw"),
                R._("Draw without enlarging"),
            };
            ZoomCombo.SelectedIndex = 0;
            DimCombo.ItemsSource = new[]
            {
                R._("dim_pc"),
                R._("dim"),
                R._("NULL (EMPTY)"),
            };
            DimCombo.SelectedIndex = 0;
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                // #649: display via the unified EditorTopBar read-only slots.
                TopBar.StartAddressText = _vm.ReadStartAddress.ToString();
                TopBar.ReadCountText = _vm.ReadCount.ToString();
                UpdatePatchNotice();
                UpdateListExpandVisibility();
                UpdateWriteControlsEnabled();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicFEditorView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        /// <summary>
        /// When the FEditor/SCA_Creator patch is absent the editor must
        /// not let the user trigger a Write — the dim/no-dim addresses
        /// resolve to U.NOT_FOUND and the row pointer would be corrupted.
        /// Mirrors the WF behavior of bailing out of `WriteDim()` when
        /// the patch isn't detected (Copilot bot review on PR #554).
        /// </summary>
        void UpdateWriteControlsEnabled()
        {
            bool ok = _vm.MagicSystemDetected;
            WriteButton.IsEnabled = ok;
            DimCombo.IsEnabled = ok;
            CommentBox.IsEnabled = ok;
            P0Box.IsEnabled = ok;
            P4Box.IsEnabled = ok;
            P8Box.IsEnabled = ok;
            P12Box.IsEnabled = ok;
            P16Box.IsEnabled = ok;
            FrameBox.IsEnabled = ok;
            // MagicListExpandButton enablement (#837): the expand is only
            // meaningful when the FEditor/CSA magic system is present (the Core
            // helper aborts on a NOT_FOUND CSA pointer anyway). Visibility is
            // driven separately by UpdateListExpandVisibility (hidden once the
            // table is already expanded — mirrors WF MagicListExpandsButton).
            MagicListExpandButton.IsEnabled = ok;
            // #881 Import button is gated on magic-system detection (mirrors WF
            // MagicAnimeImportDirect guard). When no patch is detected the button
            // is grayed out; clicking also shows an error via the FE-gate check in
            // MagicEffectImportCore.ImportMagicScript.
            MagicAnimeImportButton.IsEnabled = ok;
        }

        void UpdatePatchNotice()
        {
            if (_vm.MagicSystemDetected)
            {
                PatchNoticeLabel.Text = "";
            }
            else
            {
                // Wrap in R._() so ja/zh translations apply
                // (Copilot bot review #4 on PR #554).
                PatchNoticeLabel.Text = R._("FEditor / SCA_Creator magic-system patch not detected. Install the patch via the Patches manager.");
            }
        }

        void UpdateListExpandVisibility()
        {
            // The List Expansion button is only useful while the
            // spell-data table is at its original per-version size.
            // Mirrors WF `MagicListExpandsButton.Enabled` check.
            MagicListExpandButton.IsVisible = !_vm.IsListExpanded;
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e) => LoadList();

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                // AddressListControl.SelectedAddressChanged fires with the
                // AddrResult.addr value (CSA entry). The pointer-table slot
                // lives in AddrResult.tag; we need both to drive the VM
                // correctly (dim pointer goes to tag, comment + P0..P16 go
                // to addr). Mirrors WF AddressList_SelectedIndexChanged.
                AddrResult? selected = EntryList.SelectedItem;
                if (selected == null)
                {
                    // Copilot bot review #1 on PR #554 — without an
                    // AddrResult we have no pointer-slot address; bail
                    // out so a stray Write_Click can't corrupt ROM.
                    return;
                }
                _vm.LoadEntry(selected.addr, selected.tag);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicFEditorView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            // WF semantics:
            //   - Address NumericUpDown shows the CSA entry address (ar.addr).
            //   - Selected Address textbox shows the pointer-table slot (ar.tag).
            // Both diverge once selection plumbing is correct (Copilot bot
            // review on PR #554).
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AddressBox.Value = _vm.CurrentAddr;
            SelectedAddressBox.Text = string.Format("0x{0:X08}", _vm.PointerSlotAddr);
            CommentBox.Text = _vm.Comment ?? string.Empty;
            DimCombo.SelectedIndex = (int)_vm.DimPointer;
            P0Box.Value = _vm.P0;
            P4Box.Value = _vm.P4;
            P8Box.Value = _vm.P8;
            P12Box.Value = _vm.P12;
            P16Box.Value = _vm.P16;
            FrameBox.Value = _vm.Frame;
            RenderPreview();
            UpdateSourceButtonVisibility();
            UpdateExportButtonEnabled();
        }

        // #878 PR1 — Source-button show/hide (mirrors WF
        // AddressList_SelectedIndexChanged ~lines 169-179).
        // Buttons are visible only when a cached source filename exists for
        // the currently selected slot and the file is on disk.
        void UpdateSourceButtonVisibility()
        {
            int idx = EntryList.SelectedOriginalIndex;
            if (idx < 0)
            {
                OpenSourceButton.IsVisible = false;
                SelectSourceButton.IsVisible = false;
                return;
            }
            // WF key: "MagicAnimation_" + U.ToHexString(selectedIndex+1)
            uint id = (uint)(idx + 1);
            string key = "MagicAnimation_" + U.ToHexString(id);
            bool hasFile = CoreState.ResourceCache is EtcCacheResource rcache
                && rcache.TryGetValue(key, out string? path)
                && !string.IsNullOrEmpty(path)
                && File.Exists(path);
            OpenSourceButton.IsVisible = hasFile;
            SelectSourceButton.IsVisible = hasFile;
        }

        // Export button is enabled when magic system is detected and an entry is selected.
        void UpdateExportButtonEnabled()
        {
            MagicAnimeExportButton.IsEnabled =
                _vm.MagicSystemDetected && EntryList.SelectedOriginalIndex >= 0;
        }

        /// <summary>
        /// Render the current magic-effect frame and update the preview control.
        /// Mirrors WF <c>DrawSelectedAnime()</c>.
        /// </summary>
        void RenderPreview()
        {
            try
            {
                string log;
                var image = _vm.RenderMagicFramePreview(out log);
                MagicFramePreview.SetImage(image);
                BinInfo.Text = log;
                ExportPngButton.IsEnabled = _vm.CanExportMagicFrame && MagicFramePreview.HasImage;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicFEditorView.RenderPreview: {0}", ex.Message);
                MagicFramePreview.SetImage(null);
                ExportPngButton.IsEnabled = false;
            }
        }

        void DimCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // The combo's SelectedIndex == 0/1/2 mapping matches
            // DimPointerKind { DimPc=0, Dim=1, Empty=2 }. Use sender
            // because the AXAML parser fires the event during EndInit
            // BEFORE the AvaloniaNameSourceGenerator wires the named
            // `DimCombo` field, so `DimCombo` would NPE here. The
            // `_vm` guard tolerates the same EndInit-time invocation.
            if (_vm == null) return;
            if (sender is not ComboBox combo) return;
            int idx = combo.SelectedIndex;
            if (idx < 0) return;
            _vm.DimPointer = (ImageMagicFEditorViewModel.DimPointerKind)idx;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0u) return;
            // Patch-absence guard: WF bails out of WriteDim() when the
            // FEditor/SCA_Creator patch isn't detected; mirror that to
            // avoid silently writing 0xFFFFFFFF as the dim pointer.
            if (!_vm.MagicSystemDetected) return;
            uint reloadCsa = _vm.CurrentAddr;
            uint reloadSlot = _vm.PointerSlotAddr;
            _undoService.Begin("Edit Magic Effect (FEditor)");
            try
            {
                // Pull the editable fields back from the controls so
                // user edits land on the VM before persistence.
                _vm.Comment = CommentBox.Text ?? string.Empty;
                int dimIdx = DimCombo.SelectedIndex;
                if (dimIdx >= 0)
                    _vm.DimPointer = (ImageMagicFEditorViewModel.DimPointerKind)dimIdx;
                _vm.P0 = (uint)(P0Box.Value ?? 0);
                _vm.P4 = (uint)(P4Box.Value ?? 0);
                _vm.P8 = (uint)(P8Box.Value ?? 0);
                _vm.P12 = (uint)(P12Box.Value ?? 0);
                _vm.P16 = (uint)(P16Box.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();

                // Reload the entry list AND the current entry so the
                // listing reflects the new state (e.g. switching from
                // EMPTY to dim adds/removes the row from the listing
                // via LoadList's filter pass), then re-display the
                // freshly-saved row. (Copilot CLI re-review on PR #554
                // noted that reloading only the entry didn't refresh
                // the list as the original comment claimed.)
                _vm.IsLoading = true;
                try
                {
                    EntryList.SetItems(_vm.LoadList());
                    // Re-select the row we just wrote so the entry list
                    // selection stays in sync with the detail panel.
                    // SelectAddress raises SelectedAddressChanged which
                    // calls OnSelected → LoadEntry; no need for the
                    // direct call (Copilot bot review on PR #554).
                    EntryList.SelectAddress(reloadCsa);
                    UpdateUI();
                }
                finally { _vm.IsLoading = false; _vm.MarkClean(); }

                CoreState.Services?.ShowInfo("Magic effect written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ImageMagicFEditorView.Write: {0}", ex.Message);
            }
        }

        void MagicListExpand_Click(object? sender, RoutedEventArgs e)
        {
            // #837 — grow the magic-effect (table-1, entrySize 4) AND the CSA
            // spell table (table-2, entrySize 20) to a fixed 254 rows via the
            // all-reference path (DataExpansionCore.ExpandTableTo +
            // RepointAllReferences). The CSA-pointer NOT_FOUND clean-abort runs
            // FIRST inside the Core helper (before the table-1 expand). Mirrors
            // WF ImageMagicFEditorForm.MagicListExpandsButton_Click.

            // Confirmation (mirrors WF R.ShowYesNo("魔法テーブルを拡張...")).
            if (CoreState.Services?.ShowYesNo(
                    R._("Expand the magic table? This grows the list to 254 entries.")) != true)
                return;

            // Remember the current selection so we can restore it after refresh.
            uint reloadCsa = _vm.CurrentAddr;

            _undoService.Begin("Magic List Expansion");
            try
            {
                string err = _vm.ExpandMagicLists(_undoService.GetActiveUndoData());
                if (!string.IsNullOrEmpty(err))
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(err);
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();

                // NOTE B: refresh the list from the grown table. The magic
                // LoadList scan uses isPointerOrNULL and stops at the
                // 0xFFFFFFFF terminator ExpandTableTo wrote, so it reports the
                // grown count correctly (no re-scan undercount). Reselect the
                // previously-selected row + hide the now-spent expand button.
                _vm.IsLoading = true;
                try
                {
                    EntryList.SetItems(_vm.LoadList());
                    if (reloadCsa != 0u) EntryList.SelectAddress(reloadCsa);
                    UpdateListExpandVisibility();
                }
                finally { _vm.IsLoading = false; _vm.MarkClean(); }

                CoreState.Services?.ShowInfo(R._("Expanded magic list to 254 entries."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ImageMagicFEditorView.MagicListExpand: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("List expansion failed: {0}", ex.Message));
            }
        }

        // #852 — frame spinner drives live preview re-render.
        void FrameBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm == null) return;
            _vm.Frame = (uint)(FrameBox.Value ?? 0);
            RenderPreview();
        }

        // #852 — Export PNG read-only button.
        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            if (!MagicFramePreview.HasImage) return;
            try
            {
                await MagicFramePreview.ExportPng(this, "magic-effect.png");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicFEditorView.ExportPng: {0}", ex.Message);
            }
        }

        // #881 — Import Magic Animation (FEditor .txt script + per-frame PNGs → ROM write).
        // Mirrors WF ImageUtilMagicFEditor.Import + ImageMagicFEditorForm.MagicAnimeImportDirect.
        async void MagicAnimeImport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.MagicSystemDetected)
            {
                CoreState.Services?.ShowError(
                    R._("FEditor / SCA_Creator magic-system patch not detected."));
                return;
            }

            ROM? rom = CoreState.ROM;
            if (rom == null) return;

            // Make sure an entry is selected.
            uint magicBaseAddr = _vm.CurrentAddr;
            if (magicBaseAddr == 0u)
            {
                CoreState.Services?.ShowError(R._("No magic-animation entry selected."));
                return;
            }

            // File dialog: pick .txt script.
            // #1639: the script loads sibling PNG frames from its own directory,
            // so require a real local path; a SAF pick (no local path) cannot
            // resolve siblings → message on Android, never silent.
            string txtPath = await FEBuilderGBA.Avalonia.Dialogs.FileDialogHelper.OpenFile(
                this,
                R._("Import magic animation script"),
                "*.txt", requireLocalPath: true);

            if (string.IsNullOrEmpty(txtPath))
            {
                if (OperatingSystem.IsAndroid())
                    CoreState.Services?.ShowError(R._("Importing a magic animation script reads sibling PNG frames and requires desktop file-system access; it is not available on this device."));
                return;
            }

            await DoImport(
                txtPath,
                filename => LoadIndexedImage(filename, txtPath));
        }

        /// <summary>
        /// Injectable import entry point (used by tests to skip file-dialog and inject
        /// indexed images directly).
        /// </summary>
        internal async Task<string> DoImport(
            string txtPath,
            Func<string, (byte[] indexedPixels, int w, int h, byte[] gbaPalette)?> pngLoader)
        {
            ROM? rom = CoreState.ROM;
            if (rom == null) return "ROM is null";

            uint magicBaseAddr = _vm.CurrentAddr;
            if (magicBaseAddr == 0u) return "No magic-animation entry selected.";

            // Parse script.
            string[] scriptLines;
            try
            {
                scriptLines = File.ReadAllLines(txtPath, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                string err = R._("Cannot read script file: {0}", ex.Message);
                CoreState.Services?.ShowError(err);
                return err;
            }

            var cmds = MagicEffectImportCore.ParseMagicScript(scriptLines);
            if (cmds.Count == 0)
            {
                string err = R._("Script file is empty or contains no recognized commands.");
                CoreState.Services?.ShowError(err);
                return err;
            }

            // Snapshot ROM for rollback on failure (defensive — Core validate-before-mutate
            // should prevent partial writes, but keep a byte-level snapshot as a safety net).
            byte[] snapshot = (byte[])rom.Data.Clone();

            // ONE ambient undo scope covers all writes atomically.
            _undoService.Begin("Import Magic Animation (FEditor)");
            string importErr;
            try
            {
                importErr = MagicEffectImportCore.ImportMagicScript(
                    rom, magicBaseAddr, cmds, pngLoader);
            }
            catch (Exception ex)
            {
                importErr = ex.Message;
            }

            if (!string.IsNullOrEmpty(importErr))
            {
                // Restore ROM from snapshot before rolling back undo (defensive).
                Array.Copy(snapshot, rom.Data, snapshot.Length);
                _undoService.Rollback();
                CoreState.Services?.ShowError(R._("Magic animation import failed: {0}", importErr));
                return importErr;
            }

            _undoService.Commit();

            // Update resource cache so OpenSource/SelectSource buttons appear.
            int idx = EntryList.SelectedOriginalIndex;
            if (idx >= 0 && CoreState.ResourceCache is EtcCacheResource rcache)
            {
                string key = "MagicAnimation_" + U.ToHexString((uint)(idx + 1));
                rcache.Update(key, txtPath);
                UpdateSourceButtonVisibility();
            }

            // Refresh preview to show newly imported data.
            _vm.IsLoading = true;
            try
            {
                // Re-read the current entry's fields from ROM (pointers changed).
                AddrResult? sel = EntryList.SelectedItem;
                if (sel != null) _vm.LoadEntry(sel.addr, sel.tag);
                UpdateUI();
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }

            CoreState.Services?.ShowInfo(R._("Magic animation imported successfully."));
            return string.Empty;
        }

        /// <summary>
        /// Load a PNG referenced by the script: resolves relative filenames against
        /// the script's directory, loads as RGBA, quantizes to 16 colors, returns
        /// indexed pixels + GBA palette. Returns null on any failure.
        /// </summary>
        static (byte[] indexedPixels, int w, int h, byte[] gbaPalette)? LoadIndexedImage(
            string filename, string scriptTxtPath)
        {
            // Resolve relative path against script directory.
            string dir = System.IO.Path.GetDirectoryName(scriptTxtPath) ?? ".";
            string fullPath = System.IO.Path.IsPathRooted(filename)
                ? filename
                : System.IO.Path.Combine(dir, filename);

            try
            {
                // Use zero expected dimensions (accept any size; validation done in Core).
                var lr = FEBuilderGBA.Avalonia.Services.ImageImportService.LoadAndQuantizeFromFile(
                    fullPath, 0, 0, maxColors: 16, strictSize: false);
                if (lr == null || !lr.Success || lr.IndexedPixels == null)
                    return null;
                return (lr.IndexedPixels, lr.Width, lr.Height, lr.GBAPalette);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicFEditorView.LoadIndexedImage: {0}", ex.Message);
                return null;
            }
        }

        // #878 PR1 — Export Magic Animation (txt + per-frame PNGs).
        // Mirrors WF ImageMagicFEditorForm.MagicAnimeExportButton_Click.
        // Filter 0 = txt with comments, Filter 1 = txt without comments (FIX 4).
        async void MagicAnimeExport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.MagicSystemDetected)
            {
                CoreState.Services?.ShowError(
                    R._("FEditor / SCA_Creator magic-system patch not detected."));
                return;
            }

            ROM? rom = CoreState.ROM;
            if (rom == null) return;

            // FIX 4: use SaveFileWithFilterIndex so enableComment is driven by the
            // chosen filter index (0 = with comments, 1 = no comments), not the
            // filename heuristic. The _nc suffix heuristic is removed.
            var (filename, filterIndex) = await FEBuilderGBA.Avalonia.Dialogs.FileDialogHelper.SaveFileWithFilterIndex(
                this,
                R._("Save magic animation script"),
                new (string, string)[]
                {
                    (R._("Magic Animation (with comments)"), "*.txt"),
                    (R._("Magic Animation (no comments)"), "*.txt"),
                },
                "magic_" + U.ToHexString((uint)(EntryList.SelectedOriginalIndex + 1)) + ".txt");

            // #1639: ExportMagicAnimationAsync writes sibling PNG frames next to
            // the script, so require a real local path; a SAF pick (no local
            // path) cannot place siblings → message on Android, never silent.
            if (string.IsNullOrEmpty(filename))
            {
                if (OperatingSystem.IsAndroid())
                    CoreState.Services?.ShowError(R._("Exporting a magic animation script writes sibling PNG frames and requires desktop file-system access; it is not available on this device."));
                return;
            }

            // filterIndex 0 = with comments, 1+ = without (FIX 4).
            bool enableComment = (filterIndex == 0);

            await ExportMagicAnimationAsync(rom, filename, enableComment);
        }

        async Task ExportMagicAnimationAsync(ROM rom, string filename, bool enableComment)
        {
            try
            {
                uint frameDataAddr = _vm.P0;
                uint objRtoL = _vm.P4;
                uint objBGRtoL = _vm.P12;

                // FIX 3: Single ordered walk via ExportMagicScriptLines — replaces
                // the split ScanMagicFrames + ExportMagicScript + DetectMissContinuation
                // trio (DetectMissContinuation is removed entirely).
                // FIX 1+2: Shared anime-hash for OBJ+BG, inline ~~~ emission.
                string basename = System.IO.Path.GetFileNameWithoutExtension(filename) + "_";
                List<int> sharedObjSlots, sharedBgSlots;
                List<MagicFrameMeta> frames;
                var scriptLines = MagicEffectExportCore.ExportMagicScriptLines(
                    rom, frameDataAddr, basename, enableComment,
                    out sharedObjSlots, out sharedBgSlots, out frames);

                if (frames.Count == 0 && scriptLines.Count <= 2)
                {
                    // Only Start/End markers — bad frame-data pointer.
                    CoreState.Services?.ShowError(
                        R._("Magic animation scan failed — bad frame-data pointer."));
                    return;
                }

                string basedir = System.IO.Path.GetDirectoryName(filename) ?? ".";

                // Render and save unique OBJ frames (shared-index filenames — FIX 1).
                int objSlotCount = MagicEffectExportCore.CountUniqueObjSlots(frames);
                for (int s = 0; s < objSlotCount; s++)
                {
                    IImage? img = MagicEffectExportCore.RenderObjFrameSlot(
                        rom, frames, s, objRtoL, objBGRtoL);
                    string pngPath = System.IO.Path.Combine(
                        basedir, basename + "o_" + s.ToString("000") + ".png");
                    if (img != null)
                    {
                        try { img.Save(pngPath); }
                        catch (Exception ex)
                        {
                            Log.Error("MagicExport: save OBJ slot " + s + ": " + ex.Message);
                        }
                    }
                    else
                    {
                        SaveDummyPng(pngPath,
                            MagicEffectExportCore.OBJ_EXPORT_WIDTH,
                            MagicEffectExportCore.OBJ_EXPORT_HEIGHT);
                    }
                }

                // Render and save unique BG frames (shared-index filenames — FIX 1).
                int bgSlotCount = MagicEffectExportCore.CountUniqueBgSlots(frames);
                for (int s = 0; s < bgSlotCount; s++)
                {
                    IImage? img = MagicEffectExportCore.RenderBgFrameSlot(rom, frames, s);
                    string pngPath = System.IO.Path.Combine(
                        basedir, basename + "b_" + s.ToString("000") + ".png");
                    if (img != null)
                    {
                        try { img.Save(pngPath); }
                        catch (Exception ex)
                        {
                            Log.Error("MagicExport: save BG slot " + s + ": " + ex.Message);
                        }
                    }
                    else
                    {
                        SaveDummyPng(pngPath,
                            MagicEffectExportCore.BG_EXPORT_WIDTH,
                            MagicEffectExportCore.BG_EXPORT_HEIGHT);
                    }
                }

                // Write the .txt script.
                var textLines = new List<string>(scriptLines.Count);
                foreach (var line in scriptLines)
                    textLines.Add(line.Text);
                File.WriteAllLines(filename, textLines, System.Text.Encoding.UTF8);

                // Update resource cache so OpenSource/SelectSource buttons appear.
                int idx = EntryList.SelectedOriginalIndex;
                if (idx >= 0 && CoreState.ResourceCache is EtcCacheResource rcache)
                {
                    string key = "MagicAnimation_" + U.ToHexString((uint)(idx + 1));
                    rcache.Update(key, filename);
                    UpdateSourceButtonVisibility();
                }

                // Reveal file in explorer (mirrors WF U.SelectFileByExplorer).
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = System.IO.Path.GetDirectoryName(filename),
                        UseShellExecute = true,
                    });
                }
                catch { /* best-effort */ }

                CoreState.Services?.ShowInfo(
                    R._("Exported {0} OBJ + {1} BG frames to {2}",
                        objSlotCount, bgSlotCount, filename));
            }
            catch (Exception ex)
            {
                Log.ErrorF("MagicAnimeExport: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Export failed: {0}", ex.Message));
            }
        }

        // Save a minimal transparent PNG placeholder.
        static void SaveDummyPng(string path, int width, int height)
        {
            try
            {
                IImageService? svc = CoreState.ImageService;
                if (svc == null) return;
                var img = svc.CreateImage(width, height);
                img.SetPixelData(new byte[width * height * 4]);
                img.Save(path);
            }
            catch (Exception ex) { Log.ErrorF("SaveDummyPng: {0}", ex.Message); }
        }

        // #878 PR1 — Open Source File (mirrors WF OpenSourceButton_Click).
        void OpenSource_Click(object? sender, RoutedEventArgs e)
        {
            int idx = EntryList.SelectedOriginalIndex;
            if (idx < 0) return;
            string key = "MagicAnimation_" + U.ToHexString((uint)(idx + 1));
            if (CoreState.ResourceCache is EtcCacheResource cache
                && cache.TryGetValue(key, out string? path)
                && !string.IsNullOrEmpty(path)
                && File.Exists(path))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    Log.ErrorF("ImageMagicFEditorView.OpenSource: {0}", ex.Message);
                    CoreState.Services?.ShowError(
                        R._("Cannot open file: {0}", ex.Message));
                }
            }
            else
            {
                CoreState.Services?.ShowError(R._("Source file not recorded or not found."));
            }
        }

        // #878 PR1 — Open Source Folder / reveal in file manager
        // (mirrors WF SelectSourceButton_Click).
        void SelectSource_Click(object? sender, RoutedEventArgs e)
        {
            int idx = EntryList.SelectedOriginalIndex;
            if (idx < 0) return;
            string key = "MagicAnimation_" + U.ToHexString((uint)(idx + 1));
            if (CoreState.ResourceCache is EtcCacheResource cache
                && cache.TryGetValue(key, out string? path)
                && !string.IsNullOrEmpty(path)
                && File.Exists(path))
            {
                try
                {
                    // Reveal in file manager: open the containing directory.
                    string? dir = System.IO.Path.GetDirectoryName(path);
                    if (dir != null)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dir,
                            UseShellExecute = true,
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorF("ImageMagicFEditorView.SelectSource: {0}", ex.Message);
                    CoreState.Services?.ShowError(
                        R._("Cannot open folder: {0}", ex.Message));
                }
            }
            else
            {
                CoreState.Services?.ShowError(R._("Source file not recorded or not found."));
            }
        }

        void JumpEditor_Click(object? sender, RoutedEventArgs e)
        {
            // #996: seed the Animation Creator from the SELECTED magic entry's
            // 0x86 frame-data stream (FEditor 28-byte frame format) instead of
            // opening it blank.
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;
                if (!_vm.MagicSystemDetected)
                {
                    CoreState.Services?.ShowInfo(R._("Magic system not detected."));
                    return;
                }
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0)
                {
                    CoreState.Services?.ShowInfo(R._("No magic-animation entry selected."));
                    return;
                }
                uint id = (uint)(idx + 1);
                uint frameDataAddr = _vm.P0;
                uint off = U.toOffset(frameDataAddr);
                if (!U.isSafetyOffset(off, rom))
                {
                    CoreState.Services?.ShowInfo(R._("Frame-data pointer 0x{0:X} is outside the ROM.", frameDataAddr));
                    return;
                }
                // Probe FIRST — do NOT open a blank Creator on an empty/terminator
                // stream (#1116). Only open once frames are confirmed present.
                int frameCount = ToolAnimationCreatorViewViewModel.CountMagicFrames(frameDataAddr, isCsa: false);
                if (frameCount <= 0)
                {
                    CoreState.Services?.ShowInfo(R._("No magic frames found at 0x{0:X}.", frameDataAddr));
                    return;
                }
                string hint = R._("Magic Animation (FEditor) #{0:X2}", id);
                var view = WindowManager.Instance.Open<ToolAnimationCreatorView>();
                view.InitFromMagicRom(AnimationTypeEnum.MagicAnime_FEEDitor, id, hint, frameDataAddr, isCsa: false);
            }
            // Core Log.Error is params string[] (string.Join, NO composite
            // formatting) — a literal "{0}" would be logged verbatim, so use a
            // single interpolated string with the full exception (#969 precedent).
            catch (Exception ex) { Log.Error($"ImageMagicFEditorView.JumpEditor: {ex}"); }
        }

        void LinkInternet_Click(object? sender, PointerPressedEventArgs e)
        {
            // Opens the "Find new resources on the Internet" wiki page,
            // mirroring WF MainFormUtil.GotoMoreData().
            try
            {
                const string url = "https://github.com/laqieer/FEBuilderGBA/wiki/MoreData";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicFEditorView.LinkInternet: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
