using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolClickWriteFloatControlPanelButtonView : Window, IEditorView
    {
        readonly ToolClickWriteFloatControlPanelButtonViewModel _vm = new();
        public string ViewTitle => "Control Panel Button";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolClickWriteFloatControlPanelButtonView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Update_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "update";
            Close();
        }

        void New_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "new";
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
