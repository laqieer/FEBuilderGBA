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
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
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
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageView.LoadAll failed: {0}", ex.Message);
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
                Log.Error("WorldMapImageView.WriteAll failed: {0}", ex.Message);
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
                Log.Error("WorldMapImageView.OnBorderSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
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
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapImageView.BorderWrite failed: {0}", ex.Message);
            }
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnBorderTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadBorderList(); }
            catch (Exception ex) { Log.Error("BorderReload failed: {0}", ex.Message); }
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
                    Log.Error("WorldMapImageView.BorderListExpand inner failed: {0}", inner.Message);
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageView.BorderListExpand failed: {0}", ex.Message);
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
                Log.Error("WorldMapImageView.OnIconSelected failed: {0}", ex.Message);
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
                Log.Error("WorldMapImageView.IconWrite failed: {0}", ex.Message);
            }
        }

        // #668: routed event from the unified EditorTopBar control.
        void OnIconDataTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            try { LoadIconList(); }
            catch (Exception ex) { Log.Error("IconDataReload failed: {0}", ex.Message); }
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
                    Log.Error("WorldMapImageView.IconDataListExpand inner failed: {0}", inner.Message);
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageView.IconDataListExpand failed: {0}", ex.Message);
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
                Log.Error("WorldMapImageView.Undo failed: {0}", ex.Message);
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
