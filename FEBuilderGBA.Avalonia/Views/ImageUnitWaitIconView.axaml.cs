using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageUnitWaitIconView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageUnitWaitIconViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressPreviewRefresh;
        bool _initialized;

        public string ViewTitle => "Unit Wait Icon";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageUnitWaitIconView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();

            // Comment lost-focus → save (mirrors ImageBattleBGView).
            CommentBox.LostFocus += (_, _) => _vm.SaveComment(CommentBox.Text ?? string.Empty);

            // Default to the self-army palette; do it AFTER InitializeComponent
            // (and with the init guard already armed by the handler) so the
            // SelectionChanged handler doesn't fire mid-construction.
            PaletteCombo.SelectedIndex = 0;
            _initialized = true;
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.WaitIconDirectLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitWaitIconView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                // Reset to frame 0 on each new selection (mirrors WF
                // X_ONE_STEP.Value = 0 in AddressList_SelectedIndexChanged).
                _vm.Step = 0;
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitWaitIconView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            _suppressPreviewRefresh = true;
            try
            {
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                W0Box.Value = _vm.W0;
                W2Box.Value = _vm.W2;
                P4Box.Value = _vm.P4;
                StepBox.Value = _vm.Step;
                CommentBox.Text = _vm.Comment;
            }
            finally
            {
                _suppressPreviewRefresh = false;
            }
            RefreshPreviews();
        }

        void RefreshPreviews()
        {
            // Each Render* call returns a FRESH SkiaSharp-backed IImage. SetImage
            // copies its pixels into a WriteableBitmap (IconBitmapBuilder.FromImage
            // reads GetPixelData/GetPaletteRGBA — it does NOT retain the IImage),
            // so dispose the temporary right after to release the unmanaged
            // SKBitmap immediately rather than waiting for GC (#993 Copilot
            // review — frequent palette/step re-renders would otherwise balloon
            // memory).
            try
            {
                using IImage img = _vm.RenderFullSheet();
                SheetImage.SetImage(img);
            }
            catch { SheetImage.SetImage(null); }
            RefreshFramePreview();
        }

        void RefreshFramePreview()
        {
            try
            {
                using IImage img = _vm.RenderFrame();
                FrameImage.SetImage(img);
            }
            catch { FrameImage.SetImage(null); }
        }

        void Palette_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || _suppressPreviewRefresh) return;
            _vm.PaletteType = PaletteCombo.SelectedIndex < 0 ? 0 : PaletteCombo.SelectedIndex;
            // Both previews depend on the palette.
            RefreshPreviews();
        }

        void Step_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!_initialized || _suppressPreviewRefresh) return;
            _vm.Step = (int)(StepBox.Value ?? 0);
            RefreshFramePreview();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Unit Wait Icon");
            try
            {
                _vm.W0 = (ushort)(W0Box.Value ?? 0);
                _vm.W2 = (ushort)(W2Box.Value ?? 0);
                _vm.P4 = (uint)(P4Box.Value ?? 0);
                _vm.SaveComment(CommentBox.Text ?? string.Empty);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                RefreshPreviews();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services?.ShowError($"Write failed: {ex.Message}");
            }
        }

        async void Import_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError("No wait icon selected."); return; }

            byte[] selfPalette = ReadSelfPalette(rom);
            if (selfPalette == null) return;

            var result = await ImageImportService.LoadAndRemapToExistingPalette(this, 0, 0, selfPalette, 16);
            if (result == null) return; // cancelled
            RunWaitIconImport(rom, result);
        }

        // #1380 Part B — FE-Repo button: behaves exactly like Import, but the
        // source file comes from the FE-Repo "Map Sprites" folder instead of an
        // OS file picker. Routes through the SAME WaitIconImportCore path, so a
        // wrong-size asset fails gracefully with the existing dimension error.
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError("No wait icon selected."); return; }

            byte[] selfPalette = ReadSelfPalette(rom);
            if (selfPalette == null) return;

            string? path = await FERepoPickHelper.PickForEditor(this,
                FERepoResourceBrowser.FERepoEditorKind.UnitWaitIcon);
            if (string.IsNullOrEmpty(path)) return;

            var result = ImageImportService.LoadAndRemapFromFile(path, 0, 0, selfPalette, 16);
            RunWaitIconImport(rom, result);
        }

        byte[] ReadSelfPalette(ROM rom)
        {
            // The wait-icon entry has NO palette slot — remap the imported
            // sheet onto the shared self-army palette (nearest color), the
            // same tradeoff WF's CheckPalette/ForcePalette interactive dialog
            // resolves (plan v2 HIGH-1: remap, not quantize-to-fresh).
            byte[] selfPalette = ImageUtilCore.GetPalette(rom.RomInfo.unit_icon_palette_address, 16);
            if (selfPalette == null) { CoreState.Services?.ShowError("Failed to read unit icon palette."); return null; }
            return selfPalette;
        }

        void RunWaitIconImport(ROM rom, ImageImportService.LoadResult result)
        {
            try
            {
                if (!result.Success) { CoreState.Services?.ShowError(result.Error); return; }

                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import Unit Wait Icon");
                // WriteCompressedToROM OWNS the +4 pointer slot; do NOT call
                // _vm.Write() afterwards (that would re-write a stale P4).
                string err = WaitIconImportCore.Import(rom, addr, result.IndexedPixels, result.Width, result.Height);
                if (!string.IsNullOrEmpty(err))
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(err);
                    return;
                }
                _undoService.Commit();
                _vm.ReloadEntry(); // refresh P4 / W2 / previews
                UpdateUI();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Image imported successfully.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services?.ShowError($"Import failed: {ex.Message}");
            }
        }

        async void Export_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.ROM == null) return;
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError("No wait icon selected."); return; }

                string suggested = $"wait_icon_{_vm.CurrentIndex:X02}.png";
                string? path = await FileDialogHelper.SaveFile(this, "Export Wait Icon",
                    new[]
                    {
                        ("PNG Image", "*.png"),
                        ("Animated GIF", "*.gif"),
                    },
                    suggested);
                if (string.IsNullOrEmpty(path)) return;

                bool ok = path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                    ? _vm.ExportGif(path)
                    : _vm.ExportPng(path);

                if (!ok) { CoreState.Services?.ShowError("Failed to render wait icon for export."); return; }
                CoreState.Services?.ShowInfo($"Exported to: {path}");
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError($"Export failed: {ex.Message}");
            }
        }

        void JumpMoveIcon_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // VM owns the 1-based id -> 0-based Move Icon list-entry address
                // conversion (single source of truth, headless-tested). null =>
                // no owning class / no move icon / out-of-range.
                uint? entryAddr = _vm.ResolveMoveIconEntryAddress();
                if (entryAddr == null) return;

                WindowManager.Instance.Navigate<ImageUnitMoveIconView>(entryAddr.Value);
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitWaitIconView.JumpMoveIcon_Click: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
