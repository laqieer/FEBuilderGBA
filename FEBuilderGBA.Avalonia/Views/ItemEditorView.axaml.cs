using global::Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEditorView : TranslatedUserControl, IEmbeddableEditor, IPickableEditor, IDataVerifiableView
    {
        readonly ItemEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        List<(uint id, string name)> _weaponTypeList = new();

        public string ViewTitle => R._("Item Editor");
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Item Editor", 1408, 856, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public event Action<PickResult>? SelectionConfirmed;

        public ItemEditorView()
        {
            InitializeComponent();
            ItemList.SelectedAddressChanged += OnItemSelected;
            ItemList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);

            // Set trait flag names
            Trait1Flags.SetBitNames(AbilityFlagNames.ItemTrait1);
            Trait2Flags.SetBitNames(AbilityFlagNames.ItemTrait2);

            // Wire desc text live updates
            DescIdBox.ValueChanged += OnDescIdChanged;
            UseDescIdBox.ValueChanged += OnUseDescIdChanged;

            // Re-apply patch-aware label renames when the UI language
            // changes — TranslatedWindow's helper re-scans the AXAML, which
            // would otherwise reset Unk33Label.Text back to the original
            // "Unk33 (B33):" literal (Copilot bot review on PR #569).
            CoreState.LanguageChanged += UpdateWeaponDebuffsLink;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            CoreState.LanguageChanged -= UpdateWeaponDebuffsLink;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        void LoadList()
        {
            try
            {
                // Populate combo dropdowns BEFORE SetItems (fixes #52).
                _weaponTypeList = ComboResourceHelper.MakeWeaponTypeList();
                WeaponTypeCombo.ItemsSource = _weaponTypeList.Select(x => x.name).ToList();

                // Show "Edit Skill Config" button if a skill system is installed
                EditSkillConfigButton.IsVisible = PatchDetectionService.Instance.HasSkillSystem;

                var items = _vm.LoadItemList();
                ItemList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEditorView.LoadList failed: {0}", ex.Message);
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

        // -- Hyperlink label click handlers (#318) --

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

        void OnItemSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItem(addr);
                UpdateUI();
                TryShowListPreview();
                UpdateWarnings();
                UpdateHardCodingWarning();
                UpdateWeaponDebuffsLink();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemEditorView.OnItemSelected failed: {0}", ex.Message);
            }
        }

        // -- HardCoding warning (#409) ---------------------------------------
        // Mirrors WF `HardCodingWarningLabel_Click` + `CheckHardCodingWarning`.
        // The label visibility is driven by Core's IAsmMapCache.IsHardCodeItem
        // seam; in heads without ASM-map data wired the default returns false
        // so the label stays hidden (graceful degradation).
        void UpdateHardCodingWarning()
        {
            try
            {
                uint id = (uint)(ItemNumberBox.Value ?? 0);
                bool show = CoreState.AsmMapFileAsmCache?.IsHardCodeItem(id) ?? false;
                HardCodingWarningLink.IsVisible = show;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEditorView.UpdateHardCodingWarning failed: {0}", ex.Message);
                HardCodingWarningLink.IsVisible = false;
            }
        }

        void OnHardCodingLink_Click(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                uint id = (uint)(ItemNumberBox.Value ?? 0);
                // WF passes the selected list index (not the item id); WF also
                // formats the filter as `HARDCODING_ITEM=NN` in hex. Mirror
                // exactly so the receiving PatchManager filter matches.
                string filter = "HARDCODING_ITEM=" + id.ToString("X2");
                WindowManager.Instance.Navigate<PatchManagerView>(0);
                var pmView = WindowManager.Instance.FindOpen<PatchManagerView>();
                pmView?.JumpTo(filter, 0);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEditorView.OnHardCodingLink_Click failed: {0}", ex.Message);
            }
        }

        // -- Indirect weapon effect jump (#409) ------------------------------
        // Mirrors WF `JumpToITEMEFFECT_Click` → ItemWeaponEffectForm.JumpTo(itemId).
        // The WF receiver SCANS the table for the row whose B0 equals the
        // selected item id; the table is NOT base + itemId * 16. We mirror the
        // same scan here so the navigation lands on the correct row.
        void JumpToWeaponEffect_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint id = (uint)(ItemNumberBox.Value ?? 0);
                uint addr = FindWeaponEffectAddrForItem(id);
                // Open the editor regardless of whether a row exists. When the
                // item has no entry (addr == 0) we navigate to the table base
                // so the user can scroll/inspect — same UX as WF where the
                // receiver opens with no row selected.
                if (addr != 0)
                    WindowManager.Instance.Navigate<ItemWeaponEffectViewerView>(addr);
                else
                    WindowManager.Instance.Navigate<ItemWeaponEffectViewerView>(0);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEditorView.JumpToWeaponEffect_Click failed: {0}", ex.Message);
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

            // WF uses InputFormRef.DataCount which lambdas-check for the end
            // sentinel; mirror with the same termination logic used in
            // ItemWeaponEffectViewerViewModel.LoadItemWeaponEffectList:
            //   - addr+15 outside ROM
            //   - u16(addr) == 0xFFFF
            //   - i > 10 && four-dword run of zeros
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

        // -- Debuff Table jump (#409) ----------------------------------------
        // Mirrors WF `J_33_Click`. Visible only when a SkillSystem patch is
        // installed; opens Patch Manager filtered on the WeaponDebuffsTable
        // definition.
        void UpdateWeaponDebuffsLink()
        {
            try
            {
                bool show = PatchDetectionService.Instance.HasSkillSystem
                    && PatchDetectionService.Instance.SkillSystem == PatchDetectionService.SkillSystemType.SkillSystem;
                WeaponDebuffsLink.IsVisible = show;
                // WF also renames the field label to "Debuff" when the patch
                // is present. Apply the same rename so the Avalonia UI gives
                // the same context the WF user gets. Both labels go through
                // R._(...) so the ja/zh translations apply (Copilot bot
                // review on PR #569: runtime assignments need to route
                // through the translation table, not just the AXAML scan).
                Unk33Label.Text = show ? R._("Debuff (B33):") : R._("Unk33 (B33):");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEditorView.UpdateWeaponDebuffsLink failed: {0}", ex.Message);
                WeaponDebuffsLink.IsVisible = false;
            }
        }

        void OnWeaponDebuffsLink_Click(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                WindowManager.Instance.Navigate<PatchManagerView>(0);
                var pmView = WindowManager.Instance.FindOpen<PatchManagerView>();
                pmView?.JumpTo("defWeaponDebuffsTable", 0);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEditorView.OnWeaponDebuffsLink_Click failed: {0}", ex.Message);
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
            DescTextLabel.Text = _vm.DescText;
            UseDescIdBox.Value = _vm.UseDescId;
            UseDescTextLabel.Text = _vm.UseDescText;

            // Identity
            ItemNumberBox.Value = _vm.ItemNumber;

            // Weapon type combo
            int wtIdx = _weaponTypeList.FindIndex(x => x.id == _vm.WeaponType);
            WeaponTypeCombo.SelectedIndex = wtIdx >= 0 ? wtIdx : (int)_vm.WeaponType;

            // Trait flags (BitFlagPanel) + hex preview labels (#409 mirrors
            // WF "特性1/2/3/4" plus the raw-byte readout).
            Trait1Flags.Value = (byte)_vm.Trait1;
            Trait2Flags.Value = (byte)_vm.Trait2;
            Trait3Flags.Value = (byte)_vm.Trait3;
            Trait4Flags.Value = (byte)_vm.Trait4;
            Trait1HexLabel.Text = $"= 0x{_vm.Trait1:X02}";
            Trait2HexLabel.Text = $"= 0x{_vm.Trait2:X02}";
            Trait3HexLabel.Text = $"= 0x{_vm.Trait3:X02}";
            Trait4HexLabel.Text = $"= 0x{_vm.Trait4:X02}";

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

            // Null pointer warnings + new-alloc buttons (#831). The whole row
            // (warning label + New-alloc button) shows only when the field
            // pointer is 0 and the item index > 0, matching the WF visibility
            // gate (UpdateStateByAllocEvent).
            AllocStatBonusesRow.IsVisible = _vm.ShowAllocStatBonuses;
            AllocEffectivenessRow.IsVisible = _vm.ShowAllocEffectiveness;

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
                // MagicSplit magic bonus (WF MagicExtUnitBase) — shown only on
                // FE7U/FE8U MagicSplit ROMs; hidden on vanilla/FE8N/FE6.
                BonusMagLabel.Text = $"Mag: {_vm.BonusMag:+#;-#;0}";
                BonusMagLabel.IsVisible = _vm.HasMagicBonus;
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

            // #1132: in decomp mode, structured-table edits are source-backed. Route
            // the "items" table to the C-source writer instead of the preview ROM.
            // The classic (!IsDecompMode) ROM-write path below is byte-for-byte unchanged.
            if (CoreState.IsDecompMode)
            {
                if (TryWriteItemSource())
                    return;
                // No owner at all for the items table → genuinely ROM-only. Do NOT
                // silently write the preview ROM; tell the user, then stop.
                CoreState.Services.ShowInfo(R._("This item is ROM-only in decomp mode. Edit the source manually and rebuild."));
                return;
            }

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
                Log.ErrorF("Write failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #1132: attempt a source-backed write of the current item. Returns true when
        /// the items table HAS a source owner (so the write was attempted and an
        /// accurate, status-specific message was shown — success, no-change, romOnly,
        /// manual, json, or an error). Returns false ONLY when there is no owner at all,
        /// so the caller shows the generic ROM-only notice (never a silent ROM write).
        /// </summary>
        bool TryWriteItemSource()
        {
            var project = CoreState.DecompProject;
            var owner = project?.TryGetTableOwner("items");
            if (owner == null)
                return false;   // genuinely no owner → caller shows generic ROM-only

            // Intersect the candidate field dict with the owner's declared fields so a
            // field the manifest doesn't declare is dropped (no UnsupportedField error).
            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (owner.Fields != null)
                foreach (var f in owner.Fields)
                    if (f != null && !string.IsNullOrEmpty(f.Name))
                        declared.Add(f.Name);

            var changed = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (var kv in _vm.BuildSourceFieldDict())
                if (declared.Contains(kv.Key))
                    changed[kv.Key] = kv.Value;

            // Always call the writer and branch on its typed status so the user sees an
            // ACCURATE message (the writer returns the right message for romOnly /
            // manual / json / not-owned, rather than a generic "ROM-only" string).
            var res = DecompSourceWriterCore.WriteTableEntry(
                project, "items", _vm.CurrentItemIndex, changed);

            switch (res.Status)
            {
                case DecompSourceWriteStatus.Ok:
                    _vm.MarkClean();
                    // Re-baseline the dirty snapshot to the just-written values so an
                    // immediate re-Save is a no-op (and never re-clobbers from stale ROM).
                    _vm.RefreshSourceFieldSnapshot();
                    UpdateWarnings();
                    // ChangedFields empty ⇒ a no-op (value already matched) — don't
                    // claim a rebuild is needed.
                    if (res.ChangedFields != null && res.ChangedFields.Count > 0)
                        CoreState.Services.ShowInfo(R._("Item source updated. Project needs rebuild."));
                    else
                        CoreState.Services.ShowInfo(R._("No change needed — the source already matches."));
                    break;
                case DecompSourceWriteStatus.RomOnly:
                    CoreState.Services.ShowInfo(R._("This item table is ROM-only in decomp mode."));
                    break;
                case DecompSourceWriteStatus.Manual:
                    // Covers writePolicy=manual AND format=json (the writer returns the
                    // accurate per-case message — use it verbatim).
                    CoreState.Services.ShowInfo(res.Message);
                    break;
                default:
                    CoreState.Services.ShowError(res.Message);
                    break;
            }
            return true;
        }

        // #831: new-alloc the StatBooster (P12) block — mirrors WF
        // L_12_NEWALLOC_ITEMSTATBOOSTER (InputFormRef.AllocEvent). Opens one
        // undo scope (UndoService.Begin → ROM.BeginUndoScope) so the block-write
        // + the pointer-write commit/rollback as a single transaction, then
        // refreshes the pointer box + the warning rows.
        void AllocStatBonuses_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin(R._("New-alloc Stat Bonuses"));
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
        // L_16_NEWALLOC_EFFECTIVENESS. The patch-conditional template variant is
        // selected via PatchDetectionService.SkillSystemsClassTypeRework
        // (== WF PatchUtil.SearchClassType() == SkillSystems_Rework).
        void AllocEffectiveness_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin(R._("New-alloc Effectiveness"));
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
                Log.ErrorF("JumpToStatBonuses failed: {0}", ex.Message);
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
                Log.ErrorF("JumpToEffectiveness failed: {0}", ex.Message);
            }
        }

        void UpdateWarnings()
        {
            var warnings = _vm.ValidateItem();
            WarningsBorder.IsVisible = warnings.Count > 0;
            WarningsList.ItemsSource = warnings;
        }

        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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
                Log.ErrorF("EditSkillConfig_Click failed: {0}", ex.Message);
            }
        }

        async void ExportTSV_Click(object? sender, RoutedEventArgs e)
        {
            await TableExportImportHelper.ExportTableAsync(TopLevel.GetTopLevel(this) as Window, "items");
        }

        async void ImportTSV_Click(object? sender, RoutedEventArgs e)
        {
            await TableExportImportHelper.ImportTableAsync(TopLevel.GetTopLevel(this) as Window, "items", _undoService, () =>
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
