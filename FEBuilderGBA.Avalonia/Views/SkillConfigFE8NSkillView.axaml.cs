using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8NSkillView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8NSkillViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Skill Configuration (FE8N)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillConfigFE8NSkillView()
        {
            InitializeComponent();
            WriteButton.Click += OnWrite;
            Opened += (_, _) => _vm.Initialize();
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _vm.Icon = (uint)(IconBox.Value ?? 0);
            _vm.Description = (uint)(DescriptionBox.Value ?? 0);
            _vm.ConditionUnit1 = (uint)(ConditionUnit1Box.Value ?? 0);
            _vm.ConditionUnit2 = (uint)(ConditionUnit2Box.Value ?? 0);
            _vm.ConditionUnit3 = (uint)(ConditionUnit3Box.Value ?? 0);
            _vm.ConditionUnit4 = (uint)(ConditionUnit4Box.Value ?? 0);
            _vm.ConditionClass1 = (uint)(ConditionClass1Box.Value ?? 0);
            _vm.ConditionClass2 = (uint)(ConditionClass2Box.Value ?? 0);
            _vm.ConditionClass3 = (uint)(ConditionClass3Box.Value ?? 0);
            _vm.ConditionClass4 = (uint)(ConditionClass4Box.Value ?? 0);
            _vm.ConditionItem1 = (uint)(ConditionItem1Box.Value ?? 0);
            _vm.ConditionItem2 = (uint)(ConditionItem2Box.Value ?? 0);
            _vm.ConditionItem3 = (uint)(ConditionItem3Box.Value ?? 0);
            _vm.ConditionItem4 = (uint)(ConditionItem4Box.Value ?? 0);

            _undoService.Begin("Edit Skill Config FE8N");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillConfigFE8NSkillView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
