using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextRefAddDialogView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextRefAddDialogViewModel _vm = new();

        public string ViewTitle => "Add Text Reference";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextRefAddDialogView()
        {
            InitializeComponent();
            _vm.Initialize();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.RefId = (int)(RefIdInput.Value ?? 0);
            _vm.RefText = RefTextInput.Text ?? "";
            Close(_vm);
        }

        void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
