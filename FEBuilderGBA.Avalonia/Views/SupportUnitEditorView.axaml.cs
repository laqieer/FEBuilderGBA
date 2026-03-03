using System;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportUnitEditorView : Window, IEditorView
    {
        readonly SupportUnitEditorViewModel _vm = new();

        public string ViewTitle => "Support Unit Editor";
        public bool IsLoaded => _vm.CanWrite;

        public SupportUnitEditorView()
        {
            InitializeComponent();
            SupportList.SelectedAddressChanged += OnSupportSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSupportUnitList();
                SupportList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SupportUnitEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSupportSelected(uint addr)
        {
            try
            {
                _vm.LoadSupportUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SupportUnitEditorView.OnSupportSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            SupportList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitNameLabel.Text = _vm.UnitName;

            if (_vm.Supports.Count == 0)
            {
                PartnersBlock.Text = "(No support partners)";
                return;
            }

            var sb = new StringBuilder();
            foreach (var s in _vm.Supports)
            {
                sb.AppendLine($"Partner {s.Index}: ID=0x{s.PartnerId:X02} {s.PartnerName}");
            }
            PartnersBlock.Text = sb.ToString();
        }

        public void SelectFirstItem()
        {
            SupportList.SelectFirst();
        }
    }
}
