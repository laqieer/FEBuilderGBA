// SPDX-License-Identifier: GPL-3.0-or-later
// Portrait Import Wizard — first meaningful slice (#657) + FE-Repo browser
// and drag-and-drop integration (#664).
//
// Pipeline: file dialog / drag-drop / FE-Repo -> LoadImageFromPath ->
// ImageImportService.LoadAndQuantizeFromFile (16-color quantization) ->
// preview via PortraitImportHelper.BuildPreviewImage -> on Import, call
// PortraitImportHelper.ImportSimple / ImportSheet under an UndoService
// scope. All ROM-write logic lives in PortraitImportHelper so both this
// wizard and ImagePortraitView share a single source of truth.
//
// Batch import wired in #661: the "Pick Folder (batch)..." button enumerates
// every PNG / BMP in a folder, parses the slot ID from the filename prefix
// (0xNN.png or NN.png), and delegates to PortraitImportHelper.ImportFolderAsync
// which wraps the whole batch in a single UndoService scope.
//
// Advanced palette options + Fuchidori — implemented in #662:
//   - Auto-quantize (default), Share with target slot, Custom palette file...
//   - Fuchidori (black outline) checkbox orthogonal to palette mode.
//
// Eye/mouth block coords (#663 Slice A) — implemented via the Detail
// expander: 4 NumericUpDowns (MouthBlockX/Y, EyeBlockX/Y) that map to
// portrait entry bytes B20-B23 on FE7/FE8 ROMs. FE6 hides the inputs.
//
// Crop NUDs + frame selector + WF status label (#707 Slice A) — implemented:
// 8 crop NumericUpDowns (Eye + Mouth X/Y/W/H), Frame NumericUpDown (0-10),
// and a status TextBlock that echoes WF mode strings per frame index via
// FEBuilderGBA.PortraitFrameStrings.GetWfModeString. These are UI-only
// inputs — the per-frame live preview render pipeline (port of WF
// GenEye/GenMouth/GenPreview) is deferred to a follow-up issue.
//
// Out of scope (tracked under follow-ups):
//   - Per-frame live preview render pipeline (follow-up to #707 Slice A)
using System;
using global::Avalonia;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImagePortraitImporterView : TranslatedUserControl, IEmbeddableEditor
    {
        public EditorDescriptor Descriptor => new("Portrait Import Wizard", 660, 500, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, MinWidth: 660, MinHeight: 500);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        readonly ImagePortraitImporterViewModel _vm = new();
        readonly UndoService _undoService = new();

        // #662: advanced palette options + Fuchidori state.
        byte[] _customPaletteBytes;
        string _customPaletteFilename = string.Empty;

        // #1980: cached prepared (color-keyed + auto-quantized) preview
        // result, shared by the Step-2 source preview (SetQuantizedPreview)
        // AND the per-frame live preview (RefreshFramePreview). Rebuilt once
        // per image load via RebuildPreparedPreview() — never recomputed on
        // every frame/crop NUD change — and cleared automatically
        // (BuildPreparedPreviewLoadResult returns null) whenever the loaded
        // source is unusable. NEVER passed to PortraitImportHelper.ImportPortrait;
        // that call always reads the original _vm.LoadedImage so ROM-write
        // behavior is unchanged.
        ImageImportService.LoadResult _preparedPreview;

        // #1019 (Import jump): true once Opened -> LoadList() has populated
        // EntryList. SetItems() auto-selects row 0, so a NavigateTo arriving
        // BEFORE the list loads (the Portrait editor's JumpToImporter handler
        // opens the importer then immediately NavigateTo's the edited slot,
        // but Avalonia raises Opened asynchronously after layout) is stashed in
        // _pendingNavigateAddr and REPLAYED after the row-0 auto-select so it
        // overrides row 0 and lands on the intended slot. Mirrors the
        // pending-navigate pattern in UnitIncreaseHeightView (#1081).
        bool _listLoaded;
        uint? _pendingNavigateAddr;

        PortraitPaletteMode CurrentPaletteMode =>
            PaletteShareRadio.IsChecked == true ? PortraitPaletteMode.SharePalette :
            PaletteCustomRadio.IsChecked == true ? PortraitPaletteMode.CustomPalette :
            PortraitPaletteMode.AutoQuantize;

        bool FuchidoriEnabled => FuchidoriCheckbox.IsChecked == true;

        public string ViewTitle => "Portrait Import Wizard";
        public new bool IsLoaded => _vm.IsLoaded;

        public ImagePortraitImporterView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;

            // #664: enable drag-and-drop for image files. Mirrors the
            // pattern from ImagePortraitView (ctor lines 43-45).
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);

            // #707 Slice A: frame selector echoes WF mode strings.
            // #975: the frame selector ALSO drives the per-frame live preview
            // (port of WF GenPreviewMainChar). The Value="0" default in AXAML
            // guarantees the NUD is non-null on Open, but we keep the
            // null-coalescing fallback because Avalonia NumericUpDown can
            // transiently report null mid-edit (e.g. while the user clears the
            // field to retype).
            FrameInput.ValueChanged += (_, _) =>
            {
                FrameStatusLabel.Text = PortraitFrameStrings.GetWfModeString(
                    (int)(FrameInput.Value ?? 0));
                RefreshFramePreview();
            };
            FrameStatusLabel.Text = PortraitFrameStrings.GetWfModeString(
                (int)(FrameInput.Value ?? 0));

            // #975: every crop / block NumericUpDown also re-renders the
            // per-frame preview so the user sees the eye/mouth crop composite
            // update live as they tweak the boxes (the WF DecreaseColor16 +
            // GenPreviewMainChar pipeline). The Core seam re-derives the
            // standardized sheet slots from these crop values, so a crop change
            // genuinely changes the rendered frame.
            void Hook(NumericUpDown? nud)
            {
                if (nud != null) nud.ValueChanged += (_, _) => RefreshFramePreview();
            }
            Hook(EyeCropXInput); Hook(EyeCropYInput); Hook(EyeCropWInput); Hook(EyeCropHInput);
            Hook(MouthCropXInput); Hook(MouthCropYInput); Hook(MouthCropWInput); Hook(MouthCropHInput);
            Hook(MouthBlockXInput); Hook(MouthBlockYInput); Hook(EyeBlockXInput); Hook(EyeBlockYInput);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_listLoaded) return;
            LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                // #1911: show each slot's OWN portrait thumbnail (per-id, resolved by
                // ListIconLoaders.PortraitLoader), matching the main Portrait editor,
                // so the constant "Quantized preview" of the loaded source is no longer
                // mistaken for every character's current portrait.
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.PortraitLoader(items, i)); // auto-selects row 0
                RefreshImportButtonState();
                _listLoaded = true;

                // #1019: replay a navigation requested before the list was
                // ready so it OVERRIDES the row-0 auto-select. The replay's
                // SelectAddress re-runs OnSelected -> UpdateDetailPanel for the
                // intended slot (loading its B20-B23 into the four NUDs).
                if (_pendingNavigateAddr is uint pending)
                {
                    _pendingNavigateAddr = null;
                    EntryList.SelectAddress(pending);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitImporterView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                RefreshImportButtonState();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitImporterView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = _vm.CurrentAddr == 0
                ? "(no slot selected)"
                : string.Format("0x{0:X08}", _vm.CurrentAddr);

            // #663 Slice A: pre-populate the 4 Detail NumericUpDowns from the
            // selected slot's current B20-B23 bytes. On FE6 the bytes are
            // unrelated (16-byte entry layout), so we disable the expander and
            // surface a notice instead of reading bogus values.
            UpdateDetailPanel();
        }

        // #663 Slice A: refresh the Detail expander's enabled state + 4
        // NumericUpDowns to match the currently selected slot. Called from
        // UpdateUI (on slot selection) and once at Opened time so the panel
        // reflects whatever slot the wizard auto-selects on first show.
        void UpdateDetailPanel()
        {
            ROM rom = CoreState.ROM;
            uint addr = _vm.CurrentAddr;
            bool isFe7Or8 = rom != null && PortraitImportHelper.IsFe7Or8EntryLayout(rom);

            if (DetailFe6Notice != null)
                DetailFe6Notice.IsVisible = rom != null && !isFe7Or8;

            // Disable the NUDs when there's no slot selected or the ROM is
            // FE6 — values are meaningless / dangerous in either case.
            // Same gating applied to the #707 Slice A crop/frame NUDs so the
            // entire Detail-expander payload (#663 + #707) is consistent
            // (Copilot PR review).
            bool enableNuds = isFe7Or8 && addr != 0;
            if (MouthBlockXInput != null) MouthBlockXInput.IsEnabled = enableNuds;
            if (MouthBlockYInput != null) MouthBlockYInput.IsEnabled = enableNuds;
            if (EyeBlockXInput   != null) EyeBlockXInput.IsEnabled   = enableNuds;
            if (EyeBlockYInput   != null) EyeBlockYInput.IsEnabled   = enableNuds;
            // #707 Slice A: crop NUDs + frame selector + status label
            // share the same enablement gate as the #663 block-coord NUDs.
            if (EyeCropXInput    != null) EyeCropXInput.IsEnabled    = enableNuds;
            if (EyeCropYInput    != null) EyeCropYInput.IsEnabled    = enableNuds;
            if (EyeCropWInput    != null) EyeCropWInput.IsEnabled    = enableNuds;
            if (EyeCropHInput    != null) EyeCropHInput.IsEnabled    = enableNuds;
            if (MouthCropXInput  != null) MouthCropXInput.IsEnabled  = enableNuds;
            if (MouthCropYInput  != null) MouthCropYInput.IsEnabled  = enableNuds;
            if (MouthCropWInput  != null) MouthCropWInput.IsEnabled  = enableNuds;
            if (MouthCropHInput  != null) MouthCropHInput.IsEnabled  = enableNuds;
            if (FrameInput       != null) FrameInput.IsEnabled       = enableNuds;
            if (FrameStatusLabel != null) FrameStatusLabel.IsEnabled = enableNuds;

            if (!enableNuds) return;

            // Safe bounds check: 28-byte FE7/FE8 entries, addr+23 must fit
            // inside ROM.Data.
            if ((long)addr + 24 > rom.Data.Length) return;

            if (MouthBlockXInput != null) MouthBlockXInput.Value = rom.u8(addr + 20);
            if (MouthBlockYInput != null) MouthBlockYInput.Value = rom.u8(addr + 21);
            if (EyeBlockXInput   != null) EyeBlockXInput.Value   = rom.u8(addr + 22);
            if (EyeBlockYInput   != null) EyeBlockYInput.Value   = rom.u8(addr + 23);
        }

        // #664: drag-over handler — accept the drag if any file has a
        // .png or .bmp extension. Mirrors ImagePortraitView.OnDragOver.
        void OnDragOver(object? sender, DragEventArgs e)
        {
            if (!e.Data.Contains(DataFormats.Files)) { e.DragEffects = DragDropEffects.None; return; }
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f.Path.LocalPath).ToLowerInvariant();
                    if (ext == ".png" || ext == ".bmp") { e.DragEffects = DragDropEffects.Copy; return; }
                }
            }
            e.DragEffects = DragDropEffects.None;
        }

        // #664: drop handler — load the first .png/.bmp file via the shared
        // LoadImageFromPath helper so drag-drop, Pick PNG/BMP, and FE-Repo
        // all share the same load/preview path. The wizard does NOT
        // auto-import on drop; the user must still hit "Import to selected
        // slot" to commit the write (matches the Pick PNG/BMP flow).
        void OnDrop(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFiles();
            if (files == null) return;

            foreach (var file in files)
            {
                string path = file.Path.LocalPath;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".bmp")
                {
                    LoadImageFromPath(path);
                    return;
                }
            }
        }

        async void PickFile_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string filePath = await FileDialogHelper.OpenImageFile(TopLevel.GetTopLevel(this));
                if (string.IsNullOrEmpty(filePath))
                {
                    return; // user cancelled
                }

                LoadImageFromPath(filePath);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitImporterView.PickFile_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"Pick file failed: {ex.Message}");
            }
        }

        // #664: FE-Repo browser button — open the FE-Repo resource browser
        // window, await the user's selection, and load the returned file
        // path through the shared LoadImageFromPath helper. Mirrors
        // ImagePortraitView.FERepoButton_Click (line 676). The browser's
        // ShowDialog<string>(owner) returns the selected file path or
        // null/empty when cancelled.
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string result = await WindowManager.Instance.OpenModal<FERepoResourceBrowserWindow, string>(
                    TopLevel.GetTopLevel(this) as Window);
                if (!string.IsNullOrEmpty(result))
                {
                    LoadImageFromPath(result);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitImporterView.FERepo_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"FE-Repo browser failed: {ex.Message}");
            }
        }

        // Shared image-load + preview pipeline. Pick PNG/BMP, FE-Repo, and
        // drag-and-drop all funnel through here so the preview behavior is
        // identical across entry points (Copilot CLI plan #664 review #2).
        void LoadImageFromPath(string filePath)
        {
            try
            {
                var loadResult = ImageImportService.LoadAndQuantizeFromFile(filePath, 0, 0, 16);
                if (loadResult == null)
                {
                    CoreState.Services.ShowError("Failed to load image (no result).");
                    return;
                }
                if (!loadResult.Success)
                {
                    CoreState.Services.ShowError(loadResult.Error ?? "Failed to load image.");
                    return;
                }

                _vm.LoadedImage = loadResult;
                // #1980: rebuild the shared prepared-preview cache for the
                // freshly loaded image BEFORE either preview render call below
                // reads it, so both are guaranteed in sync with the new source.
                RebuildPreparedPreview();
                SourceFileLabel.Text = filePath;
                ImageSizeLabel.Text = $"Quantized to 16 colors — {loadResult.Width} x {loadResult.Height}";
                UpdateSheetModeLabel(loadResult);

                // Preview — see SetQuantizedPreview (single
                // BuildPreviewImageFromPrepared call site, reading the cache
                // RebuildPreparedPreview just populated above, so all entry
                // points share the same leak-safe, non-re-quantizing preview
                // path — #1980).
                SetQuantizedPreview();

                // #975: refresh the per-frame composite for the freshly loaded
                // image so the wizard's preview pane is populated immediately.
                RefreshFramePreview();

                StatusLabel.Text = string.Empty;
                StatusLabel.Foreground = global::Avalonia.Media.Brushes.Gray;
                RefreshImportButtonState();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitImporterView.LoadImageFromPath failed: {0}", ex.Message);
                CoreState.Services.ShowError($"Load image failed: {ex.Message}");
            }
        }

        // #661: batch folder import — enumerate every .png / .bmp in the
        // chosen folder, parse the slot ID from the filename prefix, and
        // delegate to PortraitImportHelper.ImportFolderAsync. A single outer
        // UndoService scope wraps the whole batch (rollback if every file
        // fails). UI buttons are disabled while the batch runs so the user
        // cannot kick off a second import mid-flight.
        async void PickFolder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Pick a folder of portrait PNG/BMP files",
                    AllowMultiple = false,
                });
                if (folders == null || folders.Count == 0) return;

                // #1639: batch import reads many files from the chosen folder and
                // needs a real local directory; a SAF folder pick (no local path)
                // cannot be enumerated by path → message on Android, never silent.
                string folder = folders[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(folder))
                {
                    CoreState.Services.ShowError(R._("Batch portrait import reads a folder of files and requires desktop file-system access; it is not available on this device."));
                    return;
                }
                bool confirm = CoreState.Services.ShowYesNo(
                    $"Batch-import all PNG/BMP files from {folder}? Each file must be named 0xNN.png or NN.png matching the portrait slot.");
                if (!confirm) return;

                var rom = CoreState.ROM;
                if (rom == null)
                {
                    CoreState.Services.ShowError("No ROM loaded.");
                    return;
                }

                PickFileButton.IsEnabled = false;
                FERepoButton.IsEnabled = false;
                PickFolderButton.IsEnabled = false;
                ImportButton.IsEnabled = false;
                BatchProgressBar.IsVisible = true;
                BatchProgressBar.Value = 0;
                BatchResultsBox.IsVisible = true;
                BatchResultsBox.Text = string.Empty;

                try
                {
                    // Pre-count the eligible files so the bar can be
                    // determinate, matching the per-file Report cadence
                    // inside ImportFolderAsync (one Report per processed file).
                    var allFiles = Directory.EnumerateFiles(folder)
                        .Where(f =>
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            return ext == ".png" || ext == ".bmp";
                        })
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    BatchProgressBar.Maximum = Math.Max(1, allFiles.Count);

                    int processed = 0;
                    // Copilot CLI PR review (round 2) #2: do NOT wrap the
                    // callback body in Dispatcher.UIThread.Post. Progress<T>
                    // already marshals each call to the captured
                    // SynchronizationContext (the Avalonia UI thread, since
                    // this handler runs on it). An inner Post schedules the
                    // mutation for a LATER pump cycle — that cycle can run
                    // AFTER the post-loop `BatchResultsBox.Text = ...` final
                    // overwrite, producing duplicate lines and overshooting
                    // the progress bar. Direct mutation here is safe and
                    // ordered.
                    var progress = new Progress<string>(line =>
                    {
                        processed++;
                        BatchProgressBar.Value = processed;
                        BatchResultsBox.Text += line + "\n";
                    });

                    var result = await PortraitImportHelper.ImportFolderAsync(folder, progress, _undoService, rom);

                    BatchResultsBox.Text = string.Join("\n", result.Lines);
                    BatchProgressBar.Value = BatchProgressBar.Maximum;

                    CoreState.Services.ShowInfo(
                        $"Batch import complete: {result.Imported} imported, {result.Failed} failed, {result.Skipped} skipped (total {result.Total}).");
                    StatusLabel.Text = $"Batch import: {result.Imported}/{result.Total} imported";
                    StatusLabel.Foreground = result.Imported > 0
                        ? global::Avalonia.Media.Brushes.DarkGreen
                        : global::Avalonia.Media.Brushes.Firebrick;
                }
                catch (Exception ex)
                {
                    Log.ErrorF("ImagePortraitImporterView.PickFolder_Click batch failed: {0}", ex.Message);
                    CoreState.Services.ShowError($"Batch import error: {ex.Message}");
                }
                finally
                {
                    PickFileButton.IsEnabled = true;
                    FERepoButton.IsEnabled = true;
                    PickFolderButton.IsEnabled = true;
                    // ImportButton: refresh from current image + slot state.
                    RefreshImportButtonState();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitImporterView.PickFolder_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"Pick folder failed: {ex.Message}");
            }
        }

        void Import_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null) { CoreState.Services.ShowError("No ROM loaded."); return; }

                uint addr = _vm.CurrentAddr;
                if (addr == 0) { CoreState.Services.ShowError("Select a portrait slot from the list first."); return; }

                var loadResult = _vm.LoadedImage;
                if (loadResult == null || !loadResult.Success)
                { CoreState.Services.ShowError("Pick a source image first."); return; }

                // #662: validate custom palette mode pre-requisites.
                PortraitPaletteMode mode = CurrentPaletteMode;
                if (mode == PortraitPaletteMode.CustomPalette && _customPaletteBytes == null)
                {
                    CoreState.Services.ShowError("Custom palette mode requires picking a palette file first.");
                    return;
                }

                // #663 Slice A: capture the 4 Detail block-coord values to pass
                // through to the helper. Only send non-null values when the ROM
                // is FE7/FE8 (the helper itself also skips FE6, but gating here
                // keeps Slice A's intent self-documenting at the call site).
                byte? mouthBlockX = null, mouthBlockY = null;
                byte? eyeBlockX = null, eyeBlockY = null;
                PortraitImportHelper.PortraitEyeMouthCrops? crops = null;
                if (PortraitImportHelper.IsFe7Or8EntryLayout(rom))
                {
                    mouthBlockX = (byte)(MouthBlockXInput?.Value ?? 0);
                    mouthBlockY = (byte)(MouthBlockYInput?.Value ?? 0);
                    eyeBlockX   = (byte)(EyeBlockXInput?.Value   ?? 0);
                    eyeBlockY   = (byte)(EyeBlockYInput?.Value   ?? 0);
                    // #1917: pass the Detail-panel eye/mouth crop rects so the
                    // 128x112 sheet import reconstructs the animation cells the
                    // same way the live preview does (RenderFramePreview) — the
                    // reporter's smear was the un-reconstructed opaque cell bg.
                    crops = new PortraitImportHelper.PortraitEyeMouthCrops(
                        (int)(EyeCropXInput?.Value   ?? 0), (int)(EyeCropYInput?.Value   ?? 0),
                        (int)(EyeCropWInput?.Value   ?? 0), (int)(EyeCropHInput?.Value   ?? 0),
                        (int)(MouthCropXInput?.Value ?? 0), (int)(MouthCropYInput?.Value ?? 0),
                        (int)(MouthCropWInput?.Value ?? 0), (int)(MouthCropHInput?.Value ?? 0));
                }

                // Single source of truth: PortraitImportHelper. Same path used
                // by ImagePortraitView (drag-drop + Import PNG button).
                ImportOutcome outcome = PortraitImportHelper.ImportPortrait(
                    rom, addr, loadResult, _undoService,
                    mode, _customPaletteBytes, FuchidoriEnabled,
                    "Import Portrait Image (Wizard)",
                    mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY, crops);

                if (!outcome.Success)
                {
                    StatusLabel.Text = outcome.Error;
                    StatusLabel.Foreground = global::Avalonia.Media.Brushes.Firebrick;
                    CoreState.Services.ShowError(outcome.Error);
                    return;
                }

                // Record source file so the ImagePortraitView Open/Select
                // Source buttons light up for this slot.
                int idx = EntryList.SelectedOriginalIndex;
                PortraitImportHelper.RecordSourceFile(idx, loadResult.SourcePath);

                _vm.MarkClean();
                StatusLabel.Text = $"Imported into 0x{addr:X08}";
                StatusLabel.Foreground = global::Avalonia.Media.Brushes.DarkGreen;
                CoreState.Services.ShowInfo("Portrait imported successfully.");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitImporterView.Import_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"Import failed: {ex.Message}");
            }
        }

        // #662: show/hide the custom-palette picker row when the user toggles
        // between Auto / Share / Custom palette modes. Wired via XAML
        // IsCheckedChanged on all three radio buttons.
        void PaletteMode_Changed(object? sender, RoutedEventArgs e)
        {
            if (CustomPalettePickerRow != null)
            {
                CustomPalettePickerRow.IsVisible = PaletteCustomRadio.IsChecked == true;
            }
            if (_vm.LoadedImage != null)
            {
                UpdateSheetModeLabel(_vm.LoadedImage);
            }
        }

        // #662: pick a .pal/.act/.gpl/.txt/.gbapal palette file and stage its
        // 32 BGR555 bytes for the next import. The PaletteFormatConverter
        // result may be longer than 32 bytes (e.g. ACT files are typically
        // 768 bytes / 256 colors); we take the first 32 bytes / 16 colors and
        // reject inputs shorter than that with a clear error.
        async void CustomPalettePick_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Pick a palette file",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Palette files")
                        {
                            Patterns = new[] { "*.pal", "*.act", "*.gpl", "*.txt", "*.gbapal" }
                        },
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                    },
                });
                if (files == null || files.Count == 0) return;

                // #1639: bridge a SAF source (no local path) to a temp file.
                string? path = await FileDialogHelper.ResolveReadPathAsync(files[0]);
                if (string.IsNullOrEmpty(path)) return;
                byte[] fileBytes;
                try
                {
                    fileBytes = File.ReadAllBytes(path);
                }
                catch (Exception ex)
                {
                    CoreState.Services.ShowError($"Failed to read palette file: {ex.Message}");
                    return;
                }

                string ext = Path.GetExtension(path);
                PaletteFormat format = PaletteFormatConverter.DetectFormat(fileBytes, ext);
                byte[] imported;
                try
                {
                    imported = PaletteFormatConverter.ImportFromFormat(fileBytes, format);
                }
                catch (Exception ex)
                {
                    CoreState.Services.ShowError($"Failed to parse palette file: {ex.Message}");
                    return;
                }

                if (imported == null || imported.Length < 32)
                {
                    CoreState.Services.ShowError(
                        $"Invalid palette file — expected 16 colors (32 bytes), got {imported?.Length ?? 0}.");
                    return;
                }

                _customPaletteBytes = new byte[32];
                Array.Copy(imported, 0, _customPaletteBytes, 0, 32);
                _customPaletteFilename = Path.GetFileName(path);
                CustomPaletteLabel.Text = _customPaletteFilename;
                CustomPaletteLabel.Foreground = global::Avalonia.Media.Brushes.Black;
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePortraitImporterView.CustomPalettePick_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"Pick palette failed: {ex.Message}");
            }
        }

        // Set the quantized SOURCE preview (the existing Step-2 pane) from the
        // cached _preparedPreview (see RebuildPreparedPreview / #1980).
        // `BuildPreviewImageFromPrepared` returns an `IImage` (IDisposable);
        // `SetImage` extracts pixel data immediately via
        // `IconBitmapBuilder.FromImage`, so we dispose the source right after to
        // avoid leaking native bitmap resources when the user picks multiple
        // files (Copilot bot PR #684 inline review). Single
        // BuildPreviewImageFromPrepared call site shared by LoadImageFromPath +
        // the #975 screenshot seed so all entry points use the same preview path.
        void UpdateSheetModeLabel(ImageImportService.LoadResult loadResult)
        {
            if (SheetModeLabel == null || loadResult == null) return;

            string paletteAction = CurrentPaletteMode == PortraitPaletteMode.SharePalette
                ? "reuse the existing D8 palette (no palette write)"
                : CurrentPaletteMode == PortraitPaletteMode.CustomPalette
                    ? "write the selected custom palette"
                    : "write the quantized palette";

            bool isSheet = loadResult.Width == 128 && loadResult.Height == 112;
            bool isFace = loadResult.Width == 96 && loadResult.Height == 80;
            SheetModeLabel.Text = isSheet
                ? $"128 x 112 composite sheet — will write face, mini, mouth, and {paletteAction} (FE7/FE8 only)"
                : isFace
                    ? $"96 x 80 face — will reverse-assemble face, clear mini/mouth, and {paletteAction}"
                    : "Unsupported portrait size — use 96 x 80 face or 128 x 112 sheet";
        }

        void SetQuantizedPreview()
        {
            using IImage preview = PortraitImportHelper.BuildPreviewImageFromPrepared(_preparedPreview);
            PreviewImage.SetImage(preview);
        }

        // #1980: (re)build the shared prepared-preview cache from the current
        // _vm.LoadedImage. Must be called exactly once right after every
        // _vm.LoadedImage assignment (LoadImageFromPath + the
        // SeedFramePreviewForScreenshot harness seed) so SetQuantizedPreview
        // and RefreshFramePreview always render from a cache that matches the
        // currently loaded source — never a stale prior image, and never
        // recomputed per frame/crop NUD tweak. Safely clears the cache to null
        // (via BuildPreparedPreviewLoadResult's own null-return contract) when
        // the source is missing, failed, or otherwise unusable.
        void RebuildPreparedPreview()
        {
            _preparedPreview = PortraitImportHelper.BuildPreparedPreviewLoadResult(_vm.LoadedImage);
        }

        // #975: render the per-frame live preview (port of WF
        // GenPreviewMainChar) for the currently loaded image, current
        // crop/block NUD values, and the selected frame. No-ops gracefully when
        // no image is loaded or the source is too small. The Core seam owns all
        // the indexed-pixel compositing; the View just gathers inputs and pushes
        // the result into the FramePreviewImage GbaImageControl.
        //
        // #1980: reads the cached _preparedPreview (color-keyed + quantized —
        // see RebuildPreparedPreview) instead of the raw _vm.LoadedImage, so
        // the composited frame treats the same palette index 0 as transparent
        // background that the Step-2 source preview does, and so this never
        // re-quantizes on every frame/crop change.
        void RefreshFramePreview()
        {
            try
            {
                if (FramePreviewImage == null) return;

                var prepared = _preparedPreview;
                if (prepared == null || !prepared.Success
                    || prepared.IndexedPixels == null || prepared.GBAPalette == null)
                {
                    FramePreviewImage.SetImage(null);
                    return;
                }

                // FE6 has no eye states; the seam skips eye overlays when isFe6
                // is true. The wizard's crop/frame NUDs are disabled on FE6, so
                // this path is only reached interactively on FE7/FE8 — but stay
                // version-correct regardless.
                ROM rom = CoreState.ROM;
                bool isFe6 = rom != null && !PortraitImportHelper.IsFe7Or8EntryLayout(rom);

                int frame = (int)(FrameInput?.Value ?? 0);

                using IImage img = PortraitImportPreviewCore.RenderFramePreview(
                    prepared.IndexedPixels, prepared.Width, prepared.Height,
                    prepared.GBAPalette,
                    (int)(EyeBlockXInput?.Value ?? 0), (int)(EyeBlockYInput?.Value ?? 0),
                    (int)(MouthBlockXInput?.Value ?? 0), (int)(MouthBlockYInput?.Value ?? 0),
                    (int)(EyeCropXInput?.Value ?? 0), (int)(EyeCropYInput?.Value ?? 0),
                    (int)(EyeCropWInput?.Value ?? 0), (int)(EyeCropHInput?.Value ?? 0),
                    (int)(MouthCropXInput?.Value ?? 0), (int)(MouthCropYInput?.Value ?? 0),
                    (int)(MouthCropWInput?.Value ?? 0), (int)(MouthCropHInput?.Value ?? 0),
                    frame, isFe6);

                // img is disposed by the using; SetImage extracts the pixel data
                // immediately (same leak-safe pattern as PreviewImage above).
                FramePreviewImage.SetImage(img);
            }
            catch (Exception ex)
            {
                Log.Error($"ImagePortraitImporterView.RefreshFramePreview failed: {ex.Message}");
            }
        }

        void RefreshImportButtonState()
        {
            bool hasImage = _vm.LoadedImage != null && _vm.LoadedImage.Success;
            bool hasSlot = _vm.CurrentAddr != 0;
            ImportButton.IsEnabled = hasImage && hasSlot;
        }

        public void NavigateTo(uint address)
        {
            // #1019: when the list has not yet loaded (JumpToImporter opens the
            // importer then calls NavigateTo synchronously, before Avalonia
            // raises Opened -> LoadList), stash the address so LoadList() can
            // replay it after the row-0 auto-select. A direct SelectAddress
            // would no-op against the still-empty list and row 0 would win.
            if (!_listLoaded)
            {
                _pendingNavigateAddr = address;
                return;
            }
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
            SeedFramePreviewForScreenshot();
        }

        /// <summary>
        /// #975: in <c>--screenshot-all</c> mode (and ONLY then) seed a
        /// synthetic 128x112 quantized image plus a non-base frame so the PNG
        /// shows a REAL per-frame composite from the live render path. The
        /// interactive runtime never enters this branch — it is invoked from the
        /// harness's <c>SelectFirstItem</c> reflection call (mirrors the
        /// PointerToolView #966 screenshot-seed pattern).
        ///
        /// The synthetic sheet uses distinct palette indices per region (face
        /// band vs eye strips vs mouth strips) so the composited frame is
        /// visibly different from the bare face — proving the seam works, not a
        /// fabricated image.
        /// </summary>
        void SeedFramePreviewForScreenshot()
        {
            if (!App.ScreenshotAllMode) return;
            try
            {
                _vm.LoadedImage = BuildSyntheticSheetLoadResult();
                // #1980: keep the prepared-preview cache in sync for the
                // synthetic screenshot-seed image too.
                RebuildPreparedPreview();
                SourceFileLabel.Text = "(screenshot seed: synthetic 128x112 sheet)";
                ImageSizeLabel.Text = "Quantized to 16 colors — 128 x 112";

                SetQuantizedPreview();

                // Expand the Detail expander so the per-frame pane is visible in
                // the capture, and select frame 2 (closed eyes) for a clear
                // eye-overlay composite.
                if (DetailExpander != null) DetailExpander.IsExpanded = true;
                if (FrameInput != null) FrameInput.Value = 2;

                RefreshFramePreview();
                RefreshImportButtonState();
            }
            catch (Exception ex)
            {
                Log.Error($"ImagePortraitImporterView.SeedFramePreviewForScreenshot: {ex.Message}");
            }
        }

        /// <summary>
        /// Build a deterministic 128x112 16-color quantized <c>LoadResult</c>
        /// for the screenshot seed. Palette: index 1 = face band, index 2 = eye
        /// strips, index 3 = mouth strips, index 0 = transparent. Each region is
        /// painted with its own index so the per-frame composite is visibly
        /// distinct from the base face.
        /// </summary>
        static ImageImportService.LoadResult BuildSyntheticSheetLoadResult()
        {
            const int w = 128, h = 112;
            byte[] indexed = new byte[w * h];

            // Base face band (whole 96x80 face area) = index 1.
            FillRegion(indexed, w, 0, 0, 96, 80, 1);
            // Eye strips at the standard sheet slots = index 2.
            FillRegion(indexed, w, 96, 48, 32, 16, 2); // half-eye
            FillRegion(indexed, w, 96, 64, 32, 16, 2); // closed-eye
            // Mouth strips (6 frames) = index 3.
            FillRegion(indexed, w, 0, 80, 96, 16, 3);
            FillRegion(indexed, w, 0, 96, 96, 16, 3);

            // GBA palette (2 bytes/color, 16 colors). 0=transparent black,
            // 1=blue face, 2=red eyes, 3=green mouth, rest=0.
            byte[] pal = new byte[32];
            void SetColor(int idx, ushort gba) { pal[idx * 2] = (byte)(gba & 0xFF); pal[idx * 2 + 1] = (byte)(gba >> 8); }
            SetColor(0, 0x0000);          // transparent
            SetColor(1, (ushort)(20 << 10 | 10 << 5 | 4));   // bluish face
            SetColor(2, (ushort)(2 << 10 | 2 << 5 | 28));    // reddish eyes
            SetColor(3, (ushort)(2 << 10 | 28 << 5 | 4));    // greenish mouth

            return new ImageImportService.LoadResult
            {
                Success = true,
                IndexedPixels = indexed,
                GBAPalette = pal,
                Width = w,
                Height = h,
                SourcePath = string.Empty,
            };
        }

        static void FillRegion(byte[] buf, int bufW, int x, int y, int w, int h, byte value)
        {
            for (int yy = 0; yy < h; yy++)
            {
                int row = (y + yy) * bufW + x;
                for (int xx = 0; xx < w; xx++)
                {
                    int i = row + xx;
                    if (i >= 0 && i < buf.Length) buf[i] = value;
                }
            }
        }
    }
}
