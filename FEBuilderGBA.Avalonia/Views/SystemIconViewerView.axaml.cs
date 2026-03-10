using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SystemIconViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SystemIconViewerViewModel _vm = new();

        public string ViewTitle => "System Icon Viewer";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public SystemIconViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadSystemIconList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("SystemIconViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                // addr here is the icon index (stored in AddrResult.addr)
                _vm.LoadSystemIconByIndex(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("SystemIconViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            IconIndexLabel.Text = $"0x{_vm.IconIndex:X02} ({_vm.IconIndex})";
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrLabel.Text = $"0x{_vm.ImageGbaPointer:X08}";
            PalPtrLabel.Text = $"0x{_vm.PaletteGbaPointer:X08}";
            TileOffsetLabel.Text = $"0x{_vm.TileOffset:X04}";
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        async void ExportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "system_icon.png");
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                byte[] pal = _vm.CachedPalette;
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("No palette loaded"); return; }
                string? path = await FileDialogHelper.SavePaletteFile(this, "system_icon_palette.pal");
                if (string.IsNullOrEmpty(path)) return;
                PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(path));
                File.WriteAllBytes(path, PaletteFormatConverter.ExportToFormat(pal, fmt));
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export palette failed: {ex.Message}"); }
        }

        async void ImportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (_vm.CachedPalette == null)
                {
                    CoreState.Services.ShowError("No palette loaded. Select an icon first.");
                    return;
                }

                // Remap to existing shared palette instead of quantizing a new one
                var loadResult = await ImageImportService.LoadAndRemapToExistingPalette(
                    this, 16, 16, _vm.CachedPalette, 16, strictSize: true);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                // Encode the 16x16 icon as 4bpp tile data (128 bytes for 2x2 tiles)
                byte[] tileData = ImageImportCore.EncodeDirectTiles4bpp(loadResult.IndexedPixels, 16, 16);
                if (tileData == null) { CoreState.Services.ShowError("Failed to encode icon tile data"); return; }

                // Decompress the full sheet, patch the icon's tiles, recompress, write back
                if (_vm.TileOffset + tileData.Length > 0)
                {
                    // Read full decompressed sheet
                    uint imgPtr = rom.RomInfo.system_icon_pointer;
                    uint imgAddr = rom.p32(imgPtr);
                    byte[] sheetData = LZ77.decompress(rom.Data, imgAddr);
                    if (sheetData == null) { CoreState.Services.ShowError("Failed to decompress icon sheet"); return; }

                    // Patch the specific icon tiles into the sheet
                    // System icons are 2x2 tiles arranged in a sheet _sheetTilesX wide
                    uint widthVal = rom.u8(rom.RomInfo.system_icon_width_address);
                    if (widthVal > 32) widthVal = 32;
                    else if (widthVal < 0x12) widthVal = 0x12;
                    int sheetTilesX = (int)widthVal;
                    int iconsPerRow = sheetTilesX / 2;
                    if (iconsPerRow <= 0) { CoreState.Services.ShowError("Invalid icon sheet width"); return; }

                    int iconX = (int)(_vm.IconIndex % (uint)iconsPerRow);
                    int iconY = (int)(_vm.IconIndex / (uint)iconsPerRow);
                    int startTileX = iconX * 2;
                    int startTileY = iconY * 2;

                    // Copy the 4 tiles from encoded data into the correct sheet positions
                    for (int ty = 0; ty < 2; ty++)
                    {
                        for (int tx = 0; tx < 2; tx++)
                        {
                            int sheetTileIdx = (startTileY + ty) * sheetTilesX + (startTileX + tx);
                            int dstOffset = sheetTileIdx * 32;
                            int srcOffset = (ty * 2 + tx) * 32;
                            if (dstOffset + 32 <= sheetData.Length && srcOffset + 32 <= tileData.Length)
                                Array.Copy(tileData, srcOffset, sheetData, dstOffset, 32);
                        }
                    }

                    // Recompress and write back
                    uint writeAddr = ImageImportCore.WriteCompressedToROM(rom, sheetData, imgPtr);
                    if (writeAddr == U.NOT_FOUND) { CoreState.Services.ShowError("Failed to write compressed icon sheet (no free space)"); return; }
                }

                // Reload to refresh cached tile data
                _vm.LoadSystemIconList();
                _vm.LoadSystemIconByIndex(_vm.IconIndex);
                LoadImage();
                CoreState.Services.ShowInfo("System icon imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
