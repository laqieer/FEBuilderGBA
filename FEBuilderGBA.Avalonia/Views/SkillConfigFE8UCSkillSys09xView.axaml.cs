using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8UCSkillSys09xView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8UCSkillSys09xViewViewModel _vm = new();
        public string ViewTitle => "Skill Configuration (CSkillSys 0.9.x)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillConfigFE8UCSkillSys09xView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
