using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageCGFE7UView : Window, IEditorView
    {
        readonly ImageCGFE7UViewModel _vm = new();

        public string ViewTitle => "CG Editor (FE7U)";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageCGFE7UView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ImageCGFE7UView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex)
            {
                Log.Error("ImageCGFE7UView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            string typeDesc = _vm.ImageType == 0 ? "0 (Single)" : _vm.ImageType == 1 ? "1 (10-Split)" : $"0x{_vm.ImageType:X02}";
            ImageTypeLabel.Text = typeDesc;
            ReservedLabel.Text = $"0x{_vm.Reserved1:X02} 0x{_vm.Reserved2:X02} 0x{_vm.Reserved3:X02}";
            SplitImagePtrLabel.Text = $"0x{_vm.SplitImagePtr:X08}";
            TSAPtrLabel.Text = $"0x{_vm.TSAPtr:X08}";
            PalettePtrLabel.Text = $"0x{_vm.PalettePtr:X08}";
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var loadResult = await ImageImportService.LoadAndQuantize(this, 240, 160, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                // P4=image, P8=TSA, P12=palette
                var importResult = ImageImportCore.Import3Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height, addr + 4, addr + 8, addr + 12);

                if (!importResult.Success) { CoreState.Services.ShowError(importResult.Error); return; }

                _vm.LoadEntry(addr);
                UpdateUI();
                LoadImage();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "cg_fe7u.png");
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint addr = _vm.CurrentAddr;
                uint palPtr = rom.u32(addr + 12);
                if (!U.isPointer(palPtr)) { CoreState.Services.ShowError("No palette pointer"); return; }
                uint palAddr = U.toOffset(palPtr);
                byte[] pal = LZ77.decompress(rom.Data, palAddr);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                string path = await FileDialogHelper.SavePaletteFile(this, "cg_fe7u_palette.pal");
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
                string path = await FileDialogHelper.OpenPaletteFile(this);
                if (string.IsNullOrEmpty(path)) return;
                byte[] fileData = File.ReadAllBytes(path);
                PaletteFormat fmt = PaletteFormatConverter.DetectFormat(fileData, System.IO.Path.GetExtension(path));
                byte[] palData = (fmt == PaletteFormat.GbaRaw) ? fileData : PaletteFormatConverter.ImportFromFormat(fileData, fmt);
                if (palData.Length < 32) { CoreState.Services.ShowError("Palette too small (need >= 32 bytes)"); return; }
                uint addr = _vm.CurrentAddr;
                uint palAddr = ImageImportCore.WriteCompressedToROM(rom, palData, addr + 12);
                if (palAddr == U.NOT_FOUND) { CoreState.Services.ShowError("Failed to write palette"); return; }
                _vm.LoadEntry(addr);
                UpdateUI();
                LoadImage();
                CoreState.Services.ShowInfo("Palette imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import palette failed: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
