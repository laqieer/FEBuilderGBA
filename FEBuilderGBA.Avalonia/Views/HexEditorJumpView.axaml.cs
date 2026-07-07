using global::Avalonia;
using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HexEditorJumpView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly HexEditorJumpViewModel _vm = new();

        public string ViewTitle => "Hex Editor - Jump";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Hex Editor - Jump", 515, 210, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public HexEditorJumpView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        public void Init(List<string> list)
        {
            var reversed = new List<string>();
            for (int i = list.Count - 1; i >= 0; i--)
                reversed.Add(list[i]);
            AddrComboBox.ItemsSource = reversed;
            if (reversed.Count > 0)
                AddrComboBox.Text = reversed[0];
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.Address = AddrComboBox.Text ?? "";
            _vm.IsLittleEndian = LittleEndianCheckBox.IsChecked == true;
            _vm.DialogResult = "OK";
            DialogResult = _vm.Address; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
