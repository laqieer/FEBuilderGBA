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
            var labels = new TextBlock[] {
                Partner0Label, Partner1Label, Partner2Label, Partner3Label, Partner4Label,
                Partner5Label, Partner6Label, Partner7Label, Partner8Label, Partner9Label
            };
            for (int i = 0; i < labels.Length && i < _vm.Partners.Length; i++)
            {
                labels[i].Text = $"0x{_vm.Partners[i]:X02} ({_vm.Partners[i]})";
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
