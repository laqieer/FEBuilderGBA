using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusRMenuView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusRMenuViewModel _vm = new();

        public string ViewTitle => "Status R-Menu";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public StatusRMenuView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadStatusRMenuList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("StatusRMenuView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadStatusRMenu(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("StatusRMenuView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UpPtrLabel.Text = $"0x{_vm.UpPtr:X08}";
            DownPtrLabel.Text = $"0x{_vm.DownPtr:X08}";
            LeftPtrLabel.Text = $"0x{_vm.LeftPtr:X08}";
            RightPtrLabel.Text = $"0x{_vm.RightPtr:X08}";
            TextIdLabel.Text = $"0x{_vm.TextId:X04} ({_vm.TextId})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
