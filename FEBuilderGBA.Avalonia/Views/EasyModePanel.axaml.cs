using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EasyModePanel : UserControl
    {
        public EasyModePanel()
        {
            InitializeComponent();
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
