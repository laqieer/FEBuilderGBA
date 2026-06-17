using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitIncreaseHeightView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly UnitIncreaseHeightViewModel _vm = new();
        readonly UndoService _undoService = new();

        // True once Opened -> LoadList() has populated EntryList. A NavigateTo
        // request that arrives BEFORE the list loads (WindowManager.Open then an
        // immediate NavigateToId, but Avalonia raises Opened asynchronously after
        // layout) is stashed in _pendingNavigateAddr and replayed once LoadList
        // completes — otherwise EntryList.SelectAddress no-ops against the
        // still-empty list. Mirrors MonsterProbabilityViewerView (#1018/#1019).
        bool _listLoaded;
        uint? _pendingNavigateAddr;
        bool _syncing;

        public string ViewTitle => "Unit Height Adjustment";
        public bool IsLoaded => _vm.IsLoaded;

        public UnitIncreaseHeightView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;

            // "Stretch" / "Don't Stretch" map to the unit_increase_height_yes / _no marker values.
            HeightCombo.ItemsSource = new[] { R._("Don't Stretch"), R._("Stretch") };
            HeightCombo.SelectionChanged += OnComboChanged;
            HeightValueBox.ValueChanged += OnHeightValueChanged;

            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.PortraitLoader(items, i));
                _listLoaded = true;
            }
            catch (Exception ex)
            {
                Log.Error("UnitIncreaseHeightView.LoadList failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }

            // Replay a navigation requested before the list was ready.
            if (_pendingNavigateAddr is uint pending)
            {
                _pendingNavigateAddr = null;
                EntryList.SelectAddress(pending);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("UnitIncreaseHeightView.OnSelected failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            _syncing = true;
            try
            {
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                HeightValueBox.Value = _vm.HeightValue;
                HeightCombo.SelectedIndex = ComboIndexFor(_vm.HeightValue);
            }
            finally
            {
                _syncing = false;
            }
        }

        static uint NoValue() => CoreState.ROM?.RomInfo?.unit_increase_height_no ?? 0;
        static uint YesValue() => CoreState.ROM?.RomInfo?.unit_increase_height_yes ?? 0;

        static int ComboIndexFor(uint value)
        {
            if (value == YesValue()) return 1;
            if (value == NoValue()) return 0;
            return -1;
        }

        // Picking Stretch/Don't Stretch sets the raw marker value.
        void OnComboChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            int idx = HeightCombo.SelectedIndex;
            if (idx < 0) return;
            _syncing = true;
            try { HeightValueBox.Value = idx == 1 ? YesValue() : NoValue(); }
            finally { _syncing = false; }
        }

        // Editing the raw value re-selects the matching combo entry (or clears it).
        void OnHeightValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_syncing) return;
            _syncing = true;
            try { HeightCombo.SelectedIndex = ComboIndexFor((uint)(HeightValueBox.Value ?? 0)); }
            finally { _syncing = false; }
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit Unit Height Adjustment"));
            try
            {
                _vm.HeightValue = (uint)(HeightValueBox.Value ?? 0);
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("UnitIncreaseHeightView.OnWrite failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address)
        {
            // When the list has not yet loaded (WindowManager.Open calls
            // NavigateToId synchronously right after Show(), before Avalonia
            // raises Opened), stash the address so LoadList() can replay the
            // selection — a direct SelectAddress would no-op (#1018/#1019).
            if (!_listLoaded)
            {
                _pendingNavigateAddr = address;
                return;
            }
            EntryList.SelectAddress(address);
        }

        /// <summary>
        /// Pre-select the height row for the given portrait id (FE8 Status Height
        /// jump from the Portrait editor, #1019). Returns false without selecting
        /// or throwing when the id has no height row (out of range / disabled
        /// table); the default first row is left selected in that case.
        /// </summary>
        public bool NavigateToId(uint portraitId)
        {
            uint addr = UnitIncreaseHeightViewModel.IdToAddress(CoreState.ROM, portraitId);
            if (addr == 0) return false;
            NavigateTo(addr);
            return true;
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
