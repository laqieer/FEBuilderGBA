using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitFE7View : Window, IEditorView, IDataVerifiableView
    {
        readonly UnitFE7ViewModel _vm = new();

        public string ViewTitle => "Units (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public UnitFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadUnitList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("UnitFE7View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("UnitFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameIdLabel.Text = $"0x{_vm.NameId:X04}";
            DecodedNameLabel.Text = _vm.Name;
            LevelLabel.Text = $"{_vm.Level}";
            StatsLine1Label.Text = $"{_vm.HP} / {_vm.Str} / {_vm.Skl}";
            StatsLine2Label.Text = $"{_vm.Spd} / {_vm.Def} / {_vm.Res}";
            LckLabel.Text = $"{_vm.Lck}";
            GrowLine1Label.Text = $"{_vm.GrowHP}% / {_vm.GrowSTR}% / {_vm.GrowSKL}% / {_vm.GrowSPD}%";
            GrowLine2Label.Text = $"{_vm.GrowDEF}% / {_vm.GrowRES}% / {_vm.GrowLCK}%";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
