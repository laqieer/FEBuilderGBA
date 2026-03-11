using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEditorView : Window, IEditorView, IDataVerifiableView
    {
        readonly ItemEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

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
            try
            {
                var items = _vm.LoadItemList();
                ItemList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnItemSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItem(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("ItemEditorView.OnItemSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            ItemList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameLabel.Text = _vm.Name;

            // Text IDs
            NameIdBox.Value = _vm.NameId;
            DescIdBox.Value = _vm.DescId;
            UseDescIdBox.Value = _vm.UseDescId;

            // Identity
            ItemNumberBox.Value = _vm.ItemNumber;
            WeaponTypeBox.Value = _vm.WeaponType;

            // Traits
            Trait1Box.Value = _vm.Trait1;
            Trait2Box.Value = _vm.Trait2;
            Trait3Box.Value = _vm.Trait3;
            Trait4Box.Value = _vm.Trait4;

            // Pointers
            StatBonusesPtrBox.Text = $"0x{_vm.StatBonusesPtr:X08}";
            EffectivenessPtrBox.Text = $"0x{_vm.EffectivenessPtr:X08}";

            // Combat stats
            UsesBox.Value = _vm.Uses;
            MightBox.Value = _vm.Might;
            HitBox.Value = _vm.Hit;
            WeightBox.Value = _vm.Weight;
            CritBox.Value = _vm.Crit;
            RangeBox.Value = _vm.Range;
            PriceBox.Value = _vm.Price;

            // Weapon rank & effects
            WeaponRankBox.Value = _vm.WeaponRank;
            IconBox.Value = _vm.Icon;
            UsageEffectBox.Value = _vm.UsageEffect;
            DamageEffectBox.Value = _vm.DamageEffect;
            WeaponExpBox.Value = _vm.WeaponExp;

            // Extension bytes
            Unk33Box.Value = _vm.Unk33;
            Unk34Box.Value = _vm.Unk34;
            Unk35Box.Value = _vm.Unk35;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.DescId = (uint)(DescIdBox.Value ?? 0);
            _vm.UseDescId = (uint)(UseDescIdBox.Value ?? 0);
            _vm.ItemNumber = (uint)(ItemNumberBox.Value ?? 0);
            _vm.WeaponType = (uint)(WeaponTypeBox.Value ?? 0);
            _vm.Trait1 = (uint)(Trait1Box.Value ?? 0);
            _vm.Trait2 = (uint)(Trait2Box.Value ?? 0);
            _vm.Trait3 = (uint)(Trait3Box.Value ?? 0);
            _vm.Trait4 = (uint)(Trait4Box.Value ?? 0);
            _vm.StatBonusesPtr = ParseHexText(StatBonusesPtrBox.Text);
            _vm.EffectivenessPtr = ParseHexText(EffectivenessPtrBox.Text);
            _vm.Uses = (uint)(UsesBox.Value ?? 0);
            _vm.Might = (uint)(MightBox.Value ?? 0);
            _vm.Hit = (uint)(HitBox.Value ?? 0);
            _vm.Weight = (uint)(WeightBox.Value ?? 0);
            _vm.Crit = (uint)(CritBox.Value ?? 0);
            _vm.Range = (uint)(RangeBox.Value ?? 0);
            _vm.Price = (uint)(PriceBox.Value ?? 0);
            _vm.WeaponRank = (uint)(WeaponRankBox.Value ?? 0);
            _vm.Icon = (uint)(IconBox.Value ?? 0);
            _vm.UsageEffect = (uint)(UsageEffectBox.Value ?? 0);
            _vm.DamageEffect = (uint)(DamageEffectBox.Value ?? 0);
            _vm.WeaponExp = (uint)(WeaponExpBox.Value ?? 0);
            _vm.Unk33 = (uint)(Unk33Box.Value ?? 0);
            _vm.Unk34 = (uint)(Unk34Box.Value ?? 0);
            _vm.Unk35 = (uint)(Unk35Box.Value ?? 0);

            _undoService.Begin("Edit Item");
            try
            {
                _vm.WriteItem();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Item data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        public ViewModelBase? DataViewModel => _vm;

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        /// <summary>Select the first item in the list (for smoke testing).</summary>
        public void SelectFirstItem()
        {
            ItemList.SelectFirst();
        }
    }
}
