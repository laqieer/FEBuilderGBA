using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8NSkillView : Window, IEditorView
    {
        public string ViewTitle => "Skill Config (FE8N)";
        public bool IsLoaded => false;

        public SkillConfigFE8NSkillView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
