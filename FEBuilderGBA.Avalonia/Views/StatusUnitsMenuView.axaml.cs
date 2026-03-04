using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusUnitsMenuView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusUnitsMenuViewModel _vm = new();

        public string ViewTitle => "Status Units Menu";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public StatusUnitsMenuView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadStatusUnitsMenuList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("StatusUnitsMenuView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadStatusUnitsMenu(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("StatusUnitsMenuView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            OrderLabel.Text = $"0x{_vm.Order:X08} ({_vm.Order})";
            TextIdLabel.Text = $"0x{_vm.TextId:X08} ({_vm.TextId})";
            TextId2Label.Text = $"0x{_vm.TextId2:X08} ({_vm.TextId2})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
