// SPDX-License-Identifier: GPL-3.0-or-later
// #2019 — Launcher search metadata. The launcher filters used to match only the visible
// button label, so a user searching for the editor's actual window/page title (e.g.
// "Visual" for the "Map Editor" button, which opens the "Visual Map Editor") found
// nothing. This registry mirrors, as plain checked-in strings, the display titles each
// launcher entry can open, so the filters can match them WITHOUT constructing any editor
// control while the user types.
//
// Identity rules (deliberate):
//   * Desktop entries are keyed by the stable MainWindow.axaml Button Name (e.g.
//     "MapEditorButton"), never by the label — labels are localized and not unique
//     ("Unit Palette" appears twice: ImgUnitPaletteButton vs UnitPaletteButton).
//   * Single-view entries are keyed by EditorEntry.Key, which is already stable.
// Because identity never depends on a label, a language switch needs no remap: R._(...)
// is applied at match time.
//
// Alias content is the union, per mapped View type, of the declared C# IEditorView.ViewTitle
// literal, the EditorDescriptor.Title literal, and the AXAML root Title literal. Aliases are
// deduplicated ordinal-ignore-case, in declaration order. A button/entry that dispatches to
// several Views (version/patch dispatch, e.g. MapSettingsButton) carries every candidate
// title; the existing version/patch visibility gate stays authoritative about whether the
// entry is shown at all, so a visible shared entry can additionally be found by a sibling
// variant's title. That is intentionally additive discoverability, documented in README.
//
// This mirror is guarded against drift by EditorCatalogParityTests, which re-parses the
// MainWindow body, the mapped View sources and their AXAML roots and fails CI on any
// mismatch. When that guard reports drift, update the affected rows from those source titles.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Constructor-free search aliases (editor display titles) for the desktop and
    /// single-view launcher filters, plus the shared case-insensitive matcher.
    /// </summary>
    public static class EditorSearchIndex
    {
        /// <summary>Editor display-title aliases keyed by stable <see cref="EditorEntry.Key"/>.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> CatalogAliases { get; } = Freeze(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "AIMapSetting", new[] { "AI Map Settings" } },
            { "AIPerformItem", new[] { "AI Item Performance" } },
            { "AIPerformStaff", new[] { "AI Staff Performance" } },
            { "AIScript", new[] { "AI Script Editor" } },
            { "AIStealItem", new[] { "AI Steal Item Logic" } },
            { "AITarget", new[] { "AI Targeting" } },
            { "AOERANGE", new[] { "Area of Effect Range" } },
            { "ArenaClassViewer", new[] { "Arena Class", "Arena Class Editor" } },
            { "ArenaEnemyWeaponViewer", new[] { "Arena Enemy Weapon", "Arena Enemy Weapon Editor" } },
            { "BattleBGViewer", new[] { "Battle Background Editor" } },
            { "BattleTerrainViewer", new[] { "Battle Terrain Editor" } },
            { "BigCGViewer", new[] { "Big CG Editor" } },
            { "CCBranchEditor", new[] { "CC Branch Editor" } },
            { "ChapterTitleViewer", new[] { "Chapter Title Editor" } },
            { "ClassEditor", new[] { "Class Editor", "Class Editor (FE6)" } },
            { "ClassFE6", new[] { "Class Editor (FE6)" } },
            { "ClassOPDemo", new[] { "Class OP Demo Editor" } },
            { "ClassOPFont", new[] { "Class OP Font" } },
            { "Command85Pointer", new[] { "Command 0x85 Pointer" } },
            { "CString", new[] { "C-String Editor" } },
            { "DecreaseColorTSATool", new[] { "Color Reduction Tool" } },
            { "DevTranslate", new[] { "Developer Translation Tool" } },
            { "DisASM", new[] { "Disassembler" } },
            { "DumpStructSelectDialog", new[] { "Data Address Editor" } },
            { "ED", new[] { "Ending Event Editor" } },
            { "EDFE6", new[] { "Ending (FE6)" } },
            { "EDFE7", new[] { "ED (FE7)" } },
            { "EDSensekiComment", new[] { "ED Senseki Comment" } },
            { "EDStaffRoll", new[] { "Staff Roll Editor" } },
            { "EventAssembler", new[] { "Event Assembler" } },
            { "EventBattleDataFE7", new[] { "Battle Data (FE7)" } },
            { "EventBattleTalk", new[] { "Battle Dialogue Editor" } },
            { "EventBattleTalkFE6", new[] { "Battle Dialogue (FE6)" } },
            { "EventBattleTalkFE7", new[] { "Battle Dialogue (FE7)" } },
            { "EventCond", new[] { "Event Condition Editor" } },
            { "EventFinalSerifFE7", new[] { "Final Serif (FE7)" } },
            { "EventForceSortie", new[] { "Force Sortie Editor" } },
            { "EventForceSortieFE7", new[] { "Force Sortie (FE7)" } },
            { "EventFunctionPointer", new[] { "Event Function Pointer Editor" } },
            { "EventFunctionPointerFE7", new[] { "Event Function Pointer (FE7)" } },
            { "EventHaiku", new[] { "Haiku Event Editor" } },
            { "EventHaikuFE6", new[] { "Haiku (FE6)" } },
            { "EventHaikuFE7", new[] { "Haiku (FE7)" } },
            { "EventMapChange", new[] { "Map Change Event Editor" } },
            { "EventMoveDataFE7", new[] { "Move Data (FE7)" } },
            { "EventScript", new[] { "Event Script Editor" } },
            { "EventScriptTemplate", new[] { "Script Template Browser" } },
            { "EventTalkGroupFE7", new[] { "Talk Group (FE7)" } },
            { "EventUnit", new[] { "Event Unit Placement" } },
            { "EventUnitColor", new[] { "Unit Color" } },
            { "EventUnitFE6", new[] { "Event Unit (FE6)" } },
            { "EventUnitFE7", new[] { "Event Unit (FE7)" } },
            { "EventUnitItemDrop", new[] { "Unit Item Drop Editor" } },
            { "EventUnitNewAlloc", new[] { "Unit Allocation Editor" } },
            { "ExtraUnit", new[] { "Extra Unit Editor" } },
            { "ExtraUnitFE8U", new[] { "Extra Unit (FE8U)" } },
            { "FE8SpellMenuExtends", new[] { "Spell Menu Extensions" } },
            { "FontEditor", new[] { "Font Editor" } },
            { "FontZH", new[] { "Font Editor (Chinese)" } },
            { "GraphicsTool", new[] { "Graphics Tool" } },
            { "GrowSimulator", new[] { "Growth Simulator" } },
            { "HexEditor", new[] { "Hex Editor" } },
            { "ImageBattleAnime", new[] { "Battle Animation Editor" } },
            { "ImageBattleAnimePallet", new[] { "Battle Animation Palette" } },
            { "ImageBattleBG", new[] { "Battle Background Editor" } },
            { "ImageBattleScreen", new[] { "Battle Screen Layout" } },
            { "ImageBG", new[] { "Background Image Editor" } },
            { "ImageCG", new[] { "CG Image Editor" } },
            { "ImageCGFE7U", new[] { "CG Editor (FE7U)" } },
            { "ImageGenericEnemyPortrait", new[] { "Generic Enemy Portraits" } },
            { "ImageMagicCSACreator", new[] { "CSA Magic Creator" } },
            { "ImageMagicFEditor", new[] { "Magic Effect Editor (FEditor)" } },
            { "ImageMapActionAnimation", new[] { "Map Action Animation" } },
            { "ImagePallet", new[] { "Palette Editor" } },
            { "ImagePortrait", new[] { "Portrait Editor (FE6)", "Portrait Image Editor" } },
            { "ImagePortraitFE6", new[] { "Portrait Editor (FE6)" } },
            { "ImagePortraitImporter", new[] { "Portrait Import Wizard" } },
            { "ImageRomAnime", new[] { "In-ROM Magic Animation" } },
            { "ImageSystemArea", new[] { "System Area Graphics" } },
            { "ImageTSAAnime", new[] { "TSA Animation Editor" } },
            { "ImageTSAAnime2", new[] { "TSA Animation Editor v2" } },
            { "ImageTSAEditor", new[] { "TSA Tile Editor" } },
            { "ImageUnitMoveIcon", new[] { "Unit Move Icon" } },
            { "ImageUnitPalette", new[] { "Unit Palette Editor" } },
            { "ImageUnitWaitIcon", new[] { "Unit Wait Icon" } },
            { "ItemEditor", new[] { "Item Editor" } },
            { "ItemEffectivenessSkillSystemsRework", new[] { "Effectiveness (Skill Systems Rework)" } },
            { "ItemEffectivenessViewer", new[] { "Effectiveness (Skill Systems Rework)", "Item Effectiveness", "Item Effectiveness Editor" } },
            { "ItemEffectPointerViewer", new[] { "Item Effect Pointer", "Item Effect Pointer Editor" } },
            { "ItemFE6", new[] { "Items (FE6)", "Item Editor (FE6)" } },
            { "ItemIconViewer", new[] { "Item/Weapon Icon Viewer" } },
            { "ItemPromotionViewer", new[] { "Item Promotion", "Item Promotion Editor" } },
            { "ItemRandomChest", new[] { "Random Chest Items" } },
            { "ItemShopViewer", new[] { "Item Shop", "Item Shop Editor" } },
            { "ItemStatBonusesSkillSystems", new[] { "Stat Bonuses (Skill Systems)" } },
            { "ItemStatBonusesVenno", new[] { "Stat Bonuses (Venno)" } },
            { "ItemStatBonusesViewer", new[] { "Stat Bonuses (Skill Systems)", "Stat Bonuses (Venno)", "Item Stat Bonuses", "Item Stat Bonuses Editor" } },
            { "ItemUsagePointerViewer", new[] { "Item Usage Pointer", "Item Usage Pointer Editor" } },
            { "ItemWeaponEffectViewer", new[] { "Item Weapon Effect", "Item Weapon Effect Editor" } },
            { "ItemWeaponTriangleViewer", new[] { "Weapon Triangle", "Weapon Triangle Editor" } },
            { "LinkArenaDenyUnitViewer", new[] { "Link Arena Deny Unit", "Link Arena Deny Unit Editor" } },
            { "LogViewer", new[] { "Log Viewer" } },
            { "MapChange", new[] { "Map Change Editor" } },
            { "MapEditor", new[] { "Visual Map Editor" } },
            { "MapExitPoint", new[] { "Map Exit Point Editor" } },
            { "MapLoadFunction", new[] { "Map Load Functions" } },
            { "MapMiniMapTerrainImage", new[] { "Mini-Map Terrain" } },
            { "MapPointer", new[] { "Map Pointer Editor" } },
            { "MapSetting", new[] { "Map Settings (FE6)", "Map Settings (FE7U)", "Map Settings (FE7JP)", "Map Settings" } },
            { "MapSettingDifficulty", new[] { "Difficulty Settings" } },
            { "MapSettingFE6", new[] { "Map Settings (FE6)" } },
            { "MapSettingFE7", new[] { "Map Settings (FE7JP)" } },
            { "MapSettingFE7U", new[] { "Map Settings (FE7U)" } },
            { "MapStyleEditor", new[] { "Map Style Editor" } },
            { "MapTerrainBGLookupTable", new[] { "Terrain BG Lookup Table" } },
            { "MapTerrainFloorLookupTable", new[] { "Terrain Floor Lookup Table" } },
            { "MapTerrainNameEng", new[] { "Terrain Name (English)" } },
            { "MapTileAnimation", new[] { "Map Tile Animation Editor" } },
            { "MapTileAnimation1", new[] { "Map Tile Animation Type 1" } },
            { "MapTileAnimation2", new[] { "Map Tile Animation Type 2 (Palette)" } },
            { "MenuCommand", new[] { "Menu Command", "Menu Command Editor" } },
            { "MenuDefinition", new[] { "Menu Definition", "Menu Definition Editor" } },
            { "MenuExtendSplitMenu", new[] { "Menu Extend Split" } },
            { "MonsterItemViewer", new[] { "Monster Item", "Monster Item Editor" } },
            { "MonsterProbabilityViewer", new[] { "Monster Probability", "Monster Probability Editor" } },
            { "MonsterWMapProbabilityViewer", new[] { "World Map Monster", "World Map Monster Editor" } },
            { "MoveCostEditor", new[] { "Move Cost Editor" } },
            { "MoveCostFE6", new[] { "Move Cost (FE6)", "Move Cost (FE6) Editor" } },
            { "MoveToFreeSpace", new[] { "Move to Free Space" } },
            { "OAMSP", new[] { "Special OAM" } },
            { "OPClassAlphaName", new[] { "OP Class Alpha Name Editor" } },
            { "OPClassAlphaNameFE6", new[] { "OP Class Alpha Name (FE6) Editor" } },
            { "OPClassDemoFE7", new[] { "OP Class Demo (FE7) Editor" } },
            { "OPClassDemoFE7U", new[] { "OP Class Demo (FE7U) Editor" } },
            { "OPClassDemoFE8U", new[] { "OP Class Demo (FE8U) Editor" } },
            { "OPClassDemoViewer", new[] { "OP Class Demo Editor" } },
            { "OPClassFontFE8U", new[] { "OP Class Font (FE8U) Editor" } },
            { "OPClassFontViewer", new[] { "OP Class Font Editor" } },
            { "OPPrologueViewer", new[] { "OP Prologue Editor" } },
            { "Options", new[] { "Options" } },
            { "OtherText", new[] { "Other Text Strings" } },
            { "PatchManager", new[] { "Patch Manager" } },
            { "PointerTool", new[] { "Pointer Tool" } },
            { "PortraitViewer", new[] { "Portrait Editor" } },
            { "ProcsScriptCategorySelect", new[] { "Procs Script Editor" } },
            { "SkillAssignmentClassCSkillSys", new[] { "Skill Assignment - Class (CSkillSys)" } },
            { "SkillAssignmentClassSkillSystem", new[] { "Skill Assignment (Class)" } },
            { "SkillAssignmentUnitCSkillSys", new[] { "Skill Assignment - Unit (CSkillSys)" } },
            { "SkillAssignmentUnitFE8N", new[] { "Skill Assignment - Unit (FE8N)" } },
            { "SkillAssignmentUnitSkillSystem", new[] { "Skill Assignment (Unit)" } },
            { "SkillConfigFE8NSkill", new[] { "Skill Configuration (FE8N)" } },
            { "SkillConfigFE8NVer2Skill", new[] { "Skill Configuration (FE8N v2)" } },
            { "SkillConfigFE8NVer3Skill", new[] { "Skill Configuration (FE8N v3)" } },
            { "SkillConfigFE8UCSkillSys09x", new[] { "Skill Configuration (CSkillSys 0.9.x)" } },
            { "SkillConfigSkillSystem", new[] { "Skill Config (SkillSystem)" } },
            { "SomeClassList", new[] { "Class List Editor" } },
            { "SongExchange", new[] { "Song Exchange Tool" } },
            { "SongInstrument", new[] { "Instrument Editor" } },
            { "SongInstrumentDirectSound", new[] { "Direct Sound Instruments" } },
            { "SongInstrumentImportWave", new[] { "Wave Import" } },
            { "SongTable", new[] { "Song Table", "Song Table Editor" } },
            { "SongTrack", new[] { "Song Track Editor" } },
            { "SongTrackAllChangeTrack", new[] { "Bulk Track Change" } },
            { "SongTrackChangeTrack", new[] { "Track Change" } },
            { "SongTrackImportMidi", new[] { "MIDI Import" } },
            { "SongTrackImportSelectInstrument", new[] { "Instrument Selection" } },
            { "SoundBossBGMViewer", new[] { "Boss BGM", "Boss BGM Editor" } },
            { "SoundFootStepsViewer", new[] { "Footstep Sounds", "Footstep Sounds Editor" } },
            { "SoundRoomCG", new[] { "Sound Room CG" } },
            { "SoundRoomFE6", new[] { "Sound Room (FE6)" } },
            { "SoundRoomViewer", new[] { "Sound Room", "Sound Room Editor" } },
            { "StatusOption", new[] { "Status Screen Options" } },
            { "StatusOptionOrder", new[] { "Status Option Order", "Status Option Order Editor" } },
            { "StatusParam", new[] { "Status Parameters", "Status Parameters Editor" } },
            { "StatusRMenu", new[] { "Status R-Menu", "Status R-Menu Editor" } },
            { "StatusUnitsMenu", new[] { "Status Units Menu", "Status Units Menu Editor" } },
            { "SummonsDemonKingViewer", new[] { "Demon King Summon", "Demon King Summon Editor" } },
            { "SummonUnitViewer", new[] { "Summon Unit", "Summon Unit Editor" } },
            { "SupportAttribute", new[] { "Support Attribute", "Support Attribute Editor" } },
            { "SupportTalk", new[] { "Support Talk" } },
            { "SupportTalkFE6", new[] { "Support Talk (FE6)" } },
            { "SupportTalkFE7", new[] { "Support Talk (FE7)" } },
            { "SupportUnitEditor", new[] { "Support Unit Editor" } },
            { "SupportUnitFE6", new[] { "Support Units (FE6)" } },
            { "SystemHoverColorViewer", new[] { "System Area Color Viewer" } },
            { "SystemIconViewer", new[] { "System Icon Viewer" } },
            { "TerrainNameEditor", new[] { "Terrain Name Editor" } },
            { "TextEscapeEditor", new[] { "Text Escape Sequences" } },
            { "TextMain", new[] { "Text Editor" } },
            { "TextViewer", new[] { "Text Editor" } },
            { "ToolASMInsert", new[] { "Add via ASM/C" } },
            { "ToolBGMMuteDialog", new[] { "BGM Mute Settings" } },
            { "ToolCustomBuild", new[] { "Custom Build" } },
            { "ToolDiff", new[] { "ROM Diff Tool" } },
            { "ToolFELint", new[] { "FELint GUI" } },
            { "ToolFlagName", new[] { "Flag Name Editor" } },
            { "ToolLZ77", new[] { "LZ77 Compression Tool" } },
            { "ToolROMRebuild", new[] { "ROM Rebuild Tool" } },
            { "ToolTranslateROM", new[] { "ROM Translation Tool" } },
            { "ToolUndo", new[] { "Undo History Viewer" } },
            { "ToolUnitTalkGroup", new[] { "Unit Talk Group" } },
            { "ToolUPSOpenSimple", new[] { "UPS Patch Applier" } },
            { "ToolUPSPatchSimple", new[] { "UPS Patch Creator" } },
            { "ToolUseFlag", new[] { "Flags Used in Chapter" } },
            { "UnitActionPointer", new[] { "Unit Action Pointers" } },
            { "UnitCustomBattleAnime", new[] { "Custom Battle Animation" } },
            { "UnitEditor", new[] { "Unit Editor" } },
            { "UnitFE6", new[] { "Unit Editor (FE6)" } },
            { "UnitFE7", new[] { "Units (FE7) Editor" } },
            { "UnitIncreaseHeight", new[] { "Unit Height Adjustment" } },
            { "UnitPalette", new[] { "Unit Palette Assignment" } },
            { "UnitsShortText", new[] { "Units Short Text Editor" } },
            { "VennouWeaponLock", new[] { "Weapon Lock (Vennou) Editor" } },
            { "WorldMapBGM", new[] { "World Map BGM", "World Map BGM Editor" } },
            { "WorldMapEventPointer", new[] { "World Map Event", "World Map Event Editor" } },
            { "WorldMapEventPointerFE6", new[] { "Event Pointer (FE6)" } },
            { "WorldMapEventPointerFE7", new[] { "World Map Event (FE7)", "Event Pointer (FE7)" } },
            { "WorldMapImage", new[] { "World Map Image" } },
            { "WorldMapImageFE6", new[] { "World Map Image (FE6)" } },
            { "WorldMapImageFE7", new[] { "World Map Image (FE7)" } },
            { "WorldMapPath", new[] { "World Map Paths" } },
            { "WorldMapPathEditor", new[] { "Path Editor" } },
            { "WorldMapPoint", new[] { "World Map Point", "World Map Point Editor" } },
        });

        /// <summary>Editor display-title aliases keyed by the stable desktop MainWindow Button Name.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> DesktopAliases { get; } = Freeze(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "ActionPointerButton", new[] { "Unit Action Pointers" } },
            { "AIItemButton", new[] { "AI Item Performance" } },
            { "AIMapSettingButton", new[] { "AI Map Settings" } },
            { "AIScriptButton", new[] { "AI Script Editor" } },
            { "AIStaffButton", new[] { "AI Staff Performance" } },
            { "AIStealButton", new[] { "AI Steal Item Logic" } },
            { "AITargetButton", new[] { "AI Targeting" } },
            { "AllChangeTrackButton", new[] { "Bulk Track Change" } },
            { "AlphaFE6Button", new[] { "OP Class Alpha Name (FE6) Editor" } },
            { "AlphaNameButton", new[] { "OP Class Alpha Name Editor" } },
            { "AOERangeButton", new[] { "Area of Effect Range" } },
            { "ArenaClassButton", new[] { "Arena Class", "Arena Class Editor" } },
            { "ArenaEnemyWeaponButton", new[] { "Arena Enemy Weapon", "Arena Enemy Weapon Editor" } },
            { "ASMInsertButton", new[] { "Add via ASM/C" } },
            { "BattleAnimButton", new[] { "Battle Animation Editor" } },
            { "BattleAnimPalButton", new[] { "Battle Animation Palette" } },
            { "BattleBGButton", new[] { "Battle Background Editor" } },
            { "BattleBGEditButton", new[] { "Battle Background Editor" } },
            { "BattleDataFE7Button", new[] { "Battle Data (FE7)" } },
            { "BattleScreenButton", new[] { "Battle Screen Layout" } },
            { "BattleTalkButton", new[] { "Battle Dialogue Editor" } },
            { "BattleTalkFE6Button", new[] { "Battle Dialogue (FE6)" } },
            { "BattleTalkFE7Button", new[] { "Battle Dialogue (FE7)" } },
            { "BattleTerrainButton", new[] { "Battle Terrain Editor" } },
            { "BGEditorButton", new[] { "Background Image Editor" } },
            { "BGMMuteButton", new[] { "BGM Mute Settings" } },
            { "BossBGMButton", new[] { "Boss BGM", "Boss BGM Editor" } },
            { "CCBranchButton", new[] { "CC Branch Editor" } },
            { "CGEditorButton", new[] { "CG Image Editor" } },
            { "CGFE7UButton", new[] { "CG Editor (FE7U)" } },
            { "CGViewerButton", new[] { "Big CG Editor" } },
            { "ChangeTrackButton", new[] { "Track Change" } },
            { "ChapterTitleButton", new[] { "Chapter Title Editor" } },
            { "ClassCSkillSysButton", new[] { "Skill Assignment - Class (CSkillSys)" } },
            { "ClassesButton", new[] { "Class Editor", "Class Editor (FE6)" } },
            { "ClassFE6Button", new[] { "Class Editor (FE6)" } },
            { "ClassListButton", new[] { "Class List Editor" } },
            { "ClassOPDemoButton", new[] { "Class OP Demo Editor" } },
            { "ClassOPFontButton", new[] { "Class OP Font" } },
            { "Cmd85PointerButton", new[] { "Command 0x85 Pointer" } },
            { "ColorReduceButton", new[] { "Color Reduction Tool" } },
            { "ConfigCSkill09xButton", new[] { "Skill Configuration (CSkillSys 0.9.x)" } },
            { "ConfigFE8NButton", new[] { "Skill Configuration (FE8N)" } },
            { "ConfigFE8Nv2Button", new[] { "Skill Configuration (FE8N v2)" } },
            { "ConfigFE8Nv3Button", new[] { "Skill Configuration (FE8N v3)" } },
            { "CSACreatorButton", new[] { "CSA Magic Creator" } },
            { "CStringButton", new[] { "C-String Editor" } },
            { "CustomAnimButton", new[] { "Custom Battle Animation" } },
            { "CustomBuildButton", new[] { "Custom Build" } },
            { "DemonKingButton", new[] { "Demon King Summon", "Demon King Summon Editor" } },
            { "DevTranslateButton", new[] { "Developer Translation Tool" } },
            { "DifficultyButton", new[] { "Difficulty Settings" } },
            { "DirectSoundButton", new[] { "Direct Sound Instruments" } },
            { "DisassemblerButton", new[] { "Disassembler" } },
            { "EDFE6Button", new[] { "Ending (FE6)" } },
            { "EDFE7Button", new[] { "ED (FE7)" } },
            { "EffectivenessButton", new[] { "Effectiveness (Skill Systems Rework)", "Item Effectiveness", "Item Effectiveness Editor" } },
            { "EffectivenessReworkButton", new[] { "Effectiveness (Skill Systems Rework)" } },
            { "EffectPointerButton", new[] { "Item Effect Pointer", "Item Effect Pointer Editor" } },
            { "EndingEventsButton", new[] { "Ending Event Editor" } },
            { "EventAssemblerButton", new[] { "Event Assembler" } },
            { "EventConditionsButton", new[] { "Event Condition Editor" } },
            { "EventScriptButton", new[] { "Event Script Editor" } },
            { "EventUnitButton", new[] { "Event Unit Placement" } },
            { "EventUnitFE6Button", new[] { "Event Unit (FE6)" } },
            { "EventUnitFE7Button", new[] { "Event Unit (FE7)" } },
            { "ExitPointsButton", new[] { "Map Exit Point Editor" } },
            { "ExtraFE8UButton", new[] { "Extra Unit (FE8U)" } },
            { "ExtraUnitButton", new[] { "Extra Unit Editor" } },
            { "FELintGUIButton", new[] { "FELint GUI" } },
            { "FinalSerifFE7Button", new[] { "Final Serif (FE7)" } },
            { "FlagNamesButton", new[] { "Flag Name Editor" } },
            { "FlagUsageButton", new[] { "Flags Used in Chapter" } },
            { "FontEditorButton", new[] { "Font Editor" } },
            { "FontZHButton", new[] { "Font Editor (Chinese)" } },
            { "FootstepsButton", new[] { "Footstep Sounds", "Footstep Sounds Editor" } },
            { "ForceSortieButton", new[] { "Force Sortie Editor" } },
            { "ForceSortieFE7Button", new[] { "Force Sortie (FE7)" } },
            { "FreeSpaceButton", new[] { "Move to Free Space" } },
            { "FuncPointerButton", new[] { "Event Function Pointer Editor" } },
            { "FuncPtrFE7Button", new[] { "Event Function Pointer (FE7)" } },
            { "GenericEnemyButton", new[] { "Generic Enemy Portraits" } },
            { "GraphicsToolButton", new[] { "Graphics Tool" } },
            { "GrowSimButton", new[] { "Growth Simulator" } },
            { "HaikuButton", new[] { "Haiku Event Editor" } },
            { "HaikuFE6Button", new[] { "Haiku (FE6)" } },
            { "HaikuFE7Button", new[] { "Haiku (FE7)" } },
            { "HeightButton", new[] { "Unit Height Adjustment" } },
            { "HexEditorButton", new[] { "Hex Editor" } },
            { "HoverColorsButton", new[] { "System Area Color Viewer" } },
            { "ImgUnitPaletteButton", new[] { "Unit Palette Editor" } },
            { "InstrumentButton", new[] { "Instrument Editor" } },
            { "ItemDropButton", new[] { "Unit Item Drop Editor" } },
            { "ItemIconsButton", new[] { "Item/Weapon Icon Viewer" } },
            { "ItemsButton", new[] { "Item Editor" } },
            { "ItemsFE6Button", new[] { "Items (FE6)", "Item Editor (FE6)" } },
            { "LinkArenaDenyButton", new[] { "Link Arena Deny Unit", "Link Arena Deny Unit Editor" } },
            { "LoadFunctionButton", new[] { "Map Load Functions" } },
            { "LogViewerButton", new[] { "Log Viewer" } },
            { "LZ77ToolButton", new[] { "LZ77 Compression Tool" } },
            { "MagicFEditorButton", new[] { "Magic Effect Editor (FEditor)" } },
            { "MapActionAnimButton", new[] { "Map Action Animation" } },
            { "MapBGMButton", new[] { "World Map BGM", "World Map BGM Editor" } },
            { "MapChangeEvtButton", new[] { "Map Change Event Editor" } },
            { "MapChangesButton", new[] { "Map Change Editor" } },
            { "MapEditorButton", new[] { "Visual Map Editor" } },
            { "MapEventsButton", new[] { "World Map Event", "World Map Event Editor" } },
            { "MapPathsButton", new[] { "World Map Paths" } },
            { "MapPointersButton", new[] { "Map Pointer Editor" } },
            { "MapPointsButton", new[] { "World Map Point", "World Map Point Editor" } },
            { "MapSettingsButton", new[] { "Map Settings (FE6)", "Map Settings (FE7U)", "Map Settings (FE7JP)", "Map Settings" } },
            { "MapSettingsFE6Button", new[] { "Map Settings (FE6)" } },
            { "MapSettingsFE7Button", new[] { "Map Settings (FE7JP)" } },
            { "MapSettingsFE7UButton", new[] { "Map Settings (FE7U)" } },
            { "MenuCommandButton", new[] { "Menu Command", "Menu Command Editor" } },
            { "MenuDefinitionButton", new[] { "Menu Definition", "Menu Definition Editor" } },
            { "MIDIImportButton", new[] { "MIDI Import" } },
            { "MiniMapButton", new[] { "Mini-Map Terrain" } },
            { "MonsterItemsButton", new[] { "Monster Item", "Monster Item Editor" } },
            { "MonsterProbabilityButton", new[] { "Monster Probability", "Monster Probability Editor" } },
            { "MoveCostButton", new[] { "Move Cost Editor" } },
            { "MoveCostFE6Button", new[] { "Move Cost (FE6)", "Move Cost (FE6) Editor" } },
            { "MoveDataFE7Button", new[] { "Move Data (FE7)" } },
            { "MoveIconButton", new[] { "Unit Move Icon" } },
            { "NewAllocButton", new[] { "Unit Allocation Editor" } },
            { "OAMSpriteButton", new[] { "Special OAM" } },
            { "OPClassDemoButton", new[] { "OP Class Demo Editor" } },
            { "OPClassFontButton", new[] { "OP Class Font Editor" } },
            { "OPDemoFE7Button", new[] { "OP Class Demo (FE7) Editor" } },
            { "OPDemoFE7UButton", new[] { "OP Class Demo (FE7U) Editor" } },
            { "OPDemoFE8UButton", new[] { "OP Class Demo (FE8U) Editor" } },
            { "OPFontFE8UButton", new[] { "OP Class Font (FE8U) Editor" } },
            { "OPPrologueButton", new[] { "OP Prologue Editor" } },
            { "OptionsButton", new[] { "Options" } },
            { "OtherTextButton", new[] { "Other Text Strings" } },
            { "PaletteButton", new[] { "Palette Editor" } },
            { "PatchManagerButton", new[] { "Patch Manager" } },
            { "PathEditorButton", new[] { "Path Editor" } },
            { "PointerToolButton", new[] { "Pointer Tool" } },
            { "PortraitEditorButton", new[] { "Portrait Editor (FE6)", "Portrait Image Editor" } },
            { "PortraitFE6Button", new[] { "Portrait Editor (FE6)" } },
            { "PortraitImportButton", new[] { "Portrait Import Wizard" } },
            { "PortraitsButton", new[] { "Portrait Editor" } },
            { "ProcsScriptButton", new[] { "Procs Script Editor" } },
            { "PromotionButton", new[] { "Item Promotion", "Item Promotion Editor" } },
            { "RandomChestButton", new[] { "Random Chest Items" } },
            { "ROMAnimeButton", new[] { "In-ROM Magic Animation" } },
            { "ROMDiffButton", new[] { "ROM Diff Tool" } },
            { "ROMRebuildButton", new[] { "ROM Rebuild Tool" } },
            { "ROMTranslateButton", new[] { "ROM Translation Tool" } },
            { "SelectInstrumentButton", new[] { "Instrument Selection" } },
            { "SensekiCommentButton", new[] { "ED Senseki Comment" } },
            { "ShopButton", new[] { "Item Shop", "Item Shop Editor" } },
            { "ShortTextButton", new[] { "Units Short Text Editor" } },
            { "SkillClassButton", new[] { "Skill Assignment (Class)" } },
            { "SkillConfigButton", new[] { "Skill Config (SkillSystem)" } },
            { "SkillUnitButton", new[] { "Skill Assignment (Unit)" } },
            { "SongExchangeButton", new[] { "Song Exchange Tool" } },
            { "SongTableButton", new[] { "Song Table", "Song Table Editor" } },
            { "SongTrackButton", new[] { "Song Track Editor" } },
            { "SoundRoomButton", new[] { "Sound Room", "Sound Room Editor" } },
            { "SoundRoomCGButton", new[] { "Sound Room CG" } },
            { "SoundRoomFE6Button", new[] { "Sound Room (FE6)" } },
            { "SpellMenuExtButton", new[] { "Spell Menu Extensions" } },
            { "SplitMenuExtButton", new[] { "Menu Extend Split" } },
            { "StaffRollButton", new[] { "Staff Roll Editor" } },
            { "StatBonusesButton", new[] { "Stat Bonuses (Skill Systems)", "Stat Bonuses (Venno)", "Item Stat Bonuses", "Item Stat Bonuses Editor" } },
            { "StatBonusesSkillButton", new[] { "Stat Bonuses (Skill Systems)" } },
            { "StatBonusesVennoButton", new[] { "Stat Bonuses (Venno)" } },
            { "StatusOptionButton", new[] { "Status Screen Options" } },
            { "StatusOptionsButton", new[] { "Status Option Order", "Status Option Order Editor" } },
            { "StatusParamButton", new[] { "Status Parameters", "Status Parameters Editor" } },
            { "StatusRMenuButton", new[] { "Status R-Menu", "Status R-Menu Editor" } },
            { "StatusUnitsButton", new[] { "Status Units Menu", "Status Units Menu Editor" } },
            { "StructDumpButton", new[] { "Data Address Editor" } },
            { "StyleEditorButton", new[] { "Map Style Editor" } },
            { "SummonUnitButton", new[] { "Summon Unit", "Summon Unit Editor" } },
            { "SupportAttributeButton", new[] { "Support Attribute", "Support Attribute Editor" } },
            { "SupportFE6Button", new[] { "Support Units (FE6)" } },
            { "SupportTalkButton", new[] { "Support Talk" } },
            { "SupportUnitsButton", new[] { "Support Unit Editor" } },
            { "SupTalkFE6Button", new[] { "Support Talk (FE6)" } },
            { "SupTalkFE7Button", new[] { "Support Talk (FE7)" } },
            { "SystemAreaButton", new[] { "System Area Graphics" } },
            { "SystemIconsButton", new[] { "System Icon Viewer" } },
            { "TalkGroupFE7Button", new[] { "Talk Group (FE7)" } },
            { "TalkGroupsButton", new[] { "Unit Talk Group" } },
            { "Template1Button", new[] { "Event Template 1" } },
            { "Template2Button", new[] { "Event Template 2" } },
            { "Template3Button", new[] { "Event Template 3" } },
            { "Template4Button", new[] { "Event Template 4" } },
            { "Template5Button", new[] { "Event Template 5" } },
            { "Template6Button", new[] { "Event Template 6" } },
            { "TemplatesButton", new[] { "Script Template Browser" } },
            { "TerrainBGButton", new[] { "Terrain BG Lookup Table" } },
            { "TerrainEngButton", new[] { "Terrain Name (English)" } },
            { "TerrainFloorButton", new[] { "Terrain Floor Lookup Table" } },
            { "TerrainNamesButton", new[] { "Terrain Name Editor" } },
            { "TextEditorButton", new[] { "Text Editor" } },
            { "TextEscapeButton", new[] { "Text Escape Sequences" } },
            { "TextViewerButton", new[] { "Text Editor" } },
            { "TileAnim1Button", new[] { "Map Tile Animation Type 1" } },
            { "TileAnim2Button", new[] { "Map Tile Animation Type 2 (Palette)" } },
            { "TileAnimationButton", new[] { "Map Tile Animation Editor" } },
            { "TSAAnime2Button", new[] { "TSA Animation Editor v2" } },
            { "TSAAnimeButton", new[] { "TSA Animation Editor" } },
            { "TSAEditorButton", new[] { "TSA Tile Editor" } },
            { "UndoHistoryButton", new[] { "Undo History Viewer" } },
            { "UnitColorButton", new[] { "Unit Color" } },
            { "UnitCSkillSysButton", new[] { "Skill Assignment - Unit (CSkillSys)" } },
            { "UnitFE6Button", new[] { "Unit Editor (FE6)" } },
            { "UnitFE8NButton", new[] { "Skill Assignment - Unit (FE8N)" } },
            { "UnitPaletteButton", new[] { "Unit Palette Assignment" } },
            { "UnitsButton", new[] { "Unit Editor" } },
            { "UnitsFE7Button", new[] { "Units (FE7) Editor" } },
            { "UPSApplyButton", new[] { "UPS Patch Applier" } },
            { "UPSCreateButton", new[] { "UPS Patch Creator" } },
            { "UsagePointerButton", new[] { "Item Usage Pointer", "Item Usage Pointer Editor" } },
            { "WaitIconButton", new[] { "Unit Wait Icon" } },
            { "WaveImportButton", new[] { "Wave Import" } },
            { "WeaponEffectButton", new[] { "Item Weapon Effect", "Item Weapon Effect Editor" } },
            { "WeaponLockButton", new[] { "Weapon Lock (Vennou) Editor" } },
            { "WeaponTriangleButton", new[] { "Weapon Triangle", "Weapon Triangle Editor" } },
            { "WMapEventsFE6Button", new[] { "Event Pointer (FE6)" } },
            { "WMapEventsFE7Button", new[] { "World Map Event (FE7)", "Event Pointer (FE7)" } },
            { "WMapImageButton", new[] { "World Map Image" } },
            { "WMapImageFE6Button", new[] { "World Map Image (FE6)" } },
            { "WMapImageFE7Button", new[] { "World Map Image (FE7)" } },
            { "WMapProbabilityButton", new[] { "World Map Monster", "World Map Monster Editor" } },
        });

        static IReadOnlyDictionary<string, IReadOnlyList<string>> Freeze(Dictionary<string, string[]> source)
        {
            var frozen = new Dictionary<string, IReadOnlyList<string>>(source.Count, StringComparer.Ordinal);
            foreach (var (key, aliases) in source)
                frozen.Add(key, Array.AsReadOnly((string[])aliases.Clone()));
            return new ReadOnlyDictionary<string, IReadOnlyList<string>>(frozen);
        }

        /// <summary>Aliases for a single-view catalog entry, or null when the key is unknown.</summary>
        public static IReadOnlyList<string>? AliasesForCatalogEntry(string? key)
            => key != null && CatalogAliases.TryGetValue(key, out var aliases) ? aliases : null;

        /// <summary>Aliases for a desktop launcher button, or null when the button name is unknown.</summary>
        public static IReadOnlyList<string>? AliasesForDesktopButton(string? buttonName)
            => buttonName != null && DesktopAliases.TryGetValue(buttonName, out var aliases) ? aliases : null;

        /// <summary>
        /// Filter predicate for a single-view launcher entry: matches the visible (already
        /// localized) label or any of the entry's editor display titles.
        /// </summary>
        public static bool MatchesCatalogEntry(string? key, string? label, string filter)
            => Matches(label, AliasesForCatalogEntry(key), filter);

        /// <summary>
        /// Filter predicate for a desktop launcher button. An unknown button name fails OPEN:
        /// it degrades to the pre-existing label-only match instead of hiding the button.
        /// </summary>
        public static bool MatchesDesktopButton(string? buttonName, string? label, string filter)
            => Matches(label, AliasesForDesktopButton(buttonName), filter);

        /// <summary>
        /// Shared matcher: ordinal-ignore-case substring test over the visible label and, for
        /// each alias, both the raw literal and its localized form. Pure — it constructs no
        /// control and reads no ROM state.
        /// </summary>
        public static bool Matches(string? label, IReadOnlyList<string>? aliases, string filter)
            => Matches(label, aliases, filter, value => R._(value));

        internal static bool Matches(
            string? label,
            IReadOnlyList<string>? aliases,
            string filter,
            Func<string, string> localize)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string q = filter.Trim();
            if (!string.IsNullOrEmpty(label) && label.Contains(q, StringComparison.OrdinalIgnoreCase))
                return true;

            if (aliases == null)
                return false;

            for (int i = 0; i < aliases.Count; i++)
            {
                string alias = aliases[i];
                if (string.IsNullOrEmpty(alias))
                    continue;
                if (alias.Contains(q, StringComparison.OrdinalIgnoreCase))
                    return true;
                // Titles are shown through R._(...) at display time, so the localized form must
                // match too. Evaluated per keystroke (cheap dictionary lookup) so switching
                // language needs no cache rebuild.
                string localized = localize(alias);
                if (!string.Equals(localized, alias, StringComparison.Ordinal)
                    && localized.Contains(q, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}