using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassFontFE8UView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPClassFontFE8UViewModel _vm = new();

        public string ViewTitle => "OP Class Font (FE8U)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public OPClassFontFE8UView()
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
                Log.Error("OPClassFontFE8UView.LoadList failed: {0}", ex.Message);
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
                Log.Error("OPClassFontFE8UView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImagePointerLabel.Text = $"0x{_vm.ImagePointer:X08}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
