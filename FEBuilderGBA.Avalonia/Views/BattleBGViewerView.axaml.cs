using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class BattleBGViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly BattleBGViewerViewModel _vm = new();

        public string ViewTitle => "Battle Background Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public BattleBGViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadBattleBGList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("BattleBGViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadBattleBG(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("BattleBGViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrBox.Text = $"0x{_vm.ImagePointer:X08}";
            TsaPtrBox.Text = $"0x{_vm.TSAPointer:X08}";
            PalPtrBox.Text = $"0x{_vm.PalettePointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
            _vm.TSAPointer = ParseHexText(TsaPtrBox.Text);
            _vm.PalettePointer = ParseHexText(PalPtrBox.Text);
            _vm.WriteBattleBG();
            CoreState.Services.ShowInfo("Battle Background data written.");
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
                if (loadResult == null) return; // cancelled
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                // BattleBG stores all 3 components LZ77-compressed (including palette)
                var importResult = ImageImportCore.Import3Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height, addr + 0, addr + 4, addr + 8, compressPalette: true);

                if (!importResult.Success) { CoreState.Services.ShowError(importResult.Error); return; }

                _vm.LoadBattleBG(addr);
                UpdateUI();
                LoadImage();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "battle_bg.png");
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
                // BattleBG palette is LZ77-compressed
                byte[] pal = LZ77.decompress(rom.Data, palAddr);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                string path = await FileDialogHelper.SavePaletteFile(this, "battle_bg_palette.pal");
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
                // BattleBG palette is LZ77-compressed
                uint palAddr = ImageImportCore.WriteCompressedToROM(rom, palData, addr + 8);
                if (palAddr == U.NOT_FOUND) { CoreState.Services.ShowError("Failed to write palette"); return; }
                _vm.LoadBattleBG(addr);
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
