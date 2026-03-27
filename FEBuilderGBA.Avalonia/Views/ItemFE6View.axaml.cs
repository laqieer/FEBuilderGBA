using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemFE6View : TranslatedWindow, IPickableEditor, IDataVerifiableView
    {
        readonly ItemFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Items (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public event Action<PickResult>? SelectionConfirmed;

        public ItemFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            EntryList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
            Opened += (_, _) => LoadList();

            // Wire desc text live update
            DescIdBox.ValueChanged += OnDescIdChanged;
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("ItemFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItem(addr);
                UpdateUI();
                TryShowListPreview();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("ItemFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void TryShowListPreview()
        {
            try
            {
                var img = PreviewIconHelper.LoadItemIcon(_vm.Icon);
                if (img != null)
                {
                    ListPreviewImage.Zoom = 2;
                    ListPreviewImage.SetImage(img);
                    ListPreviewName.Text = _vm.Name;
                    ListPreviewBorder.IsVisible = true;
                    img.Dispose();
                }
                else
                {
                    ListPreviewImage.SetImage(null);
                    ListPreviewBorder.IsVisible = false;
                }
            }
            catch
            {
                ListPreviewImage.SetImage(null);
                ListPreviewBorder.IsVisible = false;
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameLabel.Text = _vm.Name;

            NameIdBox.Value = _vm.NameId;
            DescIdBox.Value = _vm.DescId;
            DescTextLabel.Text = _vm.DescText;
            UseDescIdBox.Value = _vm.UseDescId;
            ItemNumberBox.Value = _vm.ItemNumber;
            WeaponTypeBox.Value = _vm.WeaponType;
            Trait1Box.Value = _vm.Trait1;
            Trait2Box.Value = _vm.Trait2;
            Trait3Box.Value = _vm.Trait3;
            Trait4Box.Value = _vm.Trait4;
            StatBonusesPtrBox.Text = $"0x{_vm.StatBonusesPtr:X08}";
            EffectivenessPtrBox.Text = $"0x{_vm.EffectivenessPtr:X08}";
            UsesBox.Value = _vm.Uses;
            MightBox.Value = _vm.Might;
            HitBox.Value = _vm.Hit;
            WeightBox.Value = _vm.Weight;
            CritBox.Value = _vm.Crit;
            RangeBox.Value = _vm.Range;
            PriceBox.Value = _vm.Price;
            WeaponRankBox.Value = _vm.WeaponRank;
            IconBox.Value = _vm.Icon;
            UsageEffectBox.Value = _vm.UsageEffect;
            DamageEffectBox.Value = _vm.DamageEffect;
        }

        void OnDescIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(DescIdBox.Value ?? 0);
            try { DescTextLabel.Text = NameResolver.GetTextById(id); }
            catch { DescTextLabel.Text = ""; }
        }

        void JumpToDesc_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint textId = (uint)(DescIdBox.Value ?? 0);
                uint textPtr = rom.RomInfo.text_pointer;
                if (textPtr == 0) return;
                uint baseAddr = rom.p32(textPtr);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint addr = baseAddr + textId * 4;
                if (!U.isSafetyOffset(addr)) return;
                WindowManager.Instance.Navigate<TextViewerView>(addr);
            }
            catch (Exception ex)
            {
                Log.Error($"JumpToDesc failed: {ex.Message}");
            }
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

            _undoService.Begin("Edit Item (FE6)");
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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void EnablePickMode() => EntryList.EnablePickMode();
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
