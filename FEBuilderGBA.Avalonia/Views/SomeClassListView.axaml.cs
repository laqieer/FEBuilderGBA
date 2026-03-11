using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SomeClassListView : Window, IEditorView, IDataVerifiableView
    {
        readonly SomeClassListViewModel _vm = new();
        readonly UndoService _undoService = new();
        uint _baseAddr;

        public string ViewTitle => "Class List Editor";
        public bool IsLoaded => _vm.CanWrite;

        public SomeClassListView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
        }

        public void NavigateTo(uint address)
        {
            _baseAddr = address;
            LoadList();
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        void LoadList()
        {
            var items = _vm.BuildList(_baseAddr);
            EntryList.SetItems(items);
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
                Log.Error("SomeClassListView.Write_Click failed: {0}", ex.Message);
            }
        }
    }
}
