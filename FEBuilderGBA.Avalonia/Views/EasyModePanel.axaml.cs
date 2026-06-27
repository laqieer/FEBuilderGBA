using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EasyModePanel : TranslatedUserControl
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
                (CategoryText, "text editor dialogue export import tsv"),
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
        private void EasyOpenOAMSpriteViewer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<OAMSpriteViewerView>();

        // Music
        private void EasyOpenSongTable_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTableView>();
        private void EasyOpenSongTrack_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SongTrackView>();
        private void EasyOpenSoundRoom_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<SoundRoomViewerView>();

        // Text
        private void EasyOpenTextViewer_Click(object? sender, RoutedEventArgs e) => WindowManager.Instance.Open<TextViewerView>();

        private async void EasyExportText_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var tsvType = new global::Avalonia.Platform.Storage.FilePickerFileType(R._("TSV Files")) { Patterns = new[] { "*.tsv" } };
                var allType = new global::Avalonia.Platform.Storage.FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = R._("Export Texts"),
                    SuggestedFileName = "texts.tsv",
                    FileTypeChoices = new[] { tsvType, allType },
                });
                if (file == null) return;

                // #1639: ExportAllTexts writes by path, so route through the SAF
                // bridge (temp + write-back on Android). Capture the count in the
                // closure since WriteViaAsync only returns the written label.
                var vm = new TextViewerViewModel();
                int count = 0;
                string? written = await FileDialogHelper.WriteViaAsync(file, path => { count = vm.ExportAllTexts(path); });
                if (written == null) return;
                var ownerWindow = topLevel as Window ?? new Window();
                await MessageBoxWindow.Show(ownerWindow,
                    $"Exported {count} text entries to TSV.", R._("Export Complete"), MessageBoxMode.Ok);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EasyExportText failed: {0}", ex.Message);
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window w)
                    await MessageBoxWindow.Show(w, $"Export failed: {ex.Message}", "Error", MessageBoxMode.Ok);
            }
        }

        private async void EasyImportText_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var ownerWindow = topLevel as Window ?? new Window();
                string? path = await FileDialogHelper.OpenFile(ownerWindow, "TSV Files", "*.tsv");
                if (path == null) return;

                var vm = new TextViewerViewModel();
                int count = vm.ImportAllTexts(path);
                if (count > 0)
                {
                    await MessageBoxWindow.Show(ownerWindow,
                        $"Imported {count} text entries.", R._("Import Complete"), MessageBoxMode.Ok);
                }
                else
                {
                    await MessageBoxWindow.Show(ownerWindow,
                        R._("No texts were imported. Check the file format."), R._("Import"), MessageBoxMode.Ok);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("EasyImportText failed: {0}", ex.Message);
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window w)
                    await MessageBoxWindow.Show(w, $"Import failed: {ex.Message}", "Error", MessageBoxMode.Ok);
            }
        }

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
