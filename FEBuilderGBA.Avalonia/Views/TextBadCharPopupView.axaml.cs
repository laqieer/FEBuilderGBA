using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextBadCharPopupView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextBadCharPopupViewModel _vm = new();

        public string ViewTitle => "Bad Character Warning";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextBadCharPopupView()
        {
            InitializeComponent();
            _vm.Load();
            WarningTextBlock.Text = _vm.WarningText;
        }

        public TextBadCharPopupView(string warningText) : this()
        {
            _vm.Load(warningText);
            WarningTextBlock.Text = _vm.WarningText;
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
