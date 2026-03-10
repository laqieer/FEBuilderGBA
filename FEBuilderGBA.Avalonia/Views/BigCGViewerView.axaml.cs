using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class BigCGViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly BigCGViewerViewModel _vm = new();

        public string ViewTitle => "Big CG Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public BigCGViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadBigCGList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("BigCGViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadBigCG(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("BigCGViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TablePtrBox.Text = $"0x{_vm.TablePointer:X08}";
            TsaPtrBox.Text = $"0x{_vm.TSAPointer:X08}";
            PalPtrBox.Text = $"0x{_vm.PalettePointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.TablePointer = ParseHexText(TablePtrBox.Text);
            _vm.TSAPointer = ParseHexText(TsaPtrBox.Text);
            _vm.PalettePointer = ParseHexText(PalPtrBox.Text);
            _vm.WriteBigCG();
            CoreState.Services.ShowInfo("Big CG data written.");
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "big_cg.png");
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var loadResult = await ImageImportService.LoadAndQuantize(this, 256, 160, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;

                // Encode TSA with tile deduplication
                var tsaResult = ImageImportCore.EncodeTSA(loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                if (tsaResult == null) { CoreState.Services.ShowError("Failed to encode TSA"); return; }

                // Write compressed TSA
                uint tsaAddr = ImageImportCore.WriteCompressedToROM(rom, tsaResult.TSAData, addr + 4);
                if (tsaAddr == U.NOT_FOUND) { CoreState.Services.ShowError("No free space for TSA data"); return; }

                // Write palette
                uint palAddr = ImageImportCore.WritePaletteToROM(rom, loadResult.GBAPalette, addr + 8);
                if (palAddr == U.NOT_FOUND) { CoreState.Services.ShowError("No free space for palette"); return; }

                // For tile data: write as single compressed block and update first table entry
                uint tablePtr = rom.u32(addr + 0);
                if (U.isPointer(tablePtr))
                {
                    uint tableAddr = U.toOffset(tablePtr);
                    byte[] compressed = LZ77.compress(tsaResult.TileData);
                    if (compressed != null)
                    {
                        uint tileAddr = ImageImportCore.FindAndWriteData(rom, compressed);
                        if (tileAddr != U.NOT_FOUND)
                        {
                            // Update first table entry to point to the tile data
                            rom.write_p32(tableAddr, tileAddr);
                            // Zero out remaining table entries (entries 1-9)
                            for (int i = 1; i < 10; i++)
                                rom.write_u32(tableAddr + (uint)(i * 4), 0);
                        }
                    }
                }

                _vm.LoadBigCG(addr);
                UpdateUI();
                LoadImage();
                CoreState.Services.ShowInfo("BigCG image imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint addr = _vm.CurrentAddr;
                uint palPtr = rom.u32(addr + 8);
                if (!U.isPointer(palPtr)) { CoreState.Services.ShowError("No palette pointer"); return; }
                uint palAddr = U.toOffset(palPtr);
                byte[] pal = ImageUtilCore.GetPalette(palAddr, 256);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                string path = await FileDialogHelper.SavePaletteFile(this, "big_cg_palette.pal");
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
                string path = await FileDialogHelper.OpenPaletteFile(this);
                if (string.IsNullOrEmpty(path)) return;
                byte[] palData = File.ReadAllBytes(path);
                if (palData.Length < 32) { CoreState.Services.ShowError("Palette too small (need >= 32 bytes)"); return; }
                uint addr = _vm.CurrentAddr;
                uint palAddr = ImageImportCore.WritePaletteToROM(rom, palData, addr + 8);
                if (palAddr == U.NOT_FOUND) { CoreState.Services.ShowError("Failed to write palette"); return; }
                _vm.LoadBigCG(addr);
                UpdateUI();
                LoadImage();
                CoreState.Services.ShowInfo("Palette imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import palette failed: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
