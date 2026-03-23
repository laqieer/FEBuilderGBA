using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PatchFormUninstallDialogView : Window, IEditorView
    {
        ViewTranslationHelper _translator;

        readonly PatchFormUninstallDialogViewModel _vm = new();
        public string ViewTitle => "Patch Uninstallation";
        public bool IsLoaded => _vm.IsLoaded;

        public PatchFormUninstallDialogView()
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

        async void SelectOriginal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path))
                    _vm.OriginalFilename = path;
            }
            catch (Exception ex)
            {
                Log.Error("PatchFormUninstallDialogView", ex.ToString());
            }
        }

        void Yes_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserConfirmed = true;
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
