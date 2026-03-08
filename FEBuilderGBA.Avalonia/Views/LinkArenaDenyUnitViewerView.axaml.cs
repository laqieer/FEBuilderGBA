using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class LinkArenaDenyUnitViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly LinkArenaDenyUnitViewerViewModel _vm = new();

        public string ViewTitle => "Link Arena Deny Unit";
        public bool IsLoaded => _vm.CanWrite;

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
            UnitIdBox.Value = _vm.UnitId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
            _vm.WriteLinkArenaDenyUnit();
            CoreState.Services.ShowInfo("Link Arena Deny Unit data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
