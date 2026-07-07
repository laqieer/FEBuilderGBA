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
    public partial class UnitFE6View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly UnitFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Unit Editor (FE6)";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Unit Editor (FE6)", 934, 700, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public UnitFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;

            // Wire desc text live update
            DescIdBox.ValueChanged += OnDescIdChanged;

            // #358: jump to Support Unit Editor for this unit's support row.
            JumpToSupportUnitButton.Click += JumpToSupportUnit_Click;

            // #407: wire weapon-rank NumericUpDowns -> letter labels.
            WepSwordBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepSwordBox, WepSwordLetterLabel);
            WepLanceBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepLanceBox, WepLanceLetterLabel);
            WepAxeBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepAxeBox, WepAxeLetterLabel);
            WepBowBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepBowBox, WepBowLetterLabel);
            WepStaffBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepStaffBox, WepStaffLetterLabel);
            WepAnimaBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepAnimaBox, WepAnimaLetterLabel);
            WepLightBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepLightBox, WepLightLetterLabel);
            WepDarkBox.ValueChanged += (_, _) => RefreshWeaponLetter(WepDarkBox, WepDarkLetterLabel);

            // #407: wire growth-sim recompute on base / growth / class / sim-level changes.
            HPBox.ValueChanged += (_, _) => RefreshGrowthSim();
            StrBox.ValueChanged += (_, _) => RefreshGrowthSim();
            SklBox.ValueChanged += (_, _) => RefreshGrowthSim();
            SpdBox.ValueChanged += (_, _) => RefreshGrowthSim();
            DefBox.ValueChanged += (_, _) => RefreshGrowthSim();
            ResBox.ValueChanged += (_, _) => RefreshGrowthSim();
            LckBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowHPBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowStrBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowSklBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowSpdBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowDefBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowResBox.ValueChanged += (_, _) => RefreshGrowthSim();
            GrowLckBox.ValueChanged += (_, _) => RefreshGrowthSim();
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
        /// #358 — Open the FE6 Support Unit Editor and select the support row
        /// that this unit's +44 pointer references.  Mirrors WinForms
        /// <c>J_44_SUPPORTUNIT</c> in <c>UnitFE6Form</c>.
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
                    // message instead. Same fix as UnitEditorView + UnitFE7View.
                    CoreState.Services?.ShowInfo("This unit has no support data (support pointer is 0).");
                    return;
                }
                var window = WindowManager.Instance.Open<SupportUnitFE6View>();
                window.JumpToAddr(supportPtr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("UnitFE6View.JumpToSupportUnit failed: {0}", ex.Message);
            }
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadUnitList();
                EntryList.SetItemsWithIcons(items, index => ListIconLoaders.UnitPortraitLoader(items, index));
            }
            catch (Exception ex)
            {
                Log.ErrorF("UnitFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadUnit(addr);
                UpdateUI();
                TryShowPortrait();
                _vm.IsLoading = false;

                // #407: post-selection UI refresh — HardCoding warning visibility,
                // weapon letters, growth-sim default level + initial display.
                RefreshHardCodingWarning();
                RefreshAllWeaponLetters();
                ResetSimLevelToClassMax();
                RefreshGrowthSim();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("UnitFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #407: refresh every weapon-rank letter label (after a new unit loads).
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
        /// #407: when a new unit is selected, default the SimLevel to the
        /// max level for the unit's class (matches WF UnitFE6Form's
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
            NameLabel.Text = _vm.Name;

            // Identity
            NameIdBox.Value = _vm.NameId;
            DescIdBox.Value = _vm.DescId;
            DescTextLabel.Text = _vm.DescText;
            UnitIdBox.Value = _vm.UnitId;
            ClassIdBox.Value = _vm.ClassId;
            // Push VM-resolved class name; ValueChanged also refreshes on edit.
            try { ClassIdBox.NameText = NameResolver.GetClassName(_vm.ClassId); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
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

            // Weapon levels
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

            // Ability flags
            Ability1Box.Value = _vm.Ability1;
            Ability2Box.Value = _vm.Ability2;
            Ability3Box.Value = _vm.Ability3;
            Ability4Box.Value = _vm.Ability4;

            // Support pointer (hex)
            SupportPtrBox.Text = $"0x{_vm.SupportPtr:X08}";
        }

        void ReadFromUI()
        {
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

            // Ability flags
            _vm.Ability1 = (uint)(Ability1Box.Value ?? 0);
            _vm.Ability2 = (uint)(Ability2Box.Value ?? 0);
            _vm.Ability3 = (uint)(Ability3Box.Value ?? 0);
            _vm.Ability4 = (uint)(Ability4Box.Value ?? 0);

            // Support pointer (parse hex) - #648: ViewHelpers.ParseHexText
            // handles the displayed "0x..." form; U.atoh would zero out the
            // pointer on every Write.
            _vm.SupportPtr = ViewHelpers.ParseHexText(SupportPtrBox.Text);
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
                Log.ErrorF("UnitFE6View.TryShowPortrait failed: {0}", ex.Message);
                PortraitImage.SetImage(null);
            }
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
        /// Compute the ClassFE6View ROM address for the given class index.
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
                WindowManager.Instance.Navigate<ClassFE6View>(addr);
            }
            catch (Exception ex) { Log.ErrorF("UnitFE6View.ClassId_Jump failed: {0}", ex.Message); }
        }

        async void ClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<ClassFE6View>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null)
                {
                    ClassIdBox.Value = (uint)result.Index;
                    // NameText refresh via ValueChanged.
                }
            }
            catch (Exception ex) { Log.ErrorF("UnitFE6View.ClassId_Pick failed: {0}", ex.Message); }
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
            ReadFromUI();
            // #407: wrap ROM writes in an undo group so the Undo button can roll back.
            _undoService.Begin("Edit Unit (FE6)");
            try
            {
                _vm.WriteUnit();
                _undoService.Commit();
                CoreState.Services?.ShowInfo("Unit data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("UnitFE6View.Write_Click failed: {0}", ex.Message);
            }
        }

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Rollback();
            if (_vm.CurrentAddr != 0)
            {
                _vm.LoadUnit(_vm.CurrentAddr);
                UpdateUI();
            }
        }

        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        // ---- #407: Address-bar infrastructure ------------------------------

        /// <summary>
        /// Populate the read-only address-bar labels (start address / count /
        /// size) from the live RomInfo. Matches WF UnitFE6Form's
        /// <c>InputFormRef.ReInit(p32(unit_pointer) + unit_datasize)</c> baseline:
        /// the displayed Read Start Address is the dereferenced data-table
        /// start, with the first (placeholder) entry skipped.
        ///
        /// Guards the pointer dereference against unset / out-of-range values
        /// the same way <see cref="UnitFE6ViewModel.LoadUnitList"/> does — when
        /// the ROM doesn't have a sensible <c>unit_pointer</c>, we leave the
        /// labels blank rather than display a bogus offset.
        /// </summary>
        void UpdateAddressBarInfra()
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint dataSize = rom.RomInfo.unit_datasize;
                // #649: top-bar fields are properties on the unified
                // EditorTopBar control; SizeLabel still lives on the right pane.
                if (SizeLabel != null)
                    SizeLabel.Text = $"0x{dataSize:X}";
                if (TopBar != null)
                    TopBar.ReadCountText = rom.RomInfo.unit_maxcount.ToString();

                // Mirror the LoadUnitList safety checks before dereferencing:
                // (1) the pointer field itself must be non-zero, and
                // (2) the dereferenced data-table start must be a safe offset.
                uint ptr = rom.RomInfo.unit_pointer;
                if (ptr == 0)
                {
                    if (TopBar != null) TopBar.StartAddressText = "";
                    return;
                }
                uint tableStart = rom.p32(ptr);
                if (!U.isSafetyOffset(tableStart, rom))
                {
                    if (TopBar != null) TopBar.StartAddressText = "";
                    return;
                }
                // FE6 InputFormRef.ReInit(p32(unit_pointer) + unit_datasize)
                // skips entry 0 (the null/placeholder unit). Guard against
                // uint wrap (tableStart + dataSize overflowing) and verify
                // the resulting baseAddr still lands inside the ROM before
                // displaying it — blank the label otherwise so we never
                // surface a bogus/wrapped offset.
                ulong baseAddrChecked = (ulong)tableStart + (ulong)dataSize;
                if (baseAddrChecked > uint.MaxValue)
                {
                    if (TopBar != null) TopBar.StartAddressText = "";
                    return;
                }
                uint baseAddr = (uint)baseAddrChecked;
                if (!U.isSafetyOffset(baseAddr, rom))
                {
                    if (TopBar != null) TopBar.StartAddressText = "";
                    return;
                }
                if (TopBar != null) TopBar.StartAddressText = $"0x{baseAddr:X8}";
            }
            catch (Exception ex) { Log.ErrorF("UnitFE6View.UpdateAddressBarInfra failed: {0}", ex.Message); }
        }

        /// <summary>
        /// #407 / #649: Reload routed event handler — wired from the
        /// unified EditorTopBar control. Mirrors WF UnitFE6Form's
        /// ReloadListButton click handler.
        /// </summary>
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            LoadList();
            UpdateAddressBarInfra();
        }

        // ---- #407: HardCoding warning -------------------------------------

        /// <summary>
        /// Refresh the HardCoding-warning hyperlink's visibility based on the
        /// current unit's id. Mirrors WF UnitFE6Form.CheckHardCodingWarning.
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
                Log.ErrorF("UnitFE6View.RefreshHardCodingWarning failed: {0}", ex.Message);
                HardCodingWarningLabel.IsVisible = false;
            }
        }

        /// <summary>
        /// #407: HardCoding label click — open the Patch Manager filtered to
        /// HARDCODING_UNIT=&lt;id&gt;. Mirrors WF UnitFE6Form.HardCodingWarningLabel_Click.
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
            catch (Exception ex) { Log.ErrorF("UnitFE6View.HardCodingWarning_Click failed: {0}", ex.Message); }
        }

        // ---- #407: Weapon-rank letter labels -------------------------------

        /// <summary>
        /// Compute the letter grade ("-"/"E"/"D"/"C"/"B"/"A"/"S") for a
        /// weapon-rank NumericUpDown's current value and push it into the
        /// adjacent TextBlock. Mirrors WF InputFormRef.GetWeaponClass via
        /// the Core WeaponRankUtil helper. Uses FE6 thresholds (1-50=E,
        /// 51-100=D, 101-150=C, 151-200=B, 201-250=A, 251+=S).
        /// </summary>
        void RefreshWeaponLetter(NumericUpDown box, TextBlock letterLabel)
        {
            try
            {
                uint val = (uint)(box.Value ?? 0);
                // FE6-specific thresholds — pass romVersion=6 explicitly so
                // this stays correct even when the ROM is unloaded in tests.
                letterLabel.Text = WeaponRankUtil.GetRankLetter(val, 6);
            }
            catch { letterLabel.Text = "-"; }
        }

        // ---- #407: Growth Simulator panel ---------------------------------

        /// <summary>
        /// Re-run the growth simulator and push the result into the read-only
        /// SimXxx labels. Mirrors WF UnitFE6Form.X_SIM_ValueChanged. FE6 has
        /// no magic-split patch so no Magic Ext row.
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
                _vm.GrowStr = (uint)(GrowStrBox.Value ?? 0);
                _vm.GrowSkl = (uint)(GrowSklBox.Value ?? 0);
                _vm.GrowSpd = (uint)(GrowSpdBox.Value ?? 0);
                _vm.GrowDef = (uint)(GrowDefBox.Value ?? 0);
                _vm.GrowRes = (uint)(GrowResBox.Value ?? 0);
                _vm.GrowLck = (uint)(GrowLckBox.Value ?? 0);
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
            }
            catch (Exception ex) { Log.ErrorF("UnitFE6View.RefreshGrowthSim failed: {0}", ex.Message); }
        }
    }
}
