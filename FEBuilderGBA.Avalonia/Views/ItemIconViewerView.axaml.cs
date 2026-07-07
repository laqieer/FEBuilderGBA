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
    public partial class ItemIconViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        readonly ItemIconViewerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Item/Weapon Icon Viewer";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Item/Weapon Icon Viewer", 749, 358, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public ItemIconViewerView()
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
            try { var items = _vm.LoadItemIconList(); EntryList.SetItemsWithIcons(items, i => ListIconLoaders.DirectItemIconLoader(items, i)); }
            catch (Exception ex) { Log.ErrorF("ItemIconViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItemIcon(addr);
                UpdateUI();
                LoadImage();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemIconViewerView.OnSelected: {0}", ex.Message);
            }
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
            await ImageDisplay.ExportPng(TopLevel.GetTopLevel(this) as Window, "item_icon.png");
        }

        async void ExportPal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                byte[] pal = _vm.CachedPalette;
                if (pal == null || pal.Length < 32) { CoreState.Services.ShowError("No palette loaded"); return; }
                await FileDialogHelper.SavePaletteFileVia(TopLevel.GetTopLevel(this) as Window, "item_icon_palette.pal", p =>
                {
                    // #1639: write via the SAF bridge so Android content:// targets work.
                    PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(p));
                    File.WriteAllBytes(p, PaletteFormatConverter.ExportToFormat(pal, fmt));
                });
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export palette failed: {ex.Message}"); }
        }

        async void ImportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_vm.CachedPalette == null)
            {
                CoreState.Services.ShowError("No palette loaded. Select an icon first.");
                return;
            }
            // Remap to existing shared palette instead of quantizing a new one
            var loadResult = await ImageImportService.LoadAndRemapToExistingPalette(
                TopLevel.GetTopLevel(this) as Window, 16, 16, _vm.CachedPalette, 16, strictSize: true);
            if (loadResult == null) return; // cancelled
            RunIconImport(loadResult);
        }

        // #1380 Part B — FE-Repo button: same as Import PNG, sourced from the
        // FE-Repo "Item Icons" folder. Routes through the SAME import path; a
        // non-16x16 asset fails gracefully with the existing strict-size error.
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.CachedPalette == null)
            {
                CoreState.Services.ShowError("No palette loaded. Select an icon first.");
                return;
            }
            string? path = await FERepoPickHelper.PickForEditor(TopLevel.GetTopLevel(this) as Window,
                FERepoResourceBrowser.FERepoEditorKind.ItemIcon);
            if (string.IsNullOrEmpty(path)) return;

            var loadResult = ImageImportService.LoadAndRemapFromFile(
                path, 16, 16, _vm.CachedPalette, 16, strictSize: true);
            RunIconImport(loadResult);
        }

        void RunIconImport(ImageImportService.LoadResult loadResult)
        {
            try
            {
                if (!loadResult.Success) { CoreState.Services.ShowError(loadResult.Error); return; }

                ROM rom = CoreState.ROM;
                if (rom == null) return;

                _undoService.Begin("Import Item Icon");
                try
                {
                    bool ok = ImageImportCore.ImportFixedIcon(rom, loadResult.IndexedPixels, 16, 16, _vm.CurrentAddr);
                    if (!ok)
                    {
                        _undoService.Rollback();
                        CoreState.Services.ShowError("Failed to write icon data");
                        return;
                    }
                    _undoService.Commit();
                }
                catch
                {
                    _undoService.Rollback();
                    throw;
                }

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
