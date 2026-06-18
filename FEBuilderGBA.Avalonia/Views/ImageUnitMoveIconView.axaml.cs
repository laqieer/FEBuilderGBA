using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageUnitMoveIconView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ImageUnitMoveIconViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressPreviewRefresh;
        bool _initialized;

        public string ViewTitle => "Unit Move Icon";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageUnitMoveIconView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();

            // Comment lost-focus → save (mirrors ImageUnitWaitIconView).
            CommentBox.LostFocus += (_, _) => _vm.SaveComment(CommentBox.Text ?? string.Empty);

            // Default to the self-army palette; do it AFTER InitializeComponent
            // so the SelectionChanged handler doesn't fire mid-construction.
            PaletteCombo.SelectedIndex = 0;
            _initialized = true;
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.MoveIconLoader(items, i));
                PopulateApCombo();
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitMoveIconView.LoadList failed: " + ex.ToString());
            }
        }

        // Populate the AP-pattern combo from the resource catalog (#1226). Items
        // are RESOURCE DATA names, so they are bound here (not translatable UI).
        void PopulateApCombo()
        {
            _suppressPreviewRefresh = true;
            try
            {
                ApPatternCombo.ItemsSource = _vm.ApCatalogNames;
                ApPatternCombo.SelectedItem = null;
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitMoveIconView.PopulateApCombo failed: " + ex.ToString());
            }
            finally
            {
                _suppressPreviewRefresh = false;
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
                Log.Error("ImageUnitMoveIconView.OnSelected failed: " + ex.ToString());
            }
        }

        void UpdateUI()
        {
            _suppressPreviewRefresh = true;
            try
            {
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                P0Box.Value = _vm.P0;
                P4Box.Value = _vm.P4;
                StepBox.Value = _vm.Step;
                CommentBox.Text = _vm.Comment;
                ExportAPButton.IsEnabled = _vm.HasAp();
                ImportAPButton.IsEnabled = _vm.CurrentAddr != 0;

                // AP MD5 selector (#1226): reflect the CURRENT entry's matched
                // pattern. Selecting it here is programmatic (the suppress flag
                // is set), so ApPattern_Changed is a no-op for this assignment.
                string matched = _vm.CurrentApName;
                ApPatternCombo.SelectedItem = string.IsNullOrEmpty(matched) ? null : matched;
                ApMatchedLabel.Text = matched;
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
            // copies its pixels into a WriteableBitmap; dispose the temporary
            // right after so the unmanaged SKBitmap is released immediately
            // rather than waiting for GC (frequent palette/step re-renders would
            // otherwise balloon memory — #993 lesson).
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
            RefreshPreviews();
        }

        void Step_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!_initialized || _suppressPreviewRefresh) return;
            _vm.Step = (int)(StepBox.Value ?? 0);
            RefreshFramePreview();
        }

        // AP MD5 selector (#1226): user picked a known AP pattern. Resolve it to
        // an EXISTING ROM AP region and re-point this entry's P4. ROM-mutating, so
        // it runs under an ambient undo scope (single P4 u32 write). The resolve
        // is READ-ONLY and validates BEFORE any mutation — an unresolvable pattern
        // surfaces the error and writes nothing (mirrors WF SelectAPAddresssFromAPCombo).
        void ApPattern_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (!_initialized || _suppressPreviewRefresh) return;

            string? name = ApPatternCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;
            if (_vm.CurrentAddr == 0) return;

            _undoService.Begin("Select Unit Move Icon AP");
            try
            {
                string err = _vm.ApplyApByName(name);
                if (!string.IsNullOrEmpty(err))
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(err);
                    // Restore the combo to the entry's actual matched pattern.
                    RefreshApSelectionFromVm();
                    return;
                }

                // ApplyApByName changed _vm.P4 in memory; persist via Write().
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                UpdateUI(); // refresh P4 box, matched label, previews
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services?.ShowError($"AP select failed: {ex.Message}");
                RefreshApSelectionFromVm();
            }
        }

        // Re-sync the combo selection + matched label to the VM's current AP
        // (used to roll the UI back after a failed/aborted AP selection).
        void RefreshApSelectionFromVm()
        {
            _suppressPreviewRefresh = true;
            try
            {
                string matched = _vm.CurrentApName;
                ApPatternCombo.SelectedItem = string.IsNullOrEmpty(matched) ? null : matched;
                ApMatchedLabel.Text = matched;
            }
            finally
            {
                _suppressPreviewRefresh = false;
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Unit Move Icon");
            try
            {
                _vm.P0 = (uint)(P0Box.Value ?? 0);
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
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError(R._("No move icon selected.")); return; }

                // The move-icon entry has NO palette slot — remap the imported
                // sheet onto the shared self-army unit palette (nearest color),
                // the WaitIcon contract (#991) and the WF CheckPalette/ForcePalette
                // equivalent.
                byte[] selfPalette = ImageUtilCore.GetPalette(rom.RomInfo.unit_icon_palette_address, 16);
                if (selfPalette == null) { CoreState.Services?.ShowError(R._("Failed to read unit icon palette.")); return; }

                var result = await ImageImportService.LoadAndRemapToExistingPalette(this, 0, 0, selfPalette, 16);
                if (result == null) return; // cancelled
                if (!result.Success) { CoreState.Services?.ShowError(result.Error); return; }

                // Normalize to the WF target sheet 32x480 (4*8 x 60*8) if needed:
                // WF ConvertSizeFormat reflows any 8-aligned sheet into 32-wide
                // rows. Here we only accept a 32-wide sheet (the natural sheet);
                // a non-32-wide source is rejected with the WF error.
                if (result.Width != UnitMoveIconImportCore.SHEET_WIDTH)
                {
                    CoreState.Services?.ShowError(
                        R._("The image width must be {0} pixels (got {1}).",
                            UnitMoveIconImportCore.SHEET_WIDTH, result.Width));
                    return;
                }

                uint addr = _vm.CurrentAddr;
                _undoService.Begin("Import Unit Move Icon");
                // WriteCompressedToROM OWNS the +0 (P0) pointer slot; do NOT call
                // _vm.Write() afterwards (that would re-write a stale P0).
                string err = UnitMoveIconImportCore.Import(rom, addr, result.IndexedPixels, result.Width, result.Height);
                if (!string.IsNullOrEmpty(err))
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(err);
                    return;
                }
                _undoService.Commit();
                _vm.ReloadEntry(); // refresh P0 / previews
                UpdateUI();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo(R._("Image imported successfully."));
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
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError(R._("No move icon selected.")); return; }

                string suggested = $"move_icon_{_vm.CurrentIndex:X02}.png";
                string? path = await FileDialogHelper.SaveFile(this, "Export Move Icon",
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

                if (!ok) { CoreState.Services?.ShowError(R._("Failed to render move icon for export.")); return; }
                CoreState.Services?.ShowInfo($"Exported to: {path}");
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError($"Export failed: {ex.Message}");
            }
        }

        async void ImportAP_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) return;
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError(R._("No move icon selected.")); return; }

                string? path = await FileDialogHelper.OpenFile(this, "Import AP", "*.romtcs.ap.bin");
                if (string.IsNullOrEmpty(path)) return;

                byte[] apBytes;
                try { apBytes = File.ReadAllBytes(path); }
                catch (Exception ex)
                {
                    CoreState.Services?.ShowError($"Failed to read AP file: {ex.Message}");
                    return;
                }
                if (apBytes.Length == 0) { CoreState.Services?.ShowError(R._("No AP data.")); return; }

                uint addr = _vm.CurrentAddr;

                // WF warns when the OLD AP is shared by other classes. Here
                // ImportAP always appends fresh (the old region stays intact for
                // its other referencers), so this is purely informational.
                if (_vm.IsApShared())
                    CoreState.Services?.ShowInfo(R._("This AP is shared by other classes; a fresh copy will be written."));

                _undoService.Begin("Import Unit Move Icon AP");
                // WriteRawToROM OWNS the +4 (P4) pointer slot; do NOT call
                // _vm.Write() afterwards.
                string err = UnitMoveIconImportCore.ImportAP(rom, addr, apBytes);
                if (!string.IsNullOrEmpty(err))
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(err);
                    return;
                }
                _undoService.Commit();
                _vm.ReloadEntry(); // refresh P4
                UpdateUI();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo(R._("AP imported successfully."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services?.ShowError($"AP import failed: {ex.Message}");
            }
        }

        async void ExportAP_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.ROM == null) return;
                if (_vm.CurrentAddr == 0) { CoreState.Services?.ShowError(R._("No move icon selected.")); return; }

                byte[] ap = _vm.ReadApBytes();
                if (ap == null || ap.Length == 0) { CoreState.Services?.ShowError(R._("No AP data to export.")); return; }

                string suggested = $"move_icon_{_vm.CurrentIndex:X02}.romtcs.ap.bin";
                string? path = await FileDialogHelper.SaveFile(this, "Export AP",
                    new[] { ("AP", "*.romtcs.ap.bin") }, suggested);
                if (string.IsNullOrEmpty(path)) return;

                File.WriteAllBytes(path, ap);
                CoreState.Services?.ShowInfo($"Exported to: {path}");
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError($"AP export failed: {ex.Message}");
            }
        }

        void JumpWaitIcon_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // VM owns the move-icon-row → class → wait-icon-table-entry
                // resolution. null => no owning class / no wait icon / out-of-range.
                uint? entryAddr = _vm.ResolveWaitIconEntryAddress();
                if (entryAddr == null) return;

                WindowManager.Instance.Navigate<ImageUnitWaitIconView>(entryAddr.Value);
            }
            catch (Exception ex)
            {
                Log.Error("ImageUnitMoveIconView.JumpWaitIcon_Click: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
