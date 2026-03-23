using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextToSpeechView : Window, IEditorView, IDataVerifiableView
    {
        ViewTranslationHelper _translator;

        readonly TextToSpeechViewModel _vm = new();

        public string ViewTitle => "Text to Speech";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextToSpeechView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            _vm.Initialize();
        }

        void Speak_Click(object? sender, RoutedEventArgs e)
        {
            _vm.InputText = InputTextBox.Text ?? "";
            _vm.Status = "Text-to-speech is not available on this platform.";
            StatusLabel.Text = _vm.Status;
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
