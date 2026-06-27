// SPDX-License-Identifier: GPL-3.0-or-later
// MonsterItemViewerView code-behind — three-tab parity rebuild (#394).
//
// Routes the Item / Probability / Holdings tabs to the matching ViewModel
// surfaces. Each Write_* handler opens its own distinctly-named undo scope
// so undo for the three tabs is independent. Each List Expand button
// delegates to DataExpansionCore.ExpandTable under the ambient undo scope.
//
// Cross-tab navigation: holding-tab item fields (B1..B10) call
// `JumpToItemRow(index)` to switch to Tab 1 and select the matching row;
// holding-tab item-probability fields (B21..B30) call
// `JumpToProbRow(index)` to switch to Tab 2. This mirrors the WinForms
// `JumpToItemSelect` / `JumpToProbabilitySelect` handlers.
//
// Backward compatibility: legacy `EntryList`, `AddrLabel`, `ItemIdBox`,
// `DropRateBox`, `Unknown1..3Box` names + the legacy
// `MonsterItemViewer_ItemId_Input` AutomationId on the slot-1
// IdFieldControl + the legacy `MonsterItemViewer_Write_Button` Button
// alias (kept as a hidden zero-size Button wired to the same
// `ItemWrite_Click` handler) are preserved so existing
// AvaloniaEditorTests + ListParityHelper + any automation harnesses
// pointing at the original single-tab surface continue to resolve.

