using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorAddMapChangeDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapEditorAddMapChangeDialogViewModel _vm = new();

        public string ViewTitle => "Map Editor - Add Map Change";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapEditorAddMapChangeDialogView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.MapChangeId = (uint)(MapChangeIdInput.Value ?? 0);
            Close(_vm.MapChangeId);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
