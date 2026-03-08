using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusRMenuView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusRMenuViewModel _vm = new();

        public string ViewTitle => "Status R-Menu";
        public bool IsLoaded => _vm.CanWrite;
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
            UpPtrBox.Value = _vm.UpPtr;
            DownPtrBox.Value = _vm.DownPtr;
            LeftPtrBox.Value = _vm.LeftPtr;
            RightPtrBox.Value = _vm.RightPtr;
            B16Box.Value = _vm.B16;
            B17Box.Value = _vm.B17;
            TextIdBox.Value = _vm.TextId;
            P20Box.Value = _vm.P20;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.UpPtr = (uint)(UpPtrBox.Value ?? 0);
            _vm.DownPtr = (uint)(DownPtrBox.Value ?? 0);
            _vm.LeftPtr = (uint)(LeftPtrBox.Value ?? 0);
            _vm.RightPtr = (uint)(RightPtrBox.Value ?? 0);
            _vm.B16 = (uint)(B16Box.Value ?? 0);
            _vm.B17 = (uint)(B17Box.Value ?? 0);
            _vm.TextId = (uint)(TextIdBox.Value ?? 0);
            _vm.P20 = (uint)(P20Box.Value ?? 0);
            _vm.WriteStatusRMenu();
            CoreState.Services?.ShowInfo("Status R-Menu data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
