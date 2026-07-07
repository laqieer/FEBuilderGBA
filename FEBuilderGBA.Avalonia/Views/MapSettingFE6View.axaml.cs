using global::Avalonia;
using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapSettingFE6View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapSettingFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Map Settings (FE6)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Map Settings (FE6)", 1280, 900, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public MapSettingFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWriteClick;
            RefetchButton.Click += OnRefetchClick;
            ExpandListButton.Click += OnExpandListClick;
            JumpMapEditorButton.Click += OnJumpMapEditorClick;
            JumpExitPointButton.Click += OnJumpExitPointClick;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        // 再取得 — reload the entry list and reselect the current entry. Zero mutation.
        void OnRefetchClick(object? sender, RoutedEventArgs e)
        {
            uint keep = _vm.CurrentAddr;
            LoadList();
            if (keep != 0) EntryList.SelectAddress(keep);
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

                // Default = current + 1; max = 255 (WF list-expand convention).
                uint defaultCount = current + 1;
                if (defaultCount > 255) defaultCount = 255;
                uint? chosen = await NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the new map setting entry count (current: {0}, max: 255).", current),
                    R._("List Expansion"),
                    defaultCount,
                    current,
                    255);
                if (chosen == null) return; // user cancelled

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
                    Log.ErrorF("MapSettingFE6View.OnExpandListClick inner failed: {0}", inner.ToString());
                    CoreState.Services?.ShowError(R._("Map setting list expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapSettingFE6View.OnExpandListClick failed: {0}", ex.ToString());
            }
        }

        // マップエディタへJump — Avalonia ADDRESS-keyed bridge. WF J_ID_MAPEDITOR
        // passes the selected map id to MapEditorForm.JumpTo(mapid); here we pass
        // CurrentAddr because MapEditorView keys its list by the same
        // MakeMapIDList addresses (verified identical address space). Not a literal
        // WF link-value match, but equivalent.
        void OnJumpMapEditorClick(object? sender, RoutedEventArgs e)
        {
            if (_vm.CurrentAddr == 0) return;
            try { WindowManager.Instance.Navigate<MapEditorView>(_vm.CurrentAddr); }
            catch (Exception ex) { Log.ErrorF("MapSettingFE6View.JumpMapEditor failed: {0}", ex.ToString()); }
        }

        // 離脱ポイントへJump — map id -> exit-point table slot -> navigate.
        void OnJumpExitPointClick(object? sender, RoutedEventArgs e)
        {
            if (_vm.CurrentAddr == 0) return;
            try
            {
                uint mapId = _vm.CurrentMapId;
                if (mapId == U.NOT_FOUND) return;
                uint slot = MapExitPointCore.GetMapEntrySlotAddr(CoreState.ROM, mapId);
                if (slot == U.NOT_FOUND)
                {
                    CoreState.Services?.ShowInfo("No exit-point entry exists for this map.");
                    return;
                }
                WindowManager.Instance.Navigate<MapExitPointView>(slot);
            }
            catch (Exception ex) { Log.ErrorF("MapSettingFE6View.JumpExitPoint failed: {0}", ex.ToString()); }
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
                Log.ErrorF("MapSettingFE6View.LoadList failed: {0}", ex.ToString());
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
                Log.ErrorF("MapSettingFE6View.OnSelected failed: {0}", ex.ToString());
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

        /// <summary>
        /// Copy UI controls back to the ViewModel. Returns false (and shows
        /// an error dialog) when input validation fails — currently the only
        /// validated field is the D0 CP pointer hex TextBox, which must be a
        /// well-formed hex literal so a typo cannot silently write 0 to the
        /// ROM. (PR #604 Copilot CLI review blocker #3.)
        /// </summary>
        bool ReadUIToVM()
        {
            // Section 1 — CP / Pointer (validated)
            if (!ViewHelpers.TryParseHexText(ND0.Text, out uint cpPointer))
            {
                CoreState.Services?.ShowError(
                    $"CP / Pointer (D0) must be a valid hex value (e.g. 0x08123456). " +
                    $"Got: '{ND0.Text}'");
                return false;
            }
            _vm.CpPointer = cpPointer;

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
            return true;
        }

        void OnWriteClick(object? sender, RoutedEventArgs e)
        {
            if (_vm.CurrentAddr == 0)
                return;

            // #1148: in decomp mode, route chapter-setting edits to the source writer.
            if (CoreState.IsDecompMode)
            {
                if (!ReadUIToVM())
                    return;
                if (TryWriteMapSettingSource())
                    return;
                CoreState.Services?.ShowInfo(R._("This chapter is ROM-only in decomp mode. Edit the source manually and rebuild."));
                return;
            }

            _undoService.Begin("Edit FE6 Map Setting");
            try
            {
                if (!ReadUIToVM())
                {
                    _undoService.Rollback();
                    return;
                }
                _vm.WriteMapSetting();
                _undoService.Commit();

                // Reload with IsLoading guard so SetField doesn't re-dirty
                // the VM after the write completes successfully. (PR #604
                // Copilot bot inline review #1.)
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
                CoreState.Services?.ShowInfo("Map Setting (FE6) data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MapSettingFE6View.Write failed: {0}", ex.ToString());
                CoreState.Services?.ShowError(
                    $"Failed to write Map Setting (FE6): {ex.Message}");
            }
        }

        /// <summary>
        /// #1148: source-backed write of the current FE6 chapter (map_settings). Returns
        /// true when an owner exists (write attempted, status message shown), false when
        /// there is no owner (caller shows the generic ROM-only notice — never a silent
        /// ROM write). Mirrors MapSettingView.TryWriteMapSettingSource.
        /// </summary>
        bool TryWriteMapSettingSource()
        {
            var project = CoreState.DecompProject;
            var owner = project?.TryGetTableOwner("map_settings");
            if (owner == null)
                return false;

            uint mapId = _vm.CurrentMapId;
            if (mapId == U.NOT_FOUND)
            {
                CoreState.Services?.ShowError(R._("Could not resolve this chapter's entry id — source write skipped."));
                return true;
            }

            // ALL-OR-NOTHING save gate (#1148, Copilot PR #1158 re-review) — see
            // MapSettingView.TryWriteMapSettingSource for the full rationale.
            // 1) Any pointer edit → whole save is ROM-only/manual (never a partial write).
            if (_vm.HasUnsupportedFieldChanges())
            {
                CoreState.Services?.ShowInfo(R._("This chapter field is ROM-only in decomp mode (e.g. an event/difficulty pointer). Edit the source manually and rebuild."));
                return true;
            }

            // 2) All-or-nothing gate over logical fields / alias groups (Copilot PR #1158).
            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (owner.Fields != null)
                foreach (var f in owner.Fields)
                    if (f != null && !string.IsNullOrEmpty(f.Name))
                        declared.Add(f.Name);

            ChapterSaveGateResult gate = DecompChapterSaveGate.Evaluate(
                MapSettingFE6ViewModel.SourceFieldAliasGroups, _vm.BuildSourceFieldDict(), declared,
                out Dictionary<string, uint> changed);
            if (gate == ChapterSaveGateResult.UndeclaredScalar)
            {
                CoreState.Services?.ShowError(R._("This chapter edit targets a field the manifest's fields[] does not declare for map_settings — it cannot be written to source. Declare the field in the manifest, or edit the source manually and rebuild."));
                return true;
            }
            // gate == NoChange (changed empty) → fall through; the writer reports a clean no-op.

            var res = DecompSourceWriterCore.WriteTableEntry(
                project, "map_settings", (int)mapId, changed);

            switch (res.Status)
            {
                case DecompSourceWriteStatus.Ok:
                    _vm.MarkClean();
                    _vm.RefreshSourceFieldSnapshot();
                    if (res.ChangedFields != null && res.ChangedFields.Count > 0)
                        CoreState.Services?.ShowInfo(R._("Chapter source updated. Project needs rebuild."));
                    else
                        CoreState.Services?.ShowInfo(R._("No change needed — the source already matches."));
                    break;
                case DecompSourceWriteStatus.RomOnly:
                    CoreState.Services?.ShowInfo(R._("This chapter table is ROM-only in decomp mode."));
                    break;
                case DecompSourceWriteStatus.Manual:
                    CoreState.Services?.ShowInfo(res.Message);
                    break;
                default:
                    CoreState.Services?.ShowError(res.Message);
                    break;
            }
            return true;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        /// <summary>
        /// Hex-parsing alias for tests that look for the legacy local helper.
        /// Delegates to <see cref="ViewHelpers.ParseHexText"/> to keep the
        /// canonical hex-parse logic in one place. (PR #604 Copilot bot
        /// inline review #4.)
        /// </summary>
        private static uint ParseHexText(string? text) => ViewHelpers.ParseHexText(text);
    }
}
