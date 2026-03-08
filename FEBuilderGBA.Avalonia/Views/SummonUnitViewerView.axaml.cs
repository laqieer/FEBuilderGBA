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
            try
            {
                var items = _vm.LoadSummonUnitList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SummonUnitViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadSummonUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SummonUnitViewerView.OnSelected failed: {0}", ex.Message);
            }
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

            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
            _vm.Unknown = (uint)(UnknownBox.Value ?? 0);
            _vm.WriteSummonUnit();
            CoreState.Services.ShowInfo("Summon unit data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
