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
                Log.Error("ImageMagicFEditorView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ImageMagicFEditorView.OnSelected failed: {0}", ex.Message);
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
                Log.Error("ImageMagicFEditorView.RenderPreview: {0}", ex.Message);
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
                Log.Error("ImageMagicFEditorView.Write: {0}", ex.Message);
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
                Log.Error("ImageMagicFEditorView.MagicListExpand: {0}", ex.Message);
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
                Log.Error("ImageMagicFEditorView.ExportPng: {0}", ex.Message);
            }
        }

        // Import is a follow-up (#878 PR2).
        void MagicAnimeImport_Click(object? sender, RoutedEventArgs e)
            => Log.Debug("ImageMagicFEditorView.MagicAnimeImport_Click - Import is a follow-up (#878 PR2)");

        // #878 PR1 — Export Magic Animation (txt + per-frame PNGs).
        // Mirrors WF ImageMagicFEditorForm.MagicAnimeExportButton_Click.
        // Filter 1 = txt with comments, Filter 2 = txt without comments, Filter 3 = GIF (deferred).
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

            // Choose format via SaveFilePicker (txt-with-comments / txt-without / GIF).
            string? filename = await FEBuilderGBA.Avalonia.Dialogs.FileDialogHelper.SaveFile(
                this,
                R._("Save magic animation script"),
                new (string, string)[]
                {
                    (R._("Magic Animation (with comments)"), "*.txt"),
                    (R._("Magic Animation (no comments)"), "*.txt"),
                },
                "magic_" + U.ToHexString((uint)(EntryList.SelectedOriginalIndex + 1)) + ".txt");

            if (string.IsNullOrEmpty(filename)) return;

            // Resolve whether to include comments from the extension/filter heuristic:
            // use enableComment=true by default (user chose first filter or all-files).
            // We can't distinguish filters in Avalonia's StorageProvider API cleanly;
            // use filename suffix heuristic: no-comment if basename ends with "_nc".
            bool enableComment = !System.IO.Path.GetFileNameWithoutExtension(filename)
                .EndsWith("_nc", StringComparison.OrdinalIgnoreCase);

            await ExportMagicAnimationAsync(rom, filename, enableComment);
        }

        async Task ExportMagicAnimationAsync(ROM rom, string filename, bool enableComment)
        {
            try
            {
                uint frameDataAddr = _vm.P0;
                uint objRtoL = _vm.P4;
                uint objBGRtoL = _vm.P12;

                // Scan frame stream.
                List<MagicFrameMeta> frames;
                List<MagicCommandMeta> cmds;
                bool hadContinuation = false;

                // Check for continuation terminator by doing a quick scan.
                // ScanMagicFrames internally detects it; we pass the flag out.
                bool ok = MagicEffectExportCore.ScanMagicFrames(
                    rom, frameDataAddr, objRtoL, objBGRtoL,
                    out frames, out cmds);

                if (!ok && frames.Count == 0)
                {
                    CoreState.Services?.ShowError(
                        R._("Magic animation scan failed — bad frame-data pointer."));
                    return;
                }

                // Detect miss-terminator (0x00 0x01 0x00 0x80) by rescanning.
                // We peek at the raw data to see if [n+1]==0x01 at the first 0x80 hit.
                hadContinuation = DetectMissContinuation(rom, frameDataAddr);

                // Build script lines.
                string basename = System.IO.Path.GetFileNameWithoutExtension(filename) + "_";
                List<int> objIndices, bgIndices;
                var scriptLines = MagicEffectExportCore.ExportMagicScript(
                    rom, frames, cmds, basename, enableComment,
                    hadContinuation,
                    out objIndices, out bgIndices);

                string basedir = System.IO.Path.GetDirectoryName(filename) ?? ".";

                // Render and save unique OBJ frames.
                int objSlotCount = MagicEffectExportCore.CountUniqueObjSlots(frames);
                for (int s = 0; s < objSlotCount; s++)
                {
                    IImage? img = MagicEffectExportCore.RenderObjFrameSlot(
                        rom, frames, s, objRtoL, objBGRtoL);
                    if (img != null)
                    {
                        string pngPath = System.IO.Path.Combine(
                            basedir, basename + "o_" + s.ToString("000") + ".png");
                        try { img.Save(pngPath); }
                        catch (Exception ex)
                        {
                            Log.Error("MagicExport: save OBJ slot " + s + ": " + ex.Message);
                        }
                    }
                    else
                    {
                        // Broken frame — save a placeholder.
                        string pngPath = System.IO.Path.Combine(
                            basedir, basename + "o_" + s.ToString("000") + ".png");
                        SaveDummyPng(pngPath,
                            MagicEffectExportCore.OBJ_EXPORT_WIDTH,
                            MagicEffectExportCore.OBJ_EXPORT_HEIGHT);
                    }
                }

                // Render and save unique BG frames.
                int bgSlotCount = MagicEffectExportCore.CountUniqueBgSlots(frames);
                for (int s = 0; s < bgSlotCount; s++)
                {
                    IImage? img = MagicEffectExportCore.RenderBgFrameSlot(rom, frames, s);
                    if (img != null)
                    {
                        string pngPath = System.IO.Path.Combine(
                            basedir, basename + "b_" + s.ToString("000") + ".png");
                        try { img.Save(pngPath); }
                        catch (Exception ex)
                        {
                            Log.Error("MagicExport: save BG slot " + s + ": " + ex.Message);
                        }
                    }
                    else
                    {
                        string pngPath = System.IO.Path.Combine(
                            basedir, basename + "b_" + s.ToString("000") + ".png");
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
                Log.Error("MagicAnimeExport: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Export failed: {0}", ex.Message));
            }
        }

        // Detect whether a miss-terminator (0x00 0x01 0x00 0x80) is present
        // before the first frame in the stream. Mirrors WF Export() termCount==1 branch.
        static bool DetectMissContinuation(ROM rom, uint frameDataAddr)
        {
            if (rom == null || rom.Data == null) return false;
            uint offset = U.isSafetyPointer(frameDataAddr)
                ? U.toOffset(frameDataAddr) : frameDataAddr;
            if (!U.isSafetyOffset(offset, rom)) return false;

            uint limiter = offset + 1024 * 1024;
            if (limiter > (uint)rom.Data.Length) limiter = (uint)rom.Data.Length;

            for (uint n = offset; n < limiter; n += 4)
            {
                if (n + 4 > (uint)rom.Data.Length) break;
                byte cmd = rom.Data[n + 3];
                if (cmd == 0x80)
                {
                    return n + 2 <= (uint)rom.Data.Length && rom.Data[n + 1] == 0x01;
                }
                if (cmd == 0x86) return false; // no continuation before first frame
                if (cmd == 0x85) continue;
                break;
            }
            return false;
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
            catch (Exception ex) { Log.Error("SaveDummyPng: {0}", ex.Message); }
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
                    Log.Error("ImageMagicFEditorView.OpenSource: {0}", ex.Message);
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
                    Log.Error("ImageMagicFEditorView.SelectSource: {0}", ex.Message);
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
            // Real cross-editor jump - opens ToolAnimationCreator. The
            // Avalonia ToolAnimationCreatorView exists but its Init()
            // flow for MagicAnime_FEEDitor isn't wired yet (#500). The
            // open call still surfaces the editor so users discover it.
            try
            {
                WindowManager.Instance.Open<ToolAnimationCreatorView>();
            }
            catch (Exception ex)
            {
                Log.Error("ImageMagicFEditorView.JumpEditor: {0}", ex.Message);
            }
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
                Log.Error("ImageMagicFEditorView.LinkInternet: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
