using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillAssignmentClassCSkillSysView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillAssignmentClassCSkillSysViewViewModel _vm = new();
        public string ViewTitle => "Skill Assignment - Class (CSkillSys)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillAssignmentClassCSkillSysView()
        {
            InitializeComponent();
            WriteButton.Click += OnWrite;
            Opened += (_, _) => _vm.Initialize();
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.ClassSkill = (uint)(ClassSkillBox.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("SkillAssignmentClassCSkillSysView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
