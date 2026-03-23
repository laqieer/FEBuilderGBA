using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolClickWriteFloatControlPanelButtonView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly ToolClickWriteFloatControlPanelButtonViewModel _vm = new();
        public string ViewTitle => "Which button would you click?";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolClickWriteFloatControlPanelButtonView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            DataContext = _vm;
            _vm.Initialize();
        }

        void Update_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "update";
            Close();
        }

        void New_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "new";
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
