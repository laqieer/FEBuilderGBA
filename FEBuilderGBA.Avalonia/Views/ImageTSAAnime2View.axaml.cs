using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageTSAAnime2View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ImageTSAAnime2ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "TSA Animation Editor v2";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("TSA Animation Editor v2", 1383, 766, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public ImageTSAAnime2View()
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
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                // Log.Error joins its params with spaces (no composite format) —
                // interpolate so the message isn't logged as a literal "{0}".
                Log.Error($"ImageTSAAnime2View.LoadList failed: {ex}");
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex)
            {
                Log.Error($"ImageTSAAnime2View.OnSelected failed: {ex}");
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            Unknown0Box.Value = _vm.Unknown0;
            Unknown2Box.Value = _vm.Unknown2;
            Unknown4Box.Value = _vm.Unknown4;
            Unknown6Box.Value = _vm.Unknown6;
            TSAHeaderPointerBox.Text = $"0x{_vm.TSAHeaderPointer:X08}";
        }

        void LoadImage()
        {
            try { ImageDisplay.SetImage(_vm.TryLoadImage()); }
            catch { ImageDisplay.SetImage(null); }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit TSA Animation v2");
            try
            {
                _vm.Unknown0 = (uint)(Unknown0Box.Value ?? 0);
                _vm.Unknown2 = (uint)(Unknown2Box.Value ?? 0);
                _vm.Unknown4 = (uint)(Unknown4Box.Value ?? 0);
                _vm.Unknown6 = (uint)(Unknown6Box.Value ?? 0);
                _vm.TSAHeaderPointer = ParseHexText(TSAHeaderPointerBox.Text);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                // A manual Write can change the TSA pointer; refresh the preview
                // so it doesn't show stale data until the entry is reselected.
                LoadImage();
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"ImageTSAAnime2View.Write: {ex}"); }
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        // WinForms ImageTSAAnime2Form renders the entry's TSA at a 32-tile
        // (256px) stride, with the IMAGE/PALETTE/TSA stored as a coupled trio off
        // the shared header. Imported PNGs are loaded at 256x160; a 240-wide PNG
        // is accepted and right-padded to 256 (the WF v1 form auto-inserts the
        // 2-tile right margin the same way).
        const int IMPORT_WIDTH = 256;
        const int IMPORT_HEIGHT = 160;

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Accept either 256x160 (native) or 240x160 (right-padded to 256).
                var loadResult = await ImageImportService.LoadAndQuantize(TopLevel.GetTopLevel(this) as Window, IMPORT_WIDTH, IMPORT_HEIGHT, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }
                if (loadResult.Height != IMPORT_HEIGHT ||
                    (loadResult.Width != IMPORT_WIDTH && loadResult.Width != 240))
                {
                    CoreState.Services.ShowError(
                        $"Image must be {IMPORT_WIDTH}x{IMPORT_HEIGHT} or 240x{IMPORT_HEIGHT} pixels (got {loadResult.Width}x{loadResult.Height})");
                    return;
                }

                ROM rom = CoreState.ROM;
                if (rom == null) return;
                if (!_vm.IsLoaded || _vm.CurrentAddr < ImageTSAAnime2ViewModel.HEADER_SIZE)
                {
                    CoreState.Services.ShowError("Select a TSA Animation v2 entry first.");
                    return;
                }

                uint addr = _vm.CurrentAddr;
                uint headerBase = _vm.HeaderBase;
                // Range-check the SHARED header + the selected entry. The image/
                // palette slots live in the category header (shared across all
                // entries); the TSA slot lives in the selected 12-byte entry
                // (addr+8), which #1456 keeps correct for entry[i>0] because
                // HeaderBase resolves the category base, not addr - HEADER_SIZE.
                if (!U.isSafetyOffset(headerBase, rom) ||
                    headerBase + ImageTSAAnime2ViewModel.HEADER_SIZE > (uint)rom.Data.Length ||
                    addr + 12 > (uint)rom.Data.Length)
                {
                    CoreState.Services.ShowError("Entry address is out of range.");
                    return;
                }

                // Right-pad a 240-wide source to the 256px (32-tile) header stride.
                byte[] indexed = ImageTSAAnime2ViewModel.PadIndexedWidth(
                    loadResult.IndexedPixels, loadResult.Width, IMPORT_HEIGHT, IMPORT_WIDTH);

                _undoService.Begin("Import TSA Animation v2 Image");
                try
                {
                    // Coupled trio (mirrors WinForms ImageTSAAnime2Form layout):
                    //   image  (LZ77)            @ headerBase + 16 (shared)
                    //   palette(raw 0x20)        @ headerBase + 4  (shared)
                    //   TSA    (raw header-wrap)  @ addr + 8 (selected entry TSA slot)
                    var importResult = ImageImportCore.Import3PointerHeaderTSA(
                        rom, indexed, loadResult.GBAPalette,
                        IMPORT_WIDTH, IMPORT_HEIGHT,
                        _vm.ImagePointerAddr, _vm.TSAPointerAddr, _vm.PalettePointerAddr);
                    if (!importResult.Success)
                    {
                        _undoService.Rollback();
                        CoreState.Services.ShowError(importResult.Error);
                        return;
                    }

                    _undoService.Commit();
                    _vm.LoadEntry(addr);
                    UpdateUI();
                    LoadImage();
                    _vm.MarkClean();
                    CoreState.Services.ShowInfo("Image imported successfully.");
                }
                catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
