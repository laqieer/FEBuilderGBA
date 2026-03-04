using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillAssignmentClassCSkillSysView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillAssignmentClassCSkillSysViewViewModel _vm = new();
        public string ViewTitle => "Skill Assignment - Class (CSkillSys)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillAssignmentClassCSkillSysView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
