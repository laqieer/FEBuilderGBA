using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextRefAddDialogView : Window, IEditorView, IDataVerifiableView
    {
        ViewTranslationHelper _translator;

        readonly TextRefAddDialogViewModel _vm = new();

        public string ViewTitle => "Add Text Reference";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextRefAddDialogView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.RefId = (int)(RefIdInput.Value ?? 0);
            _vm.Comment = RefTextInput.Text ?? "";
            Close(_vm);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
