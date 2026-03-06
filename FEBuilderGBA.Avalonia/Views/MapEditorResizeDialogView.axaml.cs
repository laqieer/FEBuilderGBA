using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapEditorResizeDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapEditorResizeDialogViewModel _vm = new();

        public string ViewTitle => "Map Resize";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

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
            _vm.X = (int)(XInput.Value ?? 0);
            _vm.Y = (int)(YInput.Value ?? 0);
            _vm.T = (int)(TInput.Value ?? 0);
            _vm.L = (int)(LInput.Value ?? 0);
            _vm.R = (int)(RInput.Value ?? 0);
            _vm.B = (int)(BInput.Value ?? 0);
            _vm.DialogResult = "OK";
            Close(true);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
