using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorAddMapChangeDialogView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapEditorAddMapChangeDialogViewModel _vm = new();

        public string ViewTitle => "Add Map Change";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Add Map Change", 831, 512, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public MapEditorAddMapChangeDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void New_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "new";
            DialogResult = "new"; RequestClose();
        }

        void Edit_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "edit";
            DialogResult = "edit"; RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "cancel";
            DialogResult = null; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
