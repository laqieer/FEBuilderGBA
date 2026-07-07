using global::Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventMapChangeView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventMapChangeViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        readonly ObservableCollection<string> _mapDisplayItems = new();
        List<AddrResult> _mapItems = new();

        public string ViewTitle => "Map Change Event Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Map Change Event Editor", 1280, 760, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public EventMapChangeView()
        {
            InitializeComponent();
            // #857: DataContext needed for the CanExportChange binding on the
            // Export PNG button (mirrors WorldMapImageView / ImageTSAEditorView).
            DataContext = _vm;
            MapListBox.ItemsSource = _mapDisplayItems;
            MapListBox.SelectionChanged += MapListBox_SelectionChanged;

            EntryList.SelectedAddressChanged += OnEntrySelected;
            CommentBox.LostFocus += (_, _) => _vm.SaveComment(CommentBox.Text ?? string.Empty);

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

        void LoadList()
        {
            try
            {
                // Map list (left column).
                _mapItems = _vm.LoadMapList();
                _mapDisplayItems.Clear();
                foreach (var m in _mapItems)
                    _mapDisplayItems.Add(m.name);

                // Legacy entry list (right column) — same callbacks as before.
                var entries = _vm.LoadList();
                EntryList.SetItems(entries);

                if (TopBar != null)
                {
                    TopBar.StartAddressText = $"0x{_vm.ReadStartAddress:X08}";
                    TopBar.ReadCountText = _vm.ReadCount.ToString();
                }
                BlockSizeBox.Text = $"0x{_vm.BlockSize:X}";

                if (_mapItems.Count > 0)
                    MapListBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventMapChangeView.LoadList failed: {0}", ex.Message);
            }
        }

        void MapListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                int idx = MapListBox.SelectedIndex;
                if (idx < 0 || idx >= _mapItems.Count) return;

                uint mapId = _mapItems[idx].tag;
                bool ok = _vm.LoadEntryForMap(mapId);
                if (ok)
                {
                    UpdateUI();
                    // #857: render the change-overlay preview after loading the entry.
                    RenderChangePreview();
                }
                else
                {
                    // No change-data for this map — clear the detail panel.
                    ClearDetail();
                    // Clear the preview too.
                    MapPictureImage.SetImage(null);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventMapChangeView.MapListBox_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void OnEntrySelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                // #857: re-render preview when an entry is selected from the
                // legacy AddressList (right column). The map ID was already set
                // by the left-column MapListBox selection so _currentMapId is valid.
                RenderChangePreview();
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventMapChangeView.OnEntrySelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Text = $"0x{_vm.BlockSize:X}";
            SelectedAddressBox.Text = $"0x{_vm.SelectAddress:X08}";
            // Refresh the read-config bar too — VM updates these per
            // selected change-data entry (mirrors WF's per-map ReInit;
            // Copilot bot review on issue #423).
            if (TopBar != null)
            {
                TopBar.StartAddressText = $"0x{_vm.ReadStartAddress:X08}";
                TopBar.ReadCountText = _vm.ReadCount.ToString();
            }
            B0Box.Value = _vm.B0;
            B1Box.Value = _vm.B1;
            B2Box.Value = _vm.B2;
            B3Box.Value = _vm.B3;
            B4Box.Value = _vm.B4;
            B5Box.Value = _vm.B5;
            B6Box.Value = _vm.B6;
            B7Box.Value = _vm.B7;
            P8Box.Text = $"0x{_vm.P8:X08}";
            CommentBox.Text = _vm.Comment;
        }

        void ClearDetail()
        {
            AddrLabel.Text = "";
            AddressBox.Value = 0;
            // Clear the read-config bar too — the VM was just reset by
            // ClearEntry() so the top bar should reflect "no entry
            // loaded" instead of carrying the previous map's stale
            // ReadStartAddress / ReadCount (Copilot bot 3rd-pass
            // review on issue #423).
            if (TopBar != null)
            {
                TopBar.StartAddressText = string.Empty;
                TopBar.ReadCountText = string.Empty;
            }
            BlockSizeBox.Text = "";
            SelectedAddressBox.Text = "";
            B0Box.Value = 0;
            B1Box.Value = 0;
            B2Box.Value = 0;
            B3Box.Value = 0;
            B4Box.Value = 0;
            B5Box.Value = 0;
            B6Box.Value = 0;
            B7Box.Value = 0;
            P8Box.Text = "";
            CommentBox.Text = "";
            // #857: clear preview.
            MapPictureImage.SetImage(null);
        }

        void ReadFromUI()
        {
            _vm.B0 = (uint)(B0Box.Value ?? 0);
            _vm.B1 = (uint)(B1Box.Value ?? 0);
            _vm.B2 = (uint)(B2Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.B4 = (uint)(B4Box.Value ?? 0);
            _vm.B5 = (uint)(B5Box.Value ?? 0);
            _vm.B6 = (uint)(B6Box.Value ?? 0);
            _vm.B7 = (uint)(B7Box.Value ?? 0);
            _vm.P8 = ParseHexText(P8Box.Text);
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // #1148: the map-change overlay is a raw source-tree asset (LZ77 tile binary +
            // its struct) — blocked in decomp mode; migrate via the source-tree asset path.
            if (DecompMapAssetGuard.BlockIfDecomp(R._("map-change overlay")))
                return;

            // Refuse the write when no entry is loaded — protects against
            // the Map Names list selecting a map with no change-data
            // (LoadEntryForMap returned false → VM cleared) and the user
            // then pressing Write, which would otherwise corrupt
            // CurrentAddr=0 (the ROM header). Copilot CLI re-review on
            // issue #423.
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0)
            {
                CoreState.Services?.ShowError(
                    "No map-change entry is selected. Select a map with change-data before writing.");
                return;
            }

            _undoService.Begin("Edit Map Change");
            try
            {
                ReadFromUI();
                _vm.SaveComment(CommentBox.Text ?? string.Empty);
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EventMapChangeView.Write_Click failed: {0}", ex.Message);
                CoreState.Services?.ShowError($"Write failed: {ex.Message}");
            }
        }

        // ----------------------------------------------------------------
        // Change-overlay preview (#857, NV6-PR2).
        // ----------------------------------------------------------------

        /// <summary>
        /// Render the change-map overlay into <see cref="MapPictureImage"/>
        /// and update the <c>CanExportChange</c> binding gate.
        /// </summary>
        void RenderChangePreview()
        {
            try
            {
                IImage? img = _vm.RenderChangePreview();
                MapPictureImage.SetImage(img);
                // CanExportChange is set by the VM; the binding propagates
                // to the Export PNG button automatically.
            }
            catch (Exception ex)
            {
                MapPictureImage.SetImage(null);
                _vm.CanExportChange = false;
                Log.Error($"EventMapChangeView.RenderChangePreview failed: {ex}");
            }
        }

        /// <summary>Export the change-overlay preview as PNG.</summary>
        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                await MapPictureImage.ExportPng(TopLevel.GetTopLevel(this) as Window, "eventmapchange_overlay");
            }
            catch (Exception ex)
            {
                Log.Error($"EventMapChangeView.ExportPng failed: {ex}");
            }
        }

        // #961 W2c — pointer-import (mirrors the intent of WF EventMapChangeForm
        // `button1` "変化データ ポインタ先へのインポート"). Prompts for a SOURCE
        // change-data address, copies that record's tile bytes (sized by the
        // current record's W×H) into ROM free space, and repoints THIS record's
        // P8 at the copy — all under one UndoService scope. Never overwrites in
        // place, so a size mismatch can't corrupt neighbouring data.
        async void PointerImport_Click(object? sender, RoutedEventArgs e)
        {
            // #1148: importing map-change data mutates the build-preview ROM — blocked in decomp mode.
            if (DecompMapAssetGuard.BlockIfDecomp(R._("map-change overlay")))
                return;

            try
            {
                if (!_vm.IsLoaded || _vm.CurrentAddr == 0)
                {
                    CoreState.Services?.ShowError(R._(
                        "No map-change entry is selected. Select a map with change-data before importing."));
                    return;
                }
                if (_vm.B3 == 0 || _vm.B4 == 0)
                {
                    CoreState.Services?.ShowError(R._(
                        "The selected record has zero width or height. Set the W/H fields and Write before importing."));
                    return;
                }

                // Prompt for the SOURCE change-data address. Default to the
                // current record's P8 destination so re-importing the same data
                // (a deep-copy / detach) is the one-click path. The dialog edits a
                // ROM offset; a GBA pointer is also accepted (the VM normalises
                // via U.toOffset). The dialog max is the GBA cartridge-addressable
                // ceiling (ROM space is 0x08000000–0x09FFFFFF, the exclusive bound
                // 0x0A000000 used throughout U.isSafetyOffset/isPointer) so BOTH a
                // raw offset AND a GBA pointer are enterable. The REAL bounds check
                // (U.toOffset → U.isSafetyOffset → srcOffset + length > Data.Length)
                // lives in ImportChangeDataFromPointer, which rejects anything that
                // does not resolve to in-ROM data.
                uint defaultSrc = U.toOffset(_vm.P8);
                const uint GBA_ADDRESS_MAX = 0x09FFFFFFu;
                uint? chosen = await NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the SOURCE change-data address to import from (W×H×2 = {0} bytes will be copied into this record).",
                        _vm.B3 * _vm.B4 * 2),
                    R._("Pointer Import"),
                    defaultSrc,
                    0,
                    GBA_ADDRESS_MAX);
                if (chosen == null) return; // cancelled

                _undoService.Begin("Import Map Change Pointer");
                try
                {
                    string err = _vm.ImportChangeDataFromPointer(chosen.Value);
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();

                    // Reflect the repointed P8 in the UI and re-render the overlay
                    // from the freshly imported data.
                    UpdateUI();
                    RenderChangePreview();
                    CoreState.Services?.ShowInfo(R._("Imported change data into the selected record."));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.ErrorF("EventMapChangeView.PointerImport inner failed: {0}", inner.Message);
                    CoreState.Services?.ShowError(R._("Pointer import failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventMapChangeView.PointerImport_Click failed: {0}", ex.Message);
            }
        }

        // #862 — wire the List Expand button to grow the 12-byte map-change
        // record list via DataExpansionCore.ExpandTableTo + RepointAllReferences,
        // mirroring WorldMapImageView.BorderListExpand_Click (NV1a all-reference
        // pattern).
        async void ListExpands_Click(object? sender, RoutedEventArgs e)
        {
            // #1148: expanding the map-change table mutates / reallocates ROM — blocked in decomp mode.
            if (DecompMapAssetGuard.BlockIfDecomp(R._("map-change table")))
                return;

            try
            {
                if (!_vm.IsLoaded || _vm.CurrentAddr == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Select a map with change-data before expanding."));
                    return;
                }
                if (_vm.ReadCount == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: change list is empty."));
                    return;
                }

                // Default = current + 1, max 255 (mirrors WF
                // AddressListExpandsButton_255 convention).
                uint current = (uint)_vm.ReadCount;
                uint defaultCount = current + 1;
                if (defaultCount > 255) defaultCount = 255;
                uint? chosen = await NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the new entry count for the event map-change list (current: {0}, max: 255).", current),
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

                _undoService.Begin("Expand Event Map Change List");
                try
                {
                    string err = _vm.ExpandEventMapChangeList(newCount, _undoService.GetActiveUndoData());
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();

                    // NOTE B: render the grown list directly from the new base +
                    // new count (the VM already set ReadStartAddress/ReadCount from
                    // the ExpandResult). Re-scanning would still be correct here
                    // (zero-filled rows have firstByte==0 != 0xFF) but using the
                    // result directly is cleaner.
                    RefreshChangeListFromReadConfig();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded event map-change list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.Error($"EventMapChangeView.ListExpands inner failed: {inner.Message}");
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventMapChangeView.ListExpands failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh the EntryList from the VM's post-expand read-config
        /// (ReadStartAddress = new base offset; ReadCount = new row count)
        /// WITHOUT re-scanning (NOTE B). Mirrors
        /// WorldMapImageView.RefreshBorderListFromReadConfig.
        /// </summary>
        void RefreshChangeListFromReadConfig()
        {
            try
            {
                uint baseAddr = _vm.ReadStartAddress;
                var items = _vm.BuildChangeListForCount(baseAddr, _vm.ReadCount);
                EntryList.SetItems(items);
                if (TopBar != null)
                {
                    TopBar.StartAddressText = $"0x{baseAddr:X08}";
                    TopBar.ReadCountText = _vm.ReadCount.ToString();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventMapChangeView.RefreshChangeListFromReadConfig failed: {ex.Message}");
            }
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
