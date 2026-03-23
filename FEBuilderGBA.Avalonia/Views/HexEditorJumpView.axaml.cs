using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HexEditorJumpView : Window, IEditorView, IDataVerifiableView
    {
        ViewTranslationHelper _translator;

        readonly HexEditorJumpViewModel _vm = new();

        public string ViewTitle => "Hex Editor - Jump";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public HexEditorJumpView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
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
            Close(_vm.Address);
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
