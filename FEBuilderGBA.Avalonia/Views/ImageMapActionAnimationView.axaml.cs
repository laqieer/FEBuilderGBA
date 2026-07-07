using global::Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Dialogs;
using global::Avalonia.Platform.Storage;
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
    public partial class ImageMapActionAnimationView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ImageMapActionAnimationViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        bool _suppressZoomChange;
        // Set true while UpdateUI syncs `ShowFrameUpDown.Value` from the VM
        // — the SelectionChanged handler short-circuits so the compute+render
        // pair isn't duplicated (Copilot CLI inline review on PR #506).
        bool _suppressFrameChange;
        // Track the current preview Bitmap so we can dispose it before
        // replacing — avoids unmanaged-memory growth during frame scrubbing
        // (Copilot CLI inline review on PR #506).
        Bitmap? _currentPreviewBitmap;

        public string ViewTitle => "Map Action Animation";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Map Action Animation", 1024, 640, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ImageMapActionAnimationView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            // Dispose the last preview Bitmap when the window closes so
            // unmanaged memory is released — Copilot CLI inline review on
            // PR #506.
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (_currentPreviewBitmap != null)
            {
                try { _currentPreviewBitmap.Dispose(); } catch { /* swallow */ }
                _currentPreviewBitmap = null;
            }
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
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.MapActionAnimationLoader(items, i));
                // #649: display via the unified EditorTopBar read-only slots.
                TopBar.StartAddressText = _vm.ReadStartAddress.ToString();
                TopBar.ReadCountText = _vm.ReadCount.ToString();

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
                Log.ErrorF("ImageMapActionAnimationView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        // #649: routed event from the unified EditorTopBar Reload button.
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
                Log.ErrorF("ImageMapActionAnimationView.OnSelected failed: {0}", ex.Message);
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
                // Suppress the ValueChanged handler while we sync the
                // NumericUpDown from the VM so the compute+render pair
                // below isn't duplicated by the handler (Copilot CLI
                // inline review on PR #506 — double-render flicker).
                _suppressFrameChange = true;
                try { ShowFrameUpDown.Value = _vm.SelectedFrame; }
                finally { _suppressFrameChange = false; }

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
                SetPreviewBitmap(null);
                BinInfoBox.Text = "";
            }

            // #499: re-evaluate Export/Import/Source-file button gating on
            // every selection change.
            RefreshExportImportButtonState();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // Early-guard so we don't create no-op undo entries when the
            // VM hasn't loaded an entry yet — `_vm.Write()` itself returns
            // immediately on null ROM or CurrentAddr==0, but the
            // Begin/Commit pair would still push an empty entry into the
            // undo buffer (Copilot CLI inline review on PR #506).
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

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
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("ImageMapActionAnimationView.Write: {0}", ex.Message); }
        }

        /// <summary>
        /// Replace `PreviewImage.Source` with <paramref name="bmp"/>,
        /// disposing the previous Bitmap (if any) first. Tracks the current
        /// Bitmap in <see cref="_currentPreviewBitmap"/> so unmanaged memory
        /// doesn't accumulate during frame scrubbing — Copilot CLI inline
        /// review on PR #506.
        /// </summary>
        void SetPreviewBitmap(Bitmap? bmp)
        {
            if (_currentPreviewBitmap != null && !ReferenceEquals(_currentPreviewBitmap, bmp))
            {
                try { _currentPreviewBitmap.Dispose(); } catch { /* swallow */ }
            }
            _currentPreviewBitmap = bmp;
            PreviewImage.Source = bmp;
        }

        /// <summary>
        /// List-expansion handler (#501). Prompts the user for a new row
        /// count, delegates to <see cref="ImageMapActionAnimationViewModel.ExpandList"/>
        /// inside an <see cref="UndoService"/> scope, then reloads the list.
        /// Mirrors WinForms <c>InputFormRef.OnAddressListExpandsEventHandler</c>
        /// flow (prompt -> expand -> repoint -> reload). As of #1025 ExpandList
        /// composes <c>DataExpansionCore.RepointAllReferences</c> (raw 32-bit +
        /// ARM-Thumb LDR literal-pool repoint), so the LDR rescan is no longer a
        /// gap — every reference to the moved table base is repointed.
        /// </summary>
        async void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }
                if (_vm.ReadCount == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: list is empty."));
                    return;
                }

                // Default = current count + 1, max = 255 (mirrors WF
                // AddressListExpandsButton_255 suffix convention).
                uint defaultCount = _vm.ReadCount + 1;
                if (defaultCount > 255) defaultCount = 255;
                uint? chosen = await NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the new entry count for the map action animation list (current: {0}, max: 255).",
                        _vm.ReadCount),
                    R._("List Expansion"),
                    defaultCount,
                    _vm.ReadCount,
                    255);
                if (chosen == null) return; // user cancelled
                uint newCount = chosen.Value;
                if (newCount == _vm.ReadCount)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count equals current count."));
                    return;
                }

                _undoService.Begin("Expand Map Action Animation List");
                try
                {
                    string err = _vm.ExpandList(newCount, _undoService.GetActiveUndoData());
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    LoadList();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded map action animation list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.ErrorF("ImageMapActionAnimationView.ListExpand_Click inner failed: {0}", inner.Message);
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMapActionAnimationView.ListExpand_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Open the currently-selected animation in the Animation Creator (#500).
        /// Direct-from-ROM path — no temp file. The Creator view's
        /// <c>InitFromRom</c> walks the frame table starting at
        /// <c>_vm.AnimationPointer</c> and populates its own VM state.
        /// </summary>
        void OpenInCreator_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsAnimationValid)
                {
                    CoreState.Services.ShowInfo(R._("This entry has no valid animation pointer."));
                    return;
                }
                uint animeOffset = U.toOffset(_vm.AnimationPointer);
                if (!U.isSafetyOffset(animeOffset))
                {
                    CoreState.Services.ShowInfo(R._("Animation pointer 0x{0:X} is outside the ROM.",
                        _vm.AnimationPointer));
                    return;
                }
                // Hint is user-visible via `ToolAnimationCreatorView.FileHint`,
                // so route it through `R._(...)` for translation. The id arg
                // is `SelectedId` (the 0-based animation id) — NOT
                // `CurrentAddr` — because the id is the natural identifier
                // for de-dup / tab uniqueness in the Animation Creator
                // (Copilot CLI inline review on PR #619).
                string hint = R._("Map Action Animation #{0:X2}", _vm.SelectedId);
                var view = WindowManager.Instance.Open<ToolAnimationCreatorView>();
                view.InitFromRom(AnimationTypeEnum.MapActionAnimation,
                    _vm.SelectedId, hint, animeOffset);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMapActionAnimationView.OpenInCreator_Click failed: {0}", ex.Message);
            }
        }

        void ShowFrameUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressFrameChange) return;
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
                    SetPreviewBitmap(null);
                    return;
                }
                uint animePtr = U.toOffset(_vm.AnimationPointer);
                if (!U.isSafetyOffset(animePtr))
                {
                    SetPreviewBitmap(null);
                    return;
                }
                using var img = ImageUtilMapActionAnimationCore.DrawFrame(animePtr, _vm.SelectedFrame);
                Bitmap? bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                SetPreviewBitmap(bmp);
            }
            catch
            {
                SetPreviewBitmap(null);
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

        // ----------------------------------------------------------------
        // #499 Export / Import / Source-file handlers
        // ----------------------------------------------------------------

        /// <summary>
        /// Update IsEnabled/IsVisible state of the 4 #499 buttons after a
        /// list selection change. Mirrors WF
        /// `AddressList_SelectedIndexChanged`:
        /// - WF hides Export/Import; Avalonia keeps them rendered (Copilot bot
        ///   review found the doc-vs-impl mismatch was misleading) but disables
        ///   them via IsEnabled so the layout doesn't shift when selection
        ///   changes. The disabled state is the Avalonia-equivalent of the
        ///   WF "hidden" affordance — both signal "not actionable right now".
        /// - OpenSource/SelectSource: hide when no source path is remembered
        ///   (matches WF, which uses `this.OpenSourceButton.Hide()`).
        /// </summary>
        void RefreshExportImportButtonState()
        {
            AnimationExportButton.IsEnabled = _vm.IsAnimationValid;
            AnimationImportButton.IsEnabled = _vm.IsLoaded && !_vm.IsEmptyEntry;
            bool hasSource = _vm.TryGetSourcePath(out string srcPath)
                && !string.IsNullOrEmpty(srcPath)
                && File.Exists(srcPath);
            OpenSourceButton.IsVisible = hasSource;
            SelectSourceButton.IsVisible = hasSource;
        }

        async void AnimationExport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsAnimationValid)
            {
                CoreState.Services?.ShowError(R._("No valid animation selected to export."));
                return;
            }
            string suggested = $"MapActionAnimation_{_vm.SelectedId:X02}.MapActionAnimation.txt";
            // Multi-pattern SaveFilePicker (Copilot bot review on PR #620
            // round 1, inline #1) — the user can pick the .txt script or the
            // .gif export directly from the dropdown instead of having to
            // type the extension manually under "All Files".
            // #1639: pick the handle so we can branch by format — the single-file
            // .gif export routes through the SAF bridge, while the .txt script
            // (which writes sibling PNGs) requires a real local path.
            var file = await FileDialogHelper.SaveFilePick(TopLevel.GetTopLevel(this),
                R._("Save Map Action Animation"),
                new[]
                {
                    (R._("Map Action Animation Script"), "*.MapActionAnimation.txt"),
                    (R._("Animated GIF"), "*.gif"),
                },
                suggested);
            if (file == null) return;
            bool isGif = (file.Name ?? "").EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
            string? localPath = file.TryGetLocalPath();
            if (!isGif && string.IsNullOrEmpty(localPath))
            {
                CoreState.Services?.ShowError(R._("Exporting an animation script writes sibling PNG frames and requires desktop file-system access; export as GIF instead, or use a desktop device."));
                return;
            }
            try
            {
                string err = "";
                string? written;
                if (isGif)
                {
                    written = await FileDialogHelper.WriteViaAsync(file, p => { err = _vm.ExportGif(p); });
                }
                else
                {
                    err = _vm.ExportScript(localPath);
                    written = localPath;
                }
                if (written == null) return;
                if (!string.IsNullOrEmpty(err))
                {
                    CoreState.Services?.ShowError(err);
                    return;
                }
                CoreState.Services?.ShowInfo(R._("Exported to: {0}", written));
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Export failed: {0}", ex.Message));
            }
        }

        async void AnimationImport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.IsEmptyEntry)
            {
                CoreState.Services?.ShowError(R._("Select a non-empty entry first."));
                return;
            }
            // #1639: ImportScript resolves sibling frame PNGs from the script's
            // own directory, so require a real local path; a SAF pick (no local
            // path) cannot resolve siblings → message on Android, never silent.
            string? path = await FileDialogHelper.OpenFile(TopLevel.GetTopLevel(this),
                R._("Open Map Action Animation Script"),
                "*.MapActionAnimation.txt", requireLocalPath: true);
            if (string.IsNullOrEmpty(path))
            {
                if (OperatingSystem.IsAndroid())
                    CoreState.Services?.ShowError(R._("Importing a map-action-animation script reads sibling PNG frames and requires desktop file-system access; it is not available on this device."));
                return;
            }

            _undoService.Begin("Import Map Action Animation");
            try
            {
                string err = _vm.ImportScript(path, LoadRgbaFromFile);
                if (!string.IsNullOrEmpty(err))
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(err);
                    return;
                }
                _undoService.Commit();
                _vm.RememberSourcePath(path);
                _vm.MarkClean();
                LoadList();
                RefreshExportImportButtonState();
                CoreState.Services?.ShowInfo(R._("Imported: {0}", path));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
            }
        }

        void OpenSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.TryGetSourcePath(out string path) || !File.Exists(path))
                {
                    CoreState.Services?.ShowError(R._("Source file not found."));
                    return;
                }
                var psi = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Open source failed: {0}", ex.Message));
            }
        }

        void SelectSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.TryGetSourcePath(out string path) || !File.Exists(path))
                {
                    CoreState.Services?.ShowError(R._("Source file not found."));
                    return;
                }
                string? dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir)) return;
                if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Open folder failed: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Image-loader callback for the Import path. Returns RGBA pixels +
        /// dimensions, or null if the file is unreadable. Used by
        /// <see cref="MapActionAnimationExportImportCore.ImportScript"/>
        /// when a frame line has no RAW-* hints.
        /// </summary>
        static (byte[] rgba, int w, int h)? LoadRgbaFromFile(string path)
        {
            if (!File.Exists(path) || CoreState.ImageService == null) return null;
            try
            {
                using var img = CoreState.ImageService.LoadImage(path);
                if (img == null) return null;

                // Copilot bot review on PR #620 round 2: `IImage.GetPixelData()`
                // returns ONE BYTE PER PIXEL (palette indices) when
                // `img.IsIndexed == true` — feeding that into
                // `MapActionAnimationExportImportCore.ImportScript`'s quantize
                // path would corrupt the tile data (expects 4-bytes-per-pixel RGBA).
                // Expand indexed -> RGBA via the palette so downstream code
                // always sees a uniform 4 bytes/pixel buffer.
                if (img.IsIndexed)
                {
                    byte[] indexed = img.GetPixelData();
                    byte[] paletteRgba = img.GetPaletteRGBA();
                    byte[] rgba = GifEncoderCore.IndexedToRgba(
                        indexed, paletteRgba, img.Width, img.Height);
                    return (rgba, img.Width, img.Height);
                }
                return (img.GetPixelData(), img.Width, img.Height);
            }
            catch
            {
                return null;
            }
        }
    }
}
