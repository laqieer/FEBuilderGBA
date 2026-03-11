using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillAssignmentUnitCSkillSysView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillAssignmentUnitCSkillSysViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Skill Assignment - Unit (CSkillSys)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillAssignmentUnitCSkillSysView()
        {
            InitializeComponent();
            WriteButton.Click += OnWrite;
            Opened += (_, _) => _vm.Initialize();
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _vm.UnitSkill = (uint)(UnitSkillBox.Value ?? 0);

            _undoService.Begin("Edit Skill Assignment Unit CSkillSys");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillAssignmentUnitCSkillSysView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
