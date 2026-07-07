using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OAMSpriteViewerView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly OAMSpriteViewerViewModel _vm = new();
        bool _hasLoadedList;
        bool _suppressFrameEvents;

        public string ViewTitle => "OAM Sprite Viewer";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("OAM Sprite Viewer", 1100, 750, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public OAMSpriteViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;

            // Populate section combo with mode names
            for (int i = 0; i < BattleAnimeRendererCore.SectionNames.Length; i++)
                SectionCombo.Items.Add(BattleAnimeRendererCore.SectionNames[i]);
            SectionCombo.SelectedIndex = 0;

            SectionCombo.SelectionChanged += OnSectionChanged;
            FrameUpDown.ValueChanged += OnFrameValueChanged;
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

        void LoadList()
        {
            try
            {
                var items = _vm.LoadAnimationList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("OAMSpriteViewerView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("OAMSpriteViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            StatusLabel.Text = _vm.StatusText;

            // Frame navigation
            FrameNavPanel.IsVisible = _vm.HasFrameData;
            if (_vm.HasFrameData)
            {
                UpdateFrameUI();
            }
            else
            {
                FrameImageControl.SetImage(null);
            }

            // Tile sheet
            if (_vm.TileSheetImage != null)
            {
                TileSheetPanel.IsVisible = true;
                TileSheetInfoLabel.Text = _vm.TileSheetInfo;
                TileSheetImage.SetImage(_vm.TileSheetImage);
            }
            else
            {
                TileSheetPanel.IsVisible = false;
                TileSheetImage.SetImage(null);
                TileSheetInfoLabel.Text = "";
            }
        }

        void UpdateFrameUI()
        {
            _suppressFrameEvents = true;
            try
            {
                FrameUpDown.Maximum = Math.Max(0, _vm.FrameCount - 1);
                FrameUpDown.Value = _vm.CurrentFrame;
            }
            finally { _suppressFrameEvents = false; }

            FrameInfoLabel.Text = _vm.FrameInfoText;
            FrameImageControl.SetImage(_vm.FrameImage);
        }

        void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (SectionCombo.SelectedIndex < 0 || !_vm.HasFrameData) return;
            _vm.LoadSectionFrames(SectionCombo.SelectedIndex);
            UpdateFrameUI();
        }

        void OnFrameValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressFrameEvents || !_vm.HasFrameData) return;
            int frame = (int)(FrameUpDown.Value ?? 0);
            _vm.GoToFrame(frame);
            UpdateFrameUI();
        }

        void PrevFrame_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.HasFrameData || _vm.FrameCount == 0) return;
            _vm.GoToFrame(_vm.CurrentFrame - 1);
            UpdateFrameUI();
        }

        void NextFrame_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.HasFrameData || _vm.FrameCount == 0) return;
            _vm.GoToFrame(_vm.CurrentFrame + 1);
            UpdateFrameUI();
        }

        async void ExportFrame_Click(object? sender, RoutedEventArgs e)
        {
            if (FrameImageControl.HasImage)
                await FrameImageControl.ExportPng(TopLevel.GetTopLevel(this), $"oam_frame_{_vm.CurrentSection}_{_vm.CurrentFrame}");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
