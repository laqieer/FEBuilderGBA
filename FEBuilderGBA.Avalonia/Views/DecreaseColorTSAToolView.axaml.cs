using global::Avalonia;
using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Color Reduction Tool — file→file PNG color reducer (WF
    /// <c>DecreaseColorTSAToolForm</c>). NO ROM mutation, NO Undo, NO address
    /// list. Method/Size/Reserve combo items are added in code via R._() so they
    /// pick up ja/zh translations at runtime (ViewTranslationHelper does not
    /// translate ComboBoxItem content).
    /// </summary>
    public partial class DecreaseColorTSAToolView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly DecreaseColorTSAToolViewModel _vm = new();

        // #1639: the output browse stores a TARGET the Reduce button writes LATER.
        // On Android SAF retain the handle and write the produced image back
        // through it on Reduce.
        IStorageFile? _outputFile;

        public string ViewTitle => "Color Reduction Tool";
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new("Color Reduction Tool", 560, 520, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public DecreaseColorTSAToolView()
        {
            InitializeComponent();
            DataContext = _vm;

            // Method presets (index 0 = manual; 1..0xA = WF presets).
            MethodCombo.Items.Add(R._("0: Manual (no preset)"));
            MethodCombo.Items.Add(R._("1: BG & CG"));
            MethodCombo.Items.Add(R._("2: Battle BG"));
            MethodCombo.Items.Add(R._("3: World Map (large)"));
            MethodCombo.Items.Add(R._("4: World Map (event)"));
            MethodCombo.Items.Add(R._("5: 256-color no-TSA"));
            MethodCombo.Items.Add(R._("6: Status screen BG (FE8)"));
            MethodCombo.Items.Add(R._("7: Single-image map chips"));
            MethodCombo.Items.Add(R._("8: Single-image map chips (10 colors)"));
            MethodCombo.Items.Add(R._("9: BG 256-color no-TSA (cutscene)"));
            MethodCombo.Items.Add(R._("A: BG 224-color no-TSA (talk)"));

            // Size method: 0 = resize (crop/pad), 1 = scale.
            SizeMethodCombo.Items.Add(R._("Resize (crop/pad)"));
            SizeMethodCombo.Items.Add(R._("Scale"));

            // Reserve 1st color: 0 = no, 1 = yes.
            ReserveCombo.Items.Add(R._("No"));
            ReserveCombo.Items.Add(R._("Yes"));

            // Initial selection mirrors WF ctor (Method.SelectedIndex = 1).
            _vm.Method = 1;
            _vm.ApplyPreset(1);

            // Explicitly seed each combo's SelectedIndex AFTER items are added.
            // A TwoWay {Binding SelectedIndex} can stay at -1 (no selection) when
            // the bound value never changes from its default while the items are
            // still empty at binding-attach time — pushing it here guarantees the
            // combos display the VM's current values.
            MethodCombo.SelectedIndex = _vm.Method;
            SizeMethodCombo.SelectedIndex = _vm.SizeMethodIndex;
            ReserveCombo.SelectedIndex = _vm.ReserveIndex;

            MethodCombo.SelectionChanged += MethodCombo_SelectionChanged;
        }

        void MethodCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // _vm.Method is two-way bound to SelectedIndex; apply the preset on
            // user selection (method 0 is a deliberate no-op — see ApplyPreset).
            _vm.ApplyPreset(MethodCombo.SelectedIndex);
        }

        async void InputBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            try
            {
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Please select a file to open."),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType(R._("Images")) { Patterns = new[] { "*.png", "*.bmp", "*.jpg" } },
                        new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } },
                        new FilePickerFileType("BMP") { Patterns = new[] { "*.bmp" } },
                        new FilePickerFileType("JPG") { Patterns = new[] { "*.jpg" } },
                        new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } },
                    }
                });

                if (files.Count > 0)
                {
                    // #1639: ReduceColorFile reads the input image by path → bridge
                    // a SAF source (no local path) to a temp file that survives
                    // until the deferred Reduce run.
                    string? path = await FileDialogHelper.ResolveReadPathAsync(files[0]);
                    if (!string.IsNullOrEmpty(path))
                        _vm.InputPath = path;
                    else
                        _vm.StatusMessage = R._("Please select a valid input and output file.");
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("DecreaseColorTSAToolView.InputBrowse failed: {0}", ex.Message);
                _vm.StatusMessage = ex.Message;
            }
        }

        async void OutputBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            try
            {
                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Please select a file name to save."),
                    DefaultExtension = "png",
                    SuggestedFileName = "reduced.png",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } },
                        new FilePickerFileType("BMP") { Patterns = new[] { "*.bmp" } },
                        new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } },
                    }
                });

                if (file != null)
                {
                    // #1639: retain the handle; show the local path (desktop) or the
                    // SAF display name. The actual write happens on Reduce.
                    _outputFile = file;
                    _vm.OutputPath = file.TryGetLocalPath() ?? file.Name ?? "reduced.png";
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("DecreaseColorTSAToolView.OutputBrowse failed: {0}", ex.Message);
                _vm.StatusMessage = ex.Message;
            }
        }

        async void Reduce_Click(object? sender, RoutedEventArgs e)
        {
            // The reduce loads/scales/quantizes/saves an image, which can take a
            // while for the larger presets (e.g. the 1024×688 FE7 world map), so
            // run the Core work on a background thread to keep the UI responsive
            // and update the bindable status on the UI thread.
            ReduceButton.IsEnabled = false;
            _vm.StatusMessage = R._("Reducing colors...");
            try
            {
                int code;
                // #1639: on Android SAF the output is a content:// document with no
                // local path — run the reducer into a temp file and stream it back
                // through the handle.
                if (_outputFile != null && string.IsNullOrEmpty(_outputFile.TryGetLocalPath()))
                {
                    // OutputPath holds the user-facing display name; swap in the
                    // temp path only for the Core write, then RESTORE it so the
                    // status message shows the chosen name, not the temp path.
                    string displayLabel = _vm.OutputPath;
                    int c = -1;
                    await FileDialogHelper.WriteViaAsync(_outputFile, async p =>
                    {
                        _vm.OutputPath = p;
                        c = await Task.Run(() => _vm.RunReduce());
                    });
                    _vm.OutputPath = displayLabel;
                    code = c;
                }
                else
                {
                    code = await Task.Run(() => _vm.RunReduce());
                }
                _vm.SetReduceStatus(code);
            }
            catch (Exception ex)
            {
                // Keep the low-level/English-only detail in the log; show the
                // user the standard localized failure status (SetReduceStatus(-1)).
                Log.ErrorF("DecreaseColorTSAToolView.Reduce failed: {0}", ex.Message);
                _vm.SetReduceStatus(-1);
            }
            finally
            {
                ReduceButton.IsEnabled = true;
            }
        }

        // IEditorView — this tool has no navigable address list; these are no-ops.
        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }

        /// <summary>
        /// Pre-select the Color Reduce method and APPLY its preset. Mirrors the
        /// WinForms <c>DecreaseColorTSAToolForm.InitMethod(int)</c> entry point
        /// used by callers (ImageBGView → 1, ImageBattleBGView → 2,
        /// WorldMapImageView → 3 main / 4 event) so the dialog opens populated
        /// for the caller's mode.
        /// </summary>
        /// <param name="methodIndex">WF method index (0 = manual, 1..0xA = preset).</param>
        public void InitMethod(int methodIndex)
        {
            if (methodIndex < 0 || methodIndex >= MethodCombo.ItemCount)
                return;

            _vm.Method = methodIndex;       // two-way bound → updates the combo
            MethodCombo.SelectedIndex = methodIndex;
            _vm.ApplyPreset(methodIndex);
        }
    }
}
