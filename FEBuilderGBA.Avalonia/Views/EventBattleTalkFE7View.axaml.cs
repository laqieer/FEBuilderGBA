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
        /// <c>event_ballte_talk2_pointer</c>, which uses 12-byte blocks),
        /// log the hit and do NOT call LoadEntry — the VM assumes 16-byte
        /// blocks and would misparse the 12-byte schema (Copilot review
        /// #522 round 4). The caller (e.g. EventUnitFE7View's
        /// JumpBattleTalk_Click) gets the hit address via the Core helper
        /// <see cref="MapEventUnitCore.FindBattleTalkFE7UnitIdAddress"/>;
        /// the user can manually paste the address into the Address textbox
        /// if a 12-byte-aware secondary list is added in a follow-up.
        /// </summary>
        public void NavigateTo(uint address)
        {
            if (address == 0) return;
            // Try the primary list first.
            EntryList.SelectAddress(address);
            if (_vm.CurrentAddr == address) return;
            // Out-of-list address (table-2 hit). The Table-2 schema is
            // 12-byte but the VM assumes 16-byte — loading the entry here
            // would misparse fields and leave Write enabled against the
            // wrong schema (Copilot review #522 round 4). Log the hit and
            // surface a status update via the address label.
            Log.Notify("EventBattleTalkFE7View.NavigateTo: secondary-table (12-byte) hit at 0x" + address.ToString("X8") + ". Secondary list UI is tracked as a follow-up to PR #522.");
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
