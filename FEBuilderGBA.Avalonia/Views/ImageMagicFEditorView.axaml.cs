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
                ReadStartAddressBox.Value = _vm.ReadStartAddress;
                ReadCountBox.Value = _vm.ReadCount;
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
            // MagicListExpandButton is intentionally NOT re-enabled here -
            // the underlying ExpandsArea call is WF-coupled and tracked by
            // #500 (Copilot CLI review on PR #554 #4). The AXAML keeps
            // IsEnabled="False" + ToolTip.Tip referencing #500.
        }

        void UpdatePatchNotice()
        {
            if (_vm.MagicSystemDetected)
            {
                PatchNoticeLabel.Text = "";
            }
            else
            {
                PatchNoticeLabel.Text = "FEditor / SCA_Creator magic-system patch not detected. Install the patch via the Patches manager.";
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
                uint csaEntry = selected?.addr ?? addr;
                uint pointerSlot = selected?.tag ?? addr;
                _vm.LoadEntry(csaEntry, pointerSlot);
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

                // Reload so the entry list / patch notice reflect the
                // new state (e.g. switching from EMPTY to dim moves the
                // row in/out of the listing).
                _vm.IsLoading = true;
                try
                {
                    _vm.LoadEntry(reloadCsa, reloadSlot);
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
            _undoService.Begin("Magic List Expansion");
            try
            {
                // Real spell-data + CSA-table expansion is WF-coupled
                // (InputFormRef.ExpandsArea) - tracked at #500. Logging
                // the request keeps the undo scope wired so a later
                // Core extraction can drop in without retrofitting.
                Log.Debug("ImageMagicFEditorView.MagicListExpand_Click invoked - deferred until #500 lands");
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ImageMagicFEditorView.MagicListExpand: {0}", ex.Message);
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
