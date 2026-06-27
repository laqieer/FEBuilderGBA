// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia counterpart of WinForms ImagePalletForm (#400 gap-sweep).
// Provides a WF-equivalent JumpTo(...) entry point so callers like
// ImagePortraitView.JumpToPalette_Click can carry the palette
// address from the source editor.
//
// All ROM writes are wrapped in UndoService.Begin/Commit/Rollback;
// Undo is wired to CoreState.Undo.RunUndo() + reload, Redo is
// wired to CoreState.Undo.RunRedo() (#994). The "#500 Core has no
// RunRedo() API" blocker was stale — RunRedo()/CanRedo have existed
// since #692.

using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImagePalletView : TranslatedWindow, IEditorView
    {
        readonly ImagePalletViewModel _vm = new();
        readonly UndoService _undoService = new();

        // Cache the original paletteNames passed via JumpTo so the
        // Palette Index combo labels survive a Write+reload or Undo
        // round-trip (Copilot bot round-3 inline reviews #2 + #3 on
        // PR #586).
        string[]? _lastPaletteNames;

        // Live bitmap-of-the-graphic preview (#1023). The caller (eg.
        // ImagePortraitView) supplies a render delegate that takes the
        // 16-color GBA palette block (32 bytes) currently shown in the
        // grid and returns the rendered graphic recolored with it. When
        // null, the preview is cleared. The delegate renders WITHOUT
        // writing the ROM, so an unsaved spinner edit is reflected live.
        Func<byte[], IImage?>? _renderPreview;

        // True while UpdateUI() bulk-seeds the 48 NUDs from VM state. Nud_ValueChanged
        // early-returns on it so the swatch refresh + the (potentially expensive)
        // live-preview render fire ONCE at the end of UpdateUI instead of 48 times
        // (Copilot bot review on #1087). User-driven edits run with this false.
        bool _seedingNuds;

        // Intrinsic size of the most-recently rendered preview bitmap, so
        // ApplyZoom can rescale the already-rendered image without
        // re-invoking the (potentially expensive) render delegate.
        double _previewBaseW;
        double _previewBaseH;

        public string ViewTitle => "Palette Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImagePalletView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
            // Wire all 48 R/G/B NumericUpDowns so an edit refreshes
            // the swatches immediately (Copilot bot round-3 inline
            // review #1 on PR #586).
            WireNudChangeHandlers();
        }

        void WireNudChangeHandlers()
        {
            var nuds = new[]
            {
                R1Box, G1Box, B1Box, R2Box, G2Box, B2Box,
                R3Box, G3Box, B3Box, R4Box, G4Box, B4Box,
                R5Box, G5Box, B5Box, R6Box, G6Box, B6Box,
                R7Box, G7Box, B7Box, R8Box, G8Box, B8Box,
                R9Box, G9Box, B9Box, R10Box, G10Box, B10Box,
                R11Box, G11Box, B11Box, R12Box, G12Box, B12Box,
                R13Box, G13Box, B13Box, R14Box, G14Box, B14Box,
                R15Box, G15Box, B15Box, R16Box, G16Box, B16Box,
            };
            foreach (var n in nuds) n.ValueChanged += Nud_ValueChanged;
        }

        void Nud_ValueChanged(object? sender, global::Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
            // Suppress the per-NUD swatch refresh + live render while UpdateUI is
            // bulk-seeding the 48 NUDs — otherwise each seed fires this handler and
            // the render delegate runs up to 48x per UpdateUI (Copilot bot #1087).
            if (_seedingNuds) return;
            // Re-render the 16 swatches from current NUD values so the
            // user-edit -> swatch reflection is live (Copilot bot
            // round-3 inline review #1 on PR #586). The VM is NOT
            // touched here - VM sync happens at Write time via
            // ReadNudsIntoVm().
            RefreshSwatchesFromNuds();
            // Re-render the live graphic preview from the edited colors
            // (#1023). Guarded inside RenderPreview against the bulk NUD
            // seed UpdateUI performs while IsLoading is set.
            RenderPreview();
        }

        // ---- entry / list loading ----

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePalletView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            // Address-list rows are placeholders here; selection is
            // a no-op apart from refreshing the UI.
            UpdateUI();
        }

        /// <summary>
        /// WF-equivalent entry point. Callers (eg.
        /// ImagePortraitView.JumpToPalette_Click) invoke this after
        /// Open<ImagePalletView>() to carry the palette address +
        /// multi-palette metadata (Copilot CLI plan review #2).
        /// </summary>
        public void JumpTo(uint paletteAddress, int maxPaletteCount = 1, int defaultSelectPalette = 0, string[]? paletteNames = null,
            Func<byte[], IImage?>? renderPreview = null)
        {
            try
            {
                // Store the live-preview render delegate (#1023). When non-null
                // the preview area shows the supplied graphic recolored by the
                // 16 grid colors; when null the preview is cleared.
                _renderPreview = renderPreview;

                // Cache the names so subsequent reload paths (Write,
                // Undo) keep the override labels - passing null to
                // LoadEntry would drop them (Copilot bot round-3
                // inline reviews #2 + #3 on PR #586).
                _lastPaletteNames = paletteNames;
                _vm.LoadEntry(paletteAddress, maxPaletteCount, defaultSelectPalette, paletteNames);
                UpdateUI(); // UpdateUI renders the preview once at its end.
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePalletView.JumpTo failed: {0}", ex.Message);
            }
        }

        // ---- UI sync ----

        void UpdateUI()
        {
            // Address bar (read-only; mirrors WF PALETTE_ADDRESS).
            AddressBox.Value = _vm.PaletteAddress;

            // Palette-index combo: rebuild items from VM, then select index.
            // Hide when only one palette is available (mirrors WF JumpTo behavior).
            PaletteIndexComboBox.Items.Clear();
            foreach (var name in _vm.PaletteIndexNames)
                PaletteIndexComboBox.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = name } });
            if (_vm.PaletteIndex >= 0 && _vm.PaletteIndex < PaletteIndexComboBox.Items.Count)
                PaletteIndexComboBox.SelectedIndex = _vm.PaletteIndex;

            // Only show the palette-index bar once real palette data has
            // been loaded AND there is more than one block to switch
            // between. Without the IsLoaded gate, opening from the main
            // menu (which leaves MaxPaletteCount at the 16 default and
            // PaletteIndexNames empty) would render an empty combo
            // (Copilot bot inline review #8 on PR #586).
            bool showIndex = _vm.IsLoaded
                && _vm.MaxPaletteCount > 1
                && _vm.PaletteIndexNames.Count > 0;
            PaletteIndexComboBox.IsVisible = showIndex;
            PaletteIndexLabel.IsVisible = showIndex;

            ZoomComboBox.SelectedIndex = _vm.ZoomIndex;

            // Mirror the 48 NUDs from VM state. Suppress the per-NUD handler for
            // this bulk seed so the swatch refresh + live render fire ONCE below,
            // not 48x (Copilot bot #1087).
            _seedingNuds = true;
            R1Box.Value = _vm.R1; G1Box.Value = _vm.G1; B1Box.Value = _vm.B1;
            R2Box.Value = _vm.R2; G2Box.Value = _vm.G2; B2Box.Value = _vm.B2;
            R3Box.Value = _vm.R3; G3Box.Value = _vm.G3; B3Box.Value = _vm.B3;
            R4Box.Value = _vm.R4; G4Box.Value = _vm.G4; B4Box.Value = _vm.B4;
            R5Box.Value = _vm.R5; G5Box.Value = _vm.G5; B5Box.Value = _vm.B5;
            R6Box.Value = _vm.R6; G6Box.Value = _vm.G6; B6Box.Value = _vm.B6;
            R7Box.Value = _vm.R7; G7Box.Value = _vm.G7; B7Box.Value = _vm.B7;
            R8Box.Value = _vm.R8; G8Box.Value = _vm.G8; B8Box.Value = _vm.B8;
            R9Box.Value = _vm.R9; G9Box.Value = _vm.G9; B9Box.Value = _vm.B9;
            R10Box.Value = _vm.R10; G10Box.Value = _vm.G10; B10Box.Value = _vm.B10;
            R11Box.Value = _vm.R11; G11Box.Value = _vm.G11; B11Box.Value = _vm.B11;
            R12Box.Value = _vm.R12; G12Box.Value = _vm.G12; B12Box.Value = _vm.B12;
            R13Box.Value = _vm.R13; G13Box.Value = _vm.G13; B13Box.Value = _vm.B13;
            R14Box.Value = _vm.R14; G14Box.Value = _vm.G14; B14Box.Value = _vm.B14;
            R15Box.Value = _vm.R15; G15Box.Value = _vm.G15; B15Box.Value = _vm.B15;
            R16Box.Value = _vm.R16; G16Box.Value = _vm.G16; B16Box.Value = _vm.B16;
            _seedingNuds = false;

            RefreshSwatches();
            // Single live-preview render per UpdateUI (covers JumpTo, Write, Undo,
            // Redo, palette-index switch) instead of the 48 per-NUD-seed renders.
            RenderPreview();
        }

        void RefreshSwatches()
        {
            // Render each swatch image with the current R/G/B color so the
            // user can see the slot color preview without needing live
            // Bitmap-pipeline support.
            SetSwatch(P1Swatch,  _vm.R1,  _vm.G1,  _vm.B1);
            SetSwatch(P2Swatch,  _vm.R2,  _vm.G2,  _vm.B2);
            SetSwatch(P3Swatch,  _vm.R3,  _vm.G3,  _vm.B3);
            SetSwatch(P4Swatch,  _vm.R4,  _vm.G4,  _vm.B4);
            SetSwatch(P5Swatch,  _vm.R5,  _vm.G5,  _vm.B5);
            SetSwatch(P6Swatch,  _vm.R6,  _vm.G6,  _vm.B6);
            SetSwatch(P7Swatch,  _vm.R7,  _vm.G7,  _vm.B7);
            SetSwatch(P8Swatch,  _vm.R8,  _vm.G8,  _vm.B8);
            SetSwatch(P9Swatch,  _vm.R9,  _vm.G9,  _vm.B9);
            SetSwatch(P10Swatch, _vm.R10, _vm.G10, _vm.B10);
            SetSwatch(P11Swatch, _vm.R11, _vm.G11, _vm.B11);
            SetSwatch(P12Swatch, _vm.R12, _vm.G12, _vm.B12);
            SetSwatch(P13Swatch, _vm.R13, _vm.G13, _vm.B13);
            SetSwatch(P14Swatch, _vm.R14, _vm.G14, _vm.B14);
            SetSwatch(P15Swatch, _vm.R15, _vm.G15, _vm.B15);
            SetSwatch(P16Swatch, _vm.R16, _vm.G16, _vm.B16);
        }

        /// <summary>
        /// Refresh swatches from the CURRENT NumericUpDown values rather
        /// than the VM state. Used by Nud_ValueChanged so a user edit
        /// updates the swatch immediately, before any Write/reload
        /// pushes the value into the VM (Copilot bot round-3 inline
        /// review #1 on PR #586).
        /// </summary>
        void RefreshSwatchesFromNuds()
        {
            SetSwatch(P1Swatch,  NudByte(R1Box),  NudByte(G1Box),  NudByte(B1Box));
            SetSwatch(P2Swatch,  NudByte(R2Box),  NudByte(G2Box),  NudByte(B2Box));
            SetSwatch(P3Swatch,  NudByte(R3Box),  NudByte(G3Box),  NudByte(B3Box));
            SetSwatch(P4Swatch,  NudByte(R4Box),  NudByte(G4Box),  NudByte(B4Box));
            SetSwatch(P5Swatch,  NudByte(R5Box),  NudByte(G5Box),  NudByte(B5Box));
            SetSwatch(P6Swatch,  NudByte(R6Box),  NudByte(G6Box),  NudByte(B6Box));
            SetSwatch(P7Swatch,  NudByte(R7Box),  NudByte(G7Box),  NudByte(B7Box));
            SetSwatch(P8Swatch,  NudByte(R8Box),  NudByte(G8Box),  NudByte(B8Box));
            SetSwatch(P9Swatch,  NudByte(R9Box),  NudByte(G9Box),  NudByte(B9Box));
            SetSwatch(P10Swatch, NudByte(R10Box), NudByte(G10Box), NudByte(B10Box));
            SetSwatch(P11Swatch, NudByte(R11Box), NudByte(G11Box), NudByte(B11Box));
            SetSwatch(P12Swatch, NudByte(R12Box), NudByte(G12Box), NudByte(B12Box));
            SetSwatch(P13Swatch, NudByte(R13Box), NudByte(G13Box), NudByte(B13Box));
            SetSwatch(P14Swatch, NudByte(R14Box), NudByte(G14Box), NudByte(B14Box));
            SetSwatch(P15Swatch, NudByte(R15Box), NudByte(G15Box), NudByte(B15Box));
            SetSwatch(P16Swatch, NudByte(R16Box), NudByte(G16Box), NudByte(B16Box));
        }

        static byte NudByte(NumericUpDown nud)
        {
            decimal v = nud.Value ?? 0m;
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)v;
        }

        static void SetSwatch(Image image, byte r, byte g, byte b)
        {
            // Allocate the 1x1 backing bitmap ONCE per Image control and
            // reuse it on subsequent UpdateUI calls (Copilot bot inline
            // review #9 on PR #586 - avoids per-slot WriteableBitmap
            // churn across 16 swatches x N redraws).
            WriteableBitmap bmp;
            if (image.Source is WriteableBitmap existing
                && existing.PixelSize.Width == 1
                && existing.PixelSize.Height == 1)
            {
                bmp = existing;
            }
            else
            {
                bmp = new WriteableBitmap(
                    new global::Avalonia.PixelSize(1, 1),
                    new global::Avalonia.Vector(96, 96),
                    global::Avalonia.Platform.PixelFormat.Bgra8888,
                    global::Avalonia.Platform.AlphaFormat.Unpremul);
                image.Source = bmp;
                image.Stretch = Stretch.Fill;
            }
            using (var fb = bmp.Lock())
            {
                unsafe
                {
                    byte* p = (byte*)fb.Address;
                    p[0] = b; p[1] = g; p[2] = r; p[3] = 0xFF; // BGRA
                }
            }
            // Force Avalonia to re-render the Image even though Source is
            // the same instance (WriteableBitmap mutation does not raise
            // the property-changed event by itself).
            image.InvalidateVisual();
        }

        // ---- live bitmap-of-the-graphic preview (#1023) ----

        /// <summary>
        /// Render the live preview of the source graphic recolored by the 16
        /// colors CURRENTLY in the R/G/B NumericUpDowns (so an unsaved edit is
        /// reflected immediately, without writing the ROM). When no render
        /// delegate was supplied via <see cref="JumpTo"/>, or the delegate
        /// returns null / throws, the preview is cleared. Guarded against the
        /// bulk NUD seed performed by <see cref="UpdateUI"/> via the VM
        /// IsLoading flag so a single LoadEntry doesn't trigger 48 redundant
        /// renders.
        /// </summary>
        void RenderPreview()
        {
            // Pack the 16 colors from the CURRENT control values (8-bit each,
            // clamped 0..255 by NudByte) into the 32-byte GBA BGR555 block via
            // PaletteCore.PackToBytes — the SAME byte format
            // ImageUtilCore.GetPalette(ptr,16) returns, so the rendered colors
            // match a post-Write ROM read. PackToBytes (8-bit input) is used
            // deliberately, NOT UnitPaletteWriteCore.PackRgb555 (5-bit input)
            // which would corrupt mid-range channel values.
            try
            {
                if (_renderPreview == null)
                {
                    DisposePreview();
                    PreviewImage.Source = null;
                    _previewBaseW = 0;
                    _previewBaseH = 0;
                    return;
                }

                // Bulk-seed suppression is handled by _seedingNuds in
                // Nud_ValueChanged; UpdateUI calls this exactly once at its end,
                // so it must run even when a caller holds _vm.IsLoading (e.g. the
                // Write/Undo/Redo reload paths) — otherwise those would not update
                // the preview at all.
                var colors = new (byte r, byte g, byte b)[16];
                colors[0]  = (NudByte(R1Box),  NudByte(G1Box),  NudByte(B1Box));
                colors[1]  = (NudByte(R2Box),  NudByte(G2Box),  NudByte(B2Box));
                colors[2]  = (NudByte(R3Box),  NudByte(G3Box),  NudByte(B3Box));
                colors[3]  = (NudByte(R4Box),  NudByte(G4Box),  NudByte(B4Box));
                colors[4]  = (NudByte(R5Box),  NudByte(G5Box),  NudByte(B5Box));
                colors[5]  = (NudByte(R6Box),  NudByte(G6Box),  NudByte(B6Box));
                colors[6]  = (NudByte(R7Box),  NudByte(G7Box),  NudByte(B7Box));
                colors[7]  = (NudByte(R8Box),  NudByte(G8Box),  NudByte(B8Box));
                colors[8]  = (NudByte(R9Box),  NudByte(G9Box),  NudByte(B9Box));
                colors[9]  = (NudByte(R10Box), NudByte(G10Box), NudByte(B10Box));
                colors[10] = (NudByte(R11Box), NudByte(G11Box), NudByte(B11Box));
                colors[11] = (NudByte(R12Box), NudByte(G12Box), NudByte(B12Box));
                colors[12] = (NudByte(R13Box), NudByte(G13Box), NudByte(B13Box));
                colors[13] = (NudByte(R14Box), NudByte(G14Box), NudByte(B14Box));
                colors[14] = (NudByte(R15Box), NudByte(G15Box), NudByte(B15Box));
                colors[15] = (NudByte(R16Box), NudByte(G16Box), NudByte(B16Box));

                byte[] block = PaletteCore.PackToBytes(colors);

                IImage? coreImg = _renderPreview(block);
                Bitmap? bmp = coreImg != null ? ImageConversionHelper.ToAvaloniaBitmap(coreImg) : null;
                // The Core IImage is fully consumed by the PNG encode inside
                // ToAvaloniaBitmap; dispose it if disposable so rapid spinner
                // edits don't accumulate native handles.
                (coreImg as IDisposable)?.Dispose();

                if (bmp == null)
                {
                    DisposePreview();
                    PreviewImage.Source = null;
                    _previewBaseW = 0;
                    _previewBaseH = 0;
                    return;
                }

                DisposePreview();
                _previewBaseW = bmp.PixelSize.Width;
                _previewBaseH = bmp.PixelSize.Height;
                PreviewImage.Source = bmp;
                ApplyZoom();
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePalletView.RenderPreview failed: {0}", ex.Message);
                try
                {
                    DisposePreview();
                    PreviewImage.Source = null;
                    _previewBaseW = 0;
                    _previewBaseH = 0;
                }
                catch { /* never throw to the UI */ }
            }
        }

        void DisposePreview()
        {
            if (PreviewImage.Source is IDisposable d)
                d.Dispose();
        }

        /// <summary>
        /// Apply the current Zoom-combo selection to the already-rendered
        /// preview bitmap. This NEVER re-invokes the render delegate — it only
        /// rescales the displayed image, mirroring WF where the zoom combo just
        /// changes the draw scale. Zoom item mapping:
        ///   0 = Fit to window (Uniform stretch inside the 260x260 area),
        ///   1 = Original size (1x), 2 = 2x, 3 = 3x, 4 = 4x.
        /// Nearest-neighbour interpolation keeps GBA pixels crisp.
        /// </summary>
        void ApplyZoom()
        {
            RenderOptions.SetBitmapInterpolationMode(PreviewImage, BitmapInterpolationMode.None);

            int idx = ZoomComboBox.SelectedIndex;
            if (idx < 0) idx = _vm.ZoomIndex;
            if (idx < 0) idx = 0;

            if (idx == 0)
            {
                // Fit to window: clear explicit size and let the 260x260
                // container Uniform-stretch the image.
                PreviewImage.Width = double.NaN;
                PreviewImage.Height = double.NaN;
                PreviewImage.Stretch = Stretch.Uniform;
                return;
            }

            // 1 = 1x, 2 = 2x, 3 = 3x, 4 = 4x.
            int scale = idx; // idx 1..4 maps directly to the multiplier.
            if (scale < 1) scale = 1;

            // Base = intrinsic rendered size; fall back to a 32x32 GBA mini
            // portrait base when nothing has rendered yet.
            double baseW = _previewBaseW > 0 ? _previewBaseW : 32;
            double baseH = _previewBaseH > 0 ? _previewBaseH : 32;

            // Clamp so an extreme scale can't blow past the 260x260 preview box
            // by an absurd amount (keeps it usable; mirrors the fixed preview
            // area in the AXAML).
            const double MaxDim = 256;
            double w = baseW * scale;
            double h = baseH * scale;
            if (w > MaxDim || h > MaxDim)
            {
                double f = Math.Min(MaxDim / w, MaxDim / h);
                w *= f;
                h *= f;
            }

            PreviewImage.Stretch = Stretch.Fill;
            PreviewImage.Width = w;
            PreviewImage.Height = h;
        }

        // ---- event handlers ----

        void Zoom_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Persist the selected zoom into the VM (mirrors WF
            // PaletteZoomComboBox_SelectedIndexChanged), then rescale the
            // already-rendered preview bitmap (#1023). ApplyZoom NEVER
            // re-invokes the render delegate — it only changes the displayed
            // size, so switching zoom is cheap.
            int idx = ZoomComboBox.SelectedIndex;
            if (idx < 0) idx = 0;
            _vm.ZoomIndex = idx;
            ApplyZoom();
        }

        void PaletteIndex_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            int idx = PaletteIndexComboBox.SelectedIndex;
            if (idx < 0) return;
            if (idx == _vm.PaletteIndex) return;
            _vm.PaletteIndex = idx;
            // Re-read the now-selected palette block from ROM.
            _vm.IsLoading = true;
            try
            {
                _vm.ApplyPaletteFromRom();
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            UpdateUI();
            // The newly-selected palette block changes the displayed colors,
            // so re-render the live preview with them (#1023).
            RenderPreview();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.PaletteAddress == 0) return;
            uint reloadAddr = _vm.PaletteAddress;
            int reloadMax = _vm.MaxPaletteCount;
            int reloadIdx = _vm.PaletteIndex;

            // Undo name + user-facing messages routed through R._() so
            // they pick up ja/zh translations (Copilot bot inline
            // review #10 on PR #586).
            _undoService.Begin(R._("Edit Palette"));
            try
            {
                ReadNudsIntoVm();
                // Honor PaletteCore.WritePalette's no-op return so an
                // invalid/out-of-range destination surfaces as an
                // error toast instead of a fake success path
                // (Copilot CLI round-2 review on PR #586).
                uint writtenOffset = _vm.Write();
                if (writtenOffset == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    // Pre-format the address so we pass a single
                    // already-rendered string into Log.Error's
                    // params string[]. Using "{0:X08}" + a string
                    // arg crashes Log.Error since the format spec
                    // expects a uint (Copilot bot round-2 inline
                    // review on PR #586).
                    Log.Error("ImagePalletView.Write_Click: write was no-op (addr=0x" + _vm.PaletteAddress.ToString("X8") + ")");
                    CoreState.Services?.ShowError(R._("Write failed: {0}", R._("Invalid palette address")));
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();

                // Reload so the swatch images reflect the post-write
                // 5-bit-quantized values (mirrors the
                // ImageMagicCSACreator pattern). Preserve the
                // palette-name overrides across reload (Copilot bot
                // round-3 inline review #2 on PR #586).
                _vm.IsLoading = true;
                try
                {
                    _vm.LoadEntry(reloadAddr, reloadMax, reloadIdx, _lastPaletteNames);
                }
                finally
                {
                    _vm.IsLoading = false;
                    _vm.MarkClean();
                }
                UpdateUI();
                // Re-render the preview from the post-write quantized colors
                // so the displayed graphic matches the saved ROM bytes (#1023).
                RenderPreview();

                CoreState.Services?.ShowInfo(R._("Palette written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ImagePalletView.Write_Click failed: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Write failed: {0}", ex.Message));
            }
        }

        void ReadNudsIntoVm()
        {
            // Pull current NUD values back into the VM before packing.
            // The NUD-to-VM direction is one-shot at Write time (no
            // PropertyChanged round-trip needed for the gap-sweep PR
            // since the swatches refresh on the reload after write).
            _vm.R1  = (byte)(R1Box.Value  ?? 0); _vm.G1  = (byte)(G1Box.Value  ?? 0); _vm.B1  = (byte)(B1Box.Value  ?? 0);
            _vm.R2  = (byte)(R2Box.Value  ?? 0); _vm.G2  = (byte)(G2Box.Value  ?? 0); _vm.B2  = (byte)(B2Box.Value  ?? 0);
            _vm.R3  = (byte)(R3Box.Value  ?? 0); _vm.G3  = (byte)(G3Box.Value  ?? 0); _vm.B3  = (byte)(B3Box.Value  ?? 0);
            _vm.R4  = (byte)(R4Box.Value  ?? 0); _vm.G4  = (byte)(G4Box.Value  ?? 0); _vm.B4  = (byte)(B4Box.Value  ?? 0);
            _vm.R5  = (byte)(R5Box.Value  ?? 0); _vm.G5  = (byte)(G5Box.Value  ?? 0); _vm.B5  = (byte)(B5Box.Value  ?? 0);
            _vm.R6  = (byte)(R6Box.Value  ?? 0); _vm.G6  = (byte)(G6Box.Value  ?? 0); _vm.B6  = (byte)(B6Box.Value  ?? 0);
            _vm.R7  = (byte)(R7Box.Value  ?? 0); _vm.G7  = (byte)(G7Box.Value  ?? 0); _vm.B7  = (byte)(B7Box.Value  ?? 0);
            _vm.R8  = (byte)(R8Box.Value  ?? 0); _vm.G8  = (byte)(G8Box.Value  ?? 0); _vm.B8  = (byte)(B8Box.Value  ?? 0);
            _vm.R9  = (byte)(R9Box.Value  ?? 0); _vm.G9  = (byte)(G9Box.Value  ?? 0); _vm.B9  = (byte)(B9Box.Value  ?? 0);
            _vm.R10 = (byte)(R10Box.Value ?? 0); _vm.G10 = (byte)(G10Box.Value ?? 0); _vm.B10 = (byte)(B10Box.Value ?? 0);
            _vm.R11 = (byte)(R11Box.Value ?? 0); _vm.G11 = (byte)(G11Box.Value ?? 0); _vm.B11 = (byte)(B11Box.Value ?? 0);
            _vm.R12 = (byte)(R12Box.Value ?? 0); _vm.G12 = (byte)(G12Box.Value ?? 0); _vm.B12 = (byte)(B12Box.Value ?? 0);
            _vm.R13 = (byte)(R13Box.Value ?? 0); _vm.G13 = (byte)(G13Box.Value ?? 0); _vm.B13 = (byte)(B13Box.Value ?? 0);
            _vm.R14 = (byte)(R14Box.Value ?? 0); _vm.G14 = (byte)(G14Box.Value ?? 0); _vm.B14 = (byte)(B14Box.Value ?? 0);
            _vm.R15 = (byte)(R15Box.Value ?? 0); _vm.G15 = (byte)(G15Box.Value ?? 0); _vm.B15 = (byte)(B15Box.Value ?? 0);
            _vm.R16 = (byte)(R16Box.Value ?? 0); _vm.G16 = (byte)(G16Box.Value ?? 0); _vm.B16 = (byte)(B16Box.Value ?? 0);
        }

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                CoreState.Undo?.RunUndo();
                // Reload so the displayed values reflect the post-undo
                // ROM state (Copilot CLI plan review #5 - Undo enabled).
                // Preserve the palette-name overrides across reload
                // (Copilot bot round-3 inline review #3 on PR #586).
                if (_vm.IsLoaded)
                {
                    uint addr = _vm.PaletteAddress;
                    int max = _vm.MaxPaletteCount;
                    int idx = _vm.PaletteIndex;
                    _vm.IsLoading = true;
                    try
                    {
                        _vm.LoadEntry(addr, max, idx, _lastPaletteNames);
                    }
                    finally
                    {
                        _vm.IsLoading = false;
                        _vm.MarkClean();
                    }
                    UpdateUI();
                    RenderPreview(); // reflect the post-undo colors (#1023)
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePalletView.Undo_Click failed: {0}", ex.Message);
            }
        }

        void Redo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo == null || !CoreState.Undo.CanRedo)
                {
                    CoreState.Services.ShowInfo("Nothing to redo.");
                    return;
                }
                if (!CoreState.Undo.RunRedo())
                {
                    CoreState.Services.ShowError("Redo failed.");
                    return;
                }
                // Reload so the displayed values reflect the post-redo ROM state.
                // Preserve the palette-name overrides across reload.
                if (_vm.IsLoaded)
                {
                    uint addr = _vm.PaletteAddress;
                    int max = _vm.MaxPaletteCount;
                    int idx = _vm.PaletteIndex;
                    _vm.IsLoading = true;
                    try
                    {
                        _vm.LoadEntry(addr, max, idx, _lastPaletteNames);
                    }
                    finally
                    {
                        _vm.IsLoading = false;
                        _vm.MarkClean();
                    }
                    UpdateUI();
                    RenderPreview(); // reflect the post-redo colors (#1023)
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePalletView.Redo_Click failed: {0}", ex.Message);
            }
        }

        // ---- palette file Import / Export / Clipboard (#777) ----
        //
        // Mirrors the already-merged ImageBGView palette import/export
        // (ExportPal_Click / ImportPal_Click). Uses the shared Core
        // helpers: PaletteCore.PackToBytes / ReadPalette for the BGR15
        // pack/unpack and PaletteFormatConverter for the file-format
        // (de)serialization. The single 16-color block currently
        // displayed (PaletteAddress + PaletteIndex*0x20) is the unit of
        // export/import — multi-palette spans stay out of scope per the
        // accepted v2 plan.

        /// <summary>
        /// Pack the 16 currently displayed colors into a 32-byte BGR15
        /// blob for export. Syncs the (possibly unsaved) NUD edits into
        /// the VM first via <see cref="ReadNudsIntoVm"/> so an export
        /// reflects what the user sees, not just the last Write. Exposed
        /// internal so the #777 regression tests can exercise the pack
        /// path without driving the file dialog.
        /// </summary>
        internal byte[] ComputeExportBytes()
        {
            ReadNudsIntoVm();
            return PaletteCore.PackToBytes(_vm.GetSlots());
        }

        /// <summary>
        /// Build the "RRGGBB,RRGGBB,..." clipboard line for the 16
        /// displayed colors (uppercase hex, comma-separated). Matches
        /// the ImageBattleAnimePalletView / ImageBattleScreenView
        /// clipboard format. Internal so tests can assert the string.
        /// </summary>
        internal string BuildClipboardCsv()
        {
            ReadNudsIntoVm();
            return BuildClipboardCsv(_vm.GetSlots());
        }

        internal static string BuildClipboardCsv((byte r, byte g, byte b)[] colors)
        {
            var sb = new System.Text.StringBuilder();
            int count = System.Math.Min(colors?.Length ?? 0, 16);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:X2}{1:X2}{2:X2}", colors[i].r, colors[i].g, colors[i].b);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Unpack a 32-byte BGR15 block and push the first 16 colors into
        /// the VM + NUD controls (mirrors the read path in
        /// ApplyPaletteFromRom -> SetSlot, but for an imported blob).
        /// Internal so the #777 tests can verify the import-to-grid hop.
        /// </summary>
        internal void ApplyGbaBytesToNuds(byte[] gbaBytes)
        {
            var colors = new (byte r, byte g, byte b)[16];
            for (int i = 0; i < 16; i++)
            {
                int cur = i * 2;
                ushort gba = (ushort)(gbaBytes[cur] | (gbaBytes[cur + 1] << 8));
                PaletteFormatConverter.GbaToRgb(gba, out byte r, out byte g, out byte b);
                colors[i] = (r, g, b);
            }
            ApplyColorsToNuds(colors);
        }

        void ApplyColorsToNuds((byte r, byte g, byte b)[] colors)
        {
            // Push into the VM (so GetSlots/Write see them) AND mirror
            // onto the NUDs/swatches so the grid updates immediately.
            for (int i = 0; i < 16; i++)
                _vm.SetSlot(i, colors[i].r, colors[i].g, colors[i].b);
            UpdateUI();
        }

        async void Export_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded) return;
                // Pack the 16 displayed colors (incl. unsaved NUD edits).
                byte[] gbaBytes = ComputeExportBytes();
                string? path = await FileDialogHelper.SavePaletteFile(this, "palette.pal");
                if (string.IsNullOrEmpty(path)) return;
                PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(Path.GetExtension(path));
                File.WriteAllBytes(path, PaletteFormatConverter.ExportToFormat(gbaBytes, fmt));
                CoreState.Services?.ShowInfo(R._("Palette exported."));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePalletView.Export_Click failed: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Export palette failed: {0}", ex.Message));
            }
        }

        async void Import_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // No raw isSafetyOffset check on PaletteAddress — it is a
                // GBA pointer; PaletteCore.WritePalette normalizes + range
                // checks it. The IsLoaded gate alone refuses imports when
                // no entry has been opened.
                if (!_vm.IsLoaded) return;
                string? path = await FileDialogHelper.OpenPaletteFile(this);
                if (string.IsNullOrEmpty(path)) return; // cancel = no change
                byte[] fileData = File.ReadAllBytes(path);
                PaletteFormat fmt = PaletteFormatConverter.DetectFormat(fileData, Path.GetExtension(path));
                byte[] palData = (fmt == PaletteFormat.GbaRaw) ? fileData : PaletteFormatConverter.ImportFromFormat(fileData, fmt);
                if (palData.Length < PaletteCore.PALETTE_BLOCK_SIZE)
                {
                    CoreState.Services?.ShowError(R._("Palette too small (need >= 32 bytes)"));
                    return;
                }
                // Take only the first 16 colors (first 32 bytes); a longer
                // file is truncated to this editor's single-block scope.
                byte[] first = palData;
                if (first.Length > PaletteCore.PALETTE_BLOCK_SIZE)
                {
                    first = new byte[PaletteCore.PALETTE_BLOCK_SIZE];
                    Array.Copy(palData, first, PaletteCore.PALETTE_BLOCK_SIZE);
                }
                // Snapshot the current grid so a failed import can restore the
                // prior display — Rollback() only restores ROM bytes, not the
                // NUD grid (Copilot review on PR #779).
                byte[] prevBytes = ComputeExportBytes();
                // Reflect the imported colors in the grid before writing.
                ApplyGbaBytesToNuds(first);

                _undoService.Begin(R._("Import Palette"));
                try
                {
                    ReadNudsIntoVm();
                    uint off = _vm.Write();
                    if (off == U.NOT_FOUND)
                    {
                        _undoService.Rollback();
                        ApplyGbaBytesToNuds(prevBytes); // restore the pre-import grid
                        RefreshSwatches();
                        CoreState.Services?.ShowError(R._("Write failed: {0}", R._("Invalid palette address")));
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    RefreshSwatches();
                    CoreState.Services?.ShowInfo(R._("Palette imported."));
                }
                catch (Exception ex)
                {
                    _undoService.Rollback();
                    ApplyGbaBytesToNuds(prevBytes); // restore the pre-import grid
                    RefreshSwatches();
                    Log.ErrorF("ImagePalletView.Import_Click inner failed: {0}", ex.Message);
                    CoreState.Services?.ShowError(R._("Import palette failed: {0}", ex.Message));
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePalletView.Import_Click failed: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Import palette failed: {0}", ex.Message));
            }
        }

        async void Clipboard_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded) return;
                string csv = BuildClipboardCsv();
                var cb = TopLevel.GetTopLevel(this)?.Clipboard;
                if (cb != null) await cb.SetTextAsync(csv);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ImagePalletView.Clipboard_Click failed: {0}", ex.Message);
            }
        }

        // ---- IEditorView ----

        public void NavigateTo(uint address) => JumpTo(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
