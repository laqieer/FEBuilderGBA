using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SummonUnitViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SummonUnitViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Summon Unit";
        public bool IsLoaded => _vm.CanWrite;

        public SummonUnitViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSummonUnitList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SummonUnitViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSummonUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SummonUnitViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            UnknownBox.Value = _vm.Unknown;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Summon Unit");
            try
            {
                _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
                _vm.Unknown = (uint)(UnknownBox.Value ?? 0);
                _vm.WriteSummonUnit();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Summon unit data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("SummonUnitViewerView.Write: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
