using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemFE6View : TranslatedWindow, IPickableEditor, IDataVerifiableView
    {
        readonly ItemFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();

        List<(uint id, string name)> _weaponTypeList = new();

        public string ViewTitle => "Items (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public event Action<PickResult>? SelectionConfirmed;

        public ItemFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            EntryList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
            Opened += (_, _) => LoadList();

            // Set trait flag names (FE6 shares the same trait1/trait2 bit
            // semantics as FE7/FE8 - mirrors PR #569 wiring).
            Trait1Flags.SetBitNames(AbilityFlagNames.ItemTrait1);
            Trait2Flags.SetBitNames(AbilityFlagNames.ItemTrait2);

            // Wire desc text live update
            DescIdBox.ValueChanged += OnDescIdChanged;
            UseDescIdBox.ValueChanged += OnUseDescIdChanged;

            // Copilot CLI review #576 finding 4: wire the Filter box so
            // typing actually narrows the item list (was previously
            // editable but inert). Mirrors WF `LabelFilter` text field.
            // #649: filter is now an inline slot on the EditorTopBar; the
            // routed FilterTextChanged event handler is wired in AXAML.
        }

        void LoadList()
        {
            try
            {
                // Populate combo dropdown BEFORE SetItems.
                _weaponTypeList = ComboResourceHelper.MakeWeaponTypeList();
                WeaponTypeCombo.ItemsSource = _weaponTypeList.Select(x => x.name).ToList();

                var items = _vm.LoadItemList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));

                UpdateListMetadata(items.Count);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void UpdateListMetadata(int count)
        {
            try
            {
                var rom = CoreState.ROM;
                if (TopBar != null)
                {
                    if (rom?.RomInfo != null)
                    {
                        uint baseAddr = rom.p32(rom.RomInfo.item_pointer);
                        TopBar.StartAddressText = $"0x{baseAddr:X08}";
                        TopBar.SizeText = $"{rom.RomInfo.item_datasize}";
                    }
                    else
                    {
                        TopBar.StartAddressText = "(no ROM)";
                        TopBar.SizeText = "-";
                    }
                    TopBar.ReadCountText = count.ToString();
                }
            }
            catch { /* purely informational */ }
        }

        // #649: routed events from the unified EditorTopBar control.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        /// <summary>
        /// Filter the item list by substring match against each entry's
        /// rendered name. Mirrors WF `LabelFilter` behavior (text-box
        /// typing narrows the list). Copilot CLI #576 finding 4.
        /// #649: now driven by the EditorTopBar FilterTextChanged routed event.
        /// </summary>
        void OnTopBarFilterTextChanged(object? sender, EditorTopBarFilterChangedEventArgs e)
        {
            try
            {
                string filter = (e.NewText ?? "").Trim();
                var items = _vm.LoadItemList();
                if (!string.IsNullOrEmpty(filter))
                {
                    items = items
                        .Where(r => r.name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                }
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
                UpdateListMetadata(items.Count);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemFE6View.OnTopBarFilterTextChanged failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItem(addr);
                // Copilot CLI review #576 finding 3: pass list index so
                // RecalcAllocFlags uses the WF `SelectedIndex > 0` gate
                // (dummy row 0 must NOT show null-pointer warnings).
                _vm.SelectedListIndex = EntryList.SelectedOriginalIndex;
                UpdateUI();
                TryShowListPreview();
                UpdateHardCodingWarning();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void TryShowListPreview()
        {
            try
            {
                var img = PreviewIconHelper.LoadItemIcon(_vm.Icon);
                if (img != null)
                {
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
            AddrLabelDup.Text = $"0x{_vm.CurrentAddr:X08}";
            NameLabel.Text = _vm.Name;

            NameIdBox.Value = _vm.NameId;
            DescIdBox.Value = _vm.DescId;
            DescTextLabel.Text = _vm.DescText;
            UseDescIdBox.Value = _vm.UseDescId;
            UseDescTextLabel.Text = _vm.UseDescText;
            ItemNumberBox.Value = _vm.ItemNumber;

            // Weapon type combo
            int wtIdx = _weaponTypeList.FindIndex(x => x.id == _vm.WeaponType);
            WeaponTypeCombo.SelectedIndex = wtIdx >= 0 ? wtIdx : (int)_vm.WeaponType;

            // Trait flags (BitFlagPanel) + hex preview labels (#402 mirrors
            // WF "特性1/2/3/4" plus the raw-byte readout).
            Trait1Flags.Value = (byte)_vm.Trait1;
            Trait2Flags.Value = (byte)_vm.Trait2;
            Trait3Box.Value = _vm.Trait3;
            Trait4Box.Value = _vm.Trait4;
            Trait1HexLabel.Text = $"= 0x{_vm.Trait1:X02}";
            Trait2HexLabel.Text = $"= 0x{_vm.Trait2:X02}";
            Trait3HexLabel.Text = $"= 0x{_vm.Trait3:X02}";
            Trait4HexLabel.Text = $"= 0x{_vm.Trait4:X02}";

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

            UpdateComputedUI();
        }

        void UpdateComputedUI()
        {
            // Shop prices (mirrors WF W26_ValueChanged).
            ShopBuyPriceLabel.Text = _vm.ShopBuyPrice.ToString();
            ShopSellPriceLabel.Text = _vm.ShopSellPrice.ToString();
            ShopShingekiPriceLabel.Text = _vm.ShopShingekiShopPrice.ToString();

            // Null pointer warnings + new-alloc buttons (#831). The whole row
            // (warning label + New-alloc button) shows only when the field
            // pointer is 0 and SelectedListIndex > 0, matching the WF
            // visibility gate (UpdateStateByAllocEvent).
            AllocStatBonusesRow.IsVisible = _vm.ShowAllocStatBonuses;
            AllocEffectivenessRow.IsVisible = _vm.ShowAllocEffectiveness;

            // Effective class list.
            EffectiveClassBorder.IsVisible = _vm.HasEffectiveClasses;
            if (_vm.HasEffectiveClasses)
                EffectiveClassListBox.ItemsSource = _vm.EffectiveClassList;

            // Stat bonus preview.
            BonusPreviewBorder.IsVisible = _vm.HasBonusPreview;
            if (_vm.HasBonusPreview)
            {
                BonusHPLabel.Text   = $"HP: {_vm.BonusHP:+#;-#;0}";
                BonusStrLabel.Text  = $"Str: {_vm.BonusStr:+#;-#;0}";
                BonusSklLabel.Text  = $"Skl: {_vm.BonusSkl:+#;-#;0}";
                BonusSpdLabel.Text  = $"Spd: {_vm.BonusSpd:+#;-#;0}";
                BonusDefLabel.Text  = $"Def: {_vm.BonusDef:+#;-#;0}";
                BonusResLabel.Text  = $"Res: {_vm.BonusRes:+#;-#;0}";
                BonusLckLabel.Text  = $"Lck: {_vm.BonusLck:+#;-#;0}";
                BonusMoveLabel.Text = $"Move: {_vm.BonusMove:+#;-#;0}";
                BonusConLabel.Text  = $"Con: {_vm.BonusCon:+#;-#;0}";
            }
        }

        void OnDescIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(DescIdBox.Value ?? 0);
            try { DescTextLabel.Text = NameResolver.GetTextById(id); }
            catch { DescTextLabel.Text = ""; }
        }

        void OnUseDescIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(UseDescIdBox.Value ?? 0);
            try { UseDescTextLabel.Text = NameResolver.GetTextById(id); }
            catch { UseDescTextLabel.Text = ""; }
        }

        void JumpToDesc_Click(object? sender, RoutedEventArgs e)
        {
            NavigateToTextId((uint)(DescIdBox.Value ?? 0));
        }

        void JumpToUseDesc_Click(object? sender, RoutedEventArgs e)
        {
            NavigateToTextId((uint)(UseDescIdBox.Value ?? 0));
        }

        // -- Hyperlink label click handlers --

        void OnNameIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            NavigateToTextId((uint)(NameIdBox.Value ?? 0));
        }

        void OnDescIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            NavigateToTextId((uint)(DescIdBox.Value ?? 0));
        }

        void OnUseDescIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            NavigateToTextId((uint)(UseDescIdBox.Value ?? 0));
        }

        void NavigateToTextId(uint textId)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
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
                Log.Error($"NavigateToTextId failed: {ex.Message}");
            }
        }

        // -- HardCoding warning (#402) ---------------------------------------
        // Mirrors WF `HardCodingWarningLabel_Click` + `CheckHardCodingWarning`.
        // Uses the LIST INDEX (EntryList.SelectedOriginalIndex), NOT the
        // editable B6 ItemNumber - mirrors WF `this.AddressList.SelectedIndex`
        // (Copilot v1-B2).
        void UpdateHardCodingWarning()
        {
            try
            {
                int idx = EntryList.SelectedOriginalIndex;
                bool show = idx >= 0
                    && CoreState.AsmMapFileAsmCache?.IsHardCodeItem((uint)idx) == true;
                HardCodingWarningLink.IsVisible = show;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemFE6View.UpdateHardCodingWarning failed: {0}", ex.Message);
                HardCodingWarningLink.IsVisible = false;
            }
        }

        void OnHardCodingLink_Click(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                // Copilot v1-B2: pass the LIST INDEX, not B6.
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0) return;
                string filter = "HARDCODING_ITEM=" + idx.ToString("X2");
                WindowManager.Instance.Navigate<PatchManagerView>(0);
                var pmView = WindowManager.Instance.FindOpen<PatchManagerView>();
                pmView?.JumpTo(filter, 0);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemFE6View.OnHardCodingLink_Click failed: {0}", ex.Message);
            }
        }

        // -- Indirect weapon effect jump (#402) ------------------------------
        // Mirrors WF `JumpToITEMEFFECT_Click` ->
        // ItemWeaponEffectForm.JumpTo(AddressList.SelectedIndex). The WF
        // receiver SCANS the table for the row whose B0 equals the SELECTED
        // LIST INDEX (NOT the editable B6 ItemNumber, per Copilot v1-B2).
        void JumpToWeaponEffect_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0) return;
                uint addr = FindWeaponEffectAddrForItem((uint)idx);
                if (addr != 0)
                    WindowManager.Instance.Navigate<ItemWeaponEffectViewerView>(addr);
                else
                    WindowManager.Instance.Navigate<ItemWeaponEffectViewerView>(0);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemFE6View.JumpToWeaponEffect_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Locate the indirect-weapon-effect table row whose B0 (item id)
        /// equals <paramref name="itemId"/>. Returns 0 when the item has no
        /// entry or the ROM is unavailable. Mirrors WF
        /// `ItemWeaponEffectForm.JumpTo(uint search_item_id)` exactly.
        /// </summary>
        public static uint FindWeaponEffectAddrForItem(uint itemId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint ptr = rom.RomInfo.item_effect_pointer;
            if (ptr == 0) return 0;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;

            // Walk the indirect-effect table looking for B0 == itemId.
            // Termination logic mirrors ItemWeaponEffectViewerViewModel.
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * 16;
                if (addr + 15 >= (uint)rom.Data.Length) break;
                if (rom.u16(addr) == 0xFFFF) break;
                if (i > 10 && rom.u32(addr) == 0 && rom.u32(addr + 4) == 0
                    && rom.u32(addr + 8) == 0 && rom.u32(addr + 12) == 0) break;
                if (rom.u8(addr) == itemId)
                    return addr;
            }
            return 0;
        }

        void JumpToStatBonuses_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Copilot bot review #576: check rom is non-null and pass
                // to isSafetyOffset(addr, rom) to avoid NRE when the view
                // is opened without a ROM loaded.
                var rom = CoreState.ROM;
                if (rom == null) return;
                uint ptr = ParseHexText(StatBonusesPtrBox.Text);
                if (!U.isPointer(ptr)) return;
                uint addr = ptr - 0x08000000;
                if (!U.isSafetyOffset(addr, rom)) return;
                WindowManager.Instance.Navigate<ItemStatBonusesViewerView>(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("JumpToStatBonuses failed: {0}", ex.Message);
            }
        }

        void JumpToEffectiveness_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Copilot bot review #576: check rom is non-null and pass
                // to isSafetyOffset(addr, rom) to avoid NRE when the view
                // is opened without a ROM loaded.
                var rom = CoreState.ROM;
                if (rom == null) return;
                uint ptr = ParseHexText(EffectivenessPtrBox.Text);
                if (!U.isPointer(ptr)) return;
                uint addr = ptr - 0x08000000;
                if (!U.isSafetyOffset(addr, rom)) return;
                WindowManager.Instance.Navigate<ItemEffectivenessViewerView>(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("JumpToEffectiveness failed: {0}", ex.Message);
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // Read UI values into the ViewModel.
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.DescId = (uint)(DescIdBox.Value ?? 0);
            _vm.UseDescId = (uint)(UseDescIdBox.Value ?? 0);
            _vm.ItemNumber = (uint)(ItemNumberBox.Value ?? 0);

            // Weapon type from combo.
            int wtIdx = WeaponTypeCombo.SelectedIndex;
            _vm.WeaponType = wtIdx >= 0 && wtIdx < _weaponTypeList.Count ? _weaponTypeList[wtIdx].id : 0;

            // Trait flags from BitFlagPanel + raw NumericUpDown.
            _vm.Trait1 = Trait1Flags.Value;
            _vm.Trait2 = Trait2Flags.Value;
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

                // Copilot CLI review #576 finding 2: recompute derived
                // fields after the write so Buy/Sell/Shingeki labels,
                // null-pointer warnings, stat-bonus preview, and
                // effective-class list reflect the new Uses / Price /
                // P12 / P16 values WITHOUT requiring the user to
                // reselect the item.
                _vm.RecalcShopPrices();
                _vm.RecalcAllocFlags();
                _vm.RecalcStatBonuses();
                _vm.UpdateEffectiveClassList();
                UpdateComputedUI();

                _vm.MarkClean();
                CoreState.Services.ShowInfo("Item data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("Write failed: {0}", ex.Message);
            }
        }

        // #831: new-alloc the StatBooster (P12) block — mirrors WF
        // L_12_NEWALLOC_ITEMSTATBOOSTER (InputFormRef.AllocEvent). One undo
        // scope covers the block-write + the pointer-write; refresh the box +
        // warning rows on success.
        void AllocStatBonuses_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("New-alloc Stat Bonuses (FE6)");
            try
            {
                bool ok = _vm.AllocStatBonuses(_undoService.GetActiveUndoData());
                if (ok)
                {
                    _undoService.Commit();
                    StatBonusesPtrBox.Text = $"0x{_vm.StatBonusesPtr:X08}";
                    UpdateComputedUI();
                }
                else
                {
                    _undoService.Rollback();
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("AllocStatBonuses failed: {0}", ex.Message);
            }
        }

        // #831: new-alloc the Effectiveness (P16) block — mirrors WF
        // L_16_NEWALLOC_EFFECTIVENESS. FE6 (version 6) never has the
        // SkillSystems class-type rework, so the rework flag is false here.
        void AllocEffectiveness_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("New-alloc Effectiveness (FE6)");
            try
            {
                bool rework = PatchDetectionService.Instance.SkillSystemsClassTypeRework;
                bool ok = _vm.AllocEffectiveness(rework, _undoService.GetActiveUndoData());
                if (ok)
                {
                    _undoService.Commit();
                    EffectivenessPtrBox.Text = $"0x{_vm.EffectivenessPtr:X08}";
                    UpdateComputedUI();
                }
                else
                {
                    _undoService.Rollback();
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("AllocEffectiveness failed: {0}", ex.Message);
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
