using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitFE7View : Window, IEditorView, IDataVerifiableView
    {
        readonly UnitFE7ViewModel _vm = new();

        public string ViewTitle => "Units (FE7) Editor";
        public bool IsLoaded => _vm.CanWrite;

        public UnitFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadUnitList();
                EntryList.SetItems(items);
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
                _vm.LoadUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
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
            UnitIdBox.Value = _vm.UnitId;
            ClassIdBox.Value = _vm.ClassId;
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

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            // Identity
            _vm.NameId = (uint)(NameIdBox.Value ?? 0);
            _vm.DescId = (uint)(DescIdBox.Value ?? 0);
            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
            _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);
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

            _vm.WriteUnit();
            CoreState.Services?.ShowInfo("Unit (FE7) data written.");
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
