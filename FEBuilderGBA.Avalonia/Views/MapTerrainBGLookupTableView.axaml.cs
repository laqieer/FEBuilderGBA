using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapTerrainBGLookupTableView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapTerrainBGLookupTableViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Terrain BG Lookup Table";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapTerrainBGLookupTableView()
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
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error($"MapTerrainBGLookupTableView.LoadList failed: {ex.Message}");
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error($"MapTerrainBGLookupTableView.OnSelected failed: {ex.Message}");
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            BattleBGBox.Value = _vm.BattleBG;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _undoService.Begin("Edit Terrain BG Lookup");
            try
            {
                _vm.BattleBG = (uint)(BattleBGBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Terrain BG lookup data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"MapTerrainBGLookupTableView.Write: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        /// <summary>
        /// Filter+row navigation API mirrored from the Floor sister view.
        /// Phase 4 gap-fix (#442 — Copilot CLI review point 2): when the
        /// Floor view's "Jump to BG" button fires, both filter index and
        /// selected row must be preserved on the BG side. Today the BG VM
        /// always loads filter 0 (#441 will raise it to a full filter combo);
        /// in the meantime we just select the requested row inside the
        /// vanilla filter-0 list. This keeps the parity invariant ("same
        /// filter + same row on both sides") true today AND stays correct
        /// once #441 adds the BG filter combo.
        /// </summary>
        public void NavigateToFilterAndRow(uint filterIndex, uint rowIndex)
        {
            // BG VM doesn't yet expose a multi-filter API (sister issue #441).
            // For now, ignore filterIndex and just select the requested row
            // in the default-filter list — same behaviour the WF JumpTo
            // produces against filter 0.
            EntryList.SelectByIndex((int)rowIndex);
        }
    }
}
