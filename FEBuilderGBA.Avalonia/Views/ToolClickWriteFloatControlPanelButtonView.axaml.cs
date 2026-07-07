
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolClickWriteFloatControlPanelButtonView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolClickWriteFloatControlPanelButtonViewModel _vm = new();
        public string ViewTitle => "Which button would you click?";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Which button would you click?", 920, 220, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolClickWriteFloatControlPanelButtonView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Update_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "update";
            RequestClose();
        }

        void New_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "new";
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
