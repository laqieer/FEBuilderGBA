using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDView : Window, IEditorView, IDataVerifiableView
    {
        readonly EDViewModel _vm = new();

        public string ViewTitle => "Ending Event";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public EDView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadEDList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("EDView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadED(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EDView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdLabel.Text = $"0x{_vm.UnitId:X02} ({_vm.UnitId})";
            FlagLabel.Text = $"0x{_vm.Flag:X02} ({_vm.Flag})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
