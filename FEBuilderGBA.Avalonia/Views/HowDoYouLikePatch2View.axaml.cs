using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HowDoYouLikePatch2View : TranslatedUserControl, IEmbeddableEditor
    {
        readonly HowDoYouLikePatch2ViewModel _vm = new();
        public string ViewTitle => "Patch Feedback (Extended)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Patch Review (Extended)", 849, 389, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public HowDoYouLikePatch2View()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Apply_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserApplied = true;
            RequestClose();
        }

        void Skip_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserApplied = false;
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
