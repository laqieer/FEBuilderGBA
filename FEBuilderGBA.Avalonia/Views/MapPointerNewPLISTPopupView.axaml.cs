using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapPointerNewPLISTPopupView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapPointerNewPLISTPopupViewModel _vm = new();

        public string ViewTitle => "Map Pointer - New PLIST";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapPointerNewPLISTPopupView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PlistId = (uint)(PlistIdInput.Value ?? 0);
            Close(_vm.PlistId);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
