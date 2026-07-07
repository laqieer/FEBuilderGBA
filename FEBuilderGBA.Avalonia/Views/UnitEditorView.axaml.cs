using global::Avalonia;
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
    public partial class UnitEditorView : TranslatedUserControl, IEmbeddableEditor, IPickableEditor, IDataVerifiableView
    {
        readonly UnitEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        List<(uint id, string name)> _classList = new();
        List<(uint id, string name)> _affinityList = new();

        public string ViewTitle => R._("Unit Editor");
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Unit Editor", 934, 661, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public event Action<PickResult>? SelectionConfirmed;

        public UnitEditorView()
        {
            InitializeComponent();
            UnitList.SelectedAddressChanged += OnUnitSelected;
            UnitList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);

            // Ability flag names are set in LoadList() based on ROM version

            // Wire auto-recalculation on stat/growth/level/class changes
            WireGrowthAutoRecalc();

            // Wire weapon rank label updates
            WireWeaponRankLabels();

            // Wire portrait name live update
            PortraitIdBox.ValueChanged += OnPortraitIdChanged;

            // Wire desc text live update
            DescIdBox.ValueChanged += OnDescIdChanged;

            // #358: jump to Support Unit Editor for this unit's support row.
            JumpToSupportUnitButton.Click += JumpToSupportUnit_Click;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList(); UpdateAddressBarInfra();
            }
        }

        /// <summary>
        /// #358 — Open the version-correct Support Unit Editor and select the
        /// support row that this unit's +44 pointer references.
        /// FE6 routes to <c>SupportUnitFE6View</c>; FE7/FE8 route to
        /// <c>SupportUnitEditorView</c>.  Mirrors WinForms <c>J_44_SUPPORTUNIT</c>.
        /// </summary>
        void JumpToSupportUnit_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // #648: Use ViewHelpers.ParseHexText so the "0x" prefix in the
                // displayed pointer string is parsed correctly. The legacy
                // U.atoh helper truncates at the 'x' and returns 0, which made
                // this button silently do nothing.
                uint supportPtr = ViewHelpers.ParseHexText(SupportPtrBox.Text);
                if (supportPtr == 0)
                {
                    // #648: button used to silently do nothing when the unit
                    // had no support data. Surface a friendly message so the
                    // user knows why the click had no effect.
                    CoreState.Services?.ShowInfo("This unit has no support data (support pointer is 0).");
                    return;
                }
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                if (rom.RomInfo.version == 6)
                {
                    var window = WindowManager.Instance.Open<SupportUnitFE6View>();
                    window.JumpToAddr(supportPtr);
                }
                else
                {
                    var window = WindowManager.Instance.Open<SupportUnitEditorView>();
                    window.JumpToAddr(supportPtr);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("UnitEditor.JumpToSupportUnit failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Subscribe to ValueChanged on all NumericUpDown controls that affect growth simulation,
        /// plus the ClassIdCombo. When any value changes (and we're not loading), read UI and recalculate.
        /// </summary>
        void WireGrowthAutoRecalc()
        {
            // All NUDs that affect growth simulation
            NumericUpDown[] growthNuds = {
                LevelBox, HPBox, StrBox, SklBox, SpdBox, DefBox, ResBox, LckBox, ConBox,
                GrowHPBox, GrowStrBox, GrowSklBox, GrowSpdBox, GrowDefBox, GrowResBox, GrowLckBox,
                SimLevelBox,
            };

            foreach (var nud in growthNuds)
            {
                nud.ValueChanged += OnGrowthInputChanged;
            }

            // ClassIdCombo change also triggers recalc
            ClassIdCombo.SelectionChanged += (_, _) =>
            {
                if (!_vm.IsLoading && _vm.CanWrite)
                {
                    ReadFromUI();
                    RecalcGrowth();
                }
            };
        }

        void WireWeaponRankLabels()
        {
            WepSwordBox.ValueChanged += OnWeaponValueChanged;
            WepLanceBox.ValueChanged += OnWeaponValueChanged;
            WepAxeBox.ValueChanged += OnWeaponValueChanged;
            WepBowBox.ValueChanged += OnWeaponValueChanged;
            WepStaffBox.ValueChanged += OnWeaponValueChanged;
            WepAnimaBox.ValueChanged += OnWeaponValueChanged;
            WepLightBox.ValueChanged += OnWeaponValueChanged;
            WepDarkBox.ValueChanged += OnWeaponValueChanged;
        }

        void OnWeaponValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            UpdateWeaponRankLabels();
        }

        void OnPortraitIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(PortraitIdBox.Value ?? 0);
            _vm.PortraitId = id;
            PortraitNameLabel.Text = NameResolver.GetPortraitName(id);
            TryShowPortrait();
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
            NavigateToTextId((uint)(DescIdBox.Value ?? 0));
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

        void OnClassIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            JumpToClass_Click(sender, new RoutedEventArgs());
        }

        void OnPortraitLinkClick(object? sender, PointerPressedEventArgs e)
        {
            JumpToPortrait_Click(sender, new RoutedEventArgs());
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

        void UpdateWeaponRankLabels()
        {
            SwordRankText.Text = WeaponRankUtil.GetRankLetter((uint)(WepSwordBox.Value ?? 0));
            LanceRankText.Text = WeaponRankUtil.GetRankLetter((uint)(WepLanceBox.Value ?? 0));
            AxeRankText.Text = WeaponRankUtil.GetRankLetter((uint)(WepAxeBox.Value ?? 0));
            BowRankText.Text = WeaponRankUtil.GetRankLetter((uint)(WepBowBox.Value ?? 0));
            StaffRankText.Text = WeaponRankUtil.GetRankLetter((uint)(WepStaffBox.Value ?? 0));
            AnimaRankText.Text = WeaponRankUtil.GetRankLetter((uint)(WepAnimaBox.Value ?? 0));
            LightRankText.Text = WeaponRankUtil.GetRankLetter((uint)(WepLightBox.Value ?? 0));
            DarkRankText.Text = WeaponRankUtil.GetRankLetter((uint)(WepDarkBox.Value ?? 0));
        }

        void OnGrowthInputChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!_vm.IsLoading && _vm.CanWrite)
            {
                ReadFromUI();
                RecalcGrowth();
            }
        }

        void RecalcGrowth()
        {
            _vm.SimLevel = (uint)(SimLevelBox.Value ?? 20);
            _vm.CalculateGrowth();
            GrowthSimLabel.Text = _vm.GrowthSimText;
        }

        void LoadList()
        {
            try
            {
                // Populate combo dropdowns BEFORE SetItems, because SetItems
                // auto-selects index 0 which triggers OnUnitSelected → UpdateUI
                // that needs the combos to have ItemsSource already set (fixes #52).
                _classList = ComboResourceHelper.MakeClassList();
                ClassIdCombo.ItemsSource = _classList.Select(x => x.name).ToList();

                _affinityList = ComboResourceHelper.MakeAffinityList();
                AffinityCombo.ItemsSource = _affinityList.Select(x => x.name).ToList();

                // Show "Edit Skills" button if a skill system is installed
                EditSkillsButton.IsVisible = PatchDetectionService.Instance.HasSkillSystem;

                // Set version-aware ability flag names
                int version = CoreState.ROM?.RomInfo?.version ?? 8;
                Ability1Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(version, 1));
                Ability2Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(version, 2));
                Ability3Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(version, 3));
                Ability4Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(version, 4));

                var items = _vm.LoadUnitList();
                UnitList.SetItemsWithIcons(items, index => ListIconLoaders.UnitPortraitLoader(items, index));
                UpdateFE78Visibility();
            }
            catch (Exception ex)
            {
                Log.ErrorF("UnitEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnUnitSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadUnit(addr);
                UpdateUI();
                UpdateFE78Visibility();
                TryShowPortrait();
                TryShowListPreview();
                _vm.CalculateGrowth();
                GrowthSimLabel.Text = _vm.GrowthSimText;
                UpdateWarnings();
                // #413: post-selection UI refresh - HardCoding warning visibility
                // depends on the selected unit's id.
                RefreshHardCodingWarning();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("UnitEditorView.OnUnitSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            UnitList.SelectAddress(address);
        }

        void UpdateFE78Visibility()
        {
            FE78Panel.IsVisible = !_vm.IsFE6;
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameLabel.Text = _vm.Name;

            // Identity
            NameIdBox.Value = _vm.NameId;
            DescIdBox.Value = _vm.DescId;
            DescTextLabel.Text = _vm.DescText;
            UnitIdBox.Value = _vm.UnitId;

            // Class combo
            int classIdx = _classList.FindIndex(x => x.id == _vm.ClassId);
            ClassIdCombo.SelectedIndex = classIdx >= 0 ? classIdx : (int)_vm.ClassId;

            PortraitIdBox.Value = _vm.PortraitId;
            PortraitNameLabel.Text = NameResolver.GetPortraitName(_vm.PortraitId);
            MapFaceBox.Value = _vm.MapFace;

            // Affinity combo
            int affIdx = _affinityList.FindIndex(x => x.id == _vm.Affinity);
            AffinityCombo.SelectedIndex = affIdx >= 0 ? affIdx : (int)_vm.Affinity;

            SortOrderBox.Value = _vm.SortOrder;
            LevelBox.Value = _vm.Level;

            // Base stats
            HPBox.Value = _vm.HP;
            StrBox.Value = _vm.Str;
            SklBox.Value = _vm.Skl;
            SpdBox.Value = _vm.Spd;
            DefBox.Value = _vm.Def;
            ResBox.Value = _vm.Res;
            LckBox.Value = _vm.Lck;
            ConBox.Value = _vm.Con;

            // Weapon levels
            WepSwordBox.Value = _vm.WepSword;
            WepLanceBox.Value = _vm.WepLance;
            WepAxeBox.Value = _vm.WepAxe;
            WepBowBox.Value = _vm.WepBow;
            WepStaffBox.Value = _vm.WepStaff;
            WepAnimaBox.Value = _vm.WepAnima;
            WepLightBox.Value = _vm.WepLight;
            WepDarkBox.Value = _vm.WepDark;

            UpdateWeaponRankLabels();

            // Growth rates
            GrowHPBox.Value = _vm.GrowHP;
            GrowStrBox.Value = _vm.GrowStr;
            GrowSklBox.Value = _vm.GrowSkl;
            GrowSpdBox.Value = _vm.GrowSpd;
            GrowDefBox.Value = _vm.GrowDef;
            GrowResBox.Value = _vm.GrowRes;
            GrowLckBox.Value = _vm.GrowLck;

            // Unknown 35-39
            Unk35Box.Value = _vm.Unk35;
            Unk36Box.Value = _vm.Unk36;
            Unk37Box.Value = _vm.Unk37;
            Unk38Box.Value = _vm.Unk38;
            Unk39Box.Value = _vm.Unk39;

            // Ability flags (BitFlagPanel)
            Ability1Flags.Value = (byte)_vm.Ability1;
            Ability2Flags.Value = (byte)_vm.Ability2;
            Ability3Flags.Value = (byte)_vm.Ability3;
            Ability4Flags.Value = (byte)_vm.Ability4;

            // Support pointer (hex)
            SupportPtrBox.Text = $"0x{_vm.SupportPtr:X08}";

            // FE7/8 only (always set to avoid null NUDs in hidden panel)
            TalkGroupBox.Value = _vm.TalkGroup;
            Unk49Box.Value = _vm.Unk49;
            Unk50Box.Value = _vm.Unk50;
            Unk51Box.Value = _vm.Unk51;

            // Growth simulator
            GrowthSimLabel.Text = _vm.GrowthSimText;
        }

        void ReadFromUI()
        {
            // Identity
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.DescId = (uint)(DescIdBox.Value ?? 0);
            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);

            // Class from combo
            int classIdx = ClassIdCombo.SelectedIndex;
            _vm.ClassId = classIdx >= 0 && classIdx < _classList.Count ? _classList[classIdx].id : 0;

            _vm.PortraitId = (uint)(PortraitIdBox.Value ?? 0);
            _vm.MapFace = (uint)(MapFaceBox.Value ?? 0);

            // Affinity from combo
            int affIdx = AffinityCombo.SelectedIndex;
            _vm.Affinity = affIdx >= 0 && affIdx < _affinityList.Count ? _affinityList[affIdx].id : 0;

            _vm.SortOrder = (uint)(SortOrderBox.Value ?? 0);
            _vm.Level = (uint)(LevelBox.Value ?? 0);

            // Base stats (signed)
            _vm.HP = (int)(HPBox.Value ?? 0);
            _vm.Str = (int)(StrBox.Value ?? 0);
            _vm.Skl = (int)(SklBox.Value ?? 0);
            _vm.Spd = (int)(SpdBox.Value ?? 0);
            _vm.Def = (int)(DefBox.Value ?? 0);
            _vm.Res = (int)(ResBox.Value ?? 0);
            _vm.Lck = (int)(LckBox.Value ?? 0);
            _vm.Con = (int)(ConBox.Value ?? 0);

            // Weapon levels
            _vm.WepSword = (uint)(WepSwordBox.Value ?? 0);
            _vm.WepLance = (uint)(WepLanceBox.Value ?? 0);
            _vm.WepAxe = (uint)(WepAxeBox.Value ?? 0);
            _vm.WepBow = (uint)(WepBowBox.Value ?? 0);
            _vm.WepStaff = (uint)(WepStaffBox.Value ?? 0);
            _vm.WepAnima = (uint)(WepAnimaBox.Value ?? 0);
            _vm.WepLight = (uint)(WepLightBox.Value ?? 0);
            _vm.WepDark = (uint)(WepDarkBox.Value ?? 0);

            // Growth rates
            _vm.GrowHP = (uint)(GrowHPBox.Value ?? 0);
            _vm.GrowStr = (uint)(GrowStrBox.Value ?? 0);
            _vm.GrowSkl = (uint)(GrowSklBox.Value ?? 0);
            _vm.GrowSpd = (uint)(GrowSpdBox.Value ?? 0);
            _vm.GrowDef = (uint)(GrowDefBox.Value ?? 0);
            _vm.GrowRes = (uint)(GrowResBox.Value ?? 0);
            _vm.GrowLck = (uint)(GrowLckBox.Value ?? 0);

            // Unknown 35-39
            _vm.Unk35 = (uint)(Unk35Box.Value ?? 0);
            _vm.Unk36 = (uint)(Unk36Box.Value ?? 0);
            _vm.Unk37 = (uint)(Unk37Box.Value ?? 0);
            _vm.Unk38 = (uint)(Unk38Box.Value ?? 0);
            _vm.Unk39 = (uint)(Unk39Box.Value ?? 0);

            // Ability flags (BitFlagPanel)
            _vm.Ability1 = Ability1Flags.Value;
            _vm.Ability2 = Ability2Flags.Value;
            _vm.Ability3 = Ability3Flags.Value;
            _vm.Ability4 = Ability4Flags.Value;

            // Support pointer (parse hex) - #648: ViewHelpers.ParseHexText
            // handles the displayed "0x..." form; U.atoh truncates at the 'x'
            // and would zero-out the pointer on every Write.
            _vm.SupportPtr = ViewHelpers.ParseHexText(SupportPtrBox.Text);

            // FE7/8 only
            if (!_vm.IsFE6)
            {
                _vm.TalkGroup = (uint)(TalkGroupBox.Value ?? 0);
                _vm.Unk49 = (uint)(Unk49Box.Value ?? 0);
                _vm.Unk50 = (uint)(Unk50Box.Value ?? 0);
                _vm.Unk51 = (uint)(Unk51Box.Value ?? 0);
            }
        }

        void TryShowPortrait()
        {
            try
            {
                _vm.LoadPortraitImage();
                PortraitImage.SetImage(_vm.PortraitImage);
            }
            catch (Exception ex)
            {
                Log.ErrorF("UnitEditorView.TryShowPortrait failed: {0}", ex.Message);
                PortraitImage.SetImage(null);
            }
        }

        void TryShowListPreview()
        {
            try
            {
                // Resolve portrait ID with class fallback for generic units
                uint previewPortraitId = _vm.PortraitId;
                if (previewPortraitId == 0)
                    previewPortraitId = PreviewIconHelper.GetClassPortraitId(_vm.ClassId);
                var img = PreviewIconHelper.LoadPortraitMini(previewPortraitId);
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

            // #1141: in decomp mode, structured-table edits are source-backed. Route the
            // "units" table to the C/JSON-source writer instead of the preview ROM.
            // The classic (!IsDecompMode) ROM-write path below is byte-for-byte unchanged.
            if (CoreState.IsDecompMode)
            {
                if (TryWriteUnitSource())
                    return;
                CoreState.Services.ShowInfo(R._("This unit is ROM-only in decomp mode. Edit the source manually and rebuild."));
                return;
            }

            _undoService.Begin(R._("Edit Unit"));
            try
            {
                _vm.WriteUnit();
                _undoService.Commit();
                _vm.MarkClean();
                UpdateWarnings();
                CoreState.Services.ShowInfo(R._("Unit data written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("Write failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #1141: attempt a source-backed write of the current unit. Returns true when the
        /// units table HAS a source owner (write attempted, accurate status message shown).
        /// Returns false ONLY when there is no owner at all, so the caller shows the generic
        /// ROM-only notice (never a silent preview-ROM write). Mirrors TryWriteItemSource.
        /// </summary>
        bool TryWriteUnitSource()
        {
            var project = CoreState.DecompProject;
            var owner = project?.TryGetTableOwner("units");
            if (owner == null)
                return false;

            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (owner.Fields != null)
                foreach (var f in owner.Fields)
                    if (f != null && !string.IsNullOrEmpty(f.Name))
                        declared.Add(f.Name);

            var changed = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (var kv in _vm.BuildSourceFieldDict())
                if (declared.Contains(kv.Key))
                    changed[kv.Key] = kv.Value;

            var res = DecompSourceWriterCore.WriteTableEntry(
                project, "units", _vm.CurrentUnitIndex, changed);

            switch (res.Status)
            {
                case DecompSourceWriteStatus.Ok:
                    _vm.MarkClean();
                    _vm.RefreshSourceFieldSnapshot();
                    UpdateWarnings();
                    if (res.ChangedFields != null && res.ChangedFields.Count > 0)
                        CoreState.Services.ShowInfo(R._("Unit source updated. Project needs rebuild."));
                    else
                        CoreState.Services.ShowInfo(R._("No change needed — the source already matches."));
                    break;
                case DecompSourceWriteStatus.RomOnly:
                    CoreState.Services.ShowInfo(R._("This unit table is ROM-only in decomp mode."));
                    break;
                case DecompSourceWriteStatus.Manual:
                    CoreState.Services.ShowInfo(res.Message);
                    break;
                default:
                    CoreState.Services.ShowError(res.Message);
                    break;
            }
            return true;
        }

        void JumpToClass_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                int classIdx = ClassIdCombo.SelectedIndex;
                uint classId = classIdx >= 0 && classIdx < _classList.Count ? _classList[classIdx].id : 0;
                uint baseAddr = rom.p32(rom.RomInfo.class_pointer);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint addr = baseAddr + classId * rom.RomInfo.class_datasize;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("JumpToClass failed: {0}", ex.Message);
            }
        }

        void JumpToPortrait_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint portraitId = (uint)(PortraitIdBox.Value ?? 0);
                uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint dataSize = rom.RomInfo.portrait_datasize;
                if (dataSize == 0) dataSize = 28;
                uint addr = baseAddr + portraitId * dataSize;
                WindowManager.Instance.Navigate<PortraitViewerView>(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("JumpToPortrait failed: {0}", ex.Message);
            }
        }

        async void PickClass_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;

                // Calculate current class address for navigation
                int classIdx = ClassIdCombo.SelectedIndex;
                uint classId = classIdx >= 0 && classIdx < _classList.Count ? _classList[classIdx].id : 0;
                uint baseAddr = rom.p32(rom.RomInfo.class_pointer);
                uint navAddr = U.isSafetyOffset(baseAddr) ? baseAddr + classId * rom.RomInfo.class_datasize : 0;

                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ClassFE6View>(navAddr, TopLevel.GetTopLevel(this) as Window);
                else
                    result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(navAddr, TopLevel.GetTopLevel(this) as Window);
                if (result != null)
                {
                    // result.Index is the class list index — set the combo
                    int comboIdx = _classList.FindIndex(x => x.id == (uint)result.Index);
                    if (comboIdx >= 0)
                        ClassIdCombo.SelectedIndex = comboIdx;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("PickClass failed: {0}", ex.Message);
            }
        }

        async void PickPortrait_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;

                uint portraitId = (uint)(PortraitIdBox.Value ?? 0);
                uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
                uint dataSize = rom.RomInfo.portrait_datasize;
                if (dataSize == 0) dataSize = 28;
                uint navAddr = U.isSafetyOffset(baseAddr) ? baseAddr + portraitId * dataSize : 0;

                var result = await WindowManager.Instance.PickFromEditor<PortraitViewerView>(navAddr, TopLevel.GetTopLevel(this) as Window);
                if (result != null)
                {
                    PortraitIdBox.Value = result.Index;
                    PortraitNameLabel.Text = NameResolver.GetPortraitName((uint)result.Index);
                    TryShowPortrait();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("PickPortrait failed: {0}", ex.Message);
            }
        }

        // #648: CalculateGrowth_Click removed - the simulator already
        // auto-recalculates via WireGrowthAutoRecalc() when any growth-relevant
        // input (stat NUDs, growth NUDs, level, sim-level, class) changes.

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Rollback();
            if (_vm.CurrentAddr != 0)
            {
                _vm.LoadUnit(_vm.CurrentAddr);
                UpdateUI();
            }
        }

        void UpdateWarnings()
        {
            var warnings = _vm.ValidateUnit();
            WarningsBorder.IsVisible = warnings.Count > 0;
            WarningsList.ItemsSource = warnings;
        }

        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        void EditSkills_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var skillType = PatchDetectionService.Instance.SkillSystem;
                switch (skillType)
                {
                    case PatchDetectionService.SkillSystemType.SkillSystem:
                        WindowManager.Instance.Open<SkillAssignmentUnitSkillSystemView>();
                        break;
                    case PatchDetectionService.SkillSystemType.CSkillSys09x:
                    case PatchDetectionService.SkillSystemType.CSkillSys300:
                        WindowManager.Instance.Open<SkillAssignmentUnitCSkillSysView>();
                        break;
                    case PatchDetectionService.SkillSystemType.FE8N:
                    case PatchDetectionService.SkillSystemType.FE8N_Ver2:
                    case PatchDetectionService.SkillSystemType.FE8N_Ver3:
                    case PatchDetectionService.SkillSystemType.Yugudora:
                    case PatchDetectionService.SkillSystemType.Midori:
                        // #1452: the FE8N view edits the OPEN unit's skill bytes
                        // (B39/B40/B41) directly, so pass the current unit address
                        // via Navigate (Open<T>() alone never loads an address →
                        // the view stays inert with a false "no patch" warning).
                        var fe8nView = WindowManager.Instance.Navigate<SkillAssignmentUnitFE8NView>(_vm.CurrentAddr);
                        // The FE8N view writes B39/B40/B41 directly to ROM, but the
                        // parent Unit Editor still holds a stale copy of those bytes
                        // (Unk39 + ability bytes 0x28/0x29). Re-sync the parent when
                        // the child closes so a later parent Write doesn't clobber
                        // the skill edit.
                        if (fe8nView != null)
                        {
                            fe8nView.Closed -= OnSkillEditorClosed;
                            fe8nView.Closed += OnSkillEditorClosed;
                        }
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("EditSkills_Click failed: " + ex);
            }
        }

        // #1452: re-sync the parent Unit Editor's in-memory skill bytes after the
        // FE8N skill editor closes, so the next parent Write preserves the edit.
        // Refresh ONLY the three bytes the child editor owns (Unk39 @0x27,
        // Ability1 @0x28, Ability2 @0x29) from ROM — a full LoadUnit/UpdateUI
        // would discard any UNSAVED parent edits (Level/Name/Class/...) made
        // before opening the skill editor.
        void OnSkillEditorClosed(object? sender, EventArgs e)
        {
            if (sender is global::Avalonia.Controls.Window w)
                w.Closed -= OnSkillEditorClosed;
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null || _vm.CurrentAddr == 0) return;
                uint addr = _vm.CurrentAddr;
                if (addr + 42 > (uint)rom.Data.Length) return;

                bool prev = _vm.IsLoading;
                _vm.IsLoading = true;
                try
                {
                    _vm.Unk39 = rom.u8(addr + 39);
                    _vm.Ability1 = rom.u8(addr + 40);
                    _vm.Ability2 = rom.u8(addr + 41);
                    // Push just those three controls (mirrors UpdateUI's skill rows).
                    Unk39Box.Value = _vm.Unk39;
                    Ability1Flags.Value = (byte)_vm.Ability1;
                    Ability2Flags.Value = (byte)_vm.Ability2;
                }
                finally { _vm.IsLoading = prev; }
            }
            catch (Exception ex)
            {
                Log.Error("OnSkillEditorClosed reload failed: " + ex);
            }
        }

        // ---- #413: CSV Export/Import (parity with WF UnitForm CsvManager) ----

        /// <summary>Build a UnitCsvManager from the UI's 8 option checkboxes.</summary>
        UnitCsvManager MakeCsvManager()
        {
            return new UnitCsvManager(
                useClipboard: UseClipboardCheck.IsChecked == true,
                includeUID: IncludeUIDCheck.IsChecked == true,
                includeHeader: IncludeHeaderCheck.IsChecked == true,
                includeName: IncludeNameCheck.IsChecked == true,
                includeBaseStats: IncludeBaseStatsCheck.IsChecked == true,
                includeGrowths: IncludeGrowthsCheck.IsChecked == true,
                includeWepLevel: IncludeWepLevelCheck.IsChecked == true,
                growthsAsDecimal: GrowthsAsDecimalCheck.IsChecked == true);
        }

        /// <summary>
        /// Enumerate every unit-row address currently in the AddressList.
        /// Mirrors <c>UnitEditorViewModel.LoadUnitList()</c>: applies the FE6
        /// first-entry skip (entry 0 in FE6 is a null/pointer record) and
        /// stops when the next row would overrun the ROM end (matches the
        /// `addr + dataSize > rom.Data.Length` guard in the VM).
        /// </summary>
        uint[] GetAllUnitAddresses()
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return Array.Empty<uint>();
                uint baseAddr = rom.p32(rom.RomInfo.unit_pointer);
                if (!U.isSafetyOffset(baseAddr)) return Array.Empty<uint>();
                uint count = rom.RomInfo.unit_maxcount;
                uint size = rom.RomInfo.unit_datasize;
                if (size == 0) size = 52;
                if (count == 0) count = 0x100;
                // FE6: skip first entry (matches LoadUnitList() and WF UnitForm).
                if (rom.RomInfo.version == 6) baseAddr += size;
                var addrs = new List<uint>(checked((int)count));
                for (uint i = 0; i < count; i++)
                {
                    uint addr = baseAddr + i * size;
                    if (addr + size > (uint)rom.Data.Length) break;
                    addrs.Add(addr);
                }
                return addrs.ToArray();
            }
            catch (Exception ex)
            {
                Log.ErrorF("GetAllUnitAddresses failed: {0}", ex.Message);
                return Array.Empty<uint>();
            }
        }

        async void ExportAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;
                var addrs = GetAllUnitAddresses();
                if (addrs.Length == 0) return;
                await MakeCsvManager().ExportAllAsync(TopLevel.GetTopLevel(this) as Window, rom, addrs);
            }
            catch (Exception ex) { Log.ErrorF("ExportAll_Click failed: {0}", ex.Message); }
        }

        async void ExportSelected_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;
                if (_vm.CurrentAddr == 0) return;
                // #1016: pass the SELECTED unit id (0-based AddressList index)
                // so the exported single-row CSV carries the correct UID and
                // the FE8U MagicSplit MAG column reads from the right unit
                // record. Falls back to 0 when no selection is resolvable.
                int idx = UnitList.SelectedOriginalIndex;
                uint uid = idx >= 0 ? (uint)idx : 0u;
                await MakeCsvManager().ExportSelectedAsync(TopLevel.GetTopLevel(this) as Window, rom, _vm.CurrentAddr, uid);
            }
            catch (Exception ex) { Log.ErrorF("ExportSelected_Click failed: {0}", ex.Message); }
        }

        async void ImportAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;
                var addrs = GetAllUnitAddresses();
                if (addrs.Length == 0) return;
                var mgr = MakeCsvManager();
                // Read the CSV FIRST (file picker / clipboard) so the undo
                // scope only wraps the actual ROM writes. Opening Begin/Commit
                // across an `await` would risk capturing unrelated writes
                // that happen while the picker dialog is open.
                string? csv = await mgr.ReadCsvForUiAsync(TopLevel.GetTopLevel(this) as Window);
                if (csv == null) return;
                _undoService.Begin(R._("Import Units CSV"));
                int written;
                try
                {
                    written = mgr.ApplyImportCsv(rom, csv, addrs);
                    _undoService.Commit();
                }
                catch { _undoService.Rollback(); throw; }
                if (written > 0 && _vm.CurrentAddr != 0)
                {
                    _vm.LoadUnit(_vm.CurrentAddr);
                    UpdateUI();
                }
            }
            catch (Exception ex) { Log.ErrorF("ImportAll_Click failed: {0}", ex.Message); }
        }

        async void ImportSelected_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;
                if (_vm.CurrentAddr == 0) return;
                var mgr = MakeCsvManager();
                // Read CSV first (no undo scope across the picker await).
                string? csv = await mgr.ReadCsvForUiAsync(TopLevel.GetTopLevel(this) as Window);
                if (csv == null) return;
                // #1016: thread the SELECTED unit id (0-based AddressList index)
                // so the FE8U MagicSplit MAG column is read into the correct
                // record (WriteUnit*MagicExtends index by uid).
                int selIdx = UnitList.SelectedOriginalIndex;
                uint? selUid = selIdx >= 0 ? (uint)selIdx : (uint?)null;
                _undoService.Begin(R._("Import Unit CSV"));
                int written;
                try
                {
                    written = mgr.ApplyImportCsv(rom, csv, new[] { _vm.CurrentAddr }, selUid);
                    _undoService.Commit();
                }
                catch { _undoService.Rollback(); throw; }
                if (written > 0)
                {
                    _vm.LoadUnit(_vm.CurrentAddr);
                    UpdateUI();
                }
            }
            catch (Exception ex) { Log.ErrorF("ImportSelected_Click failed: {0}", ex.Message); }
        }

        // ---- #413: Address-bar infrastructure ----

        /// <summary>
        /// Populate the address-bar labels with the current ROM's unit-table
        /// metadata. Mirrors WF UnitForm's label1 / label2 / label22 / label23.
        /// ReadStartAddress is the resolved table base (the value at the
        /// pointer slot) so it matches the actual data location, not the
        /// pointer's storage offset.
        /// </summary>
        void UpdateAddressBarInfra()
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint resolvedBase = rom.p32(rom.RomInfo.unit_pointer);
                // #649: top-bar fields are now properties on the unified
                // EditorTopBar control. Setters cope with null during early
                // construction (the AXAML-named control may be null before
                // InitializeComponent finishes).
                if (TopBar != null)
                {
                    TopBar.StartAddressText = $"0x{resolvedBase:X8}";
                    TopBar.ReadCountText = rom.RomInfo.unit_maxcount.ToString();
                }
                if (SizeLabel != null)
                    SizeLabel.Text = $"0x{rom.RomInfo.unit_datasize:X}";
            }
            catch (Exception ex) { Log.ErrorF("UnitEditorView.UpdateAddressBarInfra failed: {0}", ex.Message); }
        }

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            LoadList();
            UpdateAddressBarInfra();
        }

        // ---- #413: HardCoding warning ----

        /// <summary>
        /// Refresh the HardCoding-warning hyperlink's visibility based on the
        /// current unit's id. Mirrors WF UnitForm.CheckHardCodingWarning.
        /// </summary>
        void RefreshHardCodingWarning()
        {
            try
            {
                // AddressList indices are 0-based; WF "Unit id" (used by the
                // AsmMap cache lookup) is 1-based.
                int idx = UnitList.SelectedOriginalIndex;
                if (idx < 0)
                {
                    HardCodingWarningLabel.IsVisible = false;
                    return;
                }
                uint unitId = (uint)(idx + 1);
                bool r = CoreState.AsmMapFileAsmCache?.IsHardCodeUnit(unitId) ?? false;
                HardCodingWarningLabel.IsVisible = r;
            }
            catch (Exception ex)
            {
                Log.ErrorF("UnitEditorView.RefreshHardCodingWarning failed: {0}", ex.Message);
                HardCodingWarningLabel.IsVisible = false;
            }
        }

        void HardCodingWarning_Click(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                int idx = UnitList.SelectedOriginalIndex;
                if (idx < 0) return;
                uint unitId = (uint)(idx + 1);
                var pv = WindowManager.Instance.Open<PatchManagerView>();
                pv.JumpTo($"HARDCODING_UNIT={unitId:X2}", 0);
            }
            catch (Exception ex) { Log.ErrorF("UnitEditorView.HardCodingWarning_Click failed: {0}", ex.Message); }
        }

        public void EnablePickMode() => UnitList.EnablePickMode();

        /// <summary>Select the first item in the list (for smoke testing).</summary>
        public void SelectFirstItem()
        {
            UnitList.SelectFirst();
        }
    }
}
