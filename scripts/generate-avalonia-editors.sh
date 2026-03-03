#!/bin/bash
# Generate Avalonia editor stubs for all remaining WinForms forms
# Usage: bash scripts/generate-avalonia-editors.sh

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VM_DIR="$REPO_ROOT/FEBuilderGBA.Avalonia/ViewModels"
VIEW_DIR="$REPO_ROOT/FEBuilderGBA.Avalonia/Views"

generate_editor() {
    local NAME="$1"
    local TITLE="$2"
    local VM_FILE="$VM_DIR/${NAME}ViewModel.cs"
    local AXAML_FILE="$VIEW_DIR/${NAME}View.axaml"
    local CS_FILE="$VIEW_DIR/${NAME}View.axaml.cs"

    # Skip if already exists
    if [ -f "$CS_FILE" ]; then
        return
    fi

    # ViewModel
    cat > "$VM_FILE" << VMEOF
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ${NAME}ViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "${TITLE}", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
        }
    }
}
VMEOF

    # AXAML
    cat > "$AXAML_FILE" << AXAMLEOF
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:FEBuilderGBA.Avalonia.Controls"
        x:Class="FEBuilderGBA.Avalonia.Views.${NAME}View"
        Title="${TITLE}" Width="800" Height="500">
  <Grid ColumnDefinitions="250,*">
    <controls:AddressListControl Grid.Column="0" Name="EntryList" Margin="4" />

    <ScrollViewer Grid.Column="1" Margin="4">
      <StackPanel Spacing="8" Margin="8">
        <TextBlock Text="${TITLE}" FontSize="18" FontWeight="Bold" />

        <Grid ColumnDefinitions="140,*" RowDefinitions="Auto">
          <TextBlock Grid.Row="0" Grid.Column="0" Text="Address:" VerticalAlignment="Center" />
          <TextBlock Grid.Row="0" Grid.Column="1" Name="AddrLabel" VerticalAlignment="Center" />
        </Grid>
      </StackPanel>
    </ScrollViewer>
  </Grid>
