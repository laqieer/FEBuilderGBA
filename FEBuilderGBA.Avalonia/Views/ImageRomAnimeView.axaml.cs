using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageRomAnimeView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageRomAnimeViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "In-ROM Magic Animation";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public ImageRomAnimeView()
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
                Log.Error("ImageRomAnimeView.LoadList failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint id)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(id);
                UpdateUI();
                UpdateFrameRange();
                LoadImage();
            }
            catch (Exception ex)
            {
                Log.Error("ImageRomAnimeView.OnSelected failed: " + ex.ToString());
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            IdLabel.Text = U.ToHexString(_vm.CurrentId);
            InfoLabel.Text = _vm.Info;
        }

        void UpdateFrameRange()
        {
            // Spinner range = 0..FrameCount-1 (Maximum is inclusive in Avalonia).
            int max = Math.Max(_vm.FrameCount - 1, 0);
            FrameUpDown.Maximum = max;
            FrameUpDown.Value = 0;
        }

        void FrameUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            try
            {
                _vm.CurrentFrame = (int)(FrameUpDown.Value ?? 0);
                LoadImage();
            }
            catch (Exception ex)
            {
                Log.Error("ImageRomAnimeView.FrameUpDown_ValueChanged failed: " + ex.ToString());
            }
        }

        void LoadImage()
        {
            try
            {
                using IImage? img = _vm.TryLoadImage();
                FrameImage.SetImage(img);
            }
            catch (Exception ex)
            {
                Log.Error("ImageRomAnimeView.LoadImage failed: " + ex.ToString());
                FrameImage.SetImage(null);
            }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                await FrameImage.ExportPng(this,
                    "romanime_" + U.ToHexString(_vm.CurrentId) + "_f" + _vm.CurrentFrame + ".png");
            }
            catch (Exception ex)
            {
                Log.Error("ImageRomAnimeView.ExportPng_Click failed: " + ex.ToString());
            }
        }

        async void ImportPng_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded || _vm.Entry == null)
                {
                    CoreState.Services?.ShowError(R._("No animation entry selected."));
                    return;
                }

                string? path = await FileDialogHelper.OpenImageFile(this);
                if (string.IsNullOrEmpty(path)) return;

                int width = _vm.FrameWidthPx;
                // Load + quantize to <=16 colors. strictSize:false so any height is
                // accepted here; the Core ImportFrame re-validates dims exactly.
                var load = ImageImportService.LoadAndQuantizeFromFile(path, width, 0, 16, strictSize: false);
                if (load == null) return;
                if (!load.Success)
                {
                    CoreState.Services?.ShowError(load.Error);
                    return;
                }

                _undoService.Begin("Import In-ROM Animation Frame");
                try
                {
                    string err = _vm.Import(load.IndexedPixels, load.GBAPalette, load.Width, load.Height);
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    // Re-resolve the entry (pointers changed) + refresh preview.
                    _vm.IsLoading = true;
                    try { _vm.LoadEntry(_vm.CurrentId); UpdateUI(); UpdateFrameRange(); }
                    finally { _vm.IsLoading = false; }
                    LoadImage();
                    _vm.MarkClean();
                    CoreState.Services?.ShowInfo(R._("Animation frame imported successfully."));
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error("ImageRomAnimeView.ImportPng_Click write failed: " + ex.ToString());
                    CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImageRomAnimeView.ImportPng_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
