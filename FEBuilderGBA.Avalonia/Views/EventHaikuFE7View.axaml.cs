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
        /// <c>event_haiku_tutorial_2_pointer</c> which use 12-byte blocks),
        /// log the hit and do NOT call LoadEntry — the VM assumes the main
        /// 16-byte schema and would misparse the 12-byte tutorial rows
        /// (Copilot review #522 round 4). The Core search helper
        /// <see cref="MapEventUnitCore.FindHaikuFE7Address"/> still routes
        /// the user to the correct tutorial address; the user can manually
        /// open the address if a 12-byte-aware tutorial list is added in a
        /// follow-up.
        /// </summary>
        public void NavigateTo(uint address)
        {
            if (address == 0) return;
            EntryList.SelectAddress(address);
            if (_vm.CurrentAddr == address) return;
            // Out-of-list address (tutorial-table hit). The N1 (tutorial)
            // schema is 12-byte but this VM assumes the main 16-byte
            // schema — loading the entry would misparse fields and leave
            // Write enabled against the wrong schema (Copilot review #522
            // round 4). Log the hit; tutorial-list UI is a follow-up.
            Log.Notify("EventHaikuFE7View.NavigateTo: tutorial-table (12-byte) hit at 0x" + address.ToString("X8") + ". Tutorial list UI is tracked as a follow-up to PR #522.");
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