</Window>
AXAMLEOF

    # Code-behind
    cat > "$CS_FILE" << CSEOF
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ${NAME}View : Window, IEditorView
    {
        readonly ${NAME}ViewModel _vm = new();

        public string ViewTitle => "${TITLE}";
        public bool IsLoaded => _vm.IsLoaded;

        public ${NAME}View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("${NAME}View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("${NAME}View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
CSEOF
}

# ==========================
# A. Image Editors
# ==========================
generate_editor "ImagePortrait" "Portrait Image Editor"
generate_editor "ImagePortraitFE6" "Portrait Editor (FE6)"
generate_editor "ImagePortraitImporter" "Portrait Import Wizard"
generate_editor "ImageBG" "Background Image Editor"
generate_editor "ImageBGSelectPopup" "BG Selection"
generate_editor "ImageBattleAnime" "Battle Animation Editor"
generate_editor "ImageBattleAnimePallet" "Battle Animation Palette"
generate_editor "ImageBattleBG" "Battle Background Editor"
generate_editor "ImageBattleScreen" "Battle Screen Layout"
generate_editor "ImageCG" "CG Image Editor"
generate_editor "ImageCGFE7U" "CG Editor (FE7U)"
generate_editor "ImageUnitPalette" "Unit Palette Editor"
generate_editor "ImageUnitWaitIcon" "Unit Wait Icon"
generate_editor "ImageUnitMoveIcon" "Unit Move Icon"
generate_editor "ImageSystemArea" "System Area Graphics"
generate_editor "ImageGenericEnemyPortrait" "Generic Enemy Portraits"
generate_editor "ImageRomAnime" "ROM Animation Viewer"
generate_editor "ImageTSAEditor" "TSA Tile Editor"
generate_editor "ImageTSAAnime" "TSA Animation Editor"
generate_editor "ImageTSAAnime2" "TSA Animation Editor v2"
generate_editor "ImagePallet" "Palette Editor"
generate_editor "ImageMagicFEditor" "Magic Effect Editor (FEditor)"
generate_editor "ImageMagicCSACreator" "CSA Magic Creator"
generate_editor "ImageMapActionAnimation" "Map Action Animation"
generate_editor "DecreaseColorTSATool" "Color Reduction Tool"
generate_editor "ImageFormRefViewer" "Image Form Reference"
generate_editor "InterpolatedPictureBoxViewer" "Interpolated Picture Box"

# ==========================
# B. Event Script Editors
# ==========================
generate_editor "EventScript" "Event Script Editor"
generate_editor "EventScriptInner" "Event Script Inner Control"
generate_editor "EventScriptPopup" "Event Script Popup"
generate_editor "EventScriptCategorySelect" "Script Category Selector"
generate_editor "EventScriptTemplate" "Script Template Browser"
generate_editor "EventTemplate1" "Event Template 1"
generate_editor "EventTemplate2" "Event Template 2"
generate_editor "EventTemplate3" "Event Template 3"
generate_editor "EventTemplate4" "Event Template 4"
generate_editor "EventTemplate5" "Event Template 5"
generate_editor "EventTemplate6" "Event Template 6"
generate_editor "EventTemplateImpl" "Event Template Implementation"
generate_editor "EventUnit" "Event Unit Placement"
generate_editor "EventUnitFE6" "Event Unit (FE6)"
generate_editor "EventUnitFE7" "Event Unit (FE7)"
generate_editor "EventUnitSim" "Unit Simulation Control"
generate_editor "EventUnitColor" "Unit Color Assignment"
generate_editor "EventUnitItemDrop" "Unit Item Drop Editor"
generate_editor "EventUnitNewAlloc" "Unit Allocation Editor"
generate_editor "EventBattleTalk" "Battle Dialogue Editor"
generate_editor "EventBattleTalkFE6" "Battle Dialogue (FE6)"
generate_editor "EventBattleTalkFE7" "Battle Dialogue (FE7)"
generate_editor "EventBattleDataFE7" "Battle Data (FE7)"
generate_editor "EventHaiku" "Haiku Event Editor"
generate_editor "EventHaikuFE6" "Haiku (FE6)"
generate_editor "EventHaikuFE7" "Haiku (FE7)"
generate_editor "EventMapChange" "Map Change Event Editor"
generate_editor "EventForceSortie" "Force Sortie Editor"
generate_editor "EventForceSortieFE7" "Force Sortie (FE7)"
generate_editor "EventFinalSerifFE7" "Final Serif (FE7)"
generate_editor "EventTalkGroupFE7" "Talk Group (FE7)"
generate_editor "EventMoveDataFE7" "Move Data (FE7)"
generate_editor "EventFunctionPointer" "Function Pointer Editor"
generate_editor "EventFunctionPointerFE7" "Function Pointer (FE7)"
generate_editor "EventAssembler" "Event Assembler"
generate_editor "ProcsScript" "Procs Script Editor"
generate_editor "ProcsScriptCategorySelect" "Procs Category Selector"

# ==========================
# C. AI Script Editors
# ==========================
generate_editor "AIScript" "AI Script Editor"
generate_editor "AIScriptCategorySelect" "AI Category Selector"
generate_editor "AIASMCALLTALK" "AI ASM Call Talk"
generate_editor "AIASMCoordinate" "AI Coordinate Editor"
generate_editor "AIASMRange" "AI Range Editor"
generate_editor "AIMapSetting" "AI Map Settings"
generate_editor "AIPerformItem" "AI Item Performance"
generate_editor "AIPerformStaff" "AI Staff Performance"
generate_editor "AIStealItem" "AI Steal Item Logic"
generate_editor "AITarget" "AI Targeting"
generate_editor "AITiles" "AI Tiles Evaluation"
generate_editor "AIUnits" "AI Units Evaluation"
generate_editor "AOERANGE" "Area of Effect Range"

# ==========================
# D. Map Editors
# ==========================
generate_editor "MapSettingMain" "Map Settings (Main)"
generate_editor "MapSettingFE6" "Map Settings (FE6)"
generate_editor "MapSettingFE7" "Map Settings (FE7)"
generate_editor "MapSettingFE7U" "Map Settings (FE7U)"
generate_editor "MapSettingDifficulty" "Difficulty Settings"
generate_editor "MapPointerNewPLIST" "New PLIST Popup"
generate_editor "MapEditor" "Visual Map Editor"
generate_editor "MapEditorResize" "Map Resize"
generate_editor "MapEditorMarSize" "Map Size"
generate_editor "MapEditorAddMapChange" "Add Map Change"
generate_editor "MapTerrainName" "Terrain Name Editor"
generate_editor "MapTerrainNameEng" "Terrain Name (English)"
generate_editor "MapStyleEditor" "Map Style Editor"
generate_editor "MapStyleEditorAppend" "Tileset Append"
generate_editor "MapStyleEditorWarning" "Tile Override Warning"
generate_editor "MapStyleEditorImportImageOption" "Image Import Options"
generate_editor "MapTerrainBGLookup" "Terrain BG Lookup"
generate_editor "MapTerrainFloorLookup" "Terrain Floor Lookup"
generate_editor "MapMiniMapTerrainImage" "Mini-Map Terrain"
generate_editor "MapTileAnimation1" "Tile Animation Type 1"
generate_editor "MapTileAnimation2" "Tile Animation Type 2"
generate_editor "MapLoadFunction" "Map Load Functions"
generate_editor "MapPictureBoxViewer" "Map Picture Box"

# ==========================
# E. Audio/Sound Editors
# ==========================
generate_editor "SongTrack" "Song Track Editor"
generate_editor "SongInstrument" "Instrument Editor"
generate_editor "SongInstrumentDirectSound" "Direct Sound Instruments"
generate_editor "SongInstrumentImportWave" "Wave Import"
generate_editor "SongTrackChangeTrack" "Track Change"
generate_editor "SongTrackAllChangeTrack" "Bulk Track Change"
generate_editor "SongTrackImportMidi" "MIDI Import"
generate_editor "SongTrackImportSelectInstrument" "Instrument Selection"
generate_editor "SongTrackImportWave" "Wave Track Import"
generate_editor "SongExchange" "Song Exchange Tool"
generate_editor "SoundRoomCG" "Sound Room CG"
generate_editor "SoundRoomFE6" "Sound Room (FE6)"

# ==========================
# F. Unit/Class Specialized
# ==========================
generate_editor "UnitMain" "Unit Editor (Main)"
generate_editor "UnitFE6" "Unit Editor (FE6)"
generate_editor "UnitActionPointer" "Unit Action Pointers"
generate_editor "UnitCustomBattleAnime" "Custom Battle Animation"
generate_editor "UnitIncreaseHeight" "Unit Height Adjustment"
generate_editor "UnitPalette" "Unit Palette Assignment"
generate_editor "ClassOPDemo" "Class OP Demo"
generate_editor "ClassOPFont" "Class OP Font"
generate_editor "OPClassAlphaName" "OP Class Alpha Names"
generate_editor "OPClassAlphaNameFE6" "OP Class Alpha (FE6)"
generate_editor "OPClassDemoFE7" "Class Demo (FE7)"
generate_editor "OPClassDemoFE7U" "Class Demo (FE7U)"
generate_editor "OPClassDemoFE8U" "Class Demo (FE8U)"
generate_editor "OPClassFontFE8U" "Class Font (FE8U)"
generate_editor "ExtraUnit" "Extra Unit Editor"
generate_editor "ExtraUnitFE8U" "Extra Unit (FE8U)"
generate_editor "ClassFE6" "Class Editor (FE6)"

# ==========================
# G. Text/Translation
# ==========================
generate_editor "TextMain" "Text Editor"
generate_editor "OtherText" "Other Text Strings"
generate_editor "TextRefAddDialog" "Text Reference Dialog"
generate_editor "TextBadCharPopup" "Bad Character Warning"
generate_editor "TextScriptCategorySelect" "Text Script Category"
generate_editor "TextToSpeech" "Text-to-Speech"
generate_editor "TextEscapeEditor" "Text Escape Sequences"
generate_editor "DevTranslate" "Developer Translation Tool"
generate_editor "ToolTranslateROM" "ROM Translation Tool"
generate_editor "CString" "C-String Editor"
generate_editor "FontEditor" "Font Editor"
generate_editor "FontZH" "Font Editor (Chinese)"

# ==========================
# H. Patch/Mod Management
# ==========================
generate_editor "PatchManager" "Patch Manager"
generate_editor "PatchFilterEx" "Patch Filter Extended"
generate_editor "PatchUninstallDialog" "Patch Uninstall"
generate_editor "HowDoYouLikePatch" "Patch Review"
generate_editor "HowDoYouLikePatch2" "Patch Review v2"
generate_editor "ToolCustomBuild" "Custom Build Tool"

# ==========================
# I. Skill Systems
# ==========================
generate_editor "SkillAssignmentUnitSkillSystem" "Skill Assignment (Unit)"
generate_editor "SkillAssignmentUnitCSkillSys" "C-Skill Assignment (Unit)"
generate_editor "SkillAssignmentUnitFE8N" "FE8N Skill Assignment"
generate_editor "SkillAssignmentClassSkillSystem" "Skill Assignment (Class)"
generate_editor "SkillAssignmentClassCSkillSys" "C-Skill Assignment (Class)"
generate_editor "SkillConfigSkillSystem" "Skill Config"
generate_editor "SkillConfigFE8UCSkillSys09x" "FE8U C-Skill Config"
generate_editor "SkillConfigFE8NSkill" "FE8N Skill Config"
generate_editor "SkillConfigFE8NVer2Skill" "FE8N v2 Skill Config"
generate_editor "SkillConfigFE8NVer3Skill" "FE8N v3 Skill Config"
generate_editor "SkillSystemsEffectivenessReworkClassType" "Effectiveness Rework"
generate_editor "SkillSystemsCSkillRechain" "C-Skill Rechain"

# ==========================
# J. Tools & Advanced
# ==========================
generate_editor "ToolUndo" "Undo History Viewer"
generate_editor "ToolFELint" "FELint GUI"
generate_editor "ToolROMRebuild" "ROM Rebuild Tool"
generate_editor "ToolLZ77" "LZ77 Compression Tool"
generate_editor "ToolDiff" "ROM Diff Tool"
generate_editor "ToolUpdateDialog" "Update Checker"
generate_editor "ToolUPSPatchSimple" "UPS Patch Creator"
generate_editor "ToolUPSOpenSimple" "UPS Patch Applier"
generate_editor "ToolAllWorkSupport" "Work Support"
generate_editor "ToolProblemReport" "Problem Reporter"
generate_editor "ToolAnimationCreator" "Animation Creator"
generate_editor "ToolFlagName" "Flag Name Editor"
generate_editor "ToolUseFlag" "Flag Usage Viewer"
generate_editor "ToolUnitTalkGroup" "Talk Group Editor"
generate_editor "ToolSubtitleOverlay" "Subtitle Overlay"
generate_editor "ToolASMInsert" "ASM Insertion Tool"
generate_editor "ToolThreeMarge" "Three-Way Merge"
generate_editor "RAMRewriteTool" "RAM Rewrite Tool"
generate_editor "EmulatorMemory" "Emulator Memory Viewer"
generate_editor "GrowSimulator" "Growth Simulator"
generate_editor "HexEditor" "Hex Editor"
generate_editor "DisASM" "Disassembler"
generate_editor "LogViewer" "Log Viewer"
generate_editor "Options" "Options/Settings"

# ==========================
# K. World Map Specialized
# ==========================
generate_editor "WorldMapPath" "World Map Paths"
generate_editor "WorldMapPathEditor" "Path Editor"
generate_editor "WorldMapPathMoveEditor" "Path Movement Editor"
generate_editor "WorldMapImage" "World Map Image"
generate_editor "WorldMapImageFE6" "World Map Image (FE6)"
generate_editor "WorldMapImageFE7" "World Map Image (FE7)"
generate_editor "WorldMapEventPointerFE6" "Event Pointer (FE6)"
generate_editor "WorldMapEventPointerFE7" "Event Pointer (FE7)"

# ==========================
# L. Structural Data
# ==========================
generate_editor "Command85Pointer" "Command 0x85 Pointer"
generate_editor "FE8SpellMenuExtends" "Spell Menu Extensions"
generate_editor "StatusOption" "Status Screen Options"
generate_editor "StatusOptionOrder" "Status Option Ordering"
generate_editor "OAMSP" "OAM Sprite Editor"
generate_editor "DumpStructSelectDialog" "Struct Dump Selector"
generate_editor "DumpStructSelectToTextDialog" "Struct Dump to Text"

# ==========================
# M. Notification/UI Controls
# ==========================
generate_editor "NotifyWrite" "Write Notification"
generate_editor "NotifyPleaseWait" "Please Wait"
generate_editor "NotifyDirectInjection" "Direct Injection Notify"
generate_editor "MainSimpleMenu" "Simple Menu (Easy Mode)"
generate_editor "MainSimpleMenuEventError" "Event Error Display"
generate_editor "MainSimpleMenuImageSub" "Image Sub-Menu"

# ==========================
# Additional forms from categories (items from detailed lists)
# ==========================
generate_editor "ItemRandomChest" "Random Chest Items"
generate_editor "ItemStatBonusesSkillSystems" "Stat Bonuses (Skill Systems)"
generate_editor "ItemStatBonusesVenno" "Stat Bonuses (Venno)"
generate_editor "ItemEffectivenessSkillSystemsRework" "Effectiveness (Skill Systems Rework)"
generate_editor "MantAnimation" "Mant Animation"
generate_editor "MapPointerMain" "Map Pointer (Main)"
generate_editor "EDSensekiComment" "ED Senseki Comment"
generate_editor "EDFE6" "ED (FE6)"
generate_editor "EDFE7" "ED (FE7)"
generate_editor "OPClassAlphaNameFE6Extra" "OP Class Alpha Name (FE6 Extra)"
generate_editor "MapSettingDifficultyExtra" "Map Setting Difficulty Extra"
generate_editor "MenuExtendSplitMenu" "Menu Extend Split"
generate_editor "OpenLastSelectedFile" "Open Last Selected File"
generate_editor "ErorrUnknownROM" "Unknown ROM Error"
generate_editor "ErrorLongMessageDialog" "Long Message Dialog"
generate_editor "ErrorPaletteMissMatch" "Palette Mismatch Error"
generate_editor "ErrorPaletteShow" "Palette Show"
generate_editor "ErrorPaletteTransparent" "Palette Transparent Error"
generate_editor "ErrorReport" "Error Report"
generate_editor "ErrorTSAError" "TSA Error"
generate_editor "SongTableMain" "Song Table (Main)"
generate_editor "EventBattleTalkMain" "Battle Talk (Main)"
generate_editor "EventCondMain" "Event Conditions (Main)"
generate_editor "ItemEffectivenessMain" "Item Effectiveness (Main)"
generate_editor "MapChangeMain" "Map Change (Main)"
generate_editor "MapExitPointMain" "Map Exit Point (Main)"
generate_editor "EventScriptMain" "Event Script (Main)"

echo "Done! Generated Avalonia editor stubs."
