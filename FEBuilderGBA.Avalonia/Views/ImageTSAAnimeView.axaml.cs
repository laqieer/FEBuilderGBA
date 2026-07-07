using global::Avalonia;
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageTSAAnimeView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ImageTSAAnimeViewModel _vm = new();
        bool _hasLoadedList;

        // TSA Anime v1 palette is the WinForms 8-bank raw palette:
        // 8 banks x 16 colors x 2 bytes = 256 bytes (ImageTSAAnimeForm uses palette_count=8).
        const int PALETTE_BYTES = 0x20 * 8;

        public string ViewTitle => "TSA Animation Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("TSA Animation Editor", 1383, 556, SizeToContent: true);
        public event EventHandler? CloseRequested;

        public ImageTSAAnimeView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
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
                Log.ErrorF("ImageTSAAnimeView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("ImageTSAAnimeView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
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
                var loadResult = await ImageImportService.LoadAndQuantize(TopLevel.GetTopLevel(this) as Window, 240, 160, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                // WinForms ImageTSAAnimeForm layout: P0=image(LZ77)@+0, P4=palette(raw)@+4,
                // P8=TSA(LZ77)@+8. Import3Pointer's signature is (img, tsa, pal), so TSA
                // maps to addr+8 and the (raw) palette to addr+4 (#1457). compressPalette
                // stays false → palette written raw, matching WinForms WriteImageData(P4,...,false).
                var importResult = ImageImportCore.Import3Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height, addr + 0, addr + 8, addr + 4);

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
            await ImageDisplay.ExportPng(TopLevel.GetTopLevel(this) as Window, "tsa_anime.png");
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                uint addr = _vm.CurrentAddr;
                uint palPtr = rom.u32(addr + 4);
                if (!U.isPointer(palPtr)) { CoreState.Services.ShowError("No palette pointer"); return; }
                uint palAddr = U.toOffset(palPtr);
                // Palette is RAW in ROM (8 banks x 16 colors x 2 bytes = 256), NOT LZ77 —
                // matches WinForms ImageTSAAnimeForm (WriteImageData(P4,...,false)) (#1457).
                // getBinaryData truncates at EOF, so reject a short read (palette pointer
                // near the ROM end) rather than exporting a partial 8-bank palette.
                byte[] pal = rom.getBinaryData(palAddr, PALETTE_BYTES);
                if (pal == null || pal.Length < PALETTE_BYTES) { CoreState.Services.ShowError("Failed to read palette (expected 256 bytes)"); return; }
                await FileDialogHelper.SavePaletteFileVia(TopLevel.GetTopLevel(this) as Window, "tsa_anime_palette.pal", p =>
                {
                    // #1639: write via the SAF bridge so Android content:// targets work.
                    PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(p));
                    File.WriteAllBytes(p, PaletteFormatConverter.ExportToFormat(pal, fmt));
                });
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export palette failed: {ex.Message}"); }
        }

        async void ImportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                string path = await FileDialogHelper.OpenPaletteFile(TopLevel.GetTopLevel(this) as Window);
                if (string.IsNullOrEmpty(path)) return;
                byte[] fileData = File.ReadAllBytes(path);
                PaletteFormat fmt = PaletteFormatConverter.DetectFormat(fileData, System.IO.Path.GetExtension(path));
                byte[] palData = (fmt == PaletteFormat.GbaRaw) ? fileData : PaletteFormatConverter.ImportFromFormat(fileData, fmt);
                // TSA Anime v1 palette is the WinForms 8-bank raw palette (exactly 256 bytes).
                // Repointing P4 to a 16-color or otherwise mis-sized blob would leave
                // rendering/export reading unrelated following bytes — require the full size.
                if (palData.Length != PALETTE_BYTES) { CoreState.Services.ShowError($"Palette must be exactly {PALETTE_BYTES} bytes (8 banks x 16 colors)."); return; }
                uint addr = _vm.CurrentAddr;
                // Palette is RAW in ROM, NOT LZ77 — write uncompressed at +4 to match
                // WinForms ImageTSAAnimeForm (WriteImageData(P4,...,false)) (#1457).
                uint palAddr = ImageImportCore.WriteRawToROM(rom, palData, addr + 4);
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
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
