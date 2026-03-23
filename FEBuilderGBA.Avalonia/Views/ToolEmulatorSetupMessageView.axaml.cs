using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolEmulatorSetupMessageView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly ToolEmulatorSetupMessageViewModel _vm = new();
        public string ViewTitle => "Emulator is not configured";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolEmulatorSetupMessageView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            DataContext = _vm;
            _vm.Initialize();
        }

        void UseInitWizard_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UseInitWizardResult = "wizard";
            Close();
        }

        void ManualSetup_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UseInitWizardResult = "manual";
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
