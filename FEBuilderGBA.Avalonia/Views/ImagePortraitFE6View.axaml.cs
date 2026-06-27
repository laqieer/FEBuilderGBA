using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImagePortraitFE6View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImagePortraitFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();

        static readonly string[] ShowFrameNames = new[]
        {
            "Normal (no mouth)",
            "Mouth 1",
            "Mouth 2",
            "Mouth 3 (Example)",
            "Mouth 4",
            "Mouth 5",
        };

        public string ViewTitle => "Portrait Editor (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public ImagePortraitFE6View()
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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.PortraitLoader(items, i));
                UpdateTopBar();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitFE6View.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                LoadCommentForCurrentEntry();
                UpdateUI();
                TryShowPortraitImage();
                UpdateSourceButtonVisibility();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitFE6View.OnSelected failed: {0}", ex.Message);
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
            MouthXInput.Value = _vm.MouthX;
            MouthYInput.Value = _vm.MouthY;
            Unused14Input.Value = _vm.Unused14;
            Unused15Input.Value = _vm.Unused15;
            CommentInput.Text = _vm.Comment;
            UpdateShowFrameLabel();
            UpdateTopBar();
        }

        void UpdateShowFrameLabel()
        {
            int idx = (int)(ShowFrameInput?.Value ?? 0);
            // Translate the static description string at assignment time —
            // TranslatedWindow.TranslateAll() runs once at window open, so
            // values assigned afterward must go through R._() explicitly
            // to localize when the UI language is ja/zh.
            ShowFrameLabel.Text = R._(idx >= 0 && idx < ShowFrameNames.Length
                ? ShowFrameNames[idx] : $"Frame {idx}");
        }

        void TryShowPortraitImage()
        {
            try
            {
                int showFrame = (int)(ShowFrameInput?.Value ?? 0);
                byte mouthX = (byte)(_vm.MouthX & 0xFF);
                byte mouthY = (byte)(_vm.MouthY & 0xFF);

                // Main face: respect mouth coords + show frame
                var img = PortraitRendererCoreFE6.DrawPortraitUnitFE6(
                    _vm.PortraitImagePtr, _vm.PalettePtr,
                    mouthX, mouthY, showFrame);
                PortraitImage.SetImage(img);

                // Map face
                if (_vm.MiniPortraitPtr != 0)
                {
                    var mapImg = PortraitRendererCore.DrawPortraitMap(
                        _vm.MiniPortraitPtr, _vm.PalettePtr);
                    MapFaceImage.SetImage(mapImg);
                }
                else
                {
                    MapFaceImage.SetImage(null);
                }

                // Show example: WF uses fixed frame 3 (口ぱく3) for the side preview
                var exImg = PortraitRendererCoreFE6.DrawPortraitUnitFE6(
                    _vm.PortraitImagePtr, _vm.PalettePtr,
                    mouthX, mouthY, 3);
                ShowExampleImage.SetImage(exImg);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitFE6View.TryShowPortraitImage failed: {0}", ex.Message);
                PortraitImage.SetImage(null);
                MapFaceImage.SetImage(null);
                ShowExampleImage.SetImage(null);
            }
        }

        void LoadCommentForCurrentEntry()
        {
            // WF wires `Comment` through `Program.CommentCache.At(addr)` —
            // an IEtcCache keyed by the entry's ROM address (see
            // InputFormRef.UI_WriteCommentToUI). Avalonia uses the same
            // CoreState.CommentCache instance so the cache file lives in
            // exactly the same on-disk slot for both heads.
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
            // WF shows the Open / Select Source File buttons when a portrait
            // image was imported from disk and the source path was recorded
            // in Program.ResourceCache under "Portrait_<id>". We surface the
            // same EtcCacheResource state — when ResourceCache is null (some
            // headless / CLI launches) the buttons stay hidden.
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

        void Field_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _vm.MouthX = (uint)(MouthXInput.Value ?? 0);
            _vm.MouthY = (uint)(MouthYInput.Value ?? 0);
            _vm.Unused14 = (uint)(Unused14Input.Value ?? 0);
            _vm.Unused15 = (uint)(Unused15Input.Value ?? 0);
            // Live preview re-renders with new mouth coords
            TryShowPortraitImage();
        }

        void ShowFrame_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            UpdateShowFrameLabel();
            TryShowPortraitImage();
        }

        void Comment_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _vm.Comment = CommentInput.Text ?? string.Empty;
            // Persist via CoreState.CommentCache keyed by the current ROM
            // address — same data file WF writes through
            // Program.CommentCache.Update (InputFormRef.UI_ReadUIToComment).
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
        /// Write button. Single-owner pattern (Copilot CLI plan-review point 1):
        /// the View delegates to the VM, which owns the UndoService scope.
        /// The handler does NOT open its own Begin scope.
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
            _vm.Unused14 = (uint)(Unused14Input.Value ?? 0);
            _vm.Unused15 = (uint)(Unused15Input.Value ?? 0);

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
                    // Cross-platform "open file" — defer to OS default handler.
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
                    // Reveal the file in the OS file explorer. Windows: explorer /select,
                    string? dir = Path.GetDirectoryName(path);
                    if (string.IsNullOrEmpty(dir)) return;
                    if (OperatingSystem.IsWindows())
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                    }
                    else
                    {
                        // Cross-platform fallback: open the parent directory.
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

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            string? filePath = await FileDialogHelper.OpenImageFile(this);
            if (string.IsNullOrEmpty(filePath)) return;
            ImportImageFromFile(filePath);
        }

        // #1397 — FE-Repo button: pick a portrait mug from the FE-Repo
        // "Portrait Repository" folder and route it through the SAME FromFile
        // import path (variable-dimension portraits — lenient, matches the
        // file-picker).
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            string? path = await FERepoPickHelper.PickForEditor(this,
                FERepoResourceBrowser.FERepoEditorKind.Portrait);
            if (string.IsNullOrEmpty(path)) return;
            ImportImageFromFile(path);
        }

        // Shared FromFile import body (file-picker + FE-Repo both call this).
        void ImportImageFromFile(string filePath)
        {
            try
            {
                // Portrait face tiles: no strict size, quantize to 16 colors
                var loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath, 0, 0, 16);
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                if (addr == 0) { CoreState.Services.ShowError("No portrait entry selected"); return; }

                _undoService.Begin("Import Portrait Image (FE6)");
                // Encode tiles and write compressed to ROM
                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                if (tileData == null) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to encode tiles"); return; }

                uint tileAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, addr + 0);
                if (tileAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("No free space for tile data"); return; }

                uint palAddr = ImageImportCore.WritePaletteToROM(rom, loadResult.GBAPalette, addr + 8);
                if (palAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("No free space for palette"); return; }

                _undoService.Commit();

                // Record the source file path so the Open / Select Source
                // buttons surface this portrait's origin (matches WF
                // ImagePortraitFE6Form.ImportButton_Click).
                if (!string.IsNullOrEmpty(loadResult.SourcePath))
                {
                    int idx = EntryList.SelectedOriginalIndex;
                    if (idx >= 0 && CoreState.ResourceCache is EtcCacheResource cache)
                    {
                        string srcKey = "Portrait_" + U.ToHexString((uint)idx);
                        cache.Update(srcKey, loadResult.SourcePath);
                    }
                }

                _vm.LoadEntry(addr);
                UpdateUI();
                TryShowPortraitImage();
                UpdateSourceButtonVisibility();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Portrait imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await PortraitImage.ExportPng(this, "portrait_fe6.png");
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
                // FE6 portrait palette is raw (not compressed), 16 colors = 32 bytes
                byte[] pal = ImageUtilCore.GetPalette(palAddr, 16);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                await FileDialogHelper.SavePaletteFileVia(this, "portrait_fe6_palette.pal", p =>
                {
                    // #1639: write via the SAF bridge so Android content:// targets work.
                    PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(p));
                    File.WriteAllBytes(p, PaletteFormatConverter.ExportToFormat(pal, fmt));
                });
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
                _undoService.Begin("Import Portrait Palette (FE6)");
                // FE6 portrait palette is raw at offset +8
                uint palAddr = ImageImportCore.WritePaletteToROM(rom, palData, addr + 8);
                if (palAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to write palette"); return; }
                _undoService.Commit();
                _vm.LoadEntry(addr);
                UpdateUI();
                TryShowPortraitImage();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Palette imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import palette failed: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