using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MonsterItemViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MonsterItemViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Monster Item";
        public bool IsLoaded => _vm.CanWrite;

        public MonsterItemViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnItemSelected;
            ProbEntryList.SelectedAddressChanged += OnProbSelected;
            HoldingEntryList.SelectedAddressChanged += OnHoldingSelected;
            Opened += (_, _) =>
            {
                LoadItemList();
                LoadProbList();
                LoadHoldList();
            };
        }

        // ============================================================
        // Tab 1 — Item Table
        // ============================================================

        void LoadItemList(uint preserveAddress = 0)
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadMonsterItemList();
                // #743: unified top-bar surfaces ReadStart / Count via CLR properties.
                items = ApplyReadWindow(items,
                    ItemTopBar?.ReadStartAddress ?? 0u,
                    (uint)(ItemTopBar?.ReadCount ?? 0));
                if (preserveAddress != 0)
                    EntryList.SetItemsPreserveSelection(items, preserveAddress);
                else
                    EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("MonsterItemViewerView.LoadItemList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnItemSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMonsterItem(addr);
                UpdateItemUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("MonsterItemViewerView.OnItemSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateItemUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ItemSelectedAddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ItemIdBox.Value = _vm.ItemId;
            try { ItemIdBox.NameText = NameResolver.GetItemName(_vm.ItemId); }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.UpdateItemUI ItemName: {0}", ex.Message); }
            // #950 T4: Item 2..5 (B1..B4) are item IDs; populate the IdFieldControl
            // value + inline item-name preview just like slot-1 ItemIdBox.
            DropRateBox.Value = _vm.DropRate;
            Unknown1Box.Value = _vm.Unknown1;
            Unknown2Box.Value = _vm.Unknown2;
            Unknown3Box.Value = _vm.Unknown3;
            try
            {
                DropRateBox.NameText = NameResolver.GetItemName(_vm.DropRate);
                Unknown1Box.NameText = NameResolver.GetItemName(_vm.Unknown1);
                Unknown2Box.NameText = NameResolver.GetItemName(_vm.Unknown2);
                Unknown3Box.NameText = NameResolver.GetItemName(_vm.Unknown3);
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.UpdateItemUI ItemName 2..5: {0}", ex.Message); }
            ItemCommentBox.Text = ReadComment(_vm.CurrentAddr);
        }

        void ItemWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Monster Item");
            try
            {
                _vm.ItemId = ItemIdBox.Value;
                // #950 T4: IdFieldControl.Value is a non-nullable uint.
                _vm.DropRate = DropRateBox.Value;
                _vm.Unknown1 = Unknown1Box.Value;
                _vm.Unknown2 = Unknown2Box.Value;
                _vm.Unknown3 = Unknown3Box.Value;
                _vm.WriteMonsterItem();
                uint preserve = _vm.CurrentAddr;
                _undoService.Commit();
                // CommentCache lives outside the ambient undo scope, so
                // we persist the comment ONLY after a successful Commit
                // — otherwise a mid-write Rollback would undo the ROM
                // bytes but leave a stale comment behind.
                WriteComment(preserve, ItemCommentBox.Text ?? string.Empty);
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Monster item data written.");
                LoadItemList(preserve);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MonsterItemViewerView.ItemWrite: {0}", ex.Message);
            }
        }

        void ItemReload_Click(object? sender, RoutedEventArgs e) => LoadItemList();

        // #743: routed event from the unified EditorTopBarWithInputs Reload button (Tab 1).
        void OnItemTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadItemList();

        void ItemExpand_Click(object? sender, RoutedEventArgs e)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;
            uint ptr = rom.RomInfo.monster_item_item_pointer;
            if (ptr == 0) { CoreState.Services?.ShowError("Monster item table pointer unset for this ROM version."); return; }

            _undoService.Begin("Expand Monster Item Table");
            try
            {
                uint currentCount = (uint)_vm.LoadMonsterItemList().Count;
                var result = DataExpansionCore.ExpandTable(rom, ptr, entrySize: 5, currentCount);
                if (!result.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(result.Error ?? "Expand failed.");
                    return;
                }
                uint preserve = _vm.CurrentAddr;
                _undoService.Commit();
                LoadItemList(preserve);
                CoreState.Services?.ShowInfo($"Monster item table expanded to {result.NewCount} entries at 0x{result.NewBaseAddress:X08}.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MonsterItemViewerView.ItemExpand: {0}", ex.Message);
            }
        }

        // ============================================================
        // Tab 2 — Probability Table
        // ============================================================

        void LoadProbList(uint preserveAddress = 0)
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadMonsterItemProbabilityList();
                // #743: unified top-bar surfaces ReadStart / Count via CLR properties.
                items = ApplyReadWindow(items,
                    ProbTopBar?.ReadStartAddress ?? 0u,
                    (uint)(ProbTopBar?.ReadCount ?? 0));
                if (preserveAddress != 0)
                    ProbEntryList.SetItemsPreserveSelection(items, preserveAddress);
                else
                    ProbEntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("MonsterItemViewerView.LoadProbList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnProbSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMonsterItemProbability(addr);
                UpdateProbUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("MonsterItemViewerView.OnProbSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateProbUI()
        {
            ProbAddrLabel.Text = $"0x{_vm.ProbabilityAddr:X08}";
            ProbSelectedAddrLabel.Text = $"0x{_vm.ProbabilityAddr:X08}";
            Prob1Box.Value = _vm.Prob1;
            Prob2Box.Value = _vm.Prob2;
            Prob3Box.Value = _vm.Prob3;
            Prob4Box.Value = _vm.Prob4;
            Prob5Box.Value = _vm.Prob5;
            ProbCommentBox.Text = ReadComment(_vm.ProbabilityAddr);
            UpdateProbSum();
        }

        void UpdateProbSum()
        {
            uint sum = (uint)((Prob1Box.Value ?? 0) + (Prob2Box.Value ?? 0) +
                              (Prob3Box.Value ?? 0) + (Prob4Box.Value ?? 0) +
                              (Prob5Box.Value ?? 0));
            ProbSumLabel.Text = $"{sum}%";
        }

        void ProbabilityWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.ProbabilityCanWrite) return;
            _undoService.Begin("Edit Monster Item Probability");
            try
            {
                _vm.Prob1 = (uint)(Prob1Box.Value ?? 0);
                _vm.Prob2 = (uint)(Prob2Box.Value ?? 0);
                _vm.Prob3 = (uint)(Prob3Box.Value ?? 0);
                _vm.Prob4 = (uint)(Prob4Box.Value ?? 0);
                _vm.Prob5 = (uint)(Prob5Box.Value ?? 0);
                _vm.WriteMonsterItemProbability();
                uint preserve = _vm.ProbabilityAddr;
                _undoService.Commit();
                // CommentCache persisted AFTER Commit — see ItemWrite_Click.
                WriteComment(preserve, ProbCommentBox.Text ?? string.Empty);
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Monster item probability written.");
                LoadProbList(preserve);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MonsterItemViewerView.ProbabilityWrite: {0}", ex.Message);
            }
        }

        void ProbReload_Click(object? sender, RoutedEventArgs e) => LoadProbList();

        // #743: routed event from the unified EditorTopBarWithInputs Reload button (Tab 2).
        void OnProbTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadProbList();

        void ProbExpand_Click(object? sender, RoutedEventArgs e)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;
            uint ptr = rom.RomInfo.monster_item_probability_pointer;
            if (ptr == 0) { CoreState.Services?.ShowError("Monster probability table pointer unset for this ROM version."); return; }

            _undoService.Begin("Expand Monster Probability Table");
            try
            {
                uint currentCount = (uint)_vm.LoadMonsterItemProbabilityList().Count;
                var result = DataExpansionCore.ExpandTable(rom, ptr, entrySize: 5, currentCount);
                if (!result.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(result.Error ?? "Expand failed.");
                    return;
                }
                uint preserve = _vm.ProbabilityAddr;
                _undoService.Commit();
                LoadProbList(preserve);
                CoreState.Services?.ShowInfo($"Probability table expanded to {result.NewCount} entries.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MonsterItemViewerView.ProbExpand: {0}", ex.Message);
            }
        }

        // ============================================================
        // Tab 3 — Holdings Table
        // ============================================================

        void LoadHoldList(uint preserveAddress = 0)
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadMonsterItemHoldingsList();
                // #743: unified top-bar surfaces ReadStart / Count via CLR properties.
                items = ApplyReadWindow(items,
                    HoldTopBar?.ReadStartAddress ?? 0u,
                    (uint)(HoldTopBar?.ReadCount ?? 0));
                if (preserveAddress != 0)
                    HoldingEntryList.SetItemsPreserveSelection(items, preserveAddress);
                else
                    HoldingEntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("MonsterItemViewerView.LoadHoldList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnHoldingSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMonsterItemHoldings(addr);
                UpdateHoldingUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("MonsterItemViewerView.OnHoldingSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateHoldingUI()
        {
            HoldAddrLabel.Text = $"0x{_vm.HoldingAddr:X08}";
            HoldSelectedAddrLabel.Text = $"0x{_vm.HoldingAddr:X08}";
            HoldClassIdBox.Value = _vm.ClassId;
            try { HoldClassIdBox.NameText = NameResolver.GetClassName(_vm.ClassId); }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.UpdateHoldingUI ClassName: {0}", ex.Message); }

            HoldItem1Box.Value = _vm.HoldingItem1;
            HoldItem2Box.Value = _vm.HoldingItem2;
            HoldItem3Box.Value = _vm.HoldingItem3;
            HoldItem4Box.Value = _vm.HoldingItem4;
            HoldItem5Box.Value = _vm.HoldingItem5;
            HoldItem6Box.Value = _vm.HoldingItem6;
            HoldItem7Box.Value = _vm.HoldingItem7;
            HoldItem8Box.Value = _vm.HoldingItem8;
            HoldItem9Box.Value = _vm.HoldingItem9;
            HoldItem10Box.Value = _vm.HoldingItem10;

            HoldProb1Box.Value = _vm.HoldingProb1;
            HoldProb2Box.Value = _vm.HoldingProb2;
            HoldProb3Box.Value = _vm.HoldingProb3;
            HoldProb4Box.Value = _vm.HoldingProb4;
            HoldProb5Box.Value = _vm.HoldingProb5;
            HoldProb6Box.Value = _vm.HoldingProb6;
            HoldProb7Box.Value = _vm.HoldingProb7;
            HoldProb8Box.Value = _vm.HoldingProb8;
            HoldProb9Box.Value = _vm.HoldingProb9;
            HoldProb10Box.Value = _vm.HoldingProb10;

            HoldItemProb1Box.Value = _vm.HoldingItemProb1;
            HoldItemProb2Box.Value = _vm.HoldingItemProb2;
            HoldItemProb3Box.Value = _vm.HoldingItemProb3;
            HoldItemProb4Box.Value = _vm.HoldingItemProb4;
            HoldItemProb5Box.Value = _vm.HoldingItemProb5;
            HoldItemProb6Box.Value = _vm.HoldingItemProb6;
            HoldItemProb7Box.Value = _vm.HoldingItemProb7;
            HoldItemProb8Box.Value = _vm.HoldingItemProb8;
            HoldItemProb9Box.Value = _vm.HoldingItemProb9;
            HoldItemProb10Box.Value = _vm.HoldingItemProb10;

            HoldB31Box.Value = _vm.B31;
            HoldCommentBox.Text = ReadComment(_vm.HoldingAddr);
            UpdateHoldingSums();
        }

        void UpdateHoldingSums()
        {
            uint sum1 = (uint)((HoldProb1Box.Value ?? 0) + (HoldProb2Box.Value ?? 0) +
                               (HoldProb3Box.Value ?? 0) + (HoldProb4Box.Value ?? 0) +
                               (HoldProb5Box.Value ?? 0));
            uint sum2 = (uint)((HoldProb6Box.Value ?? 0) + (HoldProb7Box.Value ?? 0) +
                               (HoldProb8Box.Value ?? 0) + (HoldProb9Box.Value ?? 0) +
                               (HoldProb10Box.Value ?? 0));
            HoldSum1Label.Text = $"{sum1}%";
            HoldSum2Label.Text = $"{sum2}%";
        }

        void HoldingsWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.HoldingCanWrite) return;
            _undoService.Begin("Edit Monster Item Holdings");
            try
            {
                _vm.ClassId = HoldClassIdBox.Value;
                _vm.HoldingItem1 = HoldItem1Box.Value;
                _vm.HoldingItem2 = HoldItem2Box.Value;
                _vm.HoldingItem3 = HoldItem3Box.Value;
                _vm.HoldingItem4 = HoldItem4Box.Value;
                _vm.HoldingItem5 = HoldItem5Box.Value;
                _vm.HoldingItem6 = HoldItem6Box.Value;
                _vm.HoldingItem7 = HoldItem7Box.Value;
                _vm.HoldingItem8 = HoldItem8Box.Value;
                _vm.HoldingItem9 = HoldItem9Box.Value;
                _vm.HoldingItem10 = HoldItem10Box.Value;

                _vm.HoldingProb1 = (uint)(HoldProb1Box.Value ?? 0);
                _vm.HoldingProb2 = (uint)(HoldProb2Box.Value ?? 0);
                _vm.HoldingProb3 = (uint)(HoldProb3Box.Value ?? 0);
                _vm.HoldingProb4 = (uint)(HoldProb4Box.Value ?? 0);
                _vm.HoldingProb5 = (uint)(HoldProb5Box.Value ?? 0);
                _vm.HoldingProb6 = (uint)(HoldProb6Box.Value ?? 0);
                _vm.HoldingProb7 = (uint)(HoldProb7Box.Value ?? 0);
                _vm.HoldingProb8 = (uint)(HoldProb8Box.Value ?? 0);
                _vm.HoldingProb9 = (uint)(HoldProb9Box.Value ?? 0);
                _vm.HoldingProb10 = (uint)(HoldProb10Box.Value ?? 0);

                _vm.HoldingItemProb1 = HoldItemProb1Box.Value;
                _vm.HoldingItemProb2 = HoldItemProb2Box.Value;
                _vm.HoldingItemProb3 = HoldItemProb3Box.Value;
                _vm.HoldingItemProb4 = HoldItemProb4Box.Value;
                _vm.HoldingItemProb5 = HoldItemProb5Box.Value;
                _vm.HoldingItemProb6 = HoldItemProb6Box.Value;
                _vm.HoldingItemProb7 = HoldItemProb7Box.Value;
                _vm.HoldingItemProb8 = HoldItemProb8Box.Value;
                _vm.HoldingItemProb9 = HoldItemProb9Box.Value;
                _vm.HoldingItemProb10 = HoldItemProb10Box.Value;

                _vm.B31 = (uint)(HoldB31Box.Value ?? 0);

                _vm.WriteMonsterItemHoldings();
                uint preserve = _vm.HoldingAddr;
                _undoService.Commit();
                // CommentCache persisted AFTER Commit — see ItemWrite_Click.
                WriteComment(preserve, HoldCommentBox.Text ?? string.Empty);
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Monster item holdings written.");
                LoadHoldList(preserve);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MonsterItemViewerView.HoldingsWrite: {0}", ex.Message);
            }
        }

        void HoldReload_Click(object? sender, RoutedEventArgs e) => LoadHoldList();

        // #743: routed event from the unified EditorTopBarWithInputs Reload button (Tab 3).
        void OnHoldTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadHoldList();

        void HoldExpand_Click(object? sender, RoutedEventArgs e)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;
            uint ptr = rom.RomInfo.monster_item_table_pointer;
            if (ptr == 0) { CoreState.Services?.ShowError("Monster holdings table pointer unset for this ROM version."); return; }

            _undoService.Begin("Expand Monster Holdings Table");
            try
            {
                uint currentCount = (uint)_vm.LoadMonsterItemHoldingsList().Count;
                var result = DataExpansionCore.ExpandTable(rom, ptr, entrySize: 32, currentCount);
                if (!result.Success)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(result.Error ?? "Expand failed.");
                    return;
                }
                uint preserve = _vm.HoldingAddr;
                _undoService.Commit();
                LoadHoldList(preserve);
                CoreState.Services?.ShowInfo($"Holdings table expanded to {result.NewCount} entries.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MonsterItemViewerView.HoldExpand: {0}", ex.Message);
            }
        }

        // ============================================================
        // Cross-tab navigation (mirrors WF JumpToItemSelect /
        // JumpToProbabilitySelect). Each HoldingItem<N>_Jump and
        // HoldingItemProb<N>_Jump dispatches through the same helper.
        // ============================================================

        /// <summary>
        /// Switch to the Item tab and select the row corresponding to the
        /// holdings-tab item value <paramref name="value"/>.
        /// Matches WF `JumpToItemSelect` semantics:
        ///   - value 0 -> deselect (the WF UI shows "no selection" when the
        ///     holding slot is empty).
        ///   - value N (N > 0) -> select 0-based index (N - 1) — the WF
        ///     stored value is 1-based with `<= 0` meaning "empty slot",
        ///     so subtract 1 to land on the matching row.
        /// </summary>
        void JumpToItemRow(uint value)
        {
            try
            {
                MainTabs.SelectedItem = ItemTab;
                if (value == 0)
                {
                    EntryList.Deselect();
                    return;
                }
                EntryList.SelectByIndex((int)(value - 1));
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.JumpToItemRow: {0}", ex.Message); }
        }

        /// <summary>
        /// Switch to the Probability tab and select the row corresponding to
        /// the holdings-tab item-probability value <paramref name="value"/>.
        /// Same semantics as <see cref="JumpToItemRow"/>: 0 = deselect,
        /// N > 0 = select index N - 1. Mirrors WF `JumpToProbabilitySelect`.
        /// </summary>
        void JumpToProbRow(uint value)
        {
            try
            {
                MainTabs.SelectedItem = ProbTab;
                if (value == 0)
                {
                    ProbEntryList.Deselect();
                    return;
                }
                ProbEntryList.SelectByIndex((int)(value - 1));
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.JumpToProbRow: {0}", ex.Message); }
        }

        // 10 holding-item jump handlers (B1..B10 -> Item tab).
        void HoldingItem1_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem1Box.Value);
        void HoldingItem2_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem2Box.Value);
        void HoldingItem3_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem3Box.Value);
        void HoldingItem4_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem4Box.Value);
        void HoldingItem5_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem5Box.Value);
        void HoldingItem6_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem6Box.Value);
        void HoldingItem7_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem7Box.Value);
        void HoldingItem8_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem8Box.Value);
        void HoldingItem9_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem9Box.Value);
        void HoldingItem10_Jump(object? sender, RoutedEventArgs e) => JumpToItemRow(HoldItem10Box.Value);

        // NOTE: Per Copilot CLI PR #596 round-3 review thread
        // PRRT_kwDOH0Mc1M6EYelo: the holdings-tab item fields (B1..B10) store
        // INDICES into the Tab-1 MonsterItem table, NOT FE item IDs. A Pick
        // handler that opens the ItemEditor would write back a wrong-domain
        // value (item id, not table index) and corrupt the holdings record.
        // The Pick handlers are therefore intentionally OMITTED here — only
        // the Jump handlers (JumpToItemRow + JumpToProbRow) navigate to the
        // matching row. The IdFieldControl Pick affordance is unwired in
        // AXAML for these 10 boxes.

        // 10 holding-item-probability jump handlers (B21..B30 -> Probability tab).
        void HoldingItemProb1_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb1Box.Value);
        void HoldingItemProb2_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb2Box.Value);
        void HoldingItemProb3_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb3Box.Value);
        void HoldingItemProb4_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb4Box.Value);
        void HoldingItemProb5_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb5Box.Value);
        void HoldingItemProb6_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb6Box.Value);
        void HoldingItemProb7_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb7Box.Value);
        void HoldingItemProb8_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb8Box.Value);
        void HoldingItemProb9_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb9Box.Value);
        void HoldingItemProb10_Jump(object? sender, RoutedEventArgs e) => JumpToProbRow(HoldItemProb10Box.Value);

        // ============================================================
        // Item ID picker helpers (#360 IdFieldControl).
        // ============================================================

        /// <summary>
        /// Compute the Item editor's ROM entry address for a given item id.
        /// </summary>
        static uint ItemAddrFor(uint itemId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint itemPtr = rom.RomInfo.item_pointer;
            if (itemPtr == 0) return 0;
            uint baseAddr = rom.p32(itemPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + itemId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        async System.Threading.Tasks.Task PickItemIdInto(IdFieldControl box)
        {
            try
            {
                uint addr = ItemAddrFor(box.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ItemFE6View>(addr, this);
                else
                    result = await WindowManager.Instance.PickFromEditor<ItemEditorView>(addr, this);
                if (result != null)
                    box.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.PickItemIdInto: {0}", ex.Message); }
        }

        void ItemId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ItemAddrFor(ItemIdBox.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ItemFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ItemEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.ItemId_Jump failed: {0}", ex.Message); }
        }

        async void ItemId_Pick(object? sender, RoutedEventArgs e) => await PickItemIdInto(ItemIdBox);

        void ItemId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { ItemIdBox.NameText = NameResolver.GetItemName(e.NewValue); }
            catch { /* NameResolver may fail without ROM */ }
        }

        // ============================================================
        // Item 2..5 picker helpers (#950 T4). Each of the 4 secondary
        // item-id slots (B1..B4) reuses the exact slot-1 Jump/Pick wiring
        // (ItemAddrFor + ItemEditorView/ItemFE6View). The Jump routes to
        // the matching ItemEditor entry; Pick opens it in pick mode and
        // writes back the chosen item id; ValueChanged refreshes the
        // inline item-name preview.
        // ============================================================

        void JumpToItemEditor(IdFieldControl box)
        {
            try
            {
                uint addr = ItemAddrFor(box.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ItemFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ItemEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.JumpToItemEditor: {0}", ex.Message); }
        }

        void Item2_Jump(object? sender, RoutedEventArgs e) => JumpToItemEditor(DropRateBox);
        async void Item2_Pick(object? sender, RoutedEventArgs e) => await PickItemIdInto(DropRateBox);
        void Item2_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { DropRateBox.NameText = NameResolver.GetItemName(e.NewValue); }
            catch { /* NameResolver may fail without ROM */ }
        }

        void Item3_Jump(object? sender, RoutedEventArgs e) => JumpToItemEditor(Unknown1Box);
        async void Item3_Pick(object? sender, RoutedEventArgs e) => await PickItemIdInto(Unknown1Box);
        void Item3_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { Unknown1Box.NameText = NameResolver.GetItemName(e.NewValue); }
            catch { /* NameResolver may fail without ROM */ }
        }

        void Item4_Jump(object? sender, RoutedEventArgs e) => JumpToItemEditor(Unknown2Box);
        async void Item4_Pick(object? sender, RoutedEventArgs e) => await PickItemIdInto(Unknown2Box);
        void Item4_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { Unknown2Box.NameText = NameResolver.GetItemName(e.NewValue); }
            catch { /* NameResolver may fail without ROM */ }
        }

        void Item5_Jump(object? sender, RoutedEventArgs e) => JumpToItemEditor(Unknown3Box);
        async void Item5_Pick(object? sender, RoutedEventArgs e) => await PickItemIdInto(Unknown3Box);
        void Item5_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { Unknown3Box.NameText = NameResolver.GetItemName(e.NewValue); }
            catch { /* NameResolver may fail without ROM */ }
        }

        // ============================================================
        // Class ID picker helpers (Tab 3 — Holdings).
        // ============================================================

        /// <summary>Compute the Class editor entry address for a class id.</summary>
        static uint ClassAddrFor(uint classId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint classPtr = rom.RomInfo.class_pointer;
            if (classPtr == 0) return 0;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + classId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void HoldClassId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(HoldClassIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.HoldClassId_Jump: {0}", ex.Message); }
        }

        async void HoldClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(HoldClassIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr, this);
                if (result != null) HoldClassIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.HoldClassId_Pick: {0}", ex.Message); }
        }

        void HoldClassId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { HoldClassIdBox.NameText = NameResolver.GetClassName(e.NewValue); }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.HoldClassId_ValueChanged: {0}", ex.Message); }
        }

        // ============================================================
        // IEditorView / IDataVerifiableView surface.
        // ============================================================

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        // ============================================================
        // Comment + ReadWindow helpers (Copilot CLI PR review threads).
        // ============================================================

        /// <summary>
        /// Slice <paramref name="items"/> using a WF-style read window:
        ///   - If BOTH `start` and `count` are 0, return the full list
        ///     (the WF reread bar leaves both fields at 0 by default).
        ///   - If either is non-zero, slice from `start` for up to `count`
        ///     entries. `count == 0` (with `start > 0`) is interpreted as
        ///     "read to end" — return the remaining tail starting at
        ///     `start`.
        ///   - `start` is clamped to [0, items.Count] (where
        ///     `start == items.Count` deliberately yields an empty range,
        ///     matching the WF reread-bar semantics — past-end is
        ///     "show nothing", not an error); `count` is clamped to the
        ///     remaining tail.
        /// Mirrors the WF panel1 Read Start Address + Read Count semantics.
        /// </summary>
        static System.Collections.Generic.List<AddrResult> ApplyReadWindow(
            System.Collections.Generic.List<AddrResult> items, uint start, uint count)
        {
            if (start == 0 && count == 0) return items;
            int s = (int)Math.Min(start, (uint)items.Count);
            int remaining = items.Count - s;
            int c = (count == 0) ? remaining : (int)Math.Min(count, (uint)remaining);
            return items.GetRange(s, c);
        }

        /// <summary>
        /// Read the persisted comment for <paramref name="addr"/> from
        /// <see cref="CoreState.CommentCache"/> (mirrors the WF
        /// InputFormRef.UI_WriteCommentToUI pattern). Returns an empty string
        /// when the address is 0 or the cache is unavailable.
        /// </summary>
        static string ReadComment(uint addr)
        {
            if (addr == 0) return string.Empty;
            try
            {
                if (CoreState.CommentCache != null &&
                    CoreState.CommentCache.TryGetValue(addr, out string value))
                    return value ?? string.Empty;
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.ReadComment: {0}", ex.Message); }
            return string.Empty;
        }

        /// <summary>
        /// Persist <paramref name="comment"/> for <paramref name="addr"/> to
        /// <see cref="CoreState.CommentCache"/>. No-op when address is 0 or
        /// the cache is unavailable. Comments are stored separately from the
        /// ROM bytes (CommentCache is a sidecar map), so this write is NOT
        /// part of the ambient undo scope.
        /// </summary>
        static void WriteComment(uint addr, string comment)
        {
            if (addr == 0) return;
            try
            {
                CoreState.CommentCache?.Update(addr, comment ?? string.Empty);
            }
            catch (Exception ex) { Log.ErrorF("MonsterItemViewerView.WriteComment: {0}", ex.Message); }
        }
    }
}
