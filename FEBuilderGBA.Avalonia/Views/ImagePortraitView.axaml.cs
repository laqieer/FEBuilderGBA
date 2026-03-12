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
    public partial class ImagePortraitView : Window, IEditorView
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

                _undoService.Begin("Import Portrait Image (Drop)");
                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                if (tileData == null) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to encode tiles"); return; }

                uint tileAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, addr + 0);
                if (tileAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("No free space for tile data"); return; }

                uint palAddr = ImageImportCore.WritePaletteToROM(rom, loadResult.GBAPalette, addr + 8);
                if (palAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("No free space for palette"); return; }

                _undoService.Commit();
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.RefreshAllImages();
                UpdateImages();
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
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitView.LoadList failed: {0}", ex.Message);
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
                UpdateUI();
                _vm.RefreshAllImages();
                UpdateImages();
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
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
            Unused25Label.Text = $"0x{_vm.Unused25:X02}";
            Unused26Label.Text = $"0x{_vm.Unused26:X02}";
            Unused27Label.Text = $"0x{_vm.Unused27:X02}";
            UpdateShowFrameLabel();
        }

        void UpdateImages()
        {
            PortraitImage.SetImage(_vm.FaceImage);
            MiniPortraitImage.SetImage(_vm.MiniPortraitImage);
            MouthStripImage.SetImage(_vm.MouthStripImage);
            EyeStripImage.SetImage(_vm.EyeStripImage);
            ClassCardImage.SetImage(_vm.ClassCardImage);
        }

        void UpdateShowFrameLabel()
        {
            int idx = _vm.ShowFrame;
            ShowFrameLabel.Text = idx >= 0 && idx < ShowFrameNames.Length
                ? ShowFrameNames[idx] : $"Frame {idx}";
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

                _undoService.Begin("Import Portrait Image");
                // Encode tiles and write compressed to ROM
                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                if (tileData == null) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to encode tiles"); return; }

                uint tileAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, addr + 0);
                if (tileAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("No free space for tile data"); return; }

                uint palAddr = ImageImportCore.WritePaletteToROM(rom, loadResult.GBAPalette, addr + 8);
                if (palAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("No free space for palette"); return; }

                _undoService.Commit();
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.RefreshAllImages();
                UpdateImages();
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
                // Compose a sheet: face (96x80) | mini (32x32) on top-right
                //                                | eye strip (32x32) below mini
                //                  mouth strip (32x96) below face
                // Layout: 2 columns
                //   Col 0: face (96x80), then mouth strip (32x96) below
                //   Col 1: mini (32x32), then eye strip (32x32) below
                // Total: width = 96 + 8 + 32 = 136, height = max(80, 32+8+32) + 8 + 96 = 184

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

                // Layout: face top-left, mini top-right of face, mouth below face, eye below mini
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

                // Create RGBA composite
                byte[] composite = new byte[totalW * totalH * 4];

                // Blit helper
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

                // Blit each component
                BlitImage(_vm.FaceImage, 0, 0);
                BlitImage(_vm.MiniPortraitImage, col0W + padding, 0);
                BlitImage(_vm.EyeStripImage, col0W + padding, miniH + padding);
                BlitImage(_vm.MouthStripImage, 0, topRowH + padding);

                // Create WriteableBitmap (RGBA8888, matching GbaImageControl pattern)
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

                // Save via dialog
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
                // Portrait palette is raw (not compressed), 16 colors = 32 bytes
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
                // Portrait palette is raw at offset +8
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
            // Update VM from controls and live-refresh the face preview
            _vm.MouthX = (uint)(MouthXInput.Value ?? 0);
            _vm.MouthY = (uint)(MouthYInput.Value ?? 0);
            _vm.EyeX = (uint)(EyeXInput.Value ?? 0);
            _vm.EyeY = (uint)(EyeYInput.Value ?? 0);
            _vm.RefreshFaceImage();
            PortraitImage.SetImage(_vm.FaceImage);
        }

        void WritePositions_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            uint addr = _vm.CurrentAddr;
            if (addr == 0) { CoreState.Services.ShowError("No portrait entry selected"); return; }

            // Read current values from controls
            _vm.MouthX = (uint)(MouthXInput.Value ?? 0);
            _vm.MouthY = (uint)(MouthYInput.Value ?? 0);
            _vm.EyeX = (uint)(EyeXInput.Value ?? 0);
            _vm.EyeY = (uint)(EyeYInput.Value ?? 0);

            _undoService.Begin("Write Portrait Positions");
            try
            {
                rom.write_u8(addr + 20, _vm.MouthX);
                rom.write_u8(addr + 21, _vm.MouthY);
                rom.write_u8(addr + 22, _vm.EyeX);
                rom.write_u8(addr + 23, _vm.EyeY);
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Mouth/eye positions written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"Write positions failed: {ex.Message}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
