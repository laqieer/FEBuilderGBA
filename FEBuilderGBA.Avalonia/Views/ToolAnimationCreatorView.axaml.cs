// SPDX-License-Identifier: GPL-3.0-or-later
// Code-behind for ToolAnimationCreatorView — issue #500.
//
// Two public entry points mirror the WF Init surface:
//   - InitFromRom(kind, id, hint, romAddress) — direct from a ROM frame
//     table (used by ImageMapActionAnimationView's "Open in Creator" button).
//   - InitFromFile(kind, id, hint, filename)  — from a .txt script previously
//     emitted by WF Export or by Core's WriteMapActionScript.
//
// Create_Click branches on context (ROM vs file). BrowseImage_Click is wired
// through the StorageProvider file picker.
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA; // Core: ImageUtilMapActionAnimationCore, CoreState, IImage
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolAnimationCreatorView : TranslatedWindow, IEditorView
    {
        readonly ToolAnimationCreatorViewViewModel _vm = new();
        readonly UndoService _undoService = new();

        // The frame whose PropertyChanged we are currently subscribed to so the
        // live preview re-renders when its Image/Palette pointer changes. We must
        // unsubscribe before re-subscribing (and on close) to avoid leaks.
        EditableMapActionFrame? _previewTrackedFrame;

        public string ViewTitle => "Animation Creator";
        public bool IsLoaded => _vm.IsLoaded;

        /// <summary>True when the VM has at least one frame loaded (#996) — lets
        /// magic callers detect an empty-seed result after Init.</summary>
        public bool HasFrames => _vm.Frames.Count > 0;

        public ToolAnimationCreatorView()
        {
            InitializeComponent();
            // Bind the entire view tree against _vm so the AXAML
            // `{Binding AnimationName}` etc. resolve correctly. The previous
            // stub omitted this, so TextBoxes never updated when the VM
            // changed (Copilot CLI plan-review pt 2 on #500).
            DataContext = _vm;
            _vm.Initialize();
        }

        /// <summary>
        /// Init the view directly from a ROM frame table — used by the Map
        /// Action Animation entry point (no temp file involved).
        /// </summary>
        public void InitFromRom(AnimationTypeEnum kind, uint id, string filehint, uint romAddress)
        {
            _vm.InitFromRom(kind, id, filehint, romAddress);
            UpdateTitle();
        }

        /// <summary>
        /// Init the view from a .txt script — kept for parity with WF
        /// <c>ToolAnimationCreatorForm.Init(filename)</c>. Callers should
        /// note that the source file is **read at Init time only**; deleting
        /// it after Init returns is safe (the VM has copied everything it
        /// needs).
        /// </summary>
        public void InitFromFile(AnimationTypeEnum kind, uint id, string filehint, string filename)
        {
            _vm.InitFromFile(kind, id, filehint, filename);
            UpdateTitle();
        }

        /// <summary>
        /// Seed the view from a MAGIC animation frame-data stream (#996) — used by
        /// the FEditor / CSA Creator magic editors' jump buttons. READ-ONLY: the
        /// VM forces <c>RomAddress = 0</c> so Create can never overwrite the magic
        /// stream (see <see cref="ToolAnimationCreatorViewViewModel.InitFromMagicRom"/>).
        /// </summary>
        public void InitFromMagicRom(AnimationTypeEnum kind, uint id, string filehint, uint frameDataAddr, bool isCsa)
        {
            _vm.InitFromMagicRom(kind, id, filehint, frameDataAddr, isCsa);
            UpdateTitle();
        }

        void UpdateTitle()
        {
            string hint = string.IsNullOrEmpty(_vm.FileHint) ? "" : ": " + _vm.FileHint;
            Title = R._("Animation Creator{0}", hint);
        }

        void FramesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Mirror the list selection into _vm.SelectedFrame so the right-
            // pane edit controls follow the active row.
            var frame = FramesList.SelectedItem as EditableMapActionFrame;
            _vm.SelectedFrame = frame;

            // Track the newly-selected frame so the live preview re-renders when
            // its Image/Palette pointer changes. Unsubscribe from the previously
            // tracked frame first to avoid handler leaks.
            if (!ReferenceEquals(_previewTrackedFrame, frame))
            {
                if (_previewTrackedFrame != null)
                    _previewTrackedFrame.PropertyChanged -= OnTrackedFramePropertyChanged;
                _previewTrackedFrame = frame;
                if (_previewTrackedFrame != null)
                    _previewTrackedFrame.PropertyChanged += OnTrackedFramePropertyChanged;
            }

            RenderPreview();
        }

        void OnTrackedFramePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Re-render whenever the image or palette pointer changes (null name =
            // bulk reset → re-render to be safe).
            if (e.PropertyName == null
                || e.PropertyName == nameof(EditableMapActionFrame.ImagePointer)
                || e.PropertyName == nameof(EditableMapActionFrame.PalettePointer))
            {
                RenderPreview();
            }
        }

        /// <summary>
        /// Read-only live preview of the selected Map-Action frame. Decodes the
        /// frame's OBJ + palette through the Core RenderFrameImage seam and pushes
        /// the result into the GbaImageControl. No ROM mutation, no undo.
        /// </summary>
        void RenderPreview()
        {
            if (MapActionPreview == null) return;

            var f = _vm.SelectedFrame;
            if (f == null || CoreState.ROM == null)
            {
                MapActionPreview.SetImage(null);
                return;
            }

            // GbaImageControl.SetImage synchronously copies the pixels into a
            // WriteableBitmap and does NOT take ownership of the source IImage, so
            // dispose the freshly-decoded image here — otherwise frequent
            // re-renders (selection / pointer-edit changes) leak native Skia
            // memory. Copilot review on PR #1077.
            using IImage? img = ImageUtilMapActionAnimationCore.RenderFrameImage(
                CoreState.ROM, f.ImagePointer, f.PalettePointer);
            MapActionPreview.SetImage(img);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_previewTrackedFrame != null)
            {
                _previewTrackedFrame.PropertyChanged -= OnTrackedFramePropertyChanged;
                _previewTrackedFrame = null;
            }
            base.OnClosed(e);
        }

        async void BrowseImage_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string? picked = await FileDialogHelper.OpenImageFile(this);
                if (string.IsNullOrEmpty(picked)) return;
                // If a frame is selected, the user is replacing THAT frame's
                // image (most common workflow when editing a specific row).
                // Otherwise we stash the path on the global ImageSource field
                // so the user can see what they picked.
                if (_vm.SelectedFrame != null)
                    _vm.SelectedFrame.ImageName = picked;
                else
                    _vm.ImageSource = picked;
            }
            catch (Exception ex)
            {
                Log.Error("ToolAnimationCreatorView.BrowseImage_Click failed: {0}", ex.Message);
            }
        }

        async void Create_Click(object? sender, RoutedEventArgs e)
        {
            // Branches on context:
            //   - ROM path  → write frame metadata back to the ROM frame
            //                  table via ToolAnimationCreatorCore.WriteToRom
            //                  (wrapped in an UndoService scope).
            //   - File path → prompt SaveFilePickerAsync (default to the
            //                  current SourceFilename) and call
            //                  WriteMapActionScript.
            //   - Neither   → info dialog (no source loaded).
            try
            {
                if (!_vm.IsLoaded)
                {
                    CoreState.Services.ShowInfo(R._("No animation context loaded."));
                    return;
                }
                if (_vm.CanWriteBackToRom)
                {
                    DoWriteToRom();
                    return;
                }
                if (!string.IsNullOrEmpty(_vm.SourceFilename))
                {
                    await DoWriteToFile();
                    return;
                }
                CoreState.Services.ShowInfo(R._("No source loaded."));
            }
            catch (Exception ex)
            {
                Log.Error("ToolAnimationCreatorView.Create_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Save failed: {0}", ex.Message));
            }
        }

        void DoWriteToRom()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                CoreState.Services.ShowInfo(R._("No ROM loaded."));
                return;
            }
            _undoService.Begin("Animation Creator: Write Frames");
            try
            {
                var projected = _vm.ProjectFrames();
                ToolAnimationCreatorCore.WriteToRom(rom, _vm.RomAddress, projected, undoData: null);
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo(R._("Wrote {0} frames to ROM at 0x{1:X}.",
                    projected.Count, _vm.RomAddress));
            }
            catch
            {
                _undoService.Rollback();
                throw;
            }
        }

        async Task DoWriteToFile()
        {
            string? suggested = _vm.SourceFilename;
            string suggestedName = string.IsNullOrEmpty(suggested)
                ? "anim.txt"
                : System.IO.Path.GetFileName(suggested);
            string? saveTo = null;
            try
            {
                saveTo = await FileDialogHelper.SaveAnimationScriptFile(this, suggestedName);
            }
            catch (Exception ex)
            {
                Log.Error("ToolAnimationCreatorView.DoWriteToFile picker: {0}", ex.Message);
            }
            if (string.IsNullOrEmpty(saveTo)) return;

            var projected = _vm.ProjectFrames();
            string? nameHeader = string.IsNullOrEmpty(_vm.AnimationName) ? null : _vm.AnimationName;
            ToolAnimationCreatorCore.WriteMapActionScript(saveTo, nameHeader, projected);
            _vm.MarkClean();
            CoreState.Services.ShowInfo(R._("Wrote {0} frames to {1}.", projected.Count, saveTo));
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem()
        {
            // #996/#1116: in --screenshot-all mode, seed a real populated magic
            // animation so the captured PNG shows the running Creator window with a
            // frame list (no available ROM carries the FEditor/CSA patch, so the live
            // magic editor can't populate the Creator end-to-end — mirrors the
            // PointerToolView screenshot-seed precedent #1026/#966). The interactive
            // runtime never enters this branch.
            if (App.ScreenshotAllMode && (!_vm.IsLoaded || _vm.Frames.Count == 0))
            {
                try { SeedDemoMagicForScreenshot(); }
                catch (Exception ex) { Log.Error($"ToolAnimationCreatorView.SelectFirstItem seed: {ex}"); }
            }

            if (FramesList != null && FramesList.ItemCount > 0)
                FramesList.SelectedIndex = 0;
        }

        /// <summary>
        /// #996/#1116 (screenshot mode only): plant a synthetic 2-frame magic 0x86
        /// FEditor stream (radial-ring 64x64 LZ77 OBJ + rainbow palette) into scratch
        /// regions near the tail of the LIVE ROM, then seed via
        /// <see cref="ToolAnimationCreatorViewViewModel.InitFromMagicRom"/> so the
        /// captured PNG shows the populated Creator window. Transient (no undo) — this
        /// runs ONLY under <c>--screenshot-all</c>.
        /// </summary>
        void SeedDemoMagicForScreenshot()
        {
            var rom = CoreState.ROM;
            if (rom == null || rom.Data == null || rom.Data.Length < 0x4000) return;

            // Scratch regions near the tail, 4-aligned, non-overlapping:
            //   frameBase < objOffset < palOffset, each with room.
            uint palOffset = (uint)((rom.Data.Length - 0x40) & ~3);   // 0x20-byte palette
            uint objOffset = (uint)((rom.Data.Length - 0x800) & ~3);  // LZ77 OBJ
            uint frameBase = (uint)((rom.Data.Length - 0x900) & ~3);  // 2x28B frames + terminator
            if (!(frameBase < objOffset && objOffset < palOffset)) return;
            if (palOffset + 0x20 > (uint)rom.Data.Length) return;

            // Rainbow palette (RGB555 LE), index 0 dark so ring edges read clearly.
            for (int i = 0; i < 16; i++)
            {
                int r, g, b;
                if (i == 0) { r = g = b = 2; }
                else
                {
                    double h = (i - 1) / 15.0 * 6.0;
                    int seg = (int)h; double f = h - seg;
                    int v = 31, p = 4, q = (int)(31 * (1 - f)), t = (int)(31 * f);
                    switch (seg % 6)
                    {
                        case 0: r = v; g = t; b = p; break;
                        case 1: r = q; g = v; b = p; break;
                        case 2: r = p; g = v; b = t; break;
                        case 3: r = p; g = q; b = v; break;
                        case 4: r = t; g = p; b = v; break;
                        default: r = v; g = p; b = q; break;
                    }
                }
                ushort c = (ushort)((b << 10) | (g << 5) | r);
                rom.write_u8(palOffset + (uint)(i * 2 + 0), (uint)(c & 0xFF));
                rom.write_u8(palOffset + (uint)(i * 2 + 1), (uint)((c >> 8) & 0xFF));
            }

            // Radial-ring 64x64 4bpp OBJ, LZ77-compressed; bail if it won't fit.
            byte[] raw = BuildRadialTiles(64, 64);
            byte[] compressed = LZ77.compress(raw);
            if (compressed.Length > (int)(palOffset - objOffset)) return;
            for (int i = 0; i < compressed.Length; i++)
                rom.write_u8(objOffset + (uint)i, compressed[i]);

            // Two 28-byte 0x86 FEditor frames + 0x80 terminator.
            WriteMagicFrame(rom, frameBase, wait: 4, objOffset: objOffset, palOffset: palOffset);
            WriteMagicFrame(rom, frameBase + 28, wait: 6, objOffset: objOffset, palOffset: palOffset);
            rom.write_u8(frameBase + 56 + 3, 0x80); // terminator

            _vm.InitFromMagicRom(AnimationTypeEnum.MagicAnime_FEEDitor, 1,
                "Magic Animation (FEditor) #01 (demo)", U.toPointer(frameBase), isCsa: false);
            UpdateTitle();
        }

        static void WriteMagicFrame(ROM rom, uint n, uint wait, uint objOffset, uint palOffset)
        {
            rom.write_u8(n + 0, wait & 0xFF);
            rom.write_u8(n + 1, (wait >> 8) & 0xFF);
            rom.write_u8(n + 3, 0x86);
            rom.write_u32(n + 4,  U.toPointer(objOffset)); // OBJ img
            rom.write_u32(n + 16, U.toPointer(objOffset)); // BG img
            rom.write_u32(n + 20, U.toPointer(palOffset)); // OBJ pal
            rom.write_u32(n + 24, U.toPointer(palOffset)); // BG pal
        }

        static byte[] BuildRadialTiles(int w, int h)
        {
            byte[] tiles = new byte[w * h / 2];
            int tilesW = w / 8;
            int cx = w / 2, cy = h / 2;
            int p = 0;
            for (int ty = 0; ty < h / 8; ty++)
                for (int tx = 0; tx < tilesW; tx++)
                    for (int py = 0; py < 8; py++)
                        for (int px = 0; px < 8; px += 2)
                        {
                            int x0 = tx * 8 + px, y0 = ty * 8 + py;
                            int lo = RingIndex(x0, y0, cx, cy);
                            int hi = RingIndex(x0 + 1, y0, cx, cy);
                            tiles[p++] = (byte)((lo & 0x0F) | ((hi & 0x0F) << 4));
                        }
            return tiles;
        }

        static int RingIndex(int x, int y, int cx, int cy)
        {
            double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return ((int)(d / 3.0) % 15) + 1;
        }
    }
}
