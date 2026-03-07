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

            B0Nud.Value  = _vm.B0;
            B1Nud.Value  = _vm.B1;
            B2Nud.Value  = _vm.B2;
            B3Nud.Value  = _vm.B3;
            B4Nud.Value  = _vm.B4;
            B5Nud.Value  = _vm.B5;
            B6Nud.Value  = _vm.B6;
            B7Nud.Value  = _vm.B7;
            B8Nud.Value  = _vm.B8;
            B9Nud.Value  = _vm.B9;

            B10Nud.Value = _vm.B10;
            B11Nud.Value = _vm.B11;
            B12Nud.Value = _vm.B12;
            B13Nud.Value = _vm.B13;
            B14Nud.Value = _vm.B14;
            B15Nud.Value = _vm.B15;
            B16Nud.Value = _vm.B16;
            B17Nud.Value = _vm.B17;
            B18Nud.Value = _vm.B18;
            B19Nud.Value = _vm.B19;

            B20Nud.Value = _vm.B20;
            B21Nud.Value = _vm.B21;
            B22Nud.Value = _vm.B22;
            B23Nud.Value = _vm.B23;
            B24Nud.Value = _vm.B24;
            B25Nud.Value = _vm.B25;
            B26Nud.Value = _vm.B26;
            B27Nud.Value = _vm.B27;
            B28Nud.Value = _vm.B28;
            B29Nud.Value = _vm.B29;

            B30Nud.Value = _vm.B30;
            B31Nud.Value = _vm.B31;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.B0  = (uint)(B0Nud.Value ?? 0);
                _vm.B1  = (uint)(B1Nud.Value ?? 0);
                _vm.B2  = (uint)(B2Nud.Value ?? 0);
                _vm.B3  = (uint)(B3Nud.Value ?? 0);
                _vm.B4  = (uint)(B4Nud.Value ?? 0);
                _vm.B5  = (uint)(B5Nud.Value ?? 0);
                _vm.B6  = (uint)(B6Nud.Value ?? 0);
                _vm.B7  = (uint)(B7Nud.Value ?? 0);
                _vm.B8  = (uint)(B8Nud.Value ?? 0);
                _vm.B9  = (uint)(B9Nud.Value ?? 0);

                _vm.B10 = (uint)(B10Nud.Value ?? 0);
                _vm.B11 = (uint)(B11Nud.Value ?? 0);
                _vm.B12 = (uint)(B12Nud.Value ?? 0);
                _vm.B13 = (uint)(B13Nud.Value ?? 0);
                _vm.B14 = (uint)(B14Nud.Value ?? 0);
                _vm.B15 = (uint)(B15Nud.Value ?? 0);
                _vm.B16 = (uint)(B16Nud.Value ?? 0);
                _vm.B17 = (uint)(B17Nud.Value ?? 0);
                _vm.B18 = (uint)(B18Nud.Value ?? 0);
                _vm.B19 = (uint)(B19Nud.Value ?? 0);

                _vm.B20 = (uint)(B20Nud.Value ?? 0);
                _vm.B21 = (uint)(B21Nud.Value ?? 0);
                _vm.B22 = (uint)(B22Nud.Value ?? 0);
                _vm.B23 = (uint)(B23Nud.Value ?? 0);
                _vm.B24 = (uint)(B24Nud.Value ?? 0);
                _vm.B25 = (uint)(B25Nud.Value ?? 0);
                _vm.B26 = (uint)(B26Nud.Value ?? 0);
                _vm.B27 = (uint)(B27Nud.Value ?? 0);
                _vm.B28 = (uint)(B28Nud.Value ?? 0);
                _vm.B29 = (uint)(B29Nud.Value ?? 0);

                _vm.B30 = (uint)(B30Nud.Value ?? 0);
                _vm.B31 = (uint)(B31Nud.Value ?? 0);

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
