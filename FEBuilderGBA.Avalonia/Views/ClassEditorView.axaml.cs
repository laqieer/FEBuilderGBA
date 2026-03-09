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

            // Additional
            B44Box.Value = _vm.B44;
            B45Box.Value = _vm.B45;
            B46Box.Value = _vm.B46;
            B47Box.Value = _vm.B47;
            B48Box.Value = _vm.B48;
            B49Box.Value = _vm.B49;
            B50Box.Value = _vm.B50;
            B51Box.Value = _vm.B51;

            // Pointers
            Ptr52Box.Text = $"0x{_vm.Ptr52:X08}";
            Ptr56Box.Text = $"0x{_vm.Ptr56:X08}";
            Ptr60Box.Text = $"0x{_vm.Ptr60:X08}";
            Ptr64Box.Text = $"0x{_vm.Ptr64:X08}";
            Ptr68Box.Text = $"0x{_vm.Ptr68:X08}";
            Ptr72Box.Text = $"0x{_vm.Ptr72:X08}";
            Ptr76Box.Text = $"0x{_vm.Ptr76:X08}";
            D80Box.Text = $"0x{_vm.D80:X08}";
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

            _vm.B44 = (uint)(B44Box.Value ?? 0);
            _vm.B45 = (uint)(B45Box.Value ?? 0);
            _vm.B46 = (uint)(B46Box.Value ?? 0);
            _vm.B47 = (uint)(B47Box.Value ?? 0);
            _vm.B48 = (uint)(B48Box.Value ?? 0);
            _vm.B49 = (uint)(B49Box.Value ?? 0);
            _vm.B50 = (uint)(B50Box.Value ?? 0);
            _vm.B51 = (uint)(B51Box.Value ?? 0);

            _vm.Ptr52 = ParseHexText(Ptr52Box.Text);
            _vm.Ptr56 = ParseHexText(Ptr56Box.Text);
            _vm.Ptr60 = ParseHexText(Ptr60Box.Text);
            _vm.Ptr64 = ParseHexText(Ptr64Box.Text);
            _vm.Ptr68 = ParseHexText(Ptr68Box.Text);
            _vm.Ptr72 = ParseHexText(Ptr72Box.Text);
            _vm.Ptr76 = ParseHexText(Ptr76Box.Text);
            _vm.D80 = ParseHexText(D80Box.Text);

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
