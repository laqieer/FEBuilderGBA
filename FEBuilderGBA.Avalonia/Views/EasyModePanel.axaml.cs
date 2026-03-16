using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EasyModePanel : UserControl
    {
        /// <summary>Category borders paired with their searchable keywords (category name + button labels).</summary>
        private List<(Border border, string keywords)>? _categories;

        public EasyModePanel()
        {
            InitializeComponent();
            SearchBox.TextChanged += SearchBox_TextChanged;
        }

        /// <summary>Lazily build the category list after the control is initialized.</summary>
        private List<(Border border, string keywords)> GetCategories()
        {
            if (_categories != null) return _categories;
            _categories = new List<(Border, string)>
            {
                (CategoryCharacters, "characters units classes support talk"),
                (CategoryItems, "items weapons weapon effect shops promotions"),
                (CategoryMaps, "maps map settings editor terrain names move costs"),
                (CategoryEvents, "events event scripts conditions units"),
                (CategoryGraphics, "graphics portraits battle animations cg viewer image"),
                (CategoryMusic, "music song table tracks sound room"),
                (CategoryText, "text editor dialogue"),
                (CategoryTools, "tools hex editor patch manager lint pointer"),
            };
            return _categories;
        }

        private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            ApplyFilter(SearchBox.Text);
        }

        /// <summary>
        /// Show/hide category sections based on the search query.
        /// Empty query shows all categories. Otherwise, only categories whose
        /// keywords contain the query substring (case-insensitive) are shown.
        /// </summary>
        internal void ApplyFilter(string? query)
        {
            var categories = GetCategories();
            bool showAll = string.IsNullOrWhiteSpace(query);
            var q = (query ?? "").Trim();

            foreach (var (border, keywords) in categories)
            {
                border.IsVisible = showAll || keywords.Contains(q, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Characters
        private void EasyOpenUnits_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<UnitEditorView>();
        private void EasyOpenClasses_Click(object? sender, RoutedEventArgs e)
        {
            if (CoreState.ROM?.RomInfo?.version == 6)
                WindowManager.Instance.Open<ClassFE6View>();
            else
                WindowManager.Instance.Open<ClassEditorView>();
        }
        private void EasyOpenSupportUnits_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SupportUnitEditorView>();
        private void EasyOpenSupportTalk_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SupportTalkView>();

        // Items
        private void EasyOpenItems_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemEditorView>();
        private void EasyOpenWeaponEffect_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemWeaponEffectViewerView>();
        private void EasyOpenShop_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemShopViewerView>();
        private void EasyOpenPromotion_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ItemPromotionViewerView>();

        // Maps
        private void EasyOpenMapSettings_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapSettingView>();
        private void EasyOpenMapEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MapEditorView>();
        private void EasyOpenTerrainNames_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<TerrainNameEditorView>();
        private void EasyOpenMoveCost_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<MoveCostEditorView>();

        // Events
        private void EasyOpenEventScript_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventScriptView>();
        private void EasyOpenEventCond_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventCondView>();
        private void EasyOpenEventUnit_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<EventUnitView>();

        // Graphics
        private void EasyOpenPortraits_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<PortraitViewerView>();
        private void EasyOpenBattleAnime_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageBattleAnimeView>();
        private void EasyOpenBigCG_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<BigCGViewerView>();
        private void EasyOpenImageViewer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<ImageViewerView>();

        // Music
        private void EasyOpenSongTable_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTableView>();
        private void EasyOpenSongTrack_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackView>();
        private void EasyOpenSoundRoom_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SoundRoomViewerView>();

        // Text
        private void EasyOpenTextViewer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<TextViewerView>();

        // Tools
        private void EasyOpenHexEditor_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<HexEditorView>();
        private void EasyOpenPatchManager_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<PatchManagerView>();
        private void EasyOpenLint_Click(object? sender, RoutedEventArgs e)
        {
            // Delegate to MainWindow's lint handler
            if (this.VisualRoot is MainWindow mw)
                mw.RunLintFromEasyMode();
        }
        private void EasyOpenPointerTool_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<PointerToolView>();
    }
}
