using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolBGMMuteDialogView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolBGMMuteDialogViewModel _vm = new();
        public string ViewTitle => "BGM Mute Settings";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("BGM Mute Settings", 583, 320, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolBGMMuteDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Toggle_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "toggle";
            RequestClose();
        }

        void OnlyPlay_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "onlyplay";
            RequestClose();
        }

        void PlayAll_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "playall";
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
