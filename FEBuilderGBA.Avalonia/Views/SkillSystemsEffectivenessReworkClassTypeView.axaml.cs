using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillSystemsEffectivenessReworkClassTypeView : Window, IEditorView
    {
        public string ViewTitle => "Skill Systems Effectiveness Rework (Class Type)";
        public bool IsLoaded => false;

        public SkillSystemsEffectivenessReworkClassTypeView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
