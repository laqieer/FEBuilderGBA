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

        // #1115: skill-seed preview decode (TSA-correct), cached by ROM+pointer.
        // Used only when AnimationKind == Skill — it re-decodes the per-frame skill
        // image via the cross-platform SkillSystemsAnimeExportCore seam (skill frames
        // need OBJ+TSA, which the MapAction single-OBJ RenderFrameImage path cannot
        // represent). Disposed in OnClosed.
        readonly Services.SkillConfigAnimePreview _skillPreview = new();

        // #1116: when the --screenshot-all self-seed writes synthetic bytes into the
        // LIVE ROM (palette + LZ77 OBJ + frame stream), it snapshots the overwritten
        // span here and restores it on window close, so seeding the Animation Creator
        // can't leak mutations into LATER editors' captures / patch-scans / table-scans
        // (the harness reuses one ROM across all editors). Null when no seed happened.
        (uint Addr, byte[] Orig)? _screenshotSeedRestore;

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

        /// <summary>
        /// Seed the view from a SKILL animation (#1115) — used by the 4 anime-capable
        /// SkillConfig editors' jump buttons. READ-ONLY: the VM forces
        /// <c>RomAddress = 0</c> so Create can never overwrite the skill-anime config
        /// (see <see cref="ToolAnimationCreatorViewViewModel.InitFromSkillRom"/>). The
        /// per-frame preview decodes the TSA-correct skill image from
        /// <see cref="ToolAnimationCreatorViewViewModel.SkillAnimePointer"/>.
        /// </summary>
        public void InitFromSkillRom(AnimationTypeEnum kind, uint id, string filehint, uint animePointer)
        {
            // Drop any prior skill decode before seeding a different pointer.
            _skillPreview.Clear();
            _vm.InitFromSkillRom(kind, id, filehint, animePointer);
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

            // #1115: skill seeds decode through OBJ+TSA+palette (the MapAction
            // single-OBJ RenderFrameImage cannot represent TSA), so for the Skill
            // kind render the TSA-correct per-frame image via the cached
            // SkillConfigAnimePreview keyed on SkillAnimePointer + the SELECTED LIST
            // INDEX (EditableMapActionFrame carries no frame ordinal). The cache owns
            // the IImage; SetImage copies pixels and does NOT take ownership, so we
            // do NOT dispose it here (Clear() in OnClosed / re-Load disposes it).
            if (_vm.AnimationKind == AnimationTypeEnum.Skill)
            {
                int index = FramesList?.SelectedIndex ?? -1;
                if (index < 0 || _vm.SkillAnimePointer == 0)
                {
                    MapActionPreview.SetImage(null);
                    return;
                }
                _skillPreview.Load(CoreState.ROM, _vm.SkillAnimePointer);
                IImage? skillImg = _skillPreview.TryGetFrameImage(index);
                MapActionPreview.SetImage(skillImg);
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

            // #1115: dispose the cached skill-anime frame IImages (Clear() disposes
            // each unique decoded frame exactly once).
            _skillPreview.Clear();

            // #1116: restore the bytes the --screenshot-all self-seed overwrote so
            // the synthetic magic stream can't leak into later editors' captures /
            // patch-scans / table-scans (the harness reuses one ROM). No undo — the
            // seed wrote without undo, and this exactly reverses it.
            if (_screenshotSeedRestore.HasValue && CoreState.ROM != null)
            {
                var (addr, orig) = _screenshotSeedRestore.Value;
                if (addr + (uint)orig.Length <= (uint)(CoreState.ROM.Data?.Length ?? 0))
                {
                    for (int i = 0; i < orig.Length; i++)
                        CoreState.ROM.write_u8(addr + (uint)i, orig[i]);
                }
            }
            _screenshotSeedRestore = null;

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
                Log.ErrorF("ToolAnimationCreatorView.BrowseImage_Click failed: {0}", ex.Message);
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
                Log.ErrorF("ToolAnimationCreatorView.Create_Click failed: {0}", ex.Message);
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
                Log.ErrorF("ToolAnimationCreatorView.DoWriteToFile picker: {0}", ex.Message);
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
            // #996/#1116/#1115: in --screenshot-all mode, seed a real populated
            // animation so the captured PNG shows the running Creator window with a
            // frame list (no available ROM carries a FEditor/CSA/SkillSystem-detectable
            // anime, so the live editor can't populate the Creator end-to-end — mirrors
            // the PointerToolView screenshot-seed precedent #1026/#966). The interactive
            // runtime never enters this branch.
            //
            // #1115: prefer the SKILL seed on a multi-byte (FE8J) ROM — that proves the
            // skill seed path (the whole point of #1115). On a non-multi-byte ROM the
            // skill SkipCode needs a per-skill .dmp template, so fall back to the magic
            // seed there.
            if (App.ScreenshotAllMode && (!_vm.IsLoaded || _vm.Frames.Count == 0))
            {
                var rom = CoreState.ROM;
                bool seeded = false;
                if (rom?.RomInfo != null && rom.RomInfo.is_multibyte)
                {
                    try { seeded = SeedDemoSkillForScreenshot(); }
                    catch (Exception ex) { Log.Error($"ToolAnimationCreatorView.SelectFirstItem skill seed: {ex}"); }
                }
                if (!seeded)
                {
                    try { SeedDemoMagicForScreenshot(); }
                    catch (Exception ex) { Log.Error($"ToolAnimationCreatorView.SelectFirstItem magic seed: {ex}"); }
                }
            }

            if (FramesList != null && FramesList.ItemCount > 0)
                FramesList.SelectedIndex = 0;
        }

        /// <summary>
        /// #1115 (screenshot mode only): plant a synthetic SkillSystems skill-anime
        /// config (5-u32 cfg + 2-frame stream + per-id LZ77 OBJ / LZ77 TSA / raw 0x20
        /// palette lists) near the tail of the LIVE (multi-byte / FE8J) ROM, then seed
        /// via <see cref="ToolAnimationCreatorViewViewModel.InitFromSkillRom"/> so the
        /// captured PNG shows the populated Creator window with a TSA-decoded skill
        /// frame. Transient (no undo) — runs ONLY under <c>--screenshot-all</c>; the
        /// overwritten span is byte-restored in <see cref="OnClosed"/>. Returns true
        /// when the seed was planted (caller skips the magic fallback).
        /// </summary>
        bool SeedDemoSkillForScreenshot()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.Data == null || rom.Data.Length < 0x8000) return false;
            if (!rom.RomInfo.is_multibyte) return false; // FE8J direct SkipCode only

            // 240x160 frame -> 30x20 = 600 TSA cells (u16 each = 1200 bytes raw).
            // OBJ sheet = 64x64 radial (8x8 = 64 tiles); each TSA cell references
            // (cellIndex % 64) so the whole screen tiles the radial sheet.
            const int OBJ_W = 64, OBJ_H = 64;
            const int COLS = 30, ROWS = 20;
            byte[] objRaw = BuildRadialTiles(OBJ_W, OBJ_H);
            byte[] objLz = LZ77.compress(objRaw);

            byte[] tsaRaw = new byte[COLS * ROWS * 2];
            for (int i = 0; i < COLS * ROWS; i++)
            {
                ushort tile = (ushort)(i % ((OBJ_W / 8) * (OBJ_H / 8))); // 0..63
                tsaRaw[i * 2 + 0] = (byte)(tile & 0xFF);
                tsaRaw[i * 2 + 1] = (byte)((tile >> 8) & 0xFF);
            }
            byte[] tsaLz = LZ77.compress(tsaRaw);

            byte[] pal = new byte[0x20];
            for (int i = 0; i < 16; i++)
            {
                ushort c = RainbowColor(i);
                pal[i * 2 + 0] = (byte)(c & 0xFF);
                pal[i * 2 + 1] = (byte)((c >> 8) & 0xFF);
            }

            // Tail layout (ascending, 4-aligned), all within the snapshot span:
            //   cfg(20) framesStream(12) framesPtr(4) tsaPtr(4) objPtr(4) palPtr(4)
            //   objLz tsaLz pal
            // We place the lists as single-entry tables (id 0 and id 1 both point to
            // the same image so frame 1 re-uses the cache — exactly like real anime).
            uint baseOff = (uint)((rom.Data.Length - 0x2000) & ~3);
            uint cfg          = baseOff;            // 5 u32
            uint framesStream = cfg + 20;           // (id,wait) pairs + 0xFFFF
            uint framesPtrTab = framesStream + 12;  // unused list base anchor
            uint tsaListTab   = framesPtrTab + 8;   // p32[id] -> tsaLz
            uint objListTab   = tsaListTab + 8;     // p32[id] -> objLz
            uint palListTab   = objListTab + 8;     // p32[id] -> pal
            uint objOff       = palListTab + 8;
            uint tsaOff       = objOff + (uint)objLz.Length; tsaOff = (tsaOff + 3) & ~3u;
            uint palOff       = tsaOff + (uint)tsaLz.Length; palOff = (palOff + 3) & ~3u;
            uint spanEnd      = palOff + (uint)pal.Length;
            if (spanEnd > (uint)rom.Data.Length) return false;

            // Snapshot the whole span for byte-identical restore on close.
            _screenshotSeedRestore = (cfg, rom.getBinaryData(cfg, spanEnd - cfg));

            // cfg: frames, tsalist, graphiclist(obj), palettelist, soundId.
            rom.write_u32(cfg + 0,  U.toPointer(framesStream));
            rom.write_u32(cfg + 4,  U.toPointer(tsaListTab));
            rom.write_u32(cfg + 8,  U.toPointer(objListTab));
            rom.write_u32(cfg + 12, U.toPointer(palListTab));
            rom.write_u32(cfg + 16, 0x0000003C); // soundId (cosmetic)

            // frames stream: (id=0,wait=4) (id=1,wait=8) + full 4-byte terminator
            // (u16 id=0xFFFF, u16 wait=0xFFFF) — matches the documented frame layout.
            rom.write_u16(framesStream + 0, 0); rom.write_u16(framesStream + 2, 4);
            rom.write_u16(framesStream + 4, 1); rom.write_u16(framesStream + 6, 8);
            rom.write_u16(framesStream + 8, 0xFFFF); rom.write_u16(framesStream + 10, 0xFFFF);

            // per-id list tables (ids 0 and 1 -> same image/tsa/pal).
            rom.write_u32(tsaListTab + 0, U.toPointer(tsaOff));
            rom.write_u32(tsaListTab + 4, U.toPointer(tsaOff));
            rom.write_u32(objListTab + 0, U.toPointer(objOff));
            rom.write_u32(objListTab + 4, U.toPointer(objOff));
            rom.write_u32(palListTab + 0, U.toPointer(palOff));
            rom.write_u32(palListTab + 4, U.toPointer(palOff));

            for (int i = 0; i < objLz.Length; i++) rom.write_u8(objOff + (uint)i, objLz[i]);
            for (int i = 0; i < tsaLz.Length; i++) rom.write_u8(tsaOff + (uint)i, tsaLz[i]);
            for (int i = 0; i < pal.Length; i++)   rom.write_u8(palOff + (uint)i, pal[i]);

            _vm.InitFromSkillRom(AnimationTypeEnum.Skill, 1,
                "Skill Animation #01 (demo)", U.toPointer(cfg));
            UpdateTitle();
            return _vm.Frames.Count > 0;
        }

        static ushort RainbowColor(int i)
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
            return (ushort)((b << 10) | (g << 5) | r);
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

            // Compute the LZ77 OBJ up front so the restore span (below) covers its
            // exact length, and bail before ANY write if it won't fit.
            byte[] raw = BuildRadialTiles(64, 64);
            byte[] compressed = LZ77.compress(raw);
            if (compressed.Length > (int)(palOffset - objOffset)) return;

            // #1116: snapshot the ONE contiguous span we are about to overwrite
            // [frameBase .. end-of-palette) BEFORE writing, so OnClosed can restore
            // it byte-identical and keep --screenshot-all order-independent. The
            // three regions sit within ~0x900 bytes of each other near the ROM tail,
            // so a single span is small (~2.3 KB) and avoids per-region bookkeeping.
            uint spanStart = frameBase; // frameBase < objOffset < palOffset (guarded)
            uint spanEnd = Math.Max(palOffset + 0x20,
                Math.Max(objOffset + (uint)compressed.Length, frameBase + 60));
            uint spanLen = spanEnd - spanStart;
            if (spanEnd > (uint)rom.Data.Length) return;
            _screenshotSeedRestore = (spanStart, rom.getBinaryData(spanStart, spanLen));

            // Rainbow palette (RGB555 LE), index 0 dark so ring edges read clearly.
            for (int i = 0; i < 16; i++)
            {
                ushort c = RainbowColor(i);
                rom.write_u8(palOffset + (uint)(i * 2 + 0), (uint)(c & 0xFF));
                rom.write_u8(palOffset + (uint)(i * 2 + 1), (uint)((c >> 8) & 0xFF));
            }

            // Radial-ring 64x64 4bpp OBJ, LZ77-compressed (computed + fit-checked above).
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
