
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
namespace FEBuilderGBA.Avalonia.Views
{
    public partial class RAMRewriteToolView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly RAMRewriteToolViewViewModel _vm = new();
        public string ViewTitle => "RAM Rewrite Tool";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("RAM Rewrite Tool", 463, 486, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public RAMRewriteToolView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
