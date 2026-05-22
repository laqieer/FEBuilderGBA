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
        /// Floor view's "Jump to BG" button fires, both the filter index AND
        /// the selected row must be preserved — non-zero filters MUST NOT
        /// resolve to the wrong BG lookup table.
        ///
        /// The BG view doesn't yet have a UI FilterComboBox (#441 will add
        /// the full combo + density raise), but the VM exposes a
        /// `LoadList(int filterIndex)` overload that reads from the requested
        /// floor pointer via <see cref="MapTerrainLookupCore.GetPointers"/>.
        /// We use that overload here so the BG list is rebuilt against the
        /// SAME filter slot the Floor side sent.
        /// </summary>
        public void NavigateToFilterAndRow(uint filterIndex, uint rowIndex)
        {
            try
            {
                var items = _vm.LoadList((int)filterIndex);
                EntryList.SetItems(items);
                EntryList.SelectByIndex((int)rowIndex);
            }
            catch (Exception ex)
            {
                Log.Error($"MapTerrainBGLookupTableView.NavigateToFilterAndRow failed: {ex.Message}");
            }
        }
    }
}
