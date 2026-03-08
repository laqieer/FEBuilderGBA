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
        public bool IsLoaded => _vm.CanWrite;

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
            ItemIdBox.Value = _vm.ItemId;
            DropRateBox.Value = _vm.DropRate;
            Unknown1Box.Value = _vm.Unknown1;
            Unknown2Box.Value = _vm.Unknown2;
            Unknown3Box.Value = _vm.Unknown3;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.ItemId = (uint)(ItemIdBox.Value ?? 0);
            _vm.DropRate = (uint)(DropRateBox.Value ?? 0);
            _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
            _vm.Unknown2 = (uint)(Unknown2Box.Value ?? 0);
            _vm.Unknown3 = (uint)(Unknown3Box.Value ?? 0);
            _vm.WriteMonsterItem();
            CoreState.Services.ShowInfo("Monster item data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
