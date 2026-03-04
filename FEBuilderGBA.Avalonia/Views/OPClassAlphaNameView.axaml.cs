using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassAlphaNameView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassAlphaNameViewModel _vm = new();

        public string ViewTitle => "OP Class Alpha Name";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassAlphaNameView()
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
                EntryList.SetItems(items);
                if (!string.IsNullOrEmpty(_vm.UnavailableMessage))
                    UnavailableLabel.Text = _vm.UnavailableMessage;
            }
            catch (Exception ex)
            {
                Log.Error("OPClassAlphaNameView.LoadList failed: {0}", ex.Message);
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
                Log.Error("OPClassAlphaNameView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AlphaNameLabel.Text = _vm.AlphaName;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
