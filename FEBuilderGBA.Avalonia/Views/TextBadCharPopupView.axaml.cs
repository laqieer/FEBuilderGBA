using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextBadCharPopupView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextBadCharPopupViewModel _vm = new();

        public string ViewTitle => "Bad Character Warning";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextBadCharPopupView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Load();
        }

        public TextBadCharPopupView(string warningText) : this()
        {
            _vm.Load(warningText);
            ErrorMessageLabel.Text = warningText;
        }

        void GiveUp_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedAction = "GiveUp";
            Close("GiveUp");
        }

        void AntiHuffman_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedAction = "AntiHuffman";
            Close("AntiHuffman");
        }

        void EncodingTable_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedAction = "EncodingTable";
            Close("EncodingTable");
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
