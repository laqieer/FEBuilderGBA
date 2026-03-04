using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillAssignmentUnitCSkillSysView : Window, IEditorView
    {
        public string ViewTitle => "Skill Assignment (Unit - CSkillSys)";
        public bool IsLoaded => false;

        public SkillAssignmentUnitCSkillSysView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
