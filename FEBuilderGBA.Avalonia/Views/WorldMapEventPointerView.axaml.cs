// SPDX-License-Identifier: GPL-3.0-or-later
// FE8-only world map event pointer editor view. (#432)
//
// Two AddressListControls (Before / After) drive two parallel detail
// panels; a global-events sub-panel writes three RomInfo pointer slots
// in one undo scope. Every write path is wrapped in
// `_undoService.Begin/Commit` so rollbacks work as expected.
using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapEventPointerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly WorldMapEventPointerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "World Map Event";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("World Map Event Editor", 1240, 900, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public WorldMapEventPointerView()
        {
            InitializeComponent();
            BeforeList.SelectedAddressChanged += OnBeforeSelected;
            AfterList.SelectedAddressChanged += OnAfterSelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                InitialLoad();
            }
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
                Log.ErrorF("WorldMapEventPointerView.InitialLoad failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void ReloadBefore()
        {
            // Reload is a read-only action — wrap in an IsLoading scope so
            // the VM's automatic SetField -> IsDirty propagation doesn't
            // flip the dirty bit when BaseAddr/ReadCount/etc. change.
            // Saves/restores the prior IsLoading value to nest correctly
            // inside InitialLoad's outer scope (Copilot bot inline review).
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadBeforeList();
                // AddressListControl.SetItems already calls SelectFirst()
                // internally — no explicit follow-up needed (and avoids
                // double selection-changed work during reload).
                BeforeList.SetItems(items);
                UpdateReadConfigUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapEventPointerView.ReloadBefore failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
        }

        void ReloadAfter()
        {
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadAfterList();
                AfterList.SetItems(items);
                UpdateReadConfigUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapEventPointerView.ReloadAfter failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
        }

        void ReloadBefore_Click(object? sender, RoutedEventArgs e) => ReloadBefore();
        void ReloadAfter_Click(object? sender, RoutedEventArgs e) => ReloadAfter();

        // #743: routed events from the unified EditorTopBarWithInputs Reload buttons.
        void OnBeforeTopBarReloadRequested(object? sender, RoutedEventArgs e) => ReloadBefore();
        void OnAfterTopBarReloadRequested(object? sender, RoutedEventArgs e) => ReloadAfter();

        void OnBeforeSelected(uint addr)
        {
            // Save/restore the prior IsLoading state instead of forcing
            // false — InitialLoad sets IsLoading=true, then SetItems
            // triggers selection events that land here. Forcing false
            // mid-initialization re-enables dirty tracking too early
            // (Copilot bot inline review point 2).
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                _vm.LoadBeforeEntry(addr);
                UpdateBeforeRowUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapEventPointerView.OnBeforeSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
        }

        void OnAfterSelected(uint addr)
        {
            bool prevLoading = _vm.IsLoading;
            try
            {
                _vm.IsLoading = true;
                _vm.LoadAfterEntry(addr);
                UpdateAfterRowUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapEventPointerView.OnAfterSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = prevLoading; _vm.MarkClean(); }
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
            // #743: unified top-bars surface ReadStart / ReadCount via CLR properties.
            if (BeforeTopBar != null)
            {
                BeforeTopBar.ReadStartAddress = _vm.BeforeBaseAddr;
                BeforeTopBar.ReadCount = (int)_vm.BeforeReadCount;
            }
            if (AfterTopBar != null)
            {
                AfterTopBar.ReadStartAddress = _vm.AfterBaseAddr;
                AfterTopBar.ReadCount = (int)_vm.AfterReadCount;
            }
        }

        // ----------------------------------------------------------------
        // Write handlers — three independent paths matching the WF Form's
        // three Write buttons. Each starts its own undo scope only after
        // the VM's Write* method returns true (= a write actually
        // happened). Returning false means a precondition failed (no row
        // selected / no ROM); we skip Commit + the success toast to avoid
        // empty undo entries and misleading "written" messages
        // (Copilot bot inline review point 3).
        // ----------------------------------------------------------------
        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.BeforeEventPointer = (uint)(BeforeEventPtrBox.Value ?? 0);
            _undoService.Begin("Edit World Map Event Before");
            try
            {
                if (_vm.WriteBefore())
                {
                    _undoService.Commit();
                    _vm.MarkClean();
                    CoreState.Services?.ShowInfo("World map event Before pointer written.");
                }
                else
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("No Before row selected — nothing to write.");
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("WorldMapEventPointerView.Write_Click failed: {0}", ex.Message);
            }
        }

        void WriteAfter_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.AfterEventPointer = (uint)(AfterEventPtrBox.Value ?? 0);
            _undoService.Begin("Edit World Map Event After");
            try
            {
                if (_vm.WriteAfter())
                {
                    _undoService.Commit();
                    _vm.MarkClean();
                    CoreState.Services?.ShowInfo("World map event After pointer written.");
                }
                else
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("No After row selected — nothing to write.");
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("WorldMapEventPointerView.WriteAfter_Click failed: {0}", ex.Message);
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
                if (_vm.WriteGlobalEvents())
                {
                    _undoService.Commit();
                    _vm.MarkClean();
                    CoreState.Services?.ShowInfo("Opening / ending event pointers written.");
                }
                else
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("No ROM loaded — cannot write global events.");
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("WorldMapEventPointerView.WriteGlobalEvents_Click failed: {0}", ex.Message);
            }
        }

        // ----------------------------------------------------------------
        // NewAlloc handlers — mirror WF L_0_NEWALLOC_EVENT / N_L_0_NEWALLOC_EVENT
        // buttons. The full WF flow (InputFormRef.AllocEvent) auto-finds free
        // space and writes a default event header; in the Avalonia port the
        // simpler equivalent is to open the EventScript editor so the user
        // can pick / author an event, then return and paste the address into
        // the corresponding NumericUpDown. Maintains parity with the WF
        // affordance (a click target exists with the same purpose) without
        // re-implementing the 200-line AllocEvent state machine — full
        // automation is tracked separately (out-of-scope for #432).
        // ----------------------------------------------------------------
        void BeforeNewAlloc_Click(object? sender, RoutedEventArgs e)
        {
            // NewAlloc just OPENS the editor (no immediate navigate/disassemble), so it must
            // NOT stage an event kind — a staged-but-not-consumed kind would leak into a
            // later manual disassemble of a normal script (#1510). The user picks an address
            // and disassembles manually, which uses the chapter-event default.
            WindowManager.Instance.Open<EventScriptView>();
        }

        void AfterNewAlloc_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<EventScriptView>();
        }

        // ----------------------------------------------------------------
        // Jump handlers — mirror the 5 WF JumpForm<T> callsites. Each
        // opens the corresponding editor; the EventScript jumps pass the
        // selected global pointer so the user lands on the right script.
        // ----------------------------------------------------------------
        void JumpToOpening_Click(object? sender, RoutedEventArgs e)
        {
            uint addr = (uint)(OpeningEventBox.Value ?? 0);
            // World-map event — flag the editor BEFORE NavigateTo runs the disassemble so
            // the termination scan + Write-All terminator use the world-map rules (#1510
            // review finding #2). Open + NavigateTo manually rather than one-shot Navigate<T>.
            var view = WindowManager.Instance.Open<EventScriptView>();
            view.SetEventKind(isWorldMapEvent: true, isTopLevelEvent: false);
            view.NavigateTo(addr);
        }

        void JumpToEnding1_Click(object? sender, RoutedEventArgs e)
        {
            uint addr = (uint)(Ending1EventBox.Value ?? 0);
            var view = WindowManager.Instance.Open<EventScriptView>();
            view.SetEventKind(isWorldMapEvent: true, isTopLevelEvent: false);
            view.NavigateTo(addr);
        }

        void JumpToEnding2_Click(object? sender, RoutedEventArgs e)
        {
            uint addr = (uint)(Ending2EventBox.Value ?? 0);
            var view = WindowManager.Instance.Open<EventScriptView>();
            view.SetEventKind(isWorldMapEvent: true, isTopLevelEvent: false);
            view.NavigateTo(addr);
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
