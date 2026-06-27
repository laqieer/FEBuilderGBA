using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapSettingFE7UView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapSettingFE7UViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Map Settings (FE7U)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapSettingFE7UView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWriteClick;
            ExpandListButton.Click += OnExpandListClick;
            Opened += (_, _) => LoadList();
        }

        // リストの拡張 — expand the map-setting (chapter) table by a prompted
        // count, using FIRST-fill + complete reference repointing (#1085). The
        // whole expand runs under one UndoService scope with a byte-identical
        // fault restore inside MapSettingCore.ExpandMapSettingTable. On a
        // cancel / malformed / zero / same count this is a no-op.
        async void OnExpandListClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.ROM == null)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }

                uint current = (uint)_vm.LoadList().Count;
                if (current == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: the map setting list is empty."));
                    return;
                }

                // The list-expand UI caps the new count at 255 (WF convention). When the
                // table is already at/over that cap there is no valid larger value to pick,
                // and passing min=current to NumberInputDialog with max=255 would build an
                // invalid (min > max) range. Bail out with an explanatory message instead
                // of presenting a no-op/invalid prompt. (Copilot PR #1615 review.)
                if (current >= 255)
                {
                    CoreState.Services?.ShowInfo(R._("The map setting list is already at the maximum of 255 entries; it cannot be expanded further."));
                    return;
                }

                uint defaultCount = current + 1;
                if (defaultCount > 255) defaultCount = 255;
                uint? chosen = await NumberInputDialog.Show(
                    this,
                    R._("Enter the new map setting entry count (current: {0}, max: 255).", current),
                    R._("List Expansion"),
                    defaultCount,
                    current,
                    255);
                if (chosen == null) return;

                uint newCount = chosen.Value;
                if (newCount <= current)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count must be greater than the current count."));
                    return;
                }
                uint addCount = newCount - current;

                _undoService.Begin("Expand Map Settings");
                try
                {
                    var result = MapSettingCore.ExpandMapSettingTable(
                        CoreState.ROM, addCount, _undoService.GetActiveUndoData(), out string err);
                    if (!result.Success)
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(string.IsNullOrEmpty(err)
                            ? R._("Map setting list expansion failed.") : err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    LoadList();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded map setting list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.Error("MapSettingFE7UView.OnExpandListClick inner failed: " + inner.ToString());
                    CoreState.Services?.ShowError(R._("Map setting list expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingFE7UView.OnExpandListClick failed: " + ex.ToString());
            }
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
                Log.ErrorF("MapSettingFE7UView.LoadList failed: {0}", ex.ToString());
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
                Log.ErrorF("MapSettingFE7UView.OnSelected failed: {0}", ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"Address: 0x{_vm.CurrentAddr:X08}  (struct size: {_vm.DataSize} bytes)";

            ND0.Text = $"0x{_vm.ChapterPointer:X08}";
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
            NB16.Value = _vm.Padding16;
            NB17.Value = _vm.Padding17;
            NB18.Value = _vm.Weather;
            NB19.Value = _vm.BattleBG;

            NW20.Value = _vm.DifficultyAdjust;
            NW22.Value = _vm.PlayerPhaseBGM;
            NW24.Value = _vm.EnemyPhaseBGM;
            NW26.Value = _vm.NpcBGM;
            NW28.Value = _vm.PlayerPhaseBGM2;
            NW30.Value = _vm.EnemyPhaseBGM2;
            NW32.Value = _vm.NpcBGM2;
            NW34.Value = _vm.PlayerPhaseBGMFlag4;
            NW36.Value = _vm.EnemyPhaseBGMFlag4;
            NW38.Value = _vm.PrologueBGMCommon;
            NW40.Value = _vm.PrologueBGMEliwood;
            NW42.Value = _vm.PrologueBGMHector;

            NB44.Value = _vm.BreakableWallHP;
            NB61.Value = _vm.UnknownB61;
            NW94.Value = _vm.UnknownW94;

            ND96.Text = $"0x{_vm.EliwoodNormalPtr:X08}";
            ND100.Text = $"0x{_vm.EliwoodHardPtr:X08}";
            ND104.Text = $"0x{_vm.HectorNormalPtr:X08}";
            ND108.Text = $"0x{_vm.HectorHardPtr:X08}";

            NW112.Value = _vm.ChapterTitleEliwoodTextId;
            NW114.Value = _vm.ChapterTitleHectorTextId;
            NW116.Value = _vm.ChapterTitleEliwoodText2Id;
            NW118.Value = _vm.ChapterTitleHectorText2Id;
            W112TextLabel.Text = _vm.ChapterTitleEliwoodText;
            W114TextLabel.Text = _vm.ChapterTitleHectorText;
            W116TextLabel.Text = _vm.ChapterTitleEliwoodText2;
            W118TextLabel.Text = _vm.ChapterTitleHectorText2;

            NB120.Value = _vm.EventPLIST;
            NB121.Value = _vm.WorldMapAutoEvent;
            NW122.Value = _vm.FortuneDialogOpeningTextId;
            NW124.Value = _vm.FortuneDialogEliwoodTextId;
            NW126.Value = _vm.FortuneDialogHectorTextId;
            NW128.Value = _vm.FortuneDialogConfirmTextId;
            NB130.Value = _vm.FortunePortrait;
            NB131.Value = _vm.FortuneFee;

            NB132.Value = _vm.PrepScreenChNo1;
            NB133.Value = _vm.PrepScreenChNo2;
            NB134.Value = _vm.UnknownB134;
            NB135.Value = _vm.UnknownB135;
            NB136.Value = _vm.UnknownB136;
            NB137.Value = _vm.UnknownB137;
            NB138.Value = _vm.VictoryBGMEnemyCount;
            NB139.Value = _vm.DarkenBeforeStartEvent;

            NW140.Value = _vm.ClearConditionTextId;
            NW142.Value = _vm.DetailClearConditionTextId;
            W140TextLabel.Text = _vm.ClearConditionText;
            W142TextLabel.Text = _vm.DetailClearConditionText;

            NB144.Value = _vm.SpecialDisplay;
            NB145.Value = _vm.TurnCountDisplay;
            NB146.Value = _vm.DefenseUnitMark;
            NB147.Value = _vm.EscapeMarkerX;
            NB148.Value = _vm.EscapeMarkerY;
            NB149.Value = _vm.UnknownB149;
            NB150.Value = _vm.UnknownB150;
            NB151.Value = _vm.UnknownB151;
        }

        void ReadUIToVM()
        {
            _vm.ChapterPointer = ParseHexText(ND0.Text);
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
            _vm.Padding16 = (uint)(NB16.Value ?? 0);
            _vm.Padding17 = (uint)(NB17.Value ?? 0);
            _vm.Weather = (uint)(NB18.Value ?? 0);
            _vm.BattleBG = (uint)(NB19.Value ?? 0);

            _vm.DifficultyAdjust = (uint)(NW20.Value ?? 0);
            _vm.PlayerPhaseBGM = (uint)(NW22.Value ?? 0);
            _vm.EnemyPhaseBGM = (uint)(NW24.Value ?? 0);
            _vm.NpcBGM = (uint)(NW26.Value ?? 0);
            _vm.PlayerPhaseBGM2 = (uint)(NW28.Value ?? 0);
            _vm.EnemyPhaseBGM2 = (uint)(NW30.Value ?? 0);
            _vm.NpcBGM2 = (uint)(NW32.Value ?? 0);
            _vm.PlayerPhaseBGMFlag4 = (uint)(NW34.Value ?? 0);
            _vm.EnemyPhaseBGMFlag4 = (uint)(NW36.Value ?? 0);
            _vm.PrologueBGMCommon = (uint)(NW38.Value ?? 0);
            _vm.PrologueBGMEliwood = (uint)(NW40.Value ?? 0);
            _vm.PrologueBGMHector = (uint)(NW42.Value ?? 0);

            _vm.BreakableWallHP = (uint)(NB44.Value ?? 0);
            _vm.UnknownB61 = (uint)(NB61.Value ?? 0);
            _vm.UnknownW94 = (uint)(NW94.Value ?? 0);

            _vm.EliwoodNormalPtr = ParseHexText(ND96.Text);
            _vm.EliwoodHardPtr = ParseHexText(ND100.Text);
            _vm.HectorNormalPtr = ParseHexText(ND104.Text);
            _vm.HectorHardPtr = ParseHexText(ND108.Text);

            _vm.ChapterTitleEliwoodTextId = (uint)(NW112.Value ?? 0);
            _vm.ChapterTitleHectorTextId = (uint)(NW114.Value ?? 0);
            _vm.ChapterTitleEliwoodText2Id = (uint)(NW116.Value ?? 0);
            _vm.ChapterTitleHectorText2Id = (uint)(NW118.Value ?? 0);

            _vm.EventPLIST = (uint)(NB120.Value ?? 0);
            _vm.WorldMapAutoEvent = (uint)(NB121.Value ?? 0);
            _vm.FortuneDialogOpeningTextId = (uint)(NW122.Value ?? 0);
            _vm.FortuneDialogEliwoodTextId = (uint)(NW124.Value ?? 0);
            _vm.FortuneDialogHectorTextId = (uint)(NW126.Value ?? 0);
            _vm.FortuneDialogConfirmTextId = (uint)(NW128.Value ?? 0);
            _vm.FortunePortrait = (uint)(NB130.Value ?? 0);
            _vm.FortuneFee = (uint)(NB131.Value ?? 0);

            _vm.PrepScreenChNo1 = (uint)(NB132.Value ?? 0);
            _vm.PrepScreenChNo2 = (uint)(NB133.Value ?? 0);
            _vm.UnknownB134 = (uint)(NB134.Value ?? 0);
            _vm.UnknownB135 = (uint)(NB135.Value ?? 0);
            _vm.UnknownB136 = (uint)(NB136.Value ?? 0);
            _vm.UnknownB137 = (uint)(NB137.Value ?? 0);
            _vm.VictoryBGMEnemyCount = (uint)(NB138.Value ?? 0);
            _vm.DarkenBeforeStartEvent = (uint)(NB139.Value ?? 0);

            _vm.ClearConditionTextId = (uint)(NW140.Value ?? 0);
            _vm.DetailClearConditionTextId = (uint)(NW142.Value ?? 0);

            _vm.SpecialDisplay = (uint)(NB144.Value ?? 0);
            _vm.TurnCountDisplay = (uint)(NB145.Value ?? 0);
            _vm.DefenseUnitMark = (uint)(NB146.Value ?? 0);
            _vm.EscapeMarkerX = (uint)(NB147.Value ?? 0);
            _vm.EscapeMarkerY = (uint)(NB148.Value ?? 0);
            _vm.UnknownB149 = (uint)(NB149.Value ?? 0);
            _vm.UnknownB150 = (uint)(NB150.Value ?? 0);
            _vm.UnknownB151 = (uint)(NB151.Value ?? 0);
        }

        void OnWriteClick(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Map Setting (FE7U)");
            try
            {
                ReadUIToVM();
                _vm.WriteMapSetting();
                _undoService.Commit();
                // Reload with IsLoading guard so SetField doesn't re-dirty
                _vm.IsLoading = true;
                try
                {
                    _vm.LoadEntry(_vm.CurrentAddr);
                    UpdateUI();
                }
                finally
                {
                    _vm.IsLoading = false;
                    _vm.MarkClean();
                }
                CoreState.Services?.ShowInfo("Map Setting data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MapSettingFE7UView.Write failed: {0}", ex.ToString());
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        private static uint ParseHexText(string? text) => ViewHelpers.ParseHexText(text);
    }
}
