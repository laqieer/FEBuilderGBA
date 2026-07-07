using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.LogicalTree;
using global::Avalonia.Media.Imaging;
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

        /// <summary>
        /// Retains the picked SAF/content:// <see cref="global::Avalonia.Platform.Storage.IStorageFile"/>
        /// handle when a ROM was opened from a source that has NO local filesystem
        /// path (Android), so a later Save can write back via OpenWriteAsync (#1124).
        /// Null on desktop and whenever the current ROM has a real local path.
        /// </summary>
        private global::Avalonia.Platform.Storage.IStorageFile? _currentRomStorageFile;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
            WindowManager.Instance.MainWindow = this;
            Opened += MainWindow_Opened;
            Closing += MainWindow_Closing;
            FilterTextBox.TextChanged += FilterTextBox_TextChanged;
            FilterTextBox.KeyDown += FilterTextBox_KeyDown;

            // Enable drag-and-drop for ROM files
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);

            // Refresh UI strings when language changes
            CoreState.LanguageChanged += OnLanguageChanged;

            // Load recent files from config and build submenu
            _vm.LoadRecentFiles();
            RebuildRecentFilesMenu();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
            {
                if (FilterTextBox != null && SearchPanel.IsVisible)
                {
                    FilterTextBox.Focus();
                    e.Handled = true;
                }
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Y)
            {
                // Redo is not supported by the undo system — show status message
                SetStatusText(R._("Redo is not supported."));
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F5)
            {
                // Refresh: invalidate all open editors so they reload from ROM data
                Undo.OnAllFormsInvalidated?.Invoke();
                SetStatusText(R._("View refreshed."));
                e.Handled = true;
            }
        }

        void OnLanguageChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Refresh status bar
                _vm.UpdateFromRom();
                SetStatusText(_vm.StatusText);

                // Refresh menu headers and navigation labels
                RefreshMenuHeaders();
                RefreshNavigationLabels();
                RefreshMenuItemHeaders();
                RefreshEditorButtons();
                RefreshLabels();
                RebuildRecentFilesMenu();
            });
        }

        /// <summary>
        /// Set the MainWindow status bar text and mirror it into the TextBlock's
        /// ToolTip.Tip so the user can hover to read the full message when the
        /// status bar ellipsis-trims it. Wired in code-behind (not AXAML)
        /// because the tooltip must follow each dynamic status update — a
        /// static self-binding in the AXAML would not pick up programmatic
        /// .Text changes. Per the #650 status-bar exemption documented in
        /// AvaloniaEditorTests.cs, the AXAML must NOT carry a ToolTip.Tip on
        /// StatusText.
        /// </summary>
        void SetStatusText(string text)
        {
            StatusText.Text = text;
            ToolTip.SetTip(StatusText, text);
        }

        /// <summary>
        /// Rebuild the Recent Files submenu from the ViewModel's collection.
        /// Grays out entries whose files no longer exist on disk.
        /// </summary>
        void RebuildRecentFilesMenu()
        {
            if (RecentFilesMenuItem == null) return;

            RecentFilesMenuItem.Items.Clear();
            _vm.RefreshRecentFileExistence();

            if (_vm.RecentFiles.Count == 0)
            {
                var empty = new MenuItem { Header = R._("(No recent files)"), IsEnabled = false };
                RecentFilesMenuItem.Items.Add(empty);
                return;
            }

            foreach (var entry in _vm.RecentFiles)
            {
                var item = new MenuItem
                {
                    Header = entry.FilePath,
                    IsEnabled = entry.Exists,
                };
                if (!entry.Exists)
                {
                    item.Header = entry.FilePath + " " + R._("(missing)");
                }
                string capturedPath = entry.FilePath;
                item.Click += (_, _) => RecentFileItem_Click(capturedPath);
                RecentFilesMenuItem.Items.Add(item);
            }
        }

        void RecentFileItem_Click(string path)
        {
            if (!File.Exists(path))
            {
                _ = MessageBoxWindow.Show(this, R._("File not found:") + $" {path}", R._("Error"), MessageBoxMode.Ok);
                return;
            }
            bool ok = LoadRomFile(path);
            if (!ok)
            {
                _ = MessageBoxWindow.Show(this, R._("Failed to load ROM:") + $" {path}", R._("Error"), MessageBoxMode.Ok);
            }
        }

        void RefreshMenuHeaders()
        {
            // Refresh Easy Mode toggle text
            if (EasyModeMenuItem != null)
                EasyModeMenuItem.Header = _isEasyMode ? R._("Switch to _Normal Mode") : R._("Toggle _Easy Mode");
            // Refresh Dark Mode toggle text
            if (DarkModeMenuItem != null && Application.Current is App app)
                DarkModeMenuItem.Header = app.IsDarkMode ? R._("Switch to _Light Mode") : R._("Toggle _Dark Mode");
        }

        /// <summary>
        /// Refresh navigation expander headers and button labels using R._() translations.
        /// </summary>
        void RefreshNavigationLabels()
        {
            // Expander headers
            if (CharactersExpander != null) CharactersExpander.Header = R._("Characters");
            if (ItemsExpander != null) ItemsExpander.Header = R._("Items");
            if (MapsExpander != null) MapsExpander.Header = R._("Maps");
            if (TextExpander != null) TextExpander.Header = R._("Text");
            if (GraphicsExpander != null) GraphicsExpander.Header = R._("Graphics");
            if (AudioExpander != null) AudioExpander.Header = R._("Audio");
            if (ArenaExpander != null) ArenaExpander.Header = R._("Arena");
            if (MonstersExpander != null) MonstersExpander.Header = R._("Monsters");
            if (SummonsExpander != null) SummonsExpander.Header = R._("Summons");
            if (ItemsSpecializedExpander != null) ItemsSpecializedExpander.Header = R._("Items (Specialized)");
            if (MenusExpander != null) MenusExpander.Header = R._("Menus");
            if (CreditsExpander != null) CreditsExpander.Header = R._("Credits");
            if (WorldMapExpander != null) WorldMapExpander.Header = R._("World Map");
            if (ImageEditorsExpander != null) ImageEditorsExpander.Header = R._("Image Editors");
            if (EventScriptsExpander != null) EventScriptsExpander.Header = R._("Event Scripts");
            if (AIScriptsExpander != null) AIScriptsExpander.Header = R._("AI Scripts");
            if (MapEditorsExpander != null) MapEditorsExpander.Header = R._("Map Editors");
            if (AudioAdvancedExpander != null) AudioAdvancedExpander.Header = R._("Audio (Advanced)");
            if (UnitClassSpecializedExpander != null) UnitClassSpecializedExpander.Header = R._("Unit/Class Specialized");
            if (TextTranslationExpander != null) TextTranslationExpander.Header = R._("Text/Translation");
            if (PatchesExpander != null) PatchesExpander.Header = R._("Patches");
            if (SkillsExpander != null) SkillsExpander.Header = R._("Skill Systems");
            if (WorldMapAdvancedExpander != null) WorldMapAdvancedExpander.Header = R._("World Map (Advanced)");
            if (StructuralDataExpander != null) StructuralDataExpander.Header = R._("Structural Data");
            if (ToolsExpander != null) ToolsExpander.Header = R._("Tools");
            if (StatusScreenExpander != null) StatusScreenExpander.Header = R._("Status Screen");
            if (SkillsExtExpander != null) SkillsExtExpander.Header = R._("Skill Systems (Extended)");
            if (VersionSpecificExpander != null) VersionSpecificExpander.Header = R._("Version-Specific");

            // Easy mode toggle button
            if (EasyModeToggle != null) EasyModeToggle.Content = R._("Easy Mode");
        }

        /// <summary>
        /// Refresh all menu item headers (File, Edit, View, Tools, Help menus and sub-items) via R._().
        /// </summary>
        void RefreshMenuItemHeaders()
        {
            // Top-level menus
            if (FileMenu != null) FileMenu.Header = R._("_File");
            if (EditMenu != null) EditMenu.Header = R._("_Edit");
            if (ViewMenu != null) ViewMenu.Header = R._("_View");
            if (ToolsMenu != null) ToolsMenu.Header = R._("_Tools");
            if (HelpMenu != null) HelpMenu.Header = R._("_Help");

            // File sub-items
            if (OpenRomMenuItem != null) OpenRomMenuItem.Header = R._("_Open ROM...");
            if (OpenLastRomMenuItem != null) OpenLastRomMenuItem.Header = R._("Open _Last ROM");
            if (OpenDecompProjectMenuItem != null) OpenDecompProjectMenuItem.Header = R._("Open _Decomp Project...");
            if (RecentFilesMenuItem != null) RecentFilesMenuItem.Header = R._("_Recent Files");
            if (SaveMenuItem != null) SaveMenuItem.Header = R._("_Save ROM");
            if (SaveAsMenuItem != null) SaveAsMenuItem.Header = R._("Save _As...");
            if (ExitMenuItem != null) ExitMenuItem.Header = R._("E_xit");

            // Edit sub-items
            if (UndoMenuItem != null) UndoMenuItem.Header = R._("_Undo");
            if (RefreshMenuItem != null) RefreshMenuItem.Header = R._("Re_fresh");

            // View sub-items (EasyModeMenuItem / DarkModeMenuItem handled by RefreshMenuHeaders)
            if (CollapseAllMenuItem != null) CollapseAllMenuItem.Header = R._("_Collapse All Sections");
            if (ExpandAllMenuItem != null) ExpandAllMenuItem.Header = R._("_Expand All Sections");

            // Tools sub-items
            if (LintMenuItem != null) LintMenuItem.Header = R._("_Lint Check");
            if (DataExportMenuItem != null) DataExportMenuItem.Header = R._("_Data Export/Import...");
            if (ExternalToolsMenuItem != null) ExternalToolsMenuItem.Header = R._("_External Tools");
            if (RunEmulatorMenuItem != null) RunEmulatorMenuItem.Header = R._("Run _Emulator...");
            if (RunEmulator2MenuItem != null) RunEmulator2MenuItem.Header = R._("Run Emulator _2...");
            if (RunBinaryEditorMenuItem != null) RunBinaryEditorMenuItem.Header = R._("Run _Binary Editor...");
            if (RunSappyMenuItem != null) RunSappyMenuItem.Header = R._("Run _Sappy...");
            if (RunProgram1MenuItem != null) RunProgram1MenuItem.Header = R._("Run Program _1...");
            if (RunProgram2MenuItem != null) RunProgram2MenuItem.Header = R._("Run Program _2...");
            if (RunProgram3MenuItem != null) RunProgram3MenuItem.Header = R._("Run Program _3...");
            if (OptionsMenuItem != null) OptionsMenuItem.Header = R._("_Options...");
            if (ContentRepoSetupMenuItem != null) ContentRepoSetupMenuItem.Header = R._("Content Repositories…");

            // Help sub-items
            if (OnlineManualMenuItem != null) OnlineManualMenuItem.Header = R._("_Online Manual");
            if (DiscussionsMenuItem != null) DiscussionsMenuItem.Header = R._("_GitHub Discussions");
            if (ReportBugMenuItem != null) ReportBugMenuItem.Header = R._("_Report a Bug…");
            if (AboutMenuItem != null) AboutMenuItem.Header = R._("_About");
            if (CheckUpdatesMenuItem != null) CheckUpdatesMenuItem.Header = R._("Check for _Updates…");
        }

        /// <summary>
        /// Refresh all editor button Content strings via R._().
        /// </summary>
        void RefreshEditorButtons()
        {
            // Characters
            if (UnitsButton != null) UnitsButton.Content = R._("Units");
            if (ClassesButton != null) ClassesButton.Content = R._("Classes");
            if (SupportUnitsButton != null) SupportUnitsButton.Content = R._("Support Units");
            if (SupportAttributeButton != null) SupportAttributeButton.Content = R._("Support Attribute");
            if (SupportTalkButton != null) SupportTalkButton.Content = R._("Support Talk");

            // Items
            if (ItemsButton != null) ItemsButton.Content = R._("Items");
            if (CCBranchButton != null) CCBranchButton.Content = R._("CC Branch");
            if (WeaponEffectButton != null) WeaponEffectButton.Content = R._("Weapon Effect");
            if (StatBonusesButton != null) StatBonusesButton.Content = R._("Stat Bonuses");
            if (EffectivenessButton != null) EffectivenessButton.Content = R._("Effectiveness");
            if (PromotionButton != null) PromotionButton.Content = R._("Promotion");
            if (ShopButton != null) ShopButton.Content = R._("Shop");
            if (WeaponTriangleButton != null) WeaponTriangleButton.Content = R._("Weapon Triangle");
            if (UsagePointerButton != null) UsagePointerButton.Content = R._("Usage Pointer");
            if (EffectPointerButton != null) EffectPointerButton.Content = R._("Effect Pointer");

            // Maps
            if (MapSettingsButton != null) MapSettingsButton.Content = R._("Map Settings");
            if (TerrainNamesButton != null) TerrainNamesButton.Content = R._("Terrain Names");
            if (MoveCostButton != null) MoveCostButton.Content = R._("Move Cost");
            if (EventConditionsButton != null) EventConditionsButton.Content = R._("Event Conditions");
            if (MapChangesButton != null) MapChangesButton.Content = R._("Map Changes");
            if (ExitPointsButton != null) ExitPointsButton.Content = R._("Exit Points");
            if (MapPointersButton != null) MapPointersButton.Content = R._("Map Pointers");
            if (TileAnimationButton != null) TileAnimationButton.Content = R._("Tile Animation");

            // Text
            if (TextViewerButton != null) TextViewerButton.Content = R._("Text Viewer");

            // Graphics
            if (PortraitsButton != null) PortraitsButton.Content = R._("Portraits");
            if (SystemIconsButton != null) SystemIconsButton.Content = R._("System Icons");
            if (ItemIconsButton != null) ItemIconsButton.Content = R._("Item Icons");
            if (HoverColorsButton != null) HoverColorsButton.Content = R._("Hover Colors");
            if (BattleBGButton != null) BattleBGButton.Content = R._("Battle BG");
            if (BattleTerrainButton != null) BattleTerrainButton.Content = R._("Battle Terrain");
            if (ChapterTitleButton != null) ChapterTitleButton.Content = R._("Chapter Title");
            if (CGViewerButton != null) CGViewerButton.Content = R._("CG Viewer");
            if (OPClassDemoButton != null) OPClassDemoButton.Content = R._("OP Class Demo");
            if (OPClassFontButton != null) OPClassFontButton.Content = R._("OP Class Font");
            if (OPPrologueButton != null) OPPrologueButton.Content = R._("OP Prologue");

            // Audio
            if (SongTableButton != null) SongTableButton.Content = R._("Song Table");
            if (BossBGMButton != null) BossBGMButton.Content = R._("Boss BGM");
            if (FootstepsButton != null) FootstepsButton.Content = R._("Footsteps");
            if (SoundRoomButton != null) SoundRoomButton.Content = R._("Sound Room");

            // Arena
            if (ArenaClassButton != null) ArenaClassButton.Content = R._("Arena Class");
            if (ArenaEnemyWeaponButton != null) ArenaEnemyWeaponButton.Content = R._("Arena Enemy Weapon");
            if (LinkArenaDenyButton != null) LinkArenaDenyButton.Content = R._("Link Arena Deny");

            // Monsters
            if (MonsterProbabilityButton != null) MonsterProbabilityButton.Content = R._("Monster Probability");
            if (MonsterItemsButton != null) MonsterItemsButton.Content = R._("Monster Items");
            if (WMapProbabilityButton != null) WMapProbabilityButton.Content = R._("WMap Probability");

            // Summons
            if (SummonUnitButton != null) SummonUnitButton.Content = R._("Summon Unit");
            if (DemonKingButton != null) DemonKingButton.Content = R._("Demon King");

            // Items (Specialized)
            if (StatBonusesSkillButton != null) StatBonusesSkillButton.Content = R._("Stat Bonuses (Skill)");
            if (StatBonusesVennoButton != null) StatBonusesVennoButton.Content = R._("Stat Bonuses (Venno)");
            if (EffectivenessReworkButton != null) EffectivenessReworkButton.Content = R._("Effectiveness (Rework)");
            if (RandomChestButton != null) RandomChestButton.Content = R._("Random Chest");

            // Menus
            if (MenuDefinitionButton != null) MenuDefinitionButton.Content = R._("Menu Definition");
            if (MenuCommandButton != null) MenuCommandButton.Content = R._("Menu Command");
            if (SplitMenuExtButton != null) SplitMenuExtButton.Content = R._("Split Menu Ext");

            // Credits
            if (EndingEventsButton != null) EndingEventsButton.Content = R._("Ending Events");
            if (StaffRollButton != null) StaffRollButton.Content = R._("Staff Roll");
            if (EDFE6Button != null) EDFE6Button.Content = R._("ED (FE6)");
            if (EDFE7Button != null) EDFE7Button.Content = R._("ED (FE7)");
            if (SensekiCommentButton != null) SensekiCommentButton.Content = R._("Senseki Comment");

            // World Map
            if (MapPointsButton != null) MapPointsButton.Content = R._("Map Points");
            if (MapBGMButton != null) MapBGMButton.Content = R._("Map BGM");
            if (MapEventsButton != null) MapEventsButton.Content = R._("Map Events");

            // Image Editors
            if (PortraitEditorButton != null) PortraitEditorButton.Content = R._("Portrait Editor");
            if (PortraitFE6Button != null) PortraitFE6Button.Content = R._("Portrait (FE6)");
            if (PortraitImportButton != null) PortraitImportButton.Content = R._("Portrait Import");
            if (BGEditorButton != null) BGEditorButton.Content = R._("BG Editor");
            if (BattleAnimButton != null) BattleAnimButton.Content = R._("Battle Anim");
            if (BattleAnimPalButton != null) BattleAnimPalButton.Content = R._("Battle Anim Pal");
            if (BattleBGEditButton != null) BattleBGEditButton.Content = R._("Battle BG Edit");
            if (BattleScreenButton != null) BattleScreenButton.Content = R._("Battle Screen");
            if (CGEditorButton != null) CGEditorButton.Content = R._("CG Editor");
            if (CGFE7UButton != null) CGFE7UButton.Content = R._("CG (FE7U)");
            if (ImgUnitPaletteButton != null) ImgUnitPaletteButton.Content = R._("Unit Palette");
            if (WaitIconButton != null) WaitIconButton.Content = R._("Wait Icon");
            if (MoveIconButton != null) MoveIconButton.Content = R._("Move Icon");
            if (SystemAreaButton != null) SystemAreaButton.Content = R._("System Area");
            if (GenericEnemyButton != null) GenericEnemyButton.Content = R._("Generic Enemy");
            if (ROMAnimeButton != null) ROMAnimeButton.Content = R._("ROM Anime");
            if (TSAEditorButton != null) TSAEditorButton.Content = R._("TSA Editor");
            if (TSAAnimeButton != null) TSAAnimeButton.Content = R._("TSA Anime");
            if (TSAAnime2Button != null) TSAAnime2Button.Content = R._("TSA Anime 2");
            if (PaletteButton != null) PaletteButton.Content = R._("Palette");
            if (MagicFEditorButton != null) MagicFEditorButton.Content = R._("Magic FEditor");
            if (CSACreatorButton != null) CSACreatorButton.Content = R._("CSA Creator");
            if (MapActionAnimButton != null) MapActionAnimButton.Content = R._("Map Action Anim");
            if (ColorReduceButton != null) ColorReduceButton.Content = R._("Color Reduce");

            // Event Scripts
            if (EventScriptButton != null) EventScriptButton.Content = R._("Event Script");
            if (EventUnitButton != null) EventUnitButton.Content = R._("Event Unit");
            if (EventUnitFE6Button != null) EventUnitFE6Button.Content = R._("Event Unit (FE6)");
            if (EventUnitFE7Button != null) EventUnitFE7Button.Content = R._("Event Unit (FE7)");
            if (UnitColorButton != null) UnitColorButton.Content = R._("Unit Color");
            if (ItemDropButton != null) ItemDropButton.Content = R._("Item Drop");
            if (NewAllocButton != null) NewAllocButton.Content = R._("New Alloc");
            if (BattleTalkButton != null) BattleTalkButton.Content = R._("Battle Talk");
            if (BattleTalkFE6Button != null) BattleTalkFE6Button.Content = R._("Battle Talk (FE6)");
            if (BattleTalkFE7Button != null) BattleTalkFE7Button.Content = R._("Battle Talk (FE7)");
            if (BattleDataFE7Button != null) BattleDataFE7Button.Content = R._("Battle Data (FE7)");
            if (HaikuButton != null) HaikuButton.Content = R._("Haiku");
            if (HaikuFE6Button != null) HaikuFE6Button.Content = R._("Haiku (FE6)");
            if (HaikuFE7Button != null) HaikuFE7Button.Content = R._("Haiku (FE7)");
            if (MapChangeEvtButton != null) MapChangeEvtButton.Content = R._("Map Change Evt");
            if (ForceSortieButton != null) ForceSortieButton.Content = R._("Force Sortie");
            if (ForceSortieFE7Button != null) ForceSortieFE7Button.Content = R._("Force Sortie (FE7)");
            if (FuncPointerButton != null) FuncPointerButton.Content = R._("Func Pointer");
            if (FuncPtrFE7Button != null) FuncPtrFE7Button.Content = R._("Func Ptr (FE7)");
            if (EventAssemblerButton != null) EventAssemblerButton.Content = R._("Event Assembler");
            if (ProcsScriptButton != null) ProcsScriptButton.Content = R._("Procs Script");
            if (TemplatesButton != null) TemplatesButton.Content = R._("Templates");
            if (Template1Button != null) Template1Button.Content = R._("Template 1");
            if (Template2Button != null) Template2Button.Content = R._("Template 2");
            if (Template3Button != null) Template3Button.Content = R._("Template 3");
            if (Template4Button != null) Template4Button.Content = R._("Template 4");
            if (Template5Button != null) Template5Button.Content = R._("Template 5");
            if (Template6Button != null) Template6Button.Content = R._("Template 6");
            if (FinalSerifFE7Button != null) FinalSerifFE7Button.Content = R._("Final Serif (FE7)");
            if (MoveDataFE7Button != null) MoveDataFE7Button.Content = R._("Move Data (FE7)");
            if (TalkGroupFE7Button != null) TalkGroupFE7Button.Content = R._("Talk Group (FE7)");

            // AI Scripts
            if (AIScriptButton != null) AIScriptButton.Content = R._("AI Script");
            // #1414: AI ASM Call / AI Coordinate / AI Range / AI Tiles / AI Units buttons removed
            // (context-dependent sub-editors; standalone open corrupted ROM via a guessed write target).
            if (AIMapSettingButton != null) AIMapSettingButton.Content = R._("AI Map Setting");
            if (AIItemButton != null) AIItemButton.Content = R._("AI Item");
            if (AIStaffButton != null) AIStaffButton.Content = R._("AI Staff");
            if (AIStealButton != null) AIStealButton.Content = R._("AI Steal");
            if (AITargetButton != null) AITargetButton.Content = R._("AI Target");
            if (AOERangeButton != null) AOERangeButton.Content = R._("AOE Range");

            // Map Editors
            if (MapEditorButton != null) MapEditorButton.Content = R._("Map Editor");
            if (MapSettingsFE6Button != null) MapSettingsFE6Button.Content = R._("Map Settings (FE6)");
            if (MapSettingsFE7Button != null) MapSettingsFE7Button.Content = R._("Map Settings (FE7)");
            if (MapSettingsFE7UButton != null) MapSettingsFE7UButton.Content = R._("Map Settings (FE7U)");
            if (DifficultyButton != null) DifficultyButton.Content = R._("Difficulty");
            if (StyleEditorButton != null) StyleEditorButton.Content = R._("Style Editor");
            if (TerrainBGButton != null) TerrainBGButton.Content = R._("Terrain BG");
            if (TerrainFloorButton != null) TerrainFloorButton.Content = R._("Terrain Floor");
            if (MiniMapButton != null) MiniMapButton.Content = R._("Mini Map");
            if (TileAnim1Button != null) TileAnim1Button.Content = R._("Tile Anim 1");
            if (TileAnim2Button != null) TileAnim2Button.Content = R._("Tile Anim 2");
            if (LoadFunctionButton != null) LoadFunctionButton.Content = R._("Load Function");
            if (TerrainEngButton != null) TerrainEngButton.Content = R._("Terrain Eng");

            // Audio (Advanced)
            if (SongTrackButton != null) SongTrackButton.Content = R._("Song Track");
            if (InstrumentButton != null) InstrumentButton.Content = R._("Instrument");
            if (DirectSoundButton != null) DirectSoundButton.Content = R._("Direct Sound");
            if (WaveImportButton != null) WaveImportButton.Content = R._("Wave Import");
            if (MIDIImportButton != null) MIDIImportButton.Content = R._("MIDI Import");
            if (SongExchangeButton != null) SongExchangeButton.Content = R._("Song Exchange");
            if (SoundRoomFE6Button != null) SoundRoomFE6Button.Content = R._("Sound Room (FE6)");
            if (SoundRoomCGButton != null) SoundRoomCGButton.Content = R._("Sound Room CG");
            if (ChangeTrackButton != null) ChangeTrackButton.Content = R._("Change Track");
            if (AllChangeTrackButton != null) AllChangeTrackButton.Content = R._("All Change Track");
            if (SelectInstrumentButton != null) SelectInstrumentButton.Content = R._("Select Instrument");

            // Unit/Class Specialized
            if (UnitFE6Button != null) UnitFE6Button.Content = R._("Unit (FE6)");
            if (ActionPointerButton != null) ActionPointerButton.Content = R._("Action Pointer");
            if (CustomAnimButton != null) CustomAnimButton.Content = R._("Custom Anim");
            if (HeightButton != null) HeightButton.Content = R._("Height");
            if (UnitPaletteButton != null) UnitPaletteButton.Content = R._("Unit Palette");
            if (ClassFE6Button != null) ClassFE6Button.Content = R._("Class (FE6)");
            if (ClassOPDemoButton != null) ClassOPDemoButton.Content = R._("Class OP Demo");
            if (ClassOPFontButton != null) ClassOPFontButton.Content = R._("Class OP Font");
            if (ExtraUnitButton != null) ExtraUnitButton.Content = R._("Extra Unit");
            if (ExtraFE8UButton != null) ExtraFE8UButton.Content = R._("Extra (FE8U)");

            // Text/Translation
            if (TextEditorButton != null) TextEditorButton.Content = R._("Text Editor");
            if (OtherTextButton != null) OtherTextButton.Content = R._("Other Text");
            if (CStringButton != null) CStringButton.Content = R._("C-String");
            if (FontEditorButton != null) FontEditorButton.Content = R._("Font Editor");
            if (FontZHButton != null) FontZHButton.Content = R._("Font ZH");
            if (DevTranslateButton != null) DevTranslateButton.Content = R._("Dev Translate");
            if (ROMTranslateButton != null) ROMTranslateButton.Content = R._("ROM Translate");
            if (TextEscapeButton != null) TextEscapeButton.Content = R._("Text Escape");

            // Patches
            if (PatchManagerButton != null) PatchManagerButton.Content = R._("Patch Manager");
            if (CustomBuildButton != null) CustomBuildButton.Content = R._("Custom Build");

            // Skills
            if (SkillUnitButton != null) SkillUnitButton.Content = R._("Skill Unit");
            if (SkillClassButton != null) SkillClassButton.Content = R._("Skill Class");
            if (SkillConfigButton != null) SkillConfigButton.Content = R._("Skill Config");

            // World Map (Advanced)
            if (MapPathsButton != null) MapPathsButton.Content = R._("Map Paths");
            if (PathEditorButton != null) PathEditorButton.Content = R._("Path Editor");
            if (WMapImageButton != null) WMapImageButton.Content = R._("Map Image");
            if (WMapImageFE6Button != null) WMapImageFE6Button.Content = R._("Map Image (FE6)");
            if (WMapImageFE7Button != null) WMapImageFE7Button.Content = R._("Map Image (FE7)");
            if (WMapEventsFE6Button != null) WMapEventsFE6Button.Content = R._("Events (FE6)");
            if (WMapEventsFE7Button != null) WMapEventsFE7Button.Content = R._("Events (FE7)");

            // Structural Data
            if (Cmd85PointerButton != null) Cmd85PointerButton.Content = R._("Cmd85 Pointer");
            if (SpellMenuExtButton != null) SpellMenuExtButton.Content = R._("Spell Menu Ext");
            if (StatusOptionButton != null) StatusOptionButton.Content = R._("Status Option");
            if (OAMSpriteButton != null) OAMSpriteButton.Content = R._("OAM Sprite");
            if (StructDumpButton != null) StructDumpButton.Content = R._("Struct Dump");

            // Tools
            if (RunLintButton != null) RunLintButton.Content = R._("Run Lint");
            if (UndoHistoryButton != null) UndoHistoryButton.Content = R._("Undo History");
            if (FELintGUIButton != null) FELintGUIButton.Content = R._("FELint GUI");
            if (ROMRebuildButton != null) ROMRebuildButton.Content = R._("ROM Rebuild");
            if (LZ77ToolButton != null) LZ77ToolButton.Content = R._("LZ77 Tool");
            if (ROMDiffButton != null) ROMDiffButton.Content = R._("ROM Diff");
            if (UPSCreateButton != null) UPSCreateButton.Content = R._("UPS Create");
            if (UPSApplyButton != null) UPSApplyButton.Content = R._("UPS Apply");
            if (FlagNamesButton != null) FlagNamesButton.Content = R._("Flag Names");
            if (FlagUsageButton != null) FlagUsageButton.Content = R._("Flag Usage");
            if (TalkGroupsButton != null) TalkGroupsButton.Content = R._("Talk Groups");
            if (ASMInsertButton != null) ASMInsertButton.Content = R._("ASM Insert");
            if (HexEditorButton != null) HexEditorButton.Content = R._("Hex Editor");
            if (DisassemblerButton != null) DisassemblerButton.Content = R._("Disassembler");
            if (LogViewerButton != null) LogViewerButton.Content = R._("Log Viewer");
            if (GrowSimButton != null) GrowSimButton.Content = R._("Grow Sim");
            if (OptionsButton != null) OptionsButton.Content = R._("Options");
            if (PointerToolButton != null) PointerToolButton.Content = R._("Pointer Tool");
            if (FreeSpaceButton != null) FreeSpaceButton.Content = R._("Free Space");
            if (GraphicsToolButton != null) GraphicsToolButton.Content = R._("Graphics Tool");
            if (BGMMuteButton != null) BGMMuteButton.Content = R._("BGM Mute");

            // Status Screen
            if (StatusParamButton != null) StatusParamButton.Content = R._("Status Param");
            if (StatusRMenuButton != null) StatusRMenuButton.Content = R._("Status R-Menu");
            if (StatusUnitsButton != null) StatusUnitsButton.Content = R._("Status Units");
            if (StatusOptionsButton != null) StatusOptionsButton.Content = R._("Status Options");

            // Skill Systems (Extended)
            if (UnitCSkillSysButton != null) UnitCSkillSysButton.Content = R._("Unit CSkillSys");
            if (ClassCSkillSysButton != null) ClassCSkillSysButton.Content = R._("Class CSkillSys");
            if (UnitFE8NButton != null) UnitFE8NButton.Content = R._("Unit FE8N");
            if (ConfigFE8NButton != null) ConfigFE8NButton.Content = R._("Config FE8N");
            if (ConfigFE8Nv2Button != null) ConfigFE8Nv2Button.Content = R._("Config FE8N v2");
            if (ConfigFE8Nv3Button != null) ConfigFE8Nv3Button.Content = R._("Config FE8N v3");
            if (ConfigCSkill09xButton != null) ConfigCSkill09xButton.Content = R._("Config CSkill09x");

            // Version-Specific
            if (ItemsFE6Button != null) ItemsFE6Button.Content = R._("Items (FE6)");
            if (MoveCostFE6Button != null) MoveCostFE6Button.Content = R._("Move Cost (FE6)");
            if (SupportFE6Button != null) SupportFE6Button.Content = R._("Support (FE6)");
            if (SupTalkFE6Button != null) SupTalkFE6Button.Content = R._("Sup Talk (FE6)");
            if (SupTalkFE7Button != null) SupTalkFE7Button.Content = R._("Sup Talk (FE7)");
            if (UnitsFE7Button != null) UnitsFE7Button.Content = R._("Units (FE7)");
            if (OPDemoFE7Button != null) OPDemoFE7Button.Content = R._("OP Demo (FE7)");
            if (OPDemoFE7UButton != null) OPDemoFE7UButton.Content = R._("OP Demo (FE7U)");
            if (OPDemoFE8UButton != null) OPDemoFE8UButton.Content = R._("OP Demo (FE8U)");
            if (OPFontFE8UButton != null) OPFontFE8UButton.Content = R._("OP Font (FE8U)");
            if (AlphaNameButton != null) AlphaNameButton.Content = R._("Alpha Name");
            if (AlphaFE6Button != null) AlphaFE6Button.Content = R._("Alpha (FE6)");
            if (ClassListButton != null) ClassListButton.Content = R._("Class List");
            if (WeaponLockButton != null) WeaponLockButton.Content = R._("Weapon Lock");
            if (ShortTextButton != null) ShortTextButton.Content = R._("Short Text");
        }

        /// <summary>
        /// Refresh miscellaneous labels and text blocks via R._().
        /// </summary>
        void RefreshLabels()
        {
            if (FilterLabel != null) FilterLabel.Text = R._("Filter:");
            if (ClearFilterButton != null) ClearFilterButton.Content = R._("Clear");
            if (NoRomLabel != null) NoRomLabel.Text = R._("Open a ROM file to begin editing.");
            if (FilterTextBox != null) FilterTextBox.Watermark = R._("Type to filter editors...");
        }

        void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files)
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDrop(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFiles();
            if (files == null) return;

            foreach (var file in files)
            {
                string path = file.Path.LocalPath;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".gba")
                {
                    bool ok = LoadRomFile(path);
                    if (!ok)
                        CoreState.Services.ShowError(R._("Failed to load ROM:") + $" {path}");
                    return;
                }
                if (ext == ".ups")
                {
                    // Apply UPS patch if a ROM is already loaded
                    if (CoreState.ROM == null)
                    {
                        CoreState.Services.ShowError(R._("Load a ROM first before applying a UPS patch."));
                        return;
                    }
                    try
                    {
                        byte[] patchData = File.ReadAllBytes(path);
                        byte[] result = UPSUtilCore.ApplyUPS(CoreState.ROM.Data, patchData, out string errorMessage);
                        if (result == null || !string.IsNullOrEmpty(errorMessage))
                        {
                            CoreState.Services.ShowError(R._("UPS patch failed:") + $" {errorMessage}");
                            return;
                        }
                        // Replace ROM data with patched data
                        Array.Copy(result, CoreState.ROM.Data, Math.Min(result.Length, CoreState.ROM.Data.Length));
                        CoreState.Services.ShowInfo(R._("UPS patch applied:") + $" {Path.GetFileName(path)}");
                    }
                    catch (Exception ex)
                    {
                        CoreState.Services.ShowError(R._("Failed to apply UPS patch:") + $" {ex.Message}");
                    }
                    return;
                }
            }
        }

        private async void MainWindow_Opened(object? sender, EventArgs e)
        {
            // Apply translations loaded from config at startup
            RefreshMenuHeaders();
            RefreshNavigationLabels();
            RefreshMenuItemHeaders();
            RefreshEditorButtons();
            RefreshLabels();
            _vm.UpdateFromRom();
            SetStatusText(_vm.StatusText);
            // Only run the startup update check on a real interactive GUI session —
            // never in headless/smoke/CLI modes (--screenshot-all / --validate-import /
            // --data-verify / --list-parity set SmokeTestMode), which would fire a
            // background network call and could pop a dialog (nondeterminism / hang). (#1804)
            if (!App.SmokeTestMode)
                StartAutoUpdateCheckIfDue();

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
                    await MessageBoxWindow.Show(this, R._("Failed to load ROM:") + $" {App.StartupRomPath}", R._("Error"), MessageBoxMode.Ok);
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

            // Auto-open a decomp project if --project was specified (#1129). This
            // drives the headless badge screenshot. Guard with try/catch so a bad
            // project fails like the rom-failure path in smoke/screenshot mode.
            if (!string.IsNullOrEmpty(App.StartupProjectDir))
            {
                bool projectOk = false;
                try
                {
                    var project = DecompProjectDetector.Detect(App.StartupProjectDir);
                    if (project != null)
                    {
                        var resolved = DecompProjectDetector.ResolveBuiltRom(App.StartupProjectDir, project);
                        if (resolved.Status == DecompResolveStatus.Ok)
                        {
                            project.BuiltRomPath = resolved.Path;
                            CoreState.DecompProject = project;
                            projectOk = LoadRomFile(resolved.Path);
                            if (!projectOk)
                                CoreState.DecompProject = null;
                            UpdateDecompBadge();
                        }
                    }
                }
                catch
                {
                    projectOk = false;
                    CoreState.DecompProject = null;
                }

                if (!projectOk)
                {
                    if (App.SmokeTestMode)
                    {
                        Environment.ExitCode = 2;
                        Close();
                        return;
                    }
                    await MessageBoxWindow.Show(this, R._("Failed to open decomp project:") + $" {App.StartupProjectDir}", R._("Error"), MessageBoxMode.Ok);
                    return;
                }

                if (App.SmokeTestMode)
                {
                    await Task.Delay(200);
                    RunSmokeTest();
                    return;
                }
            }

            if (!App.SmokeTestMode && ContentRepoSetupCore.ShouldAutoShow(CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, CoreState.Config))
            {
                await WindowManager.Instance.OpenModal<ContentRepoSetupWizardView>(this);
            }
        }

        /// <summary>
        /// Load a ROM file and initialize all subsystems.
        /// Returns true on success.
        /// </summary>
        public bool LoadRomFile(string path)
            => LoadRomFile(path, null);

        /// <summary>
        /// Load a ROM file, optionally forcing the version detection (#1134).
        /// When <paramref name="forceVersion"/> is non-empty the ROM is loaded
        /// via <see cref="ROM.LoadForceVersion"/> (mirrors the CLI seam in
        /// <c>RomLoader.LoadRom(path, forceVersion)</c>); a null/empty value
        /// keeps the existing autodetect behaviour.
        /// </summary>
        public bool LoadRomFile(string path, string forceVersion)
        {
            ROM rom = new ROM();
            bool ok;
            if (!string.IsNullOrEmpty(forceVersion))
            {
                // Mirror CLI RomLoader: map the manifest forceVersion string to
                // the internal game code instead of trusting the ROM header.
                ok = rom.LoadForceVersion(path, forceVersion);
            }
            else
            {
                ok = rom.Load(path, out string version);
            }
            if (!ok) return false;

            // This is a real local-path load (desktop / OpenLastRom / decomp /
            // build-reload), so clear any retained SAF handle from a prior Android
            // content:// open before converging on the shared init (#1124).
            _currentRomStorageFile = null;
            return FinishLoadedRom(rom, path);
        }

        /// <summary>
        /// Shared post-load initialization for both the local-path and the Android
        /// SAF/stream load paths (#1124). Wires CoreState, caches, encoders, event
        /// scripts, UI state and auto-save. <paramref name="displayName"/> is the
        /// path on desktop or the SAF file name on Android (used for recent-files
        /// and the last-ROM config key).
        /// </summary>
        private bool FinishLoadedRom(ROM rom, string displayName)
        {
            string path = displayName;

            // Shared CORE post-load init (CoreState, hardcode/asm-map cache, the
            // headless caches, text encoders, text-id/flag caches, export + undo,
            // event/procs/AI scripts, patch detection, skill resolver). Extracted
            // to RomFileService (#1870) so the single-view web/Android shell
            // (MainView) initializes a ROM identically and the two never drift.
            // The shell-only UI wiring below (labels, recent files, editor-panel
            // visibility, autosave) stays local to this desktop window.
            RomFileService.InitializeLoadedRom(rom);

            // Update UI
            _vm.UpdateFromRom();
            // #1129: reflect decomp mode on the toolbar badge. CoreState.DecompProject
            // is set BEFORE this call in the decomp open path and cleared before it in
            // the classic open path, so reading CoreState.IsDecompMode here is correct.
            _vm.RefreshDecompMode();
            SetStatusText(_vm.StatusText);
            NoRomLabel.IsVisible = false;
            EditorPanel.IsVisible = true;
            SearchPanel.IsVisible = true;
            SaveMenuItem.IsEnabled = true;
            SaveAsMenuItem.IsEnabled = true;
            UndoMenuItem.IsEnabled = true;
            LintMenuItem.IsEnabled = true;
            DataExportMenuItem.IsEnabled = true;

            // Filter editor buttons for loaded ROM version
            UpdateEditorVisibility();

            // Show detected ROM version and size in status bar
            try
            {
                string ver = CoreState.ROM.RomInfo?.VersionToFilename ?? "Unknown";
                VersionDetectionLabel.Text = R._("ROM:") + $" {ver}";
                long romBytes = CoreState.ROM.Data?.Length ?? 0;
                RomSizeLabel.Text = romBytes > 0 ? $"{romBytes / 1024.0 / 1024.0:F1} MB" : "";
            }
            catch { VersionDetectionLabel.Text = ""; RomSizeLabel.Text = ""; }

            // Remember last opened ROM + update recent files
            if (CoreState.Config != null)
            {
                CoreState.Config["Last_Rom_Filename"] = path;
                // AddRecentFile persists to config and calls Save
                _vm.AddRecentFile(path);
                RebuildRecentFilesMenu();
            }

            // Start auto-save if enabled
            TryStartAutoSave(path);

            return true;
        }

        private void RunSmokeTest()
        {
            if (App.ValidatePaletteMode)
            {
                RunValidatePalette();
                return;
            }

            if (App.ValidateImportMode)
            {
                RunValidateImport();
                return;
            }

            if (App.ExportEditorImagesMode)
            {
                RunExportEditorImages();
                return;
            }

            if (App.ScreenshotAllMode)
            {
                RunScreenshotAll();
                return;
            }

            if (App.DataVerifyMode)
            {
                RunDataVerify();
                return;
            }

            if (App.ListParityMode)
            {
                RunListParity();
                return;
            }

            if (App.SmokeTestAll)
            {
                RunSmokeTestAll();
                return;
            }

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
                        Log.ErrorF("Smoke test failed: {0}", ex.Message);
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
                Log.ErrorF("Smoke test failed: {0}", ex.Message);
                Environment.ExitCode = 1;
                Close();
            }
        }

        /// <summary>
        /// Smoke test that opens every editor accessible from MainWindow.
        /// Opens each editor, lets it initialize, then closes it before moving on.
        /// Reports per-editor pass/fail to stdout.
        /// </summary>
        private void RunSmokeTestAll()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                int passed = 0;
                int failed = 0;
                var failures = new List<string>();

                var editors = GetAllEditorFactories();
                Console.WriteLine($"SMOKE: Testing {editors.Count} editors...");

                foreach (var (name, factory) in editors)
                {
                    try
                    {
                        var window = factory();
                        await Task.Delay(50);  // Let it initialize
                        try { window.Close(); } catch (Exception ex) { Log.ErrorF("MainWindow.SmokeTest window close: {0}", ex.Message); }
                        passed++;
                        Console.WriteLine($"SMOKE: {name} ... OK");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add(name);
                        Console.WriteLine($"SMOKE: {name} ... FAIL: {ex.Message}");
                    }
                }

                WindowManager.Instance.CloseAll();

                Console.WriteLine($"SMOKE: Results: {passed} passed, {failed} failed out of {editors.Count}");
                if (failures.Count > 0)
                    Console.WriteLine($"SMOKE: Failures: {string.Join(", ", failures)}");

                Environment.ExitCode = failed > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Screenshot mode: opens every editor, captures a screenshot via RenderTargetBitmap,
        /// and saves it as PNG. Reports per-editor status to stdout.
        /// </summary>
        private void RunScreenshotAll()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                int captured = 0;
                int failed = 0;
                var failures = new List<string>();

                string screenshotDir = App.ScreenshotDir
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
                Directory.CreateDirectory(screenshotDir);

                string romVersion = CoreState.ROM?.RomInfo?.VersionToFilename ?? "Unknown";

                // Capture the MainWindow itself first, so window-level chrome (e.g.
                // the #1129 decomp "build preview" toolbar badge) appears in a PNG.
                try
                {
                    await Task.Delay(200); // let the toolbar/badge realize
                    var mainSize = new PixelSize(
                        Math.Max((int)this.Bounds.Width, 100),
                        Math.Max((int)this.Bounds.Height, 100));
                    using var mainRtb = new RenderTargetBitmap(mainSize, new Vector(96, 96));
                    mainRtb.Render(this);
                    string mainFile = Path.Combine(screenshotDir, $"Avalonia_MainWindow_{romVersion}.png");
                    mainRtb.Save(mainFile);
                    Console.WriteLine($"SCREENSHOT: MainWindow ... OK ({mainFile})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SCREENSHOT: MainWindow ... FAIL: {ex.Message}");
                }

                var editors = GetAllEditorFactories();
                Console.WriteLine($"SCREENSHOT: Capturing {editors.Count} editors...");

                foreach (var (name, factory) in editors)
                {
                    Window? window = null;
                    try
                    {
                        window = factory();
                        await Task.Delay(300); // Let the window initialize and render

                        // Optionally switch a combo (by AutomationId) to a
                        // non-default index BEFORE the first-item selection, so a
                        // combo-driven editor is captured in an alternate state
                        // (e.g. the FE6 Battle Dialogue editor's secondary
                        // boss-conversation table). Switching the combo reloads the
                        // list, so SelectFirstItem below then picks a row in the new
                        // table. Opt-in via --screenshot-select-combo=<AutomationId>=<index>.
                        if (!string.IsNullOrEmpty(App.ScreenshotSelectComboSpec))
                        {
                            var parts = App.ScreenshotSelectComboSpec!.Split('=');
                            if (parts.Length == 2 && int.TryParse(parts[1], out int comboIndex)
                                && SelectComboByAutomationId(window, parts[0], comboIndex))
                            {
                                await Task.Delay(150); // Let the combo's reload handler run
                            }
                        }

                        Control editor = UnwrapEditorContent(window);

                        // Try to select first item for richer screenshots
                        try
                        {
                            var method = editor.GetType().GetMethod("SelectFirstItem");
                            method?.Invoke(editor, null);
                            if (method != null)
                                await Task.Delay(100); // Let selection handler run
                        }
                        catch { /* Not all editors have SelectFirstItem */ }

                        // Optionally select a specific tab (by AutomationId) so a
                        // non-default tab is shown in the PNG. Opt-in via
                        // --screenshot-tab=<AutomationId>; editors without a
                        // matching tab are captured unchanged.
                        if (!string.IsNullOrEmpty(App.ScreenshotTabAutomationId)
                            && SelectTabByAutomationId(window, App.ScreenshotTabAutomationId!))
                        {
                            await Task.Delay(100); // Let the tab content realize
                        }

                        // Optionally force an IsVisible-toggled panel visible (by
                        // AutomationId) so a category sub-panel normally hidden
                        // behind a selection state shows up in the PNG. Opt-in via
                        // --screenshot-show-panel=<AutomationId>; editors without a
                        // matching control are captured unchanged.
                        if (!string.IsNullOrEmpty(App.ScreenshotShowPanelAutomationId)
                            && ShowPanelByAutomationId(window, App.ScreenshotShowPanelAutomationId!))
                        {
                            await Task.Delay(100); // Let the panel content realize
                        }

                        // Optionally INVOKE a button (by AutomationId) so a gated
                        // editor page (e.g. the Init Wizard's Step1 reached via its
                        // Start button) is shown in the PNG. Unlike --screenshot-tab=
                        // (a direct SelectedItem set that gate-aware wizards revert),
                        // this raises the button's real Click handler. Opt-in via
                        // --screenshot-invoke-button=<AutomationId>; editors without a
                        // matching button are captured unchanged.
                        if (!string.IsNullOrEmpty(App.ScreenshotInvokeButtonAutomationId)
                            && InvokeButtonByAutomationId(window, App.ScreenshotInvokeButtonAutomationId!))
                        {
                            await Task.Delay(150); // Let the navigation handler run + content realize
                        }

                        // Capture screenshot via RenderTargetBitmap
                        var pixelSize = new PixelSize(
                            Math.Max((int)window.Width, 100),
                            Math.Max((int)window.Height, 100));
                        var dpi = new Vector(96, 96);
                        using var rtb = new RenderTargetBitmap(pixelSize, dpi);
                        rtb.Render(window);

                        string fileName = $"Avalonia_{name}_{romVersion}.png";
                        string filePath = Path.Combine(screenshotDir, fileName);
                        rtb.Save(filePath);

                        captured++;
                        Console.WriteLine($"SCREENSHOT: {name} ... OK ({filePath})");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add(name);
                        Console.WriteLine($"SCREENSHOT: {name} ... FAIL: {ex.Message}");
                    }
                    finally
                    {
                        try { window?.Close(); } catch (Exception ex) { Log.ErrorF("MainWindow.ScreenshotAll window close: {0}", ex.Message); }
                    }
                }

                WindowManager.Instance.CloseAll();

                Console.WriteLine($"SCREENSHOT: Results: {captured} captured, {failed} failed out of {editors.Count}");
                if (failures.Count > 0)
                    Console.WriteLine($"SCREENSHOT: Failures: {string.Join(", ", failures)}");

                Environment.ExitCode = failed > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Select the first <c>TabItem</c> in <paramref name="root"/> whose
        /// <c>AutomationProperties.AutomationId</c> equals
        /// <paramref name="automationId"/>, by walking the logical tree and
        /// setting its parent <see cref="TabControl"/>'s <c>SelectedItem</c>.
        /// Returns true when a matching tab was found + selected. Used by the
        /// opt-in <c>--screenshot-tab=</c> screenshot mode.
        /// </summary>
        static bool SelectTabByAutomationId(Control root, string automationId)
        {
            foreach (var descendant in root.GetLogicalDescendants())
            {
                if (descendant is TabItem tab
                    && AutomationProperties.GetAutomationId(tab) == automationId)
                {
                    if (tab.Parent is TabControl tc)
                    {
                        tc.SelectedItem = tab;
                        return true;
                    }
                    tab.IsSelected = true;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Force the <see cref="Control.IsVisible"/> of the first descendant
        /// <see cref="Control"/> whose <c>AutomationProperties.AutomationId</c>
        /// equals <paramref name="automationId"/> to <c>true</c>, by walking the
        /// logical tree. Returns true when a matching control was found. Used by
        /// the opt-in <c>--screenshot-show-panel=</c> screenshot mode to render an
        /// <c>IsVisible</c>-toggled category panel that the default selection state
        /// would otherwise hide.
        /// </summary>
        static bool ShowPanelByAutomationId(Control root, string automationId)
        {
            foreach (var descendant in root.GetLogicalDescendants())
            {
                if (descendant is Control ctrl
                    && AutomationProperties.GetAutomationId(ctrl) == automationId)
                {
                    ctrl.IsVisible = true;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Invoke (raise <see cref="global::Avalonia.Controls.Button.Command"/> /
        /// the routed Click) the first descendant <see cref="global::Avalonia.Controls.Button"/>
        /// whose <c>AutomationProperties.AutomationId</c> equals
        /// <paramref name="automationId"/>, by walking the logical tree. Returns
        /// true when a matching button was found + invoked. Used by the opt-in
        /// <c>--screenshot-invoke-button=</c> screenshot mode to drive a gated
        /// editor's real navigation handler (e.g. the Init Wizard Start button)
        /// rather than poking its view-state directly.
        /// </summary>
        static bool InvokeButtonByAutomationId(Control root, string automationId)
        {
            foreach (var descendant in root.GetLogicalDescendants())
            {
                if (descendant is global::Avalonia.Controls.Button btn
                    && AutomationProperties.GetAutomationId(btn) == automationId)
                {
                    // Use the UIA invoke pattern so both Command- and
                    // Click-handler-backed buttons fire identically.
                    var peer = global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(btn);
                    if (peer?.GetProvider<global::Avalonia.Automation.Provider.IInvokeProvider>() is { } invoke)
                    {
                        invoke.Invoke();
                        return true;
                    }
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// Set the <c>SelectedIndex</c> of the first descendant
        /// <see cref="global::Avalonia.Controls.ComboBox"/> whose
        /// <c>AutomationProperties.AutomationId</c> equals <paramref name="automationId"/>,
        /// by walking the logical tree. Returns true when a matching combo was found
        /// and the index was applied. Used by the opt-in
        /// <c>--screenshot-select-combo=&lt;AutomationId&gt;=&lt;index&gt;</c> screenshot mode
        /// to capture a combo-driven editor in a non-default state (e.g. the FE6
        /// Battle Dialogue editor's secondary boss-conversation table). Editors
        /// without a matching combo are captured unchanged.
        /// </summary>
        static bool SelectComboByAutomationId(Control root, string automationId, int index)
        {
            foreach (var descendant in root.GetLogicalDescendants())
            {
                if (descendant is global::Avalonia.Controls.ComboBox combo
                    && AutomationProperties.GetAutomationId(combo) == automationId)
                {
                    if (index >= 0 && index < combo.ItemCount)
                    {
                        combo.SelectedIndex = index;
                        return true;
                    }
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// Run export→import→export roundtrip validation for all graphics editors.
        /// </summary>
        private void RunValidateImport()
        {
            Dispatcher.UIThread.Post(() =>
            {
                int failures = ImageImportValidator.RunAll();
                Environment.ExitCode = failures > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Run palette export→import→export roundtrip validation.
        /// </summary>
        private void RunValidatePalette()
        {
            Dispatcher.UIThread.Post(() =>
            {
                int failures = ImageImportValidator.RunAllPalette();
                Environment.ExitCode = failures > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Export decoded graphics editor images as PNG files.
        /// Unlike --screenshot-all (which captures full form screenshots), this exports
        /// only the decoded image data from each graphics ViewModel for pixel-level comparison.
        /// </summary>
        private void RunExportEditorImages()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                int exported = 0;
                int failed = 0;
                var failures = new List<string>();

                string outputDir = App.ScreenshotDir
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "editor_images");
                Directory.CreateDirectory(outputDir);

                string romVersion = CoreState.ROM?.RomInfo?.VersionToFilename ?? "Unknown";
                Console.WriteLine($"EXPORT_IMAGES: Exporting graphics editor images for {romVersion}...");

                // Define graphics editor exporters: (name, loadList, loadItem, getImage)
                var exporters = GetGraphicsEditorExporters();

                foreach (var (name, exporter) in exporters)
                {
                    try
                    {
                        var list = exporter.LoadList();
                        if (list == null || list.Count == 0)
                        {
                            Console.WriteLine($"EXPORT_IMAGES: {name} ... SKIP (empty list)");
                            continue;
                        }

                        // Export first 3 entries (or fewer if list is shorter)
                        int count = Math.Min(3, list.Count);
                        for (int i = 0; i < count; i++)
                        {
                            exporter.LoadItem(list[i].addr);
                            IImage image = exporter.GetImage();
                            if (image == null)
                            {
                                Console.WriteLine($"EXPORT_IMAGES: {name}_{i:D4} ... SKIP (null image)");
                                continue;
                            }

                            string fileName = $"Avalonia_{name}_{i:D4}_{romVersion}.png";
                            string filePath = Path.Combine(outputDir, fileName);
                            image.Save(filePath);
                            exported++;
                            Console.WriteLine($"EXPORT_IMAGES: {name}_{i:D4} ... OK ({filePath})");

                            if (image is IDisposable disposable) disposable.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add(name);
                        Console.WriteLine($"EXPORT_IMAGES: {name} ... FAIL: {ex.Message}");
                    }
                }

                Console.WriteLine($"EXPORT_IMAGES: Results: {exported} exported, {failed} failed");
                if (failures.Count > 0)
                    Console.WriteLine($"EXPORT_IMAGES: Failures: {string.Join(", ", failures)}");

                Environment.ExitCode = failed > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Helper record for graphics editor image export.
        /// </summary>
        record GraphicsExporter(
            Func<List<AddrResult>> LoadList,
            Action<uint> LoadItem,
            Func<IImage> GetImage);

        /// <summary>
        /// Returns named graphics editor exporters that can load lists, select items, and get images.
        /// </summary>
        static List<(string Name, GraphicsExporter Exporter)> GetGraphicsEditorExporters()
        {
            var result = new List<(string, GraphicsExporter)>();

            // Portrait
            var portrait = new PortraitViewerViewModel();
            result.Add(("PortraitViewer", new GraphicsExporter(
                () => portrait.LoadPortraitList(),
                addr => portrait.LoadPortrait(addr),
                () => portrait.TryLoadPortraitImage())));

            // Battle BG
            var battleBG = new BattleBGViewerViewModel();
            result.Add(("BattleBGViewer", new GraphicsExporter(
                () => battleBG.LoadBattleBGList(),
                addr => battleBG.LoadBattleBG(addr),
                () => battleBG.TryLoadImage())));

            // Battle Terrain
            var battleTerrain = new BattleTerrainViewerViewModel();
            result.Add(("BattleTerrainViewer", new GraphicsExporter(
                () => battleTerrain.LoadBattleTerrainList(),
                addr => battleTerrain.LoadBattleTerrain(addr),
                () => battleTerrain.TryLoadImage())));

            // Big CG
            var bigCG = new BigCGViewerViewModel();
            result.Add(("BigCGViewer", new GraphicsExporter(
                () => bigCG.LoadBigCGList(),
                addr => bigCG.LoadBigCG(addr),
                () => bigCG.TryLoadImage())));

            // Chapter Title
            var chapterTitle = new ChapterTitleViewerViewModel();
            result.Add(("ChapterTitleViewer", new GraphicsExporter(
                () => chapterTitle.LoadChapterTitleList(),
                addr => chapterTitle.LoadChapterTitle(addr),
                () => chapterTitle.TryLoadImage())));

            // Chapter Title FE7
            var chapterTitleFE7 = new ImageChapterTitleFE7ViewModel();
            result.Add(("ChapterTitleFE7Viewer", new GraphicsExporter(
                () => chapterTitleFE7.LoadList(),
                addr => chapterTitleFE7.LoadEntry(addr),
                () => chapterTitleFE7.TryLoadImage())));

            // Item Icon
            var itemIcon = new ItemIconViewerViewModel();
            result.Add(("ItemIconViewer", new GraphicsExporter(
                () => itemIcon.LoadItemIconList(),
                addr => itemIcon.LoadItemIcon(addr),
                () => itemIcon.TryLoadImage())));

            // System Icon
            var systemIcon = new SystemIconViewerViewModel();
            result.Add(("SystemIconViewer", new GraphicsExporter(
                () => systemIcon.LoadSystemIconList(),
                addr => systemIcon.LoadSystemIcon(addr),
                () => systemIcon.TryLoadImage())));

            // OP Class Font
            var opClassFont = new OPClassFontViewerViewModel();
            result.Add(("OPClassFontViewer", new GraphicsExporter(
                () => opClassFont.LoadOPClassFontList(),
                addr => opClassFont.LoadOPClassFont(addr),
                () => opClassFont.TryLoadImage())));

            // OP Prologue
            var opPrologue = new OPPrologueViewerViewModel();
            result.Add(("OPPrologueViewer", new GraphicsExporter(
                () => opPrologue.LoadOPPrologueList(),
                addr => opPrologue.LoadOPPrologue(addr),
                () => opPrologue.TryLoadImage())));

            // Portrait FE6
            var portraitFE6 = new ImagePortraitFE6ViewModel();
            result.Add(("ImagePortraitFE6", new GraphicsExporter(
                () => portraitFE6.LoadList(),
                addr => portraitFE6.LoadEntry(addr),
                () => portraitFE6.TryLoadImage())));

            // Background Image
            var imageBG = new ImageBGViewModel();
            result.Add(("ImageBG", new GraphicsExporter(
                () => imageBG.LoadList(),
                addr => imageBG.LoadEntry(addr),
                () => imageBG.TryLoadImage())));

            // CG Image
            var imageCG = new ImageCGViewModel();
            result.Add(("ImageCG", new GraphicsExporter(
                () => imageCG.LoadList(),
                addr => imageCG.LoadEntry(addr),
                () => imageCG.TryLoadImage())));

            // CG FE7U
            var imageCGFE7U = new ImageCGFE7UViewModel();
            result.Add(("ImageCGFE7U", new GraphicsExporter(
                () => imageCGFE7U.LoadList(),
                addr => imageCGFE7U.LoadEntry(addr),
                () => imageCGFE7U.TryLoadImage())));

            // TSA Animation
            var imageTSAAnime = new ImageTSAAnimeViewModel();
            result.Add(("ImageTSAAnime", new GraphicsExporter(
                () => imageTSAAnime.LoadList(),
                addr => imageTSAAnime.LoadEntry(addr),
                () => imageTSAAnime.TryLoadImage())));

            // Battle BG (per-entry editor)
            var imageBattleBG = new ImageBattleBGViewModel();
            result.Add(("ImageBattleBG", new GraphicsExporter(
                () => imageBattleBG.LoadList(),
                addr => imageBattleBG.LoadEntry(addr),
                () => imageBattleBG.TryLoadImage())));

            return result;
        }

        /// <summary>
        /// Data verification mode: opens each editor that implements IDataVerifiable,
        /// selects the first item, reads the ViewModel data report, cross-checks
        /// against raw ROM bytes, and prints structured results to stdout.
        /// </summary>
        enum DataVerifyComparisonResult
        {
            Match,
            Mismatch,
            Skip,
        }

        private void RunDataVerify()
        {
            bool fullMode = App.DataVerifyFullMode;
            Dispatcher.UIThread.Post(async () =>
            {
                int verified = 0;
                int failed = 0;
                int skipped = 0;
                int fieldMismatches = 0;
                var failures = new List<string>();
                var sw = System.Diagnostics.Stopwatch.StartNew();

                var editors = GetAllEditorFactories();

                // Filter version-mismatched item editors to avoid data structure
                // mismatches during verification. ItemEditorView uses 36-byte items
                // (FE7/FE8); ItemFE6View uses 32-byte items (FE6 only).
                var version = CoreState.ROM?.RomInfo?.VersionToFilename ?? "";
                if (version == "FE6")
                    editors.RemoveAll(e => e is ("ItemEditorView", _));
                else
                    editors.RemoveAll(e => e is ("ItemFE6View", _));

                Console.WriteLine($"DATAVERIFY: Testing {editors.Count} editors (full={fullMode})...");

                for (int editorIdx = 0; editorIdx < editors.Count; editorIdx++)
                {
                    var (name, factory) = editors[editorIdx];

                    // Progress logging every 10 editors when full mode takes time
                    if (fullMode && editorIdx > 0 && editorIdx % 10 == 0 && sw.ElapsedMilliseconds > 30_000)
                        Console.WriteLine($"DATAVERIFY: Progress: {editorIdx}/{editors.Count} editors, elapsed={sw.ElapsedMilliseconds / 1000}s");

                    Window? window = null;
                    try
                    {
                        window = factory();
                        await Task.Delay(100); // Let it initialize

                        Control editor = UnwrapEditorContent(window);

                        // Check if this view implements IDataVerifiableView
                        if (editor is IDataVerifiableView verifiableView)
                        {
                            var vm = verifiableView.DataViewModel;
                            if (vm is IDataVerifiable verifiable)
                            {
                                // Select first item if possible
                                SelectFirstItemOnView(editor);
                                await Task.Delay(100); // Let selection handler run

                                int listCount = verifiable.GetListCount();
                                if (listCount == 0)
                                {
                                    skipped++;
                                    Console.WriteLine($"DATAVERIFY: {name} ... SKIP (listCount=0)");
                                    continue;
                                }

                                // Determine iteration range
                                int maxItem = fullMode ? listCount : 1;
                                bool editorOk = true;

                                bool editorSkipped = false;

                                // Resolve the AddressListControl once for the loop (avoids
                                // repeated visual-tree scans in --data-verify-full).
                                var (alcControl, alcMethod) = ResolveAddressListControl(editor);

                                for (int itemIdx = 0; itemIdx < maxItem; itemIdx++)
                                {
                                    // Select the item by index (for full mode, iterate all)
                                    if (itemIdx > 0 || fullMode)
                                    {
                                        bool selected = (alcControl != null && alcMethod != null)
                                            ? SelectItemByIndex(alcControl, alcMethod, itemIdx)
                                            : false;
                                        if (!selected)
                                        {
                                            Console.WriteLine($"DATAVERIFY: {name} ... WARN: SelectItemByIndex({itemIdx}) failed, skipping remaining items");
                                            editorOk = false; // Don't count as fully verified
                                            break;
                                        }
                                        await Task.Delay(50); // Let selection handler run
                                    }

                                    var dataReport = verifiable.GetDataReport();
                                    var rawReport = verifiable.GetRawRomReport();

                                    // Print VERIFY line (always for item 0; for full mode, every item)
                                    if (itemIdx == 0 || fullMode)
                                    {
                                        var verifyParts = new List<string> { $"listCount={listCount}" };
                                        if (fullMode) verifyParts.Insert(0, $"item={itemIdx}");
                                        foreach (var kv in dataReport)
                                            verifyParts.Add($"{kv.Key}={kv.Value}");
                                        // In non-full mode, use original format without [index] suffix
                                        string verifyLabel = fullMode ? $"{name}[{itemIdx}]" : name;
                                        Console.WriteLine($"VERIFY: {verifyLabel}|{string.Join("|", verifyParts)}");
                                    }

                                    // Print RAWROM line (only for item 0 in non-full mode)
                                    if (itemIdx == 0 && !fullMode)
                                    {
                                        var rawParts = new List<string>();
                                        foreach (var kv in rawReport)
                                            rawParts.Add($"{kv.Key}={kv.Value}");
                                        Console.WriteLine($"RAWROM: {name}|{string.Join("|", rawParts)}");
                                    }

                                    // Cross-check: address-level comparison
                                    var comparison = CrossCheckDataReport(name, dataReport, rawReport);

                                    if (comparison == DataVerifyComparisonResult.Skip)
                                    {
                                        // Non-comparable data — count as skip, not verified
                                        editorSkipped = true;
                                        Console.WriteLine($"DATAVERIFY: {name} ... SKIP (no comparable data)");
                                        break;
                                    }

                                    // Per-field cross-check using GetFieldOffsetMap
                                    var fieldMap = verifiable.GetFieldOffsetMap();
                                    if (fieldMap.Count > 0 && comparison == DataVerifyComparisonResult.Match)
                                    {
                                        int itemFieldMismatches = FieldLevelCrossCheck(name, itemIdx, dataReport, rawReport, fieldMap);
                                        fieldMismatches += itemFieldMismatches;
                                        if (itemFieldMismatches > 0) editorOk = false;
                                    }

                                    if (comparison == DataVerifyComparisonResult.Mismatch)
                                        editorOk = false;
                                }

                                if (editorSkipped)
                                {
                                    skipped++;
                                    // Already printed SKIP message above; skip UI verification
                                }
                                else
                                {
                                // UI check (only on first item)
                                SelectFirstItemOnView(editor);
                                await Task.Delay(50);
                                bool uiOk = editorOk && listCount > 0
                                    ? CheckNumericUpDownsDisplayValues(name, editor)
                                    : true;

                                if (!editorOk || !uiOk)
                                {
                                    failed++;
                                    failures.Add(name);
                                    if (!editorOk) Console.WriteLine($"DATAVERIFY: {name} ... MISMATCH");
                                    if (!uiOk) Console.WriteLine($"DATAVERIFY: {name} ... UI_EMPTY");
                                }
                                else
                                {
                                    verified++;
                                    Console.WriteLine($"DATAVERIFY: {name} ... VERIFIED");
                                }
                                }
                            }
                            else
                            {
                                skipped++;
                                Console.WriteLine($"DATAVERIFY: {name} ... SKIP (VM not IDataVerifiable)");
                            }
                        }
                        else
                        {
                            skipped++;
                            Console.WriteLine($"DATAVERIFY: {name} ... SKIP (no IDataVerifiableView)");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add(name);
                        Console.WriteLine($"DATAVERIFY: {name} ... FAIL: {ex.Message}");
                    }
                    finally
                    {
                        try { window?.Close(); } catch (Exception ex) { Log.ErrorF("MainWindow.DataVerify window close: {0}", ex.Message); }
                    }
                }

                WindowManager.Instance.CloseAll();

                // Text encoding verification: decode first text entry and check for garbled output
                VerifyTextEncoding();

                Console.WriteLine($"DATAVERIFY: Results: {verified} verified, {failed} failed, {skipped} skipped out of {editors.Count} (fieldMismatches={fieldMismatches}, elapsed={sw.ElapsedMilliseconds / 1000}s)");
                if (failures.Count > 0)
                    Console.WriteLine($"DATAVERIFY: Failures: {string.Join(", ", failures)}");

                Environment.ExitCode = failed > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        private void RunListParity()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                int matched = 0;
                int mismatched = 0;
                int skipped = 0;
                int noListCount = 0;
                int contextDependentCount = 0;
                var failures = new List<string>();
                var sw = System.Diagnostics.Stopwatch.StartNew();

                var editors = GetAllEditorFactories();
                var version = CoreState.ROM?.RomInfo?.VersionToFilename ?? "";

                // Filter version-mismatched item editors
                if (version == "FE6")
                    editors.RemoveAll(e => e is ("ItemEditorView", _));
                else
                    editors.RemoveAll(e => e is ("ItemFE6View", _));

                Console.WriteLine($"LISTPARITY: Testing {editors.Count} editors against reference lists (ROM version={version})...");

                for (int editorIdx = 0; editorIdx < editors.Count; editorIdx++)
                {
                    var (name, factory) = editors[editorIdx];

                    // Only process editors that have a known reference list builder
                    if (!ListParityHelper.HasMapping(name))
                    {
                        skipped++;
                        if (ListParityHelper.IsNoListEditor(name))
                        {
                            noListCount++;
                            Console.WriteLine($"LISTPARITY: {name} | NO_LIST (tool/dialog without address list)");
                        }
                        else if (ListParityHelper.IsContextDependentEditor(name))
                        {
                            contextDependentCount++;
                            Console.WriteLine($"LISTPARITY: {name} | CONTEXT_DEPENDENT (sub-editor needing parent context)");
                        }
                        else
                            Console.WriteLine($"LISTPARITY: {name} | SKIP (no reference mapping)");
                        continue;
                    }

                    Window window = null;
                    try
                    {
                        window = factory();
                        await Task.Delay(200); // Let it initialize and load list
                        Control editor = UnwrapEditorContent(window);

                        // Select first item to trigger full load
                        SelectFirstItemOnView(editor);
                        await Task.Delay(100);

                        // Extract the Avalonia list from the AddressListControl
                        var avaloniaList = ExtractListFromView(editor);
                        if (avaloniaList == null)
                        {
                            skipped++;
                            Console.WriteLine($"LISTPARITY: {name} | SKIP (no AddressListControl found)");
                            continue;
                        }

                        // Build reference list from Core ROM data
                        var referenceList = ListParityHelper.BuildReferenceList(name);
                        if (referenceList == null)
                        {
                            skipped++;
                            Console.WriteLine($"LISTPARITY: {name} | SKIP (reference list build returned null)");
                            continue;
                        }

                        // Compare
                        var result = ListParityHelper.CompareLists(name, avaloniaList, referenceList);
                        Console.WriteLine(result.FormatResult());

                        if (result.IsMatch)
                            matched++;
                        else
                        {
                            mismatched++;
                            failures.Add(name);
                        }
                    }
                    catch (Exception ex)
                    {
                        mismatched++;
                        failures.Add(name);
                        Console.WriteLine($"LISTPARITY: {name} | ERROR: {ex.GetBaseException().Message}");
                    }
                    finally
                    {
                        try { window?.Close(); } catch (Exception ex) { Log.ErrorF("MainWindow.ListParity window close: {0}", ex.Message); }
                    }
                }

                WindowManager.Instance.CloseAll();

                Console.WriteLine($"LISTPARITY: Results: {matched} matched, {mismatched} mismatched, {skipped} skipped (NO_LIST={noListCount}, CONTEXT_DEPENDENT={contextDependentCount}) out of {editors.Count} (elapsed={sw.ElapsedMilliseconds / 1000}s)");
                if (failures.Count > 0)
                    Console.WriteLine($"LISTPARITY: Failures: {string.Join(", ", failures)}");

                Environment.ExitCode = mismatched > 0 ? 1 : 0;
                Close();
            }, DispatcherPriority.Background);
        }

        static Control UnwrapEditorContent(Window window)
        {
            return window is EditorHostWindow { Content: Control content } ? content : window;
        }

        /// <summary>
        /// Extract the list items from the AddressListControl in a view window.
        /// Searches by known control names via FindControl, then falls back to
        /// a visual-tree scan for any AddressListControl descendant.
        /// </summary>
        static IReadOnlyList<AddrResult> ExtractListFromView(Control window)
        {
            // Try known named controls first
            string[] knownNames = { "UnitList", "ItemList", "ClassList", "BranchList", "EntryList" };
            foreach (var controlName in knownNames)
            {
                var control = window.FindControl<Controls.AddressListControl>(controlName);
                if (control != null)
                    return control.GetItems();
            }

            // Fallback: find any AddressListControl in the visual tree
            foreach (var descendant in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window))
            {
                if (descendant is Controls.AddressListControl alc)
                    return alc.GetItems();
            }

            return null;
        }

        /// <summary>
        /// Select the first item on a view using the known method or reflection.
        /// </summary>
        static void SelectFirstItemOnView(Control window)
        {
            if (window is UnitEditorView uev) uev.SelectFirstItem();
            else if (window is ItemEditorView iev) iev.SelectFirstItem();
            else if (window is ClassEditorView cev) cev.SelectFirstItem();
            else
            {
                var method = window.GetType().GetMethod("SelectFirstItem");
                method?.Invoke(window, null);
            }
        }

        /// <summary>
        /// Resolve the AddressListControl and its SelectByIndex method for a window.
        /// Returns (control, method) or (null, null) if not found.
        /// Cache the result to avoid repeated visual tree scans in loops.
        /// </summary>
        static (object? control, System.Reflection.MethodInfo? method) ResolveAddressListControl(Control window)
        {
            // Try known named controls first
            string[] knownNames = { "UnitList", "ItemList", "ClassList", "BranchList", "EntryList" };
            foreach (var controlName in knownNames)
            {
                var control = window.FindControl<global::Avalonia.Controls.UserControl>(controlName);
                if (control != null)
                {
                    var selectMethod = control.GetType().GetMethod("SelectByIndex");
                    if (selectMethod != null)
                        return (control, selectMethod);
                }
            }

            // Fallback: find any AddressListControl in the visual tree via reflection
            foreach (var descendant in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window))
            {
                if (descendant.GetType().Name == "AddressListControl")
                {
                    var selectMethod = descendant.GetType().GetMethod("SelectByIndex");
                    if (selectMethod != null)
                        return (descendant, selectMethod);
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Select an item by index using a pre-resolved control and method.
        /// Returns true if selection succeeded, false otherwise.
        /// </summary>
        static bool SelectItemByIndex(object control, System.Reflection.MethodInfo selectMethod, int index)
        {
            var result = selectMethod.Invoke(control, new object[] { index });
            return result is bool b && b;
        }

        /// <summary>
        /// Select an item by index using reflection on the view's AddressListControl.
        /// Tries known control names (UnitList, ItemList, ClassList, BranchList, EntryList)
        /// then falls back to finding any AddressListControl in the visual tree.
        /// Returns true if selection succeeded, false if no AddressListControl was found.
        /// Note: For repeated calls (e.g., full sweep), prefer ResolveAddressListControl()
        /// + the cached overload to avoid repeated visual tree scans.
        /// </summary>
        static bool SelectItemByIndex(Control window, int index)
        {
            var (control, method) = ResolveAddressListControl(window);
            if (control == null || method == null) return false;
            return SelectItemByIndex(control, method, index);
        }

        /// <summary>
        /// Cross-checks the ViewModel data report against the raw ROM report.
        /// Returns true if all comparable fields match.
        /// </summary>
        static DataVerifyComparisonResult CrossCheckDataReport(string viewName,
            Dictionary<string, string> dataReport,
            Dictionary<string, string> rawReport)
        {
            if (dataReport.Count == 0 || rawReport.Count == 0)
                return DataVerifyComparisonResult.Skip;

            // The data report has field names; the raw report has "u8@0x04" style keys.
            // The cross-check is: both should report the same "addr" value, and
            // the data values should be internally consistent when there is a
            // comparable current record selected.
            if (dataReport.TryGetValue("addr", out string? dataAddr) &&
                rawReport.TryGetValue("addr", out string? rawAddr))
            {
                if (string.IsNullOrWhiteSpace(dataAddr) ||
                    string.IsNullOrWhiteSpace(rawAddr) ||
                    dataAddr == "0x00000000" ||
                    rawAddr == "0x00000000")
                {
                    return DataVerifyComparisonResult.Skip;
                }

                if (dataAddr != rawAddr)
                {
                    Console.WriteLine($"DATAVERIFY: {viewName} addr mismatch: data={dataAddr} raw={rawAddr}");
                    return DataVerifyComparisonResult.Mismatch;
                }
            }
            else
            {
                return DataVerifyComparisonResult.Skip;
            }

            // Empty values usually mean the editor has no comparable current record.
            foreach (var kv in dataReport)
            {
                if (kv.Key == "addr")
                    continue;

                if (string.IsNullOrWhiteSpace(kv.Value))
                {
                    return DataVerifyComparisonResult.Skip;
                }
            }

            return DataVerifyComparisonResult.Match;
        }

        /// <summary>
        /// Per-field cross-check: compares each GetDataReport field to the corresponding
        /// GetRawRomReport field using the GetFieldOffsetMap mapping.
        /// Returns the number of field mismatches found.
        /// </summary>
        static int FieldLevelCrossCheck(string viewName, int itemIdx,
            Dictionary<string, string> dataReport,
            Dictionary<string, string> rawReport,
            Dictionary<string, string> fieldMap)
        {
            int mismatches = 0;
            foreach (var (fieldName, offsetKey) in fieldMap)
            {
                if (!dataReport.TryGetValue(fieldName, out string? dataVal))
                    continue;
                if (!rawReport.TryGetValue(offsetKey, out string? rawVal))
                    continue;

                // Normalize comparison: both should be hex strings like "0xNN"
                // Handle signed→unsigned: data may format (byte)(-1) as "0xFF" vs raw "0xFF" (already OK)
                // Handle decimal promo values: e.g. "2" vs "0x02" — normalize
                string normData = NormalizeHexValue(dataVal);
                string normRaw = NormalizeHexValue(rawVal);

                if (!string.Equals(normData, normRaw, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches++;
                    Console.WriteLine($"FIELDMISMATCH: {viewName}|item={itemIdx}|field={fieldName}|data={dataVal}|raw={rawVal}|offset={offsetKey}");
                }
            }
            return mismatches;
        }

        /// <summary>
        /// Normalizes a hex value string for comparison.
        /// Handles: "0xFF" → "0xFF", "255" → "0xFF", "0x0001" → "0x0001"
        /// Also handles signed byte values: "-1" → "0xFF", "37" → "0x25"
        /// </summary>
        static string NormalizeHexValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // Already hex-formatted
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return value.ToUpperInvariant();

            // Try parsing as decimal integer and convert to hex
            if (int.TryParse(value, out int decVal))
            {
                // Unsigned byte range (0..255)
                if (decVal >= 0 && decVal <= 255)
                    return $"0x{(byte)decVal:X02}";
                // Signed byte range (-128..-1): treat as unsigned byte for ROM comparison
                // e.g. -1 → (byte)(-1) = 0xFF, matching the u8 raw ROM value
                if (decVal >= -128 && decVal < 0)
                    return $"0x{(byte)(sbyte)decVal:X02}";
                return $"0x{decVal:X08}";
            }

            return value;
        }

        /// <summary>
        /// Checks that all NumericUpDown controls in the window display non-empty text.
        /// Returns true if no NumericUpDown has empty/null Value after data loading.
        /// This catches rendering bugs like FormatString="X" on decimal-typed NumericUpDown.
        /// </summary>
        static bool CheckNumericUpDownsDisplayValues(string viewName, Control window)
        {
            var nuds = new List<NumericUpDown>();
            foreach (var descendant in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window))
            {
                if (descendant is NumericUpDown nud)
                    nuds.Add(nud);
            }

            if (nuds.Count == 0) return true; // No NumericUpDown controls → pass

            int emptyCount = 0;
            var emptyNames = new List<string>();
            foreach (var nud in nuds)
            {
                // Skip NUDs that are not effectively visible — they may have been
                // intentionally hidden (or inside a hidden parent panel) because they
                // don't apply to the current ROM version/struct size/instrument type.
                // E.g. EventCondView hides ExtraB12-B15 for FE6/FE8 (12-byte records),
                // SongInstrumentView hides DirectSoundPanel for SquareWave instruments.
                // Use IsEffectivelyVisible to check the entire visual parent chain.
                if (!nud.IsEffectivelyVisible) continue;

                if (nud.Value == null)
                {
                    emptyCount++;
                    string nudName = nud.Name ?? "(unnamed)";
                    emptyNames.Add(nudName);
                }
            }

            if (emptyCount > 0)
            {
                Console.WriteLine($"UIVERIFY: {viewName}|emptyNUDs={emptyCount}|names={string.Join(",", emptyNames)}");
                return false;
            }

            Console.WriteLine($"UIVERIFY: {viewName}|allNUDs={nuds.Count}|OK");
            return true;
        }

        /// <summary>
        /// Verifies text encoding is correctly initialized by decoding text ID 1
        /// and checking the result doesn't contain replacement characters (U+FFFD).
        /// Prints TEXTVERIFY lines for E2E test parsing.
        /// </summary>
        static void VerifyTextEncoding()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            string encoderType = CoreState.SystemTextEncoder?.GetType().Name ?? "null";
            string encoderDetail = "";
            if (CoreState.SystemTextEncoder is HeadlessSystemTextEncoder hse)
                encoderDetail = $"|encoding={hse.EncodingName}";

            bool isMultibyte = rom.RomInfo.is_multibyte;
            Console.WriteLine($"TEXTVERIFY: encoder={encoderType}{encoderDetail}|is_multibyte={isMultibyte}|version={rom.RomInfo.version}");

            try
            {
                // Decode text ID 1 (typically the first character's name)
                string decoded = FETextDecode.Direct(1);
                bool hasReplacement = decoded.Contains('\uFFFD');
                bool isEmpty = string.IsNullOrEmpty(decoded) || decoded == "???" || decoded == "(empty)";

                // For JP ROMs (is_multibyte), text should contain CJK characters (U+3000-U+FF00 range)
                bool hasCJK = false;
                if (isMultibyte && !isEmpty)
                {
                    foreach (char c in decoded)
                    {
                        if (c >= 0x3000 && c <= 0xFF00)
                        {
                            hasCJK = true;
                            break;
                        }
                    }
                }

                string status = "OK";
                if (hasReplacement) status = "REPLACEMENT_CHARS";
                else if (isEmpty) status = "EMPTY";
                else if (isMultibyte && !hasCJK) status = "NO_CJK";

                Console.WriteLine($"TEXTVERIFY: textId=1|decoded={EscapeForLog(decoded)}|status={status}|hasCJK={hasCJK}|hasReplacement={hasReplacement}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TEXTVERIFY: textId=1|error={ex.Message}");
            }
        }

        static string EscapeForLog(string s)
        {
            if (s == null) return "(null)";
            // Keep it short and escape control characters
            if (s.Length > 60) s = s.Substring(0, 60) + "...";
            return s.Replace("\r", "").Replace("\n", "\\n").Replace("|", "\\|");
        }

        /// <summary>
        /// Returns a list of (name, factory) pairs for all editors opened from MainWindow.
        /// </summary>
        static List<(string Name, Func<Window> Factory)> GetAllEditorFactories()
        {
            var wm = WindowManager.Instance;
            return new List<(string, Func<Window>)>
            {
                // Data Editors
                ("UnitEditorView", () => wm.Open<UnitEditorView>()),
                ("ItemEditorView", () => wm.Open<ItemEditorView>()),
                ("ClassEditorView", () => wm.Open<ClassEditorView>()),
                ("ClassFE6View", () => wm.Open<ClassFE6View>()),
                ("CCBranchEditorView", () => wm.OpenAsTopLevel<CCBranchEditorView>()),
                ("MoveCostEditorView", () => wm.OpenAsTopLevel<MoveCostEditorView>()),
                ("TerrainNameEditorView", () => wm.OpenAsTopLevel<TerrainNameEditorView>()),
                ("SupportUnitEditorView", () => wm.OpenAsTopLevel<SupportUnitEditorView>()),
                ("SupportAttributeView", () => wm.OpenAsTopLevel<SupportAttributeView>()),
                ("SupportTalkView", () => wm.OpenAsTopLevel<SupportTalkView>()),
                ("UnitFE6View", () => wm.OpenAsTopLevel<UnitFE6View>()),
                ("UnitActionPointerView", () => wm.OpenAsTopLevel<UnitActionPointerView>()),
                ("UnitCustomBattleAnimeView", () => wm.OpenAsTopLevel<UnitCustomBattleAnimeView>()),
                ("UnitIncreaseHeightView", () => wm.Open<UnitIncreaseHeightView>()),
                ("UnitPaletteView", () => wm.OpenAsTopLevel<UnitPaletteView>()),
                ("ClassOPDemoView", () => wm.OpenAsTopLevel<ClassOPDemoView>()),
                ("ClassOPFontView", () => wm.OpenAsTopLevel<ClassOPFontView>()),
                ("ExtraUnitView", () => wm.OpenAsTopLevel<ExtraUnitView>()),
                ("ExtraUnitFE8UView", () => wm.OpenAsTopLevel<ExtraUnitFE8UView>()),

                // Item Editors
                ("ItemWeaponEffectViewerView", () => wm.OpenAsTopLevel<ItemWeaponEffectViewerView>()),
                ("ItemStatBonusesViewerView", () => wm.OpenAsTopLevel<ItemStatBonusesViewerView>()),
                ("ItemEffectivenessViewerView", () => wm.OpenAsTopLevel<ItemEffectivenessViewerView>()),
                ("ItemPromotionViewerView", () => wm.OpenAsTopLevel<ItemPromotionViewerView>()),
                ("ItemShopViewerView", () => wm.OpenAsTopLevel<ItemShopViewerView>()),
                ("ItemWeaponTriangleViewerView", () => wm.OpenAsTopLevel<ItemWeaponTriangleViewerView>()),
                ("ItemUsagePointerViewerView", () => wm.OpenAsTopLevel<ItemUsagePointerViewerView>()),
                ("ItemEffectPointerViewerView", () => wm.OpenAsTopLevel<ItemEffectPointerViewerView>()),
                ("ItemIconViewerView", () => wm.Open<ItemIconViewerView>()),

                // Map Editors
                ("MapSettingView", () => wm.Open<MapSettingView>()),
                ("MapChangeView", () => wm.OpenAsTopLevel<MapChangeView>()),
                ("MapExitPointView", () => wm.OpenAsTopLevel<MapExitPointView>()),
                ("MapPointerView", () => wm.OpenAsTopLevel<MapPointerView>()),
                ("MapTileAnimationView", () => wm.OpenAsTopLevel<MapTileAnimationView>()),
                ("MapEditorView", () => wm.Open<MapEditorView>()),
                ("MapSettingFE6View", () => wm.Open<MapSettingFE6View>()),
                ("MapSettingFE7View", () => wm.Open<MapSettingFE7View>()),
                ("MapSettingFE7UView", () => wm.Open<MapSettingFE7UView>()),
                ("MapSettingDifficultyView", () => wm.OpenAsTopLevel<MapSettingDifficultyView>()),
                ("MapStyleEditorView", () => wm.Open<MapStyleEditorView>()),
                ("MapTerrainBGLookupView", () => wm.OpenAsTopLevel<MapTerrainBGLookupView>()),
                ("MapTerrainFloorLookupView", () => wm.OpenAsTopLevel<MapTerrainFloorLookupView>()),
                ("MapTerrainBGLookupTableView", () => wm.OpenAsTopLevel<MapTerrainBGLookupTableView>()),
                ("MapTerrainFloorLookupTableView", () => wm.OpenAsTopLevel<MapTerrainFloorLookupTableView>()),
                ("MapMiniMapTerrainImageView", () => wm.OpenAsTopLevel<MapMiniMapTerrainImageView>()),
                ("MapTileAnimation1View", () => wm.Open<MapTileAnimation1View>()),
                ("MapTileAnimation2View", () => wm.Open<MapTileAnimation2View>()),
                ("MapLoadFunctionView", () => wm.OpenAsTopLevel<MapLoadFunctionView>()),
                ("MapTerrainNameEngView", () => wm.OpenAsTopLevel<MapTerrainNameEngView>()),
                ("MapTerrainNameView", () => wm.OpenAsTopLevel<MapTerrainNameView>()),

                // Event Script Editors
                ("EventCondView", () => wm.Open<EventCondView>()),
                ("EventScriptView", () => wm.Open<EventScriptView>()),
                ("EventUnitView", () => wm.Open<EventUnitView>()),
                ("EventUnitFE6View", () => wm.OpenAsTopLevel<EventUnitFE6View>()),
                ("EventUnitFE7View", () => wm.Open<EventUnitFE7View>()),
                ("EventUnitColorView", () => wm.Open<EventUnitColorView>()),
                ("EventUnitItemDropView", () => wm.OpenAsTopLevel<EventUnitItemDropView>()),
                ("EventUnitNewAllocView", () => wm.Open<EventUnitNewAllocView>()),
                ("EventBattleTalkView", () => wm.OpenAsTopLevel<EventBattleTalkView>()),
                ("EventBattleTalkFE6View", () => wm.OpenAsTopLevel<EventBattleTalkFE6View>()),
                ("EventBattleTalkFE7View", () => wm.OpenAsTopLevel<EventBattleTalkFE7View>()),
                ("EventBattleDataFE7View", () => wm.OpenAsTopLevel<EventBattleDataFE7View>()),
                ("EventHaikuView", () => wm.OpenAsTopLevel<EventHaikuView>()),
                ("EventHaikuFE6View", () => wm.OpenAsTopLevel<EventHaikuFE6View>()),
                ("EventHaikuFE7View", () => wm.OpenAsTopLevel<EventHaikuFE7View>()),
                ("EventMapChangeView", () => wm.Open<EventMapChangeView>()),
                ("EventForceSortieView", () => wm.OpenAsTopLevel<EventForceSortieView>()),
                ("EventForceSortieFE7View", () => wm.OpenAsTopLevel<EventForceSortieFE7View>()),
                ("EventFunctionPointerView", () => wm.OpenAsTopLevel<EventFunctionPointerView>()),
                ("EventFunctionPointerFE7View", () => wm.OpenAsTopLevel<EventFunctionPointerFE7View>()),
                ("EventAssemblerView", () => wm.Open<EventAssemblerView>()),
                ("ProcsScriptView", () => wm.Open<ProcsScriptCategorySelectView>()),
                ("EventScriptTemplateView", () => wm.Open<EventScriptTemplateView>()),

                // AI Script Editors
                ("AIScriptView", () => wm.Open<AIScriptView>()),
                ("AIASMCALLTALKView", () => wm.OpenAsTopLevel<AIASMCALLTALKView>()),
                ("AIASMCoordinateView", () => wm.OpenAsTopLevel<AIASMCoordinateView>()),
                ("AIASMRangeView", () => wm.OpenAsTopLevel<AIASMRangeView>()),
                ("AIMapSettingView", () => wm.OpenAsTopLevel<AIMapSettingView>()),
                ("AIPerformItemView", () => wm.OpenAsTopLevel<AIPerformItemView>()),
                ("AIPerformStaffView", () => wm.OpenAsTopLevel<AIPerformStaffView>()),
                ("AIStealItemView", () => wm.OpenAsTopLevel<AIStealItemView>()),
                ("AITargetView", () => wm.OpenAsTopLevel<AITargetView>()),
                ("AITilesView", () => wm.OpenAsTopLevel<AITilesView>()),
                ("AIUnitsView", () => wm.OpenAsTopLevel<AIUnitsView>()),
                ("AOERANGEView", () => wm.OpenAsTopLevel<AOERANGEView>()),

                // Image Editors
                ("ImageViewerView", () => wm.Open<ImageViewerView>()),
                ("PortraitViewerView", () => wm.Open<PortraitViewerView>()),
                ("ImagePortraitView", () => wm.Open<ImagePortraitView>()),
                ("ImagePortraitFE6View", () => wm.Open<ImagePortraitFE6View>()),
                ("ImagePortraitImporterView", () => wm.Open<ImagePortraitImporterView>()),
                ("ImageBGView", () => wm.Open<ImageBGView>()),
                ("ImageBattleAnimeView", () => wm.Open<ImageBattleAnimeView>()),
                ("ImageBattleAnimePalletView", () => wm.Open<ImageBattleAnimePalletView>()),
                ("ImageBattleBGView", () => wm.Open<ImageBattleBGView>()),
                ("ImageBattleScreenView", () => wm.Open<ImageBattleScreenView>()),
                ("ImageCGView", () => wm.Open<ImageCGView>()),
                ("ImageCGFE7UView", () => wm.Open<ImageCGFE7UView>()),
                ("ImageUnitPaletteView", () => wm.Open<ImageUnitPaletteView>()),
                ("ImageUnitWaitIconView", () => wm.Open<ImageUnitWaitIconView>()),
                ("ImageUnitMoveIconView", () => wm.Open<ImageUnitMoveIconView>()),
                ("ImageSystemAreaView", () => wm.OpenAsTopLevel<ImageSystemAreaView>()),
                ("ImageGenericEnemyPortraitView", () => wm.Open<ImageGenericEnemyPortraitView>()),
                ("ImageRomAnimeView", () => wm.Open<ImageRomAnimeView>()),
                ("ImageTSAEditorView", () => wm.Open<ImageTSAEditorView>()),
                ("ImageTSAAnimeView", () => wm.Open<ImageTSAAnimeView>()),
                ("ImageTSAAnime2View", () => wm.Open<ImageTSAAnime2View>()),
                ("ImagePalletView", () => wm.Open<ImagePalletView>()),
                ("ImageMagicFEditorView", () => wm.Open<ImageMagicFEditorView>()),
                ("ImageMagicCSACreatorView", () => wm.Open<ImageMagicCSACreatorView>()),
                ("ImageMapActionAnimationView", () => wm.Open<ImageMapActionAnimationView>()),
                ("DecreaseColorTSAToolView", () => wm.Open<DecreaseColorTSAToolView>()),
                ("SystemIconViewerView", () => wm.Open<SystemIconViewerView>()),
                ("SystemHoverColorViewerView", () => wm.OpenAsTopLevel<SystemHoverColorViewerView>()),
                ("BattleBGViewerView", () => wm.Open<BattleBGViewerView>()),
                ("BattleTerrainViewerView", () => wm.Open<BattleTerrainViewerView>()),
                ("ChapterTitleViewerView", () => wm.Open<ChapterTitleViewerView>()),
                ("ImageChapterTitleFE7View", () => wm.Open<ImageChapterTitleFE7View>()),
                ("BigCGViewerView", () => wm.Open<BigCGViewerView>()),
                ("OPClassDemoViewerView", () => wm.OpenAsTopLevel<OPClassDemoViewerView>()),
                ("OPClassFontViewerView", () => wm.Open<OPClassFontViewerView>()),
                ("OPPrologueViewerView", () => wm.Open<OPPrologueViewerView>()),

                // Audio Editors
                ("SongTableView", () => wm.Open<SongTableView>()),
                ("SongTrackView", () => wm.Open<SongTrackView>()),
                ("SongInstrumentView", () => wm.Open<SongInstrumentView>()),
                ("SongInstrumentDirectSoundView", () => wm.OpenAsTopLevel<SongInstrumentDirectSoundView>()),
                ("SongInstrumentImportWaveView", () => wm.Open<SongInstrumentImportWaveView>()),
                ("SongTrackImportMidiView", () => wm.Open<SongTrackImportMidiView>()),
                ("SongExchangeView", () => wm.Open<SongExchangeView>()),
                ("SoundBossBGMViewerView", () => wm.OpenAsTopLevel<SoundBossBGMViewerView>()),
                ("SoundFootStepsViewerView", () => wm.OpenAsTopLevel<SoundFootStepsViewerView>()),
                ("SoundRoomViewerView", () => wm.Open<SoundRoomViewerView>()),
                ("SoundRoomFE6View", () => wm.OpenAsTopLevel<SoundRoomFE6View>()),
                ("SoundRoomCGView", () => wm.OpenAsTopLevel<SoundRoomCGView>()),

                // Arena / Monster / Summon Editors
                ("ArenaClassViewerView", () => wm.OpenAsTopLevel<ArenaClassViewerView>()),
                ("ArenaEnemyWeaponViewerView", () => wm.OpenAsTopLevel<ArenaEnemyWeaponViewerView>()),
                ("LinkArenaDenyUnitViewerView", () => wm.OpenAsTopLevel<LinkArenaDenyUnitViewerView>()),
                ("MonsterProbabilityViewerView", () => wm.OpenAsTopLevel<MonsterProbabilityViewerView>()),
                ("MonsterItemViewerView", () => wm.OpenAsTopLevel<MonsterItemViewerView>()),
                ("MonsterWMapProbabilityViewerView", () => wm.OpenAsTopLevel<MonsterWMapProbabilityViewerView>()),
                ("SummonUnitViewerView", () => wm.Open<SummonUnitViewerView>()),
                ("SummonsDemonKingViewerView", () => wm.Open<SummonsDemonKingViewerView>()),

                // Menu / ED / World Map Editors
                ("MenuDefinitionView", () => wm.OpenAsTopLevel<MenuDefinitionView>()),
                ("MenuCommandView", () => wm.OpenAsTopLevel<MenuCommandView>()),
                ("EDView", () => wm.OpenAsTopLevel<EDView>()),
                ("EDStaffRollView", () => wm.OpenAsTopLevel<EDStaffRollView>()),
                ("WorldMapPointView", () => wm.OpenAsTopLevel<WorldMapPointView>()),
                ("WorldMapBGMView", () => wm.OpenAsTopLevel<WorldMapBGMView>()),
                ("WorldMapEventPointerView", () => wm.OpenAsTopLevel<WorldMapEventPointerView>()),
                ("WorldMapPathView", () => wm.OpenAsTopLevel<WorldMapPathView>()),
                ("WorldMapPathEditorView", () => wm.Open<WorldMapPathEditorView>()),
                ("WorldMapImageView", () => wm.Open<WorldMapImageView>()),
                ("WorldMapImageFE6View", () => wm.Open<WorldMapImageFE6View>()),
                ("WorldMapImageFE7View", () => wm.Open<WorldMapImageFE7View>()),
                ("WorldMapEventPointerFE6View", () => wm.OpenAsTopLevel<WorldMapEventPointerFE6View>()),
                ("WorldMapEventPointerFE7View", () => wm.OpenAsTopLevel<WorldMapEventPointerFE7View>()),

                // Text / Translation Editors
                ("TextViewerView", () => wm.Open<TextViewerView>()),
                ("TextMainView", () => wm.OpenAsTopLevel<TextMainView>()),
                ("OtherTextView", () => wm.OpenAsTopLevel<OtherTextView>()),
                ("CStringView", () => wm.OpenAsTopLevel<CStringView>()),
                ("FontEditorView", () => wm.Open<FontEditorView>()),
                ("FontZHView", () => wm.Open<FontZHView>()),
                ("DevTranslateView", () => wm.Open<DevTranslateView>()),
                ("ToolTranslateROMView", () => wm.Open<ToolTranslateROMView>()),
                ("TextEscapeEditorView", () => wm.OpenAsTopLevel<TextEscapeEditorView>()),

                // Structural Data
                ("Command85PointerView", () => wm.OpenAsTopLevel<Command85PointerView>()),
                ("FE8SpellMenuExtendsView", () => wm.Open<FE8SpellMenuExtendsView>()),
                ("StatusOptionView", () => wm.Open<StatusOptionView>()),
                ("OAMSPView", () => wm.Open<OAMSPView>()),
                ("DumpStructSelectDialogView", () => wm.Open<DumpStructSelectDialogView>()),

                // Patch / Skill Systems
                ("PatchManagerView", () => wm.Open<PatchManagerView>()),
                ("ToolCustomBuildView", () => wm.Open<ToolCustomBuildView>()),
                ("SkillAssignmentUnitSkillSystemView", () => wm.Open<SkillAssignmentUnitSkillSystemView>()),
                ("SkillAssignmentClassSkillSystemView", () => wm.Open<SkillAssignmentClassSkillSystemView>()),
                ("SkillConfigSkillSystemView", () => wm.Open<SkillConfigSkillSystemView>()),

                // Tools
                ("ToolUndoView", () => wm.Open<ToolUndoView>()),
                ("ToolFELintView", () => wm.OpenAsTopLevel<ToolFELintView>()),
                ("ToolROMRebuildView", () => wm.Open<ToolROMRebuildView>()),
                ("ToolLZ77View", () => wm.Open<ToolLZ77View>()),
                ("ToolDiffView", () => wm.Open<ToolDiffView>()),
                ("ToolUPSPatchSimpleView", () => wm.Open<ToolUPSPatchSimpleView>()),
                ("ToolUPSOpenSimpleView", () => wm.Open<ToolUPSOpenSimpleView>()),
                ("ToolFlagNameView", () => wm.OpenAsTopLevel<ToolFlagNameView>()),
                ("ToolUseFlagView", () => wm.OpenAsTopLevel<ToolUseFlagView>()),
                ("ToolUnitTalkGroupView", () => wm.OpenAsTopLevel<ToolUnitTalkGroupView>()),
                ("ToolASMInsertView", () => wm.Open<ToolASMInsertView>()),
                ("HexEditorView", () => wm.OpenAsTopLevel<HexEditorView>()),
                ("DisASMView", () => wm.OpenAsTopLevel<DisASMView>()),
                ("LogViewerView", () => wm.Open<LogViewerView>()),
                ("GrowSimulatorView", () => wm.OpenAsTopLevel<GrowSimulatorView>()),
                ("OptionsView", () => wm.Open<OptionsView>()),

                // === GAP CLOSURE: New editors added by gap analysis ===

                // Status Screen Editors (WU2)
                ("StatusParamView", () => wm.OpenAsTopLevel<StatusParamView>()),
                ("StatusRMenuView", () => wm.OpenAsTopLevel<StatusRMenuView>()),
                ("StatusUnitsMenuView", () => wm.OpenAsTopLevel<StatusUnitsMenuView>()),
                ("StatusOptionOrderView", () => wm.Open<StatusOptionOrderView>()),

                // Skill System Editors (WU3)
                ("SkillAssignmentUnitCSkillSysView", () => wm.Open<SkillAssignmentUnitCSkillSysView>()),
                ("SkillAssignmentClassCSkillSysView", () => wm.Open<SkillAssignmentClassCSkillSysView>()),
                ("SkillAssignmentUnitFE8NView", () => wm.Open<SkillAssignmentUnitFE8NView>()),
                ("SkillConfigFE8NSkillView", () => wm.Open<SkillConfigFE8NSkillView>()),
                ("SkillConfigFE8NVer2SkillView", () => wm.Open<SkillConfigFE8NVer2SkillView>()),
                ("SkillConfigFE8NVer3SkillView", () => wm.Open<SkillConfigFE8NVer3SkillView>()),
                ("SkillConfigFE8UCSkillSys09xView", () => wm.Open<SkillConfigFE8UCSkillSys09xView>()),

                // Song/Audio Dialogs (WU4)
                ("ToolBGMMuteDialogView", () => wm.OpenAsTopLevel<ToolBGMMuteDialogView>()),

                // Event/Text Sub-forms (WU5)
                ("EventScriptCategorySelectView", () => wm.Open<EventScriptCategorySelectView>()),
                ("EventScriptPopupView", () => wm.Open<EventScriptPopupView>()),
                ("ProcsScriptCategorySelectView", () => wm.Open<ProcsScriptCategorySelectView>()),
                ("AIScriptCategorySelectView", () => wm.Open<AIScriptCategorySelectView>()),
                ("TextScriptCategorySelectView", () => wm.Open<TextScriptCategorySelectView>()),
                ("TextDicView", () => wm.OpenAsTopLevel<TextDicView>()),
                ("TextCharCodeView", () => wm.OpenAsTopLevel<TextCharCodeView>()),
                ("TextBadCharPopupView", () => wm.Open<TextBadCharPopupView>()),
                ("TextRefAddDialogView", () => wm.Open<TextRefAddDialogView>()),
                ("TextToSpeechView", () => wm.OpenAsTopLevel<TextToSpeechView>()),

                // Graphics Tool Forms (WU6)
                ("GraphicsToolView", () => wm.OpenAsTopLevel<GraphicsToolView>()),
                ("GraphicsToolPatchMakerView", () => wm.Open<GraphicsToolPatchMakerView>()),
                ("PaletteChangeColorsView", () => wm.OpenAsTopLevel<PaletteChangeColorsView>()),
                ("PaletteClipboardView", () => wm.Open<PaletteClipboardView>()),
                ("PaletteSwapView", () => wm.Open<PaletteSwapView>()),
                ("ImageBGSelectPopupView", () => wm.Open<ImageBGSelectPopupView>()),

                // Map Sub-dialog Forms (WU7)
                ("MapEditorAddMapChangeDialogView", () => wm.Open<MapEditorAddMapChangeDialogView>()),
                ("MapEditorMarSizeDialogView", () => wm.Open<MapEditorMarSizeDialogView>()),
                ("MapEditorResizeDialogView", () => wm.Open<MapEditorResizeDialogView>()),
                ("MapPointerNewPLISTPopupView", () => wm.Open<MapPointerNewPLISTPopupView>()),
                ("MapStyleEditorAppendPopupView", () => wm.Open<MapStyleEditorAppendPopupView>()),
                ("MapStyleEditorWarningOverrideView", () => wm.Open<MapStyleEditorWarningOverrideView>()),
                ("MapStyleEditorImportImageOptionView", () => wm.Open<MapStyleEditorImportImageOptionView>()),
                ("MapSettingDifficultyDialogView", () => wm.Open<MapSettingDifficultyDialogView>()),

                // Tool/Utility Forms Part 1 (WU8)
                ("DisASMDumpAllView", () => wm.Open<DisASMDumpAllView>()),
                ("DisASMDumpAllArgGrepView", () => wm.Open<DisASMDumpAllArgGrepView>()),
                ("HexEditorJumpView", () => wm.Open<HexEditorJumpView>()),
                ("HexEditorMarkView", () => wm.Open<HexEditorMarkView>()),
                ("HexEditorSearchView", () => wm.Open<HexEditorSearchView>()),
                ("PointerToolView", () => wm.Open<PointerToolView>()),
                ("PointerToolBatchInputView", () => wm.Open<PointerToolBatchInputView>()),
                ("PointerToolCopyToView", () => wm.Open<PointerToolCopyToView>()),
                ("PackedMemorySlotView", () => wm.Open<PackedMemorySlotView>()),
                ("EmulatorMemoryView", () => wm.OpenAsTopLevel<EmulatorMemoryView>()),

                // Tool/Utility Forms Part 2 (WU9)
                ("RAMRewriteToolMAPView", () => wm.OpenAsTopLevel<RAMRewriteToolMAPView>()),
                ("ToolAnimationCreatorView", () => wm.Open<ToolAnimationCreatorView>()),
                ("ToolThreeMargeView", () => wm.Open<ToolThreeMargeView>()),
                ("ToolASMEditView", () => wm.Open<ToolASMEditView>()),
                ("ToolExportEAEventView", () => wm.Open<ToolExportEAEventView>()),
                ("ToolDecompileResultView", () => wm.OpenAsTopLevel<ToolDecompileResultView>()),
                ("ToolChangeProjectnameView", () => wm.Open<ToolChangeProjectnameView>()),
                ("ToolAutomaticRecoveryROMHeaderView", () => wm.Open<ToolAutomaticRecoveryROMHeaderView>()),
                ("MoveToFreeSpaceView", () => wm.OpenAsTopLevel<MoveToFreeSpaceView>()),
                ("ToolSubtitleOverlayView", () => wm.OpenAsTopLevel<ToolSubtitleOverlayView>()),
                ("ToolSubtitleSettingDialogView", () => wm.Open<ToolSubtitleSettingDialogView>()),

                // Error/Dialog Forms (WU10)
                ("ErrorReportView", () => wm.OpenAsTopLevel<ErrorReportView>()),
                ("ErrorPaletteMissMatchView", () => wm.OpenAsTopLevel<ErrorPaletteMissMatchView>()),
                ("ErrorPaletteShowView", () => wm.OpenAsTopLevel<ErrorPaletteShowView>()),
                ("ErrorPaletteTransparentView", () => wm.OpenAsTopLevel<ErrorPaletteTransparentView>()),
                ("ErrorTSAErrorView", () => wm.OpenAsTopLevel<ErrorTSAErrorView>()),
                ("ErrorLongMessageDialogView", () => wm.OpenAsTopLevel<ErrorLongMessageDialogView>()),
                ("ErrorUnknownROMView", () => wm.Open<ErrorUnknownROMView>()),
                ("DumpStructSelectToTextDialogView", () => wm.Open<DumpStructSelectToTextDialogView>()),
                ("HowDoYouLikePatchView", () => wm.Open<HowDoYouLikePatchView>()),
                ("HowDoYouLikePatch2View", () => wm.OpenAsTopLevel<HowDoYouLikePatch2View>()),
                ("PatchFilterExView", () => wm.OpenAsTopLevel<PatchFilterExView>()),
                ("PatchFormUninstallDialogView", () => wm.Open<PatchFormUninstallDialogView>()),

                // Version-Specific / Specialized Forms (WU11)
                ("ItemFE6View", () => wm.Open<ItemFE6View>()),
                ("MoveCostFE6View", () => wm.OpenAsTopLevel<MoveCostFE6View>()),
                ("SupportUnitFE6View", () => wm.OpenAsTopLevel<SupportUnitFE6View>()),
                ("SupportTalkFE6View", () => wm.OpenAsTopLevel<SupportTalkFE6View>()),
                ("SupportTalkFE7View", () => wm.OpenAsTopLevel<SupportTalkFE7View>()),
                ("UnitFE7View", () => wm.OpenAsTopLevel<UnitFE7View>()),
                ("OPClassDemoFE7View", () => wm.OpenAsTopLevel<OPClassDemoFE7View>()),
                ("OPClassDemoFE7UView", () => wm.OpenAsTopLevel<OPClassDemoFE7UView>()),
                ("OPClassDemoFE8UView", () => wm.OpenAsTopLevel<OPClassDemoFE8UView>()),
                ("OPClassFontFE8UView", () => wm.OpenAsTopLevel<OPClassFontFE8UView>()),
                ("OPClassAlphaNameView", () => wm.OpenAsTopLevel<OPClassAlphaNameView>()),
                ("OPClassAlphaNameFE6View", () => wm.OpenAsTopLevel<OPClassAlphaNameFE6View>()),
                ("SomeClassListView", () => wm.OpenAsTopLevel<SomeClassListView>()),
                ("VennouWeaponLockView", () => wm.OpenAsTopLevel<VennouWeaponLockView>()),
                ("UnitsShortTextView", () => wm.OpenAsTopLevel<UnitsShortTextView>()),
                ("UbyteBitFlagView", () => wm.Open<UbyteBitFlagView>()),
                ("UshortBitFlagView", () => wm.Open<UshortBitFlagView>()),
                ("UwordBitFlagView", () => wm.Open<UwordBitFlagView>()),

                // === Event Templates (WU15) ===
                ("EventTemplate1View", () => wm.Open<EventTemplate1View>()),
                ("EventTemplate2View", () => wm.Open<EventTemplate2View>()),
                ("EventTemplate3View", () => wm.Open<EventTemplate3View>()),
                ("EventTemplate4View", () => wm.Open<EventTemplate4View>()),
                ("EventTemplate5View", () => wm.Open<EventTemplate5View>()),
                ("EventTemplate6View", () => wm.Open<EventTemplate6View>()),
                ("EventTemplateImplView", () => wm.Open<EventTemplateImplView>()),
                ("EventFinalSerifFE7View", () => wm.OpenAsTopLevel<EventFinalSerifFE7View>()),
                ("EventMoveDataFE7View", () => wm.OpenAsTopLevel<EventMoveDataFE7View>()),
                ("EventTalkGroupFE7View", () => wm.OpenAsTopLevel<EventTalkGroupFE7View>()),

                // === Audio Sub-Forms (WU16) ===
                ("SongTrackChangeTrackView", () => wm.OpenAsTopLevel<SongTrackChangeTrackView>()),
                ("SongTrackAllChangeTrackView", () => wm.OpenAsTopLevel<SongTrackAllChangeTrackView>()),
                ("SongTrackImportSelectInstrumentView", () => wm.Open<SongTrackImportSelectInstrumentView>()),

                // === ED/Credits + Item Variants (WU17) ===
                ("EDFE6View", () => wm.OpenAsTopLevel<EDFE6View>()),
                ("EDFE7View", () => wm.OpenAsTopLevel<EDFE7View>()),
                ("EDSensekiCommentView", () => wm.OpenAsTopLevel<EDSensekiCommentView>()),
                ("ItemStatBonusesSkillSystemsView", () => wm.OpenAsTopLevel<ItemStatBonusesSkillSystemsView>()),
                ("ItemStatBonusesVennoView", () => wm.OpenAsTopLevel<ItemStatBonusesVennoView>()),
                ("ItemEffectivenessSkillSystemsReworkView", () => wm.OpenAsTopLevel<ItemEffectivenessSkillSystemsReworkView>()),
                ("ItemRandomChestView", () => wm.OpenAsTopLevel<ItemRandomChestView>()),
                ("MenuExtendSplitMenuView", () => wm.OpenAsTopLevel<MenuExtendSplitMenuView>()),

                // === App Infrastructure (WU18) ===
                ("VersionView", () => wm.OpenAsTopLevel<VersionView>()),
                ("WelcomeView", () => wm.Open<WelcomeView>()),
                ("ResourceView", () => wm.OpenAsTopLevel<ResourceView>()),
                ("ToolInitWizardView", () => wm.Open<ToolInitWizardView>()),
                ("ToolUndoPopupDialogView", () => wm.Open<ToolUndoPopupDialogView>()),
                ("OpenLastSelectedFileView", () => wm.OpenAsTopLevel<OpenLastSelectedFileView>()),
                ("ToolUpdateDialogView", () => wm.OpenAsTopLevel<ToolUpdateDialogView>()),

                // === Previously Unregistered On-Disk Views (WU19) ===
                ("ToolAllWorkSupportView", () => wm.Open<ToolAllWorkSupportView>()),
                ("ToolProblemReportView", () => wm.Open<ToolProblemReportView>()),
                ("WorldMapPathMoveEditorView", () => wm.OpenAsTopLevel<WorldMapPathMoveEditorView>()),
                ("MantAnimationView", () => wm.Open<MantAnimationView>()),
                ("RAMRewriteToolView", () => wm.OpenAsTopLevel<RAMRewriteToolView>()),
                ("MainSimpleMenuView", () => wm.OpenAsTopLevel<MainSimpleMenuView>()),
                ("MainSimpleMenuEventErrorView", () => wm.OpenAsTopLevel<MainSimpleMenuEventErrorView>()),
                ("MainSimpleMenuImageSubView", () => wm.OpenAsTopLevel<MainSimpleMenuImageSubView>()),

                // === Small Dialog/Message Views (WU19-dialogs) ===
                ("ToolEmulatorSetupMessageView", () => wm.OpenAsTopLevel<ToolEmulatorSetupMessageView>()),
                ("ToolThreeMargeCloseAlertView", () => wm.OpenAsTopLevel<ToolThreeMargeCloseAlertView>()),
                ("ToolClickWriteFloatControlPanelButtonView", () => wm.OpenAsTopLevel<ToolClickWriteFloatControlPanelButtonView>()),
                ("ToolWorkSupport_UpdateQuestionDialogView", () => wm.Open<ToolWorkSupport_UpdateQuestionDialogView>()),
                ("MainSimpleMenuEventErrorIgnoreErrorView", () => wm.OpenAsTopLevel<MainSimpleMenuEventErrorIgnoreErrorView>()),
                ("ToolProblemReportSearchBackupView", () => wm.Open<ToolProblemReportSearchBackupView>()),
                ("ToolProblemReportSearchSavView", () => wm.Open<ToolProblemReportSearchSavView>()),

                // === Tool Support Views (WU19-tools) ===
                ("ToolWorkSupportView", () => wm.Open<ToolWorkSupportView>()),
                ("ToolWorkSupport_SelectUPSView", () => wm.Open<ToolWorkSupport_SelectUPSView>()),
                ("ToolDiffDebugSelectView", () => wm.Open<ToolDiffDebugSelectView>()),

                // === Specialized Views (WU19-specialized) ===
                ("SMEPromoListView", () => wm.OpenAsTopLevel<SMEPromoListView>()),
                ("ToolRunHintMessageView", () => wm.OpenAsTopLevel<ToolRunHintMessageView>()),
            };
        }

        private async void OpenRom_Click(object? sender, RoutedEventArgs e)
        {
            var file = await FileDialogHelper.OpenRomFilePick(this);
            if (file == null) return;

            // Opening a plain ROM clears any active decomp project (#1129). Cleared
            // only AFTER the picker returns a real file, so cancelling the dialog
            // leaves a currently-open decomp preview (and its save guard) intact.
            CoreState.DecompProject = null;

            string? localPath = file.TryGetLocalPath();
            bool ok;
            if (!string.IsNullOrEmpty(localPath))
            {
                // Desktop / any provider with a real filesystem path — unchanged behavior.
                _currentRomStorageFile = null;
                ok = LoadRomFile(localPath);
            }
            else
            {
                // Android SAF content:// — no local path. Read via the stream API and
                // retain the handle so a later Save can OpenWriteAsync() it (#1124).
                ok = await LoadRomFromStorageFile(file);
            }
            if (!ok)
            {
                await MessageBoxWindow.Show(this, R._("Failed to load ROM."), R._("Error"), MessageBoxMode.Ok);
            }
            UpdateDecompBadge();
        }

        /// <summary>
        /// Load a ROM from a SAF/content:// IStorageFile that has no local filesystem
        /// path (Android). Reads bytes via the stream API, retains the handle for a
        /// later stream Save, and converges on the shared post-load init (#1124).
        /// </summary>
        private async System.Threading.Tasks.Task<bool> LoadRomFromStorageFile(global::Avalonia.Platform.Storage.IStorageFile file)
        {
            ROM rom = new ROM();
            bool ok;
            string displayName = file.Name ?? "rom.gba";
            await using (var stream = await file.OpenReadAsync())
            {
                var result = await rom.LoadFromStreamAsync(stream, displayName);
                ok = result.ok;
            }
            if (!ok) { _currentRomStorageFile = null; return false; }
            _currentRomStorageFile = file;
            return FinishLoadedRom(rom, displayName);
        }

        /// <summary>
        /// Open a decomp project folder, resolve its built ROM, and load it as a
        /// read-only preview with the "build preview" badge shown (#1129 slice 1).
        /// </summary>
        private async void OpenDecompProject_Click(object? sender, RoutedEventArgs e)
        {
            // #1639: a decomp project is a directory TREE the app walks by path,
            // so it needs a real local folder. OpenProjectFolder returns null on
            // Android SAF (no local path) → show a clear message instead of
            // silently doing nothing.
            var dir = await FileDialogHelper.OpenProjectFolder(this);
            if (string.IsNullOrEmpty(dir))
            {
                if (OperatingSystem.IsAndroid())
                    await MessageBoxWindow.Show(this, R._("Opening a decomp project reads a folder tree and requires desktop file-system access; it is not available on this device."), R._("Decomp Project"), MessageBoxMode.Ok);
                return;
            }

            var project = DecompProjectDetector.Detect(dir);
            if (project == null)
            {
                await MessageBoxWindow.Show(this, R._("Not a decomp project directory."), R._("Decomp Project"), MessageBoxMode.Ok);
                return;
            }

            var resolved = DecompProjectDetector.ResolveBuiltRom(dir, project);
            if (resolved.Status == DecompResolveStatus.NotBuilt)
            {
                await MessageBoxWindow.Show(this, R._("Project found but no built ROM — run the build first, then reload."), R._("Decomp Project"), MessageBoxMode.Ok);
                return;
            }
            if (resolved.Status != DecompResolveStatus.Ok)
            {
                await MessageBoxWindow.Show(this, R._("Not a decomp project directory."), R._("Decomp Project"), MessageBoxMode.Ok);
                return;
            }

            project.BuiltRomPath = resolved.Path;
            CoreState.DecompProject = project;

            bool ok = LoadRomFile(resolved.Path);
            if (!ok)
            {
                CoreState.DecompProject = null;
                UpdateDecompBadge();
                await MessageBoxWindow.Show(this, R._("Failed to load ROM:") + $" {resolved.Path}", R._("Error"), MessageBoxMode.Ok);
                return;
            }
            UpdateDecompBadge();
        }

        /// <summary>Refresh the decomp-mode badge from CoreState after a load.</summary>
        private void UpdateDecompBadge()
        {
            _vm.RefreshDecompMode();
        }

        /// <summary>Run the project build without reloading the ROM (#1134).</summary>
        private async void DecompBuild_Click(object? sender, RoutedEventArgs e)
        {
            await RunDecompBuild(reload: false);
        }

        /// <summary>Run the project build and reload the built ROM (#1134).</summary>
        private async void DecompBuildReload_Click(object? sender, RoutedEventArgs e)
        {
            await RunDecompBuild(reload: true);
        }

        private async System.Threading.Tasks.Task RunDecompBuild(bool reload)
        {
            var project = CoreState.DecompProject;
            if (project == null) return;

            if (!project.IsBuildEnabled)
            {
                await MessageBoxWindow.Show(this,
                    R._("Project has not opted into FEBuilder-managed builds. Add a build section to febuilder.project.json."),
                    R._("Decomp Build"), MessageBoxMode.Ok);
                return;
            }

            string cmdLine = DecompBuildCore.GetEffectiveCommandLine(project);
            string confirmMsg = $"{R._("Run build command?")}\n\n{cmdLine}\n\n{R._("Working directory:")} {project.ProjectRoot}";
            var confirm = await MessageBoxWindow.Show(this, confirmMsg, R._("Decomp Build"), MessageBoxMode.YesNo);
            if (confirm != MessageBoxResult.Yes) return;

            _vm.DecompBuildOutput = R._("Building...");

            DecompBuildResult res = await System.Threading.Tasks.Task.Run(
                () => DecompBuildCore.Build(project, ProcessRunnerCore.DefaultTimeoutMs));

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(res.Run.Stdout))
            {
                sb.AppendLine("--- stdout ---");
                sb.Append(res.Run.Stdout);
            }
            if (!string.IsNullOrEmpty(res.Run.Stderr))
            {
                sb.AppendLine("--- stderr ---");
                sb.Append(res.Run.Stderr);
            }
            if (res.Run.Started)
                sb.AppendLine($"Exit: {res.Run.ExitCode}");
            sb.AppendLine(res.Message);
            _vm.DecompBuildOutput = sb.ToString();

            if (!res.Success)
            {
                _vm.RefreshDecompMode();
                return;
            }

            if (reload)
            {
                var status = DecompBuildCore.ReloadBuiltRom(
                    project,
                    (p, fv) => LoadRomFile(p, fv));

                if (status != DecompResolveStatus.Ok)
                {
                    _vm.DecompBuildOutput += "\n" + R._("Failed to reload built ROM after build.");
                }
                else
                {
                    // Re-init for symbol re-parse (Avalonia path wires AsmMapCache in LoadRomFile)
                }
            }

            UpdateDecompBadge();
        }

        private async void SaveRom_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;
            // Amendment 5: decomp preview ROMs are read-only — block save.
            if (CoreState.IsDecompMode)
            {
                await MessageBoxWindow.Show(this, R._("This is a source-backed decomp project. The built ROM is a preview and cannot be saved over. Edit the source and rebuild instead."), R._("Decomp Project"), MessageBoxMode.Ok);
                return;
            }
            if (_currentRomStorageFile != null && string.IsNullOrEmpty(_currentRomStorageFile.TryGetLocalPath()))
            {
                // Android SAF-backed ROM — write back through the retained handle.
                await using var stream = await _currentRomStorageFile.OpenWriteAsync();
                await CoreState.ROM.SaveToStreamAsync(stream);
            }
            else
            {
                CoreState.ROM.Save(CoreState.ROM.Filename, false);
            }
            _vm.HasUnsavedChanges = false;
            AutoSaveService.Instance.MarkSaved();
            CoreState.Services.ShowInfo(R._("ROM saved."));
        }

        private async void SaveAsRom_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;
            // Amendment 5: block Save As for decomp preview ROMs too.
            if (CoreState.IsDecompMode)
            {
                await MessageBoxWindow.Show(this, R._("This is a source-backed decomp project. The built ROM is a preview and cannot be saved over. Edit the source and rebuild instead."), R._("Decomp Project"), MessageBoxMode.Ok);
                return;
            }

            string suggestedName = Path.GetFileName(CoreState.ROM.Filename ?? "rom.gba");
            var file = await FileDialogHelper.SaveRomFilePick(this, suggestedName);
            if (file == null) return;

            string? localPath = file.TryGetLocalPath();
            string displayName;
            if (!string.IsNullOrEmpty(localPath))
            {
                _currentRomStorageFile = null;
                CoreState.ROM.Save(localPath, false);
                CoreState.ROM.Filename = localPath;  // Keep ROM.Filename in sync after Save As
                displayName = localPath;
            }
            else
            {
                // Android SAF — write via stream + retain the new handle (#1124).
                await using (var stream = await file.OpenWriteAsync())
                {
                    await CoreState.ROM.SaveToStreamAsync(stream);
                }
                _currentRomStorageFile = file;
                displayName = file.Name ?? "rom.gba";
                CoreState.ROM.Filename = displayName;
            }
            _vm.RomFilename = Path.GetFileName(displayName);
            _vm.HasUnsavedChanges = false;
            AutoSaveService.Instance.UpdateRomFilename(displayName);
            AutoSaveService.Instance.MarkSaved();
            CoreState.Services.ShowInfo(R._("ROM saved as:") + $" {Path.GetFileName(displayName)}");
        }

        private void OpenLastRom_Click(object? sender, RoutedEventArgs e)
        {
            string lastPath = CoreState.Config?.at("Last_Rom_Filename", "") ?? "";
            if (string.IsNullOrEmpty(lastPath) || !File.Exists(lastPath))
            {
                // No recent ROM — leave a currently-open decomp preview (and its
                // save guard) intact rather than dropping decomp mode (#1129).
                _ = MessageBoxWindow.Show(this, R._("No recent ROM found."), R._("Open Last ROM"), MessageBoxMode.Ok);
                return;
            }

            // Opening a plain ROM clears any active decomp project (#1129). Cleared
            // only after we know a real last ROM exists to load.
            CoreState.DecompProject = null;

            bool ok = LoadRomFile(lastPath);
            if (!ok)
            {
                _ = MessageBoxWindow.Show(this, R._("Failed to load ROM:") + $" {lastPath}", R._("Error"), MessageBoxMode.Ok);
            }
            UpdateDecompBadge();
        }

        private void Undo_Click(object? sender, RoutedEventArgs e)
        {
            CoreState.Undo?.RunUndo();
            _vm.HasUnsavedChanges = CoreState.Undo?.IsModified ?? false;
        }

        private void Refresh_Click(object? sender, RoutedEventArgs e)
        {
            Undo.OnAllFormsInvalidated?.Invoke();
            SetStatusText(R._("View refreshed."));
        }

        private void Exit_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        // ===== Dark Mode Toggle =====

        private void ToggleDarkMode_Click(object? sender, RoutedEventArgs e)
        {
            if (Application.Current is App app)
            {
                app.ToggleTheme();
                if (DarkModeMenuItem != null)
                    DarkModeMenuItem.Header = app.IsDarkMode ? R._("Switch to _Light Mode") : R._("Toggle _Dark Mode");
            }
        }

        // ===== Easy Mode Toggle =====

        private bool _isEasyMode;

        private void ToggleEasyMode_Click(object? sender, RoutedEventArgs e)
        {
            _isEasyMode = !_isEasyMode;
            NormalModeContent.IsVisible = !_isEasyMode;
            EasyModePanelControl.IsVisible = _isEasyMode;

            // Update the toggle button state
            if (EasyModeToggle != null)
                EasyModeToggle.IsChecked = _isEasyMode;

            // Update menu item text
            if (EasyModeMenuItem != null)
                EasyModeMenuItem.Header = _isEasyMode ? R._("Switch to _Normal Mode") : R._("Toggle _Easy Mode");
        }

        /// <summary>
        /// Called from EasyModePanel to run lint without exposing the full handler.
        /// </summary>
        internal void RunLintFromEasyMode()
        {
            Lint_Click(this, new RoutedEventArgs());
        }

        // ===== Search / Filter =====

        private void FilterTextBox_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
        {
            ApplyFilter(FilterTextBox.Text ?? "");
        }

        private void FilterTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyFilter(FilterTextBox.Text ?? "");
                e.Handled = true;
            }
        }

        private void ClearFilter_Click(object? sender, RoutedEventArgs e)
        {
            FilterTextBox.Text = "";
        }

        private void CollapseAll_Click(object? sender, RoutedEventArgs e)
        {
            SetAllExpandersExpanded(EditorPanel, false);
        }

        private void ExpandAll_Click(object? sender, RoutedEventArgs e)
        {
            SetAllExpandersExpanded(EditorPanel, true);
        }

        /// <summary>
        /// Set IsExpanded on all Expander children of the editor panel.
        /// </summary>
        static void SetAllExpandersExpanded(StackPanel panel, bool expanded)
        {
            foreach (var child in panel.Children)
            {
                if (child is Expander exp)
                    exp.IsExpanded = expanded;
            }
        }

        /// <summary>
        /// Show/hide buttons and section expanders based on the filter text.
        /// Case-insensitive substring match on button Content.
        /// Also matches against the Expander header (section name).
        /// </summary>
        void ApplyFilter(string filter)
        {
            bool hasFilter = !string.IsNullOrWhiteSpace(filter);
            filter = filter.Trim();
            int matchCount = 0;

            foreach (var child in EditorPanel.Children)
            {
                if (child is not Expander exp) continue;
                if (exp.Content is not WrapPanel wp) continue;

                string sectionName = exp.Header?.ToString() ?? "";
                bool sectionMatch = hasFilter && sectionName.Contains(filter, StringComparison.OrdinalIgnoreCase);

                bool anyButtonVisible = false;
                foreach (var item in wp.Children)
                {
                    if (item is not Button btn) continue;
                    string content = btn.Content?.ToString() ?? "";

                    // Respect version-hidden buttons (Tag == false means version-hidden)
                    if (IsButtonVersionHidden(btn.Tag))
                    {
                        btn.IsVisible = false;
                        continue;
                    }

                    if (hasFilter)
                    {
                        bool match = sectionMatch || content.Contains(filter, StringComparison.OrdinalIgnoreCase);
                        btn.IsVisible = match;
                        if (match) { matchCount++; anyButtonVisible = true; }
                    }
                    else
                    {
                        btn.IsVisible = true;
                        anyButtonVisible = true;
                    }
                }

                // Hide entire expander if no buttons are visible
                exp.IsVisible = anyButtonVisible;
                // Auto-expand sections with matches when filtering
                if (hasFilter && anyButtonVisible)
                    exp.IsExpanded = true;
            }

            FilterMatchLabel.IsVisible = hasFilter;
            if (hasFilter)
                FilterMatchLabel.Text = $"{matchCount} " + R._("editor(s) matching") + $" \"{filter}\"";
        }

        // ===== Auto-Save =====

        void TryStartAutoSave(string romFilename)
        {
            var (enabled, interval) = AutoSaveService.ReadConfig();
            if (enabled && !string.IsNullOrEmpty(romFilename))
                AutoSaveService.Instance.Start(interval, romFilename);
            else
                AutoSaveService.Instance.Stop();
        }

        // ===== Closing Dirty Check =====

        private async void MainWindow_Closing(object? sender, global::Avalonia.Controls.WindowClosingEventArgs e)
        {
            // In headless/screenshot mode, allow close without prompting
            if (App.SmokeTestMode) { AutoSaveService.Instance.Stop(); return; }

            // Check if ROM has unsaved changes via undo buffer
            var undo = CoreState.Undo;
            if (undo == null || CoreState.ROM == null) { AutoSaveService.Instance.Stop(); return; }

            bool hasUnsavedChanges = undo.IsModified;
            if (!hasUnsavedChanges) { AutoSaveService.Instance.Stop(); return; }

            // Cancel close, show prompt, then re-close if confirmed
            e.Cancel = true;
            var result = await MessageBoxWindow.Show(this,
                R._("You have unsaved changes. Close without saving?"),
                R._("Unsaved Changes"),
                MessageBoxMode.YesNo);

            if (result == MessageBoxResult.Yes)
            {
                AutoSaveService.Instance.Stop();
                // Detach handler to prevent re-entry, then close
                Closing -= MainWindow_Closing;
                CoreState.LanguageChanged -= OnLanguageChanged;
                Close();
            }
        }

        // ===== Editor Open Handlers =====

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
            if (CoreState.ROM?.RomInfo?.version == 6)
                WindowManager.Instance.Open<ClassFE6View>();
            else
                WindowManager.Instance.Open<ClassEditorView>();
        }

        private void OpenMapSettings_Click(object? sender, RoutedEventArgs e)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                WindowManager.Instance.Open<MapSettingView>();
                return;
            }

            int ver = rom.RomInfo.version;
            if (ver == 6)
            {
                WindowManager.Instance.Open<MapSettingFE6View>();
            }
            else if (ver == 7)
            {
                // FE7U has 152-byte struct, FE7JP has 148-byte struct
                if (MapSettingCore.IsFE7ULayout(rom.RomInfo.map_setting_datasize))
                    WindowManager.Instance.Open<MapSettingFE7UView>();
                else
                    WindowManager.Instance.Open<MapSettingFE7View>();
            }
            else
            {
                // FE8 and others use the generic MapSettingView
                WindowManager.Instance.Open<MapSettingView>();
            }
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
            var pds = PatchDetectionService.Instance;
            if (pds.HasSkillSystem)
                WindowManager.Instance.Open<ItemStatBonusesSkillSystemsView>();
            else if (pds.VennouWeaponLock)
                WindowManager.Instance.Open<ItemStatBonusesVennoView>();
            else
                WindowManager.Instance.Open<ItemStatBonusesViewerView>();
        }

        private void OpenItemEffectiveness_Click(object? sender, RoutedEventArgs e)
        {
            if (PatchDetectionService.Instance.SkillSystemsClassTypeRework)
                WindowManager.Instance.Open<ItemEffectivenessSkillSystemsReworkView>();
            else
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
                _ = MessageBoxWindow.Show(this, R._("Lint: No errors found."), R._("Lint Results"), MessageBoxMode.Ok);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{errors.Count} " + R._("issue(s) found:"));
                foreach (var err in errors)
                {
                    string severity = err.Severity == FELintCore.ErrorType.ERROR ? "ERROR" : "WARNING";
                    sb.AppendLine($"[{severity}] 0x{err.Addr:X08}: {err.ErrorMessage}");
                }
                _ = MessageBoxWindow.Show(this, sb.ToString(), R._("Lint Results"), MessageBoxMode.Ok);
            }
        }

        private async void DataExport_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM == null) return;
            var dialog = new DataExportView();
            await dialog.ShowDialog(this);
        }

        private async void Options_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new OptionsView();
            await dialog.ShowDialog(this);
            // Re-read auto-save config after options dialog closes
            string currentTarget = AutoSaveService.Instance.CurrentRomFilename ?? CoreState.ROM?.Filename;
            TryStartAutoSave(currentTarget);
        }

        private void Version_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<VersionView>();
        }

        private async void About_Click(object? sender, RoutedEventArgs e)
        {
            await MessageBoxWindow.Show(this,
                R._("FEBuilderGBA") + "\n" + R._("Avalonia Cross-Platform Preview") + "\n"
                    + R._("Version") + ": " + U.getAppVersion() + "\n"
                    + R._("Copyright 2017- GPLv3"),
                R._("About"), MessageBoxMode.Ok);
        }

        // ===================== Image Editors =====================
        private void OpenImagePortrait_Click(object? sender, RoutedEventArgs e)
        {
            // #1411 — defense in depth: the generic 28-byte Portrait Editor must never
            // open on FE6 (16-byte table) because Write would silently corrupt the next
            // entry. The button is already hidden on FE6 (UpdateEditorVisibility), but if
            // this handler is ever reached on FE6 (e.g. via search-filter race), route to
            // the correct dedicated 16-byte editor. Routing lives in the shared
            // ResolvePortraitEditorViewType helper so production and tests agree.
            int ver = CoreState.ROM?.RomInfo?.version ?? 0;
            if (ResolvePortraitEditorViewType(ver) == typeof(ImagePortraitFE6View))
                WindowManager.Instance.Open<ImagePortraitFE6View>();
            else
                WindowManager.Instance.Open<ImagePortraitView>();
        }
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
        private void OpenProcsScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ProcsScriptCategorySelectView>();
        private void OpenEventScriptTemplate_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventScriptTemplateView>();

        // ===================== AI Script Editors =====================
        // PR #410: the main "AI Script" button opens the master/detail
        // AIScriptView (parity with WF MainFE8Form -> AIScriptForm). The
        // category-select dialog is reached internally from AIScriptView's
        // Script Change button, not from the main menu.
        private void OpenAIScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIScriptView>();
        // #1414: The AI ASM Call / AI Coordinate / AI Range / AI Tiles / AI Units sub-editors
        // are context-dependent and were removed from the main menu — exposing them standalone
        // let them self-initialize a heuristically-guessed write target (silent ROM corruption).
        // WinForms reaches these ONLY from the AIScript per-parameter dispatch, never standalone.
        private void OpenAIMapSetting_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIMapSettingView>();
        private void OpenAIPerformItem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIPerformItemView>();
        private void OpenAIPerformStaff_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIPerformStaffView>();
        private void OpenAIStealItem_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AIStealItemView>();
        private void OpenAITarget_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AITargetView>();
        private void OpenAOERANGE_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<AOERANGEView>();

        // ===================== Map Editors =====================
        private void OpenMapEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapEditorView>();
        private void OpenMapSettingFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingFE6View>();
        private void OpenMapSettingFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingFE7View>();
        private void OpenMapSettingFE7U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingFE7UView>();
        private void OpenMapSettingDifficulty_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingDifficultyView>();
        private void OpenMapStyleEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapStyleEditorView>();
        private void OpenMapTerrainBGLookup_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTerrainBGLookupTableView>();
        private void OpenMapTerrainFloorLookup_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapTerrainFloorLookupTableView>();
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
        private void ContentRepoSetup_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.OpenModal<ContentRepoSetupWizardView>(this);

        // ===================== Gap Closure: New Editors =====================

        // Status Screen (WU2)
        private void OpenStatusParam_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusParamView>();
        private void OpenStatusRMenu_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusRMenuView>();
        private void OpenStatusUnitsMenu_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusUnitsMenuView>();
        private void OpenStatusOptionOrder_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<StatusOptionOrderView>();

        // Skill Systems Extended (WU3)
        private void OpenSkillAssignmentUnitCSkillSys_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentUnitCSkillSysView>();
        private void OpenSkillAssignmentClassCSkillSys_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentClassCSkillSysView>();
        private void OpenSkillAssignmentUnitFE8N_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillAssignmentUnitFE8NView>();
        private void OpenSkillConfigFE8N_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigFE8NSkillView>();
        private void OpenSkillConfigFE8NVer2_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigFE8NVer2SkillView>();
        private void OpenSkillConfigFE8NVer3_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigFE8NVer3SkillView>();
        private void OpenSkillConfigFE8UCSkillSys09x_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SkillConfigFE8UCSkillSys09xView>();

        // Audio (WU4)
        private void OpenToolBGMMuteDialog_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ToolBGMMuteDialogView>();

        // Tools (WU6, WU8, WU9)
        private void OpenGraphicsTool_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<GraphicsToolView>();
        private void OpenPointerTool_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<PointerToolView>();
        private void OpenMoveToFreeSpace_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MoveToFreeSpaceView>();

        // Version-Specific (WU11)
        private void OpenItemFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemFE6View>();
        private void OpenMoveCostFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MoveCostFE6View>();
        private void OpenSupportUnitFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SupportUnitFE6View>();
        private void OpenSupportTalkFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SupportTalkFE6View>();
        private void OpenSupportTalkFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SupportTalkFE7View>();
        private void OpenUnitFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitFE7View>();
        private void OpenOPClassDemoFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassDemoFE7View>();
        private void OpenOPClassDemoFE7U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassDemoFE7UView>();
        private void OpenOPClassDemoFE8U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassDemoFE8UView>();
        private void OpenOPClassFontFE8U_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassFontFE8UView>();
        private void OpenOPClassAlphaName_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassAlphaNameView>();
        private void OpenOPClassAlphaNameFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OPClassAlphaNameFE6View>();
        private void OpenSomeClassList_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SomeClassListView>();
        private void OpenVennouWeaponLock_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<VennouWeaponLockView>();
        private void OpenUnitsShortText_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitsShortTextView>();

        // ===================== WU15: Event Templates =====================
        private void OpenEventTemplate1_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate1View>();
        private void OpenEventTemplate2_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate2View>();
        private void OpenEventTemplate3_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate3View>();
        private void OpenEventTemplate4_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate4View>();
        private void OpenEventTemplate5_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate5View>();
        private void OpenEventTemplate6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTemplate6View>();
        private void OpenEventFinalSerifFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventFinalSerifFE7View>();
        private void OpenEventMoveDataFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventMoveDataFE7View>();
        private void OpenEventTalkGroupFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventTalkGroupFE7View>();

        // ===================== WU16: Audio Sub-Forms =====================
        private void OpenSongTrackChangeTrack_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackChangeTrackView>();
        private void OpenSongTrackAllChangeTrack_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackAllChangeTrackView>();
        private void OpenSongTrackImportSelectInstrument_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackImportSelectInstrumentView>();

        // ===================== WU17: ED/Item Variants =====================
        private void OpenEDFE6_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EDFE6View>();
        private void OpenEDFE7_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EDFE7View>();
        private void OpenEDSensekiComment_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EDSensekiCommentView>();
        private void OpenItemStatBonusesSkillSystems_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemStatBonusesSkillSystemsView>();
        private void OpenItemStatBonusesVenno_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemStatBonusesVennoView>();
        private void OpenItemEffectivenessSkillSystemsRework_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemEffectivenessSkillSystemsReworkView>();
        private void OpenItemRandomChest_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemRandomChestView>();
        private void OpenMenuExtendSplitMenu_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MenuExtendSplitMenuView>();

        // ===================== WU19: Help & External Tools =====================
        private void OnlineManual_Click(object? sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/laqieer/FEBuilderGBA/wiki") { UseShellExecute = true }); }
            catch (Exception ex) { Log.ErrorF("MainWindow.OnlineManual_Click launch browser: {0}", ex.Message); }
        }

        private void Discussions_Click(object? sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/laqieer/FEBuilderGBA/discussions") { UseShellExecute = true }); }
            catch (Exception ex) { Log.ErrorF("MainWindow.Discussions_Click launch browser: {0}", ex.Message); }
        }

        private async void CheckUpdates_Click(object? sender, RoutedEventArgs e)
        {
            UpdateCheckCore.UpdateCheckResult result = await Task.Run(() => UpdateCheckCore.CheckLatest());
            await Dispatcher.UIThread.InvokeAsync(async () => await ShowUpdateCheckResultAsync(result, manual: true));
        }

        void StartAutoUpdateCheckIfDue()
        {
            try
            {
                Config? cfg = CoreState.Config;
                string interval = cfg?.at("func_auto_update", "3") ?? "3";
                string last = cfg?.at("LastUpdateCheck", "0") ?? "0";
                string today = DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                if (!UpdateCheckCore.ShouldAutoCheck(interval, last, today))
                    return;

                _ = Task.Run(() =>
                {
                    UpdateCheckCore.UpdateCheckResult result = UpdateCheckCore.CheckLatest();
                    // Marshal back to the UI thread: the config write (MarkAutoUpdateChecked ->
                    // CoreState.Config.Save) and any dialog must NOT run on the background thread.
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            // Only consume the interval on a SUCCESSFUL check — a failed (offline/
                            // rate-limited) check retries on the next launch instead of being suppressed
                            // for a full interval, matching WinForms IsAutoUpdateTime semantics. (#1804)
                            if (!result.CheckSucceeded)
                                return;
                            MarkAutoUpdateChecked(today);
                            if (!result.IsUpdateAvailable)
                                return;
                            await ShowUpdateCheckResultAsync(result, manual: false);
                        }
                        catch (Exception ex)
                        {
                            // async-void fire-and-forget: swallow+log so an exception (e.g. the window
                            // closing during startup) can never become an unobserved crash.
                            Log.ErrorF("MainWindow auto-update dialog: {0}", ex.Message);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Log.ErrorF("MainWindow.StartAutoUpdateCheckIfDue: {0}", ex.Message);
            }
        }

        static void MarkAutoUpdateChecked(string todayYyyyMmdd)
        {
            try
            {
                Config? cfg = CoreState.Config;
                if (cfg == null)
                    return;
                cfg["LastUpdateCheck"] = todayYyyyMmdd;
                cfg.Save();
            }
            catch (Exception ex)
            {
                Log.ErrorF("MainWindow.MarkAutoUpdateChecked: {0}", ex.Message);
            }
        }

        async Task ShowUpdateCheckResultAsync(UpdateCheckCore.UpdateCheckResult result, bool manual)
        {
            if (!result.CheckSucceeded)
            {
                if (manual)
                    await MessageBoxWindow.Show(this, R._("Could not check for updates (offline or GitHub unavailable)."), R._("FEBuilderGBA"), MessageBoxMode.Ok);
                return;
            }

            if (!result.IsUpdateAvailable)
            {
                if (manual)
                    await MessageBoxWindow.Show(this,
                        string.Format(R._("You are running the latest version (current {0})."), result.CurrentVersion),
                        R._("FEBuilderGBA"), MessageBoxMode.Ok);
                return;
            }

            var answer = await MessageBoxWindow.Show(this,
                string.Format(R._("A new version is available: {0} (you have {1}). Open the releases page to download and install it?"),
                    result.LatestVersion, result.CurrentVersion),
                R._("FEBuilderGBA"), MessageBoxMode.YesNo);
            if (answer == MessageBoxResult.Yes)
                OpenUrlInBrowser(result.ReleasePageUrl);
        }

        static void OpenUrlInBrowser(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.ErrorF("MainWindow.OpenUrlInBrowser: {0}", ex.Message); }
        }

        private async void ReportBug_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // #1747: target the editor window the user is actually working in
                // (the most-recently-activated editor). Falls back to this main window
                // when no editor is open — matching the previous behavior in that case.
                Window target = WindowManager.Instance.ActiveEditorWindow ?? this;

                // 1. Capture screenshot of the target window to temp PNG
                string pngPath = Path.Combine(Path.GetTempPath(), $"febuilder-bugshot-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.png");
                bool screenshotSaved = false;
                try
                {
                    var win = TopLevel.GetTopLevel(target);
                    if (win != null)
                    {
                        var bounds = win.Bounds;
                        // Guard against non-finite/zero bounds (would yield an invalid 0x0 bitmap).
                        int w = Math.Max((int)(double.IsFinite(bounds.Width) ? bounds.Width : 0), 1);
                        int h = Math.Max((int)(double.IsFinite(bounds.Height) ? bounds.Height : 0), 1);
                        if (w > 1 && h > 1)
                        {
                            using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
                            rtb.Render(win);
                            rtb.Save(pngPath);
                            // In headless/locked environments Save can be a no-op; only treat it as
                            // saved if a non-empty file actually landed on disk.
                            screenshotSaved = File.Exists(pngPath) && new System.IO.FileInfo(pngPath).Length > 0;
                        }
                    }
                }
                catch (Exception ex) { Log.ErrorF("MainWindow.ReportBug_Click screenshot: {0}", ex.Message); }

                // 2. Build prefill fields
                string? appVersion = U.getAppVersion();
                string? romTag = CoreState.ROM?.RomInfo?.VersionToFilename;
                string? editorTitle = target.Title ?? "Main Window";
                string appLabel = "Avalonia GUI (cross-platform)";
                var fields = BugReportCore.BuildPrefill(appVersion, romTag, editorTitle, appLabel);
                var url = BugReportCore.BuildIssueUrl(BugReportCore.Owner, BugReportCore.Repo, BugReportCore.GuiBugTemplate, fields);

                // 3. Reveal screenshot in file browser (desktop only, and only if we saved one)
                if (screenshotSaved && !OperatingSystem.IsAndroid())
                {
                    try
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            // Match the codebase-wide explorer-select convention (LogViewerView): a single
                            // quoted "/select,\"<path>\"" argument string, which Explorer parses reliably.
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{pngPath}\"") { UseShellExecute = true });
                        }
                        else if (OperatingSystem.IsMacOS())
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open") { UseShellExecute = false, ArgumentList = { "-R", pngPath } });
                        }
                        else
                        {
                            var dir = Path.GetDirectoryName(pngPath);
                            if (!string.IsNullOrEmpty(dir))
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open") { UseShellExecute = false, ArgumentList = { dir } });
                        }
                    }
                    catch (Exception ex) { Log.ErrorF("MainWindow.ReportBug_Click reveal: {0}", ex.Message); }

                    // 4. Copy screenshot path to clipboard
                    try
                    {
                        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(pngPath) ?? System.Threading.Tasks.Task.CompletedTask);
                    }
                    catch (Exception ex) { Log.ErrorF("MainWindow.ReportBug_Click clipboard: {0}", ex.Message); }
                }

                // 5. Open pre-filled issue URL in browser
                bool browserOpened = false;
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    browserOpened = true;
                }
                catch (Exception ex) { Log.ErrorF("MainWindow.ReportBug_Click open browser: {0}", ex.Message); }

                // 6. Show info dialog (wording reflects what actually happened — every step is best-effort)
                string head = browserOpened
                    ? R._("Opened a pre-filled bug report in your browser.")
                    : R._("Couldn't open your browser automatically. Please file the bug at this URL:") + "\n" + url;
                string shotNote = screenshotSaved
                    ? R._("A screenshot of this window was saved — drag it into the issue's Screenshot box:") + "\n" + pngPath
                    : R._("Couldn't capture a screenshot automatically — please attach one manually in the issue's Screenshot box.");
                await MessageBoxWindow.Show(this,
                    head + "\n\n" + shotNote + "\n\n" + R._("Never attach your ROM (.gba)."),
                    R._("Report a Bug"), MessageBoxMode.Ok);
            }
            catch (Exception ex)
            {
                Log.ErrorF("MainWindow.ReportBug_Click: {0}", ex.Message);
            }
        }

        private async void RunEmulator_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("emulator", "emulator", "Emulator_Path");
        }

        private async void RunEmulator2_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("emulator2", "emulator 2");
        }

        private async void RunBinaryEditor_Click(object? sender, RoutedEventArgs e)
        {
            string path = OptionsViewModel.GetToolPath(CoreState.Config, "binary_editor", "BinaryEditor_Path");
            if (string.IsNullOrEmpty(path))
            {
                WindowManager.Instance.Open<HexEditorView>();
                return;
            }

            await RunExternalTool("binary_editor", "binary editor", "BinaryEditor_Path");
        }

        private async void RunSappy_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("sappy", "Sappy", "Sappy_Path");
        }

        private async void RunProgram1_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("program1", "Program 1", "CustomTool_Path");
        }

        private async void RunProgram2_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("program2", "Program 2");
        }

        private async void RunProgram3_Click(object? sender, RoutedEventArgs e)
        {
            await RunExternalTool("program3", "Program 3");
        }

        private async System.Threading.Tasks.Task RunExternalTool(string configKey, string toolName, params string[] fallbackKeys)
        {
            string path = OptionsViewModel.GetToolPath(CoreState.Config, configKey, fallbackKeys);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                await MessageBoxWindow.Show(this,
                    R._("No") + $" {toolName} " + R._("configured. Set the path in Options first."),
                    R._("External Tool"), MessageBoxMode.Ok);
                return;
            }
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
                if (CoreState.ROM != null && !string.IsNullOrEmpty(CoreState.ROM.Filename))
                    psi.Arguments = $"\"{CoreState.ROM.Filename}\"";
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                await MessageBoxWindow.Show(this, R._("Failed to run") + $" {toolName}: {ex.Message}", R._("Error"), MessageBoxMode.Ok);
            }
        }
        // ==================================================================
        // Editor visibility filtering — hide buttons that don't match the
        // loaded ROM version so the UI is not cluttered with irrelevant editors.
        // ==================================================================

        /// <summary>
        /// Orchestrator: reset all visibility, then hide mismatched buttons/sections.
        /// Called from LoadRomFile() after EditorPanel becomes visible.
        /// </summary>
        void UpdateEditorVisibility()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            int ver = rom.RomInfo.version;
            bool isMultibyte = rom.RomInfo.is_multibyte;

            ResetAllButtonVisibility(EditorPanel);

            HideVersionMismatchedButtons(EditorPanel, ver, isMultibyte);

            // FE8-only whole sections (Expanders)
            bool isFE8 = ver == 8;
            MonstersExpander.IsVisible = isFE8;
            SummonsExpander.IsVisible = isFE8;
            SkillsExpander.IsVisible = isFE8;
            SkillsExtExpander.IsVisible = isFE8;

            // Senseki Comment is FE7-only (no version suffix in its Content)
            SensekiCommentButton.IsVisible = ver == 7;

            // #1411 — the generic "Portrait Editor" (ImagePortraitView, 28-byte stride)
            // must be HIDDEN on FE6, whose portrait table is 16 bytes/entry. WinForms
            // MainFE6Form opens only the dedicated 16-byte ImagePortraitFE6Form and never
            // the 28-byte editor; opening the generic one on FE6 silently corrupts the
            // next entry on Write. Its Content carries no version suffix, so we gate it by
            // Name here (like SensekiCommentButton above). Setting Tag = false also keeps
            // it durably hidden across the editor search filter (ApplyFilter only preserves
            // buttons whose Tag is bool false as version-hidden).
            if (PortraitEditorButton != null)
            {
                PortraitEditorButton.IsVisible = ShouldShowGenericPortraitEditor(ver);
                PortraitEditorButton.Tag = GenericPortraitEditorTag(ver);
            }

            AutoHideEmptySections(EditorPanel);
        }

        /// <summary>
        /// Reset all buttons and section expanders inside the panel to visible.
        /// </summary>
        static void ResetAllButtonVisibility(StackPanel panel)
        {
            foreach (var child in panel.Children)
            {
                child.IsVisible = true;
                if (child is Expander exp && exp.Content is WrapPanel wp)
                {
                    foreach (var btn in wp.Children)
                    {
                        btn.IsVisible = true;
                        if (btn is Button b) b.Tag = null; // clear version tag
                    }
                }
            }
        }

        /// <summary>
        /// Walk all Expander > WrapPanel inside the editor panel, check each Button's Content
        /// text for a version tag, and hide buttons that don't match.
        /// Also stores version visibility in Tag property so the search filter can respect it.
        /// </summary>
        static void HideVersionMismatchedButtons(StackPanel panel, int ver, bool isMultibyte)
        {
            foreach (var child in panel.Children)
            {
                if (child is not Expander exp) continue;
                if (exp.Content is not WrapPanel wp) continue;
                foreach (var item in wp.Children)
                {
                    if (item is not Button btn) continue;
                    // #1798: gate by the STABLE, version-encoding button Name (never
                    // localized) — NOT the translatable Content. In Japanese,
                    // RefreshEditorButtons sets Content to full-width parens (e.g.
                    // "ユニット（FE6）"), so the ASCII "(FE6)" check in GetVersionVisibility
                    // silently failed and left FE6/FE7/FE7U/FE8U buttons visible on FE8J.
                    // Fall back to the Content check only when the Name carries no tag.
                    bool? visible = GetVersionVisibilityByName(btn.Name, ver, isMultibyte)
                        ?? GetVersionVisibility(btn.Content?.ToString() ?? "", ver, isMultibyte);
                    if (visible.HasValue)
                    {
                        btn.IsVisible = visible.Value;
                        if (!visible.Value)
                            btn.Tag = false; // mark as version-hidden for filter
                    }
                }
            }
        }

        /// <summary>
        /// Pure logic: returns true if the button should be shown, false if hidden,
        /// null if no version tag was found (always show).
        /// Order matters: check region-specific tags (FE7U, FE8U) before generic (FE7, FE8).
        /// </summary>
        internal static bool? GetVersionVisibility(string content, int ver, bool isMultibyte)
        {
            if (content.Contains("(FE7U)"))
                return ver == 7 && !isMultibyte;
            if (content.Contains("(FE8U)"))
                return ver == 8 && !isMultibyte;
            if (content.Contains("(FE6)"))
                return ver == 6;
            if (content.Contains("(FE7)"))
                return ver == 7;
            if (content.Contains("(FE8)"))
                return ver == 8;
            return null; // no tag — always show
        }

        /// <summary>
        /// #1798: translation-independent version gating. Version-specific editor buttons
        /// are named "&lt;Editor&gt;FE&lt;n&gt;[U]Button" (e.g. UnitFE6Button,
        /// MapSettingsFE7UButton, OPDemoFE8UButton) — a stable identifier that is never
        /// localized, unlike Content (which becomes full-width "（FE6）" in Japanese and
        /// defeats GetVersionVisibility's ASCII "(FE6)" check). Matches the version token
        /// immediately before the "Button" suffix so a generic name can't be misread.
        /// Returns null when the name has no version suffix (caller falls back to Content).
        /// Order matters: region-specific (FE7U, FE8U) before generic (FE7, FE8).
        /// </summary>
        internal static bool? GetVersionVisibilityByName(string? name, int ver, bool isMultibyte)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (name.EndsWith("FE7UButton", StringComparison.Ordinal))
                return ver == 7 && !isMultibyte;
            if (name.EndsWith("FE8UButton", StringComparison.Ordinal))
                return ver == 8 && !isMultibyte;
            if (name.EndsWith("FE6Button", StringComparison.Ordinal))
                return ver == 6;
            if (name.EndsWith("FE7Button", StringComparison.Ordinal))
                return ver == 7;
            if (name.EndsWith("FE8Button", StringComparison.Ordinal))
                return ver == 8;
            return null; // no version suffix in the name
        }

        // ===================== #1411 Portrait Editor version gating =====================
        // The generic "Portrait Editor" button has no version suffix in its Content, so
        // GetVersionVisibility cannot express its rule ("hide on FE6 only"). These pure,
        // testable predicates are the SINGLE SOURCE OF TRUTH: UpdateEditorVisibility and
        // OpenImagePortrait_Click below call them, and the unit tests call the SAME
        // methods — so a regression in the production assignment is caught (Copilot
        // PR-review #2).

        /// <summary>
        /// True when the generic 28-byte Portrait Editor should be shown. It is hidden
        /// ONLY on FE6 (16-byte portrait table), whose dedicated editor is Portrait (FE6).
        /// </summary>
        internal static bool ShouldShowGenericPortraitEditor(int ver) => ver != 6;

        /// <summary>
        /// The Button.Tag value for the generic Portrait Editor button. When the button
        /// is version-hidden the Tag must be boxed bool false so the editor search filter
        /// (ApplyFilter) keeps it hidden across typing/clearing (it only preserves buttons
        /// whose Tag is bool b and !b); otherwise null.
        /// </summary>
        internal static object? GenericPortraitEditorTag(int ver)
            => ShouldShowGenericPortraitEditor(ver) ? null : (object)false;

        /// <summary>
        /// Which portrait editor view OpenImagePortrait_Click opens for a given version:
        /// FE6 to the dedicated 16-byte ImagePortraitFE6View; any other version to the
        /// generic 28-byte ImagePortraitView. Returns the view Type so both the handler
        /// and the tests agree on the routing without constructing a window.
        /// </summary>
        internal static System.Type ResolvePortraitEditorViewType(int ver)
            => ver == 6 ? typeof(ImagePortraitFE6View) : typeof(ImagePortraitView);

        /// <summary>
        /// The exact rule ApplyFilter uses to decide a button is version-hidden and must
        /// stay hidden through search/clear: its Tag is a boxed bool false. Shared so a
        /// test can prove the full chain (GenericPortraitEditorTag on FE6 => this returns
        /// true => filter keeps the button hidden), guarding the #1411 P1 regression.
        /// </summary>
        internal static bool IsButtonVersionHidden(object? tag) => tag is bool b && !b;

        /// <summary>
        /// After filtering, if all buttons inside an Expander are hidden,
        /// also hide the entire Expander.
        /// </summary>
        static void AutoHideEmptySections(StackPanel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is not Expander exp) continue;
                if (exp.Content is not WrapPanel wp) continue;
                bool anyVisible = false;
                foreach (var btn in wp.Children)
                {
                    if (btn.IsVisible) { anyVisible = true; break; }
                }
                if (!anyVisible)
                    exp.IsVisible = false;
            }
        }
    }
}



