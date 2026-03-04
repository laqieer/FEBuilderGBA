using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8NSkillView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8NSkillViewViewModel _vm = new();
        public string ViewTitle => "Skill Configuration (FE8N)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillConfigFE8NSkillView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
