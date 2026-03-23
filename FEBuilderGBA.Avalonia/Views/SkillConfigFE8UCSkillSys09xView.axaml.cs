using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8UCSkillSys09xView : Window, IEditorView, IDataVerifiableView
    {
        ViewTranslationHelper _translator;

        readonly SkillConfigFE8UCSkillSys09xViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Skill Configuration (CSkillSys 0.9.x)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillConfigFE8UCSkillSys09xView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            WriteButton.Click += OnWrite;
            Opened += (_, _) => _vm.Initialize();
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _vm.SkillName = (uint)(SkillNameBox.Value ?? 0);
            _vm.Description = (uint)(DescriptionBox.Value ?? 0);

            _undoService.Begin("Edit Skill Config CSkillSys 0.9.x");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillConfigFE8UCSkillSys09xView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
