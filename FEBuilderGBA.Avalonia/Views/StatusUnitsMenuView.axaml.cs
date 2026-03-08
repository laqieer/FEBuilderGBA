using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusUnitsMenuView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusUnitsMenuViewModel _vm = new();

        public string ViewTitle => "Status Units Menu";
        public bool IsLoaded => _vm.CanWrite;
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
            OrderBox.Value = _vm.Order;
            TextIdBox.Value = _vm.TextId;
            D8Box.Value = _vm.D8;
            TextId2Box.Value = _vm.TextId2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.Order = (uint)(OrderBox.Value ?? 0);
            _vm.TextId = (uint)(TextIdBox.Value ?? 0);
            _vm.D8 = (uint)(D8Box.Value ?? 0);
            _vm.TextId2 = (uint)(TextId2Box.Value ?? 0);
            _vm.WriteStatusUnitsMenu();
            CoreState.Services?.ShowInfo("Status units menu data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
