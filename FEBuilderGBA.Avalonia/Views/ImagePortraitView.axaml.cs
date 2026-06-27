using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImagePortraitView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImagePortraitViewModel _vm = new();
        readonly UndoService _undoService = new();

        static readonly string[] ShowFrameNames = new[]
        {
            "Normal",
            "Half-closed Eyes",
            "Closed Eyes",
            "Mouth 1",
            "Mouth 2",
            "Mouth 3",
            "Mouth 4",
            "Mouth 5",
            "Mouth 6",
            "Mouth 7 (sheet)",
        };

        public string ViewTitle => "Portrait Image Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImagePortraitView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();

            // Enable drag-and-drop for image files
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);

            // MugExceed panel + status-height button visibility — gated on
            // patch detection (cross-platform Avalonia path, no WinForms dep).
            // Plan v3 #424 / WU2.
            UpdatePatchGatedVisibility();
        }

        void UpdatePatchGatedVisibility()
        {
            try
            {
                MugExceedPanel.IsVisible =
                    PatchDetectionService.Instance.PortraitExtends ==
                    PatchDetectionService.PortraitExtendsType.MugExceed;
                JumpToStatusHeightButton.IsVisible =
                    CoreState.ROM?.RomInfo?.version == 8;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitView.UpdatePatchGatedVisibility failed: {0}", ex.Message);
            }
        }

        void OnDragOver(object? sender, DragEventArgs e)
        {
            if (!e.Data.Contains(DataFormats.Files)) { e.DragEffects = DragDropEffects.None; return; }
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f.Path.LocalPath).ToLowerInvariant();
                    if (ext == ".png" || ext == ".bmp") { e.DragEffects = DragDropEffects.Copy; return; }
                }
            }
            e.DragEffects = DragDropEffects.None;
        }

        void OnDrop(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFiles();
            if (files == null) return;

            foreach (var file in files)
            {
                string path = file.Path.LocalPath;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".bmp")
                {
                    ImportImageFromFile(path);
                    return;
                }
            }
        }

        void ImportImageFromFile(string filePath)
        {
            try
            {
                var loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath, 0, 0, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                if (addr == 0) { CoreState.Services.ShowError("No portrait entry selected"); return; }

                // Single source of truth: PortraitImportHelper. Same path used
                // by the Portrait Import Wizard (#657 first slice).
                ImportOutcome outcome;
                if (loadResult.Width == 128 && loadResult.Height == 112)
                {
                    outcome = PortraitImportHelper.ImportSheet(rom, addr, loadResult, _undoService,
                        "Import Portrait Sheet (Drop)");
                }
                else
                {
                    outcome = PortraitImportHelper.ImportSimple(rom, addr, loadResult, _undoService,
                        "Import Portrait Image (Drop)");
                }
                if (!outcome.Success) { CoreState.Services.ShowError(outcome.Error); return; }

                // Record the source file path so the Open / Select Source
                // buttons surface this portrait's origin (matches WF
                // ImagePortraitForm.ImportButton_Click).
                int idx = EntryList.SelectedOriginalIndex;
                PortraitImportHelper.RecordSourceFile(idx, filePath);

                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.RefreshAllImages();
                UpdateImages();
                UpdateSourceButtonVisibility();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Portrait imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.PortraitLoader(items, i));
                UpdateTopBar();
                UpdatePatchGatedVisibility();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                _vm.ShowFrame = 0;
                ShowFrameSelector.Value = 0;
                LoadCommentForCurrentEntry();
                UpdateUI();
                _vm.RefreshAllImages();
                UpdateImages();
                UpdateSourceButtonVisibility();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateTopBar()
        {
            // #649: unified EditorTopBar control.
            if (TopBar == null) return;
            TopBar.StartAddressText = $"0x{_vm.ReadStartAddress:X08}";
            TopBar.ReadCountText = _vm.ReadCount.ToString();
            TopBar.SizeText = _vm.BlockSize.ToString();
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SelectedAddressLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            PortraitImagePtrLabel.Text = $"0x{_vm.PortraitImagePtr:X08}";
            MiniPortraitPtrLabel.Text = $"0x{_vm.MiniPortraitPtr:X08}";
            PalettePtrLabel.Text = $"0x{_vm.PalettePtr:X08}";
            MouthFramesPtrLabel.Text = $"0x{_vm.MouthFramesPtr:X08}";
            ClassCardPtrLabel.Text = $"0x{_vm.ClassCardPtr:X08}";
            MouthXInput.Value = _vm.MouthX;
            MouthYInput.Value = _vm.MouthY;
            EyeXInput.Value = _vm.EyeX;
            EyeYInput.Value = _vm.EyeY;
            StatusLabel.Text = $"0x{_vm.Status:X02}";
            // Sync StatusCombo selection (WF 0/1/6 -> combo index 0/1/2).
            StatusCombo.SelectedIndex = _vm.Status switch { 0 => 0, 1 => 1, 6 => 2, _ => -1 };
            Unused25Label.Text = $"0x{_vm.Unused25:X02}";
            Unused26Label.Text = $"0x{_vm.Unused26:X02}";
            Unused27Label.Text = $"0x{_vm.Unused27:X02}";
            // MugExceed slice inputs — one-way bound from VM (read-only slices).
            MugExceedB16Input.Value = _vm.MugExceedB16;
            MugExceedB17Input.Value = _vm.MugExceedB17;
            MugExceedB18Input.Value = _vm.MugExceedB18;
            MugExceedB19Input.Value = _vm.MugExceedB19;
            CommentInput.Text = _vm.Comment;
            UpdateShowFrameLabel();
            UpdateTopBar();
        }

        void UpdateImages()
        {
            PortraitImage.SetImage(_vm.FaceImage);
            MiniPortraitImage.SetImage(_vm.MiniPortraitImage);
            MouthStripImage.SetImage(_vm.MouthStripImage);
            EyeStripImage.SetImage(_vm.EyeStripImage);
            ClassCardImage.SetImage(_vm.ClassCardImage);
            // Show Example — fixed frame 4 preview matching WF X_PIC_ZZZ.
            try
            {
                var exImg = PortraitRendererCore.DrawPortraitUnitWithFrame(
                    _vm.PortraitImagePtr, _vm.PalettePtr, _vm.MouthFramesPtr,
                    (byte)_vm.MouthX, (byte)_vm.MouthY,
                    (byte)_vm.EyeX, (byte)_vm.EyeY, (byte)_vm.Status, 4);
                ShowExampleImage.SetImage(exImg);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitView.UpdateImages show-example failed: {0}", ex.Message);
                ShowExampleImage.SetImage(null);
            }
        }

        void UpdateShowFrameLabel()
        {
            int idx = _vm.ShowFrame;
            // Translate at assignment time — TranslatedWindow.TranslateAll() runs
            // once at window open, so values assigned afterward go through R._().
            ShowFrameLabel.Text = R._(idx >= 0 && idx < ShowFrameNames.Length
                ? ShowFrameNames[idx] : $"Frame {idx}");
        }

        void LoadCommentForCurrentEntry()
        {
            // Mirrors ImagePortraitFE6View.LoadCommentForCurrentEntry —
            // CoreState.CommentCache is the same EtcCache instance the
            // WinForms InputFormRef.UI_WriteCommentToUI wires through.
            try
            {
                uint addr = _vm.CurrentAddr;
                if (addr == 0) { _vm.Comment = string.Empty; return; }
                if (CoreState.CommentCache != null
                    && CoreState.CommentCache.TryGetValue(addr, out string value))
                {
                    _vm.Comment = value ?? string.Empty;
                }
                else
                {
                    _vm.Comment = string.Empty;
                }
            }
            catch { _vm.Comment = string.Empty; }
        }

        void UpdateSourceButtonVisibility()
        {
            try
            {
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0) { OpenSourceButton.IsVisible = false; SelectSourceButton.IsVisible = false; return; }
                string key = "Portrait_" + U.ToHexString((uint)idx);
                bool has = CoreState.ResourceCache is EtcCacheResource cache
                    && cache.TryGetValue(key, out string? path)
                    && !string.IsNullOrEmpty(path)
                    && File.Exists(path);
                OpenSourceButton.IsVisible = has;
                SelectSourceButton.IsVisible = has;
            }
            catch
            {
                OpenSourceButton.IsVisible = false;
                SelectSourceButton.IsVisible = false;
            }
        }

        void ShowFrame_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            int newFrame = (int)(e.NewValue ?? 0);
            _vm.ShowFrame = newFrame;
            PortraitImage.SetImage(_vm.FaceImage);
            UpdateShowFrameLabel();
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Portrait face tiles: no strict size, quantize to 16 colors
                var loadResult = await ImageImportService.LoadAndQuantize(this, 0, 0, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                if (addr == 0) { CoreState.Services.ShowError("No portrait entry selected"); return; }

                // Single source of truth: PortraitImportHelper. Same path used
                // by the Portrait Import Wizard (#657 first slice).
                ImportOutcome outcome;
                if (loadResult.Width == 128 && loadResult.Height == 112)
                {
                    outcome = PortraitImportHelper.ImportSheet(rom, addr, loadResult, _undoService);
                }
                else
                {
                    outcome = PortraitImportHelper.ImportSimple(rom, addr, loadResult, _undoService);
                }
                if (!outcome.Success) { CoreState.Services.ShowError(outcome.Error); return; }

                // Record source file path (matches WF ImagePortraitForm.ImportButton_Click).
                if (!string.IsNullOrEmpty(loadResult.SourcePath))
                {
                    int idx = EntryList.SelectedOriginalIndex;
                    PortraitImportHelper.RecordSourceFile(idx, loadResult.SourcePath);
                }

                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.RefreshAllImages();
                UpdateImages();
                UpdateSourceButtonVisibility();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Portrait imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await PortraitImage.ExportPng(this, "portrait_face.png");
        }

        async void ExportMini_Click(object? sender, RoutedEventArgs e)
        {
            await MiniPortraitImage.ExportPng(this, "portrait_mini.png");
        }

        async void ExportSheet_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                const int padding = 4;
                int faceW = _vm.FaceImage?.Width ?? 0;
                int faceH = _vm.FaceImage?.Height ?? 0;
                int miniW = _vm.MiniPortraitImage?.Width ?? 0;
                int miniH = _vm.MiniPortraitImage?.Height ?? 0;
                int mouthW = _vm.MouthStripImage?.Width ?? 0;
                int mouthH = _vm.MouthStripImage?.Height ?? 0;
                int eyeW = _vm.EyeStripImage?.Width ?? 0;
                int eyeH = _vm.EyeStripImage?.Height ?? 0;

                if (faceW == 0 && miniW == 0)
                {
                    CoreState.Services.ShowError("No images to export.");
                    return;
                }

                int col0W = Math.Max(faceW, mouthW);
                int col1W = Math.Max(miniW, eyeW);
                int topRowH = Math.Max(faceH, miniH + padding + eyeH);
                int totalW = col0W + (col1W > 0 ? padding + col1W : 0);
                int totalH = topRowH + (mouthH > 0 ? padding + mouthH : 0);

                if (totalW <= 0 || totalH <= 0)
                {
                    CoreState.Services.ShowError("No images to export.");
                    return;
                }

                byte[] composite = new byte[totalW * totalH * 4];

                void BlitImage(IImage? img, int destX, int destY)
                {
                    if (img == null) return;
                    int w = img.Width, h = img.Height;
                    byte[] rgba;
                    if (img.IsIndexed)
                    {
                        byte[] palette = img.GetPaletteRGBA();
                        byte[] indices = img.GetPixelData();
                        rgba = new byte[w * h * 4];
                        for (int i = 0; i < w * h; i++)
                        {
                            int pi = indices[i];
                            if (pi * 4 + 3 < palette.Length)
                            {
                                rgba[i * 4 + 0] = palette[pi * 4 + 0];
                                rgba[i * 4 + 1] = palette[pi * 4 + 1];
                                rgba[i * 4 + 2] = palette[pi * 4 + 2];
                                rgba[i * 4 + 3] = palette[pi * 4 + 3];
                            }
                        }
                    }
                    else
                    {
                        rgba = img.GetPixelData();
                    }

                    for (int y = 0; y < h; y++)
                    {
                        int srcRow = y * w * 4;
                        int dstRow = ((destY + y) * totalW + destX) * 4;
                        for (int x = 0; x < w; x++)
                        {
                            int si = srcRow + x * 4;
                            int di = dstRow + x * 4;
                            if (di + 3 < composite.Length && si + 3 < rgba.Length)
                            {
                                composite[di + 0] = rgba[si + 0];
                                composite[di + 1] = rgba[si + 1];
                                composite[di + 2] = rgba[si + 2];
                                composite[di + 3] = rgba[si + 3];
                            }
                        }
                    }
                }

                BlitImage(_vm.FaceImage, 0, 0);
                BlitImage(_vm.MiniPortraitImage, col0W + padding, 0);
                BlitImage(_vm.EyeStripImage, col0W + padding, miniH + padding);
                BlitImage(_vm.MouthStripImage, 0, topRowH + padding);

                var wb = new WriteableBitmap(
                    new PixelSize(totalW, totalH),
                    new Vector(96, 96),
                    global::Avalonia.Platform.PixelFormat.Rgba8888,
                    global::Avalonia.Platform.AlphaFormat.Premul);

                using (var buf = wb.Lock())
                {
                    unsafe
                    {
                        byte* ptr = (byte*)buf.Address;
                        int stride = buf.RowBytes;
                        for (int y = 0; y < totalH; y++)
                        {
                            int srcRow = y * totalW * 4;
                            int dstRow = y * stride;
                            for (int x = 0; x < totalW * 4; x++)
                            {
                                ptr[dstRow + x] = composite[srcRow + x];
                            }
                        }
                    }
                }

                string? path = await FileDialogHelper.SaveImageFile(this, "portrait_sheet.png");
                if (string.IsNullOrEmpty(path)) return;

                using var stream = File.Create(path);
                wb.Save(stream);
                wb.Dispose();
            }
            catch (Exception ex)
            {
                CoreState.Services.ShowError($"Export sheet failed: {ex.Message}");
            }
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint palPtr = _vm.PalettePtr;
                if (!U.isPointer(palPtr)) { CoreState.Services.ShowError("No palette pointer"); return; }
                uint palAddr = U.toOffset(palPtr);
                byte[] pal = ImageUtilCore.GetPalette(palAddr, 16);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                string? path = await FileDialogHelper.SavePaletteFile(this, "portrait_palette.pal");
                if (string.IsNullOrEmpty(path)) return;
                PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(path));
                File.WriteAllBytes(path, PaletteFormatConverter.ExportToFormat(pal, fmt));
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export palette failed: {ex.Message}"); }
        }

        async void ImportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                string? path = await FileDialogHelper.OpenPaletteFile(this);
                if (string.IsNullOrEmpty(path)) return;
                byte[] fileData = File.ReadAllBytes(path);
                PaletteFormat fmt = PaletteFormatConverter.DetectFormat(fileData, System.IO.Path.GetExtension(path));
                byte[] palData = (fmt == PaletteFormat.GbaRaw) ? fileData : PaletteFormatConverter.ImportFromFormat(fileData, fmt);
                if (palData.Length < 32) { CoreState.Services.ShowError("Palette too small (need >= 32 bytes)"); return; }
                uint addr = _vm.CurrentAddr;
                if (addr == 0) { CoreState.Services.ShowError("No portrait entry selected"); return; }
                _undoService.Begin("Import Portrait Palette");
                uint palAddr = ImageImportCore.WritePaletteToROM(rom, palData, addr + 8);
                if (palAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to write palette"); return; }
                _undoService.Commit();
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.RefreshAllImages();
                UpdateImages();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Palette imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import palette failed: {ex.Message}"); }
        }

        void Position_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _vm.MouthX = (uint)(MouthXInput.Value ?? 0);
            _vm.MouthY = (uint)(MouthYInput.Value ?? 0);
            _vm.EyeX = (uint)(EyeXInput.Value ?? 0);
            _vm.EyeY = (uint)(EyeYInput.Value ?? 0);
            _vm.RefreshFaceImage();
            PortraitImage.SetImage(_vm.FaceImage);
        }

        /// <summary>
        /// Status combo changed — translate combo index → ROM B24 value.
        /// (Combo: 0→0=Close Mouth, 1→1=Normal, 2→6=Close Eyes.)
        /// </summary>
        void StatusCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            int sel = StatusCombo.SelectedIndex;
            uint b24 = sel switch { 0 => 0u, 1 => 1u, 2 => 6u, _ => _vm.Status };
            _vm.Status = b24;
            StatusLabel.Text = $"0x{_vm.Status:X02}";
        }

        /// <summary>
        /// MugExceed Tile1 X/Y + Tile2 X/Y changed — compose the new
        /// ClassCardPtr u32 from the 4 NumericUpDown values. The MugExceedB16-B19
        /// VM properties are read-only computed slices of D16, so we write the
        /// composed u32 here and the VM stays consistent. (Plan v3 #424 /
        /// Copilot CLI plan-review point on MugExceed write path.)
        /// </summary>
        void MugExceed_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint b16 = (uint)(MugExceedB16Input.Value ?? 0) & 0xFF;
            uint b17 = (uint)(MugExceedB17Input.Value ?? 0) & 0xFF;
            uint b18 = (uint)(MugExceedB18Input.Value ?? 0) & 0xFF;
            uint b19 = (uint)(MugExceedB19Input.Value ?? 0) & 0xFF;
            _vm.ClassCardPtr = (b19 << 24) | (b18 << 16) | (b17 << 8) | b16;
            ClassCardPtrLabel.Text = $"0x{_vm.ClassCardPtr:X08}";
        }

        void Comment_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _vm.Comment = CommentInput.Text ?? string.Empty;
            try
            {
                uint addr = _vm.CurrentAddr;
                if (addr == 0) return;
                CoreState.CommentCache?.Update(addr, _vm.Comment);
            }
            catch { /* non-fatal — caching is best effort */ }
        }

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        /// <summary>
        /// Write button. Single-owner pattern (Copilot CLI plan-review point):
        /// the View delegates to the VM, which owns the UndoService scope.
        /// </summary>
        void WriteButton_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            uint addr = _vm.CurrentAddr;
            if (addr == 0) { CoreState.Services.ShowError("No portrait entry selected"); return; }

            // Snapshot UI state into the VM before delegating.
            _vm.MouthX = (uint)(MouthXInput.Value ?? 0);
            _vm.MouthY = (uint)(MouthYInput.Value ?? 0);
            _vm.EyeX = (uint)(EyeXInput.Value ?? 0);
            _vm.EyeY = (uint)(EyeYInput.Value ?? 0);
            // Status is already kept in sync by StatusCombo_SelectionChanged.
            // MugExceed bytes are already composed into ClassCardPtr by MugExceed_ValueChanged.

            try
            {
                _vm.Write(_undoService);
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Portrait entry written.");
            }
            catch (Exception ex)
            {
                CoreState.Services.ShowError($"Write failed: {ex.Message}");
            }
        }

        void OpenSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0) return;
                string key = "Portrait_" + U.ToHexString((uint)idx);
                if (CoreState.ResourceCache is EtcCacheResource cache
                    && cache.TryGetValue(key, out string? path)
                    && !string.IsNullOrEmpty(path))
                {
                    if (!File.Exists(path)) { CoreState.Services.ShowError("Source file not found."); return; }
                    var psi = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
                else
                {
                    CoreState.Services.ShowError("No source file recorded for this portrait.");
                }
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Open source failed: {ex.Message}"); }
        }

        void SelectSource_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0) return;
                string key = "Portrait_" + U.ToHexString((uint)idx);
                if (CoreState.ResourceCache is EtcCacheResource cache
                    && cache.TryGetValue(key, out string? path)
                    && !string.IsNullOrEmpty(path))
                {
                    if (!File.Exists(path)) { CoreState.Services.ShowError("Source file not found."); return; }
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
                else
                {
                    CoreState.Services.ShowError("No source file recorded for this portrait.");
                }
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Select source failed: {ex.Message}"); }
        }

        async void FERepoButton_Click(object? sender, RoutedEventArgs e)
        {
            var browser = new FERepoResourceBrowserWindow();
            string result = await browser.ShowDialog<string>(this);
            if (!string.IsNullOrEmpty(result))
            {
                ImportImageFromFile(result);
            }
        }

        // ---------------------------------------------------------------
        // Cross-editor jump buttons (Phase 4 #424) — open-only contract.
        // Match WF semantics where the click handler opens the target form,
        // with rich JumpTo enrichment (bitmap propagation, ID-based row
        // selection) deferred to follow-up issues.
        // ---------------------------------------------------------------

        void JumpToPalette_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Wire the portrait's palette address into the rebuilt
                // ImagePalletView (#400 - Copilot CLI plan review #2).
                // Portraits use a single palette block, so
                // maxPaletteCount=1 hides the palette-index combo.
                //
                // #1023: also pass a render delegate so the Palette Editor's
                // live preview shows THIS portrait's mini/map face recolored by
                // the grid colors (block-overload renders WITHOUT writing the
                // ROM, so an unsaved edit is reflected live).
                // DrawPortraitMap renders the 4x4-tile mini/map face — that is
                // MiniPortraitPtr (u32@4), NOT PortraitImagePtr (u32@0, the
                // LZ77-compressed 96x80 main face which this raw-tile renderer
                // cannot decode). Mirrors the VM's own MiniPortraitImage pairing.
                uint mapFacePtr = _vm.MiniPortraitPtr;
                var window = WindowManager.Instance.Open<ImagePalletView>();
                window.JumpTo(_vm.PalettePtr, maxPaletteCount: 1, defaultSelectPalette: 0, paletteNames: null,
                    renderPreview: block => PortraitRendererCore.DrawPortraitMap(mapFacePtr, block));
            }
            catch (Exception ex) { Log.ErrorF("JumpToPalette failed: {0}", ex.Message); }
        }

        void JumpToImporter_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var view = WindowManager.Instance.Open<ImagePortraitImporterView>();
                if (_vm.CurrentAddr != 0)
                    view.NavigateTo(_vm.CurrentAddr);   // position the importer at the portrait being edited (right target + its B20-B23)
            }
            catch (Exception ex) { Log.ErrorF("JumpToImporter failed: {0}", ex.Message); }
        }

        void JumpToStatusHeight_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var view = WindowManager.Instance.Open<UnitIncreaseHeightView>();
                // Best-effort pre-selection of the selected portrait's height row.
                // An out-of-range id (no height row, or NoPortraitSelection) just
                // leaves the default first row selected — no error (#1019).
                view.NavigateToId(_vm.GetSelectedPortraitId());
            }
            catch (Exception ex) { Log.ErrorF("JumpToStatusHeight failed: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
