using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventUnitFE7View : Window, IEditorView
    {
        readonly EventUnitFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();

        readonly ObservableCollection<string> _mapDisplayItems = new();
        readonly ObservableCollection<string> _groupDisplayItems = new();
        readonly ObservableCollection<string> _unitDisplayItems = new();

        List<AddrResult> _mapItems = new();
        List<AddrResult> _groupItems = new();
        List<AddrResult> _unitItems = new();

        public string ViewTitle => "Event Unit (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventUnitFE7View()
        {
            InitializeComponent();
            MapListBox.ItemsSource = _mapDisplayItems;
            GroupListBox.ItemsSource = _groupDisplayItems;
            UnitListBox.ItemsSource = _unitDisplayItems;

            MapListBox.SelectionChanged += MapListBox_SelectionChanged;
            GroupListBox.SelectionChanged += GroupListBox_SelectionChanged;
            UnitListBox.SelectionChanged += UnitListBox_SelectionChanged;

            Opened += (_, _) => LoadMapList();
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
                Log.Error("EventUnitFE7View.LoadMapList failed: {0}", ex.Message);
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
                Log.Error("EventUnitFE7View.MapListBox_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void GroupListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                int idx = GroupListBox.SelectedIndex;
                if (idx < 0 || idx >= _groupItems.Count) return;

                uint groupAddr = _groupItems[idx].addr;
                LoadUnitsFromAddress(groupAddr);
            }
            catch (Exception ex)
            {
                Log.Error("EventUnitFE7View.GroupListBox_SelectionChanged failed: {0}", ex.Message);
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
                Log.Error("EventUnitFE7View.UnitListBox_SelectionChanged failed: {0}", ex.Message);
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
                    Log.Error("EventUnitFE7View: Invalid address {0}", text);
                    return;
                }
                LoadUnitsFromAddress(addr);
            }
            catch (Exception ex)
            {
                Log.Error("EventUnitFE7View.LoadAddr_Click failed: {0}", ex.Message);
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
                Log.Error("EventUnitFE7View.Write failed: {0}", ex.Message);
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
    }
}
