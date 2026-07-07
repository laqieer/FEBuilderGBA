// SPDX-License-Identifier: GPL-3.0-or-later
using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Avalonia counterpart of WinForms `SkillConfigSkillSystemForm`.
    /// Phase 1/2/4/5/6 gap-sweep fix (#427) raises the AXAML control surface
    /// from 7 to MEDIUM-verdict density and wires the textId + animation-
    /// pointer write under a single UndoService scope. Real image/animation
    /// import/export, bulk import/export, list expand, and editor-jump still
    /// depend on Core extraction work tracked by #500 - those buttons render
    /// so the density verdict moves, but their click handlers are intentional
    /// no-ops with a tooltip until the Core seam lands (mirrors the pattern
    /// established by PR #516 for SkillConfigFE8UCSkillSys09xView).
    /// </summary>
    public partial class SkillConfigSkillSystemView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SkillConfigSkillSystemViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        readonly SkillConfigAnimePreview _animePreview = new();
        bool _suppressZoomChange;
        bool _suppressFrameChange;
        Bitmap? _currentIconBitmap;
        Bitmap? _currentPreviewBitmap;

        public string ViewTitle => "Skill Config (SkillSystem)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Skill Config (SkillSystem)", 980, 700, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public SkillConfigSkillSystemView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            DisposeBitmap(ref _currentIconBitmap);
            DisposeBitmap(ref _currentPreviewBitmap);
            _animePreview.Clear();
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

        static void DisposeBitmap(ref Bitmap? bmp)
        {
            if (bmp == null) return;
            try { bmp.Dispose(); } catch { /* swallow */ }
            bmp = null;
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                // Pass the cached IconBaseAddress (already resolved by
                // LoadList) into the icon loader so it doesn't re-run the
                // full 0xB00000..0xC00000 byte-pattern scan per row -
                // Copilot bot review on PR #525.
                uint cachedIconBase = _vm.IconBaseAddress;
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.SkillIconLoader(items, i, cachedIconBase));
                // #743: unified top-bar surfaces ReadStart / ReadCount via the
                // EditorTopBarWithInputs CLR properties; the legacy
                // ReadStartAddressBox / ReadCountBox names are gone.
                if (TopBar != null)
                {
                    TopBar.ReadStartAddress = _vm.ReadStartAddress;
                    TopBar.ReadCount = (int)_vm.ReadCount;
                }

                _suppressZoomChange = true;
                try
                {
                    ShowZoomComboBox.SelectedIndex = 0;
                    _vm.ShowZoomed = true;
                    PreviewImage.Stretch = global::Avalonia.Media.Stretch.Uniform;
                }
                finally { _suppressZoomChange = false; }
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillConfigSkillSystemView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        // #743: routed event from the unified EditorTopBarWithInputs Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e) => LoadList();

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SkillConfigSkillSystemView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            // Selection-bar widgets.
            AddressBox.Value = _vm.CurrentAddr;
            BlockSizeBox.Value = _vm.BlockSize;
            SelectedAddressLabel.Content = $"0x{_vm.CurrentAddr:X08}";

            // Icon address label - derive from icon-base + 128 * id (same as WF).
            const uint TILE_SIZE = 128; // 16x16 4bpp
            uint iconAddr = _vm.IconBaseAddress + TILE_SIZE * _vm.SelectedId;
            IconAddrLabel.Content = $"0x{iconAddr:X08}";

            // Edit fields.
            TextDetailBox.Value = _vm.TextDetail;
            string textPreview = _vm.TextDetail != 0
                ? NameResolver.GetTextById(_vm.TextDetail)
                : "";
            TextDetailTextBox.Text = textPreview ?? "";
            AnimationPointerBox.Value = _vm.AnimationPointer;

            // Icon Image - render the per-skill icon from the striped table.
            try
            {
                ROM rom = CoreState.ROM;
                if (rom != null && _vm.IconBaseAddress != 0)
                {
                    using var img = PreviewIconHelper.LoadSkillIcon(_vm.SelectedId, _vm.IconBaseAddress);
                    Bitmap? bmp = img != null ? ImageConversionHelper.ToAvaloniaBitmap(img) : null;
                    SetIconBitmap(bmp);
                }
                else
                {
                    SetIconBitmap(null);
                }
            }
            catch { SetIconBitmap(null); }

            // Animation panel - only when the resolved pointer is safe.
            AnimationPanel.IsVisible = _vm.IsAnimationValid;

            if (_vm.IsAnimationValid)
            {
                // #1010 — render the per-frame preview via the cross-platform
                // READ-ONLY SkillSystemsAnimeExportCore decode (cached by anime
                // pointer in _animePreview).
                bool hasFrames = _animePreview.Load(CoreState.ROM, _vm.AnimationPointer);
                int frameCount = _animePreview.FrameCount;
                // Clamp SelectedFrame into range BEFORE rendering (a shorter
                // animation on a same-row pointer change).
                if (frameCount > 0 && _vm.SelectedFrame >= (uint)frameCount) _vm.SelectedFrame = (uint)(frameCount - 1);
                _suppressFrameChange = true;
                try
                {
                    ShowFrameUpDown.Maximum = Math.Max(0, frameCount - 1);
                    ShowFrameUpDown.Value = _vm.SelectedFrame;
                }
                finally { _suppressFrameChange = false; }
                BinInfoBox.Text = _vm.BinInfoText;
                SetPreviewBitmap(hasFrames ? _animePreview.TryGetFrameBitmap((int)_vm.SelectedFrame) : null);
            }
            else
            {
                _animePreview.Clear();
                SetPreviewBitmap(null);
                BinInfoBox.Text = "";
            }
        }

        void WriteButton_Click(object? sender, RoutedEventArgs e)
        {
            // Early-guard so we don't create no-op undo entries when the
            // VM hasn't loaded an entry yet.
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;

            _undoService.Begin("Edit Skill Config (SkillSystem)");
            try
            {
                _vm.TextDetail = (uint)(TextDetailBox.Value ?? 0);
                _vm.AnimationPointer = (uint)(AnimationPointerBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                // #1010 — the animation pointer may have changed; drop the
                // cached decode and re-render the preview for the new pointer.
                _animePreview.Clear();
                UpdateUI();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SkillConfigSkillSystemView.Write failed: {0}", ex.Message);
            }
        }

        void SetIconBitmap(Bitmap? bmp)
        {
            if (_currentIconBitmap != null && !ReferenceEquals(_currentIconBitmap, bmp))
            {
                try { _currentIconBitmap.Dispose(); } catch { /* swallow */ }
            }
            _currentIconBitmap = bmp;
            IconImage.Source = bmp;
        }

        void SetPreviewBitmap(Bitmap? bmp)
        {
            if (_currentPreviewBitmap != null && !ReferenceEquals(_currentPreviewBitmap, bmp))
            {
                try { _currentPreviewBitmap.Dispose(); } catch { /* swallow */ }
            }
            _currentPreviewBitmap = bmp;
            PreviewImage.Source = bmp;
        }

        // -----------------------------------------------------------
        // No-op handlers - wired so the AutomationIds are enumerable
        // from headless tests, and so the density verdict moves. The
        // real implementations depend on Core extraction tracked by
        // #500. Mirrors the exact pattern used by #433 and PR #516.
        // -----------------------------------------------------------

        // Skill palette pointer is a fixed ROM location for SkillSystem (and
        // CSkillSys) — mirrors WinForms `SkillPalettePointer = 0x22370`.
        const uint SKILL_PALETTE_POINTER = 0x22370;

        // #898 — real skill-icon Image Import/Export via the shared
        // SkillConfigIconIoHelper. Icon byte-address is the striped table
        // slot IconBaseAddress + 128 * SelectedId (re-derived fresh here),
        // and the palette is the fixed skill-palette pointer.
        async void ImageImport_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null || !_vm.IsLoaded) return;
            if (_vm.IconBaseAddress == 0) return;
            if (!U.isSafetyOffset(SKILL_PALETTE_POINTER + 3, rom)) return;

            uint iconByteAddr = _vm.IconBaseAddress + SkillConfigIconIoHelper.IconByteSize * _vm.SelectedId;
            uint paletteAddr = rom.p32(SKILL_PALETTE_POINTER);

            string? err = await SkillConfigIconIoHelper.ImportIconAsync(
                TopLevel.GetTopLevel(this) as Window, rom, iconByteAddr, paletteAddr, _undoService);
            if (err == null) return; // user cancelled — do not refresh.
            if (err != "")
            {
                Log.Notify("SkillConfigSkillSystemView.ImageImport_Click: " + err);
                return;
            }

            // Success: refresh the icon preview + list thumbnails.
            UpdateUI();
            LoadList();
            EntryList.SelectAddress(_vm.CurrentAddr);
        }

        // #1397 — FE-Repo button: pick a 16x16 skill icon from the FE-Repo
        // "Special - Skill Icons" folder and route it through the SAME
        // path-taking import core (16x16 strict + remap onto the ROM's 16-color
        // skill palette → 17+-color sheets are reduced, not corrupted). No
        // second import code path.
        async void FERepo_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null || !_vm.IsLoaded) return;
            if (_vm.IconBaseAddress == 0) return;
            if (!U.isSafetyOffset(SKILL_PALETTE_POINTER + 3, rom)) return;

            string? path = await FERepoPickHelper.PickForEditor(TopLevel.GetTopLevel(this) as Window,
                FERepoResourceBrowser.FERepoEditorKind.SkillIcon);
            if (string.IsNullOrEmpty(path)) return;

            uint iconByteAddr = _vm.IconBaseAddress + SkillConfigIconIoHelper.IconByteSize * _vm.SelectedId;
            uint paletteAddr = rom.p32(SKILL_PALETTE_POINTER);

            string? err = SkillConfigIconIoHelper.ImportIconFromPath(
                rom, iconByteAddr, paletteAddr, _undoService, path);
            if (err == null) return; // nothing chosen.
            if (err != "")
            {
                Log.Notify("SkillConfigSkillSystemView.FERepo_Click: " + err);
                return;
            }

            UpdateUI();
            LoadList();
            EntryList.SelectAddress(_vm.CurrentAddr);
        }

        async void ImageExport_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null || !_vm.IsLoaded) return;
            if (_vm.IconBaseAddress == 0) return;
            if (!U.isSafetyOffset(SKILL_PALETTE_POINTER + 3, rom)) return;

            uint iconByteAddr = _vm.IconBaseAddress + SkillConfigIconIoHelper.IconByteSize * _vm.SelectedId;
            uint paletteAddr = rom.p32(SKILL_PALETTE_POINTER);

            await SkillConfigIconIoHelper.ExportIconAsync(TopLevel.GetTopLevel(this) as Window, rom, iconByteAddr, paletteAddr);
        }

        // #913 SLICE 1 — real skill-anime import via the cross-platform
        // SkillSystemsAnimeImportCore seam (FE8J path; FE8U shows a clean
        // not-supported message and mutates ZERO bytes).
        async void AnimationImport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            bool ok = await SkillConfigAnimeImportHelper.ImportAsync(
                TopLevel.GetTopLevel(this) as Window, _vm.AnimationPointer, _undoService);
            if (!ok) return;

            // Success: the animation bytes changed — drop the cached decode,
            // then refresh the animation preview + the list thumbnails.
            _animePreview.Clear();
            OnSelected(_vm.CurrentAddr);
            LoadList();
            EntryList.SelectAddress(_vm.CurrentAddr);
        }

        // #910 — real animation export via the cross-platform
        // SkillSystemsAnimeExportCore seam (.txt script + per-frame PNGs, or
        // an animated GIF). Import stays WinForms-side (separate PR).
        async void AnimationExport_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            await SkillConfigAnimeExportHelper.ExportAsync(TopLevel.GetTopLevel(this) as Window, _vm.AnimationPointer, _vm.SelectedId);
        }

        void JumpToEditor_Click(object? sender, RoutedEventArgs e)
        {
            // #1115: seed the Animation Creator from the selected skill's animation
            // (read-only). Probe-before-open so a 0/empty pointer shows an honest
            // message instead of a blank Creator. Replaces the #996 carve-out.
            if (!_vm.IsLoaded) return;
            SkillConfigAnimeJumpHelper.JumpToCreator(
                _vm.SelectedId, _vm.AnimationPointer, "SkillConfigSkillSystemView");
        }

        // #923 SLICE 2 — real bulk IMPORT via the cross-platform BULK-ATOMIC
        // SkillConfigSkillSystemBulkImportCore seam. Reads a *.SkillConfig.tsv
        // (one `textID<TAB>animePtr` row per skill) and, for each skill with an
        // `anime{i:hex}/anime.txt`, re-imports the animation. The whole multi-
        // skill import is ONE atomic transaction (one undo record on success,
        // byte-identical rollback on any fault).
        async void BulkImport_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null || !_vm.IsLoaded) return;

            // #1639: bulk import derives per-skill anime* directories from the
            // chosen TSV's OWN directory, so require a real local path; a SAF pick
            // (no local path) cannot resolve those sibling dirs → message on
            // Android, never silent.
            string? path = await FileDialogHelper.OpenFile(TopLevel.GetTopLevel(this),
                R._("Bulk Import Skill Config"), "*.SkillConfig.tsv", requireLocalPath: true);
            if (string.IsNullOrEmpty(path))
            {
                if (OperatingSystem.IsAndroid())
                    CoreState.Services?.ShowError(R._("Bulk skill-config import reads sibling animation directories and requires desktop file-system access; it is not available on this device."));
                return;
            }

            try
            {
                string basedir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";

                // The Core seam resolves each skill i's anime dir via this
                // resolver, then SCOPES every relative frame-PNG name to THAT dir
                // before calling the imageProvider (so two skills with distinct
                // anime{i}/ dirs but same-named PNGs each load their own frames —
                // #925 thread 1). The imageProvider therefore receives an already-
                // scoped path; it just loads + quantizes it (rooted as-is; a bare
                // relative fallback is resolved against basedir for safety).
                Func<uint, string> dirResolver = i =>
                    System.IO.Path.Combine(basedir, "anime" + U.ToHexString(i));

                SkillSystemsAnimeImportCore.ImageProvider imageProvider = pngName =>
                {
                    string full = System.IO.Path.IsPathRooted(pngName)
                        ? pngName
                        : System.IO.Path.Combine(basedir, pngName);
                    try
                    {
                        var lr = ImageImportService.LoadAndQuantizeFromFile(
                            full, 0, 0, maxColors: 16, strictSize: false,
                            requireTileMultiple: false);
                        if (lr == null || !lr.Success || lr.IndexedPixels == null)
                            return null;
                        return (lr.IndexedPixels, lr.Width, lr.Height, lr.GBAPalette);
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorF("SkillConfigSkillSystemView.BulkImport image load failed: {0}", ex.Message);
                        return null;
                    }
                };

                // The Core seam OWNS the undo transaction: it opens its OWN single
                // ambient BeginUndoScope wrapping the whole loop, restores the ROM
                // byte-identical on any fault (pushing ZERO records), and pushes
                // exactly ONE undo record on success. So we do NOT open a UI
                // UndoService scope here (that would clobber the Core's ambient
                // scope — BeginUndoScope is non-reentrant). #923 H3.
                string err;
                try
                {
                    err = SkillConfigSkillSystemBulkImportCore.ImportAll(
                        rom, _vm.TextPointerLocation, _vm.AnimePointerLocation,
                        path, dirResolver, imageProvider);
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                }

                if (!string.IsNullOrEmpty(err))
                {
                    CoreState.Services?.ShowError(R._("Bulk import failed: {0}", err));
                    return;
                }

                CoreState.Services?.ShowInfo(R._("Bulk imported from: {0}", path));

                // Success: the animation bytes / pointer slots changed — drop the
                // cached decode, then refresh the VM + reload the list/preview.
                _animePreview.Clear();
                OnSelected(_vm.CurrentAddr);
                LoadList();
                EntryList.SelectAddress(_vm.CurrentAddr);
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Bulk import failed: {0}", ex.Message));
            }
        }

        // #920 SLICE 1 — real bulk EXPORT via the cross-platform READ-ONLY
        // SkillConfigSkillSystemBulkExportCore seam. Writes a *.SkillConfig.tsv
        // (one `textID<TAB>animePtr` row per skill) and, for each extended-area
        // anime, an `anime{i:hex}/anime.txt` script + per-frame PNGs. Bulk
        // IMPORT stays a no-op until SLICE 2 (#920) lands.
        async void BulkExport_Click(object? sender, RoutedEventArgs e)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null || !_vm.IsLoaded) return;

            string suggested = "skills.SkillConfig.tsv";
            // #1639: bulk export writes per-skill anime* directories next to the
            // TSV, so require a real local path; a SAF pick (no local path) cannot
            // place those siblings → message on Android, never silent.
            string? path = await FileDialogHelper.SaveFile(TopLevel.GetTopLevel(this),
                R._("Bulk Export Skill Config"),
                new[] { (R._("Skill Config TSV"), "*.SkillConfig.tsv") },
                suggested);
            if (string.IsNullOrEmpty(path))
            {
                if (OperatingSystem.IsAndroid())
                    CoreState.Services?.ShowError(R._("Bulk skill-config export writes sibling animation directories and requires desktop file-system access; it is not available on this device."));
                return;
            }

            try
            {
                string basedir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";

                // writeAnime: render each extended-area animation's frames to
                // PNGs under `anime{i:hex}/` + write `anime.txt`. Dispose each
                // UNIQUE IImage exactly once (the #912 hygiene lesson — the Core
                // export caches one IImage per OBJ id, so duplicate frames share
                // an instance).
                Action<SkillConfigBulkAnimeEntry> writeAnime = entry =>
                {
                    string animedir = System.IO.Path.Combine(basedir, entry.AnimeDirName);
                    System.IO.Directory.CreateDirectory(animedir);

                    string basename = "anime_";
                    var lines = SkillSystemsAnimeExportCore.BuildScriptLines(entry.Result, basename);

                    // #922 review thread 2: track every UNIQUE frame IImage (the
                    // Core export caches one IImage per OBJ id, so duplicate
                    // frames share an instance) and dispose them in a finally,
                    // so a mid-loop Save() throw (IO/permission/disk full) can't
                    // leak the remaining native bitmaps.
                    var unique = new System.Collections.Generic.HashSet<IImage>();
                    try
                    {
                        var written = new System.Collections.Generic.HashSet<uint>();
                        foreach (var f in entry.Result.Frames)
                        {
                            unique.Add(f.Image);
                            if (!written.Add(f.Id)) continue;
                            string imagefilename = basename.Replace(" ", "_") + "g" + f.Id.ToString("000") + ".png";
                            f.Image.Save(System.IO.Path.Combine(animedir, imagefilename));
                        }

                        System.IO.File.WriteAllLines(System.IO.Path.Combine(animedir, "anime.txt"), lines);
                    }
                    finally
                    {
                        foreach (var img in unique)
                        {
                            try { img.Dispose(); } catch { /* swallow */ }
                        }
                    }
                };

                string err = SkillConfigSkillSystemBulkExportCore.ExportAll(
                    rom, _vm.TextPointerLocation, _vm.AnimePointerLocation, path, writeAnime);

                if (!string.IsNullOrEmpty(err))
                {
                    CoreState.Services?.ShowError(R._("Bulk export failed: {0}", err));
                    return;
                }
                CoreState.Services?.ShowInfo(R._("Bulk exported to: {0}", path));
            }
            catch (Exception ex)
            {
                CoreState.Services?.ShowError(R._("Bulk export failed: {0}", ex.Message));
            }
        }

        void ShowFrameUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressFrameChange) return;
            if (!_vm.IsAnimationValid) return;
            _vm.SelectedFrame = (uint)(ShowFrameUpDown.Value ?? 0);
            BinInfoBox.Text = _vm.BinInfoText;
            // #1010 — render the selected frame from the cached decode.
            SetPreviewBitmap(_animePreview.TryGetFrameBitmap((int)_vm.SelectedFrame));
        }

        void ShowZoomComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressZoomChange) return;
            bool zoomed = ShowZoomComboBox.SelectedIndex == 0;
            _vm.ShowZoomed = zoomed;
            PreviewImage.Stretch = zoomed
                ? global::Avalonia.Media.Stretch.Uniform
                : global::Avalonia.Media.Stretch.None;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
