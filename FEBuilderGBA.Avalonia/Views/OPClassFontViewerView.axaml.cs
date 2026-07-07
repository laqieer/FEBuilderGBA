using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassFontViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly OPClassFontViewerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "OP Class Font Editor";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("OP Class Font Editor", 1179, 475, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public OPClassFontViewerView()
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
            // #939: rows are font-image pointers, NOT classes — the prefix is
            // the row index, so the old class-icon loader showed a spurious
            // icon. Drop the icon column entirely.
            try { var items = _vm.LoadOPClassFontList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error($"OPClassFontViewerView.LoadList: {ex.Message}"); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadOPClassFont(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error($"OPClassFontViewerView.OnSelected: {ex.Message}"); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrBox.Text = $"0x{_vm.ImagePointer:X08}";
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
            _undoService.Begin("Edit OP Class Font");
            try
            {
                _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
                _vm.WriteOPClassFont();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("OP Class Font data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"OPClassFontViewerView.Write: {ex.Message}"); }
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError("No glyph selected."); return; }

                // Shared op_class_font_palette (used by ALL glyphs) — remap onto it, not
                // quantize-to-fresh. Read via the rom-aware overload with pointer guards.
                uint palPtrAddr = rom.RomInfo.op_class_font_palette_pointer;
                if (!U.isSafetyOffset(palPtrAddr, rom) || palPtrAddr + 4 > (uint)rom.Data.Length)
                { CoreState.Services?.ShowError("OP class font palette pointer is invalid."); return; }
                uint palOff = U.toOffset(rom.p32(palPtrAddr));
                if (!U.isSafetyOffset(palOff, rom)) { CoreState.Services?.ShowError("OP class font palette is invalid."); return; }
                byte[] palette = ImageUtilCore.GetPalette(rom, palOff, 16);
                if (palette == null) { CoreState.Services?.ShowError("Failed to read OP class font palette."); return; }

                // Generic OP class font glyph is 32x32 (4x4 tiles). LoadAndRemapToExistingPalette
                // enforces the size + remaps onto the shared palette (nearest color).
                var result = await ImageImportService.LoadAndRemapToExistingPalette(TopLevel.GetTopLevel(this) as Window, 32, 32, palette, 16, strictSize: true);
                if (result == null) return;                              // cancelled
                if (!result.Success) { CoreState.Services?.ShowError(result.Error); return; }  // BEFORE the undo scope

                _undoService.Begin("Import OP Class Font Glyph");
                try
                {
                    // WriteCompressedToROM OWNS the D0 pointer at CurrentAddr — do NOT call
                    // WriteOPClassFont() afterward (it would re-point to a stale ImagePointer).
                    string err = OPClassFontImportCore.Import(rom, _vm.CurrentAddr, result.IndexedPixels, result.Width, result.Height);
                    if (!string.IsNullOrEmpty(err)) { _undoService.Rollback(); CoreState.Services?.ShowError(err); return; }
                    _undoService.Commit();
                    // Reload the entry so ImagePointer + preview refresh from the new D0 —
                    // mirrors OnSelected's refresh path (re-reads D0, repaints ImgPtrBox +
                    // the glyph preview).
                    _vm.LoadOPClassFont(_vm.CurrentAddr);
                    _vm.MarkClean();
                    UpdateUI();
                    LoadImage();
                    CoreState.Services?.ShowInfo("Glyph imported successfully.");
                }
                catch { _undoService.Rollback(); throw; }
            }
            catch (Exception ex) { CoreState.Services?.ShowError($"Import failed: {ex.Message}"); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(TopLevel.GetTopLevel(this) as Window, "op_class_font.png");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
