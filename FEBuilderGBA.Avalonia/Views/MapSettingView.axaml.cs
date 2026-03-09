using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapSettingView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapSettingViewModel _vm = new();

        public string ViewTitle => "Map Settings";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapSettingView()
        {
            InitializeComponent();
            MapList.SelectedAddressChanged += OnMapSelected;
            WriteButton.Click += OnWriteClick;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMapSettingList();
                MapList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnMapSelected(uint addr)
        {
            try
            {
                _vm.LoadMapSetting(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingView.OnMapSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            MapList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"Address: 0x{_vm.CurrentAddr:X08}  (struct size: {_vm.DataSize} bytes)";

            ND0.Text = $"0x{_vm.CpPointer:X08}";
            NW4.Value = _vm.ObjectTypePLIST;

            NB6.Value = _vm.PalettePLIST;
            NB7.Value = _vm.ChipsetConfigPLIST;
            NB8.Value = _vm.MapPointerPLIST;
            NB9.Value = _vm.TileAnimation1PLIST;
            NB10.Value = _vm.TileAnimation2PLIST;
            NB11.Value = _vm.MapChangePLIST;
            NB12.Value = _vm.FogLevel;
            NB13.Value = _vm.BattlePreparation;
            NB14.Value = _vm.ChapterTitleImage;
            NB15.Value = _vm.ChapterTitleImage2;
            NB16.Value = _vm.InitialX;
            NB17.Value = _vm.InitialY;
            NB18.Value = _vm.Weather;
            NB19.Value = _vm.BattleBGLookup;

            NW20.Value = _vm.DifficultyAdjustment;
            NW22.Value = _vm.PlayerPhaseBGM;
            NW24.Value = _vm.EnemyPhaseBGM;
            NW26.Value = _vm.NpcPhaseBGM;
            NW28.Value = _vm.PlayerPhaseBGM2;
            NW30.Value = _vm.EnemyPhaseBGM2;
            NW32.Value = _vm.NpcPhaseBGM2;
            NW34.Value = _vm.PlayerPhaseBGMFlag4;
            NW36.Value = _vm.EnemyPhaseBGMFlag4;
            NW38.Value = _vm.UnknownW38;
            NW40.Value = _vm.UnknownW40;
            NW42.Value = _vm.UnknownW42;

            NB44.Value = _vm.BreakableWallHP;
            NB45.Value = _vm.RatingAEliwoodNormal;
            NB46.Value = _vm.RatingAEliwoodHard;
            NB47.Value = _vm.RatingAHectorNormal;
            NB48.Value = _vm.RatingAHectorHard;
            NB49.Value = _vm.RatingBEliwoodNormal;
            NB50.Value = _vm.RatingBEliwoodHard;
            NB51.Value = _vm.RatingBHectorNormal;
            NB52.Value = _vm.RatingBHectorHard;
            NB53.Value = _vm.RatingCEliwoodNormal;
            NB54.Value = _vm.RatingCEliwoodHard;
            NB55.Value = _vm.RatingCHectorNormal;
            NB56.Value = _vm.RatingCHectorHard;
            NB57.Value = _vm.RatingDEliwoodNormal;
            NB58.Value = _vm.RatingDEliwoodHard;
            NB59.Value = _vm.RatingDHectorNormal;
            NB60.Value = _vm.RatingDHectorHard;
            NB61.Value = _vm.UnknownB61;

            NW62.Value = _vm.RatingAEliwoodNormalW;
            NW64.Value = _vm.RatingAEliwoodHardW;
            NW66.Value = _vm.RatingAHectorNormalW;
            NW68.Value = _vm.RatingAHectorHardW;
            NW70.Value = _vm.RatingBEliwoodNormalW;
            NW72.Value = _vm.RatingBEliwoodHardW;
            NW74.Value = _vm.RatingBHectorNormalW;
            NW76.Value = _vm.RatingBHectorHardW;
            NW78.Value = _vm.RatingCEliwoodNormalW;
            NW80.Value = _vm.RatingCEliwoodHardW;
            NW82.Value = _vm.RatingCHectorNormalW;
            NW84.Value = _vm.RatingCHectorHardW;
            NW86.Value = _vm.RatingDEliwoodNormalW;
            NW88.Value = _vm.RatingDEliwoodHardW;
            NW90.Value = _vm.RatingDHectorNormalW;
            NW92.Value = _vm.RatingDHectorHardW;
            NW94.Value = _vm.UnknownW94;

            ND96.Text = $"0x{_vm.DiffPtrEliwoodNormal:X08}";
            ND100.Text = $"0x{_vm.DiffPtrEliwoodHard:X08}";
            ND104.Text = $"0x{_vm.DiffPtrHectorNormal:X08}";
            ND108.Text = $"0x{_vm.DiffPtrHectorHard:X08}";

            NW112.Value = _vm.MapNameText1;
            NW114.Value = _vm.MapNameText2;
            W112TextLabel.Text = _vm.MapNameText1Resolved;
            W114TextLabel.Text = _vm.MapNameText2Resolved;

            NB116.Value = _vm.EventIdPLIST;
            NB117.Value = _vm.WorldMapAutoEvent;
            NB118.Value = _vm.UnknownB118;
            NB119.Value = _vm.UnknownB119;
            NB120.Value = _vm.UnknownB120;
            NB121.Value = _vm.UnknownB121;
            NB122.Value = _vm.UnknownB122;
            NB123.Value = _vm.UnknownB123;
            NB124.Value = _vm.UnknownB124;
            NB125.Value = _vm.UnknownB125;
            NB126.Value = _vm.UnknownB126;
            NB127.Value = _vm.UnknownB127;
            NB128.Value = _vm.ChapterNumber;
            NB129.Value = _vm.UnknownB129;
            NB130.Value = _vm.UnknownB130;
            NB131.Value = _vm.UnknownB131;
            NB132.Value = _vm.UnknownB132;
            NB133.Value = _vm.UnknownB133;
            NB134.Value = _vm.VictoryBGMEnemyCount;
            NB135.Value = _vm.BlackoutBeforeStart;

            NW136.Value = _vm.ClearConditionText;
            NW138.Value = _vm.DetailClearConditionText;
            W136TextLabel.Text = _vm.ClearConditionTextResolved;
            W138TextLabel.Text = _vm.DetailClearConditionTextResolved;

            NB140.Value = _vm.SpecialDisplay;
            NB141.Value = _vm.TurnCountDisplay;
            NB142.Value = _vm.DefenseUnitMark;
            NB143.Value = _vm.EscapeMarkerX;
            NB144.Value = _vm.EscapeMarkerY;
            NB145.Value = _vm.UnknownB145;
            NB146.Value = _vm.UnknownB146;
            NB147.Value = _vm.UnknownB147;
        }

        void ReadUIToVM()
        {
            _vm.CpPointer = ParseHexText(ND0.Text);
            _vm.ObjectTypePLIST = (uint)(NW4.Value ?? 0);

            _vm.PalettePLIST = (uint)(NB6.Value ?? 0);
            _vm.ChipsetConfigPLIST = (uint)(NB7.Value ?? 0);
            _vm.MapPointerPLIST = (uint)(NB8.Value ?? 0);
            _vm.TileAnimation1PLIST = (uint)(NB9.Value ?? 0);
            _vm.TileAnimation2PLIST = (uint)(NB10.Value ?? 0);
            _vm.MapChangePLIST = (uint)(NB11.Value ?? 0);
            _vm.FogLevel = (uint)(NB12.Value ?? 0);
            _vm.BattlePreparation = (uint)(NB13.Value ?? 0);
            _vm.ChapterTitleImage = (uint)(NB14.Value ?? 0);
            _vm.ChapterTitleImage2 = (uint)(NB15.Value ?? 0);
            _vm.InitialX = (uint)(NB16.Value ?? 0);
            _vm.InitialY = (uint)(NB17.Value ?? 0);
            _vm.Weather = (uint)(NB18.Value ?? 0);
            _vm.BattleBGLookup = (uint)(NB19.Value ?? 0);

            _vm.DifficultyAdjustment = (uint)(NW20.Value ?? 0);
            _vm.PlayerPhaseBGM = (uint)(NW22.Value ?? 0);
            _vm.EnemyPhaseBGM = (uint)(NW24.Value ?? 0);
            _vm.NpcPhaseBGM = (uint)(NW26.Value ?? 0);
            _vm.PlayerPhaseBGM2 = (uint)(NW28.Value ?? 0);
            _vm.EnemyPhaseBGM2 = (uint)(NW30.Value ?? 0);
            _vm.NpcPhaseBGM2 = (uint)(NW32.Value ?? 0);
            _vm.PlayerPhaseBGMFlag4 = (uint)(NW34.Value ?? 0);
            _vm.EnemyPhaseBGMFlag4 = (uint)(NW36.Value ?? 0);
            _vm.UnknownW38 = (uint)(NW38.Value ?? 0);
            _vm.UnknownW40 = (uint)(NW40.Value ?? 0);
            _vm.UnknownW42 = (uint)(NW42.Value ?? 0);

            _vm.BreakableWallHP = (uint)(NB44.Value ?? 0);
            _vm.RatingAEliwoodNormal = (uint)(NB45.Value ?? 0);
            _vm.RatingAEliwoodHard = (uint)(NB46.Value ?? 0);
            _vm.RatingAHectorNormal = (uint)(NB47.Value ?? 0);
            _vm.RatingAHectorHard = (uint)(NB48.Value ?? 0);
            _vm.RatingBEliwoodNormal = (uint)(NB49.Value ?? 0);
            _vm.RatingBEliwoodHard = (uint)(NB50.Value ?? 0);
            _vm.RatingBHectorNormal = (uint)(NB51.Value ?? 0);
            _vm.RatingBHectorHard = (uint)(NB52.Value ?? 0);
            _vm.RatingCEliwoodNormal = (uint)(NB53.Value ?? 0);
            _vm.RatingCEliwoodHard = (uint)(NB54.Value ?? 0);
            _vm.RatingCHectorNormal = (uint)(NB55.Value ?? 0);
            _vm.RatingCHectorHard = (uint)(NB56.Value ?? 0);
            _vm.RatingDEliwoodNormal = (uint)(NB57.Value ?? 0);
            _vm.RatingDEliwoodHard = (uint)(NB58.Value ?? 0);
            _vm.RatingDHectorNormal = (uint)(NB59.Value ?? 0);
            _vm.RatingDHectorHard = (uint)(NB60.Value ?? 0);
            _vm.UnknownB61 = (uint)(NB61.Value ?? 0);

            _vm.RatingAEliwoodNormalW = (uint)(NW62.Value ?? 0);
            _vm.RatingAEliwoodHardW = (uint)(NW64.Value ?? 0);
            _vm.RatingAHectorNormalW = (uint)(NW66.Value ?? 0);
            _vm.RatingAHectorHardW = (uint)(NW68.Value ?? 0);
            _vm.RatingBEliwoodNormalW = (uint)(NW70.Value ?? 0);
            _vm.RatingBEliwoodHardW = (uint)(NW72.Value ?? 0);
            _vm.RatingBHectorNormalW = (uint)(NW74.Value ?? 0);
            _vm.RatingBHectorHardW = (uint)(NW76.Value ?? 0);
            _vm.RatingCEliwoodNormalW = (uint)(NW78.Value ?? 0);
            _vm.RatingCEliwoodHardW = (uint)(NW80.Value ?? 0);
            _vm.RatingCHectorNormalW = (uint)(NW82.Value ?? 0);
            _vm.RatingCHectorHardW = (uint)(NW84.Value ?? 0);
            _vm.RatingDEliwoodNormalW = (uint)(NW86.Value ?? 0);
            _vm.RatingDEliwoodHardW = (uint)(NW88.Value ?? 0);
            _vm.RatingDHectorNormalW = (uint)(NW90.Value ?? 0);
            _vm.RatingDHectorHardW = (uint)(NW92.Value ?? 0);
            _vm.UnknownW94 = (uint)(NW94.Value ?? 0);

            _vm.DiffPtrEliwoodNormal = ParseHexText(ND96.Text);
            _vm.DiffPtrEliwoodHard = ParseHexText(ND100.Text);
            _vm.DiffPtrHectorNormal = ParseHexText(ND104.Text);
            _vm.DiffPtrHectorHard = ParseHexText(ND108.Text);

            _vm.MapNameText1 = (uint)(NW112.Value ?? 0);
            _vm.MapNameText2 = (uint)(NW114.Value ?? 0);

            _vm.EventIdPLIST = (uint)(NB116.Value ?? 0);
            _vm.WorldMapAutoEvent = (uint)(NB117.Value ?? 0);
            _vm.UnknownB118 = (uint)(NB118.Value ?? 0);
            _vm.UnknownB119 = (uint)(NB119.Value ?? 0);
            _vm.UnknownB120 = (uint)(NB120.Value ?? 0);
            _vm.UnknownB121 = (uint)(NB121.Value ?? 0);
            _vm.UnknownB122 = (uint)(NB122.Value ?? 0);
            _vm.UnknownB123 = (uint)(NB123.Value ?? 0);
            _vm.UnknownB124 = (uint)(NB124.Value ?? 0);
            _vm.UnknownB125 = (uint)(NB125.Value ?? 0);
            _vm.UnknownB126 = (uint)(NB126.Value ?? 0);
            _vm.UnknownB127 = (uint)(NB127.Value ?? 0);
            _vm.ChapterNumber = (uint)(NB128.Value ?? 0);
            _vm.UnknownB129 = (uint)(NB129.Value ?? 0);
            _vm.UnknownB130 = (uint)(NB130.Value ?? 0);
            _vm.UnknownB131 = (uint)(NB131.Value ?? 0);
            _vm.UnknownB132 = (uint)(NB132.Value ?? 0);
            _vm.UnknownB133 = (uint)(NB133.Value ?? 0);
            _vm.VictoryBGMEnemyCount = (uint)(NB134.Value ?? 0);
            _vm.BlackoutBeforeStart = (uint)(NB135.Value ?? 0);

            _vm.ClearConditionText = (uint)(NW136.Value ?? 0);
            _vm.DetailClearConditionText = (uint)(NW138.Value ?? 0);

            _vm.SpecialDisplay = (uint)(NB140.Value ?? 0);
            _vm.TurnCountDisplay = (uint)(NB141.Value ?? 0);
            _vm.DefenseUnitMark = (uint)(NB142.Value ?? 0);
            _vm.EscapeMarkerX = (uint)(NB143.Value ?? 0);
            _vm.EscapeMarkerY = (uint)(NB144.Value ?? 0);
            _vm.UnknownB145 = (uint)(NB145.Value ?? 0);
            _vm.UnknownB146 = (uint)(NB146.Value ?? 0);
            _vm.UnknownB147 = (uint)(NB147.Value ?? 0);
        }

        void OnWriteClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                ReadUIToVM();
                _vm.WriteMapSetting();
                // Reload to confirm write
                _vm.LoadMapSetting(_vm.CurrentAddr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingView.Write failed: {0}", ex.Message);
            }
        }

        public void SelectFirstItem()
        {
            MapList.SelectFirst();
        }

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
