using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportUnitFE6View : Window, IEditorView, IDataVerifiableView
    {
        readonly SupportUnitFE6ViewModel _vm = new();

        public string ViewTitle => "Support Units (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportUnitFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += Write_Click;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSupportUnitList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SupportUnitFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadSupportUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SupportUnitFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            Partner1Nud.Value  = _vm.Partner1;
            Partner2Nud.Value  = _vm.Partner2;
            Partner3Nud.Value  = _vm.Partner3;
            Partner4Nud.Value  = _vm.Partner4;
            Partner5Nud.Value  = _vm.Partner5;
            Partner6Nud.Value  = _vm.Partner6;
            Partner7Nud.Value  = _vm.Partner7;
            Partner8Nud.Value  = _vm.Partner8;
            Partner9Nud.Value  = _vm.Partner9;
            Partner10Nud.Value = _vm.Partner10;

            InitialValue1Nud.Value  = _vm.InitialValue1;
            InitialValue2Nud.Value  = _vm.InitialValue2;
            InitialValue3Nud.Value  = _vm.InitialValue3;
            InitialValue4Nud.Value  = _vm.InitialValue4;
            InitialValue5Nud.Value  = _vm.InitialValue5;
            InitialValue6Nud.Value  = _vm.InitialValue6;
            InitialValue7Nud.Value  = _vm.InitialValue7;
            InitialValue8Nud.Value  = _vm.InitialValue8;
            InitialValue9Nud.Value  = _vm.InitialValue9;
            InitialValue10Nud.Value = _vm.InitialValue10;

            GrowthRate1Nud.Value  = _vm.GrowthRate1;
            GrowthRate2Nud.Value  = _vm.GrowthRate2;
            GrowthRate3Nud.Value  = _vm.GrowthRate3;
            GrowthRate4Nud.Value  = _vm.GrowthRate4;
            GrowthRate5Nud.Value  = _vm.GrowthRate5;
            GrowthRate6Nud.Value  = _vm.GrowthRate6;
            GrowthRate7Nud.Value  = _vm.GrowthRate7;
            GrowthRate8Nud.Value  = _vm.GrowthRate8;
            GrowthRate9Nud.Value  = _vm.GrowthRate9;
            GrowthRate10Nud.Value = _vm.GrowthRate10;

            PartnerCountNud.Value = _vm.PartnerCount;
            SeparatorNud.Value = _vm.Separator;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Partner1  = (uint)(Partner1Nud.Value ?? 0);
                _vm.Partner2  = (uint)(Partner2Nud.Value ?? 0);
                _vm.Partner3  = (uint)(Partner3Nud.Value ?? 0);
                _vm.Partner4  = (uint)(Partner4Nud.Value ?? 0);
                _vm.Partner5  = (uint)(Partner5Nud.Value ?? 0);
                _vm.Partner6  = (uint)(Partner6Nud.Value ?? 0);
                _vm.Partner7  = (uint)(Partner7Nud.Value ?? 0);
                _vm.Partner8  = (uint)(Partner8Nud.Value ?? 0);
                _vm.Partner9  = (uint)(Partner9Nud.Value ?? 0);
                _vm.Partner10 = (uint)(Partner10Nud.Value ?? 0);

                _vm.InitialValue1  = (uint)(InitialValue1Nud.Value ?? 0);
                _vm.InitialValue2  = (uint)(InitialValue2Nud.Value ?? 0);
                _vm.InitialValue3  = (uint)(InitialValue3Nud.Value ?? 0);
                _vm.InitialValue4  = (uint)(InitialValue4Nud.Value ?? 0);
                _vm.InitialValue5  = (uint)(InitialValue5Nud.Value ?? 0);
                _vm.InitialValue6  = (uint)(InitialValue6Nud.Value ?? 0);
                _vm.InitialValue7  = (uint)(InitialValue7Nud.Value ?? 0);
                _vm.InitialValue8  = (uint)(InitialValue8Nud.Value ?? 0);
                _vm.InitialValue9  = (uint)(InitialValue9Nud.Value ?? 0);
                _vm.InitialValue10 = (uint)(InitialValue10Nud.Value ?? 0);

                _vm.GrowthRate1  = (uint)(GrowthRate1Nud.Value ?? 0);
                _vm.GrowthRate2  = (uint)(GrowthRate2Nud.Value ?? 0);
                _vm.GrowthRate3  = (uint)(GrowthRate3Nud.Value ?? 0);
                _vm.GrowthRate4  = (uint)(GrowthRate4Nud.Value ?? 0);
                _vm.GrowthRate5  = (uint)(GrowthRate5Nud.Value ?? 0);
                _vm.GrowthRate6  = (uint)(GrowthRate6Nud.Value ?? 0);
                _vm.GrowthRate7  = (uint)(GrowthRate7Nud.Value ?? 0);
                _vm.GrowthRate8  = (uint)(GrowthRate8Nud.Value ?? 0);
                _vm.GrowthRate9  = (uint)(GrowthRate9Nud.Value ?? 0);
                _vm.GrowthRate10 = (uint)(GrowthRate10Nud.Value ?? 0);

                _vm.PartnerCount = (uint)(PartnerCountNud.Value ?? 0);
                _vm.Separator = (uint)(SeparatorNud.Value ?? 0);

                _vm.WriteSupportUnit();
            }
            catch (Exception ex)
            {
                Log.Error("SupportUnitFE6View.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
