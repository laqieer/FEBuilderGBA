
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
namespace FEBuilderGBA.Avalonia.Views
{
    public partial class RAMRewriteToolMAPView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly RAMRewriteToolMAPViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "RAM Rewrite Tool (MAP)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("RAM Rewrite Tool (MAP)", 1456, 966, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public RAMRewriteToolMAPView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // RAM rewrite requires P/Invoke to read/write emulator process memory,
            // which is only available on Windows with a running GBA emulator.
            CoreState.Services?.ShowInfo(
                "RAM rewrite requires a running GBA emulator (Windows only).\n" +
                "This feature uses Windows P/Invoke to access emulator process memory\n" +
                "and is not available in the cross-platform Avalonia build.");
        }
        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
