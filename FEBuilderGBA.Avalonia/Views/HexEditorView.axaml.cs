using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HexEditorView : Window, IEditorView
    {
        readonly HexEditorViewModel _vm = new();

        public string ViewTitle => "Hex Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public HexEditorView()
        {
            InitializeComponent();
            DataContext = _vm;
            Opened += (_, _) => { _vm.RefreshDisplay(); UpdateUI(); };
        }

        public void NavigateTo(uint address)
        {
            _vm.JumpTo(address);
            UpdateUI();
            AddressBox.Text = $"0x{address:X08}";
        }

        void UpdateUI()
        {
            HexGrid.Text = _vm.HexDisplay;
            InfoLabel.Text = _vm.AddressInfo;
        }

        void Go_Click(object? sender, RoutedEventArgs e)
        {
            uint addr = U.atoh(AddressBox.Text ?? "0");
            _vm.JumpTo(addr);
            UpdateUI();
        }

        void Search_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<HexEditorSearchView>();
        }

        void PageUp_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PageUp();
            UpdateUI();
            AddressBox.Text = $"0x{_vm.BaseAddress:X08}";
        }

        void PageDown_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PageDown();
            UpdateUI();
            AddressBox.Text = $"0x{_vm.BaseAddress:X08}";
        }
    }
}
