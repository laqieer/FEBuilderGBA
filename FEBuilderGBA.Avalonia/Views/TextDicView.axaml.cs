using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextDicView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextDicViewModel _vm = new();

        public string ViewTitle => "Text Dictionary";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextDicView()
        {
            InitializeComponent();
            _vm.Initialize();
            ResultList.ItemsSource = _vm.Entries;
        }

        void Search_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SearchTerm = SearchBox.Text ?? "";
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(address);
                UpdateUI();
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        public void SelectFirstItem() { if (_vm.Entries.Count > 0) ResultList.SelectedIndex = 0; }

        void UpdateUI()
        {
            TitleIndexBox.Value = (decimal)_vm.TitleIndex;
            ChapterIndexBox.Value = (decimal)_vm.ChapterIndex;
            TextId1Box.Value = (decimal)_vm.TextId1;
            TextId2Box.Value = (decimal)_vm.TextId2;
            Flag1Box.Value = (decimal)_vm.Flag1;
            Flag2Box.Value = (decimal)_vm.Flag2;
            UnitIdBox.Value = (decimal)_vm.UnitId;
            ClassIdBox.Value = (decimal)_vm.ClassId;
        }
    }
}
