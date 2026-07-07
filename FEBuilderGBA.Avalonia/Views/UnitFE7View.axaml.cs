using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitFE7View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly UnitFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Units (FE7) Editor";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Units (FE7) Editor", 1409, 900, SizeToContent: true);
        public event EventHandler? CloseRequested;

        public UnitFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;

            // Wire desc text live update
            DescIdBox.ValueChanged += OnDescIdChanged;

            // #358: jump to Support Unit Editor for this unit's support row.
            JumpToSupportUnitButton.Click += JumpToSupportUnit_Click;

            // #428: wire weapon-rank NumericUpDowns -> letter labels.
            WepSwordBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepSwordBox, WepSwordLetterLabel);
            WepLanceBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepLanceBox, WepLanceLetterLabel);
            WepAxeBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepAxeBox, WepAxeLetterLabel);
            WepBowBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepBowBox, WepBowLetterLabel);
            WepStaffBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepStaffBox, WepStaffLetterLabel);
            WepAnimaBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepAnimaBox, WepAnimaLetterLabel);
            WepLightBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepLightBox, WepLightLetterLabel);
            WepDarkBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepDarkBox, WepDarkLetterLabel);

            // #428: wire growth-sim recompute on base / growth / class / sim-level changes.
            // (HP/STR/.../ClassId boxes are bound to VM; subscribing to ValueChanged is enough.)
            HPBox.ValueChanged += (_, _) => RefreshGrowthSim();
            StrBox.ValueChanged += (_, _) => RefreshGrowthSim();
            SklBox.ValueChanged += (_, _) => RefreshGrowthSim();
            SpdBox.ValueChanged += (_, _) => RefreshGrowthSim();
            DefBox.ValueChanged += (_, _) => RefreshGrowthSim();
            ResBox.ValueChanged += (_, _) => RefreshGrowthSim();
            LckBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowHPBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowSTRBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowSKLBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowSPDBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowDEFBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowRESBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowLCKBox.ValueChanged += (_, _) => RefreshGrowthSim();
            LevelBox.ValueChanged += (_, _) => RefreshGrowthSim();
            ClassIdBox.ValueChanged += (_, _) => RefreshGrowthSim();
            SimLevelBox.ValueChanged += (_, _) => RefreshGrowthSim();
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
        /// #358 — Open the FE7 Support Unit Editor (shared FE7/FE8 view) and
        /// select the support row that this unit's +44 pointer references.
        /// </summary>
        void JumpToSupportUnit_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // #648: Use ViewHelpers.ParseHexText - U.atoh truncates at the
                // 'x' in the displayed "0x..." string and silently returns 0.
                uint supportPtr = ViewHelpers.ParseHexText(SupportPtrBox.Text);
                if (supportPtr == 0)
                {
                    // #648: silent no-op made the button appear broken when the
                    // current unit had no support data. Surface a friendly
                    // message instead. Same fix as UnitEditorView + UnitFE6View.
                    CoreState.Services?.ShowInfo("This unit has no support data (support pointer is 0).");
                    return;
                }
                var window = WindowManager.Instance.Open<SupportUnitEditorView>();
                window.JumpToAddr(supportPtr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("UnitFE7View.JumpToSupportUnit failed: {0}", ex.Message);
            }
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadUnitList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("UnitFE7View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadUnit(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();

                // #428: post-selection UI refresh — HardCoding warning visibility,
                // weapon letters, growth-sim default level + initial display.
                RefreshHardCodingWarning();
                RefreshAllWeaponLetters();
                ResetSimLevelToClassMax();
                RefreshGrowthSim();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("UnitFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #428: refresh every weapon-rank letter label (after a new unit
        /// loads). Each weapon NumericUpDown's ValueChanged also calls this
        /// when the user edits a single rank.
        /// </summary>
        void RefreshAllWeaponLetters()
        {
            RefreshWeaponLetter(WepSwordBox, WepSwordLetterLabel);
            RefreshWeaponLetter(WepLanceBox, WepLanceLetterLabel);
            RefreshWeaponLetter(WepAxeBox, WepAxeLetterLabel);
            RefreshWeaponLetter(WepBowBox, WepBowLetterLabel);
            RefreshWeaponLetter(WepStaffBox, WepStaffLetterLabel);
            RefreshWeaponLetter(WepAnimaBox, WepAnimaLetterLabel);
            RefreshWeaponLetter(WepLightBox, WepLightLetterLabel);
            RefreshWeaponLetter(WepDarkBox, WepDarkLetterLabel);
        }

        /// <summary>
        /// #428: when a new unit is selected, default the SimLevel to the
        /// max level for the unit's class (matches WF UnitFE7Form's
        /// AddressList_SelectedIndexChanged behavior).
        /// </summary>
        void ResetSimLevelToClassMax()
        {
            try
            {
                int max = GrowSimulator.CalcMaxLevel(_vm.ClassId);
                if (max <= 0) max = 20;
                SimLevelBox.Value = max;
            }
            catch { SimLevelBox.Value = 20; }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            DecodedNameLabel.Text = _vm.Name;

            // Identity
            NameIdBox.Value = _vm.NameId;
            DescIdBox.Value = _vm.DescId;
            DescTextLabel.Text = _vm.DescText;
            UnitIdBox.Value = _vm.UnitId;
            ClassIdBox.Value = _vm.ClassId;
            // Push VM-resolved class name; ValueChanged also refreshes on edit.
            try { ClassIdBox.NameText = NameResolver.GetClassName(_vm.ClassId); }
            catch { /* NameResolver may fail without ROM */ }
            PortraitIdBox.Value = _vm.PortraitId;
            MapFaceBox.Value = _vm.MapFace;
            AffinityBox.Value = _vm.Affinity;
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

            // Weapon ranks
            WepSwordBox.Value = _vm.WepSword;
            WepLanceBox.Value = _vm.WepLance;
            WepAxeBox.Value = _vm.WepAxe;
            WepBowBox.Value = _vm.WepBow;
            WepStaffBox.Value = _vm.WepStaff;
            WepAnimaBox.Value = _vm.WepAnima;
            WepLightBox.Value = _vm.WepLight;
            WepDarkBox.Value = _vm.WepDark;

            // Growth rates
            GrowHPBox.Value = _vm.GrowHP;
            GrowSTRBox.Value = _vm.GrowSTR;
            GrowSKLBox.Value = _vm.GrowSKL;
            GrowSPDBox.Value = _vm.GrowSPD;
            GrowDEFBox.Value = _vm.GrowDEF;
            GrowRESBox.Value = _vm.GrowRES;
            GrowLCKBox.Value = _vm.GrowLCK;

            // Palette & anime
            LowerClassPaletteBox.Value = _vm.LowerClassPalette;
            UpperClassPaletteBox.Value = _vm.UpperClassPalette;
            LowerClassAnimeBox.Value = _vm.LowerClassAnime;
            UpperClassAnimeBox.Value = _vm.UpperClassAnime;
            Unk39Box.Value = _vm.Unk39;

            // Ability flags
            Ability1Box.Value = _vm.Ability1;
            Ability2Box.Value = _vm.Ability2;
            Ability3Box.Value = _vm.Ability3;
            Ability4Box.Value = _vm.Ability4;

            // Support & talk
            SupportPtrBox.Text = $"0x{_vm.SupportPtr:X08}";
            TalkGroupBox.Value = _vm.TalkGroup;
            Unk49Box.Value = _vm.Unk49;
            Unk50Box.Value = _vm.Unk50;
            Unk51Box.Value = _vm.Unk51;
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

        // -- ClassId IdFieldControl handlers (#366) --------------------------

        /// <summary>
        /// Compute the ClassEditorView ROM address for the given class index.
        /// Returns 0 when the class table is unavailable OR when the computed
        /// entry address falls outside ROM bounds (i.e. the id is out of range).
        /// </summary>
        static uint ClassAddrFor(uint classId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint classPtr = rom.RomInfo.class_pointer;
            if (classPtr == 0) return 0;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + classId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void ClassId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("UnitFE7View.ClassId_Jump failed: {0}", ex.Message); }
        }

        async void ClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null)
                {
                    ClassIdBox.Value = (uint)result.Index;
                    // NameText refresh via ValueChanged.
                }
            }
            catch (Exception ex) { Log.ErrorF("UnitFE7View.ClassId_Pick failed: {0}", ex.Message); }
        }

        void ClassId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { ClassIdBox.NameText = NameResolver.GetClassName(e.NewValue); }
            catch { /* fallback silently */ }
        }

        void OnPortraitLinkClick(object? sender, PointerPressedEventArgs e)
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
            catch (Exception ex) { Log.ErrorF("OnPortraitLinkClick failed: {0}", ex.Message); }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            // Identity
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.DescId = (uint)(DescIdBox.Value ?? 0);
            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
            _vm.ClassId = ClassIdBox.Value;
            _vm.PortraitId = (uint)(PortraitIdBox.Value ?? 0);
            _vm.MapFace = (uint)(MapFaceBox.Value ?? 0);
            _vm.Affinity = (uint)(AffinityBox.Value ?? 0);
            _vm.SortOrder = (uint)(SortOrderBox.Value ?? 0);
            _vm.Level = (uint)(LevelBox.Value ?? 0);

            // Base stats
            _vm.HP = (int)(HPBox.Value ?? 0);
            _vm.Str = (int)(StrBox.Value ?? 0);
            _vm.Skl = (int)(SklBox.Value ?? 0);
            _vm.Spd = (int)(SpdBox.Value ?? 0);
            _vm.Def = (int)(DefBox.Value ?? 0);
            _vm.Res = (int)(ResBox.Value ?? 0);
            _vm.Lck = (int)(LckBox.Value ?? 0);
            _vm.Con = (int)(ConBox.Value ?? 0);

            // Weapon ranks
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
            _vm.GrowSTR = (uint)(GrowSTRBox.Value ?? 0);
            _vm.GrowSKL = (uint)(GrowSKLBox.Value ?? 0);
            _vm.GrowSPD = (uint)(GrowSPDBox.Value ?? 0);
            _vm.GrowDEF = (uint)(GrowDEFBox.Value ?? 0);
            _vm.GrowRES = (uint)(GrowRESBox.Value ?? 0);
            _vm.GrowLCK = (uint)(GrowLCKBox.Value ?? 0);

            // Palette & anime
            _vm.LowerClassPalette = (uint)(LowerClassPaletteBox.Value ?? 0);
            _vm.UpperClassPalette = (uint)(UpperClassPaletteBox.Value ?? 0);
            _vm.LowerClassAnime = (uint)(LowerClassAnimeBox.Value ?? 0);
            _vm.UpperClassAnime = (uint)(UpperClassAnimeBox.Value ?? 0);
            _vm.Unk39 = (uint)(Unk39Box.Value ?? 0);

            // Ability flags
            _vm.Ability1 = (uint)(Ability1Box.Value ?? 0);
            _vm.Ability2 = (uint)(Ability2Box.Value ?? 0);
            _vm.Ability3 = (uint)(Ability3Box.Value ?? 0);
            _vm.Ability4 = (uint)(Ability4Box.Value ?? 0);

            // Support & talk
            _vm.SupportPtr = ParseHexText(SupportPtrBox.Text);
            _vm.TalkGroup = (uint)(TalkGroupBox.Value ?? 0);
            _vm.Unk49 = (uint)(Unk49Box.Value ?? 0);
            _vm.Unk50 = (uint)(Unk50Box.Value ?? 0);
            _vm.Unk51 = (uint)(Unk51Box.Value ?? 0);

            _undoService.Begin("Edit Unit (FE7)");
            try
            {
                _vm.WriteUnit();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Unit (FE7) data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        private static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        }

        // ---- #428: Address-bar infrastructure ------------------------------

        /// <summary>
        /// Populate the read-only address-bar labels (start address / count /
        /// size / hardcoding warning) from the live RomInfo. Mirrors WF
        /// UnitFE7Form's address bar (label1 / label2 / label3 / label22).
        /// </summary>
        void UpdateAddressBarInfra()
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                // #649: top-bar fields are properties on the unified
                // EditorTopBar control.
                if (TopBar != null)
                {
                    TopBar.StartAddressText = $"0x{rom.RomInfo.unit_pointer:X8}";
                    TopBar.ReadCountText = rom.RomInfo.unit_maxcount.ToString();
                }
                if (SizeLabel != null)
                    SizeLabel.Text = $"0x{rom.RomInfo.unit_datasize:X}";
            }
            catch (Exception ex) { Log.ErrorF("UnitFE7View.UpdateAddressBarInfra failed: {0}", ex.Message); }
        }

        /// <summary>
        /// #428 / #649: Reload routed event handler — wired from the unified
        /// EditorTopBar control. Mirrors WF UnitFE7Form's ReloadListButton.
        /// </summary>
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            LoadList();
            UpdateAddressBarInfra();
        }

        // ---- #428: HardCoding warning -------------------------------------

        /// <summary>
        /// Refresh the HardCoding-warning hyperlink's visibility based on the
        /// current unit's id. Mirrors WF UnitFE7Form.CheckHardCodingWarning.
        /// </summary>
        void RefreshHardCodingWarning()
        {
            try
            {
                // AddressList indices are 0-based; the WF "Unit id" (used by
                // the AsmMap cache lookup) is 1-based, matching the WF form.
                int idx = EntryList.SelectedOriginalIndex;
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
                Log.ErrorF("UnitFE7View.RefreshHardCodingWarning failed: {0}", ex.Message);
                HardCodingWarningLabel.IsVisible = false;
            }
        }

        /// <summary>
        /// #428: HardCoding label click — open the Patch Manager filtered to
        /// HARDCODING_UNIT=<id>. Mirrors WF UnitFE7Form.HardCodingWarningLabel_Click.
        /// </summary>
        void HardCodingWarning_Click(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0) return;
                uint unitId = (uint)(idx + 1);
                var pv = WindowManager.Instance.Open<PatchManagerView>();
                pv.JumpTo($"HARDCODING_UNIT={unitId:X2}", 0);
            }
            catch (Exception ex) { Log.ErrorF("UnitFE7View.HardCodingWarning_Click failed: {0}", ex.Message); }
        }

        // ---- #428: Weapon-rank letter labels -------------------------------

        /// <summary>
        /// Compute the letter grade ("-"/"E"/"D"/"C"/"B"/"A"/"S") for a
        /// weapon-rank NumericUpDown's current value and push it into the
        /// adjacent TextBlock. Mirrors WF InputFormRef.GetWeaponClass via
        /// the new Core WeaponRankUtil helper.
        /// </summary>
        void RefreshWeaponLetter(NumericUpDown box, TextBlock letterLabel)
        {
            try
            {
                int romVer = (int)(CoreState.ROM?.RomInfo?.version ?? 7);
                uint val = (uint)(box.Value ?? 0);
                letterLabel.Text = WeaponRankUtil.GetRankLetter(val, romVer);
            }
            catch { letterLabel.Text = "-"; }
        }

        // ---- #428: Growth Simulator panel ---------------------------------

        /// <summary>
        /// Re-run the growth simulator and push the result into the read-only
        /// SimXxx labels. Mirrors WF UnitFE7Form.X_SIM_ValueChanged. The
        /// magic-extends row is shown only when the FE7UMAGIC patch is
        /// installed (matches WF behavior).
        /// </summary>
        void RefreshGrowthSim()
        {
            if (_vm.IsLoading) return;
            try
            {
                // Push the latest box values back into the VM (the view's
                // existing wiring only does this on Write_Click).
                _vm.HP = (int)(HPBox.Value ?? 0);
                _vm.Str = (int)(StrBox.Value ?? 0);
                _vm.Skl = (int)(SklBox.Value ?? 0);
                _vm.Spd = (int)(SpdBox.Value ?? 0);
                _vm.Def = (int)(DefBox.Value ?? 0);
                _vm.Res = (int)(ResBox.Value ?? 0);
                _vm.Lck = (int)(LckBox.Value ?? 0);
                _vm.GrowHP = (uint)(GrowHPBox.Value ?? 0);
                _vm.GrowSTR = (uint)(GrowSTRBox.Value ?? 0);
                _vm.GrowSKL = (uint)(GrowSKLBox.Value ?? 0);
                _vm.GrowSPD = (uint)(GrowSPDBox.Value ?? 0);
                _vm.GrowDEF = (uint)(GrowDEFBox.Value ?? 0);
                _vm.GrowRES = (uint)(GrowRESBox.Value ?? 0);
                _vm.GrowLCK = (uint)(GrowLCKBox.Value ?? 0);
                _vm.Level = (uint)(LevelBox.Value ?? 0);
                _vm.ClassId = ClassIdBox.Value;

                int simLevel = (int)(SimLevelBox.Value ?? 0);
                var sim = _vm.BuildSimAndGrow(simLevel);
                SimHPLabel.Text = sim.sim_hp.ToString();
                SimSTRLabel.Text = sim.sim_str.ToString();
                SimSKLLabel.Text = sim.sim_skill.ToString();
                SimSPDLabel.Text = sim.sim_spd.ToString();
                SimDEFLabel.Text = sim.sim_def.ToString();
                SimRESLabel.Text = sim.sim_res.ToString();
                SimLCKLabel.Text = sim.sim_luck.ToString();
                SimTotalLabel.Text = sim.sim_sum_grow_rate.ToString();

                bool magicSplit = false;
                try { magicSplit = MagicSplitUtil.SearchMagicSplit() == MagicSplitUtil.magic_split_enum.FE7UMAGIC; }
                catch { /* magic-split is best-effort */ }
                SimMagicExtPrefixLabel.IsVisible = magicSplit;
                SimMagicExtLabel.IsVisible = magicSplit;
                if (magicSplit) SimMagicExtLabel.Text = sim.sim_ext_magic.ToString();
            }
            catch (Exception ex) { Log.ErrorF("UnitFE7View.RefreshGrowthSim failed: {0}", ex.Message); }
        }
    }
}
