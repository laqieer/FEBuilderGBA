using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.ViewModels;

using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class FERepoResourceBrowserWindow : Window
    {
        ViewTranslationHelper _translator;

        public string SelectedFilePath => (DataContext as FERepoResourceBrowserViewModel)?.SelectedFilePath;

        public FERepoResourceBrowserWindow() : this(false) { }

        public FERepoResourceBrowserWindow(bool musicMode)
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            DataContext = new FERepoResourceBrowserViewModel(musicMode);
            if (musicMode) Title = "FE-Repo Music Browser";
        }

        void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            Close(SelectedFilePath);
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
