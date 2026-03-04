using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillAssignmentUnitFE8NView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillAssignmentUnitFE8NViewViewModel _vm = new();
        public string ViewTitle => "Skill Assignment - Unit (FE8N)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillAssignmentUnitFE8NView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
