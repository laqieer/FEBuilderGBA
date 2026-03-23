using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolBGMMuteDialogView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly ToolBGMMuteDialogViewModel _vm = new();
        public string ViewTitle => "BGM Mute Settings";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolBGMMuteDialogView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Toggle_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "toggle";
            Close();
        }

        void OnlyPlay_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "onlyplay";
            Close();
        }

        void PlayAll_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "playall";
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
