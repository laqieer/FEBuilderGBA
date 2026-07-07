using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitsShortTextView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly UnitsShortTextViewModel _vm = new();
        readonly UndoService _undoService = new();

        bool _hasLoadedList;
        uint _baseAddr;

        public string ViewTitle => "Units Short Text Editor";
        public new bool IsLoaded => _vm.CanWrite;


        public EditorDescriptor Descriptor => new("Units Short Text Editor", 1155, 551, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public UnitsShortTextView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            // Issue #372: no auto-init from any ROM pointer. This editor is pointer-driven —
            // a vanilla ROM has no unit-short-text table, so opening standalone shows an
            // empty-state explanation until a caller supplies an address via NavigateTo().
        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                UpdateEmptyState();

            }

        }

        /// <summary>
        /// Show the empty-state label when no base address has been supplied; otherwise show
        /// the editor grid and Write button. Called on window open and on NavigateTo().
        /// </summary>
        void UpdateEmptyState()
        {
            bool hasData = _baseAddr != 0;
            EmptyStateLabel.IsVisible = !hasData;
            EditorGrid.IsVisible = hasData;
            WriteButton.IsVisible = hasData;
        }

        public void NavigateTo(uint address)
        {
            _baseAddr = address;
            var items = _vm.BuildList(address);
            EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            UpdateEmptyState();
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
                CoreState.Services?.ShowInfo("Units short text data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("Write failed: {0}", ex.Message);
            }
        }
    }
}
