using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventHaikuFE7View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventHaikuFE7ViewModel _vm = new();

        public string ViewTitle => "Haiku (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventHaikuFE7View()
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("EventHaikuFE7View.LoadList failed: {0}", ex.Message);
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
                Log.Error("EventHaikuFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        /// <summary>
        /// Select the row whose address matches <paramref name="address"/>.
        /// If the address sits in a table that this view's <see cref="LoadList"/>
        /// did NOT load (e.g. FE7's tutorial tables at
        /// <c>event_haiku_tutorial_1_pointer</c> /
        /// <c>event_haiku_tutorial_2_pointer</c>), fall back to loading the
        /// entry directly into the VM so the detail panel still shows the
        /// user the hit row — the caller (e.g. EventUnitFE7View's
        /// `JumpHaiku_Click`) passes the byte address resolved by
        /// <see cref="MapEventUnitCore.FindHaikuFE7Address"/>
        /// which searches BOTH the main table and the tutorial tables with
        /// the WF exact-match / wildcard / unit-only fallback semantics.
        /// </summary>
        public void NavigateTo(uint address)
        {
            if (address == 0) return;
            EntryList.SelectAddress(address);
            if (_vm.CurrentAddr == address) return;
            // Out-of-list address (e.g. tutorial-table hit) — load the entry
            // directly so the detail panel reflects the jump target.
            try
            {
                _vm.LoadEntry(address);
                UpdateUI();
                // Keep AddrLabel as a pure address string (Copilot review
                // #522 third pass — the English suffix wasn't picked up by
                // the translation layer). The detail panel shows the entry.
                Log.Notify("EventHaikuFE7View.NavigateTo: loaded tutorial-table entry at 0x" + address.ToString("X8"));
            }
            catch (Exception ex)
            {
                Log.Error("EventHaikuFE7View.NavigateTo fallback failed: {0}", ex.Message);
            }
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
