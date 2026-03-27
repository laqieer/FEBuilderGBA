using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitsShortTextView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly UnitsShortTextViewModel _vm = new();
        readonly UndoService _undoService = new();
        uint _baseAddr;

        public string ViewTitle => "Units Short Text Editor";
        public bool IsLoaded => _vm.CanWrite;

        public UnitsShortTextView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => AutoInitIfNeeded();
        }

        /// <summary>
        /// If no external NavigateTo call provided a base address, try to find
        /// a default unit short text table from ROM for standalone operation.
        /// Uses event_haiku_pointer (death quotes) which has the same format.
        /// </summary>
        void AutoInitIfNeeded()
        {
            if (_baseAddr != 0) return; // Already initialized via NavigateTo

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            uint ptr = rom.RomInfo.event_haiku_pointer;
            if (ptr == 0) return;

            uint addr = rom.p32(ptr);
            if (U.isSafetyOffset(addr, rom) && addr + 0x46 * 2 <= (uint)rom.Data.Length)
            {
                NavigateTo(addr);
            }
        }

        public void NavigateTo(uint address)
        {
            _baseAddr = address;
            var items = _vm.BuildList(address);
            EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitLoader(items, i));
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitLoader(items, i));
                CoreState.Services?.ShowInfo("Units short text data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }
    }
}
