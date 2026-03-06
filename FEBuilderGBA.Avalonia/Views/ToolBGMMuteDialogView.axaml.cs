using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolBGMMuteDialogView : Window, IEditorView
    {
        readonly ToolBGMMuteDialogViewModel _vm = new();
        public string ViewTitle => "BGM Mute Settings";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolBGMMuteDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        void Toggle_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "toggle";
            Close();
        }

        void OnlyPlay_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "onlyplay";
            Close();
        }

        void PlayAll_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "playall";
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
