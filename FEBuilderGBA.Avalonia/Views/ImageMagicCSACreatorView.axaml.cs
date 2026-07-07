// #886 — Export + OpenSource/SelectSource wired.
// #889 — Import wired (closes #889, completes #500 image-import).
// Editor remains a stub (follow-up to #500).
using global::Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>ImageMagicCSACreatorForm</c>.
    /// Gap-sweep fix (#417) raises the AXAML control surface from 3 to
    /// MEDIUM-verdict density (&gt;=28 of WF's 37 controls), wires the
    /// read-config / selection-bar / dim selector / animation buttons,
    /// and carries <c>AddrResult.tag</c> separately from the CSA struct
    /// address so the dim/no-dim/empty pointer write targets the correct
    /// pointer-table slot (Copilot CLI plan review #1).
    ///
    /// The "Data Expansion" (list expansion) button is wired in #837 — it grows
    /// the magic-effect + CSA spell tables to 254 rows via the all-reference
    /// <see cref="MagicListExpandCore"/> path (see <c>ListExpand_Click</c> /
    /// <c>UpdateListExpandVisibility</c>). Real CSA animation import/export,
    /// source open/select, JumpEditor, and live preview rendering remain
    /// WF-coupled and are surfaced as disabled buttons with tooltips
    /// referencing the open follow-up <c>#500</c> (ToolAnimationCreator
    /// real-init flow).
    /// </summary>
    public partial class ImageMagicCSACreatorView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ImageMagicCSACreatorViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "CSA Magic Creator";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("CSA Magic Creator", 1080, 610, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ImageMagicCSACreatorView()
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

                // Populate the top read-config bar so the UI reflects the
                // actual scan state (Copilot bot inline review #1 round 2:
                // start address = pointer-table base, count = spell-data cap).
                // The values come from the ViewModel after LoadList resolves
                // the magic system. If no system is detected, both stay 0.
                var rom = CoreState.ROM;
                uint readStart = 0;
                if (rom?.RomInfo != null && _vm.MagicKind == MagicSystemKind.CsaCreator)
                {
                    readStart = rom.p32(rom.RomInfo.magic_effect_pointer);
                }
                // #649: display via the unified EditorTopBar read-only slots.
                TopBar.StartAddressText = readStart.ToString();
                TopBar.ReadCountText = _vm.SpellDataCount.ToString();

                if (items.Count == 0)
                {
                    ClearDetailPanel();
                    UpdateUI();
                }

                UpdateListExpandVisibility();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicCSACreatorView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void ClearDetailPanel()
        {
            _vm.IsLoaded = false;
            _vm.CurrentAddr = 0;
            _vm.TagAddress = 0;
            _vm.P0 = 0;
            _vm.P4 = 0;
            _vm.P8 = 0;
            _vm.P12 = 0;
            _vm.P16 = 0;
            _vm.DimMode = 2;
            _vm.Comment = "";
            _vm.Frame = 0;
            _vm.Zoom = 0;
            _vm.BinInfo = "";
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e) => LoadList();

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                // Carry AddrResult.tag from the AddressListControl - the dim
                // selector targets this slot (Copilot CLI plan review #1).
                uint tagAddr = 0;
                var selected = EntryList.SelectedItem;
                if (selected != null) tagAddr = selected.tag;
                _vm.LoadEntry(addr, tagAddr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicCSACreatorView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            // The selection bar shows TWO different addresses (mirrors WF
            // panel5): AddressBox = the pointer-table slot (TagAddress), and
            // SelectedAddressLabel = the CSA struct itself (CurrentAddr).
            // Showing the same address in both was a Copilot CLI inline
            // review finding on PR #547.
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AddressBox.Value = _vm.TagAddress;
            SelectedAddressLabel.Content = string.Format("0x{0:X08}", _vm.CurrentAddr);
            P0Box.Text = string.Format("0x{0:X08}", _vm.P0);
            P4Box.Text = string.Format("0x{0:X08}", _vm.P4);
            P8Box.Text = string.Format("0x{0:X08}", _vm.P8);
            P12Box.Text = string.Format("0x{0:X08}", _vm.P12);
            P16Box.Text = string.Format("0x{0:X08}", _vm.P16);
            DimComboBox.SelectedIndex = (int)_vm.DimMode;
            CommentBox.Text = _vm.Comment;
            // #1021 — bound the frame spinner to the actual 0x86 frame count so
            // the user can't scrub past the end (mirrors the FEditor preview).
            int frameCount = _vm.GetFrameCount();
            FrameBox.Maximum = frameCount > 0 ? frameCount - 1 : 0;
            FrameBox.Value = _vm.Frame;
            ZoomComboBox.SelectedIndex = (int)_vm.Zoom;
            BinInfoBox.Text = _vm.BinInfo;
            // #886
            UpdateIoButtonsEnabled();
            UpdateSourceButtonVisibility();
            // #1021 — live CSA frame preview re-render on entry select.
            RenderPreview();
        }

        /// <summary>
        /// Render the current CSA frame and update the live preview control
        /// (READ-ONLY). Clears the surface when the preview can't be produced.
        /// Mirrors the FEditor <c>RenderPreview</c> (#1021).
        /// </summary>
        void RenderPreview()
        {
            try
            {
                if (!_vm.CanRenderPreview)
                {
                    MagicFramePreview.SetImage(null);
                    return;
                }
                using IImage? img = _vm.RenderCsaFramePreview(out _);
                MagicFramePreview.SetImage(img);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicCSACreatorView.RenderPreview: {0}", ex.Message);
                MagicFramePreview.SetImage(null);
            }
        }

        // #1021 — frame spinner drives live preview re-render (CSA had none).
        void FrameBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm == null) return;
            _vm.Frame = (uint)(FrameBox.Value ?? 0);
            RenderPreview();
        }

        // #1021 — zoom combo maps to the GbaImageControl zoom; NO ROM re-render.
        void ZoomComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            if (sender is not ComboBox combo) return;
            int idx = combo.SelectedIndex;
            if (idx < 0) return;
            _vm.Zoom = (uint)idx;
            // Index 0 = "Zoom in" (2x), index 1 = "Original size" (1x).
            // GbaImageControl owns the zoom; clamps to [1..8] internally.
            MagicFramePreview.Zoom = idx == 0 ? 2 : 1;
        }

        // #1021 — working "Find new resources online" link → MoreData wiki
        // (mirrors ImageMagicFEditorView.LinkInternet_Click / WF GotoMoreData).
        void LinkInternet_Click(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                const string url = "https://github.com/laqieer/FEBuilderGBA/wiki/MoreData";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicCSACreatorView.LinkInternet: {0}", ex.Message);
            }
        }

        // #886 — Export button enabled when CSA system is detected + entry selected.
        // #889 — Import button also enabled here (both toggled by the same gate condition).
        void UpdateIoButtonsEnabled()
        {
            bool csaAndSelected = _vm.MagicKind == MagicSystemKind.CsaCreator
                && EntryList.SelectedOriginalIndex >= 0;
            ExportButton.IsEnabled = csaAndSelected;
            ImportButton.IsEnabled = csaAndSelected;
        }

        // #886 — Source buttons visible when a cached source file exists on disk.
        // Mirrors FEditor UpdateSourceButtonVisibility. Same key scheme:
        // "MagicAnimation_" + U.ToHexString(selectedIndex+1).
        void UpdateSourceButtonVisibility()
        {
            int idx = EntryList.SelectedOriginalIndex;
            if (idx < 0)
            {
                OpenSourceButton.IsVisible   = false;
                SelectSourceButton.IsVisible = false;
                return;
            }
            string key = "MagicAnimation_" + U.ToHexString((uint)(idx + 1));
            bool hasFile = CoreState.ResourceCache is EtcCacheResource rcache
                && rcache.TryGetValue(key, out string? path)
                && !string.IsNullOrEmpty(path)
                && File.Exists(path);
            OpenSourceButton.IsVisible   = hasFile;
            SelectSourceButton.IsVisible = hasFile;
        }

        // Save a minimal transparent PNG placeholder.
        static void SaveDummyPng(string path, int width, int height)
        {
            try
            {
                IImageService? svc = CoreState.ImageService;
                if (svc == null) return;
                var img = svc.CreateImage(width, height);
                img.SetPixelData(new byte[width * height * 4]);
                img.Save(path);
            }
            catch (Exception ex) { Log.ErrorF("CSA SaveDummyPng: {0}", ex.Message); }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            uint reloadAddr = _vm.CurrentAddr;
            uint reloadTag = _vm.TagAddress;
            _undoService.Begin("Edit CSA Magic Entry");
            try
            {
                _vm.P0 = ParseHexText(P0Box.Text);
                _vm.P4 = ParseHexText(P4Box.Text);
                _vm.P8 = ParseHexText(P8Box.Text);
                _vm.P12 = ParseHexText(P12Box.Text);
                _vm.P16 = ParseHexText(P16Box.Text);
                int dimIdx = DimComboBox.SelectedIndex;
                if (dimIdx < 0) dimIdx = 2;
                _vm.DimMode = (uint)dimIdx;
                _vm.Comment = CommentBox.Text ?? "";

                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();

                // Reload so the BinInfo / SelectedAddress / RGB labels reflect
                // the new ROM bytes (mirrors the MapTileAnimation2View pattern).
                _vm.IsLoading = true;
                try
                {
                    _vm.LoadEntry(reloadAddr, reloadTag);
                    UpdateUI();
                }
                finally
                {
                    _vm.IsLoading = false;
                    _vm.MarkClean();
                }
                CoreState.Services?.ShowInfo("CSA Magic entry written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ImageMagicCSACreatorView.Write failed: {0}", ex.Message);
                // Surface the error to the user so it's actionable instead
                // of silently logged (Copilot bot inline review #2 round 2).
                CoreState.Services?.ShowError($"Write failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Enable the "Data Expansion" button only when the CSA magic system is
        /// present, and hide it once the table is already expanded past the
        /// original per-version count (mirrors WF
        /// <c>MagicListExpandsButton.Enabled = false</c> when
        /// <c>DataCount &gt; magic_effect_original_data_count</c>). #837.
        /// </summary>
        void UpdateListExpandVisibility()
        {
            ListExpandButton.IsEnabled = _vm.MagicKind == MagicSystemKind.CsaCreator;
            ListExpandButton.IsVisible = !_vm.IsListExpanded;
        }

        void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            // #837 — grow the magic-effect (table-1, entrySize 4) AND the CSA
            // spell table (table-2, entrySize 20) to a fixed 254 rows via the
            // all-reference path (DataExpansionCore.ExpandTableTo +
            // RepointAllReferences). The CSA-pointer NOT_FOUND clean-abort runs
            // FIRST inside the Core helper. Byte-identical to the WF
            // ImageMagicCSACreatorForm.MagicListExpandsButton_Click handler.
            if (CoreState.Services?.ShowYesNo(
                    R._("Expand the magic table? This grows the list to 254 entries.")) != true)
                return;

            uint reloadAddr = _vm.CurrentAddr;

            _undoService.Begin("Magic List Expansion");
            try
            {
                string err = _vm.ExpandMagicLists(_undoService.GetActiveUndoData());
                if (!string.IsNullOrEmpty(err))
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(err);
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();

                // NOTE B: refresh the list from the grown table (LoadList scans
                // via isPointerOrNULL and stops at the 0xFFFFFFFF terminator).
                // Reselect the prior row + hide the spent expand button.
                _vm.IsLoading = true;
                try
                {
                    EntryList.SetItems(_vm.LoadList());
                    if (reloadAddr != 0u) EntryList.SelectAddress(reloadAddr);
                    UpdateListExpandVisibility();
                }
                finally { _vm.IsLoading = false; _vm.MarkClean(); }

                CoreState.Services?.ShowInfo(R._("Expanded magic list to 254 entries."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ImageMagicCSACreatorView.ListExpand: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("List expansion failed: {0}", ex.Message));
            }
        }

        // #889 — Import Magic Animation (CSA .txt script + per-frame PNGs → ROM write).
        // Mirrors WF ImageUtilMagicCSACreator.Import + ImageMagicCSACreatorForm import handler.
        async void Import_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.MagicKind != MagicSystemKind.CsaCreator)
            {
                CoreState.Services?.ShowError(
                    R._("CSA Creator magic-system not detected. Install the patch first."));
                return;
            }

            ROM? rom = CoreState.ROM;
            if (rom == null) return;

            uint magicBaseAddr = _vm.CurrentAddr;
            if (magicBaseAddr == 0u)
            {
                CoreState.Services?.ShowError(R._("No magic-animation entry selected."));
                return;
            }

            // File dialog: pick .txt script.
            // #1639: the script loads sibling PNG frames from its own directory,
            // so require a real local path; a SAF pick (no local path) cannot
            // resolve siblings → message on Android, never silent.
            string txtPath = await FileDialogHelper.OpenFile(
                TopLevel.GetTopLevel(this) as Window,
                R._("Import CSA magic animation script"),
                "*.txt", requireLocalPath: true);

            if (string.IsNullOrEmpty(txtPath))
            {
                if (OperatingSystem.IsAndroid())
                    CoreState.Services?.ShowError(R._("Importing a magic animation script reads sibling PNG frames and requires desktop file-system access; it is not available on this device."));
                return;
            }

            await DoImport(
                txtPath,
                filename => LoadIndexedImage(filename, txtPath));
        }

        /// <summary>
        /// Injectable import entry point for tests (bypasses file dialog + image loader).
        /// </summary>
        internal async Task<string> DoImport(
            string txtPath,
            Func<string, (byte[] indexedPixels, int w, int h, byte[] gbaPalette)?> pngLoader)
        {
            ROM? rom = CoreState.ROM;
            if (rom == null) return "ROM is null";

            uint magicBaseAddr = _vm.CurrentAddr;
            if (magicBaseAddr == 0u) return "No magic-animation entry selected.";

            // Parse script (reuse shared ParseMagicScript from FEditor import #885).
            string[] scriptLines;
            try
            {
                scriptLines = File.ReadAllLines(txtPath, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                string err = R._("Cannot read script file: {0}", ex.Message);
                CoreState.Services?.ShowError(err);
                return err;
            }

            var cmds = MagicEffectImportCore.ParseMagicScript(scriptLines);
            if (cmds.Count == 0)
            {
                string err = R._("Script file is empty or contains no recognized commands.");
                CoreState.Services?.ShowError(err);
                return err;
            }

            // Snapshot ROM for rollback on failure (defensive safety net).
            byte[] snapshot = (byte[])rom.Data.Clone();

            // ONE ambient undo scope covers all writes atomically.
            _undoService.Begin("Import CSA Magic Animation (#889)");
            string importErr;
            try
            {
                importErr = MagicEffectCSAImportCore.ImportCsaMagicScript(
                    rom, magicBaseAddr, cmds, pngLoader);
            }
            catch (Exception ex)
            {
                importErr = ex.Message;
            }

            if (!string.IsNullOrEmpty(importErr))
            {
                // Restore ROM from snapshot before rolling back undo (defensive).
                Array.Copy(snapshot, rom.Data, snapshot.Length);
                _undoService.Rollback();
                CoreState.Services?.ShowError(R._("CSA magic animation import failed: {0}", importErr));
                return importErr;
            }

            _undoService.Commit();

            // Update resource cache so OpenSource/SelectSource buttons appear.
            int idx = EntryList.SelectedOriginalIndex;
            if (idx >= 0 && CoreState.ResourceCache is EtcCacheResource rcache)
            {
                string key = "MagicAnimation_" + U.ToHexString((uint)(idx + 1));
                rcache.Update(key, txtPath);
                UpdateSourceButtonVisibility();
            }

            // Refresh preview to show newly imported data.
            _vm.IsLoading = true;
            try
            {
                AddrResult? sel = EntryList.SelectedItem;
                if (sel != null) _vm.LoadEntry(sel.addr, sel.tag);
                UpdateUI();
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }

            CoreState.Services?.ShowInfo(R._("CSA magic animation imported successfully."));
            return string.Empty;
        }

        /// <summary>
        /// Load a PNG referenced by the CSA script: resolves relative filenames against the
        /// script's directory, loads as RGBA, quantizes to 16 colors, returns
        /// indexed pixels + GBA palette. Returns null on any failure.
        /// </summary>
        static (byte[] indexedPixels, int w, int h, byte[] gbaPalette)? LoadIndexedImage(
            string filename, string scriptTxtPath)
        {
            string dir = Path.GetDirectoryName(scriptTxtPath) ?? ".";
            string fullPath = Path.IsPathRooted(filename)
                ? filename
                : Path.Combine(dir, filename);

            try
            {
                var lr = ImageImportService.LoadAndQuantizeFromFile(
                    fullPath, 0, 0, maxColors: 16, strictSize: false);
                if (lr == null || !lr.Success || lr.IndexedPixels == null)
                    return null;
                return (lr.IndexedPixels, lr.Width, lr.Height, lr.GBAPalette);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImageMagicCSACreatorView.LoadIndexedImage: {0}", ex.Message);
                return null;
            }
        }

        // #886 — Export Magic Animation (CSA .txt + per-frame OBJ/BG PNGs).
        // CSA BG is TSA-composited via RenderCsaBgFrameSlot; OBJ reuses FEditor path.
        async void Export_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.MagicKind != MagicSystemKind.CsaCreator)
            {
                CoreState.Services?.ShowError(
                    R._("CSA Creator magic-system not detected. Install the patch first."));
                return;
            }

            ROM? rom = CoreState.ROM;
            if (rom == null) return;

            int idx = EntryList.SelectedOriginalIndex;
            if (idx < 0)
            {
                CoreState.Services?.ShowError(R._("No magic-animation entry selected."));
                return;
            }

            var (filename, filterIndex) = await FileDialogHelper.SaveFileWithFilterIndex(
                TopLevel.GetTopLevel(this) as Window,
                R._("Save CSA magic animation script"),
                new (string, string)[]
                {
                    (R._("Magic Animation (with comments)"), "*.txt"),
                    (R._("Magic Animation (no comments)"), "*.txt"),
                },
                "csa_" + U.ToHexString((uint)(idx + 1)) + ".txt");

            // #1639: DoExport writes sibling PNG frames next to the script, so
            // require a real local path; a SAF pick (no local path) cannot place
            // siblings → message on Android, never silent.
            if (string.IsNullOrEmpty(filename))
            {
                if (OperatingSystem.IsAndroid())
                    CoreState.Services?.ShowError(R._("Exporting a magic animation script writes sibling PNG frames and requires desktop file-system access; it is not available on this device."));
                return;
            }

            bool enableComment = (filterIndex == 0);
            await DoExport(filename, enableComment);
        }

        /// <summary>
        /// Injectable export entry point for tests (bypasses file dialog).
        /// </summary>
        internal async Task<string> DoExport(string filename, bool enableComment = true)
        {
            ROM? rom = CoreState.ROM;
            if (rom == null) return "ROM is null";

            if (_vm.MagicKind != MagicSystemKind.CsaCreator)
                return "CSA magic system not detected";

            try
            {
                uint frameDataAddr = _vm.P0;   // FrameData pointer
                uint objRtoL       = _vm.P4;   // OBJRightToLeft OAM
                uint objBGRtoL     = _vm.P12;  // OBJBGRightToLeft OAM

                string basename = Path.GetFileNameWithoutExtension(filename) + "_";
                string basedir  = Path.GetDirectoryName(filename) ?? ".";

                // Single ordered walk (isCsa=true reads +28 TSA, uses bgPtr+tsaPtr hash).
                List<int> sharedObjSlots, sharedBgSlots;
                List<MagicFrameMeta> frames;
                var scriptLines = MagicEffectExportCore.ExportMagicScriptLines(
                    rom, frameDataAddr, basename, enableComment,
                    out sharedObjSlots, out sharedBgSlots, out frames,
                    isCsa: true);

                if (frames.Count == 0 && scriptLines.Count <= 2)
                {
                    string err = R._("CSA animation scan failed — bad frame-data pointer.");
                    CoreState.Services?.ShowError(err);
                    return err;
                }

                // Render and save unique OBJ frames (same as FEditor — OBJ render is shared).
                int objSlotCount = MagicEffectExportCore.CountUniqueObjSlots(frames);
                for (int s = 0; s < objSlotCount; s++)
                {
                    IImage? img = MagicEffectExportCore.RenderObjFrameSlot(
                        rom, frames, s, objRtoL, objBGRtoL);
                    string pngPath = Path.Combine(
                        basedir, basename + "o_" + s.ToString("000") + ".png");
                    if (img != null)
                    {
                        try { img.Save(pngPath); }
                        catch (Exception ex)
                        {
                            Log.Error("CSAExport: save OBJ slot " + s + ": " + ex.Message);
                        }
                    }
                    else
                    {
                        SaveDummyPng(pngPath,
                            MagicEffectExportCore.OBJ_EXPORT_WIDTH,
                            MagicEffectExportCore.OBJ_EXPORT_HEIGHT);
                    }
                }

                // Render and save unique CSA BG frames (TSA-composited).
                // #886 fix: enumerate the SHARED-space slot indices from sharedBgSlots
                // (deduplicated, insertion-order), not a fresh 0-based counter.
                // RenderCsaBgFrameSlot and the b_NNN.png filenames BOTH use the shared
                // index space (OBJ slots registered first — e.g. 1 OBJ → BG slot 1,
                // not slot 0). This mirrors ExportMagicScriptLines's b_NNN naming and
                // the #880 FEditor approach where animeHash is a single shared list.
                var seenBgSlots = new System.Collections.Generic.HashSet<int>();
                var uniqueBgSharedIndices = new System.Collections.Generic.List<int>();
                foreach (int si in sharedBgSlots)
                {
                    if (seenBgSlots.Add(si))
                        uniqueBgSharedIndices.Add(si);
                }
                int bgSlotCount = uniqueBgSharedIndices.Count;
                foreach (int sharedSlot in uniqueBgSharedIndices)
                {
                    // Pass the SHARED-space index to RenderCsaBgFrameSlot so it
                    // locates the correct BG frame in the OBJ+BG shared hash.
                    IImage? img = MagicEffectExportCore.RenderCsaBgFrameSlot(rom, frames, sharedSlot);
                    // Filename uses the same shared-space index as the .txt script
                    // (e.g. b_001.png when OBJ has slot 0 and this BG has slot 1).
                    string pngPath = Path.Combine(
                        basedir, basename + "b_" + sharedSlot.ToString("000") + ".png");
                    if (img != null)
                    {
                        try { img.Save(pngPath); }
                        catch (Exception ex)
                        {
                            Log.Error("CSAExport: save BG slot " + sharedSlot + ": " + ex.Message);
                        }
                    }
                    else
                    {
                        // Fallback dummy: use full 240×160 or 240×64 depending on TSA.
                        SaveDummyPng(pngPath,
                            MagicEffectExportCore.CSA_BG_EXPORT_WIDTH,
                            MagicEffectExportCore.CSA_BG_EXPORT_HEIGHT_FULL);
                    }
                }

                // Write .txt script.
                var textLines = new List<string>(scriptLines.Count);
                foreach (var line in scriptLines)
                    textLines.Add(line.Text);
                File.WriteAllLines(filename, textLines, System.Text.Encoding.UTF8);

                // Update resource cache so OpenSource/SelectSource become visible.
                int idx = EntryList.SelectedOriginalIndex;
                if (idx >= 0 && CoreState.ResourceCache is EtcCacheResource rcache)
                {
                    string key = "MagicAnimation_" + U.ToHexString((uint)(idx + 1));
                    rcache.Update(key, filename);
                    UpdateSourceButtonVisibility();
                }

                // Reveal in file manager (best-effort, mirrors FEditor export).
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = basedir,
                        UseShellExecute = true,
                    });
                }
                catch { /* best-effort */ }

                CoreState.Services?.ShowInfo(
                    R._("Exported {0} OBJ + {1} BG frames to {2}",
                        objSlotCount, bgSlotCount, filename));
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.ErrorF("CSAExport: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Export failed: {0}", ex.Message));
                return ex.Message;
            }
        }

        // #886 — OpenSource (mirrors FEditor view OpenSource_Click).
        void OpenSource_Click(object? sender, RoutedEventArgs e)
        {
            int idx = EntryList.SelectedOriginalIndex;
            if (idx < 0) return;
            string key = "MagicAnimation_" + U.ToHexString((uint)(idx + 1));
            if (CoreState.ResourceCache is EtcCacheResource cache
                && cache.TryGetValue(key, out string? path)
                && !string.IsNullOrEmpty(path)
                && File.Exists(path))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    Log.ErrorF("ImageMagicCSACreatorView.OpenSource: {0}", ex.Message);
                    CoreState.Services?.ShowError(R._("Cannot open file: {0}", ex.Message));
                }
            }
            else
            {
                CoreState.Services?.ShowError(R._("Source file not recorded or not found."));
            }
        }

        // #886 — SelectSource: reveal containing folder in file manager.
        void SelectSource_Click(object? sender, RoutedEventArgs e)
        {
            int idx = EntryList.SelectedOriginalIndex;
            if (idx < 0) return;
            string key = "MagicAnimation_" + U.ToHexString((uint)(idx + 1));
            if (CoreState.ResourceCache is EtcCacheResource cache
                && cache.TryGetValue(key, out string? path)
                && !string.IsNullOrEmpty(path)
                && File.Exists(path))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(path);
                    if (dir != null)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dir,
                            UseShellExecute = true,
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorF("ImageMagicCSACreatorView.SelectSource: {0}", ex.Message);
                    CoreState.Services?.ShowError(R._("Cannot open folder: {0}", ex.Message));
                }
            }
            else
            {
                CoreState.Services?.ShowError(R._("Source file not recorded or not found."));
            }
        }

        void Editor_Click(object? sender, RoutedEventArgs e)
        {
            // #996: seed the Animation Creator from the SELECTED CSA entry's 0x86
            // frame-data stream (CSA Creator 32-byte frame format, +28 TSA) instead
            // of opening it blank. Symmetric with ImageMagicFEditorView.JumpEditor_Click.
            try
            {
                var rom = CoreState.ROM;
                if (rom == null) return;
                if (_vm.MagicKind != MagicSystemKind.CsaCreator)
                {
                    CoreState.Services?.ShowInfo(R._("Magic system not detected."));
                    return;
                }
                int idx = EntryList.SelectedOriginalIndex;
                if (idx < 0)
                {
                    CoreState.Services?.ShowInfo(R._("No magic-animation entry selected."));
                    return;
                }
                uint id = (uint)(idx + 1);
                uint frameDataAddr = _vm.P0;
                uint off = U.toOffset(frameDataAddr);
                if (!U.isSafetyOffset(off, rom))
                {
                    CoreState.Services?.ShowInfo(R._("Frame-data pointer 0x{0:X} is outside the ROM.", frameDataAddr));
                    return;
                }
                // Probe FIRST — do NOT open a blank Creator on an empty/terminator
                // stream (#1116). Only open once frames are confirmed present.
                int frameCount = ToolAnimationCreatorViewViewModel.CountMagicFrames(frameDataAddr, isCsa: true);
                if (frameCount <= 0)
                {
                    CoreState.Services?.ShowInfo(R._("No magic frames found at 0x{0:X}.", frameDataAddr));
                    return;
                }
                string hint = R._("Magic Animation (CSA) #{0:X2}", id);
                var view = WindowManager.Instance.Open<ToolAnimationCreatorView>();
                view.InitFromMagicRom(AnimationTypeEnum.MagicAnime_CSACreator, id, hint, frameDataAddr, isCsa: true);
            }
            // Core Log.Error is params string[] (string.Join, NO composite
            // formatting) — a literal "{0}" would be logged verbatim, so use a
            // single interpolated string with the full exception (#969 precedent).
            catch (Exception ex) { Log.Error($"ImageMagicCSACreatorView.Editor_Click: {ex}"); }
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
