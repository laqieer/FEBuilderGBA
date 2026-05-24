using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapSettingFE6View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapSettingFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Map Settings (FE6)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapSettingFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWriteClick;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingFE6View.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingFE6View.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SizeLabel.Text = $"{_vm.DataSize} bytes";
            NameLabel.Text = _vm.Name;
            SelectAddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            // Section 1 — CP / Pointer
            ND0.Text = $"0x{_vm.CpPointer:X08}";

            // Section 2 — Map Style / PLIST
            NW4.Value = _vm.ObjectTypePLIST;
            NB6.Value = _vm.PalettePLIST;
            NB7.Value = _vm.ChipsetConfigPLIST;
            NB8.Value = _vm.MapPointerPLIST;
            NB9.Value = _vm.TileAnimation1PLIST;
            NB10.Value = _vm.TileAnimation2PLIST;
            NB11.Value = _vm.MapChangePLIST;

            // Section 3 — Map Properties
            NB12.Value = _vm.FogLevel;
            NB13.Value = _vm.BattlePreparation;
            NB14.Value = _vm.ChapterTitleImage;
            NB15.Value = _vm.UnknownB15;
            NB16.Value = _vm.UnknownB16;
            NB17.Value = _vm.UnknownB17;
            NB18.Value = _vm.Weather;
            NB19.Value = _vm.BattleBGLookup;

            // Section 4 — BGM / Music
            NB20.Value = _vm.PlayerPhaseBGM;
            NB21.Value = _vm.EnemyPhaseBGM;
            NB22.Value = _vm.NpcPhaseBGM;
            NB23.Value = _vm.HardBoost;
            NB24.Value = _vm.UnknownB24;
            B20SongLabel.Text = NameResolver.GetSongName(_vm.PlayerPhaseBGM);
            B21SongLabel.Text = NameResolver.GetSongName(_vm.EnemyPhaseBGM);
            B22SongLabel.Text = NameResolver.GetSongName(_vm.NpcPhaseBGM);
            B23SongLabel.Text = NameResolver.GetSongName(_vm.HardBoost);
            B24SongLabel.Text = NameResolver.GetSongName(_vm.UnknownB24);

            // Section 5 — Breakable Wall HP / Ratings
            NB25.Value = _vm.BreakableWallHP;
            NB26.Value = _vm.UnknownB26;
            NB27.Value = _vm.UnknownB27;
            NB28.Value = _vm.UnknownB28;
            NB29.Value = _vm.UnknownB29;
            NB30.Value = _vm.UnknownB30;
            NB31.Value = _vm.UnknownB31;
            NW32.Value = _vm.PlayerPhaseBGMW;
            NW34.Value = _vm.EnemyPhaseBGMW;
            NW36.Value = _vm.NpcPhaseBGMW;
            NW38.Value = _vm.UnknownW38;
            NW40.Value = _vm.UnknownW40;
            NW42.Value = _vm.UnknownW42;
            NW44.Value = _vm.UnknownW44;
            NW46.Value = _vm.UnknownW46;

            // Section 6 — Text IDs
            NW48.Value = _vm.ClearConditionText;
            NW50.Value = _vm.UpperArmyText;
            NW52.Value = _vm.LowerArmyText;
            NW54.Value = _vm.EnemyBannerFlag;
            NW56.Value = _vm.ChapterTitleText;
            W48TextLabel.Text = NameResolver.GetTextById(_vm.ClearConditionText);
            W50TextLabel.Text = NameResolver.GetTextById(_vm.UpperArmyText);
            W52TextLabel.Text = NameResolver.GetTextById(_vm.LowerArmyText);
            W56TextLabel.Text = NameResolver.GetTextById(_vm.ChapterTitleText);

            // Section 7 — World Map / Event
            NB58.Value = _vm.EventIdPLIST;
            NB59.Value = _vm.WorldMapAutoEvent;
            NW60.Value = _vm.WorldMapPlaceName;
            NB62.Value = _vm.ChapterNumber;
            NB63.Value = _vm.WorldMapX;
            NB64.Value = _vm.WorldMapY;
            NB65.Value = _vm.WorldMapPointX;
            NB66.Value = _vm.WorldMapPointY;

            // Section 8 — Victory BGM
            NB67.Value = _vm.VictoryBGMEnemyCount;
        }

        void ReadUIToVM()
        {
            // Section 1 — CP / Pointer
            _vm.CpPointer = ParseHexText(ND0.Text);

            // Section 2 — Map Style / PLIST
            _vm.ObjectTypePLIST = (uint)(NW4.Value ?? 0);
            _vm.PalettePLIST = (uint)(NB6.Value ?? 0);
            _vm.ChipsetConfigPLIST = (uint)(NB7.Value ?? 0);
            _vm.MapPointerPLIST = (uint)(NB8.Value ?? 0);
            _vm.TileAnimation1PLIST = (uint)(NB9.Value ?? 0);
            _vm.TileAnimation2PLIST = (uint)(NB10.Value ?? 0);
            _vm.MapChangePLIST = (uint)(NB11.Value ?? 0);

            // Section 3 — Map Properties
            _vm.FogLevel = (uint)(NB12.Value ?? 0);
            _vm.BattlePreparation = (uint)(NB13.Value ?? 0);
            _vm.ChapterTitleImage = (uint)(NB14.Value ?? 0);
            _vm.UnknownB15 = (uint)(NB15.Value ?? 0);
            _vm.UnknownB16 = (uint)(NB16.Value ?? 0);
            _vm.UnknownB17 = (uint)(NB17.Value ?? 0);
            _vm.Weather = (uint)(NB18.Value ?? 0);
            _vm.BattleBGLookup = (uint)(NB19.Value ?? 0);

            // Section 4 — BGM
            _vm.PlayerPhaseBGM = (uint)(NB20.Value ?? 0);
            _vm.EnemyPhaseBGM = (uint)(NB21.Value ?? 0);
            _vm.NpcPhaseBGM = (uint)(NB22.Value ?? 0);
            _vm.HardBoost = (uint)(NB23.Value ?? 0);
            _vm.UnknownB24 = (uint)(NB24.Value ?? 0);

            // Section 5 — Ratings
            _vm.BreakableWallHP = (uint)(NB25.Value ?? 0);
            _vm.UnknownB26 = (uint)(NB26.Value ?? 0);
            _vm.UnknownB27 = (uint)(NB27.Value ?? 0);
            _vm.UnknownB28 = (uint)(NB28.Value ?? 0);
            _vm.UnknownB29 = (uint)(NB29.Value ?? 0);
            _vm.UnknownB30 = (uint)(NB30.Value ?? 0);
            _vm.UnknownB31 = (uint)(NB31.Value ?? 0);
            _vm.PlayerPhaseBGMW = (uint)(NW32.Value ?? 0);
            _vm.EnemyPhaseBGMW = (uint)(NW34.Value ?? 0);
            _vm.NpcPhaseBGMW = (uint)(NW36.Value ?? 0);
            _vm.UnknownW38 = (uint)(NW38.Value ?? 0);
            _vm.UnknownW40 = (uint)(NW40.Value ?? 0);
            _vm.UnknownW42 = (uint)(NW42.Value ?? 0);
            _vm.UnknownW44 = (uint)(NW44.Value ?? 0);
            _vm.UnknownW46 = (uint)(NW46.Value ?? 0);

            // Section 6 — Text IDs
            _vm.ClearConditionText = (uint)(NW48.Value ?? 0);
            _vm.UpperArmyText = (uint)(NW50.Value ?? 0);
            _vm.LowerArmyText = (uint)(NW52.Value ?? 0);
            _vm.EnemyBannerFlag = (uint)(NW54.Value ?? 0);
            _vm.ChapterTitleText = (uint)(NW56.Value ?? 0);

            // Section 7 — World Map / Event
            _vm.EventIdPLIST = (uint)(NB58.Value ?? 0);
            _vm.WorldMapAutoEvent = (uint)(NB59.Value ?? 0);
            _vm.WorldMapPlaceName = (uint)(NW60.Value ?? 0);
            _vm.ChapterNumber = (uint)(NB62.Value ?? 0);
            _vm.WorldMapX = (uint)(NB63.Value ?? 0);
            _vm.WorldMapY = (uint)(NB64.Value ?? 0);
            _vm.WorldMapPointX = (uint)(NB65.Value ?? 0);
            _vm.WorldMapPointY = (uint)(NB66.Value ?? 0);

            // Section 8 — Victory BGM
            _vm.VictoryBGMEnemyCount = (uint)(NB67.Value ?? 0);
        }

        void OnWriteClick(object? sender, RoutedEventArgs e)
        {
            if (_vm.CurrentAddr == 0)
                return;

            _undoService.Begin("Edit FE6 Map Setting");
            try
            {
                ReadUIToVM();
                _vm.WriteMapSetting();
                _undoService.Commit();
                _vm.MarkClean();
                // Reload to confirm write
                _vm.LoadEntry(_vm.CurrentAddr);
                UpdateUI();
                CoreState.Services?.ShowInfo("Map Setting (FE6) data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapSettingFE6View.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        }
    }
}
