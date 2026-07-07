using System;
using global::Avalonia;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MoveCostFE6View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MoveCostFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        bool _suppressEvents;

        public string ViewTitle => "Move Cost (FE6)";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Move Cost (FE6) Editor", 1536, 634, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public MoveCostFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            CostTypeCombo.SelectionChanged += OnCostTypeChanged;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("MoveCostFE6View.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("MoveCostFE6View.OnSelected failed: {0}", ex.Message);
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
                        Log.ErrorF("MoveCostFE6View.OnCostTypeChanged failed: {0}", ex.Message);
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
                Log.ErrorF("MoveCostFE6View.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Jump to <paramref name="classAddr"/> with the cost-type combo
        /// pre-selected to <paramref name="costType"/>. Mirrors
        /// <c>MoveCostEditorView.NavigateToWithCostType</c> so FE6 class jumps
        /// (P52=MoveCostNormal, P56=TerrainAvoid, P60=TerrainDefense,
        /// P64=TerrainResistance) land on the correct table without the user
        /// having to manually change the combo.
        ///
        /// Used by <c>ClassFE6View.JumpToMoveCost_Click</c> /
        /// <c>JumpToTerrainAvoid_Click</c> / <c>JumpToTerrainDef_Click</c> /
        /// <c>JumpToTerrainRes_Click</c> (#388).
        /// </summary>
        public void NavigateToWithCostType(uint classAddr, CostType costType)
        {
            // Ensure the editor is fully initialized (matches
            // MoveCostEditorView.NavigateToWithCostType). When invoked
            // synchronously after WindowManager.Open<T>(), the Opened event
            // may not have fired yet, so CostTypeCombo.ItemsSource and
            // EntryList items are empty. Run LoadList eagerly in that case.
            if (_vm.CostTypeItems.Count == 0 || EntryList.ItemCount == 0)
            {
                LoadList();
            }

            // Locate the combo entry matching the requested cost type. The
            // FE6 cost-type list contains MoveCostNormal, TerrainAvoid,
            // TerrainDefense, TerrainResistance, TerrainRecovery (built in
            // MoveCostFE6ViewModel.BuildCostTypeItems()). Rain/Snow are FE7/8
            // only, so a non-matching cost type leaves the combo untouched.
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
                // Setting SelectedIndex fires OnCostTypeChanged which updates
                // _vm.SelectedCostType + reloads the cost data; SelectAddress
                // below then reloads with the new cost type already active.
                CostTypeCombo.SelectedIndex = targetIndex;
            }

            EntryList.SelectAddress(classAddr);
        }
    }
}
