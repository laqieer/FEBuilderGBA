using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPPrologueViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly OPPrologueViewerViewModel _vm = new();

        public string ViewTitle => "OP Prologue Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public OPPrologueViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadOPPrologueList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("OPPrologueViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadOPPrologue(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("OPPrologueViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrBox.Text = $"0x{_vm.ImagePointer:X08}";
            TsaPtrBox.Text = $"0x{_vm.TSAPointer:X08}";
            PalAddrLabel.Text = $"0x{_vm.PaletteColorPointer:X08}";
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
            _vm.TSAPointer = ParseHexText(TsaPtrBox.Text);
            _vm.WriteOPPrologue();
            CoreState.Services?.ShowInfo("OP Prologue data written.");
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "op_prologue.png");
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
                var importResult = ImageImportCore.Import3Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height, addr + 0, addr + 4, addr + 8);

                if (!importResult.Success) { CoreState.Services.ShowError(importResult.Error); return; }

                _vm.LoadOPPrologue(addr);
                UpdateUI();
                LoadImage();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint palAddr = _vm.PaletteColorPointer;
                if (!U.isSafetyOffset(palAddr)) { CoreState.Services.ShowError("No palette pointer"); return; }
                byte[] pal = ImageUtilCore.GetPalette(palAddr, 256);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                string path = await FileDialogHelper.SavePaletteFile(this, "op_prologue_palette.pal");
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
                uint palAddr = _vm.PaletteColorPointer;
                if (!U.isSafetyOffset(palAddr)) { CoreState.Services.ShowError("No palette address to write to"); return; }
                // Write raw palette data directly at the shared palette address
                int writeLen = Math.Min(palData.Length, 256 * 2);
                for (int i = 0; i < writeLen; i++)
                    rom.write_u8(palAddr + (uint)i, palData[i]);
                _vm.LoadOPPrologue(_vm.CurrentAddr);
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
