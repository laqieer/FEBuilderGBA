using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageRomAnimeView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ImageRomAnimeViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "In-ROM Magic Animation";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("In-ROM Magic Animation", 820, 520, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ImageRomAnimeView()
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
                await FrameImage.ExportPng(TopLevel.GetTopLevel(this) as Window,
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

                string? path = await FileDialogHelper.OpenImageFile(TopLevel.GetTopLevel(this) as Window);
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

        async void ExportScript_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded || _vm.Entry == null)
                {
                    CoreState.Services?.ShowError(R._("No animation entry selected."));
                    return;
                }
                // #1639: ExportScript writes per-frame sibling PNGs next to the
                // script, so it needs a real local path. SaveAnimationScriptFile
                // returns null on Android SAF (no local path) → disable with a
                // clear message instead of silently returning. (Use Export GIF for
                // a single-file animation export on Android.)
                string? path = await FileDialogHelper.SaveAnimationScriptFile(TopLevel.GetTopLevel(this) as Window,
                    "romanime_" + U.ToHexString(_vm.CurrentId) + ".txt");
                if (string.IsNullOrEmpty(path))
                {
                    if (OperatingSystem.IsAndroid())
                        CoreState.Services?.ShowError(R._("Exporting an animation script writes sibling PNG frames and requires desktop file-system access; export as GIF instead, or use a desktop device."));
                    return;
                }

                string err = _vm.ExportScript(path);
                if (!string.IsNullOrEmpty(err)) { CoreState.Services?.ShowError(err); return; }
                CoreState.Services?.ShowInfo(R._("Animation script exported successfully."));
            }
            catch (Exception ex)
            {
                Log.Error("ImageRomAnimeView.ExportScript_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Export failed: {0}", ex.Message));
            }
        }

        async void ExportGif_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded || _vm.Entry == null)
                {
                    CoreState.Services?.ShowError(R._("No animation entry selected."));
                    return;
                }
                // #1639: single-file GIF export → SAF bridge.
                string err = "";
                string? written = await FileDialogHelper.SaveFileVia(TopLevel.GetTopLevel(this) as Window,
                    R._("Save Animation GIF"), R._("Animated GIF (.gif)"), "*.gif",
                    "romanime_" + U.ToHexString(_vm.CurrentId) + ".gif",
                    p => { err = _vm.ExportGif(p); });
                if (written == null) return;
                if (!string.IsNullOrEmpty(err)) { CoreState.Services?.ShowError(err); return; }
                CoreState.Services?.ShowInfo(R._("Animation GIF exported successfully."));
            }
            catch (Exception ex)
            {
                Log.Error("ImageRomAnimeView.ExportGif_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Export failed: {0}", ex.Message));
            }
        }

        async void ImportScript_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded || _vm.Entry == null)
                {
                    CoreState.Services?.ShowError(R._("No animation entry selected."));
                    return;
                }
                // #1639: ImportScript resolves sibling frame PNGs from the
                // script's own directory, so require a real local path; a SAF pick
                // (no local path) cannot resolve siblings → message on Android.
                string? path = await FileDialogHelper.OpenFile(TopLevel.GetTopLevel(this) as Window,
                    R._("Open Animation Script"), "*.txt", requireLocalPath: true);
                if (string.IsNullOrEmpty(path))
                {
                    if (OperatingSystem.IsAndroid())
                        CoreState.Services?.ShowError(R._("Importing an animation script reads sibling PNG frames and requires desktop file-system access; it is not available on this device."));
                    return;
                }

                int width = _vm.FrameWidthPx;
                // Per-PNG loader: quantize each frame image to <=16 colors (the per-frame
                // import path). strictSize:false — Core crops/pads to the canvas.
                (byte[] indexedPixels, byte[] gbaPalette16, int width, int height)? Loader(string pngPath)
                {
                    var load = ImageImportService.LoadAndQuantizeFromFile(pngPath, width, 0, 16, strictSize: false);
                    if (load == null || !load.Success) return null;
                    return (load.IndexedPixels, load.GBAPalette, load.Width, load.Height);
                }

                _undoService.Begin("Import In-ROM Animation Script");
                try
                {
                    string err = _vm.ImportScript(path, Loader);
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
                    CoreState.Services?.ShowInfo(R._("Animation script imported successfully."));
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    Log.Error("ImageRomAnimeView.ImportScript_Click write failed: " + ex.ToString());
                    CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImageRomAnimeView.ImportScript_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Import failed: {0}", ex.Message));
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
