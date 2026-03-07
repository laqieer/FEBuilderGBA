using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class UnitFE6View : Window, IEditorView, IDataVerifiableView
    {
        readonly UnitFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Unit Editor (FE6)";
        public bool IsLoaded => _vm.CanWrite;

        public UnitFE6View()
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
                Log.Error("UnitFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadUnit(addr);
                UpdateUI();
                TryShowPortrait();
            }
            catch (Exception ex)
            {
                Log.Error("UnitFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameLabel.Text = _vm.Name;

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
            _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);
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

            // Support pointer (parse hex)
            _vm.SupportPtr = U.atoh(SupportPtrBox.Text ?? "0");
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
                Log.Error("UnitFE6View.TryShowPortrait failed: {0}", ex.Message);
                PortraitImage.SetImage(null);
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            _vm.WriteUnit();
            CoreState.Services.ShowInfo("Unit data written.");
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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
