using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageBGView : Window, IEditorView
    {
        readonly ImageBGViewModel _vm = new();

        public string ViewTitle => "Background Image Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageBGView()
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
                Log.Error("ImageBGView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ImageBGView.OnSelected failed: {0}", ex.Message);
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
                var loadResult = await ImageImportService.LoadAndQuantize(this, 240, 160, 16);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                uint addr = _vm.CurrentAddr;
                // P0=image, P4=palette, P8=TSA
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
            await ImageDisplay.ExportPng(this, "background.png");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
