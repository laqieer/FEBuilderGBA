using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEditorView : Window, IPickableEditor, IDataVerifiableView
    {
        readonly ItemEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        List<(uint id, string name)> _weaponTypeList = new();

        public string ViewTitle => R._("Item Editor");
        public bool IsLoaded => _vm.CanWrite;

        public event Action<PickResult>? SelectionConfirmed;

        public ItemEditorView()
        {
            InitializeComponent();
            ItemList.SelectedAddressChanged += OnItemSelected;
            ItemList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
            Opened += (_, _) => LoadList();

            // Set trait flag names
            Trait1Flags.SetBitNames(AbilityFlagNames.ItemTrait1);
            Trait2Flags.SetBitNames(AbilityFlagNames.ItemTrait2);
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemList();
                ItemList.SetItems(items);

                // Populate weapon type combo
                _weaponTypeList = ComboResourceHelper.MakeWeaponTypeList();
                WeaponTypeCombo.ItemsSource = _weaponTypeList.Select(x => x.name).ToList();

                // Show "Edit Skill Config" button if a skill system is installed
                EditSkillConfigButton.IsVisible = PatchDetectionService.Instance.HasSkillSystem;
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
                TryShowListPreview();
                UpdateWarnings();
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

            // Weapon type combo
            int wtIdx = _weaponTypeList.FindIndex(x => x.id == _vm.WeaponType);
            WeaponTypeCombo.SelectedIndex = wtIdx >= 0 ? wtIdx : (int)_vm.WeaponType;

            // Trait flags (BitFlagPanel)
            Trait1Flags.Value = (byte)_vm.Trait1;
            Trait2Flags.Value = (byte)_vm.Trait2;
            Trait3Flags.Value = (byte)_vm.Trait3;
            Trait4Flags.Value = (byte)_vm.Trait4;

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

            UpdateComputedUI();
        }

        void UpdateComputedUI()
        {
            // Shop prices
            ShopBuyPriceLabel.Text = _vm.ShopBuyPrice.ToString();
            ShopSellPriceLabel.Text = _vm.ShopSellPrice.ToString();
            ShopForgePriceLabel.Text = _vm.ShopForgePrice.ToString();

            // Null pointer warnings
            AllocStatBonusesWarning.IsVisible = _vm.ShowAllocStatBonuses;
            AllocEffectivenessWarning.IsVisible = _vm.ShowAllocEffectiveness;

            // Effective class list
            EffectiveClassBorder.IsVisible = _vm.HasEffectiveClasses;
            if (_vm.HasEffectiveClasses)
                EffectiveClassListBox.ItemsSource = _vm.EffectiveClassList;

            // Stat bonus preview
            BonusPreviewBorder.IsVisible = _vm.HasBonusPreview;
            if (_vm.HasBonusPreview)
            {
                BonusHPLabel.Text = $"HP: {_vm.BonusHP:+#;-#;0}";
                BonusStrLabel.Text = $"Str: {_vm.BonusStr:+#;-#;0}";
                BonusSklLabel.Text = $"Skl: {_vm.BonusSkl:+#;-#;0}";
                BonusSpdLabel.Text = $"Spd: {_vm.BonusSpd:+#;-#;0}";
                BonusDefLabel.Text = $"Def: {_vm.BonusDef:+#;-#;0}";
                BonusResLabel.Text = $"Res: {_vm.BonusRes:+#;-#;0}";
                BonusLckLabel.Text = $"Lck: {_vm.BonusLck:+#;-#;0}";
                BonusMoveLabel.Text = $"Move: {_vm.BonusMove:+#;-#;0}";
                BonusConLabel.Text = $"Con: {_vm.BonusCon:+#;-#;0}";
            }
        }

        void ReadFromUI()
        {
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.DescId = (uint)(DescIdBox.Value ?? 0);
            _vm.UseDescId = (uint)(UseDescIdBox.Value ?? 0);
            _vm.ItemNumber = (uint)(ItemNumberBox.Value ?? 0);

            // Weapon type from combo
            int wtIdx = WeaponTypeCombo.SelectedIndex;
            _vm.WeaponType = wtIdx >= 0 && wtIdx < _weaponTypeList.Count ? _weaponTypeList[wtIdx].id : 0;

            // Trait flags from BitFlagPanel
            _vm.Trait1 = Trait1Flags.Value;
            _vm.Trait2 = Trait2Flags.Value;
            _vm.Trait3 = Trait3Flags.Value;
            _vm.Trait4 = Trait4Flags.Value;

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

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            _vm.RecalcComputed();
            UpdateComputedUI();

            _undoService.Begin(R._("Edit Item"));
            try
            {
                _vm.WriteItem();
                _undoService.Commit();
                _vm.MarkClean();
                UpdateWarnings();
                CoreState.Services.ShowInfo(R._("Item data written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        void JumpToStatBonuses_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint ptr = ParseHexText(StatBonusesPtrBox.Text);
                if (!U.isPointer(ptr)) return;
                uint addr = ptr - 0x08000000;
                if (!U.isSafetyOffset(addr)) return;
                var pds = PatchDetectionService.Instance;
                if (pds.HasSkillSystem)
                    WindowManager.Instance.Navigate<ItemStatBonusesSkillSystemsView>(addr);
                else if (pds.VennouWeaponLock)
                    WindowManager.Instance.Navigate<ItemStatBonusesVennoView>(addr);
                else
                    WindowManager.Instance.Navigate<ItemStatBonusesViewerView>(addr);
            }
            catch (Exception ex)
            {
                Log.Error("JumpToStatBonuses failed: {0}", ex.Message);
            }
        }

        void JumpToEffectiveness_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint ptr = ParseHexText(EffectivenessPtrBox.Text);
                if (!U.isPointer(ptr)) return;
                uint addr = ptr - 0x08000000;
                if (!U.isSafetyOffset(addr)) return;
                if (PatchDetectionService.Instance.SkillSystemsClassTypeRework)
                    WindowManager.Instance.Navigate<ItemEffectivenessSkillSystemsReworkView>(addr);
                else
                    WindowManager.Instance.Navigate<ItemEffectivenessViewerView>(addr);
            }
            catch (Exception ex)
            {
                Log.Error("JumpToEffectiveness failed: {0}", ex.Message);
            }
        }

        void UpdateWarnings()
        {
            var warnings = _vm.ValidateItem();
            WarningsBorder.IsVisible = warnings.Count > 0;
            WarningsList.ItemsSource = warnings;
        }

        public ViewModelBase? DataViewModel => _vm;

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        void EditSkillConfig_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var skillType = PatchDetectionService.Instance.SkillSystem;
                switch (skillType)
                {
                    case PatchDetectionService.SkillSystemType.SkillSystem:
                        WindowManager.Instance.Open<SkillConfigSkillSystemView>();
                        break;
                    case PatchDetectionService.SkillSystemType.CSkillSys09x:
                    case PatchDetectionService.SkillSystemType.CSkillSys300:
                        WindowManager.Instance.Open<SkillConfigFE8UCSkillSys09xView>();
                        break;
                    case PatchDetectionService.SkillSystemType.FE8N:
                        WindowManager.Instance.Open<SkillConfigFE8NSkillView>();
                        break;
                    case PatchDetectionService.SkillSystemType.FE8N_Ver2:
                        WindowManager.Instance.Open<SkillConfigFE8NVer2SkillView>();
                        break;
                    case PatchDetectionService.SkillSystemType.FE8N_Ver3:
                        WindowManager.Instance.Open<SkillConfigFE8NVer3SkillView>();
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("EditSkillConfig_Click failed: {0}", ex.Message);
            }
        }

        async void ExportTSV_Click(object? sender, RoutedEventArgs e)
        {
            await TableExportImportHelper.ExportTableAsync(this, "items");
        }

        async void ImportTSV_Click(object? sender, RoutedEventArgs e)
        {
            await TableExportImportHelper.ImportTableAsync(this, "items", _undoService, () =>
            {
                // Reload the current entry after import
                if (_vm.CurrentAddr != 0)
                {
                    _vm.LoadItem(_vm.CurrentAddr);
                    UpdateUI();
                }
            });
        }

        public void EnablePickMode() => ItemList.EnablePickMode();

        /// <summary>Select the first item in the list (for smoke testing).</summary>
        public void SelectFirstItem()
        {
            ItemList.SelectFirst();
        }
    }
}
