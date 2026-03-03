using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SummonsDemonKingViewerView : Window, IEditorView
    {
        readonly SummonsDemonKingViewerViewModel _vm = new();

        public string ViewTitle => "Demon King Summon";
        public bool IsLoaded => _vm.IsLoaded;

        public SummonsDemonKingViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSummonsDemonKingList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SummonsDemonKingViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadSummonsDemonKing(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SummonsDemonKingViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdLabel.Text = $"0x{_vm.UnitId:X02} ({_vm.UnitId})";
            ClassIdLabel.Text = $"0x{_vm.ClassId:X02} ({_vm.ClassId})";
            UnknownLabel.Text = $"0x{_vm.Unknown1:X02}";
            UnitGrowLabel.Text = $"0x{_vm.UnitGrow:X04}";
            LevelLabel.Text = _vm.Level.ToString();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
