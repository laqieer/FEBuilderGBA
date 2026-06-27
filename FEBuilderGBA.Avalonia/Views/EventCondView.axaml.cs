using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventCondView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventCondViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressSlotChange;
        bool _suppressRecordChange;

        public string ViewTitle => "Event Condition Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public EventCondView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnMapSelected;
            SlotCombo.SelectionChanged += OnSlotChanged;
            RecordList.SelectionChanged += OnRecordSelected;
            Opened += (_, _) => LoadAll();

            // Initialize all NUD controls to 0 so UIVERIFY doesn't flag hidden
            // ones as empty. FE7-extended fields (ExtraB12-B15) are hidden on
            // FE6/FE8 and never set by UpdateEditorUI, leaving them null.
            CondTypeBox.Value ??= 0;
            SubTypeBox.Value ??= 0;
            FlagIdBox.Value ??= 0;
            EventPtrBox.Value ??= 0;
            ExtraB8Box.Value ??= 0;
            ExtraB9Box.Value ??= 0;
            ExtraB10Box.Value ??= 0;
            ExtraB11Box.Value ??= 0;
            ExtraB12Box.Value ??= 0;
            ExtraB13Box.Value ??= 0;
            ExtraB14Box.Value ??= 0;
            ExtraB15Box.Value ??= 0;

            // Category-specific NUDs: initialise to 0 to satisfy UIVERIFY when
            // their parent panels are hidden.
            TurnStartBox.Value ??= 0;
            TurnEndBox.Value ??= 0;
            // #950 T4: Unit1Box/Unit2Box are now IdFieldControls (Value is a
            // non-nullable uint defaulting to 0) — no null-seed needed.
            AdditionalDecisionBox.Value ??= 0;
            DecisionFlagBox.Value ??= 0;
            TalkAsmFuncBox.Value ??= 0;
            ObjectXBox.Value ??= 0;
            ObjectYBox.Value ??= 0;
            EventTypeBox.Value ??= 0;
            // #950 T4: ChestItemBox is now an IdFieldControl (uint Value) — no null-seed.
            GoldBox.Value ??= 0;
            DurabilityBox.Value ??= 0;
            ShopTypeBox.Value ??= 0;
            ItemListBox.Value ??= 0;
            RangeStartXBox.Value ??= 0;
            RangeStartYBox.Value ??= 0;
            RangeEndXBox.Value ??= 0;
            RangeEndYBox.Value ??= 0;
            AsmFuncBox.Value ??= 0;
            TrapXBox.Value ??= 0;
            TrapYBox.Value ??= 0;
            BallistaTypeBox.Value ??= 0;
            TrapDirectionBox.Value ??= 0;
            TrapDurabilityBox.Value ??= 0;
            DamageAmountBox.Value ??= 0;
            GasDirectionBox.Value ??= 0;
            DurationBox.Value ??= 0;
            HatchingStartBox.Value ??= 0;
            HatchingEndBox.Value ??= 0;
            VeinEffectIdBox.Value ??= 0;
            InitialTimerBox.Value ??= 0;
            RepeatTimerBox.Value ??= 0;
            // #957 W1a: TextIdBox is now an IdFieldControl (uint Value) — no null-seed needed.
        }

        void LoadAll()
        {
            _vm.IsLoading = true;
            try
            {
                // Ensure slot defs are loaded
                if (EventCondViewModel.SlotDefs.Count == 0)
                    EventCondViewModel.LoadSlotDefs();

                // Populate slot combo
                _suppressSlotChange = true;
                SlotCombo.Items.Clear();
                foreach (var def in EventCondViewModel.SlotDefs)
                {
                    SlotCombo.Items.Add($"{def.Category}: {def.Name}");
                }
                if (SlotCombo.Items.Count > 0)
                    SlotCombo.SelectedIndex = 0;
                _suppressSlotChange = false;

                // Populate map list
                var maps = _vm.LoadMapList();
                EntryList.SetItems(maps);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventCondView.LoadAll failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnMapSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                bool ok = _vm.ResolveEventDataAddr(addr);
                if (ok)
                {
                    ReloadRecordList();
                    UpdateTopBar();
                }
                else
                {
                    ClearRecordList();
                    ClearEditor();
                    SlotInfoLabel.Text = "No event data for this map.";
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventCondView.OnMapSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateTopBar()
        {
            TopAddrBox.Text = $"0x{_vm.EventDataAddr:X08}";
            ReadCountBox.Value = EventCondViewModel.SlotDefs.Count;
        }

        void OnSlotChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressSlotChange) return;
            _vm.IsLoading = true;
            try
            {
                ReloadRecordList();
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventCondView.OnSlotChanged failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void ReloadRecordList()
        {
            int slotIndex = SlotCombo.SelectedIndex;
            if (slotIndex < 0 || _vm.EventDataAddr == 0)
            {
                ClearRecordList();
                ClearEditor();
                return;
            }

            var records = _vm.LoadConditionRecords(slotIndex);
            _suppressRecordChange = true;
            RecordList.Items.Clear();
            foreach (var r in records)
            {
                var item = new ListBoxItem { Content = r.name, Tag = r.addr };
                RecordList.Items.Add(item);
            }
            _suppressRecordChange = false;

            SlotInfoLabel.Text = _vm.SlotInfo;

            if (RecordList.Items.Count > 0)
            {
                RecordList.SelectedIndex = 0;
            }
            else
            {
                ClearEditor();
            }
        }

        void ClearRecordList()
        {
            _suppressRecordChange = true;
            RecordList.Items.Clear();
            _suppressRecordChange = false;
        }

        void OnRecordSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressRecordChange) return;
            _vm.IsLoading = true;
            try
            {
                if (RecordList.SelectedItem is ListBoxItem item && item.Tag is uint addr)
                {
                    _vm.LoadCondRecord(addr);
                    UpdateEditorUI();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventCondView.OnRecordSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateEditorUI()
        {
            AddrLabel.Text = $"0x{_vm.CondRecordAddr:X08}";
            // Display the EFFECTIVE per-record stride so FE7 TURN type-1 rows
            // show "12 bytes" not "16 bytes" (Copilot round 7 #2).
            RecordSizeLabel.Text = $"{_vm.EffectiveRecordSize} bytes";
            CondTypeNameLabel.Text = _vm.CondTypeName;
            BlockSizeBox.Text = $"0x{_vm.EffectiveRecordSize:X02}";
            SelectedAddrBox.Text = $"0x{_vm.CondRecordAddr:X08}";
            CommentBox.Text = _vm.Comment ?? "";

            // Update field labels
            var labels = _vm.GetFieldLabels();
            LblB0.Text = labels.B0;
            LblB1.Text = labels.B1;
            LblB2B3.Text = labels.B2B3;
            LblB4B7.Text = labels.B4B7;
            LblB8.Text = labels.B8;
            LblB9.Text = labels.B9;
            LblB10.Text = labels.B10;
            LblB11.Text = labels.B11;

            if (_vm.IsPointerSlot)
            {
                // For pointer-only slots, only show the event pointer field
                EventPtrBox.Value = _vm.EventPtr;
                CondTypeBox.Value = 0;
                SubTypeBox.Value = 0;
                FlagIdBox.Value = 0;
                ExtraB8Box.Value = 0;
                ExtraB9Box.Value = 0;
                ExtraB10Box.Value = 0;
                ExtraB11Box.Value = 0;

                SetFieldVisibility(false, false, false, true, false, false, false, false);
                SetFE7FieldsVisible(false);
            }
            else
            {
                CondTypeBox.Value = _vm.CondType;
                SubTypeBox.Value = _vm.SubType;
                FlagIdBox.Value = _vm.FlagId;
                EventPtrBox.Value = _vm.EventPtr;
                ExtraB8Box.Value = _vm.ExtraB8;
                ExtraB9Box.Value = _vm.ExtraB9;
                ExtraB10Box.Value = _vm.ExtraB10;
                ExtraB11Box.Value = _vm.ExtraB11;

                if (_vm.CondRecordSize == 4)
                {
                    // TUTORIAL records: 4 bytes — single u32 (TUTORIAL_P0). Only
                    // the Event Pointer field is meaningful; everything else is
                    // hidden so the user can't accidentally edit type/sub-type
                    // bytes (Copilot CLI review round 2 #2).
                    FlagIdBox.Maximum = 65535;
                    EventPtrBox.Maximum = 4294967295;
                    // Round 6 fix: keep decimal format strings (Avalonia
                    // NumericUpDown throws on hex specifiers with decimal? values).
                    EventPtrBox.FormatString = "0";
                    SetFieldVisibility(false, false, false, true, false, false, false, false);
                }
                else if (_vm.CondRecordSize <= 6)
                {
                    // TRAP: 6-byte records — show type, X(B1), Y(B2/FlagId), subtype(B3/EventPtr), B4, B5
                    FlagIdBox.Maximum = 255;
                    EventPtrBox.Maximum = 255;
                    // Round 6 fix: decimal format strings (no hex specifiers).
                    FlagIdBox.FormatString = "0";
                    EventPtrBox.FormatString = "0";
                    SetFieldVisibility(true, true, true, true, true, true, false, false);
                }
                else
                {
                    FlagIdBox.Maximum = 65535;
                    EventPtrBox.Maximum = 4294967295;
                    // Round 6 fix: decimal format strings (no hex specifiers).
                    FlagIdBox.FormatString = "0";
                    EventPtrBox.FormatString = "0";
                    bool show12 = _vm.CondRecordSize >= 12;
                    SetFieldVisibility(true, true, true, true, show12, show12, show12, show12);
                }

                bool showFE7 = _vm.IsFE7Extended;
                SetFE7FieldsVisible(showFE7);
                if (showFE7)
                {
                    ExtraB12Box.Value = _vm.ExtraB12;
                    ExtraB13Box.Value = _vm.ExtraB13;
                    ExtraB14Box.Value = _vm.ExtraB14;
                    ExtraB15Box.Value = _vm.ExtraB15;
                }
            }

            // Update category-specific sub-panels
            UpdateCategoryPanels();

            // Alloc Event template affordance (#1592): only on NEWALLOC-EVENT
            // surfaces (TURN/TALK/OBJECT/ALWAYS event-pointer records). The
            // counter-reinforcement button is TURN-only.
            AllocTemplatePanel.IsVisible = _vm.CanAllocCallTemplate;
            AllocCounterBtn.IsVisible = _vm.CanAllocCounterReinforcement;

            // Name hints
            UpdateNameHints();

            // Raw hex dump
            UpdateRawHex();
        }

        void UpdateCategoryPanels()
        {
            // Hide all sub-panels first.
            TurnPanel.IsVisible = false;
            TalkPanel.IsVisible = false;
            ObjectPanel.IsVisible = false;
            AlwaysPanel.IsVisible = false;
            TrapPanel.IsVisible = false;
            TutorialPanel.IsVisible = false;

            int slotIdx = _vm.SelectedSlotIndex;
            if (slotIdx < 0 || slotIdx >= EventCondViewModel.SlotDefs.Count) return;

            var cat = EventCondViewModel.SlotDefs[slotIdx].Category;

            // When a category sub-panel is visible, hide the corresponding
            // generic ExtraB8-B11 controls so the user can't edit the same
            // bytes in two places (Copilot bot review on round-3 fixes #6).
            // The category panel is the canonical edit surface for the
            // category-specific bytes.
            bool hideGenericExtras = cat == CondCategory.TURN
                || cat == CondCategory.TALK
                || cat == CondCategory.OBJECT
                || cat == CondCategory.ALWAYS
                || cat == CondCategory.TRAP;
            if (hideGenericExtras)
            {
                ExtraB8Box.IsVisible = false;  LblB8.IsVisible = false;
                ExtraB9Box.IsVisible = false;  LblB9.IsVisible = false;
                ExtraB10Box.IsVisible = false; LblB10.IsVisible = false;
                ExtraB11Box.IsVisible = false; LblB11.IsVisible = false;
            }

            switch (cat)
            {
                case CondCategory.TURN:
                    TurnPanel.IsVisible = true;
                    TurnStartBox.Value = _vm.TurnStart;
                    TurnEndBox.Value = _vm.TurnEnd;
                    PhaseCombo.SelectedIndex = _vm.Phase switch { 0x40 => 1, 0x80 => 2, 0xC0 => 3, _ => 0 };
                    break;
                case CondCategory.TALK:
                    TalkPanel.IsVisible = true;
                    Unit1Box.Value = _vm.Unit1;
                    Unit2Box.Value = _vm.Unit2;
                    // 1-based ROM-stored unit IDs (matches Cond TALK convention).
                    // #950 T4: keep the standalone name labels AND seed the
                    // IdFieldControl inline previews.
                    Unit1NameLabel.Text = NameResolver.GetUnitNameByOneBasedId(_vm.Unit1);
                    Unit2NameLabel.Text = NameResolver.GetUnitNameByOneBasedId(_vm.Unit2);
                    Unit1Box.NameText = Unit1NameLabel.Text;
                    Unit2Box.NameText = Unit2NameLabel.Text;
                    AdditionalDecisionBox.Value = _vm.AdditionalDecision;
                    DecisionFlagBox.Value = _vm.DecisionFlag;
                    TalkAsmFuncBox.Value = _vm.AsmFunc;
                    // Round 9 fix: subtype-aware visibility. TALK subtypes
                    // have different layouts:
                    //   N03 (0x03): Unit 1/2 + AdditionalDecision (W12) + DecisionFlag (W14)
                    //   N04 (0x04): Unit 1/2 + ASM pointer (P12)
                    //   N0D (0x0D, FE6): ASM pointer (P8) — NO Unit fields
                    bool isTalkN0D = _vm.CondType == 0x0D;
                    bool isTalkN04 = _vm.CondType == 0x04;
                    bool isTalkN03 = _vm.CondType == 0x03;
                    // Unit 1/2 hidden for N0D (FE6 ASM Talk has no Unit fields).
                    Unit1Box.IsVisible = !isTalkN0D;
                    Unit2Box.IsVisible = !isTalkN0D;
                    Unit1NameLabel.IsVisible = !isTalkN0D;
                    Unit2NameLabel.IsVisible = !isTalkN0D;
                    // AdditionalDecision/DecisionFlag only for N03.
                    AdditionalDecisionBox.IsVisible = isTalkN03;
                    DecisionFlagBox.IsVisible = isTalkN03;
                    // ASM Function only for N04/N0D (the ASM-talk subtypes).
                    TalkAsmFuncBox.IsVisible = isTalkN04 || isTalkN0D;
                    break;
                case CondCategory.OBJECT:
                    ObjectPanel.IsVisible = true;
                    ObjectSubHeaderLabel.Text = _vm.CondType switch
                    {
                        0x05 => "Seize Point / House (制圧ポイントと民家)",
                        0x06 => "Visit Village (訪問村)",
                        0x07 => "Chest (宝箱)",
                        0x08 => "Door (扉)",
                        0x0A => "Shop (店)",
                        _ => $"Unknown OBJECT type (0x{_vm.CondType:X02})",
                    };
                    ObjectXBox.Value = _vm.X1;
                    ObjectYBox.Value = _vm.Y1;
                    EventTypeBox.Value = _vm.EventType;
                    ChestItemBox.Value = _vm.ItemId;
                    // #950 T4: keep the standalone name label AND seed the
                    // IdFieldControl inline preview.
                    ChestItemNameLabel.Text = NameResolver.GetItemName(_vm.ItemId);
                    ChestItemBox.NameText = ChestItemNameLabel.Text;
                    GoldBox.Value = _vm.Gold;
                    DurabilityBox.Value = _vm.Durability;
                    ShopTypeBox.Value = _vm.ShopType;
                    ItemListBox.Value = _vm.EventPtr;
                    break;
                case CondCategory.ALWAYS:
                    AlwaysPanel.IsVisible = true;
                    RangeStartXBox.Value = _vm.X1;
                    RangeStartYBox.Value = _vm.Y1;
                    RangeEndXBox.Value = _vm.X2;
                    RangeEndYBox.Value = _vm.Y2;
                    AsmFuncBox.Value = _vm.AsmFunc;
                    break;
                case CondCategory.TRAP:
                    TrapPanel.IsVisible = true;
                    TrapSubHeaderLabel.Text = _vm.CondType switch
                    {
                        0x01 => "Ballista Placement (アーチ配置)",
                        0x04 => "Damage Floor (トラップ床)",
                        0x05 => "Poison Gas (毒ガス)",
                        0x06 => "Dragon Vein",
                        0x07 => "Arrow of God (神の矢)",
                        0x08 => "Fire (炎)",
                        0x0B => "Mine (地雷)",
                        0x0C => "Gorgon Egg (ゴーゴンの卵)",
                        _ => $"Unknown TRAP type (0x{_vm.CondType:X02})",
                    };
                    TrapXBox.Value = _vm.X1;
                    TrapYBox.Value = _vm.Y1;
                    // BallistaType / VeinEffect are the B3 sub-type byte
                    // (TrapSubType, not _vm.SubType which holds B1 = X for
                    // 6-byte TRAP records). Copilot CLI review round 3 #2.
                    BallistaTypeBox.Value = _vm.TrapSubType;
                    TrapDirectionBox.Value = _vm.TrapDirection;
                    TrapDurabilityBox.Value = _vm.Durability;
                    DamageAmountBox.Value = _vm.DamageAmount;
                    GasDirectionBox.Value = _vm.GasDirection;
                    DurationBox.Value = _vm.Duration;
                    HatchingStartBox.Value = _vm.HatchingStart;
                    HatchingEndBox.Value = _vm.HatchingEnd;
                    VeinEffectIdBox.Value = _vm.TrapSubType;
                    // Round 8 fix: hide TRAP aliases that aren't active for the
                    // current trap type so the user can't edit a B4/B5 alias
                    // whose value will be silently discarded by Compose.
                    // Active per type:
                    //   0x04 Damage Floor: DamageAmount + Durability
                    //   0x05 Poison Gas:   GasDirection + Durability
                    //   0x06 Dragon Vein:  VeinEffectId (B3 only) + Direction + Durability
                    //   0x08 Fire:         Direction + Duration
                    //   0x0C Gorgon Egg:   HatchingStart + HatchingEnd
                    //   0x0B Mine:         ItemId (B3) + Direction + Durability
                    //   other (Ballista):  BallistaType (B3) + Direction + Durability
                    bool isDmg = _vm.CondType == 0x04;
                    bool isGas = _vm.CondType == 0x05;
                    bool isFire = _vm.CondType == 0x08;
                    bool isEgg = _vm.CondType == 0x0C;
                    bool isVein = _vm.CondType == 0x06;
                    DamageAmountBox.IsVisible = isDmg;
                    GasDirectionBox.IsVisible = isGas;
                    DurationBox.IsVisible = isFire;
                    HatchingStartBox.IsVisible = isEgg;
                    HatchingEndBox.IsVisible = isEgg;
                    // Direction + Durability remain visible for non-alias types.
                    TrapDirectionBox.IsVisible = !isDmg && !isGas && !isEgg;
                    TrapDurabilityBox.IsVisible = !isFire && !isEgg;
                    BallistaTypeBox.IsVisible = !isVein;
                    VeinEffectIdBox.IsVisible = isVein;
                    break;
                case CondCategory.TUTORIAL:
                    // TUTORIAL records are a single 4-byte u32 (TUTORIAL_P0).
                    // The TutorialPanel sub-fields (InitialTimer / RepeatTimer
                    // / TextId) are visual hints only — they do NOT round-trip
                    // because the WF record has no separate timer fields. We
                    // surface the raw u32 as InitialTimer when value==1, and
                    // as the pointer offset when isPointer (purely decorative).
                    TutorialPanel.IsVisible = true;
                    if (_vm.EventPtr == 1)
                    {
                        InitialTimerBox.Value = 1; // canonical "blank" marker
                        RepeatTimerBox.Value = 0;
                        // #957 W1a: IdFieldControl — seed value + inline preview.
                        TextIdBox.Value = 0;
                        TextIdBox.NameText = "";
                    }
                    else
                    {
                        InitialTimerBox.Value = 0;
                        RepeatTimerBox.Value = 0;
                        // #957 W1a: IdFieldControl — seed value + inline preview.
                        TextIdBox.Value = 0;
                        TextIdBox.NameText = "";
                    }
                    break;
            }
        }

        void SetFieldVisibility(bool b0, bool b1, bool b2b3, bool b4b7, bool b8, bool b9, bool b10, bool b11)
        {
            CondTypeBox.IsVisible = b0;     LblB0.IsVisible = b0;
            SubTypeBox.IsVisible = b1;      LblB1.IsVisible = b1;
            FlagIdBox.IsVisible = b2b3;     LblB2B3.IsVisible = b2b3;
            EventPtrBox.IsVisible = b4b7;   LblB4B7.IsVisible = b4b7;
            ExtraB8Box.IsVisible = b8;      LblB8.IsVisible = b8;
            ExtraB9Box.IsVisible = b9;      LblB9.IsVisible = b9;
            ExtraB10Box.IsVisible = b10;    LblB10.IsVisible = b10;
            ExtraB11Box.IsVisible = b11;    LblB11.IsVisible = b11;
        }

        void SetFE7FieldsVisible(bool visible)
        {
            ExtraB12Box.IsVisible = visible; LblB12.IsVisible = visible;
            ExtraB13Box.IsVisible = visible; LblB13.IsVisible = visible;
            ExtraB14Box.IsVisible = visible; LblB14.IsVisible = visible;
            ExtraB15Box.IsVisible = visible; LblB15.IsVisible = visible;

            // Initialize hidden NUDs to 0 so UIVERIFY doesn't flag them as empty.
            // When hidden, their Value is never set by UpdateEditorUI, leaving it null.
            if (!visible)
            {
                ExtraB12Box.Value ??= 0;
                ExtraB13Box.Value ??= 0;
                ExtraB14Box.Value ??= 0;
                ExtraB15Box.Value ??= 0;
            }
        }

        void UpdateNameHints()
        {
            if (_vm.IsPointerSlot)
            {
                uint ptr = _vm.EventPtr;
                if (U.isPointer(ptr))
                    NameHintLabel.Text = $"Points to: 0x{U.toOffset(ptr):X06}";
                else if (ptr == 0)
                    NameHintLabel.Text = "(null pointer)";
                else
                    NameHintLabel.Text = $"Raw value: 0x{ptr:X08}";
                return;
            }

            int slotIdx = _vm.SelectedSlotIndex;
            if (slotIdx < 0 || slotIdx >= EventCondViewModel.SlotDefs.Count)
            {
                NameHintLabel.Text = "";
                return;
            }

            var cat = EventCondViewModel.SlotDefs[slotIdx].Category;
            var hints = new System.Text.StringBuilder();

            if (cat == CondCategory.TALK)
            {
                // 1-based ROM-stored unit IDs.
                string u1 = NameResolver.GetUnitNameByOneBasedId(_vm.ExtraB8);
                string u2 = NameResolver.GetUnitNameByOneBasedId(_vm.ExtraB9);
                hints.AppendLine($"Unit 1: {u1}");
                hints.AppendLine($"Unit 2: {u2}");
            }
            else if (cat == CondCategory.TURN)
            {
                uint phase = _vm.ExtraB10;
                string phaseName = phase switch
                {
                    0x00 => "Player Phase",
                    0x40 => "Ally Phase",
                    0x80 => "Enemy Phase",
                    0xC0 => "4th Allegiance Phase",
                    _ => $"Phase 0x{phase:X02}",
                };
                hints.AppendLine($"Turn {_vm.ExtraB8} - {_vm.ExtraB9}, {phaseName}");
            }
            else if (cat == CondCategory.OBJECT)
            {
                hints.AppendLine($"Position: ({_vm.ExtraB8}, {_vm.ExtraB9})");
                hints.AppendLine($"Object sub-type: 0x{_vm.ExtraB10:X02}");
            }

            if (U.isPointer(_vm.EventPtr))
                hints.AppendLine($"Event at: 0x{U.toOffset(_vm.EventPtr):X06}");

            NameHintLabel.Text = hints.ToString().TrimEnd();
        }

        void UpdateRawHex()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _vm.CondRecordAddr == 0)
            {
                RawHexLabel.Text = "";
                return;
            }

            uint addr = _vm.CondRecordAddr;
            // Use EffectiveRecordSize so FE7 TURN type-1 rows display only
            // their actual 12-byte stride, not 16 (Copilot round 7 #2).
            uint size = _vm.IsPointerSlot ? 4 : _vm.EffectiveRecordSize;
            if (addr + size > (uint)rom.Data.Length)
            {
                RawHexLabel.Text = "(out of range)";
                return;
            }

            var sb = new System.Text.StringBuilder();
            for (uint i = 0; i < size; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(rom.u8(addr + i).ToString("X02"));
            }
            RawHexLabel.Text = sb.ToString();
        }

        void ClearEditor()
        {
            _vm.CanWrite = false;
            AddrLabel.Text = "";
            RecordSizeLabel.Text = "";
            CondTypeNameLabel.Text = "";
            NameHintLabel.Text = "";
            RawHexLabel.Text = "";
            BlockSizeBox.Text = "";
            SelectedAddrBox.Text = "";
            CommentBox.Text = "";
            TurnPanel.IsVisible = false;
            TalkPanel.IsVisible = false;
            ObjectPanel.IsVisible = false;
            AlwaysPanel.IsVisible = false;
            TrapPanel.IsVisible = false;
            TutorialPanel.IsVisible = false;
            AllocTemplatePanel.IsVisible = false;
            AllocCounterBtn.IsVisible = false;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _undoService.Begin("Edit Event Condition");
            try
            {
                if (_vm.IsPointerSlot)
                {
                    _vm.EventPtr = (uint)(EventPtrBox.Value ?? 0);
                }
                else
                {
                    _vm.CondType = (uint)(CondTypeBox.Value ?? 0);
                    _vm.SubType = (uint)(SubTypeBox.Value ?? 0);
                    _vm.FlagId = (uint)(FlagIdBox.Value ?? 0);
                    _vm.EventPtr = (uint)(EventPtrBox.Value ?? 0);
                    _vm.ExtraB8 = (uint)(ExtraB8Box.Value ?? 0);
                    _vm.ExtraB9 = (uint)(ExtraB9Box.Value ?? 0);
                    _vm.ExtraB10 = (uint)(ExtraB10Box.Value ?? 0);
                    _vm.ExtraB11 = (uint)(ExtraB11Box.Value ?? 0);

                    if (_vm.IsFE7Extended)
                    {
                        _vm.ExtraB12 = (uint)(ExtraB12Box.Value ?? 0);
                        _vm.ExtraB13 = (uint)(ExtraB13Box.Value ?? 0);
                        _vm.ExtraB14 = (uint)(ExtraB14Box.Value ?? 0);
                        _vm.ExtraB15 = (uint)(ExtraB15Box.Value ?? 0);
                    }

                    // Also flush category-specific composite values into the VM
                    // properties so ComposeCategoryFields() picks them up.
                    UpdateVmCategoryProperties();
                }

                // Comment round-trip (also requires undo scope).
                _vm.UpdateComment(CommentBox.Text ?? "");

                _vm.WriteCondRecord();
                _undoService.Commit();
                _vm.MarkClean();

                // Refresh display
                UpdateRawHex();
                UpdateNameHints();
                CondTypeNameLabel.Text = EventCondViewModel.GetCondTypeName((byte)_vm.CondType);

                CoreState.Services.ShowInfo(R._("Event condition data written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EventCondView.Write_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Pull category-specific values from the visible sub-panel NUDs into the
        /// VM properties. Called before WriteCondRecord so ComposeCategoryFields
        /// has the latest user edits.
        /// </summary>
        void UpdateVmCategoryProperties()
        {
            int slotIdx = _vm.SelectedSlotIndex;
            if (slotIdx < 0 || slotIdx >= EventCondViewModel.SlotDefs.Count) return;

            var cat = EventCondViewModel.SlotDefs[slotIdx].Category;
            switch (cat)
            {
                case CondCategory.TURN:
                    _vm.TurnStart = (uint)(TurnStartBox.Value ?? 0);
                    _vm.TurnEnd = (uint)(TurnEndBox.Value ?? 0);
                    _vm.Phase = PhaseCombo.SelectedIndex switch { 1 => 0x40, 2 => 0x80, 3 => 0xC0, _ => 0u };
                    break;
                case CondCategory.TALK:
                    // Round 9 fix: only copy back values for VISIBLE controls.
                    // Hidden controls are not part of the current TALK subtype's
                    // layout, and copying them would leak stale state into bytes
                    // the Compose path doesn't touch.
                    // #950 T4: Unit1Box/Unit2Box IdFieldControl.Value is uint.
                    if (Unit1Box.IsVisible) _vm.Unit1 = Unit1Box.Value;
                    if (Unit2Box.IsVisible) _vm.Unit2 = Unit2Box.Value;
                    if (AdditionalDecisionBox.IsVisible)
                        _vm.AdditionalDecision = (uint)(AdditionalDecisionBox.Value ?? 0);
                    if (DecisionFlagBox.IsVisible)
                        _vm.DecisionFlag = (uint)(DecisionFlagBox.Value ?? 0);
                    if (TalkAsmFuncBox.IsVisible)
                        _vm.AsmFunc = (uint)(TalkAsmFuncBox.Value ?? 0);
                    break;
                case CondCategory.OBJECT:
                    _vm.X1 = (uint)(ObjectXBox.Value ?? 0);
                    _vm.Y1 = (uint)(ObjectYBox.Value ?? 0);
                    _vm.EventType = (uint)(EventTypeBox.Value ?? 0);
                    // #950 T4: ChestItemBox IdFieldControl.Value is uint.
                    _vm.ItemId = ChestItemBox.Value;
                    _vm.Gold = (uint)(GoldBox.Value ?? 0);
                    _vm.Durability = (uint)(DurabilityBox.Value ?? 0);
                    _vm.ShopType = (uint)(ShopTypeBox.Value ?? 0);
                    // OBJECT N0A Shop: ItemList u32 maps to _vm.EventPtr (B4-B7
                    // of the record). Round-trip the displayed ItemList value
                    // back so shop edits are persisted (Copilot CLI review
                    // round 3 #3). Only relevant when CondType == 0x0A; for
                    // other OBJECT types the generic EventPtrBox handles the
                    // pointer/chest packing.
                    if (_vm.CondType == 0x0A)
                    {
                        _vm.EventPtr = (uint)(ItemListBox.Value ?? 0);
                    }
                    break;
                case CondCategory.ALWAYS:
                    _vm.X1 = (uint)(RangeStartXBox.Value ?? 0);
                    _vm.Y1 = (uint)(RangeStartYBox.Value ?? 0);
                    _vm.X2 = (uint)(RangeEndXBox.Value ?? 0);
                    _vm.Y2 = (uint)(RangeEndYBox.Value ?? 0);
                    _vm.AsmFunc = (uint)(AsmFuncBox.Value ?? 0);
                    break;
                case CondCategory.TRAP:
                    _vm.X1 = (uint)(TrapXBox.Value ?? 0);
                    _vm.Y1 = (uint)(TrapYBox.Value ?? 0);
                    // BallistaType / VeinEffect both bind to TrapSubType (B3 byte),
                    // but only one is active at a time depending on TRAP type:
                    //   0x06 (DragonVein) — VeinEffectId is the user's source.
                    //   0x01 (Ballista) and others — BallistaType is the source.
                    // Round 6 fix: select source by CondType so edits to the
                    // visible control round-trip to B3 instead of being silently
                    // overwritten by the other (stale) control's value.
                    _vm.TrapSubType = _vm.CondType == 0x06
                        ? (uint)(VeinEffectIdBox.Value ?? 0)
                        : (uint)(BallistaTypeBox.Value ?? 0);
                    _vm.TrapDirection = (uint)(TrapDirectionBox.Value ?? 0);
                    _vm.Durability = (uint)(TrapDurabilityBox.Value ?? 0);
                    _vm.DamageAmount = (uint)(DamageAmountBox.Value ?? 0);
                    _vm.GasDirection = (uint)(GasDirectionBox.Value ?? 0);
                    _vm.Duration = (uint)(DurationBox.Value ?? 0);
                    _vm.HatchingStart = (uint)(HatchingStartBox.Value ?? 0);
                    _vm.HatchingEnd = (uint)(HatchingEndBox.Value ?? 0);
                    break;
                case CondCategory.TUTORIAL:
                    // TUTORIAL is a 4-byte u32 record. Only EventPtr is
                    // meaningful (already pulled from EventPtrBox above).
                    // The tutorial sub-panel InitialTimer/RepeatTimer/TextId
                    // boxes are visual hints only — they do NOT round-trip
                    // to the ROM record because the WF TUTORIAL_P0 is a
                    // single u32, not byte fields.
                    break;
            }
        }

        void ExpandList_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Expand Event Condition List");
            try
            {
                uint newPtr = _vm.ExpandRecordList();
                _undoService.Commit();
                _vm.MarkClean();

                if (newPtr != 0)
                {
                    ReloadRecordList();
                    CoreState.Services.ShowInfo(R._("Record list expanded. New base pointer: 0x{0}", newPtr.ToString("X08")));
                }
                else
                {
                    CoreState.Services.ShowInfo(R._("Cannot expand this slot type (pointer-only or no allocator wired)."));
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EventCondView.ExpandList_Click failed: {0}", ex.Message);
            }
        }

        void NewAlloc_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Allocate New Event");
            try
            {
                uint newPtr = _vm.AllocateNewEvent();
                _undoService.Commit();
                _vm.MarkClean();

                if (newPtr != 0)
                {
                    EventPtrBox.Value = newPtr;
                    _vm.EventPtr = newPtr;
                    CoreState.Services.ShowInfo(R._("New event allocated at: 0x{0}", newPtr.ToString("X08")));
                }
                else
                {
                    CoreState.Services.ShowInfo(R._("Cannot allocate new event (no allocator wired)."));
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EventCondView.NewAlloc_Click failed: {0}", ex.Message);
            }
        }

        // ============================================================
        // Alloc Event template record side effects (#1592).
        //   CALL EndEvent / CALL 1 → write the record's event-pointer field
        //     (+ W2 victory flag for EndEvent), via ResolveCallTemplate.
        //   Counter Reinforcement → allocate a counter event + set TURN 1-255.
        // All under one undo scope; refuse (no mutation) on unresolvable.
        // ============================================================

        void AllocCallEndEvent_Click(object? sender, RoutedEventArgs e)
            => ApplyAllocCall(EventEditorHostContext.AllocTemplateChoice.CallEndEvent,
                              R._("Could not resolve the chapter end-event for this map (no end-event or no map selected)."));

        void AllocCall1_Click(object? sender, RoutedEventArgs e)
            => ApplyAllocCall(EventEditorHostContext.AllocTemplateChoice.Call1,
                              R._("Cannot apply the CALL 1 template to this record."));

        void ApplyAllocCall(EventEditorHostContext.AllocTemplateChoice choice, string refuseMessage)
        {
            if (!_vm.CanAllocCallTemplate) return;

            _undoService.Begin("Alloc Event Template (CALL)");
            try
            {
                bool ok = _vm.ApplyAllocCallTemplate(choice);
                if (!ok)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowInfo(refuseMessage);
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();

                // Refresh the WHOLE editor (not just EventPtr/W2) so the generic
                // ExtraB8-B11 boxes + every category sub-panel re-sync from the
                // now-current VM state — no stale values left in any field
                // (Copilot re-review: same-byte fields must not diverge).
                UpdateEditorUI();
                CoreState.Services.ShowInfo(R._("Alloc-Event template applied. Event pointer = 0x{0}.", _vm.EventPtr.ToString("X08")));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                // Log.Error takes params string[] (string.Join, NOT composite
                // format) — a single interpolated string with the FULL exception
                // keeps the stack trace (Avalonia Log.Error params-string trap).
                Log.Error($"EventCondView.ApplyAllocCall failed: {ex}");
            }
        }

        void AllocCounter_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanAllocCounterReinforcement) return;

            _undoService.Begin("Alloc Event Template (Counter Reinforcement)");
            try
            {
                bool ok = _vm.ApplyCounterReinforcement();
                if (!ok)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowInfo(R._("Could not allocate the counter-reinforcement event."));
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();

                // Refresh the WHOLE editor so the TURN sub-panel (TurnStart/End)
                // AND the generic ExtraB8/B9 boxes (same underlying bytes for a
                // TURN record) re-sync together — no field shows a stale value
                // (Copilot re-review #1595).
                UpdateEditorUI();
                CoreState.Services.ShowInfo(R._("Counter-reinforcement event allocated at 0x{0} (turn 1-255).", _vm.EventPtr.ToString("X08")));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                // Full exception via interpolation (Avalonia Log.Error params-string trap).
                Log.Error($"EventCondView.AllocCounter_Click failed: {ex}");
            }
        }

        async void PreciseAlloc_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WF EventCondForm.PreciseEevntCondArea (lines 3117-3247):
            // show PLIST picker, allocate version-exact block, write slot back.
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            var popup = new MapPointerNewPLISTPopupView();
            uint? plist = await popup.ShowDialog<uint?>(this);

            // WF guard: plist == 0 means cancelled or reserved sentinel slot.
            if (plist is null || plist == 0) return;

            // Open undo scope AFTER the modal returns (not around it).
            // _undoService.Begin already calls ROM.BeginUndoScope internally,
            // so EventCondCore ambient write_p32 calls are tracked automatically.
            _undoService.Begin("Precise EventCondArea");
            uint off = U.NOT_FOUND;
            try
            {
                off = EventCondCore.AllocNewEventCondBlock(rom, 0);
                if (off == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowInfo(R._("Could not allocate free space for the event condition block."));
                    return;
                }

                if (!EventCondCore.WriteEventPLIST(rom, plist.Value, off))
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowInfo(R._("Could not write the event PLIST slot (plist=0 or slot unsafe)."));
                    return;
                }

                // Navigate the editor to the newly-allocated block so the user
                // sees the new block's slots (turn/talk/object/always/...) rather
                // than "No event data for this map" (WF parity: WF returns
                // write_addr and the caller displays it directly, without
                // re-reading the map-setting event-plist byte).
                _vm.EventDataAddr = off;
                UpdateTopBar();
                ReloadRecordList();
                _undoService.Commit();
                CoreState.Services.ShowInfo(R._("Precise event condition area allocated at 0x{0} and wired to PLIST {1}.", off.ToString("X08"), plist.Value));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EventCondView.PreciseAlloc_Click failed: {0}", ex.Message);
            }
        }

        void JumpEvent_Click(object? sender, RoutedEventArgs e)
        {
            // WF EventCondForm.cs line 2215: open EventScriptForm at the event pointer.
            uint ptr = (uint)(EventPtrBox.Value ?? 0);
            if (!U.isPointer(ptr))
            {
                CoreState.Services.ShowInfo(R._("Event pointer is not a valid ROM address."));
                return;
            }
            uint addr = U.toOffset(ptr);
            // EventCond targets are chapter event-condition (top-level) event pointers, so
            // flag the editor accordingly — the Write-All terminator selection then uses the
            // top-level terminator (#1510 review finding #2). The kind must be set before
            // NavigateTo runs the disassemble.
            var view = WindowManager.Instance.Open<EventScriptView>();
            view.SetEventKind(isWorldMapEvent: false, isTopLevelEvent: true);
            view.NavigateTo(addr);
        }

        void JumpEventUnit_Click(object? sender, RoutedEventArgs e)
        {
            // WF EventCondForm.cs lines 2219-2237: dispatch per ROM version.
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            uint ptr = (uint)(EventPtrBox.Value ?? 0);
            if (!U.isPointer(ptr))
            {
                CoreState.Services.ShowInfo(R._("Event pointer is not a valid ROM address."));
                return;
            }
            uint addr = U.toOffset(ptr);

            int ver = rom.RomInfo.version;
            if (ver >= 8)
                WindowManager.Instance.Navigate<EventUnitView>(addr);
            else if (ver >= 7)
                WindowManager.Instance.Navigate<EventUnitFE7View>(addr);
            else
                WindowManager.Instance.Navigate<EventUnitFE6View>(addr);
        }

        void Reload_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ReloadRecordList();
                UpdateTopBar();
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventCondView.Reload_Click failed: {0}", ex.Message);
            }
        }

        // ============================================================
        // IdFieldControl handlers (#950 T4).
        //   TALK Unit 1/2 (1-based unit IDs) → UnitEditorView Jump/Pick.
        //   OBJECT Chest Item (item ID) → ItemEditorView/ItemFE6View Jump/Pick.
        // The Event Pointer (CODE pointer) keeps its existing cross-jump and is
        // intentionally NOT migrated; the Type discriminant is also left alone.
        // ============================================================

        static uint ItemAddrFor(uint itemId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint itemPtr = rom.RomInfo.item_pointer;
            if (itemPtr == 0) return 0;
            uint baseAddr = rom.p32(itemPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + itemId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void JumpToUnitEditor(IdFieldControl box)
        {
            try
            {
                // TALK Unit IDs are 1-based (matches GetUnitNameByOneBasedId above).
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, box.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("EventCondView.JumpToUnitEditor failed: {0}", ex.Message); }
        }

        async System.Threading.Tasks.Task PickUnitInto(IdFieldControl box)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, box.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null)
                    box.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
            }
            catch (Exception ex) { Log.ErrorF("EventCondView.PickUnitInto failed: {0}", ex.Message); }
        }

        void Unit1_Jump(object? sender, RoutedEventArgs e) => JumpToUnitEditor(Unit1Box);
        async void Unit1_Pick(object? sender, RoutedEventArgs e) => await PickUnitInto(Unit1Box);
        void Unit1_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try
            {
                Unit1NameLabel.Text = NameResolver.GetUnitNameByOneBasedId(e.NewValue);
                Unit1Box.NameText = Unit1NameLabel.Text;
            }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws */ }
        }

        void Unit2_Jump(object? sender, RoutedEventArgs e) => JumpToUnitEditor(Unit2Box);
        async void Unit2_Pick(object? sender, RoutedEventArgs e) => await PickUnitInto(Unit2Box);
        void Unit2_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try
            {
                Unit2NameLabel.Text = NameResolver.GetUnitNameByOneBasedId(e.NewValue);
                Unit2Box.NameText = Unit2NameLabel.Text;
            }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws */ }
        }

        void ChestItem_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ItemAddrFor(ChestItemBox.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ItemFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ItemEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("EventCondView.ChestItem_Jump failed: {0}", ex.Message); }
        }

        async void ChestItem_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ItemAddrFor(ChestItemBox.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ItemFE6View>(addr, this);
                else
                    result = await WindowManager.Instance.PickFromEditor<ItemEditorView>(addr, this);
                if (result != null) ChestItemBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("EventCondView.ChestItem_Pick failed: {0}", ex.Message); }
        }

        void ChestItem_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try
            {
                ChestItemNameLabel.Text = NameResolver.GetItemName(e.NewValue);
                ChestItemBox.NameText = ChestItemNameLabel.Text;
            }
            catch { /* NameResolver may fail without ROM */ }
        }

        // ============================================================
        // #957 W1a: Tutorial Text ID (Huffman text id → TextViewerView Jump).
        //   ShowPick=False — there is no text-picker; Jump only.
        // ============================================================

        void TutorialTextId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = EditorJumpAddressHelper.TextRowAddrFor(CoreState.ROM, TextIdBox.Value);
                if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("EventCondView.TutorialTextId_Jump failed: {0}", ex.Message); }
        }

        void TutorialTextId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { TextIdBox.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : ""; }
            catch { /* NameResolver may fail without ROM */ }
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }
    }
}
