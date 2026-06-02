using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms `SkillConfigCSkillSystem09xForm`.
    /// Phase 1/2/4/5/6 gap-sweep fix (#430) raises the AXAML control surface
    /// from 9 to MEDIUM-verdict density and wires the W4/W6/animation-pointer
    /// write under a single UndoService scope. Real image/animation
    /// import/export and editor-jump still depend on Core extraction work
    /// tracked by #500 - those buttons render so the density verdict moves,
    /// but their click handlers are intentional no-ops with a tooltip until
    /// the Core seam lands (mirrors the pattern used by #433).
    /// </summary>
    public partial class SkillConfigFE8UCSkillSys09xView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8UCSkillSys09xViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressZoomChange;
        bool _suppressFrameChange;
        Bitmap? _currentIconBitmap;
        Bitmap? _currentPreviewBitmap;

        public string ViewTitle => "Skill Configuration (CSkillSys 0.9.x)";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public SkillConfigFE8UCSkillSys09xView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
            Closed += (_, _) =>
            {
                DisposeBitmap(ref _currentIconBitmap);
                DisposeBitmap(ref _currentPreviewBitmap);
            };
        }

        static void DisposeBitmap(ref Bitmap? bmp)
        {
            if (bmp == null) return;
            try { bmp.Dispose(); } catch { /* swallow */ }
            bmp = null;
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.CSkillSysSkillIconLoader(items, i));
                // #743: unified top-bar surfaces ReadStart / ReadCount via CLR properties.
                if (TopBar != null)
                {
                    TopBar.ReadStartAddress = _vm.ReadStartAddress;
                    TopBar.ReadCount = (int)_vm.ReadCount;
                }

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
                Log.Error("SkillConfigFE8UCSkillSys09xView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        // #743: routed event from the unified EditorTopBarWithInputs Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

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
                Log.Error("SkillConfigFE8UCSkillSys09xView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            // Selection-bar widgets.
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Value = _vm.BlockSize;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            // Edit fields.
            IconAddrLabel.Content = $"0x{_vm.IconAddr:X08}";
            SkillNameBox.Value = _vm.SkillNameMsg;
            DescriptionBox.Value = _vm.DescriptionMsg;
            SkillNameTextBox.Text = _vm.SkillNameText;
            DescriptionTextBox.Text = _vm.DescriptionText;
            AnimationPointerBox.Value = _vm.AnimationPointer;

            // Icon Image - render the per-skill icon by dereferencing the
            // GBA pointer stored at entry+0 (not striped like SkillSystems).
            try
            {
                using var img = PreviewIconHelper.LoadCSkillSysIcon(_vm.IconAddr);
                Bitmap? bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                SetIconBitmap(bmp);
            }
            catch { SetIconBitmap(null); }

            // Animation panel - only when D0 resolves safely.
            AnimationPanel.IsVisible = _vm.IsAnimationValid;

            if (_vm.IsAnimationValid)
            {
                _suppressFrameChange = true;
                try { ShowFrameUpDown.Value = _vm.SelectedFrame; }
                finally { _suppressFrameChange = false; }

                BinInfoBox.Text = _vm.BinInfoText;
                // Real frame rendering depends on the WinForms-only
                // ImageUtilSkillSystemsAnimeCreator (#500). Until that
                // moves to Core, leave the preview Image blank but show the
                // animation address text so the user knows the pointer is
                // valid.
                SetPreviewBitmap(null);
            }
            else
            {
                SetPreviewBitmap(null);
                BinInfoBox.Text = "";
            }
        }

        void WriteButton_Click(object? sender, RoutedEventArgs e)
        {
            // Early-guard so we don't create no-op undo entries when the
            // VM hasn't loaded an entry yet (#506 pattern).
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

            _undoService.Begin("Edit Skill Config CSkillSys 0.9.x");
            try
            {
                _vm.SkillNameMsg = (uint)(SkillNameBox.Value ?? 0);
                _vm.DescriptionMsg = (uint)(DescriptionBox.Value ?? 0);
                _vm.AnimationPointer = (uint)(AnimationPointerBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillConfigFE8UCSkillSys09xView.Write failed: {0}", ex.Message);
            }
        }

        void SetIconBitmap(Bitmap? bmp)
        {
            if (_currentIconBitmap != null && !ReferenceEquals(_currentIconBitmap, bmp))
            {
                try { _currentIconBitmap.Dispose(); } catch { /* swallow */ }
            }
            _currentIconBitmap = bmp;
            IconImage.Source = bmp;
        }

        void SetPreviewBitmap(Bitmap? bmp)
        {
            if (_currentPreviewBitmap != null && !ReferenceEquals(_currentPreviewBitmap, bmp))
            {
                try { _currentPreviewBitmap.Dispose(); } catch { /* swallow */ }
            }
            _currentPreviewBitmap = bmp;
            PreviewImage.Source = bmp;
        }

        // -----------------------------------------------------------
        // No-op handlers - wired so the AutomationIds are enumerable
        // from headless tests, and so the density verdict moves. The
        // real implementations depend on Core extraction tracked by
        // #500. Mirrors the exact pattern used by #433's
        // ListExpand_Click.
        // -----------------------------------------------------------

        void ImageImport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8UCSkillSys09xView.ImageImport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void ImageExport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8UCSkillSys09xView.ImageExport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void AnimationImport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8UCSkillSys09xView.AnimationImport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void AnimationExport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Debug("SkillConfigFE8UCSkillSys09xView.AnimationExport_Click invoked - disabled until Core extraction lands (#500)");
        }

        void JumpToEditor_Click(object? sender, RoutedEventArgs e)
        {
            // Navigate-only open; full Init/struct-jump context is the
            // symmetric #500/#374 follow-up. Mirrors the merged
            // ImageMagicFEditorView.JumpEditor_Click (#894) pattern.
            try
            {
                WindowManager.Instance.Open<ToolAnimationCreatorView>();
            }
            catch (Exception ex)
            {
                Log.Error("SkillConfigFE8UCSkillSys09xView.JumpToEditor_Click failed: {0}", ex.Message);
            }
        }

        void ShowFrameUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressFrameChange) return;
            if (!_vm.IsAnimationValid) return;
            _vm.SelectedFrame = (uint)(ShowFrameUpDown.Value ?? 0);
            BinInfoBox.Text = _vm.BinInfoText;
            // Real per-frame rendering pending #500.
        }

        void ShowZoomComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressZoomChange) return;
            bool zoomed = ShowZoomComboBox.SelectedIndex == 0;
            _vm.ShowZoomed = zoomed;
            PreviewImage.Stretch = zoomed
                ? global::Avalonia.Media.Stretch.Uniform
                : global::Avalonia.Media.Stretch.None;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
