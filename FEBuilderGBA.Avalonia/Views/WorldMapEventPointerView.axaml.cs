// SPDX-License-Identifier: GPL-3.0-or-later
// FE8-only world map event pointer editor view. (#432)
//
// Two AddressListControls (Before / After) drive two parallel detail
// panels; a global-events sub-panel writes three RomInfo pointer slots
// in one undo scope. Every write path is wrapped in
// `_undoService.Begin/Commit` so rollbacks work as expected.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapEventPointerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly WorldMapEventPointerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "World Map Event";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public WorldMapEventPointerView()
        {
            InitializeComponent();
            BeforeList.SelectedAddressChanged += OnBeforeSelected;
            AfterList.SelectedAddressChanged += OnAfterSelected;
            Opened += (_, _) => InitialLoad();
        }

        void InitialLoad()
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadGlobalEvents();
                ReloadBefore();
                ReloadAfter();
                UpdateGlobalEventsUI();
                UpdateReadConfigUI();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapEventPointerView.InitialLoad failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void ReloadBefore()
        {
            try
            {
                var items = _vm.LoadBeforeList();
                BeforeList.SetItems(items);
                UpdateReadConfigUI();
                if (items.Count > 0)
                {
                    BeforeList.SelectFirst();
                }
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapEventPointerView.ReloadBefore failed: {0}", ex.Message);
            }
        }

        void ReloadAfter()
        {
            try
            {
                var items = _vm.LoadAfterList();
                AfterList.SetItems(items);
                UpdateReadConfigUI();
                if (items.Count > 0)
                {
                    AfterList.SelectFirst();
                }
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapEventPointerView.ReloadAfter failed: {0}", ex.Message);
            }
        }

        void ReloadBefore_Click(object? sender, RoutedEventArgs e) => ReloadBefore();
        void ReloadAfter_Click(object? sender, RoutedEventArgs e) => ReloadAfter();

        void OnBeforeSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadBeforeEntry(addr);
                UpdateBeforeRowUI();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapEventPointerView.OnBeforeSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnAfterSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadAfterEntry(addr);
                UpdateAfterRowUI();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapEventPointerView.OnAfterSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateBeforeRowUI()
        {
            BeforeRowAddrBox.Value = _vm.CurrentBeforeAddr;
            BeforeSelectAddrBox.Text = $"0x{_vm.CurrentBeforeAddr:X08}";
            BeforeEventPtrBox.Value = _vm.BeforeEventPointer;
        }

        void UpdateAfterRowUI()
        {
            AfterRowAddrBox.Value = _vm.CurrentAfterAddr;
            AfterSelectAddrBox.Text = $"0x{_vm.CurrentAfterAddr:X08}";
            AfterEventPtrBox.Value = _vm.AfterEventPointer;
        }

        void UpdateGlobalEventsUI()
        {
            OpeningEventBox.Value = _vm.OpeningEvent;
            Ending1EventBox.Value = _vm.Ending1Event;
            Ending2EventBox.Value = _vm.Ending2Event;
        }

        void UpdateReadConfigUI()
        {
            BeforeBaseAddrBox.Value = _vm.BeforeBaseAddr;
            BeforeReadCountBox.Value = _vm.BeforeReadCount;
            AfterBaseAddrBox.Value = _vm.AfterBaseAddr;
            AfterReadCountBox.Value = _vm.AfterReadCount;
        }

        // ----------------------------------------------------------------
        // Write handlers — three independent paths matching the WF Form's
        // three Write buttons. Each starts its own undo scope.
        // ----------------------------------------------------------------
        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.BeforeEventPointer = (uint)(BeforeEventPtrBox.Value ?? 0);
            _undoService.Begin("Edit World Map Event Before");
            try
            {
                _vm.WriteBefore();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("World map event Before pointer written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapEventPointerView.Write_Click failed: {0}", ex.Message);
            }
        }

        void WriteAfter_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.AfterEventPointer = (uint)(AfterEventPtrBox.Value ?? 0);
            _undoService.Begin("Edit World Map Event After");
            try
            {
                _vm.WriteAfter();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("World map event After pointer written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapEventPointerView.WriteAfter_Click failed: {0}", ex.Message);
            }
        }

        void WriteGlobalEvents_Click(object? sender, RoutedEventArgs e)
        {
            _vm.OpeningEvent = (uint)(OpeningEventBox.Value ?? 0);
            _vm.Ending1Event = (uint)(Ending1EventBox.Value ?? 0);
            _vm.Ending2Event = (uint)(Ending2EventBox.Value ?? 0);
            _undoService.Begin("Edit World Map Global Events");
            try
            {
                _vm.WriteGlobalEvents();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Opening / ending event pointers written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapEventPointerView.WriteGlobalEvents_Click failed: {0}", ex.Message);
            }
        }

        // ----------------------------------------------------------------
        // Jump handlers — mirror the 5 WF JumpForm<T> callsites. Each
        // opens the corresponding editor; the EventScript jumps pass the
        // selected global pointer so the user lands on the right script.
        // ----------------------------------------------------------------
        void JumpToOpening_Click(object? sender, RoutedEventArgs e)
        {
            uint addr = (uint)(OpeningEventBox.Value ?? 0);
            WindowManager.Instance.Navigate<EventScriptView>(addr);
        }

        void JumpToEnding1_Click(object? sender, RoutedEventArgs e)
        {
            uint addr = (uint)(Ending1EventBox.Value ?? 0);
            WindowManager.Instance.Navigate<EventScriptView>(addr);
        }

        void JumpToEnding2_Click(object? sender, RoutedEventArgs e)
        {
            uint addr = (uint)(Ending2EventBox.Value ?? 0);
            WindowManager.Instance.Navigate<EventScriptView>(addr);
        }

        void JumpToWorldMapPath_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<WorldMapPathView>();
        }

        void JumpToWorldMapPoint_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<WorldMapPointView>();
        }

        public void NavigateTo(uint address)
        {
            // Try the Before list first, fall back to the After list. Matches
            // WF JumpTo() which probes both lists in sequence.
            BeforeList.SelectAddress(address);
            AfterList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            // Prefer Before since it's the "primary" list in WF (panel6
            // is the visually-dominant top-left list).
            if (BeforeList.ItemCount > 0)
                BeforeList.SelectFirst();
            else
                AfterList.SelectFirst();
        }
    }
}
