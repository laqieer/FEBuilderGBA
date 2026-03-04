using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillSystemsEffectivenessReworkClassTypeView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillSystemsEffectivenessReworkClassTypeViewViewModel _vm = new();
        public string ViewTitle => "Effectiveness Rework - Class Type";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillSystemsEffectivenessReworkClassTypeView()
        {
            InitializeComponent();
            Opened += (_, _) => _vm.Initialize();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
