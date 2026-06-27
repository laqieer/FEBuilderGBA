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
    public partial class ClassFE6View : TranslatedWindow, IPickableEditor, IDataVerifiableView
    {
        readonly ClassFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Class Editor (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public event Action<PickResult>? SelectionConfirmed;

        public ClassFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            EntryList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
            Opened += (_, _) => LoadList();

            // Auto-recalculate growth sim when SimLevel or growth/base stat boxes change.
            SimLevelBox.ValueChanged += OnGrowthInputChanged;
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

            // Weapon-rank label live updates (B40-B47 in FE6).
            B40Box.ValueChanged += OnWeaponValueChanged;
            B41Box.ValueChanged += OnWeaponValueChanged;
            B42Box.ValueChanged += OnWeaponValueChanged;
            B43Box.ValueChanged += OnWeaponValueChanged;
            B44Box.ValueChanged += OnWeaponValueChanged;
            B45Box.ValueChanged += OnWeaponValueChanged;
            B46Box.ValueChanged += OnWeaponValueChanged;
            B47Box.ValueChanged += OnWeaponValueChanged;
        }

        void LoadList()
        {
            try
            {
                // Set ability flag names (FE6 = version 6).
                Ability1Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(6, 1));
                Ability2Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(6, 2));
                Ability3Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(6, 3));
                Ability4Flags.SetBitNames(AbilityFlagNames.GetAbilityNames(6, 4));

                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));

                UpdateAddressBarInfra();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ClassFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                RefreshHardCodingWarning();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ClassFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            _vm.IsLoading = true;
            try
            {
                AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
                NameLabel.Text = _vm.Name;

                // Identity
                NameIdBox.Value = _vm.NameId;
                DescIdBox.Value = _vm.DescId;
                ClassNumberBox.Value = _vm.ClassId;
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

                // Promotion gains (FE6: HP + Str only)
                PromoHpBox.Value = _vm.PromoHp;
                PromoStrBox.Value = _vm.PromoStr;

                // Ability flags
                Ability1Flags.Value = (byte)_vm.Ability1;
                Ability2Flags.Value = (byte)_vm.Ability2;
                Ability3Flags.Value = (byte)_vm.Ability3;
                Ability4Flags.Value = (byte)_vm.Ability4;

                // Weapon ranks (FE6: B40-B47)
                B40Box.Value = _vm.WepSword;
                B41Box.Value = _vm.WepLance;
                B42Box.Value = _vm.WepAxe;
                B43Box.Value = _vm.WepBow;
                B44Box.Value = _vm.WepStaff;
                B45Box.Value = _vm.WepAnima;
                B46Box.Value = _vm.WepLight;
                B47Box.Value = _vm.WepDark;
                UpdateWeaponRankLabels();

                // Pointers (FE6: no rain/snow)
                Ptr48Box.Text = $"0x{_vm.BattleAnimePtr:X08}";
                Ptr52Box.Text = $"0x{_vm.MoveCostPtr:X08}";
                Ptr56Box.Text = $"0x{_vm.TerrainAvoidPtr:X08}";
                Ptr60Box.Text = $"0x{_vm.TerrainDefPtr:X08}";
                Ptr64Box.Text = $"0x{_vm.TerrainResPtr:X08}";
                D68Box.Text = $"0x{_vm.UnknownD68:X08}";

                // Growth Simulator
                SimLevelBox.Value = _vm.SimLevel;
                _vm.CalculateGrowth();
                GrowthSimLabel.Text = _vm.GrowthSimText;
            }
            finally
            {
                _vm.IsLoading = false;
            }
        }

        void OnGrowthInputChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading || !_vm.CanWrite) return;
            // Push current growth/base/sim inputs into VM, recalculate.
            _vm.SimLevel = (uint)(SimLevelBox.Value ?? 20);
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
            _vm.CalculateGrowth();
            GrowthSimLabel.Text = _vm.GrowthSimText;
        }

        void CalculateGrowth_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SimLevel = (uint)(SimLevelBox.Value ?? 20);
            _vm.CalculateGrowth();
            GrowthSimLabel.Text = _vm.GrowthSimText;
        }

        void OnWeaponValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            UpdateWeaponRankLabels();
        }

        void UpdateWeaponRankLabels()
        {
            // This view is FE6-specific by design (ClassFE6View handles the FE6
            // class layout — B40-B47 weapon ranks, FE6 stat thresholds). Always
            // pass romVersion=6 to WeaponRankUtil so the displayed rank letter
            // uses FE6 thresholds (1-50=E, 51-100=D, 101-150=C, 151-200=B,
            // 201-250=A, 251+=S) regardless of which ROM file is loaded
            // (CoreState.ROM may be null or a non-FE6 version during
            // headless tests). Copilot bot review on PR #610.
            const int romVersion = 6;
            B40RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B40Box.Value ?? 0), romVersion);
            B41RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B41Box.Value ?? 0), romVersion);
            B42RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B42Box.Value ?? 0), romVersion);
            B43RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B43Box.Value ?? 0), romVersion);
            B44RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B44Box.Value ?? 0), romVersion);
            B45RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B45Box.Value ?? 0), romVersion);
            B46RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B46Box.Value ?? 0), romVersion);
            B47RankText.Text = WeaponRankUtil.GetRankLetter((uint)(B47Box.Value ?? 0), romVersion);
        }

        // -- Hyperlink label click handlers --

        void OnNameIdLinkClick(object? sender, PointerPressedEventArgs e)
            => NavigateToTextId((uint)(NameIdBox.Value ?? 0));

        void OnDescIdLinkClick(object? sender, PointerPressedEventArgs e)
            => NavigateToTextId((uint)(DescIdBox.Value ?? 0));

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

                // PortraitIdBox accepts u16 (0..65535). portraitId * dataSize
                // can overflow u32 (e.g. 65535 * 28 = ~1.8M which is fine, but
                // an arbitrarily large dataSize could wrap). Use checked
                // arithmetic + bounds-test against ROM length to fail safely.
                // Copilot bot review on PR #610.
                uint addr;
                try { addr = checked(baseAddr + portraitId * dataSize); }
                catch (OverflowException) { return; }
                if (!U.isSafetyOffset(addr)) return;
                if (addr + dataSize > (uint)rom.Data.Length) return;

                WindowManager.Instance.Navigate<PortraitViewerView>(addr);
            }
            catch (Exception ex)
            {
                Log.Error($"ClassFE6View.OnPortraitLinkClick failed: {ex.Message}");
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
                Log.Error($"ClassFE6View.NavigateToTextId failed: {ex.Message}");
            }
        }

        // -- Pointer jump handlers (FE6: no rain/snow) --

        void JumpToBattleAnime_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint rawPtr = ParseHexText(Ptr48Box.Text);
                if (!ShouldJumpToBattleAnime(rawPtr, _vm.CurrentAddr)) return;
                uint romOffset = U.toOffset(rawPtr);
                WindowManager.Instance.Navigate<ImageBattleAnimeView>(romOffset);
            }
            catch (Exception ex)
            {
                Log.Error($"ClassFE6View.JumpToBattleAnime failed: {ex.Message}");
            }
        }

        void JumpToMoveCost_Click(object? sender, RoutedEventArgs e)
            => JumpToMoveCostFE6(CostType.MoveCostNormal);

        void JumpToTerrainAvoid_Click(object? sender, RoutedEventArgs e)
            => JumpToMoveCostFE6(CostType.TerrainAvoid);

        void JumpToTerrainDef_Click(object? sender, RoutedEventArgs e)
            => JumpToMoveCostFE6(CostType.TerrainDefense);

        void JumpToTerrainRes_Click(object? sender, RoutedEventArgs e)
            => JumpToMoveCostFE6(CostType.TerrainResistance);

        void JumpToMoveCostFE6(CostType costType)
        {
            try
            {
                if (_vm.CurrentAddr == 0) return;
                var view = WindowManager.Instance.Open<MoveCostFE6View>();
                view.NavigateToWithCostType(_vm.CurrentAddr, costType);
            }
            catch (Exception ex)
            {
                Log.Error($"ClassFE6View.JumpToMoveCostFE6({costType}) failed: {ex.Message}");
            }
        }

        internal static bool ShouldJumpToBattleAnime(uint battleAnimePtr, uint currentClassAddr)
        {
            if (!U.isPointer(battleAnimePtr)) return false;
            if (!U.isSafetyOffset(U.toOffset(battleAnimePtr))) return false;
            if (currentClassAddr == 0) return false;
            return true;
        }

        // -- HardCoding warning --

        /// <summary>
        /// Refresh the HardCoding-warning hyperlink's visibility for the current
        /// class. Uses <see cref="IAsmMapCache.IsHardCodeClass"/> (matches WF
        /// ClassForm + the HARDCODING_CLASS= patch jump). The WF
        /// ClassFE6Form.CheckHardCodingWarning historically called
        /// <c>IsHardCodeUnit</c> with a class id, which is a latent bug — see
        /// PR #388 known limitations.
        ///
        /// #1035: the cache is now backed by the real patch-scan
        /// <see cref="CoreAsmMapCache"/>, so FE6 ADDRESS_TYPE=CLASS patches can
        /// genuinely light this warning. Calling <c>IsHardCodeClass</c> here (NOT
        /// <c>IsHardCodeUnit</c>) is the INTENDED, correct behavior — the WF
        /// ClassFE6Form bug is deliberately NOT replicated.
        /// </summary>
        void RefreshHardCodingWarning()
        {
            try
            {
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0)
                {
                    HardCodingWarningLabel.IsVisible = false;
                    return;
                }
                uint classId = (uint)idx;
                bool r = CoreState.AsmMapFileAsmCache?.IsHardCodeClass(classId) ?? false;
                HardCodingWarningLabel.IsVisible = r;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ClassFE6View.RefreshHardCodingWarning failed: {0}", ex.Message);
                HardCodingWarningLabel.IsVisible = false;
            }
        }

        void HardCodingWarning_Click(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0) return;
                uint classId = (uint)idx;
                var pv = WindowManager.Instance.Open<PatchManagerView>();
                pv.JumpTo($"HARDCODING_CLASS={classId:X2}", 0);
            }
            catch (Exception ex)
            {
                Log.Error($"ClassFE6View.HardCodingWarning_Click failed: {ex.Message}");
            }
        }

        // -- Address-bar infra --

        void UpdateAddressBarInfra()
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint resolvedBase = rom.p32(rom.RomInfo.class_pointer);
                // #649: top-bar fields are properties on the unified
                // EditorTopBar control.
                if (TopBar == null) return;
                TopBar.StartAddressText = $"0x{resolvedBase:X8}";
                TopBar.ReadCountText = _vm.GetListCount().ToString();
                TopBar.SizeText = $"0x{rom.RomInfo.class_datasize:X}";
            }
            catch (Exception ex)
            {
                Log.ErrorF("ClassFE6View.UpdateAddressBarInfra failed: {0}", ex.Message);
            }
        }

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        // -- Write & CSV --

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            SyncUiToVm();

            _undoService.Begin("Edit Class (FE6)");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Class data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ClassFE6View.Write_Click failed: {0}", ex.Message);
            }
        }

        void SyncUiToVm()
        {
            // Identity
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.DescId = (uint)(DescIdBox.Value ?? 0);
            _vm.ClassId = (uint)(ClassNumberBox.Value ?? 0);
            _vm.PromotionLevel = (uint)(PromotionLevelBox.Value ?? 0);
            _vm.WaitIcon = (uint)(WaitIconBox.Value ?? 0);
            _vm.WalkSpeed = (uint)(WalkSpeedBox.Value ?? 0);
            _vm.PortraitId = (uint)(PortraitIdBox.Value ?? 0);
            _vm.SortOrder = (uint)(SortOrderBox.Value ?? 0);

            // Base stats
            _vm.BaseHp = (uint)(BaseHpBox.Value ?? 0);
            _vm.BaseStr = (uint)(BaseStrBox.Value ?? 0);
            _vm.BaseSkl = (uint)(BaseSklBox.Value ?? 0);
            _vm.BaseSpd = (uint)(BaseSpdBox.Value ?? 0);
            _vm.BaseDef = (uint)(BaseDefBox.Value ?? 0);
            _vm.BaseRes = (uint)(BaseResBox.Value ?? 0);
            _vm.BaseCon = (uint)(BaseConBox.Value ?? 0);
            _vm.BaseMov = (uint)(BaseMovBox.Value ?? 0);

            // Stat caps
            _vm.MaxHp = (uint)(MaxHpBox.Value ?? 0);
            _vm.MaxStr = (uint)(MaxStrBox.Value ?? 0);
            _vm.MaxSkl = (uint)(MaxSklBox.Value ?? 0);
            _vm.MaxSpd = (uint)(MaxSpdBox.Value ?? 0);
            _vm.MaxDef = (uint)(MaxDefBox.Value ?? 0);
            _vm.MaxRes = (uint)(MaxResBox.Value ?? 0);
            _vm.MaxCon = (uint)(MaxConBox.Value ?? 0);
            _vm.ClassPower = (uint)(ClassPowerBox.Value ?? 0);

            // Growth rates
            _vm.GrowHp = (uint)(GrowHpBox.Value ?? 0);
            _vm.GrowStr = (uint)(GrowStrBox.Value ?? 0);
            _vm.GrowSkl = (uint)(GrowSklBox.Value ?? 0);
            _vm.GrowSpd = (uint)(GrowSpdBox.Value ?? 0);
            _vm.GrowDef = (uint)(GrowDefBox.Value ?? 0);
            _vm.GrowRes = (uint)(GrowResBox.Value ?? 0);
            _vm.GrowLck = (uint)(GrowLckBox.Value ?? 0);

            // Promotion gains (FE6: HP + Str only)
            _vm.PromoHp = (int)(PromoHpBox.Value ?? 0);
            _vm.PromoStr = (int)(PromoStrBox.Value ?? 0);

            // Abilities from BitFlagPanel
            _vm.Ability1 = Ability1Flags.Value;
            _vm.Ability2 = Ability2Flags.Value;
            _vm.Ability3 = Ability3Flags.Value;
            _vm.Ability4 = Ability4Flags.Value;

            // Weapon ranks (FE6: B40-B47)
            _vm.WepSword = (uint)(B40Box.Value ?? 0);
            _vm.WepLance = (uint)(B41Box.Value ?? 0);
            _vm.WepAxe = (uint)(B42Box.Value ?? 0);
            _vm.WepBow = (uint)(B43Box.Value ?? 0);
            _vm.WepStaff = (uint)(B44Box.Value ?? 0);
            _vm.WepAnima = (uint)(B45Box.Value ?? 0);
            _vm.WepLight = (uint)(B46Box.Value ?? 0);
            _vm.WepDark = (uint)(B47Box.Value ?? 0);

            // Pointers
            _vm.BattleAnimePtr = ParseHexText(Ptr48Box.Text);
            _vm.MoveCostPtr = ParseHexText(Ptr52Box.Text);
            _vm.TerrainAvoidPtr = ParseHexText(Ptr56Box.Text);
            _vm.TerrainDefPtr = ParseHexText(Ptr60Box.Text);
            _vm.TerrainResPtr = ParseHexText(Ptr64Box.Text);
            _vm.UnknownD68 = ParseHexText(D68Box.Text);
        }

        ClassFE6CsvManager BuildCsvManager() => new ClassFE6CsvManager(
            useClipboard: UseClipboardCheck.IsChecked ?? false,
            includeUID: IncludeUIDCheck.IsChecked ?? true,
            includeHeader: IncludeHeaderCheck.IsChecked ?? true,
            includeName: IncludeNameCheck.IsChecked ?? true,
            includeBaseStats: IncludeBaseStatsCheck.IsChecked ?? true,
            includeGrowths: IncludeGrowthsCheck.IsChecked ?? true,
            includeWepLevel: IncludeWepLevelCheck.IsChecked ?? true,
            growthsAsDecimal: GrowthsAsDecimalCheck.IsChecked ?? true);

        async void ExportAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;
                var addrs = GetAllClassAddresses();
                if (addrs.Length == 0) return;
                var mgr = BuildCsvManager();
                await mgr.ExportAllAsync(this, rom, addrs);
            }
            catch (Exception ex) { Log.Error($"ClassFE6View.ExportAll failed: {ex.Message}"); }
        }

        async void ExportSelected_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null || _vm.CurrentAddr == 0) return;
                var mgr = BuildCsvManager();
                int idx = EntryList.SelectedOriginalIndex;
                uint uid = idx >= 0 ? (uint)idx : 0;
                await mgr.ExportSelectedAsync(this, rom, _vm.CurrentAddr, uid);
            }
            catch (Exception ex) { Log.Error($"ClassFE6View.ExportSelected failed: {ex.Message}"); }
        }

        async void ImportAll_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;
                var addrs = GetAllClassAddresses();
                if (addrs.Length == 0) return;
                var mgr = BuildCsvManager();
                string? csv = await mgr.ReadCsvForUiAsync(this);
                if (csv == null) return;
                _undoService.Begin("Import Classes (FE6) CSV");
                int written;
                try
                {
                    written = mgr.ApplyImportCsv(rom, csv, addrs);
                    _undoService.Commit();
                }
                catch { _undoService.Rollback(); throw; }
                if (written > 0 && _vm.CurrentAddr != 0)
                {
                    _vm.LoadEntry(_vm.CurrentAddr);
                    UpdateUI();
                }
                CoreState.Services?.ShowInfo($"Imported {written} classes.");
            }
            catch (Exception ex) { Log.Error($"ClassFE6View.ImportAll failed: {ex.Message}"); }
        }

        async void ImportSelected_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null || _vm.CurrentAddr == 0) return;
                var mgr = BuildCsvManager();
                string? csv = await mgr.ReadCsvForUiAsync(this);
                if (csv == null) return;
                _undoService.Begin("Import Class (FE6) CSV");
                int written;
                try
                {
                    written = mgr.ApplyImportCsv(rom, csv, new[] { _vm.CurrentAddr });
                    _undoService.Commit();
                }
                catch { _undoService.Rollback(); throw; }
                if (written > 0)
                {
                    _vm.LoadEntry(_vm.CurrentAddr);
                    UpdateUI();
                }
                CoreState.Services?.ShowInfo($"Imported {written} class.");
            }
            catch (Exception ex) { Log.Error($"ClassFE6View.ImportSelected failed: {ex.Message}"); }
        }

        uint[] GetAllClassAddresses() => _vm.LoadList().Select(r => r.addr).ToArray();

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public void EnablePickMode() => EntryList.EnablePickMode();

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
