using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class BattleTerrainViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly BattleTerrainViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Battle Terrain Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public BattleTerrainViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try { var items = _vm.LoadBattleTerrainList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("BattleTerrainViewerView.LoadList: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadBattleTerrain(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("BattleTerrainViewerView.OnSelected: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TerrainNameLabel.Text = _vm.TerrainName;
            B0Box.Value = _vm.NameChar0;
            B1Box.Value = _vm.NameChar1;
            B2Box.Value = _vm.NameChar2;
            B3Box.Value = _vm.NameChar3;
            B4Box.Value = _vm.NameChar4;
            B5Box.Value = _vm.NameChar5;
            B6Box.Value = _vm.NameChar6;
            B7Box.Value = _vm.NameChar7;
            B8Box.Value = _vm.NameChar8;
            B9Box.Value = _vm.NameChar9;
            B10Box.Value = _vm.NameChar10;
            B11Box.Value = _vm.NameChar11;
            ImgPtrBox.Text = $"0x{_vm.ImagePointer:X08}";
            PalPtrBox.Text = $"0x{_vm.PalettePointer:X08}";
            D20Box.Text = $"0x{_vm.UnknownD20:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Battle Terrain");
            try
            {
                _vm.NameChar0 = (uint)(B0Box.Value ?? 0);
                _vm.NameChar1 = (uint)(B1Box.Value ?? 0);
                _vm.NameChar2 = (uint)(B2Box.Value ?? 0);
                _vm.NameChar3 = (uint)(B3Box.Value ?? 0);
                _vm.NameChar4 = (uint)(B4Box.Value ?? 0);
                _vm.NameChar5 = (uint)(B5Box.Value ?? 0);
                _vm.NameChar6 = (uint)(B6Box.Value ?? 0);
                _vm.NameChar7 = (uint)(B7Box.Value ?? 0);
                _vm.NameChar8 = (uint)(B8Box.Value ?? 0);
                _vm.NameChar9 = (uint)(B9Box.Value ?? 0);
                _vm.NameChar10 = (uint)(B10Box.Value ?? 0);
                _vm.NameChar11 = (uint)(B11Box.Value ?? 0);
                _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
                _vm.PalettePointer = ParseHexText(PalPtrBox.Text);
                _vm.UnknownD20 = ParseHexText(D20Box.Text);
                _vm.WriteBattleTerrain();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Battle Terrain data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"Write failed: {ex.Message}");
            }
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
                var loadResult = await ImageImportService.LoadAndQuantize(this, 256, 0, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import Battle Terrain Image");
                // BattleTerrain: Image at offset 12, Palette at offset 16 (2-pointer, LZ77 compressed tiles)
                var importResult = ImageImportCore.Import2Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height, addr + 12, addr + 16);

                if (!importResult.Success) { _undoService.Rollback(); CoreState.Services.ShowError(importResult.Error); return; }

                _undoService.Commit();
                _vm.LoadBattleTerrain(addr);
                UpdateUI();
                LoadImage();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "battle_terrain.png");
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
                // BattleTerrain palette is LZ77-compressed
                byte[] pal = LZ77.decompress(rom.Data, palAddr);
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("Failed to read palette"); return; }
                string? path = await FileDialogHelper.SavePaletteFile(this, "battle_terrain_palette.pal");
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
                _undoService.Begin("Import Battle Terrain Palette");
                // BattleTerrain palette is LZ77-compressed at offset +16
                uint palAddr = ImageImportCore.WriteCompressedToROM(rom, palData, addr + 16);
                if (palAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to write palette"); return; }
                _undoService.Commit();
                _vm.LoadBattleTerrain(addr);
                UpdateUI();
                LoadImage();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Palette imported successfully.");
            }
            catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import palette failed: {ex.Message}"); }
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
