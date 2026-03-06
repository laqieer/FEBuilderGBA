using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HexEditorSearchView : Window, IEditorView, IDataVerifiableView
    {
        readonly HexEditorSearchViewModel _vm = new();

        public string ViewTitle => "Hex Editor - Search";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public HexEditorSearchView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        public void Init(List<string> list)
        {
            var reversed = new List<string>();
            for (int i = list.Count - 1; i >= 0; i--)
                reversed.Add(list[i]);
            AddrComboBox.ItemsSource = reversed;
            if (reversed.Count > 0)
                AddrComboBox.Text = reversed[0];
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SearchText = AddrComboBox.Text ?? "";
            _vm.IsReverse = RevCheckBox.IsChecked == true;
            _vm.IsLittleEndian = LittleEndianCheckBox.IsChecked == true;
            _vm.IsAlign4 = Align4CheckBox.IsChecked == true;
            _vm.DialogResult = "OK";
            Close(_vm.SearchText);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
