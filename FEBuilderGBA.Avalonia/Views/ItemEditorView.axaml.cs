using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEditorView : Window, IEditorView
    {
        readonly ItemEditorViewModel _vm = new();

        public string ViewTitle => "Item Editor";
        public bool IsLoaded => _vm.CanWrite;

        public ItemEditorView()
        {
            InitializeComponent();
            ItemList.SelectedAddressChanged += OnItemSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            var items = _vm.LoadItemList();
            ItemList.SetItems(items);
        }

        void OnItemSelected(uint addr)
        {
            _vm.LoadItem(addr);
            UpdateUI();
        }

        public void NavigateTo(uint address)
        {
            ItemList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameLabel.Text = _vm.Name;
            NameIdBox.Value = _vm.NameId;
            WeaponTypeBox.Value = _vm.WeaponType;
            RankBox.Value = _vm.Rank;
            MightBox.Value = _vm.Might;
            HitBox.Value = _vm.Hit;
            WeightBox.Value = _vm.Weight;
            CritBox.Value = _vm.Crit;
            RangeBox.Value = _vm.Range;
            UsesBox.Value = _vm.Uses;
            PriceBox.Value = _vm.Price;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.WeaponType = (uint)(WeaponTypeBox.Value ?? 0);
            _vm.Rank = (uint)(RankBox.Value ?? 0);
            _vm.Might = (uint)(MightBox.Value ?? 0);
            _vm.Hit = (uint)(HitBox.Value ?? 0);
            _vm.Weight = (uint)(WeightBox.Value ?? 0);
            _vm.Crit = (uint)(CritBox.Value ?? 0);
            _vm.Range = (uint)(RangeBox.Value ?? 0);
            _vm.Uses = (uint)(UsesBox.Value ?? 0);
            _vm.Price = (uint)(PriceBox.Value ?? 0);
            _vm.WriteItem();
            CoreState.Services.ShowInfo("Item data written.");
        }
    }
}
