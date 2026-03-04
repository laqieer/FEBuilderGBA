using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportAttributeView : Window, IEditorView, IDataVerifiableView
    {
        readonly SupportAttributeViewModel _vm = new();

        public string ViewTitle => "Support Attribute";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportAttributeView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSupportAttributeList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SupportAttributeView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadSupportAttribute(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SupportAttributeView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AffinityTypeLabel.Text = $"0x{_vm.AffinityType:X02}";
            AttackBonusLabel.Text = _vm.AttackBonus.ToString();
            DefenseBonusLabel.Text = _vm.DefenseBonus.ToString();
            HitBonusLabel.Text = _vm.HitBonus.ToString();
            AvoidBonusLabel.Text = _vm.AvoidBonus.ToString();
            CritBonusLabel.Text = _vm.CritBonus.ToString();
            CritAvoidBonusLabel.Text = _vm.CritAvoidBonus.ToString();
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
