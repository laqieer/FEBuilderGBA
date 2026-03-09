using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
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
            WriteButton.Click += OnWrite;
            Opened += (_, _) => _vm.Initialize();
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.PersonalSkill = (uint)(PersonalSkillBox.Value ?? 0);
                _vm.SkillSet1 = (uint)(SkillSet1Box.Value ?? 0);
                _vm.SkillSet2 = (uint)(SkillSet2Box.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentUnitFE8NView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
