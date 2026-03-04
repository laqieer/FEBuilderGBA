using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolRunHintMessageView : Window, IEditorView
    {
        readonly ToolRunHintMessageViewModel _vm = new();
        public string ViewTitle => "Test Run Hint";
        public bool IsLoaded => _vm.IsLoaded;

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
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
