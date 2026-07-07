// SPDX-License-Identifier: GPL-3.0-or-later
// Shared editor catalog (#1891) — the single source of truth for the editors the
// launcher exposes. The desktop MainWindow lists ~230 editors across 28 categories via
// thin Open*_Click handlers; the single-view (WebAssembly / Android) shell MainView
// previously hard-coded only 9, so the web app showed "only a few editors". This catalog
// mirrors the desktop body editors so the single-view launcher shows the full set.
//
// Kept in sync with the desktop MainWindow by EditorCatalogParityTests
// (FEBuilderGBA.Avalonia.Tests): every desktop body editor View type must appear here.
// Every View type implements IEmbeddableEditor (asserted by EditorCatalogTests) so it
// hosts correctly in the single-view navigation host. The 6 Window-derived EventTemplate
// editors (EventTemplate1..6View : EventTemplateViewBase : TranslatedWindow) are
// intentionally EXCLUDED — they throw NotSupportedException on a single-view host — and
// are listed in EditorCatalogParityTests as the sole allowed desktop-only exclusions.
using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>One launcher entry: a labeled editor with an open action and its candidate View type(s).</summary>
    public sealed record EditorEntry(string Category, string Label, string Key, Action Open, params Type[] Views);

    /// <summary>
    /// The catalog of editors the launcher exposes, grouped by category, mirroring the desktop
    /// MainWindow body (see file header). Consumed by the single-view shell (MainView) and, via
    /// the parity tests, guarded against drift from the desktop.
    /// </summary>
    public static class EditorCatalog
    {
        static T Open<T>() where T : Control, new() => WindowManager.Instance.Open<T>();

        // ===== version / patch dispatch (mirrors the desktop Open*_Click handlers verbatim) =====

        static void OpenClasses()
        {
            if (CoreState.ROM?.RomInfo?.version == 6) Open<ClassFE6View>();
            else Open<ClassEditorView>();
        }

        static void OpenMapSettings()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) { Open<MapSettingView>(); return; }
            int ver = rom.RomInfo.version;
            if (ver == 6) Open<MapSettingFE6View>();
            else if (ver == 7)
            {
                // FE7U has a 152-byte struct, FE7JP a 148-byte struct.
                if (MapSettingCore.IsFE7ULayout(rom.RomInfo.map_setting_datasize)) Open<MapSettingFE7UView>();
                else Open<MapSettingFE7View>();
            }
            else Open<MapSettingView>();
        }

        static void OpenPortraitEditor()
        {
            // #1411 — the generic 28-byte Portrait Editor must never open on FE6 (16-byte table).
            int ver = CoreState.ROM?.RomInfo?.version ?? 0;
            if (ver == 6) Open<ImagePortraitFE6View>();
            else Open<ImagePortraitView>();
        }

        static void OpenStatBonuses()
        {
            var pds = PatchDetectionService.Instance;
            if (pds.HasSkillSystem) Open<ItemStatBonusesSkillSystemsView>();
            else if (pds.VennouWeaponLock) Open<ItemStatBonusesVennoView>();
            else Open<ItemStatBonusesViewerView>();
        }

        static void OpenEffectiveness()
        {
            if (PatchDetectionService.Instance.SkillSystemsClassTypeRework) Open<ItemEffectivenessSkillSystemsReworkView>();
            else Open<ItemEffectivenessViewerView>();
        }

        /// <summary>All launcher entries, in desktop category order.</summary>
        public static IReadOnlyList<EditorEntry> AllEntries { get; } = new EditorEntry[]
        {
            // ---- Characters ----
            new("Characters", "Units", "UnitEditor", () => Open<UnitEditorView>(), typeof(UnitEditorView)),
            new("Characters", "Classes", "ClassEditor", OpenClasses, typeof(ClassEditorView), typeof(ClassFE6View)),
            new("Characters", "Support Units", "SupportUnitEditor", () => Open<SupportUnitEditorView>(), typeof(SupportUnitEditorView)),
            new("Characters", "Support Attribute", "SupportAttribute", () => Open<SupportAttributeView>(), typeof(SupportAttributeView)),
            new("Characters", "Support Talk", "SupportTalk", () => Open<SupportTalkView>(), typeof(SupportTalkView)),

            // ---- Items ----
            new("Items", "Items", "ItemEditor", () => Open<ItemEditorView>(), typeof(ItemEditorView)),
            new("Items", "CC Branch", "CCBranchEditor", () => Open<CCBranchEditorView>(), typeof(CCBranchEditorView)),
            new("Items", "Weapon Effect", "ItemWeaponEffectViewer", () => Open<ItemWeaponEffectViewerView>(), typeof(ItemWeaponEffectViewerView)),
            new("Items", "Stat Bonuses", "ItemStatBonusesViewer", OpenStatBonuses, typeof(ItemStatBonusesViewerView), typeof(ItemStatBonusesSkillSystemsView), typeof(ItemStatBonusesVennoView)),
            new("Items", "Effectiveness", "ItemEffectivenessViewer", OpenEffectiveness, typeof(ItemEffectivenessViewerView), typeof(ItemEffectivenessSkillSystemsReworkView)),
            new("Items", "Promotion", "ItemPromotionViewer", () => Open<ItemPromotionViewerView>(), typeof(ItemPromotionViewerView)),
            new("Items", "Shop", "ItemShopViewer", () => Open<ItemShopViewerView>(), typeof(ItemShopViewerView)),
            new("Items", "Weapon Triangle", "ItemWeaponTriangleViewer", () => Open<ItemWeaponTriangleViewerView>(), typeof(ItemWeaponTriangleViewerView)),
            new("Items", "Usage Pointer", "ItemUsagePointerViewer", () => Open<ItemUsagePointerViewerView>(), typeof(ItemUsagePointerViewerView)),
            new("Items", "Effect Pointer", "ItemEffectPointerViewer", () => Open<ItemEffectPointerViewerView>(), typeof(ItemEffectPointerViewerView)),

            // ---- Maps ----
            new("Maps", "Map Settings", "MapSetting", OpenMapSettings, typeof(MapSettingView), typeof(MapSettingFE6View), typeof(MapSettingFE7View), typeof(MapSettingFE7UView)),
            new("Maps", "Terrain Names", "TerrainNameEditor", () => Open<TerrainNameEditorView>(), typeof(TerrainNameEditorView)),
            new("Maps", "Move Cost", "MoveCostEditor", () => Open<MoveCostEditorView>(), typeof(MoveCostEditorView)),
            new("Maps", "Event Conditions", "EventCond", () => Open<EventCondView>(), typeof(EventCondView)),
            new("Maps", "Map Changes", "MapChange", () => Open<MapChangeView>(), typeof(MapChangeView)),
            new("Maps", "Exit Points", "MapExitPoint", () => Open<MapExitPointView>(), typeof(MapExitPointView)),
            new("Maps", "Map Pointers", "MapPointer", () => Open<MapPointerView>(), typeof(MapPointerView)),
            new("Maps", "Tile Animation", "MapTileAnimation", () => Open<MapTileAnimationView>(), typeof(MapTileAnimationView)),

            // ---- Text ----
            new("Text", "Text Viewer", "TextViewer", () => Open<TextViewerView>(), typeof(TextViewerView)),

            // ---- Graphics ----
            new("Graphics", "Portraits", "PortraitViewer", () => Open<PortraitViewerView>(), typeof(PortraitViewerView)),
            new("Graphics", "System Icons", "SystemIconViewer", () => Open<SystemIconViewerView>(), typeof(SystemIconViewerView)),
            new("Graphics", "Item Icons", "ItemIconViewer", () => Open<ItemIconViewerView>(), typeof(ItemIconViewerView)),
            new("Graphics", "Hover Colors", "SystemHoverColorViewer", () => Open<SystemHoverColorViewerView>(), typeof(SystemHoverColorViewerView)),
            new("Graphics", "Battle BG", "BattleBGViewer", () => Open<BattleBGViewerView>(), typeof(BattleBGViewerView)),
            new("Graphics", "Battle Terrain", "BattleTerrainViewer", () => Open<BattleTerrainViewerView>(), typeof(BattleTerrainViewerView)),
            new("Graphics", "Chapter Title", "ChapterTitleViewer", () => Open<ChapterTitleViewerView>(), typeof(ChapterTitleViewerView)),
            new("Graphics", "CG Viewer", "BigCGViewer", () => Open<BigCGViewerView>(), typeof(BigCGViewerView)),
            new("Graphics", "OP Class Demo", "OPClassDemoViewer", () => Open<OPClassDemoViewerView>(), typeof(OPClassDemoViewerView)),
            new("Graphics", "OP Class Font", "OPClassFontViewer", () => Open<OPClassFontViewerView>(), typeof(OPClassFontViewerView)),
            new("Graphics", "OP Prologue", "OPPrologueViewer", () => Open<OPPrologueViewerView>(), typeof(OPPrologueViewerView)),

            // ---- Audio ----
            new("Audio", "Song Table", "SongTable", () => Open<SongTableView>(), typeof(SongTableView)),
            new("Audio", "Boss BGM", "SoundBossBGMViewer", () => Open<SoundBossBGMViewerView>(), typeof(SoundBossBGMViewerView)),
            new("Audio", "Footsteps", "SoundFootStepsViewer", () => Open<SoundFootStepsViewerView>(), typeof(SoundFootStepsViewerView)),
            new("Audio", "Sound Room", "SoundRoomViewer", () => Open<SoundRoomViewerView>(), typeof(SoundRoomViewerView)),

            // ---- Arena ----
            new("Arena", "Arena Class", "ArenaClassViewer", () => Open<ArenaClassViewerView>(), typeof(ArenaClassViewerView)),
            new("Arena", "Arena Enemy Weapon", "ArenaEnemyWeaponViewer", () => Open<ArenaEnemyWeaponViewerView>(), typeof(ArenaEnemyWeaponViewerView)),
            new("Arena", "Link Arena Deny", "LinkArenaDenyUnitViewer", () => Open<LinkArenaDenyUnitViewerView>(), typeof(LinkArenaDenyUnitViewerView)),

            // ---- Monsters ----
            new("Monsters", "Monster Probability", "MonsterProbabilityViewer", () => Open<MonsterProbabilityViewerView>(), typeof(MonsterProbabilityViewerView)),
            new("Monsters", "Monster Items", "MonsterItemViewer", () => Open<MonsterItemViewerView>(), typeof(MonsterItemViewerView)),
            new("Monsters", "WMap Probability", "MonsterWMapProbabilityViewer", () => Open<MonsterWMapProbabilityViewerView>(), typeof(MonsterWMapProbabilityViewerView)),

            // ---- Summons ----
            new("Summons", "Summon Unit", "SummonUnitViewer", () => Open<SummonUnitViewerView>(), typeof(SummonUnitViewerView)),
            new("Summons", "Demon King", "SummonsDemonKingViewer", () => Open<SummonsDemonKingViewerView>(), typeof(SummonsDemonKingViewerView)),

            // ---- Items (Specialized) ----
            new("Items (Specialized)", "Stat Bonuses (Skill)", "ItemStatBonusesSkillSystems", () => Open<ItemStatBonusesSkillSystemsView>(), typeof(ItemStatBonusesSkillSystemsView)),
            new("Items (Specialized)", "Stat Bonuses (Venno)", "ItemStatBonusesVenno", () => Open<ItemStatBonusesVennoView>(), typeof(ItemStatBonusesVennoView)),
            new("Items (Specialized)", "Effectiveness (Rework)", "ItemEffectivenessSkillSystemsRework", () => Open<ItemEffectivenessSkillSystemsReworkView>(), typeof(ItemEffectivenessSkillSystemsReworkView)),
            new("Items (Specialized)", "Random Chest", "ItemRandomChest", () => Open<ItemRandomChestView>(), typeof(ItemRandomChestView)),

            // ---- Menus ----
            new("Menus", "Menu Definition", "MenuDefinition", () => Open<MenuDefinitionView>(), typeof(MenuDefinitionView)),
            new("Menus", "Menu Command", "MenuCommand", () => Open<MenuCommandView>(), typeof(MenuCommandView)),
            new("Menus", "Split Menu Ext", "MenuExtendSplitMenu", () => Open<MenuExtendSplitMenuView>(), typeof(MenuExtendSplitMenuView)),

            // ---- Credits ----
            new("Credits", "Ending Events", "ED", () => Open<EDView>(), typeof(EDView)),
            new("Credits", "Staff Roll", "EDStaffRoll", () => Open<EDStaffRollView>(), typeof(EDStaffRollView)),
            new("Credits", "ED (FE6)", "EDFE6", () => Open<EDFE6View>(), typeof(EDFE6View)),
            new("Credits", "ED (FE7)", "EDFE7", () => Open<EDFE7View>(), typeof(EDFE7View)),
            new("Credits", "Senseki Comment", "EDSensekiComment", () => Open<EDSensekiCommentView>(), typeof(EDSensekiCommentView)),

            // ---- World Map ----
            new("World Map", "Map Points", "WorldMapPoint", () => Open<WorldMapPointView>(), typeof(WorldMapPointView)),
            new("World Map", "Map BGM", "WorldMapBGM", () => Open<WorldMapBGMView>(), typeof(WorldMapBGMView)),
            new("World Map", "Map Events", "WorldMapEventPointer", () => Open<WorldMapEventPointerView>(), typeof(WorldMapEventPointerView)),

            // ---- Image Editors ----
            new("Image Editors", "Portrait Editor", "ImagePortrait", OpenPortraitEditor, typeof(ImagePortraitView), typeof(ImagePortraitFE6View)),
            new("Image Editors", "Portrait (FE6)", "ImagePortraitFE6", () => Open<ImagePortraitFE6View>(), typeof(ImagePortraitFE6View)),
            new("Image Editors", "Portrait Import", "ImagePortraitImporter", () => Open<ImagePortraitImporterView>(), typeof(ImagePortraitImporterView)),
            new("Image Editors", "BG Editor", "ImageBG", () => Open<ImageBGView>(), typeof(ImageBGView)),
            new("Image Editors", "Battle Anim", "ImageBattleAnime", () => Open<ImageBattleAnimeView>(), typeof(ImageBattleAnimeView)),
            new("Image Editors", "Battle Anim Pal", "ImageBattleAnimePallet", () => Open<ImageBattleAnimePalletView>(), typeof(ImageBattleAnimePalletView)),
            new("Image Editors", "Battle BG Edit", "ImageBattleBG", () => Open<ImageBattleBGView>(), typeof(ImageBattleBGView)),
            new("Image Editors", "Battle Screen", "ImageBattleScreen", () => Open<ImageBattleScreenView>(), typeof(ImageBattleScreenView)),
            new("Image Editors", "CG Editor", "ImageCG", () => Open<ImageCGView>(), typeof(ImageCGView)),
            new("Image Editors", "CG (FE7U)", "ImageCGFE7U", () => Open<ImageCGFE7UView>(), typeof(ImageCGFE7UView)),
            new("Image Editors", "Unit Palette", "ImageUnitPalette", () => Open<ImageUnitPaletteView>(), typeof(ImageUnitPaletteView)),
            new("Image Editors", "Wait Icon", "ImageUnitWaitIcon", () => Open<ImageUnitWaitIconView>(), typeof(ImageUnitWaitIconView)),
            new("Image Editors", "Move Icon", "ImageUnitMoveIcon", () => Open<ImageUnitMoveIconView>(), typeof(ImageUnitMoveIconView)),
            new("Image Editors", "System Area", "ImageSystemArea", () => Open<ImageSystemAreaView>(), typeof(ImageSystemAreaView)),
            new("Image Editors", "Generic Enemy", "ImageGenericEnemyPortrait", () => Open<ImageGenericEnemyPortraitView>(), typeof(ImageGenericEnemyPortraitView)),
            new("Image Editors", "ROM Anime", "ImageRomAnime", () => Open<ImageRomAnimeView>(), typeof(ImageRomAnimeView)),
            new("Image Editors", "TSA Editor", "ImageTSAEditor", () => Open<ImageTSAEditorView>(), typeof(ImageTSAEditorView)),
            new("Image Editors", "TSA Anime", "ImageTSAAnime", () => Open<ImageTSAAnimeView>(), typeof(ImageTSAAnimeView)),
            new("Image Editors", "TSA Anime 2", "ImageTSAAnime2", () => Open<ImageTSAAnime2View>(), typeof(ImageTSAAnime2View)),
            new("Image Editors", "Palette", "ImagePallet", () => Open<ImagePalletView>(), typeof(ImagePalletView)),
            new("Image Editors", "Magic FEditor", "ImageMagicFEditor", () => Open<ImageMagicFEditorView>(), typeof(ImageMagicFEditorView)),
            new("Image Editors", "CSA Creator", "ImageMagicCSACreator", () => Open<ImageMagicCSACreatorView>(), typeof(ImageMagicCSACreatorView)),
            new("Image Editors", "Map Action Anim", "ImageMapActionAnimation", () => Open<ImageMapActionAnimationView>(), typeof(ImageMapActionAnimationView)),
            new("Image Editors", "Color Reduce", "DecreaseColorTSATool", () => Open<DecreaseColorTSAToolView>(), typeof(DecreaseColorTSAToolView)),

            // ---- Event Scripts ----
            new("Event Scripts", "Event Script", "EventScript", () => Open<EventScriptView>(), typeof(EventScriptView)),
            new("Event Scripts", "Event Unit", "EventUnit", () => Open<EventUnitView>(), typeof(EventUnitView)),
            new("Event Scripts", "Event Unit (FE6)", "EventUnitFE6", () => Open<EventUnitFE6View>(), typeof(EventUnitFE6View)),
            new("Event Scripts", "Event Unit (FE7)", "EventUnitFE7", () => Open<EventUnitFE7View>(), typeof(EventUnitFE7View)),
            new("Event Scripts", "Unit Color", "EventUnitColor", () => Open<EventUnitColorView>(), typeof(EventUnitColorView)),
            new("Event Scripts", "Item Drop", "EventUnitItemDrop", () => Open<EventUnitItemDropView>(), typeof(EventUnitItemDropView)),
            new("Event Scripts", "New Alloc", "EventUnitNewAlloc", () => Open<EventUnitNewAllocView>(), typeof(EventUnitNewAllocView)),
            new("Event Scripts", "Battle Talk", "EventBattleTalk", () => Open<EventBattleTalkView>(), typeof(EventBattleTalkView)),
            new("Event Scripts", "Battle Talk (FE6)", "EventBattleTalkFE6", () => Open<EventBattleTalkFE6View>(), typeof(EventBattleTalkFE6View)),
            new("Event Scripts", "Battle Talk (FE7)", "EventBattleTalkFE7", () => Open<EventBattleTalkFE7View>(), typeof(EventBattleTalkFE7View)),
            new("Event Scripts", "Battle Data (FE7)", "EventBattleDataFE7", () => Open<EventBattleDataFE7View>(), typeof(EventBattleDataFE7View)),
            new("Event Scripts", "Haiku", "EventHaiku", () => Open<EventHaikuView>(), typeof(EventHaikuView)),
            new("Event Scripts", "Haiku (FE6)", "EventHaikuFE6", () => Open<EventHaikuFE6View>(), typeof(EventHaikuFE6View)),
            new("Event Scripts", "Haiku (FE7)", "EventHaikuFE7", () => Open<EventHaikuFE7View>(), typeof(EventHaikuFE7View)),
            new("Event Scripts", "Map Change Evt", "EventMapChange", () => Open<EventMapChangeView>(), typeof(EventMapChangeView)),
            new("Event Scripts", "Force Sortie", "EventForceSortie", () => Open<EventForceSortieView>(), typeof(EventForceSortieView)),
            new("Event Scripts", "Force Sortie (FE7)", "EventForceSortieFE7", () => Open<EventForceSortieFE7View>(), typeof(EventForceSortieFE7View)),
            new("Event Scripts", "Func Pointer", "EventFunctionPointer", () => Open<EventFunctionPointerView>(), typeof(EventFunctionPointerView)),
            new("Event Scripts", "Func Ptr (FE7)", "EventFunctionPointerFE7", () => Open<EventFunctionPointerFE7View>(), typeof(EventFunctionPointerFE7View)),
            new("Event Scripts", "Event Assembler", "EventAssembler", () => Open<EventAssemblerView>(), typeof(EventAssemblerView)),
            new("Event Scripts", "Procs Script", "ProcsScriptCategorySelect", () => Open<ProcsScriptCategorySelectView>(), typeof(ProcsScriptCategorySelectView)),
            new("Event Scripts", "Templates", "EventScriptTemplate", () => Open<EventScriptTemplateView>(), typeof(EventScriptTemplateView)),
            new("Event Scripts", "Final Serif (FE7)", "EventFinalSerifFE7", () => Open<EventFinalSerifFE7View>(), typeof(EventFinalSerifFE7View)),
            new("Event Scripts", "Move Data (FE7)", "EventMoveDataFE7", () => Open<EventMoveDataFE7View>(), typeof(EventMoveDataFE7View)),
            new("Event Scripts", "Talk Group (FE7)", "EventTalkGroupFE7", () => Open<EventTalkGroupFE7View>(), typeof(EventTalkGroupFE7View)),

            // ---- AI Scripts ----
            new("AI Scripts", "AI Script", "AIScript", () => Open<AIScriptView>(), typeof(AIScriptView)),
            new("AI Scripts", "AI Map Setting", "AIMapSetting", () => Open<AIMapSettingView>(), typeof(AIMapSettingView)),
            new("AI Scripts", "AI Item", "AIPerformItem", () => Open<AIPerformItemView>(), typeof(AIPerformItemView)),
            new("AI Scripts", "AI Staff", "AIPerformStaff", () => Open<AIPerformStaffView>(), typeof(AIPerformStaffView)),
            new("AI Scripts", "AI Steal", "AIStealItem", () => Open<AIStealItemView>(), typeof(AIStealItemView)),
            new("AI Scripts", "AI Target", "AITarget", () => Open<AITargetView>(), typeof(AITargetView)),
            new("AI Scripts", "AOE Range", "AOERANGE", () => Open<AOERANGEView>(), typeof(AOERANGEView)),

            // ---- Map Editors ----
            new("Map Editors", "Map Editor", "MapEditor", () => Open<MapEditorView>(), typeof(MapEditorView)),
            new("Map Editors", "Map Settings (FE6)", "MapSettingFE6", () => Open<MapSettingFE6View>(), typeof(MapSettingFE6View)),
            new("Map Editors", "Map Settings (FE7)", "MapSettingFE7", () => Open<MapSettingFE7View>(), typeof(MapSettingFE7View)),
            new("Map Editors", "Map Settings (FE7U)", "MapSettingFE7U", () => Open<MapSettingFE7UView>(), typeof(MapSettingFE7UView)),
            new("Map Editors", "Difficulty", "MapSettingDifficulty", () => Open<MapSettingDifficultyView>(), typeof(MapSettingDifficultyView)),
            new("Map Editors", "Style Editor", "MapStyleEditor", () => Open<MapStyleEditorView>(), typeof(MapStyleEditorView)),
            new("Map Editors", "Terrain BG", "MapTerrainBGLookupTable", () => Open<MapTerrainBGLookupTableView>(), typeof(MapTerrainBGLookupTableView)),
            new("Map Editors", "Terrain Floor", "MapTerrainFloorLookupTable", () => Open<MapTerrainFloorLookupTableView>(), typeof(MapTerrainFloorLookupTableView)),
            new("Map Editors", "Mini Map", "MapMiniMapTerrainImage", () => Open<MapMiniMapTerrainImageView>(), typeof(MapMiniMapTerrainImageView)),
            new("Map Editors", "Tile Anim 1", "MapTileAnimation1", () => Open<MapTileAnimation1View>(), typeof(MapTileAnimation1View)),
            new("Map Editors", "Tile Anim 2", "MapTileAnimation2", () => Open<MapTileAnimation2View>(), typeof(MapTileAnimation2View)),
            new("Map Editors", "Load Function", "MapLoadFunction", () => Open<MapLoadFunctionView>(), typeof(MapLoadFunctionView)),
            new("Map Editors", "Terrain Eng", "MapTerrainNameEng", () => Open<MapTerrainNameEngView>(), typeof(MapTerrainNameEngView)),

            // ---- Audio (Advanced) ----
            new("Audio (Advanced)", "Song Track", "SongTrack", () => Open<SongTrackView>(), typeof(SongTrackView)),
            new("Audio (Advanced)", "Instrument", "SongInstrument", () => Open<SongInstrumentView>(), typeof(SongInstrumentView)),
            new("Audio (Advanced)", "Direct Sound", "SongInstrumentDirectSound", () => Open<SongInstrumentDirectSoundView>(), typeof(SongInstrumentDirectSoundView)),
            new("Audio (Advanced)", "Wave Import", "SongInstrumentImportWave", () => Open<SongInstrumentImportWaveView>(), typeof(SongInstrumentImportWaveView)),
            new("Audio (Advanced)", "MIDI Import", "SongTrackImportMidi", () => Open<SongTrackImportMidiView>(), typeof(SongTrackImportMidiView)),
            new("Audio (Advanced)", "Song Exchange", "SongExchange", () => Open<SongExchangeView>(), typeof(SongExchangeView)),
            new("Audio (Advanced)", "Sound Room (FE6)", "SoundRoomFE6", () => Open<SoundRoomFE6View>(), typeof(SoundRoomFE6View)),
            new("Audio (Advanced)", "Sound Room CG", "SoundRoomCG", () => Open<SoundRoomCGView>(), typeof(SoundRoomCGView)),
            new("Audio (Advanced)", "Change Track", "SongTrackChangeTrack", () => Open<SongTrackChangeTrackView>(), typeof(SongTrackChangeTrackView)),
            new("Audio (Advanced)", "All Change Track", "SongTrackAllChangeTrack", () => Open<SongTrackAllChangeTrackView>(), typeof(SongTrackAllChangeTrackView)),
            new("Audio (Advanced)", "Select Instrument", "SongTrackImportSelectInstrument", () => Open<SongTrackImportSelectInstrumentView>(), typeof(SongTrackImportSelectInstrumentView)),

            // ---- Unit/Class Specialized ----
            new("Unit/Class Specialized", "Unit (FE6)", "UnitFE6", () => Open<UnitFE6View>(), typeof(UnitFE6View)),
            new("Unit/Class Specialized", "Action Pointer", "UnitActionPointer", () => Open<UnitActionPointerView>(), typeof(UnitActionPointerView)),
            new("Unit/Class Specialized", "Custom Anim", "UnitCustomBattleAnime", () => Open<UnitCustomBattleAnimeView>(), typeof(UnitCustomBattleAnimeView)),
            new("Unit/Class Specialized", "Height", "UnitIncreaseHeight", () => Open<UnitIncreaseHeightView>(), typeof(UnitIncreaseHeightView)),
            new("Unit/Class Specialized", "Unit Palette", "UnitPalette", () => Open<UnitPaletteView>(), typeof(UnitPaletteView)),
            new("Unit/Class Specialized", "Class (FE6)", "ClassFE6", () => Open<ClassFE6View>(), typeof(ClassFE6View)),
            new("Unit/Class Specialized", "Class OP Demo", "ClassOPDemo", () => Open<ClassOPDemoView>(), typeof(ClassOPDemoView)),
            new("Unit/Class Specialized", "Class OP Font", "ClassOPFont", () => Open<ClassOPFontView>(), typeof(ClassOPFontView)),
            new("Unit/Class Specialized", "Extra Unit", "ExtraUnit", () => Open<ExtraUnitView>(), typeof(ExtraUnitView)),
            new("Unit/Class Specialized", "Extra (FE8U)", "ExtraUnitFE8U", () => Open<ExtraUnitFE8UView>(), typeof(ExtraUnitFE8UView)),

            // ---- Text/Translation ----
            new("Text/Translation", "Text Editor", "TextMain", () => Open<TextMainView>(), typeof(TextMainView)),
            new("Text/Translation", "Other Text", "OtherText", () => Open<OtherTextView>(), typeof(OtherTextView)),
            new("Text/Translation", "C-String", "CString", () => Open<CStringView>(), typeof(CStringView)),
            new("Text/Translation", "Font Editor", "FontEditor", () => Open<FontEditorView>(), typeof(FontEditorView)),
            new("Text/Translation", "Font ZH", "FontZH", () => Open<FontZHView>(), typeof(FontZHView)),
            new("Text/Translation", "Dev Translate", "DevTranslate", () => Open<DevTranslateView>(), typeof(DevTranslateView)),
            new("Text/Translation", "ROM Translate", "ToolTranslateROM", () => Open<ToolTranslateROMView>(), typeof(ToolTranslateROMView)),
            new("Text/Translation", "Text Escape", "TextEscapeEditor", () => Open<TextEscapeEditorView>(), typeof(TextEscapeEditorView)),

            // ---- Patches ----
            new("Patches", "Patch Manager", "PatchManager", () => Open<PatchManagerView>(), typeof(PatchManagerView)),
            new("Patches", "Custom Build", "ToolCustomBuild", () => Open<ToolCustomBuildView>(), typeof(ToolCustomBuildView)),

            // ---- Skill Systems ----
            new("Skill Systems", "Skill Unit", "SkillAssignmentUnitSkillSystem", () => Open<SkillAssignmentUnitSkillSystemView>(), typeof(SkillAssignmentUnitSkillSystemView)),
            new("Skill Systems", "Skill Class", "SkillAssignmentClassSkillSystem", () => Open<SkillAssignmentClassSkillSystemView>(), typeof(SkillAssignmentClassSkillSystemView)),
            new("Skill Systems", "Skill Config", "SkillConfigSkillSystem", () => Open<SkillConfigSkillSystemView>(), typeof(SkillConfigSkillSystemView)),

            // ---- World Map (Advanced) ----
            new("World Map (Advanced)", "Map Paths", "WorldMapPath", () => Open<WorldMapPathView>(), typeof(WorldMapPathView)),
            new("World Map (Advanced)", "Path Editor", "WorldMapPathEditor", () => Open<WorldMapPathEditorView>(), typeof(WorldMapPathEditorView)),
            new("World Map (Advanced)", "Map Image", "WorldMapImage", () => Open<WorldMapImageView>(), typeof(WorldMapImageView)),
            new("World Map (Advanced)", "Map Image (FE6)", "WorldMapImageFE6", () => Open<WorldMapImageFE6View>(), typeof(WorldMapImageFE6View)),
            new("World Map (Advanced)", "Map Image (FE7)", "WorldMapImageFE7", () => Open<WorldMapImageFE7View>(), typeof(WorldMapImageFE7View)),
            new("World Map (Advanced)", "Events (FE6)", "WorldMapEventPointerFE6", () => Open<WorldMapEventPointerFE6View>(), typeof(WorldMapEventPointerFE6View)),
            new("World Map (Advanced)", "Events (FE7)", "WorldMapEventPointerFE7", () => Open<WorldMapEventPointerFE7View>(), typeof(WorldMapEventPointerFE7View)),

            // ---- Structural Data ----
            new("Structural Data", "Cmd85 Pointer", "Command85Pointer", () => Open<Command85PointerView>(), typeof(Command85PointerView)),
            new("Structural Data", "Spell Menu Ext", "FE8SpellMenuExtends", () => Open<FE8SpellMenuExtendsView>(), typeof(FE8SpellMenuExtendsView)),
            new("Structural Data", "Status Option", "StatusOption", () => Open<StatusOptionView>(), typeof(StatusOptionView)),
            new("Structural Data", "OAM Sprite", "OAMSP", () => Open<OAMSPView>(), typeof(OAMSPView)),
            new("Structural Data", "Struct Dump", "DumpStructSelectDialog", () => Open<DumpStructSelectDialogView>(), typeof(DumpStructSelectDialogView)),

            // ---- Tools ----
            new("Tools", "Undo History", "ToolUndo", () => Open<ToolUndoView>(), typeof(ToolUndoView)),
            new("Tools", "FELint GUI", "ToolFELint", () => Open<ToolFELintView>(), typeof(ToolFELintView)),
            new("Tools", "ROM Rebuild", "ToolROMRebuild", () => Open<ToolROMRebuildView>(), typeof(ToolROMRebuildView)),
            new("Tools", "LZ77 Tool", "ToolLZ77", () => Open<ToolLZ77View>(), typeof(ToolLZ77View)),
            new("Tools", "ROM Diff", "ToolDiff", () => Open<ToolDiffView>(), typeof(ToolDiffView)),
            new("Tools", "UPS Create", "ToolUPSPatchSimple", () => Open<ToolUPSPatchSimpleView>(), typeof(ToolUPSPatchSimpleView)),
            new("Tools", "UPS Apply", "ToolUPSOpenSimple", () => Open<ToolUPSOpenSimpleView>(), typeof(ToolUPSOpenSimpleView)),
            new("Tools", "Flag Names", "ToolFlagName", () => Open<ToolFlagNameView>(), typeof(ToolFlagNameView)),
            new("Tools", "Flag Usage", "ToolUseFlag", () => Open<ToolUseFlagView>(), typeof(ToolUseFlagView)),
            new("Tools", "Talk Groups", "ToolUnitTalkGroup", () => Open<ToolUnitTalkGroupView>(), typeof(ToolUnitTalkGroupView)),
            new("Tools", "ASM Insert", "ToolASMInsert", () => Open<ToolASMInsertView>(), typeof(ToolASMInsertView)),
            new("Tools", "Hex Editor", "HexEditor", () => Open<HexEditorView>(), typeof(HexEditorView)),
            new("Tools", "Disassembler", "DisASM", () => Open<DisASMView>(), typeof(DisASMView)),
            new("Tools", "Log Viewer", "LogViewer", () => Open<LogViewerView>(), typeof(LogViewerView)),
            new("Tools", "Grow Sim", "GrowSimulator", () => Open<GrowSimulatorView>(), typeof(GrowSimulatorView)),
            new("Tools", "Options", "Options", () => Open<OptionsView>(), typeof(OptionsView)),
            new("Tools", "Pointer Tool", "PointerTool", () => Open<PointerToolView>(), typeof(PointerToolView)),
            new("Tools", "Free Space", "MoveToFreeSpace", () => Open<MoveToFreeSpaceView>(), typeof(MoveToFreeSpaceView)),
            new("Tools", "Graphics Tool", "GraphicsTool", () => Open<GraphicsToolView>(), typeof(GraphicsToolView)),
            new("Tools", "BGM Mute", "ToolBGMMuteDialog", () => Open<ToolBGMMuteDialogView>(), typeof(ToolBGMMuteDialogView)),

            // ---- Status Screen ----
            new("Status Screen", "Status Param", "StatusParam", () => Open<StatusParamView>(), typeof(StatusParamView)),
            new("Status Screen", "Status R-Menu", "StatusRMenu", () => Open<StatusRMenuView>(), typeof(StatusRMenuView)),
            new("Status Screen", "Status Units", "StatusUnitsMenu", () => Open<StatusUnitsMenuView>(), typeof(StatusUnitsMenuView)),
            new("Status Screen", "Status Options", "StatusOptionOrder", () => Open<StatusOptionOrderView>(), typeof(StatusOptionOrderView)),

            // ---- Skill Systems (Extended) ----
            new("Skill Systems (Extended)", "Unit CSkillSys", "SkillAssignmentUnitCSkillSys", () => Open<SkillAssignmentUnitCSkillSysView>(), typeof(SkillAssignmentUnitCSkillSysView)),
            new("Skill Systems (Extended)", "Class CSkillSys", "SkillAssignmentClassCSkillSys", () => Open<SkillAssignmentClassCSkillSysView>(), typeof(SkillAssignmentClassCSkillSysView)),
            new("Skill Systems (Extended)", "Unit FE8N", "SkillAssignmentUnitFE8N", () => Open<SkillAssignmentUnitFE8NView>(), typeof(SkillAssignmentUnitFE8NView)),
            new("Skill Systems (Extended)", "Config FE8N", "SkillConfigFE8NSkill", () => Open<SkillConfigFE8NSkillView>(), typeof(SkillConfigFE8NSkillView)),
            new("Skill Systems (Extended)", "Config FE8N v2", "SkillConfigFE8NVer2Skill", () => Open<SkillConfigFE8NVer2SkillView>(), typeof(SkillConfigFE8NVer2SkillView)),
            new("Skill Systems (Extended)", "Config FE8N v3", "SkillConfigFE8NVer3Skill", () => Open<SkillConfigFE8NVer3SkillView>(), typeof(SkillConfigFE8NVer3SkillView)),
            new("Skill Systems (Extended)", "Config CSkill09x", "SkillConfigFE8UCSkillSys09x", () => Open<SkillConfigFE8UCSkillSys09xView>(), typeof(SkillConfigFE8UCSkillSys09xView)),

            // ---- Version-Specific ----
            new("Version-Specific", "Items (FE6)", "ItemFE6", () => Open<ItemFE6View>(), typeof(ItemFE6View)),
            new("Version-Specific", "Move Cost (FE6)", "MoveCostFE6", () => Open<MoveCostFE6View>(), typeof(MoveCostFE6View)),
            new("Version-Specific", "Support (FE6)", "SupportUnitFE6", () => Open<SupportUnitFE6View>(), typeof(SupportUnitFE6View)),
            new("Version-Specific", "Sup Talk (FE6)", "SupportTalkFE6", () => Open<SupportTalkFE6View>(), typeof(SupportTalkFE6View)),
            new("Version-Specific", "Sup Talk (FE7)", "SupportTalkFE7", () => Open<SupportTalkFE7View>(), typeof(SupportTalkFE7View)),
            new("Version-Specific", "Units (FE7)", "UnitFE7", () => Open<UnitFE7View>(), typeof(UnitFE7View)),
            new("Version-Specific", "OP Demo (FE7)", "OPClassDemoFE7", () => Open<OPClassDemoFE7View>(), typeof(OPClassDemoFE7View)),
            new("Version-Specific", "OP Demo (FE7U)", "OPClassDemoFE7U", () => Open<OPClassDemoFE7UView>(), typeof(OPClassDemoFE7UView)),
            new("Version-Specific", "OP Demo (FE8U)", "OPClassDemoFE8U", () => Open<OPClassDemoFE8UView>(), typeof(OPClassDemoFE8UView)),
            new("Version-Specific", "OP Font (FE8U)", "OPClassFontFE8U", () => Open<OPClassFontFE8UView>(), typeof(OPClassFontFE8UView)),
            new("Version-Specific", "Alpha Name", "OPClassAlphaName", () => Open<OPClassAlphaNameView>(), typeof(OPClassAlphaNameView)),
            new("Version-Specific", "Alpha (FE6)", "OPClassAlphaNameFE6", () => Open<OPClassAlphaNameFE6View>(), typeof(OPClassAlphaNameFE6View)),
            new("Version-Specific", "Class List", "SomeClassList", () => Open<SomeClassListView>(), typeof(SomeClassListView)),
            new("Version-Specific", "Weapon Lock", "VennouWeaponLock", () => Open<VennouWeaponLockView>(), typeof(VennouWeaponLockView)),
            new("Version-Specific", "Short Text", "UnitsShortText", () => Open<UnitsShortTextView>(), typeof(UnitsShortTextView)),
        };

        /// <summary>Entries grouped by category, preserving declaration order.</summary>
        public static IReadOnlyList<IGrouping<string, EditorEntry>> Categories { get; } =
            AllEntries.GroupBy(e => e.Category).ToList();

        // ===== version applicability (mirrors MainWindow.UpdateEditorVisibility) =====

        static readonly HashSet<string> Fe8OnlyCategories = new(StringComparer.Ordinal)
        {
            "Monsters", "Summons", "Skill Systems", "Skill Systems (Extended)",
        };

        /// <summary>
        /// Whether an entry applies to the given ROM, mirroring the desktop version-hiding rules:
        /// FE8-only whole categories, "(FE6)/(FE7)/(FE7U)/(FE8)/(FE8U)" label tags, and the two
        /// special-cased buttons (Senseki Comment = FE7 only; the generic Portrait Editor is hidden
        /// on FE6). When no ROM is loaded the launcher gates on ROM presence, so this is only
        /// evaluated for a real, loaded version.
        /// </summary>
        public static bool AppliesTo(EditorEntry entry, int version, bool isMultibyte)
        {
            if (Fe8OnlyCategories.Contains(entry.Category)) return version == 8;
            bool? byTag = VersionByLabelTag(entry.Label, version, isMultibyte);
            if (byTag.HasValue) return byTag.Value;
            if (entry.Label == "Senseki Comment") return version == 7;
            if (entry.Label == "Portrait Editor") return version != 6; // #1411 generic 28-byte editor
            return true;
        }

        // Order matters: region-specific (FE7U, FE8U) before generic (FE7, FE8). Mirrors
        // MainWindow.GetVersionVisibility.
        static bool? VersionByLabelTag(string label, int ver, bool isMultibyte)
        {
            if (label.Contains("(FE7U)")) return ver == 7 && !isMultibyte;
            if (label.Contains("(FE8U)")) return ver == 8 && !isMultibyte;
            if (label.Contains("(FE6)")) return ver == 6;
            if (label.Contains("(FE7)")) return ver == 7;
            if (label.Contains("(FE8)")) return ver == 8;
            return null;
        }
    }
}
