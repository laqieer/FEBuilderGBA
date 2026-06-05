using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventBattleTalkFE7View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventBattleTalkFE7ViewModel _vm = new();

        public string ViewTitle => "Battle Dialogue (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventBattleTalkFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            TableFilter.SelectionChanged += TableFilter_SelectionChanged;
            Opened += (_, _) => LoadList();
        }

        EventBattleTalkFE7ViewModel.BattleTalkTable SelectedTable =>
            TableFilter.SelectedIndex == 1
                ? EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary
                : EventBattleTalkFE7ViewModel.BattleTalkTable.Main;

        void TableFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList(SelectedTable);
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitFromAddrU8Loader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleTalkFE7View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleTalkFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            // Route through R._() at assignment time — TranslatedWindow.TranslateAll()
            // runs once at window open, so values assigned afterward must be
            // localized explicitly to apply in ja/zh (#958 review).
            TableLabel.Text = R._(_vm.IsSecondaryTable ? "Secondary (12-byte)" : "Main (16-byte)");
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        /// <summary>
        /// Select the row whose address matches <paramref name="address"/>.
        /// Resolves which physical table the address belongs to — the main
        /// 16-byte table or the secondary 12-byte table
        /// (<c>event_ballte_talk2_pointer</c>) — switches the Table filter combo
        /// to that table (reloading the list under the correct schema), then
        /// selects the row (#957 W1b). Previously out-of-list (secondary) hits
        /// were only logged because the VM assumed the 16-byte schema.
        /// </summary>
        public void NavigateTo(uint address)
        {
            if (address == 0) return;

            // First try the currently-loaded table.
            EntryList.SelectAddress(address);
            if (_vm.CurrentAddr == address) return;

            int targetIndex = ResolveTableIndexFor(address);
            if (targetIndex >= 0 && targetIndex != TableFilter.SelectedIndex)
            {
                // Setting SelectedIndex fires TableFilter_SelectionChanged -> LoadList().
                TableFilter.SelectedIndex = targetIndex;
                EntryList.SelectAddress(address);
                if (_vm.CurrentAddr == address) return;
            }

            Log.Notify("EventBattleTalkFE7View.NavigateTo: address 0x" + address.ToString("X8") + " not found in any battle-talk table.");
        }

        /// <summary>
        /// Returns the Table filter combo index (0=Main, 1=Secondary) whose
        /// data region contains <paramref name="address"/>, or -1 if none.
        /// Read-only: does not mutate the VM's current-table state.
        /// </summary>
        int ResolveTableIndexFor(uint address)
        {
            var rom = CoreState.ROM;
            if (rom == null) return -1;

            // Main: 16-byte stride, stop on u16==0 || u16==0xFFFF.
            uint mainBase = EventBattleTalkFE7ViewModel.ResolveBaseAddr(rom, EventBattleTalkFE7ViewModel.BattleTalkTable.Main);
            if (mainBase != 0 && address >= mainBase && (address - mainBase) % 16 == 0)
            {
                for (uint a = mainBase; a + 16 <= (uint)rom.Data.Length; a += 16)
                {
                    uint u = rom.u16(a);
                    if (u == 0 || u == 0xFFFF) break;
                    if (a == address) return 0;
                }
            }

            // Secondary: 12-byte stride, stop on u8==0 || u8==0xFF.
            uint secBase = EventBattleTalkFE7ViewModel.ResolveBaseAddr(rom, EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary);
            if (secBase != 0 && address >= secBase && (address - secBase) % 12 == 0)
            {
                for (uint a = secBase; a + 12 <= (uint)rom.Data.Length; a += 12)
                {
                    uint u = rom.u8(a);
                    if (u == 0 || u == 0xFF) break;
                    if (a == address) return 1;
                }
            }
            return -1;
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
