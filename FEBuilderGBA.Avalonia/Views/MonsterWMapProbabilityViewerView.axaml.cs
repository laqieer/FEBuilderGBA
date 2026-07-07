using System;
using global::Avalonia;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MonsterWMapProbabilityViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MonsterWMapProbabilityViewerViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        // Guard against ValueChanged events firing while we programmatically
        // load fields (prevents spurious dirty/SUM churn during selection).
        bool _loading;

        public string ViewTitle => "World Map Monster";
        public new bool IsLoaded => _vm.CanWrite;


        public EditorDescriptor Descriptor => new("World Map Monster Editor", 1100, 760, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public MonsterWMapProbabilityViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            StageList.SelectedAddressChanged += OnStageSelected;
            ProbList.SelectedAddressChanged += OnProbSelected;
            StageFilter.SelectionChanged += OnStageFilterChanged;
            ProbFilter.SelectionChanged += OnProbFilterChanged;        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                LoadAll();

            }

        }

        void LoadAll()
        {
            LoadList();

            // FE8-only surfaces: hide entirely on non-FE8 ROMs.
            Fe8Panel.IsVisible = _vm.IsSupported;
            if (!_vm.IsSupported) return;

            LoadStageList();
            LoadProbList();
            LoadSkirmishEvents();
        }

        // ----------------------------------------------------------------
        // Surface 1 — base point list
        // ----------------------------------------------------------------
        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadMonsterWMapProbabilityList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error($"MonsterWMapProbabilityViewerView.LoadList failed: {ex}");
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMonsterWMapProbability(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error($"MonsterWMapProbabilityViewerView.OnSelected failed: {ex}");
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            BasePointIdBox.Value = _vm.BasePointId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit World Map Monster Base Point");
            try
            {
                _vm.BasePointId = (uint)(BasePointIdBox.Value ?? 0);
                _vm.WriteMonsterWMapProbability();
                _undoService.Commit();
                _vm.MarkClean();
                // Base-point names feed the probability column labels — refresh them
                // (mirrors WinForms N2_BaseNameUpdate wired to WriteButton.Click).
                RefreshProbLabels();
                CoreState.Services?.ShowInfo("World map monster data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"MonsterWMapProbabilityViewerView.Write: {ex}"); }
        }

        // ----------------------------------------------------------------
        // Surface 2 — stage spread
        // ----------------------------------------------------------------
        void LoadStageList()
        {
            try
            {
                var items = _vm.LoadStageList();
                StageList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error($"MonsterWMapProbabilityViewerView.LoadStageList failed: {ex}");
            }
        }

        void OnStageSelected(uint addr)
        {
            _loading = true;
            try
            {
                _vm.LoadStage(addr);
                StageAddrLabel.Text = $"0x{_vm.StageAddr:X08}";
                StageMapIdBox.Value = _vm.StageMapId;
                StageMapNameLabel.Text = _vm.StageMapName;
            }
            catch (Exception ex)
            {
                Log.Error($"MonsterWMapProbabilityViewerView.OnStageSelected failed: {ex}");
            }
            finally { _loading = false; }

            // Mirror the selected row into the probability list (WinForms N1<->N2 row sync).
            MirrorSelection(StageList, ProbList);
        }

        void StageMapId_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_loading) return;
            uint id = (uint)(StageMapIdBox.Value ?? 0);
            StageMapNameLabel.Text = MapSettingCore.GetMapNameById(CoreState.ROM, id);
        }

        void StageWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.StageAddr == 0) return;
            _undoService.Begin("Edit World Map Monster Stage");
            try
            {
                _vm.StageMapId = (uint)(StageMapIdBox.Value ?? 0);
                _vm.WriteStage();
                _undoService.Commit();
                // Refresh the stage list so the map-name label updates in place.
                uint sel = _vm.StageAddr;
                LoadStageList();
                StageList.SelectAddress(sel);
                CoreState.Services?.ShowInfo("World map monster stage written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"MonsterWMapProbabilityViewerView.StageWrite: {ex}"); }
        }

        void OnStageFilterChanged(object? sender, SelectionChangedEventArgs e)
        {
            _vm.StageIsEphraim = StageFilter.SelectedIndex == 1;
            LoadStageList();
            // Keep the probability filter in sync (WinForms N1_Filter<->N2_Filter).
            if (ProbFilter.SelectedIndex != StageFilter.SelectedIndex)
                ProbFilter.SelectedIndex = StageFilter.SelectedIndex;
        }

        // ----------------------------------------------------------------
        // Surface 3 — per-base probabilities
        // ----------------------------------------------------------------
        void LoadProbList()
        {
            try
            {
                var items = _vm.LoadProbabilityList();
                ProbList.SetItems(items);
                RefreshProbLabels();
            }
            catch (Exception ex)
            {
                Log.Error($"MonsterWMapProbabilityViewerView.LoadProbList failed: {ex}");
            }
        }

        void RefreshProbLabels()
        {
            try
            {
                List<string> labels = _vm.GetBasePointLabels();
                TextBlock[] boxes = { ProbLabel0, ProbLabel1, ProbLabel2, ProbLabel3, ProbLabel4, ProbLabel5, ProbLabel6, ProbLabel7, ProbLabel8 };
                for (int i = 0; i < boxes.Length; i++)
                {
                    string text = (i < labels.Count && !string.IsNullOrEmpty(labels[i])) ? labels[i] : $"Base {i}";
                    boxes[i].Text = text + ":";
                }
            }
            catch (Exception ex)
            {
                Log.Error($"MonsterWMapProbabilityViewerView.RefreshProbLabels failed: {ex}");
            }
        }

        void OnProbSelected(uint addr)
        {
            _loading = true;
            try
            {
                _vm.LoadProbability(addr);
                ProbAddrLabel.Text = $"0x{_vm.ProbAddr:X08}";
                Prob0Box.Value = _vm.Prob0; Prob1Box.Value = _vm.Prob1; Prob2Box.Value = _vm.Prob2;
                Prob3Box.Value = _vm.Prob3; Prob4Box.Value = _vm.Prob4; Prob5Box.Value = _vm.Prob5;
                Prob6Box.Value = _vm.Prob6; Prob7Box.Value = _vm.Prob7; Prob8Box.Value = _vm.Prob8;
                ProbSumLabel.Text = _vm.ProbSum;
            }
            catch (Exception ex)
            {
                Log.Error($"MonsterWMapProbabilityViewerView.OnProbSelected failed: {ex}");
            }
            finally { _loading = false; }

            // Mirror the selected row into the stage list (WinForms N2<->N1 row sync).
            MirrorSelection(ProbList, StageList);
        }

        void Prob_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_loading) return;
            uint sum = 0;
            sum += (uint)(Prob0Box.Value ?? 0); sum += (uint)(Prob1Box.Value ?? 0); sum += (uint)(Prob2Box.Value ?? 0);
            sum += (uint)(Prob3Box.Value ?? 0); sum += (uint)(Prob4Box.Value ?? 0); sum += (uint)(Prob5Box.Value ?? 0);
            sum += (uint)(Prob6Box.Value ?? 0); sum += (uint)(Prob7Box.Value ?? 0); sum += (uint)(Prob8Box.Value ?? 0);
            ProbSumLabel.Text = sum + "%";
        }

        void ProbWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.ProbAddr == 0) return;
            _undoService.Begin("Edit World Map Monster Probability");
            try
            {
                _vm.Prob0 = (uint)(Prob0Box.Value ?? 0); _vm.Prob1 = (uint)(Prob1Box.Value ?? 0); _vm.Prob2 = (uint)(Prob2Box.Value ?? 0);
                _vm.Prob3 = (uint)(Prob3Box.Value ?? 0); _vm.Prob4 = (uint)(Prob4Box.Value ?? 0); _vm.Prob5 = (uint)(Prob5Box.Value ?? 0);
                _vm.Prob6 = (uint)(Prob6Box.Value ?? 0); _vm.Prob7 = (uint)(Prob7Box.Value ?? 0); _vm.Prob8 = (uint)(Prob8Box.Value ?? 0);
                _vm.WriteProbability();
                _undoService.Commit();
                CoreState.Services?.ShowInfo("World map monster probability written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"MonsterWMapProbabilityViewerView.ProbWrite: {ex}"); }
        }

        void OnProbFilterChanged(object? sender, SelectionChangedEventArgs e)
        {
            _vm.ProbIsEphraim = ProbFilter.SelectedIndex == 1;
            LoadProbList();
            // Keep the stage filter in sync (WinForms N2_Filter<->N1_Filter).
            if (StageFilter.SelectedIndex != ProbFilter.SelectedIndex)
                StageFilter.SelectedIndex = ProbFilter.SelectedIndex;
        }

        // ----------------------------------------------------------------
        // Surface 4 — skirmish events
        // ----------------------------------------------------------------
        void LoadSkirmishEvents()
        {
            try
            {
                _vm.LoadSkirmishEvents();
                SkirmishStartBox.Value = _vm.SkirmishStartEvent;
                SkirmishEndBox.Value = _vm.SkirmishEndEvent;
            }
            catch (Exception ex)
            {
                Log.Error($"MonsterWMapProbabilityViewerView.LoadSkirmishEvents failed: {ex}");
            }
        }

        void SkirmishWrite_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit World Map Skirmish Events");
            try
            {
                _vm.SkirmishStartEvent = (uint)(SkirmishStartBox.Value ?? 0);
                _vm.SkirmishEndEvent = (uint)(SkirmishEndBox.Value ?? 0);
                _vm.WriteSkirmishEvents();
                _undoService.Commit();
                CoreState.Services?.ShowInfo("World map skirmish events written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"MonsterWMapProbabilityViewerView.SkirmishWrite: {ex}"); }
        }

        void SkirmishStartJump_Click(object? sender, RoutedEventArgs e)
            => JumpToEvent((uint)(SkirmishStartBox.Value ?? 0));

        void SkirmishEndJump_Click(object? sender, RoutedEventArgs e)
            => JumpToEvent((uint)(SkirmishEndBox.Value ?? 0));

        void JumpToEvent(uint addr)
        {
            try
            {
                // World-map event — flag the editor BEFORE NavigateTo so the
                // termination scan + Write-All terminator use the world-map rules
                // (matches WorldMapEventPointerView jump semantics).
                var view = WindowManager.Instance.Open<EventScriptView>();
                view.SetEventKind(isWorldMapEvent: true, isTopLevelEvent: false);
                view.NavigateTo(addr);
            }
            catch (Exception ex)
            {
                Log.Error($"MonsterWMapProbabilityViewerView.JumpToEvent failed: {ex}");
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>Mirror the selected index from one list into the other (WinForms N1<->N2 row sync).</summary>
        void MirrorSelection(AddressListControl from, AddressListControl to)
        {
            int idx = from.SelectedOriginalIndex;
            if (idx < 0) return;
            if (to.SelectedOriginalIndex == idx) return;
            to.SelectByIndex(idx);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
