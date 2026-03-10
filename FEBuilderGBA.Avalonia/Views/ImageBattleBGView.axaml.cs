using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBattleBGView : Window, IEditorView
    {
        readonly ImageBattleBGViewModel _vm = new();

        public string ViewTitle => "Battle Background Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageBattleBGView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
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
                Log.Error("ImageBattleBGView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImageBattleBGView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImagePointerBox.Text = $"0x{_vm.ImagePointer:X08}";
            TSAPointerBox.Text = $"0x{_vm.TSAPointer:X08}";
            PalettePointerBox.Text = $"0x{_vm.PalettePointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ImagePointer = ParseHexText(ImagePointerBox.Text);
            _vm.TSAPointer = ParseHexText(TSAPointerBox.Text);
            _vm.PalettePointer = ParseHexText(PalettePointerBox.Text);
            _vm.Write();
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

                uint addr = _vm.CurrentAddr;
                var importResult = ImageImportCore.Import3Pointer(rom, loadResult.IndexedPixels, loadResult.GBAPalette,
                    loadResult.Width, loadResult.Height, addr + 0, addr + 4, addr + 8);

                if (!importResult.Success) { CoreState.Services.ShowError(importResult.Error); return; }

                _vm.LoadEntry(addr);
                UpdateUI();
                CoreState.Services.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
