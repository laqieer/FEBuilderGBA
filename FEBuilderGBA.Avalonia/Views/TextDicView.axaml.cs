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
        readonly UndoService _undoService = new();

        public string ViewTitle => "Text Dictionary";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextDicView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            var items = _vm.BuildList();
            EntryList.SetItems(items);
            _vm.Initialize();
        }

        void OnSelected(uint address)
        {
            _vm.LoadEntry(address);
            UpdateUI();
        }

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
            Text1PreviewLabel.Text = _vm.Text1Preview;
            Text2PreviewLabel.Text = _vm.Text2Preview;
            UnitNameLabel.Text = _vm.UnitName;
            ClassNameLabel.Text = _vm.ClassNameDisplay;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

            _vm.TitleIndex = (uint)(TitleIndexBox.Value ?? 0);
            _vm.ChapterIndex = (uint)(ChapterIndexBox.Value ?? 0);
            _vm.TextId1 = (uint)(TextId1Box.Value ?? 0);
            _vm.TextId2 = (uint)(TextId2Box.Value ?? 0);
            _vm.Flag1 = (uint)(Flag1Box.Value ?? 0);
            _vm.Flag2 = (uint)(Flag2Box.Value ?? 0);
            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
            _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);

            _undoService.Begin("Edit Text Dictionary");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                LoadList();
                CoreState.Services?.ShowInfo("Text dictionary data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("TextDicView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
