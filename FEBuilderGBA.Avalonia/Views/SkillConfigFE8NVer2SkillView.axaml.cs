using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8NVer2SkillView : Window, IEditorView
    {
        public string ViewTitle => "Skill Config (FE8N Ver2)";
        public bool IsLoaded => false;

        public SkillConfigFE8NVer2SkillView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
