using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8UCSkillSys09xView : Window, IEditorView
    {
        public string ViewTitle => "Skill Config (FE8U CSkillSys 09x)";
        public bool IsLoaded => false;

        public SkillConfigFE8UCSkillSys09xView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
