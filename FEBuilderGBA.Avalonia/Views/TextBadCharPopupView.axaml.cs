using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextBadCharPopupView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly TextBadCharPopupViewModel _vm = new();

        public string ViewTitle => "Bad Character Warning";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Bad Character Warning", 849, 469, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public TextBadCharPopupView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Load();
        }

        public TextBadCharPopupView(string warningText) : this()
        {
            LoadWarning(warningText);
        }

        public void LoadWarning(string warningText)
        {
            _vm.Load(warningText);
            ErrorMessageLabel.Text = warningText;
        }

        void GiveUp_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedAction = "GiveUp";
            DialogResult = "GiveUp"; RequestClose();
        }

        void AntiHuffman_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedAction = "AntiHuffman";
            DialogResult = "AntiHuffman"; RequestClose();
        }

        void EncodingTable_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedAction = "EncodingTable";
            DialogResult = "EncodingTable"; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
