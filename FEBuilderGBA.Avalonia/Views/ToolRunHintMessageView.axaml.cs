using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolRunHintMessageView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly ToolRunHintMessageViewModel _vm = new();
        public string ViewTitle => "Test Run";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolRunHintMessageView()
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
            if (_vm.DoNotShowAgain)
            {
                try
                {
                    CoreState.Config["RunTestMessage"] = "1";
                }
                catch (Exception ex)
                {
                    Log.Error("ToolRunHintMessageView", ex.ToString());
                }
            }
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
