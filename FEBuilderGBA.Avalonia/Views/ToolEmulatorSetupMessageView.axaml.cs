using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolEmulatorSetupMessageView : Window, IEditorView
    {
        readonly ToolEmulatorSetupMessageViewModel _vm = new();
        public string ViewTitle => "Emulator Setup";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolEmulatorSetupMessageView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void UseInitWizard_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UseInitWizardResult = "wizard";
            Close();
        }

        void ManualSetup_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UseInitWizardResult = "manual";
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
