using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorWarningOverrideView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapStyleEditorWarningOverrideViewModel _vm = new();

        public string ViewTitle => "Map Style Editor - Override Warning";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapStyleEditorWarningOverrideView()
        {
            InitializeComponent();
            _vm.Initialize();
            WarningText.Text = _vm.WarningMessage;
        }

        void OK_Click(object? sender, RoutedEventArgs e) => Close(true);

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
