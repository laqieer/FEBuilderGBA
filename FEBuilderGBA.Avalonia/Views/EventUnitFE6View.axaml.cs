using System;
using global::Avalonia;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventUnitFE6View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventUnitFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        readonly ObservableCollection<string> _mapDisplayItems = new();
        readonly ObservableCollection<string> _groupDisplayItems = new();
        readonly ObservableCollection<string> _unitDisplayItems = new();

        List<AddrResult> _mapItems = new();
        List<MapEventUnitCore.UnitGroupResult> _groupItems = new();
        List<AddrResult> _unitItems = new();

        public string ViewTitle => "Event Unit (FE6)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Event Unit (FE6)", 1902, 1047, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public EventUnitFE6View()
        {
            InitializeComponent();
            MapListBox.ItemsSource = _mapDisplayItems;
            GroupListBox.ItemsSource = _groupDisplayItems;
            UnitListBox.ItemsSource = _unitDisplayItems;

            MapListBox.SelectionChanged += MapListBox_SelectionChanged;
            GroupListBox.SelectionChanged += GroupListBox_SelectionChanged;
            UnitListBox.SelectionChanged += UnitListBox_SelectionChanged;

        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreState.LanguageChanged -= OnLanguageChanged;
            CoreState.LanguageChanged += OnLanguageChanged;
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadMapList();
            }
            else
            {
                ReloadGroupsForLanguage();
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreState.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        void OnLanguageChanged()
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(
                ReloadGroupsForLanguage);
        }

        void ReloadGroupsForLanguage()
        {
            try { ReloadGroupsPreservingSelection(); }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE6View language refresh failed: {0}", ex.Message);
            }
        }

        void LoadMapList()
        {
            try
            {
                _mapItems = _vm.LoadMapList();
                _mapDisplayItems.Clear();
                foreach (var item in _mapItems)
                    _mapDisplayItems.Add(item.name);

                if (_mapItems.Count > 0)
                    MapListBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE6View.LoadMapList failed: {0}", ex.Message);
            }
        }

        void MapListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                ReloadGroupsPreservingSelection(
                    preserveCurrentSelection: false, clearUnits: true);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE6View.MapListBox_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void GroupListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                int idx = GroupListBox.SelectedIndex;
                if (idx < 0 || idx >= _groupItems.Count) return;

                uint groupAddr = _groupItems[idx].Addr;
                LoadUnitsFromAddress(groupAddr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE6View.GroupListBox_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void ReloadGroupsPreservingSelection(
            string? origin = null,
            uint address = U.NOT_FOUND,
            bool preserveCurrentSelection = true,
            bool clearUnits = false)
        {
            int mapIndex = MapListBox.SelectedIndex;
            if (mapIndex < 0 || mapIndex >= _mapItems.Count)
            {
                GroupListBox.SelectedIndex = -1;
                _groupItems.Clear();
                _groupDisplayItems.Clear();
                _unitItems.Clear();
                _unitDisplayItems.Clear();
                ClearDetail();
                return;
            }
            if (preserveCurrentSelection && origin == null)
            {
                int oldIndex = GroupListBox.SelectedIndex;
                if (oldIndex >= 0 && oldIndex < _groupItems.Count)
                {
                    origin = _groupItems[oldIndex].OriginKey;
                    address = _groupItems[oldIndex].Addr;
                }
            }
            GroupListBox.SelectedIndex = -1;
            if (clearUnits)
            {
                _unitItems.Clear();
                _unitDisplayItems.Clear();
                ClearDetail();
            }
            _groupItems = _vm.LoadUnitGroups(_mapItems[mapIndex].tag);
            _groupDisplayItems.Clear();
            foreach (var group in _groupItems) _groupDisplayItems.Add(group.Name);
            if (_groupItems.Count == 0)
            {
                _unitItems.Clear();
                _unitDisplayItems.Clear();
                ClearDetail();
                return;
            }
            int select = -1;
            for (int i = 0; i < _groupItems.Count; i++)
                if (_groupItems[i].OriginKey == origin && _groupItems[i].Addr == address)
                { select = i; break; }
            GroupListBox.SelectedIndex = select >= 0 ? select : (_groupItems.Count > 0 ? 0 : -1);
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
                Log.ErrorF("EventUnitFE6View.UnitListBox_SelectionChanged failed: {0}", ex.Message);
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
                    Log.ErrorF("EventUnitFE6View: Invalid address {0}", text);
                    return;
                }
                LoadUnitsFromAddress(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventUnitFE6View.LoadAddr_Click failed: {0}", ex.Message);
            }
        }

        void ClearDetail()
        {
            AddrLabel.Text = "";
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
            _vm.IsLoaded = false;
            _vm.CurrentAddr = 0;
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            UnitIDBox.Value = _vm.UnitID;
            ClassIDBox.Value = _vm.ClassID;
            LeaderUnitIDBox.Value = _vm.LeaderUnitID;
            UnitInfoBox.Value = _vm.UnitInfo;
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
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            _undoService.Begin("Edit Event Unit FE6");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EventUnitFE6View.Write failed: {0}", ex.Message);
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
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
