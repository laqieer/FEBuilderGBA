using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorResizeDialogView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapEditorResizeDialogViewModel _vm = new();

        public string ViewTitle => "Map Resize";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Map Resize", 444, 385, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public MapEditorResizeDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        public void SetPosition(int x, int y, int w, int h)
        {
            XInput.Value = x;
            YInput.Value = y;
            WInput.Value = w;
            HInput.Value = h;
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PositionX = (int)(XInput.Value ?? 0);
            _vm.PositionY = (int)(YInput.Value ?? 0);
            _vm.PaddingTop = (int)(TInput.Value ?? 0);
            _vm.PaddingLeft = (int)(LInput.Value ?? 0);
            _vm.PaddingRight = (int)(RInput.Value ?? 0);
            _vm.PaddingBottom = (int)(BInput.Value ?? 0);
            _vm.DialogResult = "OK";
            DialogResult = true; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
