using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventUnitFE7View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventUnitFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();

        readonly ObservableCollection<string> _mapDisplayItems = new();
        readonly ObservableCollection<string> _groupDisplayItems = new();
        readonly ObservableCollection<string> _unitDisplayItems = new();

        List<AddrResult> _mapItems = new();
        List<AddrResult> _groupItems = new();
        List<AddrResult> _unitItems = new();
        readonly List<AddrResult> _newAllocData = new();

        public string ViewTitle => "Event Unit (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        bool _suppressUiSync;

        public EventUnitFE7View()
        {
            InitializeComponent();
            MapListBox.ItemsSource = _mapDisplayItems;
            GroupListBox.ItemsSource = _groupDisplayItems;
            UnitListBox.ItemsSource = _unitDisplayItems;

            MapListBox.SelectionChanged += MapListBox_SelectionChanged;
            GroupListBox.SelectionChanged += GroupListBox_SelectionChanged;
            UnitListBox.SelectionChanged += UnitListBox_SelectionChanged;

            // Populate combos with R._-translated entries so ja/zh users
            // see localised labels (Copilot review #522 third pass —
            // ViewTranslationHelper does not scan ComboBoxItem content).
            AllegianceCombo.ItemsSource = new[]
            {
                R._("Player"),
                R._("Ally"),
                R._("Enemy"),
                R._("Disappear"),
            };
            GrowthRateCombo.ItemsSource = new[]
            {
                R._("No Growth"),
                R._("Class Dependent"),
            };
            PosSyncCombo.ItemsSource = new[]
            {
                R._("Sync After with Before"),
                R._("Before and After independent"),
            };

            // Wire B3 sub-field combos to live-update UnitInfoBox via the VM.
            LVBox.ValueChanged += LVBox_ValueChanged;
            AllegianceCombo.SelectionChanged += AllegianceCombo_SelectionChanged;
            GrowthRateCombo.SelectionChanged += GrowthRateCombo_SelectionChanged;

            // Wire the raw UnitInfoBox so that direct edits to the byte
            // refresh the LV/Allegiance/Growth Rate sub-controls (parity
            // with WF UNITGROW two-way binding).
            UnitInfoBox.ValueChanged += UnitInfoBox_ValueChanged;

            // Wire StartX/StartY → EndX/EndY sync when PosSyncCombo selects
            // index 0 ("Sync After with Before") — mirrors WF
            // EventUnitFE7Form.B4_ValueChanged / B5_ValueChanged
            // (PosSyncUpdateComboBox.SelectedIndex == 0 → also update B6/B7).
            // Default to index 1 ("Before and After independent") so editing
            // doesn't surprise the user. (Copilot review #522 round 4.)
            PosSyncCombo.SelectedIndex = 1;
            StartXBox.ValueChanged += StartXBox_ValueChanged;
            StartYBox.ValueChanged += StartYBox_ValueChanged;

            // Live-refresh the ItemDrop status when UnitID/ClassID change
            // (Copilot review #522 round 4 — status was only refreshed on
            // LoadEntry, not while the user was editing).
            UnitIDBox.ValueChanged += UnitOrClassIdChanged;
            ClassIDBox.ValueChanged += UnitOrClassIdChanged;

            Opened += (_, _) => LoadMapList();
        }

        void StartXBox_ValueChanged(object? sender, global::Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressUiSync) return;
            if (PosSyncCombo.SelectedIndex != 0) return;
            // Sync mode 0 — propagate Before-X to After-X.
            _suppressUiSync = true;
            try { EndXBox.Value = StartXBox.Value; }
            finally { _suppressUiSync = false; }
        }

        void StartYBox_ValueChanged(object? sender, global::Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressUiSync) return;
            if (PosSyncCombo.SelectedIndex != 0) return;
            _suppressUiSync = true;
            try { EndYBox.Value = StartYBox.Value; }
            finally { _suppressUiSync = false; }
        }

        void UnitOrClassIdChanged(object? sender, global::Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressUiSync) return;
            // Push the new unit/class id into the VM so the VM setter
            // refreshes ItemDropDisplay; then mirror it onto the label.
            _vm.UnitID = (uint)(UnitIDBox.Value ?? 0);
            _vm.ClassID = (uint)(ClassIDBox.Value ?? 0);
            ItemDropLabel.Text = _vm.ItemDropDisplay;
        }

        void LoadMapList()
        {
            try
            {
                _mapItems = _vm.LoadMapList();
                _mapDisplayItems.Clear();
                foreach (var item in _mapItems)
                    _mapDisplayItems.Add(item.name);

                ReadCountBox.Value = _mapItems.Count;

                if (_mapItems.Count > 0)
                    MapListBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.LoadMapList failed: {0}", ex.Message);
            }
        }

        void MapListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                int idx = MapListBox.SelectedIndex;
                if (idx < 0 || idx >= _mapItems.Count) return;

                uint mapId = _mapItems[idx].tag;
                _groupItems = _vm.LoadUnitGroups(mapId);

                // Re-merge any session NEW allocations for this map that the
                // group scan has not yet discovered (WF AppendNoWriteNewData parity).
                MergeNewAllocData(mapId);

                _groupDisplayItems.Clear();
                foreach (var item in _groupItems)
                    _groupDisplayItems.Add(item.name);

                _unitDisplayItems.Clear();
                _unitItems = new List<AddrResult>();
                ClearDetail();

                if (_groupItems.Count > 0)
                    GroupListBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.MapListBox_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void GroupListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                int idx = GroupListBox.SelectedIndex;
                if (idx < 0 || idx >= _groupItems.Count) return;

                uint groupAddr = _groupItems[idx].addr;
                TopAddrBox.Text = string.Format("0x{0:X08}", groupAddr);
                LoadUnitsFromAddress(groupAddr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.GroupListBox_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void LoadUnitsFromAddress(uint baseAddr)
        {
            _unitItems = _vm.LoadUnitList(baseAddr);
            _unitDisplayItems.Clear();
            foreach (var item in _unitItems)
                _unitDisplayItems.Add(item.name);

            ClearDetail();

            if (_unitItems.Count > 0)
                UnitListBox.SelectedIndex = 0;
        }

        void UnitListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                int idx = UnitListBox.SelectedIndex;
                if (idx < 0 || idx >= _unitItems.Count) return;

                _vm.LoadEntry(_unitItems[idx].addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.UnitListBox_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void LoadAddr_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string text = ManualAddrBox.Text ?? "";
                uint addr = U.atoh(text);
                if (addr == 0 || !U.isSafetyOffset(addr))
                {
                    Log.ErrorF("EventUnitFE7View: Invalid address {0}", text);
                    return;
                }
                TopAddrBox.Text = string.Format("0x{0:X08}", addr);
                LoadUnitsFromAddress(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.LoadAddr_Click failed: {0}", ex.Message);
            }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadMapList();
        }

        void ClearDetail()
        {
            AddrLabel.Text = "";
            SelectedAddrBox.Text = "";
            UnitNameLabel.Text = "";
            ClassNameLabel.Text = "";
            Item1NameLabel.Text = "";
            Item2NameLabel.Text = "";
            Item3NameLabel.Text = "";
            Item4NameLabel.Text = "";
            AI1DescLabel.Text = "";
            AI2DescLabel.Text = "";
            AI3DescLabel.Text = "";
            AI4DescLabel.Text = "";
            ItemDropLabel.Text = "";
            CommentBox.Text = "";
        }

        void UpdateUI()
        {
            _suppressUiSync = true;
            try
            {
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                SelectedAddrBox.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                UnitIDBox.Value = _vm.UnitID;
                ClassIDBox.Value = _vm.ClassID;
                LeaderUnitIDBox.Value = _vm.LeaderUnitID;
                UnitInfoBox.Value = _vm.UnitInfo;
                LVBox.Value = _vm.UnitInfoLV;
                AllegianceCombo.SelectedIndex = (int)_vm.UnitInfoAllegiance;
                GrowthRateCombo.SelectedIndex = (int)_vm.UnitInfoGrow;
                StartXBox.Value = _vm.StartX;
                StartYBox.Value = _vm.StartY;
                EndXBox.Value = _vm.EndX;
                EndYBox.Value = _vm.EndY;
                Item1Box.Value = _vm.Item1;
                Item2Box.Value = _vm.Item2;
                Item3Box.Value = _vm.Item3;
                Item4Box.Value = _vm.Item4;
                AI1PrimaryBox.Value = _vm.AI1Primary;
                AI2SecondaryBox.Value = _vm.AI2Secondary;
                AI3TargetRecoveryBox.Value = _vm.AI3TargetRecovery;
                AI4RetreatBox.Value = _vm.AI4Retreat;
                CommentBox.Text = _vm.Comment ?? "";

                UnitNameLabel.Text = _vm.UnitName;
                ClassNameLabel.Text = _vm.ClassName;
                Item1NameLabel.Text = _vm.Item1Name;
                Item2NameLabel.Text = _vm.Item2Name;
                Item3NameLabel.Text = _vm.Item3Name;
                Item4NameLabel.Text = _vm.Item4Name;
                AI1DescLabel.Text = _vm.AI1Desc;
                AI2DescLabel.Text = _vm.AI2Desc;
                AI3DescLabel.Text = _vm.AI3Desc;
                AI4DescLabel.Text = _vm.AI4Desc;
                ItemDropLabel.Text = _vm.ItemDropDisplay;
            }
            finally { _suppressUiSync = false; }
        }

        void ReadFromUI()
        {
            _vm.UnitID = (uint)(UnitIDBox.Value ?? 0);
            _vm.ClassID = (uint)(ClassIDBox.Value ?? 0);
            _vm.LeaderUnitID = (uint)(LeaderUnitIDBox.Value ?? 0);
            _vm.UnitInfo = (uint)(UnitInfoBox.Value ?? 0);
            _vm.StartX = (uint)(StartXBox.Value ?? 0);
            _vm.StartY = (uint)(StartYBox.Value ?? 0);
            _vm.EndX = (uint)(EndXBox.Value ?? 0);
            _vm.EndY = (uint)(EndYBox.Value ?? 0);
            _vm.Item1 = (uint)(Item1Box.Value ?? 0);
            _vm.Item2 = (uint)(Item2Box.Value ?? 0);
            _vm.Item3 = (uint)(Item3Box.Value ?? 0);
            _vm.Item4 = (uint)(Item4Box.Value ?? 0);
            _vm.AI1Primary = (uint)(AI1PrimaryBox.Value ?? 0);
            _vm.AI2Secondary = (uint)(AI2SecondaryBox.Value ?? 0);
            _vm.AI3TargetRecovery = (uint)(AI3TargetRecoveryBox.Value ?? 0);
            _vm.AI4Retreat = (uint)(AI4RetreatBox.Value ?? 0);
            _vm.Comment = CommentBox.Text ?? "";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            _undoService.Begin("Edit Event Unit FE7");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EventUnitFE7View.Write failed: {0}", ex.Message);
            }
        }

        // ---------------------------------------------------------------
        // B3 sub-field two-way sync (LV / Allegiance / Growth Rate <-> UnitInfo)
        // ---------------------------------------------------------------

        void LVBox_ValueChanged(object? sender, global::Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressUiSync) return;
            _suppressUiSync = true;
            try
            {
                _vm.UnitInfoLV = (uint)(LVBox.Value ?? 0);
                UnitInfoBox.Value = _vm.UnitInfo;
            }
            finally { _suppressUiSync = false; }
        }

        void AllegianceCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressUiSync) return;
            int idx = AllegianceCombo.SelectedIndex;
            if (idx < 0) return;
            _suppressUiSync = true;
            try
            {
                _vm.UnitInfoAllegiance = (uint)idx;
                UnitInfoBox.Value = _vm.UnitInfo;
            }
            finally { _suppressUiSync = false; }
        }

        void GrowthRateCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressUiSync) return;
            int idx = GrowthRateCombo.SelectedIndex;
            if (idx < 0) return;
            _suppressUiSync = true;
            try
            {
                _vm.UnitInfoGrow = (uint)idx;
                UnitInfoBox.Value = _vm.UnitInfo;
            }
            finally { _suppressUiSync = false; }
        }

        void UnitInfoBox_ValueChanged(object? sender, global::Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressUiSync) return;
            _suppressUiSync = true;
            try
            {
                _vm.UnitInfo = (uint)(UnitInfoBox.Value ?? 0);
                LVBox.Value = _vm.UnitInfoLV;
                AllegianceCombo.SelectedIndex = (int)_vm.UnitInfoAllegiance;
                GrowthRateCombo.SelectedIndex = (int)_vm.UnitInfoGrow;
            }
            finally { _suppressUiSync = false; }
        }

        // ---------------------------------------------------------------
        // Cross-editor jumps (B0=unit id, B1 selected map id passed to Haiku).
        // ---------------------------------------------------------------

        void JumpBattleTalk_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint unitId = (uint)(UnitIDBox.Value ?? 0);
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint hitAddr = MapEventUnitCore.FindBattleTalkFE7UnitIdAddress(rom, unitId);
                if (hitAddr == 0)
                {
                    // No match in either FE7 BattleTalk table — log and
                    // no-op so we don't open the editor at address 0
                    // (Copilot review #522 third pass).
                    Log.Notify("EventUnitFE7View.JumpBattleTalk_Click: no BattleTalk entry found for unit " + unitId.ToString());
                    return;
                }
                WindowManager.Instance.Navigate<EventBattleTalkFE7View>(hitAddr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.JumpBattleTalk_Click failed: {0}", ex.Message);
            }
        }

        void JumpBattleBGM_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint unitId = (uint)(UnitIDBox.Value ?? 0);
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint hitAddr = MapEventUnitCore.FindBossBGMFE7UnitIdAddress(rom, unitId);
                if (hitAddr == 0)
                {
                    Log.Notify("EventUnitFE7View.JumpBattleBGM_Click: no BossBGM entry found for unit " + unitId.ToString());
                    return;
                }
                WindowManager.Instance.Navigate<SoundBossBGMViewerView>(hitAddr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.JumpBattleBGM_Click failed: {0}", ex.Message);
            }
        }

        void JumpHaiku_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint unitId = (uint)(UnitIDBox.Value ?? 0);
                uint mapId = _vm.SelectedMapId;
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint hitAddr = MapEventUnitCore.FindHaikuFE7Address(rom, unitId, mapId);
                if (hitAddr == 0)
                {
                    Log.Notify("EventUnitFE7View.JumpHaiku_Click: no Haiku entry found for unit " + unitId.ToString());
                    return;
                }
                WindowManager.Instance.Navigate<EventHaikuFE7View>(hitAddr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.JumpHaiku_Click failed: {0}", ex.Message);
            }
        }

        // ---------------------------------------------------------------
        // List allocation / expansion handlers (mirror WF NewButton +
        // AddressListExpandsButton). Each opens a dedicated undo scope so
        // partial failures roll back cleanly.
        // ---------------------------------------------------------------

        async void NewAlloc_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int mapIdx = MapListBox.SelectedIndex;
                if (mapIdx < 0 || mapIdx >= _mapItems.Count) return;
                uint mapId = _mapItems[mapIdx].tag;

                // Modal count-picker (WF EventUnitNewAllocForm parity).
                var dlg = new EventUnitNewAllocView();
                uint? count = await dlg.ShowDialog<uint?>(this);
                if (count == null || count.Value == 0) return; // Cancel / count=0 no-op

                _undoService.Begin("EventUnit NEW FE7");
                try
                {
                    uint newBase = _vm.NewAllocUnitList(count.Value, _undoService.GetActiveUndoData());
                    if (newBase == U.NOT_FOUND)
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(R._("Failed to allocate new event-unit block."));
                        return;
                    }
                    _undoService.Commit();

                    // Session-scoped NEW tracking (mirrors WF NewAllocData):
                    // the block is unlinked — WF writes no cond pointer; the
                    // user references it from the event script, after which
                    // the Core GetUnitGroupsForMap POINTER_UNIT scan finds it.
                    var newEntry = new AddrResult(newBase, "NEW", mapId);
                    _newAllocData.Add(newEntry);
                    _groupItems.Add(newEntry);
                    _groupDisplayItems.Add("NEW");
                    // Select it so the editable starter rows load.
                    GroupListBox.SelectedIndex = _groupDisplayItems.Count - 1;

                    CoreState.Services?.ShowInfo(
                        string.Format(R._("Allocated new event-unit block ({0} units) at 0x{1:X08}."),
                            count.Value, newBase));
                }
                catch
                {
                    _undoService.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.NewAlloc_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Re-append any session NEW allocations for <paramref name="mapId"/>
        /// that the group scan has not already discovered, and drop those that
        /// it has (they are now "booked" via the script POINTER_UNIT scan).
        /// Avalonia port of WF <c>EventUnitFE7Form.AppendNoWriteNewData</c>.
        /// This method only mutates <c>_groupItems</c>; the caller
        /// (<c>MapListBox_SelectionChanged</c>) rebuilds <c>_groupDisplayItems</c>
        /// from <c>_groupItems</c> afterward so the display list stays aligned.
        /// </summary>
        void MergeNewAllocData(uint mapId)
        {
            for (int i = _newAllocData.Count - 1; i >= 0; i--)
            {
                AddrResult entry = _newAllocData[i];
                if (entry.tag != mapId) continue;

                bool alreadyListed = false;
                foreach (var g in _groupItems)
                {
                    if (g.addr == entry.addr) { alreadyListed = true; break; }
                }
                if (alreadyListed)
                {
                    // Booked by the script scan — stop tracking it as NEW.
                    _newAllocData.RemoveAt(i);
                }
                else
                {
                    _groupItems.Add(entry);
                }
            }
        }

        void ExpandList_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_unitItems.Count == 0) return;
                if (_vm.SelectedUnitListBase == 0)
                {
                    Log.Notify("EventUnitFE7View.ExpandList_Click: no unit list selected yet.");
                    return;
                }

                _undoService.Begin("Expand Event Unit List FE7");
                try
                {
                    uint newBase = _vm.ExpandUnitListCurrent(addRows: 1);
                    if (newBase == U.NOT_FOUND)
                    {
                        _undoService.Rollback();
                        Log.Notify("EventUnitFE7View.ExpandList_Click: expansion failed (slot not found or no free space).");
                        return;
                    }
                    _undoService.Commit();
                    Log.Notify("EventUnitFE7View.ExpandList_Click: expanded to new base " + string.Format("0x{0:X08}", newBase));
                    // Update the cached group entry so re-selecting this
                    // group doesn't load the stale orphaned table (Copilot
                    // review #522 round 4). _groupItems entries are
                    // AddrResult records — replace the selected one's addr
                    // with the new base.
                    int gi = GroupListBox.SelectedIndex;
                    if (gi >= 0 && gi < _groupItems.Count)
                    {
                        var old = _groupItems[gi];
                        _groupItems[gi] = new AddrResult(newBase, old.name, old.tag);
                    }
                    // Refresh the unit list from the new base so the user sees
                    // the new starter row immediately.
                    LoadUnitsFromAddress(newBase);
                }
                catch
                {
                    _undoService.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE7View.ExpandList_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            for (int i = 0; i < _unitItems.Count; i++)
            {
                if (_unitItems[i].addr == address)
                {
                    UnitListBox.SelectedIndex = i;
                    return;
                }
            }
        }

        public void SelectFirstItem()
        {
            if (_mapItems.Count > 0)
                MapListBox.SelectedIndex = 0;
        }
        public ViewModelBase? DataViewModel => _vm;
    }
}
