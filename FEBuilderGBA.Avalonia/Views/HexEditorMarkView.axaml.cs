using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HexEditorMarkView : Window, IEditorView, IDataVerifiableView
    {
        readonly HexEditorMarkViewModel _vm = new();

        public string ViewTitle => "Hex Editor - Bookmarks";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public HexEditorMarkView()
        {
            InitializeComponent();
            _vm.Initialize();
            MarkList.ItemsSource = _vm.Marks;
        }

        void Add_Click(object? sender, RoutedEventArgs e)
        {
            var text = NewMarkInput.Text ?? string.Empty;
            _vm.AddMark(text);
            NewMarkInput.Text = string.Empty;
        }

        void Remove_Click(object? sender, RoutedEventArgs e)
        {
            if (MarkList.SelectedItem is string mark)
                _vm.RemoveMark(mark);
        }

        void Jump_Click(object? sender, RoutedEventArgs e)
        {
            if (MarkList.SelectedItem is string mark)
            {
                _vm.SelectedMark = mark;
                Close(mark);
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close(null);

        public void NavigateTo(uint address) { }
        public void SelectFirstItem()
        {
            if (_vm.Marks.Count > 0)
                MarkList.SelectedIndex = 0;
        }
    }
}
