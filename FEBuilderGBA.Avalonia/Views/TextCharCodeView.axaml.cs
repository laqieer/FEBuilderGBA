using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextCharCodeView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextCharCodeViewModel _vm = new();

        public string ViewTitle => "Text Character Code";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextCharCodeView()
        {
            InitializeComponent();
            _vm.Initialize();
            CharCodeList.ItemsSource = _vm.CharCodes;
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { if (_vm.CharCodes.Count > 0) CharCodeList.SelectedIndex = 0; }
    }
}
