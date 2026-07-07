
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolRunHintMessageView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolRunHintMessageViewModel _vm = new();
        public string ViewTitle => "Test Run";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Test Run", 965, 480, SizeToContent: true, CanResize: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolRunHintMessageView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = true;
            if (_vm.DoNotShowAgain)
            {
                try
                {
                    CoreState.Config["RunTestMessage"] = "1";
                }
                catch (Exception ex)
                {
                    Log.Error("ToolRunHintMessageView", ex.ToString());
                }
            }
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
