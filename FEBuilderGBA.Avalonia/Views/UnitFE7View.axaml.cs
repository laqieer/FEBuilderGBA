using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitFE7View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly UnitFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Units (FE7) Editor";
        public bool IsLoaded => _vm.CanWrite;

        public UnitFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();

            // Wire desc text live update
            DescIdBox.ValueChanged += OnDescIdChanged;

            // #358: jump to Support Unit Editor for this unit's support row.
            JumpToSupportUnitButton.Click += JumpToSupportUnit_Click;
        }

        /// <summary>
        /// #358 — Open the FE7 Support Unit Editor (shared FE7/FE8 view) and
        /// select the support row that this unit's +44 pointer references.
        /// </summary>
        void JumpToSupportUnit_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint supportPtr = U.atoh(SupportPtrBox.Text ?? "0");
                if (supportPtr == 0) return;
                var window = WindowManager.Instance.Open<SupportUnitEditorView>();
                window.JumpToAddr(supportPtr);
            }
            catch (Exception ex)
            {
                Log.Error("UnitFE7View.JumpToSupportUnit failed: {0}", ex.Message);
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
                Log.Error("UnitFE7View.LoadList failed: {0}", ex.Message);
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
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("UnitFE7View.OnSelected failed: {0}", ex.Message);
            }
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

        static uint ClassAddrFor(uint classId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint classPtr = rom.RomInfo.class_pointer;
            if (classPtr == 0) return 0;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr)) return 0;
            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return 0;
            return baseAddr + classId * dataSize;
        }

        void ClassId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("UnitFE7View.ClassId_Jump failed: {0}", ex.Message); }
        }

        async void ClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr, this);
                if (result != null)
                {
                    ClassIdBox.Value = (uint)result.Index;
                    // NameText refresh via ValueChanged.
                }
            }
            catch (Exception ex) { Log.Error("UnitFE7View.ClassId_Pick failed: {0}", ex.Message); }
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
            catch (Exception ex) { Log.Error("OnPortraitLinkClick failed: {0}", ex.Message); }
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
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        private static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        }
    }
}
