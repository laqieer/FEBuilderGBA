using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
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
            MouthXLabel.Text = _vm.MouthX.ToString();
            MouthYLabel.Text = _vm.MouthY.ToString();
            EyeXLabel.Text = _vm.EyeX.ToString();
            EyeYLabel.Text = _vm.EyeY.ToString();
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
            await PortraitImage.ExportPng(this, "portrait.png");
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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
