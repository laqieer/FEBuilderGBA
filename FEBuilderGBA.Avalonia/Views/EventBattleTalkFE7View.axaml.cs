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
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
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
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        /// <summary>
        /// Select the row whose address matches <paramref name="address"/>.
        /// If the address sits in a table that this view's <see cref="LoadList"/>
        /// did NOT load (e.g. FE7's secondary table at
        /// <c>event_ballte_talk2_pointer</c>), fall back to loading the entry
        /// directly into the VM so the detail panel still shows the user the
        /// hit row — the caller (e.g. EventUnitFE7View's `JumpBattleTalk_Click`)
        /// passes the byte address resolved by
        /// <see cref="MapEventUnitCore.FindBattleTalkFE7UnitIdAddress"/>
        /// which searches BOTH tables.
        /// </summary>
        public void NavigateTo(uint address)
        {
            if (address == 0) return;
            // Try the primary list first.
            EntryList.SelectAddress(address);
            if (_vm.CurrentAddr == address) return;
            // Out-of-list address (e.g. table-2 hit) — load the entry directly
            // so the detail panel reflects the jump target.
            try
            {
                _vm.LoadEntry(address);
                UpdateUI();
                AddrLabel.Text = string.Format("0x{0:X08} (jumped — see #431 table-2)", _vm.CurrentAddr);
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleTalkFE7View.NavigateTo fallback failed: {0}", ex.Message);
            }
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
