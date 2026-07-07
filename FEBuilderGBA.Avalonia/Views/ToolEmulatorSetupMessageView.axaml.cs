
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolEmulatorSetupMessageView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolEmulatorSetupMessageViewModel _vm = new();
        public string ViewTitle => "Emulator is not configured";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Emulator is not configured", 700, 245, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolEmulatorSetupMessageView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void UseInitWizard_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UseInitWizardResult = "wizard";
            RequestClose();
        }

        void ManualSetup_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UseInitWizardResult = "manual";
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
