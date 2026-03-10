using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImagePortraitView : Window, IEditorView
    {
        readonly ImagePortraitViewModel _vm = new();

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
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                TryShowPortraitImage();
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitView.OnSelected failed: {0}", ex.Message);
            }
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
        }

        void TryShowPortraitImage()
        {
            try
            {
                // Try to render portrait using PortraitRendererCore
                var img = PortraitRendererCore.DrawPortraitUnit(
                    _vm.PortraitImagePtr, _vm.PalettePtr,
                    (byte)_vm.EyeX, (byte)_vm.EyeY, (byte)_vm.Status);
                PortraitImage.SetImage(img);
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitView.TryShowPortraitImage failed: {0}", ex.Message);
                PortraitImage.SetImage(null);
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

                // Encode tiles and write compressed to ROM
                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                if (tileData == null) { CoreState.Services.ShowError("Failed to encode tiles"); return; }

                uint tileAddr = ImageImportCore.WriteCompressedToROM(rom, tileData, addr + 0);
                if (tileAddr == U.NOT_FOUND) { CoreState.Services.ShowError("No free space for tile data"); return; }

                uint palAddr = ImageImportCore.WritePaletteToROM(rom, loadResult.GBAPalette, addr + 8);
                if (palAddr == U.NOT_FOUND) { CoreState.Services.ShowError("No free space for palette"); return; }

                _vm.LoadEntry(addr);
                UpdateUI();
                TryShowPortraitImage();
                CoreState.Services.ShowInfo("Portrait imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await PortraitImage.ExportPng(this, "portrait.png");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
