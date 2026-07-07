using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolWorkSupport_UpdateQuestionDialogView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolWorkSupport_UpdateQuestionDialogViewModel _vm = new();
        public string ViewTitle => "Current version is the latest";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Current version is the latest", 830, 190, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolWorkSupport_UpdateQuestionDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        /// <summary>Set the local work version shown in the dialog (mirrors WF <c>SetVersion</c>).</summary>
        public void SetVersion(string version) => _vm.SetVersion(version);

        void ForceUpdate_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "retry";
            DialogResult = "retry"; RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "cancel";
            DialogResult = "cancel"; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
