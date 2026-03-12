using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitEditorView : Window, IPickableEditor, IDataVerifiableView
    {
        readonly UnitEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        List<(uint id, string name)> _classList = new();
        List<(uint id, string name)> _affinityList = new();

        public string ViewTitle => "Unit Editor";
        public bool IsLoaded => _vm.CanWrite;

        public event Action<PickResult>? SelectionConfirmed;

        public UnitEditorView()
        {
            InitializeComponent();
            UnitList.SelectedAddressChanged += OnUnitSelected;
            UnitList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
            Opened += (_, _) => LoadList();

            // Set bit flag names
            Ability1Flags.SetBitNames(AbilityFlagNames.UnitAbility1);
            Ability2Flags.SetBitNames(AbilityFlagNames.UnitAbility2);
            Ability3Flags.SetBitNames(AbilityFlagNames.UnitAbility3);
            Ability4Flags.SetBitNames(AbilityFlagNames.UnitAbility4);

            // Wire auto-recalculation on stat/growth/level/class changes
            WireGrowthAutoRecalc();
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
                var items = _vm.LoadUnitList();
                UnitList.SetItems(items);
                UpdateFE78Visibility();

                // Populate combo dropdowns
                _classList = ComboResourceHelper.MakeClassList();
                ClassIdCombo.ItemsSource = _classList.Select(x => x.name).ToList();

                _affinityList = ComboResourceHelper.MakeAffinityList();
                AffinityCombo.ItemsSource = _affinityList.Select(x => x.name).ToList();
            }
            catch (Exception ex)
            {
                Log.Error("UnitEditorView.LoadList failed: {0}", ex.Message);
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
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("UnitEditorView.OnUnitSelected failed: {0}", ex.Message);
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
            UnitIdBox.Value = _vm.UnitId;

            // Class combo
            int classIdx = _classList.FindIndex(x => x.id == _vm.ClassId);
            ClassIdCombo.SelectedIndex = classIdx >= 0 ? classIdx : (int)_vm.ClassId;

            PortraitIdBox.Value = _vm.PortraitId;
            PortraitNameLabel.Text = NameResolver.GetUnitName(_vm.PortraitId);
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

            // Support pointer (parse hex)
            _vm.SupportPtr = U.atoh(SupportPtrBox.Text ?? "0");

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
                Log.Error("UnitEditorView.TryShowPortrait failed: {0}", ex.Message);
                PortraitImage.SetImage(null);
            }
        }

        void TryShowListPreview()
        {
            try
            {
                var img = PreviewIconHelper.LoadPortraitMini(_vm.PortraitId);
                if (img != null)
                {
                    ListPreviewImage.Zoom = 1;
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
            _undoService.Begin("Edit Unit");
            try
            {
                _vm.WriteUnit();
                _undoService.Commit();
                _vm.MarkClean();
                UpdateWarnings();
                CoreState.Services.ShowInfo("Unit data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
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
                WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex)
            {
                Log.Error("JumpToClass failed: {0}", ex.Message);
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
                Log.Error("JumpToPortrait failed: {0}", ex.Message);
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

                var result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(navAddr, this);
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
                Log.Error("PickClass failed: {0}", ex.Message);
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

                var result = await WindowManager.Instance.PickFromEditor<PortraitViewerView>(navAddr, this);
                if (result != null)
                {
                    PortraitIdBox.Value = result.Index;
                    PortraitNameLabel.Text = NameResolver.GetUnitName((uint)result.Index);
                    TryShowPortrait();
                }
            }
            catch (Exception ex)
            {
                Log.Error("PickPortrait failed: {0}", ex.Message);
            }
        }

        void CalculateGrowth_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            RecalcGrowth();
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

        void UpdateWarnings()
        {
            var warnings = _vm.ValidateUnit();
            WarningsBorder.IsVisible = warnings.Count > 0;
            WarningsList.ItemsSource = warnings;
        }

        public ViewModelBase? DataViewModel => _vm;

        async void ExportTSV_Click(object? sender, RoutedEventArgs e)
        {
            await TableExportImportHelper.ExportTableAsync(this, "units");
        }

        async void ImportTSV_Click(object? sender, RoutedEventArgs e)
        {
            await TableExportImportHelper.ImportTableAsync(this, "units", _undoService, () =>
            {
                // Reload the current entry after import
                if (_vm.CurrentAddr != 0)
                {
                    _vm.LoadUnit(_vm.CurrentAddr);
                    UpdateUI();
                }
            });
        }

        public void EnablePickMode() => UnitList.EnablePickMode();

        /// <summary>Select the first item in the list (for smoke testing).</summary>
        public void SelectFirstItem()
        {
            UnitList.SelectFirst();
        }
    }
}
