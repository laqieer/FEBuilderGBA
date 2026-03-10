using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemIconViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemIconViewerViewModel _vm = new();

        public string ViewTitle => "Item/Weapon Icon Viewer";
        public bool IsLoaded => _vm.CanWrite;

        public ItemIconViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadItemIconList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("ItemIconViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemIcon(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("ItemIconViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ImgPtrLabel.Text = $"0x{_vm.ImagePointer:X08}";
            PalPtrLabel.Text = $"0x{_vm.PalettePointer:X08}";
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        async void ExportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "item_icon.png");
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                byte[] pal = _vm.CachedPalette;
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("No palette loaded"); return; }
                string? path = await FileDialogHelper.SavePaletteFile(this, "item_icon_palette.pal");
                if (string.IsNullOrEmpty(path)) return;
                PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(path));
                File.WriteAllBytes(path, PaletteFormatConverter.ExportToFormat(pal, fmt));
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export palette failed: {ex.Message}"); }
        }

        async void ImportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (_vm.CachedPalette == null)
                {
                    CoreState.Services.ShowError("No palette loaded. Select an icon first.");
                    return;
                }

                // Remap to existing shared palette instead of quantizing a new one
                var loadResult = await ImageImportService.LoadAndRemapToExistingPalette(
                    this, 16, 16, _vm.CachedPalette, 16, strictSize: true);
                if (loadResult == null) return;
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                bool ok = ImageImportCore.ImportFixedIcon(rom, loadResult.IndexedPixels, 16, 16, _vm.CurrentAddr);
                if (!ok) { CoreState.Services.ShowError("Failed to write icon data"); return; }

                _vm.LoadItemIcon(_vm.CurrentAddr);
                LoadImage();
                CoreState.Services.ShowInfo("Icon imported successfully.");
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Import failed: {ex.Message}"); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
