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

        // True once Opened -> LoadList() has populated EntryList. A NavigateTo
        // request that arrives BEFORE the list loads (WindowManager.Open then an
        // immediate NavigateToId, but Avalonia raises Opened asynchronously after
        // layout) is stashed in _pendingNavigateAddr and replayed once LoadList
        // completes — otherwise EntryList.SelectAddress no-ops against the
        // still-empty list. Mirrors MonsterProbabilityViewerView (#1018/#1019).
        bool _listLoaded;
        uint? _pendingNavigateAddr;

        public string ViewTitle => "Unit Height Adjustment";
        public bool IsLoaded => _vm.IsLoaded;

        public UnitIncreaseHeightView()
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.PortraitLoader(items, i));
                _listLoaded = true;
            }
            catch (Exception ex)
            {
                Log.Error("UnitIncreaseHeightView.LoadList failed: {0}", ex.Message);
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
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("UnitIncreaseHeightView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
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
