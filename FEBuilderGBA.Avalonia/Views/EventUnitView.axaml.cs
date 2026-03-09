using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventUnitView : Window, IEditorView
    {
        readonly EventUnitViewModel _vm = new();

        public string ViewTitle => "Event Unit Placement";
        public bool IsLoaded => _vm.IsLoaded;

        public EventUnitView()
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
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("EventUnitView.LoadList failed: {0}", ex.Message);
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
                Log.Error("EventUnitView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            UnitIDBox.Value = _vm.UnitID;
            ClassIDBox.Value = _vm.ClassID;
            LeaderUnitIDBox.Value = _vm.LeaderUnitID;
            UnitInfoBox.Value = _vm.UnitInfo;
            UnitGrowthBox.Value = _vm.UnitGrowth;
            Reserved6Box.Value = _vm.Reserved6;
            CoordCountBox.Value = _vm.CoordCount;
            CoordPointerBox.Text = string.Format("0x{0:X08}", _vm.CoordPointer);
            Item1Box.Value = _vm.Item1;
            Item2Box.Value = _vm.Item2;
            Item3Box.Value = _vm.Item3;
            Item4Box.Value = _vm.Item4;
            AI1PrimaryBox.Value = _vm.AI1Primary;
            AI2SecondaryBox.Value = _vm.AI2Secondary;
            AI3TargetRecoveryBox.Value = _vm.AI3TargetRecovery;
            AI4RetreatBox.Value = _vm.AI4Retreat;
        }

        void ReadFromUI()
        {
            _vm.UnitID = (uint)(UnitIDBox.Value ?? 0);
            _vm.ClassID = (uint)(ClassIDBox.Value ?? 0);
            _vm.LeaderUnitID = (uint)(LeaderUnitIDBox.Value ?? 0);
            _vm.UnitInfo = (uint)(UnitInfoBox.Value ?? 0);
            _vm.UnitGrowth = (uint)(UnitGrowthBox.Value ?? 0);
            _vm.Reserved6 = (uint)(Reserved6Box.Value ?? 0);
            _vm.CoordCount = (uint)(CoordCountBox.Value ?? 0);
            _vm.CoordPointer = U.atoh(CoordPointerBox.Text ?? "");
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
            try
            {
                ReadFromUI();
                _vm.WriteEntry();
            }
            catch (Exception ex)
            {
                Log.Error("EventUnitView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
