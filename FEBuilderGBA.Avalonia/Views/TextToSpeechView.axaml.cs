using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextToSpeechView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly TextToSpeechViewModel _vm = new();

        public string ViewTitle => "Text to Speech";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Text to Speech", 863, 385, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public TextToSpeechView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void Speak_Click(object? sender, RoutedEventArgs e)
        {
            _vm.InputText = InputTextBox.Text ?? "";
            _vm.Status = "Text-to-speech is not available on this platform.";
            StatusLabel.Text = _vm.Status;
        }

        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
