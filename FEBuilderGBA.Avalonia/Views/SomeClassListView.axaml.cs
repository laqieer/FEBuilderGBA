using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SomeClassListView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SomeClassListViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        uint _baseAddr;

        public string ViewTitle => "Class List Editor";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Class List Editor", 1155, 661, SizeToContent: true);
        public event EventHandler? CloseRequested;

        public SomeClassListView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                AutoInitIfNeeded();
            }
        }

        /// <summary>
        /// If no external NavigateTo call provided a base address, try to find
        /// a default class list from CC item pointers for standalone operation.
        /// </summary>
        void AutoInitIfNeeded()
        {
            if (_baseAddr != 0) return; // Already initialized via NavigateTo

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            // Try cc_item_hero_crest_pointer — contains a null-terminated class list
            uint ptr = rom.RomInfo.cc_item_hero_crest_pointer;
            if (ptr != 0)
            {
                uint addr = rom.p32(ptr);
                if (U.isSafetyOffset(addr, rom) && rom.u8(addr) != 0)
                {
                    NavigateTo(addr);
                    return;
                }
            }
        }

        public void NavigateTo(uint address)
        {
            _baseAddr = address;
            LoadList();
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        void LoadList()
        {
            var items = _vm.BuildList(_baseAddr);
            EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));
        }

        void OnSelected(uint address)
        {
            _vm.LoadEntry(address);
            UpdateUI();
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            B0Box.Value = _vm.ClassId;
            ClassNameLabel.Text = _vm.ClassDisplayName;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.ClassId = (uint)(B0Box.Value ?? 0);

            _undoService.Begin("Edit Class List");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                // Refresh name display
                _vm.ClassDisplayName = NameResolver.GetClassName(_vm.ClassId);
                UpdateUI();
                LoadList();
                CoreState.Services?.ShowInfo("Class list data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SomeClassListView.Write_Click failed: {0}", ex.Message);
            }
        }
    }
}
