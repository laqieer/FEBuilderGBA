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
    public partial class MoveCostEditorView : TranslatedWindow, IEditorView, IDataVerifiableView
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

                    var rowPanel = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 1) };

                    // #1685: terrain names can be long. Keep the fixed 140-wide
                    // label column (so the rows stay aligned) but trim with an
                    // ellipsis inside that width so a long name is truncated
                    // cleanly instead of overflowing into the neighbouring cell.
                    var label = new TextBlock
                    {
                        Text = $"0x{index:X2}",
                        Width = 140,
                        TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 4, 0),
                        FontSize = 11,
                    };
                    _labelFields[index] = label;

                    // #1685: pin an explicit width + left alignment so the spinner buttons
                    // stay inside the field and never overlap the adjacent column.
                    var nud = new NumericUpDown
                    {
                        Minimum = 0,
                        Maximum = 255,
                        Value = 0,
                        Width = 110,
                        HorizontalAlignment = HorizontalAlignment.Left,
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
                ClassList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("MoveCostEditorView.LoadList failed: {0}", ex.Message);
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
                        Log.ErrorF("MoveCostEditorView.OnCostTypeChanged failed: {0}", ex.Message);
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
                Log.ErrorF("MoveCostEditorView.OnClassSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            ClassList.SelectAddress(address);
        }

        /// <summary>
        /// Cross-editor jump overload introduced for #359: navigate to the
        /// given class address AND switch the cost-type combo to the
        /// requested CostType in one call. Used by the new Pointer/Movement/
        /// Terrain Jump buttons in the Class Editor so that, e.g., clicking
        /// "Jump" next to "Move Cost Rain" lands the user on the same class
        /// in the receiving editor with the Rain cost type pre-selected.
        ///
        /// If the requested cost type is not available for the current ROM
        /// (e.g. <see cref="CostType.MoveCostRain"/> on FE6), the combo
        /// retains its current selection — only the class navigation
        /// proceeds. This keeps the behavior graceful when a caller
        /// dispatches before the per-version combo items are populated.
        /// </summary>
        public void NavigateToWithCostType(uint classAddr, CostType costType)
        {
            // Ensure the editor is fully initialized before selecting class/cost type.
            // Window.Opened -> LoadList() normally handles this when the user opens
            // the editor manually, but if NavigateToWithCostType is invoked
            // synchronously after WindowManager.Open<T>() the Opened event may not
            // have fired yet. In that case the combo, terrain names, and ClassList
            // items are all empty, so SelectAddress would no-op and the combo
            // selection logic below could leave SelectedIndex == -1
            // (Copilot bot review feedback). We detect this and run LoadList()
            // eagerly so navigation proceeds as expected.
            if (_vm.CostTypeItems.Count == 0 || ClassList.ItemCount == 0)
            {
                LoadList();
            }

            // Find the combo index for the requested cost type. If absent
            // (FE6 + Rain/Snow), leave the combo untouched and just navigate.
            int targetIndex = -1;
            for (int i = 0; i < _vm.CostTypeItems.Count; i++)
            {
                if (_vm.CostTypeItems[i].CostType == costType)
                {
                    targetIndex = i;
                    break;
                }
            }
            if (targetIndex >= 0 && CostTypeCombo.SelectedIndex != targetIndex)
            {
                // Setting SelectedIndex fires OnCostTypeChanged, which reloads
                // the current class with the new cost type. When no class is
                // loaded yet, that path is a safe no-op; the SelectAddress
                // call below then loads the class with the new cost type
                // already active (because _vm.SelectedCostType is updated by
                // the SelectionChanged handler before SelectAddress fires
                // OnClassSelected).
                CostTypeCombo.SelectedIndex = targetIndex;
            }

            ClassList.SelectAddress(classAddr);
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

                // Update terrain labels with names. The labels are trimmed with
                // an ellipsis (#1685), so mirror the full text into a tooltip so
                // a truncated terrain name is still recoverable on hover (#650).
                for (int i = 0; i < MoveCostEditorViewModel.TerrainCount; i++)
                {
                    if (_vm.TerrainNames != null && i < _vm.TerrainNames.Length)
                        _labelFields[i].Text = _vm.TerrainNames[i];
                    else
                        _labelFields[i].Text = $"0x{i:X2}";
                    ToolTip.SetTip(_labelFields[i], _labelFields[i].Text);
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
                Log.ErrorF("MoveCostEditorView.OnWriteClick failed: {0}", ex.Message);
            }
        }

        public void SelectFirstItem()
        {
            ClassList.SelectFirst();
        }
    }
}
