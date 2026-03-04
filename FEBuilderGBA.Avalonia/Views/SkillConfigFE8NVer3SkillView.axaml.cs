using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8NVer3SkillView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8NVer3SkillViewViewModel _vm = new();
        public string ViewTitle => "Skill Configuration (FE8N v3)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillConfigFE8NVer3SkillView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
