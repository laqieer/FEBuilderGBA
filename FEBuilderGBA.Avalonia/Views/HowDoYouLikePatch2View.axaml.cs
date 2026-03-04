using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HowDoYouLikePatch2View : Window, IEditorView
    {
        readonly HowDoYouLikePatch2ViewModel _vm = new();
        public string ViewTitle => "Patch Feedback (Extended)";
        public bool IsLoaded => _vm.IsLoaded;

        public HowDoYouLikePatch2View()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Apply_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserApplied = true;
            Close();
        }

        void Skip_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserApplied = false;
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
