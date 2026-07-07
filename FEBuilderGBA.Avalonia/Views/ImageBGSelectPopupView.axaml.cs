using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBGSelectPopupView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ImageBGSelectPopupViewViewModel _vm = new();

        public string ViewTitle => "BG Image Select";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("BG Image Select", 945, 594, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ImageBGSelectPopupView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Select_Click(object? sender, RoutedEventArgs e)
        {
            var selected = BGList.SelectedItem;
            DialogResult = selected; RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) { DialogResult = null; RequestClose(); }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
