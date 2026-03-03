using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventCondView : Window, IEditorView
    {
        readonly EventCondViewModel _vm = new();

        public string ViewTitle => "Event Condition";
        public bool IsLoaded => _vm.IsLoaded;

        public EventCondView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadEventCondList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("EventCondView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEventCond(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EventCondView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            DataSizeLabel.Text = _vm.MapDataSize.ToString() + " bytes";
            RawBytesLabel.Text = _vm.RawBytes;
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }
    }
}
