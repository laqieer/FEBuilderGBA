using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ClassEditorView : Window, IEditorView, IDataVerifiableView
    {
        readonly ClassEditorViewModel _vm = new();

        public string ViewTitle => "Class Editor";
        public bool IsLoaded => _vm.CanWrite;

        public ClassEditorView()
        {
            InitializeComponent();
            ClassList.SelectedAddressChanged += OnClassSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadClassList();
                ClassList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ClassEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnClassSelected(uint addr)
        {
            try
            {
                _vm.LoadClass(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ClassEditorView.OnClassSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            ClassList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameLabel.Text = _vm.Name;

            // Identity
            NameIdBox.Value = _vm.NameId;
            DescIdBox.Value = _vm.DescId;
            ClassNumberBox.Value = _vm.ClassNumber;
            PromotionLevelBox.Value = _vm.PromotionLevel;
            WaitIconBox.Value = _vm.WaitIcon;
            WalkSpeedBox.Value = _vm.WalkSpeed;
            PortraitIdBox.Value = _vm.PortraitId;
            BuildStatBox.Value = _vm.BuildStat;

            // Base stats
            BaseHpBox.Value = _vm.BaseHp;
            BaseStrBox.Value = _vm.BaseStr;
            BaseSklBox.Value = _vm.BaseSkl;
            BaseSpdBox.Value = _vm.BaseSpd;
            BaseDefBox.Value = _vm.BaseDef;
            BaseResBox.Value = _vm.BaseRes;
            MovBox.Value = _vm.Mov;
            ConBox.Value = _vm.Con;
            ClassStat19Box.Value = _vm.ClassStat19;

            // Weapons
            WepSwordBox.Value = _vm.WepSword;
            WepLanceBox.Value = _vm.WepLance;
            WepAxeBox.Value = _vm.WepAxe;
            WepBowBox.Value = _vm.WepBow;
            WepStaffBox.Value = _vm.WepStaff;
            WepAnimaBox.Value = _vm.WepAnima;
            WepLightBox.Value = _vm.WepLight;

            // Growth rates
            GrowHpBox.Value = _vm.GrowHp;
            GrowStrBox.Value = _vm.GrowStr;
            GrowSklBox.Value = _vm.GrowSkl;
            GrowSpdBox.Value = _vm.GrowSpd;
            GrowDefBox.Value = _vm.GrowDef;
            GrowResBox.Value = _vm.GrowRes;
            GrowLckBox.Value = _vm.GrowLck;

            // Stat caps
            CapHpBox.Value = _vm.CapHp;
            CapStrBox.Value = _vm.CapStr;
            CapSklBox.Value = _vm.CapSkl;
            CapSpdBox.Value = _vm.CapSpd;
            CapDefBox.Value = _vm.CapDef;
            CapResBox.Value = _vm.CapRes;

            // Abilities
            Ability1Box.Value = _vm.Ability1;
            Ability2Box.Value = _vm.Ability2;
            Ability3Box.Value = _vm.Ability3;
            Ability4Box.Value = _vm.Ability4;

            // Weapon rank levels
            B44Box.Value = _vm.WepRankSword;
            B45Box.Value = _vm.WepRankLance;
            B46Box.Value = _vm.WepRankAxe;
            B47Box.Value = _vm.WepRankBow;
            B48Box.Value = _vm.WepRankStaff;
            B49Box.Value = _vm.WepRankAnima;
            B50Box.Value = _vm.WepRankLight;
            B51Box.Value = _vm.WepRankDark;

            // Pointers
            Ptr52Box.Text = $"0x{_vm.BattleAnimePtr:X08}";
            Ptr56Box.Text = $"0x{_vm.MoveCostPtr:X08}";
            Ptr60Box.Text = $"0x{_vm.MoveCostRainPtr:X08}";
            Ptr64Box.Text = $"0x{_vm.MoveCostSnowPtr:X08}";
            Ptr68Box.Text = $"0x{_vm.TerrainAvoidPtr:X08}";
            Ptr72Box.Text = $"0x{_vm.TerrainDefPtr:X08}";
            Ptr76Box.Text = $"0x{_vm.TerrainResPtr:X08}";
            D80Box.Text = $"0x{_vm.UnknownD80:X08}";
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
            _vm.BuildStat = (uint)(BuildStatBox.Value ?? 0);

            _vm.BaseHp = (uint)(BaseHpBox.Value ?? 0);
            _vm.BaseStr = (uint)(BaseStrBox.Value ?? 0);
            _vm.BaseSkl = (uint)(BaseSklBox.Value ?? 0);
            _vm.BaseSpd = (uint)(BaseSpdBox.Value ?? 0);
            _vm.BaseDef = (uint)(BaseDefBox.Value ?? 0);
            _vm.BaseRes = (uint)(BaseResBox.Value ?? 0);
            _vm.Mov = (uint)(MovBox.Value ?? 0);
            _vm.Con = (uint)(ConBox.Value ?? 0);
            _vm.ClassStat19 = (uint)(ClassStat19Box.Value ?? 0);

            _vm.WepSword = (uint)(WepSwordBox.Value ?? 0);
            _vm.WepLance = (uint)(WepLanceBox.Value ?? 0);
            _vm.WepAxe = (uint)(WepAxeBox.Value ?? 0);
            _vm.WepBow = (uint)(WepBowBox.Value ?? 0);
            _vm.WepStaff = (uint)(WepStaffBox.Value ?? 0);
            _vm.WepAnima = (uint)(WepAnimaBox.Value ?? 0);
            _vm.WepLight = (uint)(WepLightBox.Value ?? 0);

            _vm.GrowHp = (uint)(GrowHpBox.Value ?? 0);
            _vm.GrowStr = (uint)(GrowStrBox.Value ?? 0);
            _vm.GrowSkl = (uint)(GrowSklBox.Value ?? 0);
            _vm.GrowSpd = (uint)(GrowSpdBox.Value ?? 0);
            _vm.GrowDef = (uint)(GrowDefBox.Value ?? 0);
            _vm.GrowRes = (uint)(GrowResBox.Value ?? 0);
            _vm.GrowLck = (uint)(GrowLckBox.Value ?? 0);

            _vm.CapHp = (int)(CapHpBox.Value ?? 0);
            _vm.CapStr = (int)(CapStrBox.Value ?? 0);
            _vm.CapSkl = (int)(CapSklBox.Value ?? 0);
            _vm.CapSpd = (int)(CapSpdBox.Value ?? 0);
            _vm.CapDef = (int)(CapDefBox.Value ?? 0);
            _vm.CapRes = (int)(CapResBox.Value ?? 0);

            _vm.Ability1 = (uint)(Ability1Box.Value ?? 0);
            _vm.Ability2 = (uint)(Ability2Box.Value ?? 0);
            _vm.Ability3 = (uint)(Ability3Box.Value ?? 0);
            _vm.Ability4 = (uint)(Ability4Box.Value ?? 0);

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
            _vm.MoveCostRainPtr = ParseHexText(Ptr60Box.Text);
            _vm.MoveCostSnowPtr = ParseHexText(Ptr64Box.Text);
            _vm.TerrainAvoidPtr = ParseHexText(Ptr68Box.Text);
            _vm.TerrainDefPtr = ParseHexText(Ptr72Box.Text);
            _vm.TerrainResPtr = ParseHexText(Ptr76Box.Text);
            _vm.UnknownD80 = ParseHexText(D80Box.Text);

            _vm.WriteClass();
            CoreState.Services.ShowInfo("Class data written.");
        }

        public ViewModelBase? DataViewModel => _vm;

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
