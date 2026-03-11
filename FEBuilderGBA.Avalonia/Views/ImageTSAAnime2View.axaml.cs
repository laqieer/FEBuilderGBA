using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageTSAAnime2View : Window, IEditorView
    {
        readonly ImageTSAAnime2ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "TSA Animation Editor v2";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageTSAAnime2View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
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
                Log.Error("ImageTSAAnime2View.LoadList failed: {0}", ex.Message);
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
            }
            catch (Exception ex)
            {
                Log.Error("ImageTSAAnime2View.OnSelected failed: {0}", ex.Message);
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
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("ImageTSAAnime2View.Write: {0}", ex.Message); }
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

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var loadResult = await ImageImportService.LoadAndQuantize(this, 240, 160, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                _undoService.Begin("Import TSA Animation v2 Image");
                try
                {
                    uint addr = _vm.CurrentAddr;
                    // TSA Animation v2 only has TSA header pointer at offset 8.
                    // Write compressed TSA data to ROM and update the pointer.
                    var tsaResult = ImageImportCore.EncodeTSA(loadResult.IndexedPixels, loadResult.Width, loadResult.Height);
                    if (tsaResult == null) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to encode TSA data"); return; }

                    uint tsaAddr = ImageImportCore.WriteCompressedToROM(rom, tsaResult.TSAData, addr + 8);
                    if (tsaAddr == U.NOT_FOUND) { _undoService.Rollback(); CoreState.Services.ShowError("Failed to write TSA data (no free space)"); return; }

                    _undoService.Commit();
                    _vm.LoadEntry(addr);
                    UpdateUI();
                    _vm.MarkClean();
                    CoreState.Services.ShowInfo("TSA data imported successfully.");
                }
                catch (Exception ex) { _undoService.Rollback(); CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
