using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillAssignmentUnitFE8NView : Window, IEditorView
    {
        public string ViewTitle => "Skill Assignment (Unit - FE8N)";
        public bool IsLoaded => false;

        public SkillAssignmentUnitFE8NView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
