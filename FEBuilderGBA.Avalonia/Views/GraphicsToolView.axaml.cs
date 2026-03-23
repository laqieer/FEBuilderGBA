using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class GraphicsToolView : Window, IEditorView, IDataVerifiableView
    {
        ViewTranslationHelper _translator;

        readonly GraphicsToolViewViewModel _vm = new();

        public string ViewTitle => "Graphics Tool";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public GraphicsToolView()
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

        void Draw_Click(object? sender, RoutedEventArgs e)
        {
            var image = _vm.DrawTiles();
            if (image != null)
            {
                ImageDisplay.Zoom = _vm.Zoom;
                ImageDisplay.SetImage(image);
            }
            else
            {
                ImageDisplay.SetImage(null);
            }
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
