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
    public partial class ImagePortraitImporterView : TranslatedWindow, IEditorView
    {
        readonly ImagePortraitImporterViewModel _vm = new();
        readonly UndoService _undoService = new();

        // #662: advanced palette options + Fuchidori state.
        byte[] _customPaletteBytes;
        string _customPaletteFilename = string.Empty;

        PortraitPaletteMode CurrentPaletteMode =>
            PaletteShareRadio.IsChecked == true ? PortraitPaletteMode.SharePalette :
            PaletteCustomRadio.IsChecked == true ? PortraitPaletteMode.CustomPalette :
            PortraitPaletteMode.AutoQuantize;

        bool FuchidoriEnabled => FuchidoriCheckbox.IsChecked == true;

        public string ViewTitle => "Portrait Import Wizard";
        public bool IsLoaded => _vm.IsLoaded;

        public ImagePortraitImporterView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();

            // #664: enable drag-and-drop for image files. Mirrors the
            // pattern from ImagePortraitView (ctor lines 43-45).
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);

            // #707 Slice A: frame selector echoes WF mode strings.
            // Render pipeline (eye/mouth crop -> live preview) is the
            // follow-up issue (#717); here we just keep the status label
            // in sync. The Value="0" default in AXAML guarantees the NUD
            // is non-null on Open, but we keep the null-coalescing fallback
            // because Avalonia NumericUpDown can transiently report null
            // mid-edit (e.g. while the user clears the field to retype).
            FrameInput.ValueChanged += (_, _) =>
                FrameStatusLabel.Text = PortraitFrameStrings.GetWfModeString(
                    (int)(FrameInput.Value ?? 0));
            FrameStatusLabel.Text = PortraitFrameStrings.GetWfModeString(
                (int)(FrameInput.Value ?? 0));
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                RefreshImportButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ImagePortraitImporterView.OnSelected failed: {0}", ex.Message);
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
            bool enableNuds = isFe7Or8 && addr != 0;
            if (MouthBlockXInput != null) MouthBlockXInput.IsEnabled = enableNuds;
            if (MouthBlockYInput != null) MouthBlockYInput.IsEnabled = enableNuds;
            if (EyeBlockXInput   != null) EyeBlockXInput.IsEnabled   = enableNuds;
            if (EyeBlockYInput   != null) EyeBlockYInput.IsEnabled   = enableNuds;

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
                string filePath = await FileDialogHelper.OpenImageFile(this);
                if (string.IsNullOrEmpty(filePath))
                {
                    return; // user cancelled
                }

                LoadImageFromPath(filePath);
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.PickFile_Click failed: {0}", ex.Message);
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
                var browser = new FERepoResourceBrowserWindow();
                string result = await browser.ShowDialog<string>(this);
                if (!string.IsNullOrEmpty(result))
                {
                    LoadImageFromPath(result);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.FERepo_Click failed: {0}", ex.Message);
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
                SourceFileLabel.Text = filePath;
                ImageSizeLabel.Text = $"Quantized to 16 colors — {loadResult.Width} x {loadResult.Height}";
                bool isSheet = loadResult.Width == 128 && loadResult.Height == 112;
                SheetModeLabel.Text = isSheet
                    ? "128 x 112 composite sheet — will write face, mini, mouth, palette (FE7/FE8 only)"
                    : "Simple image — will write sheet (D0) + palette (D8) only";

                // Preview — `BuildPreviewImage` returns an `IImage` (IDisposable);
                // `SetImage` extracts pixel data immediately via
                // `IconBitmapBuilder.FromImage`, so we dispose the source
                // right after to avoid leaking native bitmap resources when
                // the user picks multiple files (Copilot bot PR #684 inline
                // review).
                using (IImage preview = PortraitImportHelper.BuildPreviewImage(loadResult))
                {
                    PreviewImage.SetImage(preview);
                }

                StatusLabel.Text = string.Empty;
                StatusLabel.Foreground = global::Avalonia.Media.Brushes.Gray;
                RefreshImportButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("ImagePortraitImporterView.LoadImageFromPath failed: {0}", ex.Message);
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

                string folder = folders[0].Path.LocalPath;
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
                    Log.Error("ImagePortraitImporterView.PickFolder_Click batch failed: {0}", ex.Message);
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
                Log.Error("ImagePortraitImporterView.PickFolder_Click failed: {0}", ex.Message);
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
                if (PortraitImportHelper.IsFe7Or8EntryLayout(rom))
                {
                    mouthBlockX = (byte)(MouthBlockXInput?.Value ?? 0);
                    mouthBlockY = (byte)(MouthBlockYInput?.Value ?? 0);
                    eyeBlockX   = (byte)(EyeBlockXInput?.Value   ?? 0);
                    eyeBlockY   = (byte)(EyeBlockYInput?.Value   ?? 0);
                }

                // Single source of truth: PortraitImportHelper. Same path used
                // by ImagePortraitView (drag-drop + Import PNG button).
                ImportOutcome outcome;
                if (loadResult.Width == 128 && loadResult.Height == 112)
                {
                    outcome = PortraitImportHelper.ImportSheet(rom, addr, loadResult, _undoService,
                        mode, _customPaletteBytes, FuchidoriEnabled,
                        "Import Portrait Sheet (Wizard)",
                        mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY);
                }
                else
                {
                    outcome = PortraitImportHelper.ImportSimple(rom, addr, loadResult, _undoService,
                        mode, _customPaletteBytes, FuchidoriEnabled,
                        "Import Portrait Image (Wizard)",
                        mouthBlockX, mouthBlockY, eyeBlockX, eyeBlockY);
                }

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
                Log.Error("ImagePortraitImporterView.Import_Click failed: {0}", ex.Message);
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

                string path = files[0].Path.LocalPath;
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
                Log.Error("ImagePortraitImporterView.CustomPalettePick_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError($"Pick palette failed: {ex.Message}");
            }
        }

        void RefreshImportButtonState()
        {
            bool hasImage = _vm.LoadedImage != null && _vm.LoadedImage.Success;
            bool hasSlot = _vm.CurrentAddr != 0;
            ImportButton.IsEnabled = hasImage && hasSlot;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
