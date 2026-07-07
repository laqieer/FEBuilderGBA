
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolThreeMargeCloseAlertView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolThreeMargeCloseAlertViewModel _vm = new();
        public string ViewTitle => "Do you want to close the comparison tool?";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Do you want to close the comparison tool?", 820, 380, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolThreeMargeCloseAlertView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "cancel";
            RequestClose();
        }

        void No_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "no";
            RequestClose();
        }

        void Yes_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "yes";
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
