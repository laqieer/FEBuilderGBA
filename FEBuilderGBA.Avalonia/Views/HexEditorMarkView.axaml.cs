using global::Avalonia;
using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HexEditorMarkView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly HexEditorMarkViewModel _vm = new();

        public string ViewTitle => "Marked Addresses";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Marked Addresses", 409, 685, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public HexEditorMarkView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
            AddressList.DoubleTapped += AddressList_DoubleTapped;
            Loaded += (_, _) =>
            {
                if (AddressList.ItemCount > 0)
                    AddressList.SelectedIndex = 0;
            };
        }

        public void Init(List<string> marks)
        {
            _vm.Marks.Clear();
            foreach (var m in marks)
                _vm.Marks.Add(m);
        }

        void AddressList_DoubleTapped(object? sender, TappedEventArgs e)
        {
            JumpTo_Click(sender, new RoutedEventArgs());
        }

        void JumpTo_Click(object? sender, RoutedEventArgs e)
        {
            if (AddressList.SelectedItem is string selected)
            {
                _vm.SelectedMark = selected;
                _vm.DialogResult = "OK";
                DialogResult = selected; RequestClose();
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem()
        {
            if (_vm.Marks.Count > 0)
                AddressList.SelectedIndex = 0;
        }
    }
}
