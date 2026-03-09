using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
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
            WriteButton.Click += OnWrite;
            Opened += (_, _) => _vm.Initialize();
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.SkillName = (uint)(SkillNameBox.Value ?? 0);
                _vm.Description = (uint)(DescriptionBox.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("SkillConfigFE8UCSkillSys09xView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
