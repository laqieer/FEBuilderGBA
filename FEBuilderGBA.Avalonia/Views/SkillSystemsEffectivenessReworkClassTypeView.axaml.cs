using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillSystemsEffectivenessReworkClassTypeView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillSystemsEffectivenessReworkClassTypeViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Effectiveness Rework - Class Type";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillSystemsEffectivenessReworkClassTypeView()
        {
            InitializeComponent();
            WriteButton.Click += OnWrite;
            Opened += (_, _) => _vm.Initialize();
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _vm.ClassType = (uint)(ClassTypeBox.Value ?? 0);

            _undoService.Begin("Edit Effectiveness Rework Class Type");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillSystemsEffectivenessReworkClassTypeView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
