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
        readonly UndoService _undoService = new();

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
            _vm.IsLoading = true;
            try { var items = _vm.LoadOPPrologueList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("OPPrologueViewerView.LoadList: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadOPPrologue(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("OPPrologueViewerView.OnSelected: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
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
            _undoService.Begin("Edit OP Prologue");
            try
            {
                _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
                _vm.TSAPointer = ParseHexText(TsaPtrBox.Text);
                _vm.WriteOPPrologue();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("OP Prologue data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("OPPrologueViewerView.Write: {0}", ex.Message); }
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

                _undoService.Begin("Import OP Prologue Image");
                try
                {
                    uint addr = _vm.CurrentAddr;
                    var importResult = ImageImportCore.Import3Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                        loadResult.Width, loadResult.Height, addr + 0, addr + 4, addr + 8);

                    if (!importResult.Success) { _undoService.Rollback(); CoreState.Services.ShowError(importResult.Error); return; }

                    _vm.LoadOPPrologue(addr);
                    UpdateUI();
                    LoadImage();
                    _undoService.Commit();
                    _vm.MarkClean();
                    CoreState.Services.ShowInfo("Image imported successfully.");
                }
                catch { _undoService.Rollback(); throw; }
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
                uint palAddr = _vm.PaletteColorPointer;
                if (!U.isSafetyOffset(palAddr)) { CoreState.Services.ShowError("No palette address to write to"); return; }

                _undoService.Begin("Import OP Prologue Palette");
                try
                {
                    int writeLen = Math.Min(palData.Length, 256 * 2);
                    for (int i = 0; i < writeLen; i++)
                        rom.write_u8(palAddr + (uint)i, palData[i]);
                    _vm.LoadOPPrologue(_vm.CurrentAddr);
                    UpdateUI();
                    LoadImage();
                    _undoService.Commit();
                    _vm.MarkClean();
                    CoreState.Services.ShowInfo("Palette imported successfully.");
                }
                catch { _undoService.Rollback(); throw; }
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
