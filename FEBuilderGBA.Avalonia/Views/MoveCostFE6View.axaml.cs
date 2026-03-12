using System;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MoveCostFE6View : Window, IEditorView, IDataVerifiableView
    {
        readonly MoveCostFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressEvents;

        public string ViewTitle => "Move Cost (FE6)";
        public bool IsLoaded => _vm.CanWrite;

        public MoveCostFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            CostTypeCombo.SelectionChanged += OnCostTypeChanged;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.BuildCostTypeItems();

                _suppressEvents = true;
                CostTypeCombo.ItemsSource = _vm.CostTypeItems;
                if (_vm.CostTypeItems.Count > 0)
                    CostTypeCombo.SelectedIndex = 0;
                _suppressEvents = false;

                var items = _vm.LoadClassList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MoveCostFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMoveCost(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MoveCostFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void OnCostTypeChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (CostTypeCombo.SelectedItem is CostTypeItem item)
            {
                _vm.SelectedCostType = item.CostType;
                _vm.SelectedCostTypeIndex = CostTypeCombo.SelectedIndex;

                if (_vm.CurrentAddr != 0)
                {
                    try
                    {
                        _vm.LoadMoveCost(_vm.CurrentAddr, item.CostType);
                        UpdateUI();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("MoveCostFE6View.OnCostTypeChanged failed: {0}", ex.Message);
                    }
                }
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassNameLabel.Text = _vm.ClassName;
            CostTypeHeading.Text = $"{_vm.SelectedCostType} (51 entries):";

            if (_vm.MoveCosts.Length == 0)
            {
                MoveCostsLabel.Text = "(no move cost data)";
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < _vm.MoveCosts.Length; i++)
            {
                if (i > 0 && i % 10 == 0) sb.AppendLine();
                sb.Append($"[{i:X2}]={_vm.MoveCosts[i]:X2} ");
            }
            MoveCostsLabel.Text = sb.ToString();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _undoService.Begin("Edit Move Cost FE6");
            try
            {
                _vm.WriteMoveCost();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Move cost data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MoveCostFE6View.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
