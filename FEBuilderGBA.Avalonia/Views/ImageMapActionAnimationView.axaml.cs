using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms `ImageMapActionAnimationForm`. The
    /// Phase 1/4/6 gap-sweep fix (#433) folds the missing read-config bar,
    /// selection bar, list-expansion affordance, comment textbox, KeepEmpty
    /// notice, and animation preview panel into the AXAML, and wires the
    /// click handlers that exist on master to update the new fields. Real
    /// Export / Import / Source-file controls are tracked as follow-ups
    /// (#499, #500, #501) — see the navigation manifest in
    /// `ImageMapActionAnimationViewModel.NavigationTargets.cs`.
    /// </summary>
    public partial class ImageMapActionAnimationView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageMapActionAnimationViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressZoomChange;

        public string ViewTitle => "Map Action Animation";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public ImageMapActionAnimationView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.MapActionAnimationLoader(items, i));
                ReadStartAddressBox.Value = _vm.ReadStartAddress;
                ReadCountBox.Value = _vm.ReadCount;

                // Reset zoom selection AND explicitly sync `PreviewImage.Stretch`
                // + `_vm.ShowZoomed` because the SelectionChanged handler is
                // suppressed while we mutate `SelectedIndex`. Without this
                // explicit sync, reloading after the user picked "Original
                // size" would leave the preview unzoomed while the combo
                // showed "Zoomed" — Copilot CLI inline review on PR #506.
                _suppressZoomChange = true;
                try
                {
                    ShowZoomComboBox.SelectedIndex = 0;
                    _vm.ShowZoomed = true;
                    PreviewImage.Stretch = global::Avalonia.Media.Stretch.Uniform;
                }
                finally { _suppressZoomChange = false; }
            }
            catch (Exception ex)
            {
                Log.Error("ImageMapActionAnimationView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
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
                Log.Error("ImageMapActionAnimationView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            // Selection-bar widgets — mirror WF panel5.
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Value = _vm.BlockSize;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            // Edit fields.
            AnimationPointerBox.Text = $"0x{_vm.AnimationPointer:X08}";
            Padding1Box.Value = _vm.Padding1;
            Padding2Box.Value = _vm.Padding2;
            CommentBox.Text = _vm.Comment;

            // KeepEmpty notice — ID=0 is reserved as null data.
            KeepEmptyLabel.IsVisible = _vm.IsEmptyEntry;

            // Animation panel — only when D0 resolves safely.
            AnimationPanel.IsVisible = _vm.IsAnimationValid;

            if (_vm.IsAnimationValid)
            {
                ShowFrameUpDown.Value = _vm.SelectedFrame;
                _vm.ComputeFrameInfo(_vm.SelectedFrame);
                BinInfoBox.Text = _vm.BinInfoText;
                // Render the SELECTED frame (mirrors WinForms
                // ShowFrameUpDown_ValueChanged on initial load). The earlier
                // implementation called both UpdatePreview() and
                // UpdatePreviewForFrame() which raced — Copilot CLI inline
                // review on PR #506.
                UpdatePreviewForFrame();
            }
            else
            {
                PreviewImage.Source = null;
                BinInfoBox.Text = "";
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Map Action Animation");
            try
            {
                _vm.AnimationPointer = ParseHexText(AnimationPointerBox.Text);
                _vm.Padding1 = (uint)(Padding1Box.Value ?? 0);
                _vm.Padding2 = (uint)(Padding2Box.Value ?? 0);
                _vm.Comment = CommentBox.Text ?? "";
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ImageMapActionAnimationView.Write: {0}", ex.Message); }
        }

        /// <summary>
        /// List-expansion handler — currently disabled (the button has
        /// <c>IsEnabled=false</c> in AXAML) because there is no Core helper
        /// for expanding the map-action-animation pointer table yet. The
        /// handler stays wired so the AutomationId is enumerable from
        /// headless tests; the real expansion is tracked by #501.
        /// </summary>
        void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            // Intentionally no-op until the table-expansion helper lands —
            // see issue #501. The button is disabled in AXAML; this handler
            // exists only so the binding/AutomationId surface is complete.
            Log.Debug("ImageMapActionAnimationView.ListExpand_Click invoked — disabled until #501 lands");
        }

        void ShowFrameUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!_vm.IsAnimationValid) return;
            _vm.SelectedFrame = (uint)(ShowFrameUpDown.Value ?? 0);
            _vm.ComputeFrameInfo(_vm.SelectedFrame);
            BinInfoBox.Text = _vm.BinInfoText;
            UpdatePreviewForFrame();
        }

        void UpdatePreviewForFrame()
        {
            try
            {
                // Mirror WinForms: accept either a GBA pointer (e.g.
                // 0x08800000) or a safe ROM offset (e.g. 0x800000). The
                // earlier guard rejected raw offsets even when
                // `IsAnimationValid` (offset-based) said the panel should
                // be visible — leaving the preview blank for user-entered
                // offsets. `ImageUtilMapActionAnimationCore.DrawFrame`
                // returns null for un-renderable input so the catch handles
                // anything else. Copilot CLI inline review on PR #506.
                if (_vm.AnimationPointer == 0)
                {
                    PreviewImage.Source = null;
                    return;
                }
                uint animePtr = U.toOffset(_vm.AnimationPointer);
                if (!U.isSafetyOffset(animePtr))
                {
                    PreviewImage.Source = null;
                    return;
                }
                using var img = ImageUtilMapActionAnimationCore.DrawFrame(animePtr, _vm.SelectedFrame);
                Bitmap? bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                PreviewImage.Source = bmp;
            }
            catch
            {
                PreviewImage.Source = null;
            }
        }

        void ShowZoomComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressZoomChange) return;
            // SelectedIndex 0 => Zoomed (default), 1 => original size.
            bool zoomed = ShowZoomComboBox.SelectedIndex == 0;
            _vm.ShowZoomed = zoomed;
            PreviewImage.Stretch = zoomed
                ? global::Avalonia.Media.Stretch.Uniform
                : global::Avalonia.Media.Stretch.None;
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
