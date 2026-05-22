using System;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ClassEditorView : TranslatedWindow, IPickableEditor, IDataVerifiableView
    {
        readonly ClassEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => R._("Class Editor");
        public bool IsLoaded => _vm.CanWrite;

        public event Action<PickResult>? SelectionConfirmed;

        public ClassEditorView()
        {
            InitializeComponent();
            ClassList.SelectedAddressChanged += OnClassSelected;
            ClassList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
            Opened += (_, _) => LoadList();

            // Ability flag names are set in LoadList() based on ROM version

            // Auto-recalculate growth sim when SimLevel or growth/base stat boxes change
            SimLevelBox.ValueChanged += OnGrowthInputChanged;
            // Wire growth rate and base stat boxes for auto-recalc
            GrowHpBox.ValueChanged += OnGrowthInputChanged;
            GrowStrBox.ValueChanged += OnGrowthInputChanged;
            GrowSklBox.ValueChanged += OnGrowthInputChanged;
            GrowSpdBox.ValueChanged += OnGrowthInputChanged;
            GrowDefBox.ValueChanged += OnGrowthInputChanged;
            GrowResBox.ValueChanged += OnGrowthInputChanged;
            GrowLckBox.ValueChanged += OnGrowthInputChanged;
            BaseHpBox.ValueChanged += OnGrowthInputChanged;
            BaseStrBox.ValueChanged += OnGrowthInputChanged;
            BaseSklBox.ValueChanged += OnGrowthInputChanged;
            BaseSpdBox.ValueChanged += OnGrowthInputChanged;
            BaseDefBox.ValueChanged += OnGrowthInputChanged;
            BaseResBox.ValueChanged += OnGrowthInputChanged;

            // Wire desc text live update
            DescIdBox.ValueChanged += OnDescIdChanged;

            // Wire weapon rank label updates
            B44Box.ValueChanged += OnWeaponValueChanged;
            B45Box.ValueChanged += OnWeaponValueChanged;
            B46Box.ValueChanged += OnWeaponValueChanged;
            B47Box.ValueChanged += OnWeaponValueChanged;
            B48Box.ValueChanged += OnWeaponValueChanged;
            B49Box.ValueChanged += OnWeaponValueChanged;
            B50Box.ValueChanged += OnWeaponValueChanged;
            B51Box.ValueChanged += OnWeaponValueChanged;

            // Wire class-card live updates (issue #357): when the user edits
            // the portrait or wait-icon field, refresh the card images.
            PortraitIdBox.ValueChanged += OnClassCardInputChanged;
            WaitIconBox.ValueChanged   += OnClassCardInputChanged;
        }

        void LoadList()
        {
            try
            {
                // Configure version-specific UI BEFORE setting items,
                // because SetItems() auto-selects the first item which triggers
                // OnClassSelected -> LoadClass -> UpdateUI. We need correct labels
                // and visibility before that first render.
                ConfigureVersionUI();

                var items = _vm.LoadClassList();
                ClassList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));

                // Show "Edit Skills" button if a skill system with class assignment is installed
                var skillType = PatchDetectionService.Instance.SkillSystem;
                EditSkillsButton.IsVisible =
                    skillType == PatchDetectionService.SkillSystemType.SkillSystem ||
                    skillType == PatchDetectionService.SkillSystemType.CSkillSys09x ||
                    skillType == PatchDetectionService.SkillSystemType.CSkillSys300;
            }
            catch (Exception ex)
            {
                Log.Error("ClassEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Configure UI elements based on ROM version. FE6 has a 72-byte class struct
        /// with different offsets and fewer fields compared to FE7/8 (84 bytes).
        /// </summary>
        void ConfigureVersionUI()
        {
            int version = CoreState.ROM?.RomInfo?.version ?? 8;

            // Set version-aware ability flag names
            Ability1Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(version, 1));
            Ability2Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(version, 2));
            Ability3Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(version, 3));
            Ability4Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(version, 4));

            bool isFE6 = version == 6;

            // FE6 has terrain avoid/def/res at +56/+60/+64 (mapped to the "rain/snow" and terrain controls)
            // FE7/8 has separate terrain pointers at +68/+72/+76 shown in TerrainRow and D80Row
            // For FE6: show TerrainRow (repurposed for terrain res) but hide D80Row (no D80 field)
            if (TerrainRow != null) TerrainRow.IsVisible = true;  // all versions need it
            if (D80Row != null) D80Row.IsVisible = !isFE6;

            // FE6: promo gains only have HP and Str (b34-b35); Skl/Spd/Def/Res don't exist
            if (PromoSklLabel != null) PromoSklLabel.IsVisible = !isFE6;
            if (PromoSklBox != null) PromoSklBox.IsVisible = !isFE6;
            if (PromoSpdLabel != null) PromoSpdLabel.IsVisible = !isFE6;
            if (PromoSpdBox != null) PromoSpdBox.IsVisible = !isFE6;
            if (PromoDefLabel != null) PromoDefLabel.IsVisible = !isFE6;
            if (PromoDefBox != null) PromoDefBox.IsVisible = !isFE6;
            if (PromoResLabel != null) PromoResLabel.IsVisible = !isFE6;
            if (PromoResBox != null) PromoResBox.IsVisible = !isFE6;

            if (isFE6)
            {
                // FE6 class struct: ability at +36, weapon ranks at +40, battle anime at +48
                // Ability flag labels
                if (AbilityHeaderText1 != null) AbilityHeaderText1.Text = "Ability 1 (B36):";
                if (AbilityHeaderText2 != null) AbilityHeaderText2.Text = "Ability 2 (B37):";
                if (AbilityHeaderText3 != null) AbilityHeaderText3.Text = "Ability 3 (B38):";
                if (AbilityHeaderText4 != null) AbilityHeaderText4.Text = "Ability 4 (B39):";

                // Weapon rank labels: FE6 uses B40-B47
                if (WepRankExpander != null) WepRankExpander.Header = "Weapon Rank Levels (B40-B47)";
                if (WepRankSwordLabel != null) WepRankSwordLabel.Text = "Sword (B40):";
                if (WepRankLanceLabel != null) WepRankLanceLabel.Text = "Lance (B41):";
                if (WepRankAxeLabel != null) WepRankAxeLabel.Text = "Axe (B42):";
                if (WepRankBowLabel != null) WepRankBowLabel.Text = "Bow (B43):";
                if (WepRankStaffLabel != null) WepRankStaffLabel.Text = "Staff (B44):";
                if (WepRankAnimaLabel != null) WepRankAnimaLabel.Text = "Anima (B45):";
                if (WepRankLightLabel != null) WepRankLightLabel.Text = "Light (B46):";
                if (WepRankDarkLabel != null) WepRankDarkLabel.Text = "Dark (B47):";

                // Pointer labels: FE6 battle anime at P48, move cost at P52,
                // terrain avoid/def/res at P56/P60/P64 (per WinForms ClassFE6Form)
                if (BattleAnimeLabel != null) BattleAnimeLabel.Text = "Battle Anime (P48):";
                if (MoveCostLabel != null) MoveCostLabel.Text = "Move Cost (P52):";
                if (MoveCostRainLabel != null) MoveCostRainLabel.Text = "Terrain Avoid (P56):";
                if (MoveCostSnowLabel != null) MoveCostSnowLabel.Text = "Terrain Def (P60):";

                // TerrainRow: repurpose first slot for Terrain Res (P64), hide second pair
                if (TerrainAvoidLabel != null) TerrainAvoidLabel.Text = "Terrain Res (P64):";
                if (TerrainDefLabel != null) TerrainDefLabel.IsVisible = false;
                if (Ptr72Box != null) Ptr72Box.IsVisible = false;
            }
            else
            {
                // FE7/8: reset labels to defaults in case they were set to FE6 values previously
                if (AbilityHeaderText1 != null) AbilityHeaderText1.Text = "Ability 1 (B40):";
                if (AbilityHeaderText2 != null) AbilityHeaderText2.Text = "Ability 2 (B41):";
                if (AbilityHeaderText3 != null) AbilityHeaderText3.Text = "Ability 3 (B42):";
                if (AbilityHeaderText4 != null) AbilityHeaderText4.Text = "Ability 4 (B43):";

                if (WepRankExpander != null) WepRankExpander.Header = "Weapon Rank Levels (B44-B51)";
                if (WepRankSwordLabel != null) WepRankSwordLabel.Text = "Sword (B44):";
                if (WepRankLanceLabel != null) WepRankLanceLabel.Text = "Lance (B45):";
                if (WepRankAxeLabel != null) WepRankAxeLabel.Text = "Axe (B46):";
                if (WepRankBowLabel != null) WepRankBowLabel.Text = "Bow (B47):";
                if (WepRankStaffLabel != null) WepRankStaffLabel.Text = "Staff (B48):";
                if (WepRankAnimaLabel != null) WepRankAnimaLabel.Text = "Anima (B49):";
                if (WepRankLightLabel != null) WepRankLightLabel.Text = "Light (B50):";
                if (WepRankDarkLabel != null) WepRankDarkLabel.Text = "Dark (B51):";

                if (BattleAnimeLabel != null) BattleAnimeLabel.Text = "Battle Anime (P52):";
                if (MoveCostLabel != null) MoveCostLabel.Text = "Move Cost (P56):";
                if (MoveCostRainLabel != null) MoveCostRainLabel.Text = "Move Cost Rain (P60):";
                if (MoveCostSnowLabel != null) MoveCostSnowLabel.Text = "Move Cost Snow (P64):";

                // TerrainRow: restore defaults for FE7/8
                if (TerrainAvoidLabel != null) TerrainAvoidLabel.Text = "Terrain Avoid (P68):";
                if (TerrainDefLabel != null) TerrainDefLabel.IsVisible = true;
                if (Ptr72Box != null) Ptr72Box.IsVisible = true;
            }
        }

        void OnClassSelected(uint addr)
        {
            try
            {
                _vm.LoadClass(addr);
                UpdateUI();
                TryShowListPreview();
                UpdateClassCard();
                UpdateWarnings();
            }
            catch (Exception ex)
            {
                Log.Error("ClassEditorView.OnClassSelected failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address)
        {
            ClassList.SelectAddress(address);
        }

        void UpdateUI()
        {
            // Suppress growth-input-changed handlers during bulk UI update.
            // Without this guard, setting e.g. GrowHpBox.Value fires OnGrowthInputChanged,
            // which calls SyncGrowthInputsToVm() and reads GrowResBox (still at its old/default
            // value), overwriting the ViewModel's GrowRes back to 0.
            _vm.IsLoading = true;
            try
            {
                AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
                NameLabel.Text = _vm.Name;

                // Identity
                NameIdBox.Value = _vm.NameId;
                DescIdBox.Value = _vm.DescId;
                DescTextLabel.Text = _vm.DescText;
                ClassNumberBox.Value = _vm.ClassNumber;
                PromotionLevelBox.Value = _vm.PromotionLevel;
                WaitIconBox.Value = _vm.WaitIcon;
                WalkSpeedBox.Value = _vm.WalkSpeed;
                PortraitIdBox.Value = _vm.PortraitId;
                SortOrderBox.Value = _vm.SortOrder;

                // Base stats
                BaseHpBox.Value = _vm.BaseHp;
                BaseStrBox.Value = _vm.BaseStr;
                BaseSklBox.Value = _vm.BaseSkl;
                BaseSpdBox.Value = _vm.BaseSpd;
                BaseDefBox.Value = _vm.BaseDef;
                BaseResBox.Value = _vm.BaseRes;
                BaseConBox.Value = _vm.BaseCon;
                BaseMovBox.Value = _vm.BaseMov;

                // Stat caps
                MaxHpBox.Value = _vm.MaxHp;
                MaxStrBox.Value = _vm.MaxStr;
                MaxSklBox.Value = _vm.MaxSkl;
                MaxSpdBox.Value = _vm.MaxSpd;
                MaxDefBox.Value = _vm.MaxDef;
                MaxResBox.Value = _vm.MaxRes;
                MaxConBox.Value = _vm.MaxCon;
                ClassPowerBox.Value = _vm.ClassPower;

                // Growth rates
                GrowHpBox.Value = _vm.GrowHp;
                GrowStrBox.Value = _vm.GrowStr;
                GrowSklBox.Value = _vm.GrowSkl;
                GrowSpdBox.Value = _vm.GrowSpd;
                GrowDefBox.Value = _vm.GrowDef;
                GrowResBox.Value = _vm.GrowRes;
                GrowLckBox.Value = _vm.GrowLck;

                // Promotion gains
                PromoHpBox.Value = _vm.PromoHp;
                PromoStrBox.Value = _vm.PromoStr;
                PromoSklBox.Value = _vm.PromoSkl;
                PromoSpdBox.Value = _vm.PromoSpd;
                PromoDefBox.Value = _vm.PromoDef;
                PromoResBox.Value = _vm.PromoRes;

                // Abilities (BitFlagPanel)
                Ability1Flags.Value = (byte)_vm.Ability1;
                Ability2Flags.Value = (byte)_vm.Ability2;
                Ability3Flags.Value = (byte)_vm.Ability3;
                Ability4Flags.Value = (byte)_vm.Ability4;

                // Weapon rank levels
                B44Box.Value = _vm.WepRankSword;
                B45Box.Value = _vm.WepRankLance;
                B46Box.Value = _vm.WepRankAxe;
                B47Box.Value = _vm.WepRankBow;
                B48Box.Value = _vm.WepRankStaff;
                B49Box.Value = _vm.WepRankAnima;
                B50Box.Value = _vm.WepRankLight;
                B51Box.Value = _vm.WepRankDark;

                UpdateWeaponRankLabels();

                // Pointers — layout differs between FE6 and FE7/8.
                // FE6: P48=BattleAnime, P52=MoveCost, P56=TerrainAvoid, P60=TerrainDef, P64=TerrainRes
                // FE7/8: P52=BattleAnime, P56=MoveCost, P60=MoveCostRain, P64=MoveCostSnow,
                //        P68=TerrainAvoid, P72=TerrainDef, P76=TerrainRes, D80=Unknown
                Ptr52Box.Text = $"0x{_vm.BattleAnimePtr:X08}";
                Ptr56Box.Text = $"0x{_vm.MoveCostPtr:X08}";
                if (_vm.IsFE6)
                {
                    // FE6: Ptr60=TerrainAvoid(+56), Ptr64=TerrainDef(+60), Ptr68=TerrainRes(+64)
                    Ptr60Box.Text = $"0x{_vm.TerrainAvoidPtr:X08}";
                    Ptr64Box.Text = $"0x{_vm.TerrainDefPtr:X08}";
                    Ptr68Box.Text = $"0x{_vm.TerrainResPtr:X08}";
                    Ptr72Box.Text = "";
                    Ptr76Box.Text = "";
                    D80Box.Text = $"0x{_vm.UnknownD80:X08}";
                }
                else
                {
                    Ptr60Box.Text = $"0x{_vm.MoveCostRainPtr:X08}";
                    Ptr64Box.Text = $"0x{_vm.MoveCostSnowPtr:X08}";
                    Ptr68Box.Text = $"0x{_vm.TerrainAvoidPtr:X08}";
                    Ptr72Box.Text = $"0x{_vm.TerrainDefPtr:X08}";
                    Ptr76Box.Text = $"0x{_vm.TerrainResPtr:X08}";
                    D80Box.Text = $"0x{_vm.UnknownD80:X08}";
                }

                // Auto-calculate growth on entry load
                SimLevelBox.Value = _vm.SimLevel;
            }
            finally
            {
                // Finished bulk UI update — re-enable change handlers even if an exception occurred.
                _vm.IsLoading = false;
            }

            _vm.CalculateGrowth();
            GrowthSimLabel.Text = _vm.GrowthSimText;
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
            catch (Exception ex)
            {
                Log.Error($"OnPortraitLinkClick failed: {ex.Message}");
            }
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

        void OnWeaponValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            UpdateWeaponRankLabels();
        }

        void UpdateWeaponRankLabels()
        {
            // Weapon rank levels (B44-B51)
            B44RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B44Box.Value ?? 0));
            B45RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B45Box.Value ?? 0));
            B46RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B46Box.Value ?? 0));
            B47RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B47Box.Value ?? 0));
            B48RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B48Box.Value ?? 0));
            B49RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B49Box.Value ?? 0));
            B50RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B50Box.Value ?? 0));
            B51RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B51Box.Value ?? 0));
        }

        void TryShowListPreview()
        {
            try
            {
                var img = PreviewIconHelper.LoadClassWaitIcon(_vm.WaitIcon);
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

        /// <summary>
        /// Refresh the class-card preview (issue #357). Mirrors WinForms
        /// <c>ClassForm</c> top-right block — class face portrait
        /// (<c>L_8_PORTRAIT_CLASS</c>) + class name (<c>L_5_CLASS</c>) +
        /// class wait icon (<c>L_6_CLASSICONSRC</c>). Reads the current
        /// portrait id and wait icon from the NumericUpDown controls so
        /// edits propagate live (matching WinForms <c>InputFormRef</c>'s
        /// <c>ValueChanged</c>-driven linktype refresh).
        /// </summary>
        void UpdateClassCard()
        {
            try
            {
                uint portraitId = (uint)(PortraitIdBox.Value ?? 0);
                uint waitIcon = (uint)(WaitIconBox.Value ?? 0);

                // `using` so the IDisposable IImage temporaries are released
                // even if SetImage / Avalonia bitmap conversion throws.
                // PR #471 Copilot inline review fix.
                using var facePic = PreviewIconHelper.LoadClassFacePortrait(portraitId);
                using var waitPic = PreviewIconHelper.LoadClassWaitIcon(waitIcon);

                CardPortraitImage.SetImage(facePic);
                CardWaitIconImage.SetImage(waitPic);

                CardNameLabel.Text = _vm.Name ?? "";
                CardIdLabel.Text = $"0x{_vm.CurrentAddr:X08}  /  ID {_vm.ClassNumber}";
                ClassCardBorder.IsVisible = true;
            }
            catch (Exception ex)
            {
                // Log the full exception (stack trace + inner) so UI/ROM
                // rendering failures are diagnosable from logs.
                // PR #471 Copilot inline review fix.
                Log.Error("UpdateClassCard failed: " + ex.ToString());
                CardPortraitImage.SetImage(null);
                CardWaitIconImage.SetImage(null);
                ClassCardBorder.IsVisible = false;
            }
        }

        /// <summary>
        /// Live-refresh the class card when the user edits PortraitIdBox or
        /// WaitIconBox. Skipped during bulk UI loads to avoid redundant work.
        /// </summary>
        void OnClassCardInputChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            UpdateClassCard();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.DescId = (uint)(DescIdBox.Value ?? 0);
            _vm.ClassNumber = (uint)(ClassNumberBox.Value ?? 0);
            _vm.PromotionLevel = (uint)(PromotionLevelBox.Value ?? 0);
            _vm.WaitIcon = (uint)(WaitIconBox.Value ?? 0);
            _vm.WalkSpeed = (uint)(WalkSpeedBox.Value ?? 0);
            _vm.PortraitId = (uint)(PortraitIdBox.Value ?? 0);
            _vm.SortOrder = (uint)(SortOrderBox.Value ?? 0);

            _vm.BaseHp = (uint)(BaseHpBox.Value ?? 0);
            _vm.BaseStr = (uint)(BaseStrBox.Value ?? 0);
            _vm.BaseSkl = (uint)(BaseSklBox.Value ?? 0);
            _vm.BaseSpd = (uint)(BaseSpdBox.Value ?? 0);
            _vm.BaseDef = (uint)(BaseDefBox.Value ?? 0);
            _vm.BaseRes = (uint)(BaseResBox.Value ?? 0);
            _vm.BaseCon = (uint)(BaseConBox.Value ?? 0);
            _vm.BaseMov = (uint)(BaseMovBox.Value ?? 0);

            _vm.MaxHp = (uint)(MaxHpBox.Value ?? 0);
            _vm.MaxStr = (uint)(MaxStrBox.Value ?? 0);
            _vm.MaxSkl = (uint)(MaxSklBox.Value ?? 0);
            _vm.MaxSpd = (uint)(MaxSpdBox.Value ?? 0);
            _vm.MaxDef = (uint)(MaxDefBox.Value ?? 0);
            _vm.MaxRes = (uint)(MaxResBox.Value ?? 0);
            _vm.MaxCon = (uint)(MaxConBox.Value ?? 0);
            _vm.ClassPower = (uint)(ClassPowerBox.Value ?? 0);

            _vm.GrowHp = (uint)(GrowHpBox.Value ?? 0);
            _vm.GrowStr = (uint)(GrowStrBox.Value ?? 0);
            _vm.GrowSkl = (uint)(GrowSklBox.Value ?? 0);
            _vm.GrowSpd = (uint)(GrowSpdBox.Value ?? 0);
            _vm.GrowDef = (uint)(GrowDefBox.Value ?? 0);
            _vm.GrowRes = (uint)(GrowResBox.Value ?? 0);
            _vm.GrowLck = (uint)(GrowLckBox.Value ?? 0);

            _vm.PromoHp = (int)(PromoHpBox.Value ?? 0);
            _vm.PromoStr = (int)(PromoStrBox.Value ?? 0);
            _vm.PromoSkl = (int)(PromoSklBox.Value ?? 0);
            _vm.PromoSpd = (int)(PromoSpdBox.Value ?? 0);
            _vm.PromoDef = (int)(PromoDefBox.Value ?? 0);
            _vm.PromoRes = (int)(PromoResBox.Value ?? 0);

            // Abilities from BitFlagPanel
            _vm.Ability1 = Ability1Flags.Value;
            _vm.Ability2 = Ability2Flags.Value;
            _vm.Ability3 = Ability3Flags.Value;
            _vm.Ability4 = Ability4Flags.Value;

            _vm.WepRankSword = (uint)(B44Box.Value ?? 0);
            _vm.WepRankLance = (uint)(B45Box.Value ?? 0);
            _vm.WepRankAxe = (uint)(B46Box.Value ?? 0);
            _vm.WepRankBow = (uint)(B47Box.Value ?? 0);
            _vm.WepRankStaff = (uint)(B48Box.Value ?? 0);
            _vm.WepRankAnima = (uint)(B49Box.Value ?? 0);
            _vm.WepRankLight = (uint)(B50Box.Value ?? 0);
            _vm.WepRankDark = (uint)(B51Box.Value ?? 0);

            _vm.BattleAnimePtr = ParseHexText(Ptr52Box.Text);
            _vm.MoveCostPtr = ParseHexText(Ptr56Box.Text);
            if (_vm.IsFE6)
            {
                // FE6: Ptr60=TerrainAvoid, Ptr64=TerrainDef, Ptr68=TerrainRes
                _vm.MoveCostRainPtr = 0;
                _vm.MoveCostSnowPtr = 0;
                _vm.TerrainAvoidPtr = ParseHexText(Ptr60Box.Text);
                _vm.TerrainDefPtr = ParseHexText(Ptr64Box.Text);
                _vm.TerrainResPtr = ParseHexText(Ptr68Box.Text);
            }
            else
            {
                _vm.MoveCostRainPtr = ParseHexText(Ptr60Box.Text);
                _vm.MoveCostSnowPtr = ParseHexText(Ptr64Box.Text);
                _vm.TerrainAvoidPtr = ParseHexText(Ptr68Box.Text);
                _vm.TerrainDefPtr = ParseHexText(Ptr72Box.Text);
                _vm.TerrainResPtr = ParseHexText(Ptr76Box.Text);
            }
            _vm.UnknownD80 = ParseHexText(D80Box.Text);

            _undoService.Begin(R._("Edit Class"));
            try
            {
                _vm.WriteClass();
                _undoService.Commit();
                _vm.MarkClean();
                UpdateWarnings();
                CoreState.Services.ShowInfo(R._("Class data written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ClassEditorView.Write_Click failed: {0}", ex.Message);
            }
        }

        void JumpToMoveCost_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint ptr = ParseHexText(Ptr56Box.Text);
                if (!ShouldJumpToMoveCost(ptr, _vm.CurrentAddr)) return;

                // Pass the CURRENT CLASS address — MoveCostEditorView's ClassList
                // contains class addresses and resolves the move-cost pointer internally.
                // Previously this passed the move-cost-table offset, which never matches
                // any class in the list and silently fell back to entry 0 (issue #344).
                WindowManager.Instance.Navigate<MoveCostEditorView>(_vm.CurrentAddr);
            }
            catch (Exception ex)
            {
                Log.Error("JumpToMoveCost failed: {0}", ex.Message);
            }
        }

        // Issue #344 defensive gates for the Move Cost Jump button. Extracted
        // for unit testing without a live UI: the displayed move-cost pointer
        // must be a real GBA pointer AND its ROM offset must be inside the
        // loaded image AND a class must currently be loaded. The same textbox
        // (Ptr56Box) is used by both FE7/FE8 (where it represents P56) and
        // FE6 (where it represents P52) — the gate is layout-agnostic and
        // works for both versions.
        internal static bool ShouldJumpToMoveCost(uint moveCostPtr, uint currentClassAddr)
        {
            if (!U.isPointer(moveCostPtr)) return false;
            // U.isPointer only checks the GBA pointer range — also reject
            // pointers whose ROM offset is outside the loaded image (e.g.
            // dangling pointer in corrupted/unmapped class data).
            if (!U.isSafetyOffset(U.toOffset(moveCostPtr))) return false;
            if (currentClassAddr == 0) return false;
            return true;
        }

        // ============================================================
        // Issue #359 — Jump handlers for the remaining Pointers/Movement/
        // Terrain fields. Each dispatches to MoveCostEditorView with the
        // CURRENT CLASS address + the cost type appropriate for the
        // textbox. The mapping is version-aware because FE6 reuses
        // Ptr60/Ptr64/Ptr68 for Terrain Avoid/Def/Res (P56/P60/P64 in the
        // FE6 class struct), while FE7/8 uses Ptr60 for Move Cost Rain
        // (P60), Ptr64 for Move Cost Snow (P64), and Ptr68/Ptr72/Ptr76
        // for Terrain Avoid/Def/Res (P68/P72/P76). The boxes themselves
        // are physically the same controls; ConfigureVersionUI hides
        // Ptr72/Ptr76 for FE6 and adjusts the labels.
        //
        // The same ShouldJumpToMoveCost safety gate is reused for all
        // five move-cost/terrain Jump paths since the validation logic
        // is identical (valid GBA pointer + in-ROM offset + class loaded).
        // BattleAnime uses a distinct gate because its target is a
        // different editor (ImageBattleAnimeView) and the pointer must be
        // converted from raw GBA pointer to ROM offset before navigation.
        // ============================================================

        void JumpToPtr60_Click(object? sender, RoutedEventArgs e)
        {
            // FE7/8: Move Cost Rain (P60); FE6: Terrain Avoid (P56 in struct)
            try
            {
                uint ptr = ParseHexText(Ptr60Box.Text);
                if (!ShouldJumpToMoveCost(ptr, _vm.CurrentAddr)) return;
                CostType costType = _vm.IsFE6 ? CostType.TerrainAvoid : CostType.MoveCostRain;
                var view = WindowManager.Instance.Open<MoveCostEditorView>();
                view.NavigateToWithCostType(_vm.CurrentAddr, costType);
            }
            catch (Exception ex) { Log.Error("JumpToPtr60 failed: {0}", ex.Message); }
        }

        void JumpToPtr64_Click(object? sender, RoutedEventArgs e)
        {
            // FE7/8: Move Cost Snow (P64); FE6: Terrain Def (P60 in struct)
            try
            {
                uint ptr = ParseHexText(Ptr64Box.Text);
                if (!ShouldJumpToMoveCost(ptr, _vm.CurrentAddr)) return;
                CostType costType = _vm.IsFE6 ? CostType.TerrainDefense : CostType.MoveCostSnow;
                var view = WindowManager.Instance.Open<MoveCostEditorView>();
                view.NavigateToWithCostType(_vm.CurrentAddr, costType);
            }
            catch (Exception ex) { Log.Error("JumpToPtr64 failed: {0}", ex.Message); }
        }

        void JumpToPtr68_Click(object? sender, RoutedEventArgs e)
        {
            // FE7/8: Terrain Avoid (P68); FE6: Terrain Res (P64 in struct)
            try
            {
                uint ptr = ParseHexText(Ptr68Box.Text);
                if (!ShouldJumpToMoveCost(ptr, _vm.CurrentAddr)) return;
                CostType costType = _vm.IsFE6 ? CostType.TerrainResistance : CostType.TerrainAvoid;
                var view = WindowManager.Instance.Open<MoveCostEditorView>();
                view.NavigateToWithCostType(_vm.CurrentAddr, costType);
            }
            catch (Exception ex) { Log.Error("JumpToPtr68 failed: {0}", ex.Message); }
        }

        void JumpToPtr72_Click(object? sender, RoutedEventArgs e)
        {
            // FE7/8: Terrain Def (P72). FE6 hides this control so the
            // handler is unreachable; we still guard with IsFE6 in case
            // the visibility check is bypassed.
            try
            {
                if (_vm.IsFE6) return;
                uint ptr = ParseHexText(Ptr72Box.Text);
                if (!ShouldJumpToMoveCost(ptr, _vm.CurrentAddr)) return;
                var view = WindowManager.Instance.Open<MoveCostEditorView>();
                view.NavigateToWithCostType(_vm.CurrentAddr, CostType.TerrainDefense);
            }
            catch (Exception ex) { Log.Error("JumpToPtr72 failed: {0}", ex.Message); }
        }

        void JumpToPtr76_Click(object? sender, RoutedEventArgs e)
        {
            // FE7/8: Terrain Res (P76). FE6 hides this control via D80Row.
            try
            {
                if (_vm.IsFE6) return;
                uint ptr = ParseHexText(Ptr76Box.Text);
                if (!ShouldJumpToMoveCost(ptr, _vm.CurrentAddr)) return;
                var view = WindowManager.Instance.Open<MoveCostEditorView>();
                view.NavigateToWithCostType(_vm.CurrentAddr, CostType.TerrainResistance);
            }
            catch (Exception ex) { Log.Error("JumpToPtr76 failed: {0}", ex.Message); }
        }

        void JumpToBattleAnime_Click(object? sender, RoutedEventArgs e)
        {
            // Battle Anime pointer (P52 FE7/8 / P48 FE6). The class textbox
            // shows the raw GBA pointer (rom.u32, e.g. 0x089D1234) but
            // ImageBattleAnimeView.EntryList stores ROM offsets
            // (baseAddr + i*4 where baseAddr already went through
            // U.toOffset). Convert before navigating so SelectAddress
            // finds the matching slot.
            try
            {
                uint rawPtr = ParseHexText(Ptr52Box.Text);
                if (!ShouldJumpToBattleAnime(rawPtr, _vm.CurrentAddr)) return;
                uint romOffset = U.toOffset(rawPtr);
                WindowManager.Instance.Navigate<ImageBattleAnimeView>(romOffset);
            }
            catch (Exception ex)
            {
                Log.Error("JumpToBattleAnime failed: {0}", ex.Message);
            }
        }

        // Defensive gate for the BattleAnime Jump button. Same structure
        // as ShouldJumpToMoveCost — valid GBA pointer, in-ROM offset,
        // class loaded — extracted as a separate method so the safety
        // logic is visible to unit tests and any future change to the
        // BattleAnime path can adjust its gate independently from the
        // Move Cost path.
        internal static bool ShouldJumpToBattleAnime(uint battleAnimePtr, uint currentClassAddr)
        {
            if (!U.isPointer(battleAnimePtr)) return false;
            if (!U.isSafetyOffset(U.toOffset(battleAnimePtr))) return false;
            if (currentClassAddr == 0) return false;
            return true;
        }

        void UpdateWarnings()
        {
            var warnings = _vm.ValidateClass();
            WarningsBorder.IsVisible = warnings.Count > 0;
            WarningsList.ItemsSource = warnings;
        }

        public ViewModelBase? DataViewModel => _vm;

        void CalculateGrowth_Click(object? sender, RoutedEventArgs e)
        {
            SyncGrowthInputsToVm();
            _vm.CalculateGrowth();
            GrowthSimLabel.Text = _vm.GrowthSimText;
        }

        void OnGrowthInputChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading || !_vm.CanWrite) return;
            SyncGrowthInputsToVm();
            _vm.CalculateGrowth();
            GrowthSimLabel.Text = _vm.GrowthSimText;
        }

        void SyncGrowthInputsToVm()
        {
            _vm.IsLoading = true; // prevent cascading dirty marks during sync
            _vm.SimLevel = (uint)(SimLevelBox.Value ?? 20);
            // Use BaseHp through BaseRes for growth simulation (B11-B16)
            _vm.BaseHp = (uint)(BaseHpBox.Value ?? 0);
            _vm.BaseStr = (uint)(BaseStrBox.Value ?? 0);
            _vm.BaseSkl = (uint)(BaseSklBox.Value ?? 0);
            _vm.BaseSpd = (uint)(BaseSpdBox.Value ?? 0);
            _vm.BaseDef = (uint)(BaseDefBox.Value ?? 0);
            _vm.BaseRes = (uint)(BaseResBox.Value ?? 0);
            _vm.GrowHp = (uint)(GrowHpBox.Value ?? 0);
            _vm.GrowStr = (uint)(GrowStrBox.Value ?? 0);
            _vm.GrowSkl = (uint)(GrowSklBox.Value ?? 0);
            _vm.GrowSpd = (uint)(GrowSpdBox.Value ?? 0);
            _vm.GrowDef = (uint)(GrowDefBox.Value ?? 0);
            _vm.GrowRes = (uint)(GrowResBox.Value ?? 0);
            _vm.GrowLck = (uint)(GrowLckBox.Value ?? 0);
            _vm.IsLoading = false;
        }

        void EditSkills_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var skillType = PatchDetectionService.Instance.SkillSystem;
                switch (skillType)
                {
                    case PatchDetectionService.SkillSystemType.SkillSystem:
                        WindowManager.Instance.Open<SkillAssignmentClassSkillSystemView>();
                        break;
                    case PatchDetectionService.SkillSystemType.CSkillSys09x:
                    case PatchDetectionService.SkillSystemType.CSkillSys300:
                        WindowManager.Instance.Open<SkillAssignmentClassCSkillSysView>();
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("EditSkills_Click failed: {0}", ex.Message);
            }
        }

        async void ExportTSV_Click(object? sender, RoutedEventArgs e)
        {
            await TableExportImportHelper.ExportTableAsync(this, "classes");
        }

        async void ImportTSV_Click(object? sender, RoutedEventArgs e)
        {
            await TableExportImportHelper.ImportTableAsync(this, "classes", _undoService,
                ReloadCurrentClassAfterImport);
        }

        /// <summary>
        /// Reload the current class after a TSV import so all UI surfaces
        /// (form fields, list preview, class card, validation warnings) stay
        /// in sync with the imported data. Mirrors the
        /// <see cref="OnClassSelected"/> refresh sequence.
        ///
        /// CRITICAL: <see cref="UpdateUI"/> sets <c>PortraitIdBox</c> and
        /// <c>WaitIconBox</c> while <c>_vm.IsLoading</c> is true, so the
        /// <see cref="OnClassCardInputChanged"/> handler intentionally skips
        /// its refresh. <see cref="UpdateClassCard"/> MUST therefore be
        /// invoked explicitly here — removing this call leaves the class
        /// card preview stale until the user re-selects the class or
        /// manually edits one of those fields.
        ///
        /// Extracted into an internal method so it can be regression-tested
        /// without driving the full async TSV-import flow (PR #471 Copilot
        /// inline-review follow-up).
        /// </summary>
        internal void ReloadCurrentClassAfterImport()
        {
            if (_vm.CurrentAddr == 0) return;
            _vm.LoadClass(_vm.CurrentAddr);
            UpdateUI();
            TryShowListPreview();
            UpdateClassCard();
            UpdateWarnings();
        }

        public void EnablePickMode() => ClassList.EnablePickMode();

        public void SelectFirstItem()
        {
            ClassList.SelectFirst();
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
