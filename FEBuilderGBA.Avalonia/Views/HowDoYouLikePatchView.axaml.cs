using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HowDoYouLikePatchView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly HowDoYouLikePatchViewModel _vm = new();
        public string ViewTitle => "Patch Feedback";
        public bool IsLoaded => _vm.IsLoaded;

        public HowDoYouLikePatchView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
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

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
