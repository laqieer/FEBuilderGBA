using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitsShortTextView : Window, IEditorView, IDataVerifiableView
    {
        ViewTranslationHelper _translator;

        readonly UnitsShortTextViewModel _vm = new();
        readonly UndoService _undoService = new();
        uint _baseAddr;

        public string ViewTitle => "Units Short Text Editor";
        public bool IsLoaded => _vm.CanWrite;

        public UnitsShortTextView()
        {
            InitializeComponent();
            // Translation support
            _translator = new ViewTranslationHelper(this);
            _translator.TranslateAll();
            CoreState.LanguageChanged += _translator.OnLanguageChanged;
            EntryList.SelectedAddressChanged += OnSelected;
        }

        public void NavigateTo(uint address)
        {
            _baseAddr = address;
            var items = _vm.BuildList(address);
            EntryList.SetItems(items);
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        void OnSelected(uint address)
        {
            _vm.LoadEntry(address);
            UpdateUI();
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitNameLabel.Text = _vm.UnitName;
            TextIdBox.Value = _vm.TextId;
            TextPreviewLabel.Text = _vm.TextPreview;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.TextId = (uint)(TextIdBox.Value ?? 0);

            _undoService.Begin("Edit Units Short Text");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                _vm.TextPreview = _vm.TextId > 0 ? NameResolver.GetTextById(_vm.TextId) : "(empty)";
                UpdateUI();
                // Refresh list
                var items = _vm.BuildList(_baseAddr);
                EntryList.SetItems(items);
                CoreState.Services?.ShowInfo("Units short text data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CoreState.LanguageChanged -= _translator.OnLanguageChanged;
            base.OnClosed(e);
        }
    }
}
