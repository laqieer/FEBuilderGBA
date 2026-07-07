using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextRefAddDialogView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly TextRefAddDialogViewModel _vm = new();

        public string ViewTitle => "Add Text Reference";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Add Text Reference", 820, 361, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public TextRefAddDialogView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        /// <summary>
        /// Pre-fill the dialog for the Text Editor References-tab "Add Reference"
        /// flow (#1028 Slice A): fix the Text ID to the selected text, seed the
        /// comment box with the existing reference comment, and lock the Text ID
        /// input so the user can only edit the comment (mirrors WinForms
        /// <c>TextRefAddDialogForm.Init</c>).
        /// </summary>
        public void Init(uint textid, string existingComment)
        {
            _vm.Init(textid, existingComment);
            RefIdInput.Value = textid;
            RefTextInput.Text = existingComment ?? "";
            RefIdInput.IsEnabled = false;
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            // When opened via Init the Text ID is fixed to the selected text; only
            // read the (editable) input when not locked. The caller persists via
            // _vm.GetComment() which applies the WF blank-entry convention.
            if (!_vm.IsTextIdLocked)
            {
                _vm.RefId = (int)(RefIdInput.Value ?? 0);
            }
            _vm.Comment = RefTextInput.Text ?? "";
            DialogResult = _vm; RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) { DialogResult = null; RequestClose(); }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
