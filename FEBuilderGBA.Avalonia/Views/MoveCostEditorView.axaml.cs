using System;
using System.Collections.Generic;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Layout;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MoveCostEditorView : Window, IEditorView, IDataVerifiableView
    {
        readonly MoveCostEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        readonly NumericUpDown[] _nudFields = new NumericUpDown[MoveCostEditorViewModel.TerrainCount];
        readonly TextBlock[] _labelFields = new TextBlock[MoveCostEditorViewModel.TerrainCount];
        bool _suppressEvents;

        public string ViewTitle => "Move Cost Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MoveCostEditorView()
        {
            InitializeComponent();
            BuildTerrainGrid();
            ClassList.SelectedAddressChanged += OnClassSelected;
            WriteButton.Click += OnWriteClick;
            CostTypeCombo.SelectionChanged += OnCostTypeChanged;
            Opened += (_, _) => LoadList();
        }

        void BuildTerrainGrid()
        {
            // Layout: 5 columns, 13 rows each = 65 fields
            // Each column has pairs of (Label, NumericUpDown) stacked vertically
            int columns = 5;
            int rowsPerCol = 13;

            for (int col = 0; col < columns; col++)
            {
                var colPanel = new StackPanel { Spacing = 2, Margin = new Thickness(4) };

                for (int row = 0; row < rowsPerCol; row++)
                {
                    int index = col * rowsPerCol + row;
                    if (index >= MoveCostEditorViewModel.TerrainCount)
                        break;

                    var rowPanel = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 1) };

                    var label = new TextBlock
                    {
                        Text = $"0x{index:X2}",
                        Width = 140,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 11,
                    };
                    _labelFields[index] = label;

                    var nud = new NumericUpDown
                    {
                        Minimum = 0,
                        Maximum = 255,
                        Value = 0,
                        Width = 80,
                        FontSize = 11,
                        Tag = index,
                    };
                    nud.ValueChanged += OnTerrainCostChanged;
                    _nudFields[index] = nud;

                    rowPanel.Children.Add(label);
                    rowPanel.Children.Add(nud);
                    colPanel.Children.Add(rowPanel);
                }

                Grid.SetColumn(colPanel, col);
                TerrainGrid.Children.Add(colPanel);
            }
        }

        void LoadList()
        {
            try
            {
                _vm.BuildCostTypeItems();
                _vm.LoadTerrainNames();

                // Populate cost type combo
                _suppressEvents = true;
                CostTypeCombo.ItemsSource = _vm.CostTypeItems;
                if (_vm.CostTypeItems.Count > 0)
                    CostTypeCombo.SelectedIndex = 0;
                _suppressEvents = false;

                var items = _vm.LoadClassList();
                ClassList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MoveCostEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnCostTypeChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (CostTypeCombo.SelectedItem is CostTypeItem item)
            {
                _vm.SelectedCostType = item.CostType;
                _vm.SelectedCostTypeIndex = CostTypeCombo.SelectedIndex;

                // Reload the current class with the new cost type
                if (_vm.CurrentAddr != 0)
                {
                    try
                    {
                        _vm.LoadMoveCost(_vm.CurrentAddr, item.CostType);
                        UpdateUI();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("MoveCostEditorView.OnCostTypeChanged failed: {0}", ex.Message);
                    }
                }
            }
        }

        void OnClassSelected(uint addr)
        {
            try
            {
                _vm.LoadMoveCost(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MoveCostEditorView.OnClassSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            ClassList.SelectAddress(address);
        }

        void UpdateUI()
        {
            _suppressEvents = true;
            try
            {
                ClassNameLabel.Text = _vm.ClassName;
                AddrLabel.Text = $"Class: 0x{_vm.CurrentAddr:X08}";
                MoveCostAddrLabel.Text = $"Table: 0x{_vm.MoveCostAddr:X08}";
                CostTypeLabel.Text = $"{_vm.SelectedCostType} (65 terrains: 0x00 - 0x40):";

                // Update terrain labels with names
                for (int i = 0; i < MoveCostEditorViewModel.TerrainCount; i++)
                {
                    if (_vm.TerrainNames != null && i < _vm.TerrainNames.Length)
                        _labelFields[i].Text = _vm.TerrainNames[i];
                    else
                        _labelFields[i].Text = $"0x{i:X2}";
                }

                // Update all 65 NumericUpDown values from the ViewModel
                if (!_vm.CanWrite)
                {
                    for (int i = 0; i < MoveCostEditorViewModel.TerrainCount; i++)
                    {
                        _nudFields[i].Value = 0;
                        _nudFields[i].IsEnabled = false;
                    }
                    return;
                }

                // Read all costs from _vm.MoveCosts array for UI display
                byte[] costs = _vm.MoveCosts;
                for (int i = 0; i < MoveCostEditorViewModel.TerrainCount; i++)
                {
                    _nudFields[i].Value = costs[i];
                    _nudFields[i].IsEnabled = true;
                }
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        void OnTerrainCostChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEvents) return;
            if (sender is NumericUpDown nud && nud.Tag is int index)
            {
                byte val = (byte)(nud.Value ?? 0);
                _vm.SetCost(index, val);
            }
        }

        void OnWriteClick(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Move Cost");
            try
            {
                _vm.WriteMoveCost();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MoveCostEditorView.OnWriteClick failed: {0}", ex.Message);
            }
        }

        public void SelectFirstItem()
        {
            ClassList.SelectFirst();
        }
    }
}
