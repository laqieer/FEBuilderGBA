using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolUndoPopupDialogView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ToolUndoPopupDialogViewModel _vm = new();
        public string ViewTitle => "Undo";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Undo", 771, 355, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolUndoPopupDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        /// <summary>Set the version label/prompt (mirrors WinForms ToolUndoPopupDialogForm.Init).</summary>
        public void Init(string version) => _vm.Init(version);

        void TestPlay_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "TestPlay";
            DialogResult = "TestPlay"; RequestClose();
        }

        void RunUndo_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "RunUndo";
            DialogResult = "RunUndo"; RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "Cancel";
            DialogResult = null; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
