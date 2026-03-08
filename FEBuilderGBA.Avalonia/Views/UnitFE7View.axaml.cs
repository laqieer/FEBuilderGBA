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

        public string ViewTitle => "Units (FE7) Editor";
        public bool IsLoaded => _vm.CanWrite;

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
            NameIdBox.Value = _vm.NameId;
            DecodedNameLabel.Text = _vm.Name;
            W2Box.Value = _vm.W2;
            B4Box.Value = _vm.B4;
            B5Box.Value = _vm.B5;
            W6Box.Value = _vm.W6;
            B8Box.Value = _vm.B8;
            B9Box.Value = _vm.B9;
            B10Box.Value = _vm.B10;
            LevelBox.Value = _vm.Level;
            HPBox.Value = _vm.HP;
            StrBox.Value = _vm.Str;
            SklBox.Value = _vm.Skl;
            SpdBox.Value = _vm.Spd;
            DefBox.Value = _vm.Def;
            ResBox.Value = _vm.Res;
            LckBox.Value = _vm.Lck;
            B19Box.Value = _vm.B19Signed;
            GrowHPBox.Value = _vm.GrowHP;
            GrowSTRBox.Value = _vm.GrowSTR;
            GrowSKLBox.Value = _vm.GrowSKL;
            GrowSPDBox.Value = _vm.GrowSPD;
            GrowDEFBox.Value = _vm.GrowDEF;
            GrowRESBox.Value = _vm.GrowRES;
            GrowLCKBox.Value = _vm.GrowLCK;
            P44Box.Value = _vm.P44;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.W2 = (uint)(W2Box.Value ?? 0);
            _vm.B4 = (uint)(B4Box.Value ?? 0);
            _vm.B5 = (uint)(B5Box.Value ?? 0);
            _vm.W6 = (uint)(W6Box.Value ?? 0);
            _vm.B8 = (uint)(B8Box.Value ?? 0);
            _vm.B9 = (uint)(B9Box.Value ?? 0);
            _vm.B10 = (uint)(B10Box.Value ?? 0);
            _vm.Level = (uint)(LevelBox.Value ?? 0);
            _vm.HP = (int)(HPBox.Value ?? 0);
            _vm.Str = (int)(StrBox.Value ?? 0);
            _vm.Skl = (int)(SklBox.Value ?? 0);
            _vm.Spd = (int)(SpdBox.Value ?? 0);
            _vm.Def = (int)(DefBox.Value ?? 0);
            _vm.Res = (int)(ResBox.Value ?? 0);
            _vm.Lck = (int)(LckBox.Value ?? 0);
            _vm.B19Signed = (int)(B19Box.Value ?? 0);
            _vm.GrowHP = (uint)(GrowHPBox.Value ?? 0);
            _vm.GrowSTR = (uint)(GrowSTRBox.Value ?? 0);
            _vm.GrowSKL = (uint)(GrowSKLBox.Value ?? 0);
            _vm.GrowSPD = (uint)(GrowSPDBox.Value ?? 0);
            _vm.GrowDEF = (uint)(GrowDEFBox.Value ?? 0);
            _vm.GrowRES = (uint)(GrowRESBox.Value ?? 0);
            _vm.GrowLCK = (uint)(GrowLCKBox.Value ?? 0);
            _vm.P44 = (uint)(P44Box.Value ?? 0);
            _vm.WriteUnit();
            CoreState.Services?.ShowInfo("Unit (FE7) data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
