using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MainSimpleMenuEventErrorIgnoreErrorView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly MainSimpleMenuEventErrorIgnoreErrorViewModel _vm = new();
        public string ViewTitle => "Hide this error";
        public bool IsLoaded => _vm.IsLoaded;

        public MainSimpleMenuEventErrorIgnoreErrorView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            DataContext = _vm;
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = true;
            Close();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = false;
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
