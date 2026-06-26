using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ArenaEnemyWeaponViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ArenaEnemyWeaponViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        List<AddrResult> _basicItems = new();
        List<AddrResult> _rankupItems = new();

        public string ViewTitle => "Arena Enemy Weapon";
        public bool IsLoaded => _vm.CanWrite;

        public ArenaEnemyWeaponViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            RankupEntryList.SelectedAddressChanged += OnRankupSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadArenaEnemyWeaponList();
                _basicItems = items;
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconFromAddrU8Loader(items, i));

                var rankupItems = _vm.LoadArenaEnemyWeaponRankupList();
                _rankupItems = rankupItems;
                RankupEntryList.SetItemsWithIcons(rankupItems, i => ListIconLoaders.ItemIconFromAddrU8Loader(rankupItems, i));
            }
            catch (Exception ex)
            {
                Log.Error("ArenaEnemyWeaponViewerView.LoadList failed: " + ex);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        // ---------------- Basic list ----------------

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadArenaEnemyWeapon(addr);
                int index = IndexOfAddr(_basicItems, addr);
                UpdateUI(index);
            }
            catch (Exception ex)
            {
                Log.Error("ArenaEnemyWeaponViewerView.OnSelected failed: " + ex);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI(int index)
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponIdBox.Value = _vm.WeaponId;
            if (index >= 0)
            {
                var info = _vm.GetBasicTypeInfo(index);
                TypeLabel.Text = info.Label;
                InfoLabel.Text = info.Guidance;
            }
            else
            {
                // No matching slot (reload race / SelectAddress to a non-present
                // addr) — clear so the panel never shows a different slot's label.
                TypeLabel.Text = string.Empty;
                InfoLabel.Text = string.Empty;
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Arena Enemy Weapon");
            try
            {
                _vm.WeaponId = (uint)(WeaponIdBox.Value ?? 0);
                _vm.WriteArenaEnemyWeapon();
                _undoService.Commit();
                _vm.MarkClean();
                ReloadBasicListPreserve(_vm.CurrentAddr);
                CoreState.Services?.ShowInfo("Arena Enemy Weapon data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ArenaEnemyWeaponViewerView.Write: " + ex); }
        }

        void ReloadBasicListPreserve(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadArenaEnemyWeaponList();
                _basicItems = items;
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconFromAddrU8Loader(items, i));
                EntryList.SelectAddress(addr);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        // ---------------- Rank-up list (#1465) ----------------

        void OnRankupSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadArenaEnemyWeaponRankup(addr);
                int index = IndexOfAddr(_rankupItems, addr);
                UpdateRankupUI(index);
            }
            catch (Exception ex)
            {
                Log.Error("ArenaEnemyWeaponViewerView.OnRankupSelected failed: " + ex);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateRankupUI(int index)
        {
            RankupAddrLabel.Text = $"0x{_vm.RankupCurrentAddr:X08}";
            RankupWeaponIdBox.Value = _vm.RankupWeaponId;
            if (index >= 0)
            {
                var info = _vm.GetRankupTypeInfo(index);
                RankupTypeLabel.Text = info.Label;
                RankupInfoLabel.Text = info.Guidance;
            }
            else
            {
                // No matching slot — clear stale label/guidance (see UpdateUI).
                RankupTypeLabel.Text = string.Empty;
                RankupInfoLabel.Text = string.Empty;
            }
        }

        void RankupWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.RankupCanWrite) return;
            _undoService.Begin("Edit Arena Enemy Weapon (Rank-up)");
            try
            {
                _vm.RankupWeaponId = (uint)(RankupWeaponIdBox.Value ?? 0);
                _vm.WriteArenaEnemyWeaponRankup();
                _undoService.Commit();
                _vm.MarkClean();
                ReloadRankupListPreserve(_vm.RankupCurrentAddr);
                CoreState.Services?.ShowInfo("Arena Enemy Weapon (Rank-up) data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ArenaEnemyWeaponViewerView.RankupWrite: " + ex); }
        }

        void ReloadRankupListPreserve(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                var rankupItems = _vm.LoadArenaEnemyWeaponRankupList();
                _rankupItems = rankupItems;
                RankupEntryList.SetItemsWithIcons(rankupItems, i => ListIconLoaders.ItemIconFromAddrU8Loader(rankupItems, i));
                RankupEntryList.SelectAddress(addr);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        // ---------------- Helpers ----------------

        static int IndexOfAddr(List<AddrResult> items, uint addr)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].addr == addr) return i;
            }
            return -1;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
