using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PortraitViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly PortraitViewerViewModel _vm = new();

        public string ViewTitle => "Portrait Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public PortraitViewerView()
        {
            InitializeComponent();
            PortraitList.SelectedAddressChanged += OnPortraitSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadPortraitList();
                PortraitList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("PortraitViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnPortraitSelected(uint addr)
        {
            try
            {
                _vm.LoadPortrait(addr);
                UpdateUI();
                TryShowPortraitImage();
            }
            catch (Exception ex)
            {
                Log.Error("PortraitViewerView.OnPortraitSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            PortraitList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrBox.Text = $"0x{_vm.ImagePointer:X08}";
            MapPtrBox.Text = $"0x{_vm.MapPointer:X08}";
            PalPtrBox.Text = $"0x{_vm.PalettePointer:X08}";
            D12Box.Text = $"0x{_vm.MouthPointer:X08}";
            D16Box.Text = $"0x{_vm.ClassCardPointer:X08}";
            B20Box.Value = _vm.MouthX;
            B21Box.Value = _vm.MouthY;
            B22Box.Value = _vm.EyeX;
            B23Box.Value = _vm.EyeY;
            B24Box.Value = _vm.State;
            B25Box.Value = _vm.Padding25;
            B26Box.Value = _vm.Padding26;
            B27Box.Value = _vm.Padding27;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
            _vm.MapPointer = ParseHexText(MapPtrBox.Text);
            _vm.PalettePointer = ParseHexText(PalPtrBox.Text);
            _vm.MouthPointer = ParseHexText(D12Box.Text);
            _vm.ClassCardPointer = ParseHexText(D16Box.Text);
            _vm.MouthX = (uint)(B20Box.Value ?? 0);
            _vm.MouthY = (uint)(B21Box.Value ?? 0);
            _vm.EyeX = (uint)(B22Box.Value ?? 0);
            _vm.EyeY = (uint)(B23Box.Value ?? 0);
            _vm.State = (uint)(B24Box.Value ?? 0);
            _vm.Padding25 = (uint)(B25Box.Value ?? 0);
            _vm.Padding26 = (uint)(B26Box.Value ?? 0);
            _vm.Padding27 = (uint)(B27Box.Value ?? 0);
            _vm.WritePortrait();
            CoreState.Services.ShowInfo("Portrait data written.");
        }

        void TryShowPortraitImage()
        {
            try
            {
                MainPortraitImage.SetImage(_vm.TryLoadMainPortrait());
            }
            catch (Exception ex)
            {
                Log.Error("TryShowPortraitImage main failed: {0}", ex.Message);
                MainPortraitImage.SetImage(null);
            }

            try
            {
                MapPortraitImage.SetImage(_vm.TryLoadMapPortrait());
            }
            catch (Exception ex)
            {
                Log.Error("TryShowPortraitImage map failed: {0}", ex.Message);
                MapPortraitImage.SetImage(null);
            }

            try
            {
                ClassPortraitImage.SetImage(_vm.TryLoadClassPortrait());
            }
            catch (Exception ex)
            {
                Log.Error("TryShowPortraitImage class failed: {0}", ex.Message);
                ClassPortraitImage.SetImage(null);
            }
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

                // Encode tiles and write compressed to ROM (ImagePointer at offset 0)
                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                if (tileData == null) { CoreState.Services.ShowError("Failed to encode tiles"); return; }

                uint tileAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, addr + 0);
                if (tileAddr == U.NOT_FOUND) { CoreState.Services.ShowError("No free space for tile data"); return; }

                // PalettePointer at offset 8
                uint palAddr = ImageImportCore.WritePaletteToROM(rom, loadResult.GBAPalette, addr + 8);
                if (palAddr == U.NOT_FOUND) { CoreState.Services.ShowError("No free space for palette"); return; }

                _vm.LoadPortrait(addr);
                UpdateUI();
                TryShowPortraitImage();
                CoreState.Services.ShowInfo("Portrait imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await MainPortraitImage.ExportPng(this, "portrait.png");
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint palPtr = _vm.PalettePointer;
                if (!U.isPointer(palPtr)) { CoreState.Services.ShowError("No palette pointer"); return; }
                uint palAddr = U.toOffset(palPtr);
                // Portrait palette is raw (not compressed), 16 colors = 32 bytes
                byte[] pal = ImageUtilCore.GetPalette(palAddr, 16);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                string? path = await FileDialogHelper.SavePaletteFile(this, "portrait_palette.pal");
                if (string.IsNullOrEmpty(path)) return;
                File.WriteAllBytes(path, pal);
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
                byte[] palData = File.ReadAllBytes(path);
                if (palData.Length < 32) { CoreState.Services.ShowError("Palette too small (need >= 32 bytes)"); return; }
                uint addr = _vm.CurrentAddr;
                if (addr == 0) { CoreState.Services.ShowError("No portrait entry selected"); return; }
                // Portrait palette is raw at offset +8
                uint palAddr = ImageImportCore.WritePaletteToROM(rom, palData, addr + 8);
                if (palAddr == U.NOT_FOUND) { CoreState.Services.ShowError("Failed to write palette"); return; }
                _vm.LoadPortrait(addr);
                UpdateUI();
                TryShowPortraitImage();
                CoreState.Services.ShowInfo("Palette imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import palette failed: {ex.Message}"); }
        }

        public void SelectFirstItem()
        {
            PortraitList.SelectFirst();
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
