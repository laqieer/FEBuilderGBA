using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EmulatorMemoryView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly EmulatorMemoryViewModel _vm = new();
        public string ViewTitle => "Emulator Memory";
        public bool IsLoaded => _vm.IsLoaded;

        public EmulatorMemoryView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            _vm.Initialize();
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
