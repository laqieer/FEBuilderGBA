using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitPaletteView : Window, IEditorView, IDataVerifiableView
    {
        readonly UnitPaletteViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Unit Palette Assignment";
        public bool IsLoaded => _vm.IsLoaded;

        public UnitPaletteView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("UnitPaletteView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("UnitPaletteView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TraineeClassBox.Value = _vm.TraineeClass;
            BaseClass1Box.Value = _vm.BaseClass1;
            BaseClass2Box.Value = _vm.BaseClass2;
            AdvancedClass1Box.Value = _vm.AdvancedClass1;
            AdvancedClass2Box.Value = _vm.AdvancedClass2;
            AdvancedClass3Box.Value = _vm.AdvancedClass3;
            AdvancedClass4Box.Value = _vm.AdvancedClass4;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _vm.TraineeClass = (uint)(TraineeClassBox.Value ?? 0);
            _vm.BaseClass1 = (uint)(BaseClass1Box.Value ?? 0);
            _vm.BaseClass2 = (uint)(BaseClass2Box.Value ?? 0);
            _vm.AdvancedClass1 = (uint)(AdvancedClass1Box.Value ?? 0);
            _vm.AdvancedClass2 = (uint)(AdvancedClass2Box.Value ?? 0);
            _vm.AdvancedClass3 = (uint)(AdvancedClass3Box.Value ?? 0);
            _vm.AdvancedClass4 = (uint)(AdvancedClass4Box.Value ?? 0);

            _undoService.Begin("Edit Unit Palette");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Unit palette data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
