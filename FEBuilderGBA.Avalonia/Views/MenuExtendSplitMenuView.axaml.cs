using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MenuExtendSplitMenuView : Window, IEditorView, IDataVerifiableView
    {
        readonly MenuExtendSplitMenuViewModel _vm = new();

        public string ViewTitle => "Menu Extend Split";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MenuExtendSplitMenuView()
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
            }
            catch (Exception ex)
            {
                Log.Error("MenuExtendSplitMenuView.LoadList failed: {0}", ex.Message);
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
                Log.Error("MenuExtendSplitMenuView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            PosXBox.Value = _vm.PosX;
            PosYBox.Value = _vm.PosY;
            WidthBox.Value = _vm.Width;
            StyleBox.Value = _vm.Style;
            Str0Box.Value = _vm.String0;
            Str1Box.Value = _vm.String1;
            Str2Box.Value = _vm.String2;
            Str3Box.Value = _vm.String3;
            Str4Box.Value = _vm.String4;
            Str5Box.Value = _vm.String5;
            Str6Box.Value = _vm.String6;
            Str7Box.Value = _vm.String7;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _vm.PosX = (uint)(PosXBox.Value ?? 0);
            _vm.PosY = (uint)(PosYBox.Value ?? 0);
            _vm.Width = (uint)(WidthBox.Value ?? 0);
            _vm.Style = (uint)(StyleBox.Value ?? 0);
            _vm.String0 = (uint)(Str0Box.Value ?? 0);
            _vm.String1 = (uint)(Str1Box.Value ?? 0);
            _vm.String2 = (uint)(Str2Box.Value ?? 0);
            _vm.String3 = (uint)(Str3Box.Value ?? 0);
            _vm.String4 = (uint)(Str4Box.Value ?? 0);
            _vm.String5 = (uint)(Str5Box.Value ?? 0);
            _vm.String6 = (uint)(Str6Box.Value ?? 0);
            _vm.String7 = (uint)(Str7Box.Value ?? 0);
            _vm.Write();
            CoreState.Services?.ShowInfo("Split menu data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
