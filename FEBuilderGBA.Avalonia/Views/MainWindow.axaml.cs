using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using global::Avalonia.Threading;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        readonly MainWindowViewModel _vm = new();

        public MainWindow()
        {
            InitializeComponent();
            WindowManager.Instance.MainWindow = this;
            Opened += MainWindow_Opened;
        }

        private async void MainWindow_Opened(object? sender, EventArgs e)
        {
            // Auto-load ROM if --rom was specified
            if (!string.IsNullOrEmpty(App.StartupRomPath))
            {
                bool ok = LoadRomFile(App.StartupRomPath);
                if (!ok)
                {
                    if (App.SmokeTestMode)
                    {
                        Environment.ExitCode = 2;
                        Close();
                        return;
                    }
                    await MessageBoxWindow.Show(this, $"Failed to load ROM: {App.StartupRomPath}", "Error", MessageBoxMode.Ok);
                    return;
                }

                // If smoke test mode, run the editor selection test
                if (App.SmokeTestMode)
                {
                    // Use a short delay so the window is fully initialized
                    await Task.Delay(200);
                    RunSmokeTest();
                    return;
                }
            }
        }

        /// <summary>
        /// Load a ROM file and initialize all subsystems.
        /// Returns true on success.
        /// </summary>
        public bool LoadRomFile(string path)
        {
            ROM rom = new ROM();
            bool ok = rom.Load(path, out string version);
            if (!ok) return false;

            CoreState.ROM = rom;

            // Wire headless caches so Core code doesn't NullRef
            CoreState.CommentCache ??= new HeadlessEtcCache();
            CoreState.LintCache ??= new HeadlessEtcCache();
            CoreState.WorkSupportCache ??= new HeadlessEtcCache();

            // Wire text encoder — with HeadlessSystemTextEncoder fallback
            if (CoreState.SystemTextEncoder == null || CoreState.SystemTextEncoder is HeadlessSystemTextEncoder)
            {
                try
                {
                    CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, CoreState.ROM);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to init SystemTextEncoder, using headless fallback: {0}", ex.Message);
                    CoreState.SystemTextEncoder ??= new HeadlessSystemTextEncoder();
                }
            }

            // Init Huffman text encoder
            if (CoreState.FETextEncoder == null)
            {
                try { CoreState.FETextEncoder = new FETextEncode(); }
                catch (Exception ex) { Log.Error("Failed to init FETextEncode: {0}", ex.Message); }
            }

            // Init text escape
            CoreState.TextEscape ??= new TextEscape();

            // Init flag cache
            if (CoreState.FlagCache == null)
            {
                try { CoreState.FlagCache = new EtcCacheFLag(); }
                catch (Exception ex) { Log.Error("Failed to init FlagCache: {0}", ex.Message); }
            }

            // Init export function + undo
            CoreState.ExportFunction ??= new ExportFunction();
            CoreState.Undo ??= new Undo();

            // Init event scripts
            try
            {
                if (CoreState.EventScript == null)
                {
                    CoreState.EventScript = new EventScript();
                    CoreState.EventScript.Load(EventScript.EventScriptType.Event);
                }
                if (CoreState.ProcsScript == null)
                {
                    CoreState.ProcsScript = new EventScript();
                    CoreState.ProcsScript.Load(EventScript.EventScriptType.Procs);
                }
                if (CoreState.AIScript == null)
                {
                    CoreState.AIScript = new EventScript();
                    CoreState.AIScript.Load(EventScript.EventScriptType.AI);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to init EventScripts: {0}", ex.Message);
            }

            // Update UI
            _vm.UpdateFromRom();
            StatusText.Text = _vm.StatusText;
            NoRomLabel.IsVisible = false;
            EditorPanel.IsVisible = true;
            SaveMenuItem.IsEnabled = true;
            LintMenuItem.IsEnabled = true;

            return true;
        }

        private void RunSmokeTest()
        {
            try
            {
                // Test 1: Open Unit Editor and select the first item
                var unitEditor = WindowManager.Instance.Open<UnitEditorView>();

                // Test 2: Open Item Editor and select the first item
                var itemEditor = WindowManager.Instance.Open<ItemEditorView>();

                // Give the windows time to load their lists, then select items
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        await Task.Delay(500);

                        // Select first item in each editor (index 0)
                        unitEditor.SelectFirstItem();
                        await Task.Delay(200);

                        itemEditor.SelectFirstItem();
                        await Task.Delay(200);

                        // If we got here without crashing, the smoke test passed
                        Environment.ExitCode = 0;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Smoke test failed: {0}", ex.Message);
                        Environment.ExitCode = 1;
                    }
                    finally
                    {
                        WindowManager.Instance.CloseAll();
                        Close();
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Error("Smoke test failed: {0}", ex.Message);
                Environment.ExitCode = 1;
                Close();
            }
        }

        private async void OpenRom_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(this);
            if (string.IsNullOrEmpty(path)) return;

            bool ok = LoadRomFile(path);
            if (!ok)
            {
                await MessageBoxWindow.Show(this, "Failed to load ROM.", "Error", MessageBoxMode.Ok);
            }
        }

        private void SaveRom_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;
            CoreState.ROM.Save(CoreState.ROM.Filename, false);
            CoreState.Services.ShowInfo("ROM saved.");
        }

        private void Exit_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenUnits_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<UnitEditorView>();
        }

        private void OpenItems_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemEditorView>();
        }

        private void OpenImageViewer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ImageViewerView>();
        }

        private void OpenClasses_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ClassEditorView>();
        }

        private void OpenMapSettings_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapSettingView>();
        }

        private void OpenTextViewer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<TextViewerView>();
        }

        private void OpenCCBranch_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<CCBranchEditorView>();
        }

        private void OpenTerrainNames_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<TerrainNameEditorView>();
        }

        private void OpenSongTable_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SongTableView>();
        }

        private void OpenPortraits_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<PortraitViewerView>();
        }

        private void OpenMoveCost_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MoveCostEditorView>();
        }

        private void OpenSupportUnits_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SupportUnitEditorView>();
        }

        private void OpenSupportAttribute_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SupportAttributeView>();
        }

        private void OpenSupportTalk_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SupportTalkView>();
        }

        private void OpenItemWeaponEffect_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemWeaponEffectViewerView>();
        }

        private void OpenItemStatBonuses_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemStatBonusesViewerView>();
        }

        private void OpenItemEffectiveness_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemEffectivenessViewerView>();
        }

        private void OpenItemPromotion_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemPromotionViewerView>();
        }

        private void OpenItemShop_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemShopViewerView>();
        }

        private void OpenItemWeaponTriangle_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemWeaponTriangleViewerView>();
        }

        private void OpenItemUsagePointer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemUsagePointerViewerView>();
        }

        private void OpenItemEffectPointer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemEffectPointerViewerView>();
        }

        private void OpenEventCond_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<EventCondView>();
        }

        private void OpenMapChange_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapChangeView>();
        }

        private void OpenMapExitPoint_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapExitPointView>();
        }

        private void OpenMapPointer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapPointerView>();
        }

        private void OpenMapTileAnimation_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MapTileAnimationView>();
        }

        private void OpenSystemIcon_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SystemIconViewerView>();
        }

        private void OpenItemIcon_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ItemIconViewerView>();
        }

        private void OpenSystemHoverColor_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SystemHoverColorViewerView>();
        }

        private void OpenBattleBG_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<BattleBGViewerView>();
        }

        private void OpenBattleTerrain_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<BattleTerrainViewerView>();
        }

        private void OpenChapterTitle_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ChapterTitleViewerView>();
        }

        private void OpenBigCG_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<BigCGViewerView>();
        }

        private void OpenOPClassDemo_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<OPClassDemoViewerView>();
        }

        private void OpenOPClassFont_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<OPClassFontViewerView>();
        }

        private void OpenOPPrologue_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<OPPrologueViewerView>();
        }

        private void OpenSoundBossBGM_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SoundBossBGMViewerView>();
        }

        private void OpenSoundFootSteps_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SoundFootStepsViewerView>();
        }

        private void OpenSoundRoom_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SoundRoomViewerView>();
        }

        private void OpenArenaClass_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ArenaClassViewerView>();
        }

        private void OpenArenaEnemyWeapon_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<ArenaEnemyWeaponViewerView>();
        }

        private void OpenLinkArenaDenyUnit_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<LinkArenaDenyUnitViewerView>();
        }

        private void OpenMonsterProbability_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MonsterProbabilityViewerView>();
        }

        private void OpenMonsterItem_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MonsterItemViewerView>();
        }

        private void OpenMonsterWMapProbability_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MonsterWMapProbabilityViewerView>();
        }

        private void OpenSummonUnit_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SummonUnitViewerView>();
        }

        private void OpenSummonsDemonKing_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<SummonsDemonKingViewerView>();
        }

        private void OpenMenuDefinition_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MenuDefinitionView>();
        }

        private void OpenMenuCommand_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<MenuCommandView>();
        }

        private void OpenED_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<EDView>();
        }

        private void OpenEDStaffRoll_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<EDStaffRollView>();
        }

        private void OpenWorldMapPoint_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<WorldMapPointView>();
        }

        private void OpenWorldMapBGM_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<WorldMapBGMView>();
        }

        private void OpenWorldMapEventPointer_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<WorldMapEventPointerView>();
        }

        private void Lint_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;

            var scanner = new FELintScanner();
            var errors = scanner.Scan();

            if (errors.Count == 0)
            {
                _ = MessageBoxWindow.Show(this, "Lint: No errors found.", "Lint Results", MessageBoxMode.Ok);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{errors.Count} issue(s) found:");
                foreach (var err in errors)
                {
                    string severity = err.Severity == FELintCore.ErrorType.ERROR ? "ERROR" : "WARNING";
                    sb.AppendLine($"[{severity}] 0x{err.Addr:X08}: {err.ErrorMessage}");
                }
                _ = MessageBoxWindow.Show(this, sb.ToString(), "Lint Results", MessageBoxMode.Ok);
            }
        }

        private async void About_Click(object? sender, RoutedEventArgs e)
        {
            await MessageBoxWindow.Show(this,
                "FEBuilderGBA\nAvalonia Cross-Platform Preview\nCopyright 2017- GPLv3",
                "About", MessageBoxMode.Ok);
        }

        // ===================== Image Editors =====================
        private void OpenImagePortrait_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImagePortraitView>();
        private void OpenImagePortraitFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImagePortraitFE6View>();
        private void OpenImagePortraitImporter_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImagePortraitImporterView>();
        private void OpenImageBG_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBGView>();
        private void OpenImageBattleAnime_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBattleAnimeView>();
        private void OpenImageBattleAnimePallet_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBattleAnimePalletView>();
        private void OpenImageBattleBG_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBattleBGView>();
        private void OpenImageBattleScreen_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBattleScreenView>();
        private void OpenImageCG_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageCGView>();
        private void OpenImageCGFE7U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageCGFE7UView>();
        private void OpenImageUnitPalette_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageUnitPaletteView>();
        private void OpenImageUnitWaitIcon_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageUnitWaitIconView>();
        private void OpenImageUnitMoveIcon_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageUnitMoveIconView>();
        private void OpenImageSystemArea_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageSystemAreaView>();
        private void OpenImageGenericEnemyPortrait_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageGenericEnemyPortraitView>();
        private void OpenImageRomAnime_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageRomAnimeView>();
        private void OpenImageTSAEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageTSAEditorView>();
        private void OpenImageTSAAnime_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageTSAAnimeView>();
        private void OpenImageTSAAnime2_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageTSAAnime2View>();
        private void OpenImagePallet_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImagePalletView>();
        private void OpenImageMagicFEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageMagicFEditorView>();
        private void OpenImageMagicCSACreator_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageMagicCSACreatorView>();
        private void OpenImageMapActionAnimation_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageMapActionAnimationView>();
        private void OpenDecreaseColorTSATool_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<DecreaseColorTSAToolView>();

        // ===================== Event Script Editors =====================
        private void OpenEventScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventScriptView>();
        private void OpenEventUnit_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitView>();
        private void OpenEventUnitFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitFE6View>();
        private void OpenEventUnitFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitFE7View>();
        private void OpenEventUnitColor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitColorView>();
        private void OpenEventUnitItemDrop_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitItemDropView>();
        private void OpenEventUnitNewAlloc_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitNewAllocView>();
        private void OpenEventBattleTalk_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventBattleTalkView>();
        private void OpenEventBattleTalkFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventBattleTalkFE6View>();
        private void OpenEventBattleTalkFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventBattleTalkFE7View>();
        private void OpenEventBattleDataFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventBattleDataFE7View>();
        private void OpenEventHaiku_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventHaikuView>();
        private void OpenEventHaikuFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventHaikuFE6View>();
        private void OpenEventHaikuFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventHaikuFE7View>();
        private void OpenEventMapChange_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventMapChangeView>();
        private void OpenEventForceSortie_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventForceSortieView>();
        private void OpenEventForceSortieFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventForceSortieFE7View>();
        private void OpenEventFunctionPointer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventFunctionPointerView>();
        private void OpenEventFunctionPointerFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventFunctionPointerFE7View>();
        private void OpenEventAssembler_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventAssemblerView>();
        private void OpenProcsScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ProcsScriptView>();
        private void OpenEventScriptTemplate_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventScriptTemplateView>();

        // ===================== AI Script Editors =====================
        private void OpenAIScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIScriptView>();
        private void OpenAIASMCALLTALK_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIASMCALLTALKView>();
        private void OpenAIASMCoordinate_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIASMCoordinateView>();
        private void OpenAIASMRange_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIASMRangeView>();
        private void OpenAIMapSetting_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIMapSettingView>();
        private void OpenAIPerformItem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIPerformItemView>();
        private void OpenAIPerformStaff_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIPerformStaffView>();
        private void OpenAIStealItem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIStealItemView>();
        private void OpenAITarget_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AITargetView>();
        private void OpenAITiles_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AITilesView>();
        private void OpenAIUnits_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIUnitsView>();
        private void OpenAOERANGE_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AOERANGEView>();

        // ===================== Map Editors =====================
        private void OpenMapEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapEditorView>();
        private void OpenMapSettingFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingFE6View>();
        private void OpenMapSettingFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingFE7View>();
        private void OpenMapSettingFE7U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingFE7UView>();
        private void OpenMapSettingDifficulty_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingDifficultyView>();
        private void OpenMapStyleEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapStyleEditorView>();
        private void OpenMapTerrainBGLookup_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTerrainBGLookupView>();
        private void OpenMapTerrainFloorLookup_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTerrainFloorLookupView>();
        private void OpenMapMiniMapTerrainImage_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapMiniMapTerrainImageView>();
        private void OpenMapTileAnimation1_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTileAnimation1View>();
        private void OpenMapTileAnimation2_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTileAnimation2View>();
        private void OpenMapLoadFunction_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapLoadFunctionView>();
        private void OpenMapTerrainNameEng_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTerrainNameEngView>();

        // ===================== Audio Editors =====================
        private void OpenSongTrack_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackView>();
        private void OpenSongInstrument_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongInstrumentView>();
        private void OpenSongInstrumentDirectSound_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongInstrumentDirectSoundView>();
        private void OpenSongInstrumentImportWave_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongInstrumentImportWaveView>();
        private void OpenSongTrackImportMidi_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackImportMidiView>();
        private void OpenSongExchange_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongExchangeView>();
        private void OpenSoundRoomFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SoundRoomFE6View>();
        private void OpenSoundRoomCG_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SoundRoomCGView>();

        // ===================== Unit/Class Specialized =====================
        private void OpenUnitFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitFE6View>();
        private void OpenUnitActionPointer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitActionPointerView>();
        private void OpenUnitCustomBattleAnime_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitCustomBattleAnimeView>();
        private void OpenUnitIncreaseHeight_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitIncreaseHeightView>();
        private void OpenUnitPalette_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitPaletteView>();
        private void OpenClassFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ClassFE6View>();
        private void OpenClassOPDemo_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ClassOPDemoView>();
        private void OpenClassOPFont_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ClassOPFontView>();
        private void OpenExtraUnit_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ExtraUnitView>();
        private void OpenExtraUnitFE8U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ExtraUnitFE8UView>();

        // ===================== Text/Translation =====================
        private void OpenTextMain_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<TextMainView>();
        private void OpenOtherText_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OtherTextView>();
        private void OpenCString_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<CStringView>();
        private void OpenFontEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<FontEditorView>();
        private void OpenFontZH_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<FontZHView>();
        private void OpenDevTranslate_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<DevTranslateView>();
        private void OpenToolTranslateROM_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolTranslateROMView>();
        private void OpenTextEscapeEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<TextEscapeEditorView>();

        // ===================== Patch/Mod =====================
        private void OpenPatchManager_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<PatchManagerView>();
        private void OpenToolCustomBuild_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolCustomBuildView>();

        // ===================== Skill Systems =====================
        private void OpenSkillAssignmentUnitSkillSystem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentUnitSkillSystemView>();
        private void OpenSkillAssignmentClassSkillSystem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentClassSkillSystemView>();
        private void OpenSkillConfigSkillSystem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigSkillSystemView>();

        // ===================== World Map =====================
        private void OpenWorldMapPath_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapPathView>();
        private void OpenWorldMapPathEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapPathEditorView>();
        private void OpenWorldMapImage_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapImageView>();
        private void OpenWorldMapImageFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapImageFE6View>();
        private void OpenWorldMapImageFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapImageFE7View>();
        private void OpenWorldMapEventPointerFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapEventPointerFE6View>();
        private void OpenWorldMapEventPointerFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<WorldMapEventPointerFE7View>();

        // ===================== Structural Data =====================
        private void OpenCommand85Pointer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<Command85PointerView>();
        private void OpenFE8SpellMenuExtends_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<FE8SpellMenuExtendsView>();
        private void OpenStatusOption_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusOptionView>();
        private void OpenOAMSP_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OAMSPView>();
        private void OpenDumpStructSelectDialog_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<DumpStructSelectDialogView>();

        // ===================== Tools =====================
        private void OpenToolUndo_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUndoView>();
        private void OpenToolFELint_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolFELintView>();
        private void OpenToolROMRebuild_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolROMRebuildView>();
        private void OpenToolLZ77_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolLZ77View>();
        private void OpenToolDiff_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolDiffView>();
        private void OpenToolUPSPatchSimple_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUPSPatchSimpleView>();
        private void OpenToolUPSOpenSimple_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUPSOpenSimpleView>();
        private void OpenToolFlagName_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolFlagNameView>();
        private void OpenToolUseFlag_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUseFlagView>();
        private void OpenToolUnitTalkGroup_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolUnitTalkGroupView>();
        private void OpenToolASMInsert_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolASMInsertView>();
        private void OpenHexEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<HexEditorView>();
        private void OpenDisASM_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<DisASMView>();
        private void OpenLogViewer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<LogViewerView>();
        private void OpenGrowSimulator_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<GrowSimulatorView>();
        private void OpenOptions_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OptionsView>();
    }
}
