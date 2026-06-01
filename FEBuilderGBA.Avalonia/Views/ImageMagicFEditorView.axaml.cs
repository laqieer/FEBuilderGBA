// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia counterpart of WinForms ImageMagicFEditorForm. Gap-sweep
// fix (#418) - rebuilds this view from a 3-control stub into a full
// editor surface mirroring the WF panel3 / panel5 / panel8 /
// DragTargetPanel layout.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
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

        // Deferred file/shell actions - tracked by #500.
        void MagicAnimeImport_Click(object? sender, RoutedEventArgs e)
            => Log.Debug("ImageMagicFEditorView.MagicAnimeImport_Click - disabled until #500 lands");

        void MagicAnimeExport_Click(object? sender, RoutedEventArgs e)
            => Log.Debug("ImageMagicFEditorView.MagicAnimeExport_Click - disabled until #500 lands");

        void OpenSource_Click(object? sender, RoutedEventArgs e)
            => Log.Debug("ImageMagicFEditorView.OpenSource_Click - disabled until #500 lands");

        void SelectSource_Click(object? sender, RoutedEventArgs e)
            => Log.Debug("ImageMagicFEditorView.SelectSource_Click - disabled until #500 lands");

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
