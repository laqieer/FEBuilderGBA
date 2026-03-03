using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class LinkArenaDenyUnitViewerView : Window, IEditorView
    {
        readonly LinkArenaDenyUnitViewerViewModel _vm = new();

        public string ViewTitle => "Link Arena Deny Unit";
        public bool IsLoaded => _vm.IsLoaded;

        public LinkArenaDenyUnitViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadLinkArenaDenyUnitList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("LinkArenaDenyUnitViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadLinkArenaDenyUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("LinkArenaDenyUnitViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdLabel.Text = $"0x{_vm.UnitId:X02} ({_vm.UnitId})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
