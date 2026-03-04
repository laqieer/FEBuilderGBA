using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MonsterItemViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly MonsterItemViewerViewModel _vm = new();

        public string ViewTitle => "Monster Item";
        public bool IsLoaded => _vm.IsLoaded;

        public MonsterItemViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMonsterItemList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MonsterItemViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMonsterItem(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MonsterItemViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ItemIdLabel.Text = $"0x{_vm.ItemId:X02} ({_vm.ItemId})";
            DropRateLabel.Text = $"{_vm.DropRate}";
            Unknown1Label.Text = $"0x{_vm.Unknown1:X02}";
            Unknown2Label.Text = $"0x{_vm.Unknown2:X02}";
            Unknown3Label.Text = $"0x{_vm.Unknown3:X02}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
