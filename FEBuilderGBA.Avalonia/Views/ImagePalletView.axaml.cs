// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia counterpart of WinForms ImagePalletForm (#400 gap-sweep).
// Provides a WF-equivalent JumpTo(...) entry point so callers like
// ImagePortraitView.JumpToPalette_Click can carry the palette
// address from the source editor.
//
// All ROM writes are wrapped in UndoService.Begin/Commit/Rollback;
// Undo is wired to CoreState.Undo.RunUndo() + reload, Redo is
// deferred behind #500 (Core has no RunRedo() API).

using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImagePalletView : TranslatedWindow, IEditorView
    {
        readonly ImagePalletViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Palette Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImagePalletView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
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
                Log.Error("ImagePalletView.LoadList failed: {0}", ex.Message);
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
        public void JumpTo(uint paletteAddress, int maxPaletteCount = 1, int defaultSelectPalette = 0, string[]? paletteNames = null)
        {
            try
            {
                _vm.LoadEntry(paletteAddress, maxPaletteCount, defaultSelectPalette, paletteNames);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImagePalletView.JumpTo failed: {0}", ex.Message);
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

            bool showIndex = _vm.MaxPaletteCount > 1;
            PaletteIndexComboBox.IsVisible = showIndex;
            PaletteIndexLabel.IsVisible = showIndex;

            ZoomComboBox.SelectedIndex = _vm.ZoomIndex;

            // Mirror the 48 NUDs from VM state.
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

            RefreshSwatches();
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

        static void SetSwatch(Image image, byte r, byte g, byte b)
        {
            // Lightweight 1x1 pixel rendered at the swatch's display size.
            // Avalonia stretches it to the Image bounds automatically.
            var bmp = new WriteableBitmap(
                new global::Avalonia.PixelSize(1, 1),
                new global::Avalonia.Vector(96, 96),
                global::Avalonia.Platform.PixelFormat.Bgra8888,
                global::Avalonia.Platform.AlphaFormat.Unpremul);
            using (var fb = bmp.Lock())
            {
                unsafe
                {
                    byte* p = (byte*)fb.Address;
                    p[0] = b; p[1] = g; p[2] = r; p[3] = 0xFF; // BGRA
                }
            }
            image.Source = bmp;
            image.Stretch = Stretch.Fill;
        }

        // ---- event handlers ----

        void Zoom_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Persist the selected zoom into the VM (mirrors WF
            // PaletteZoomComboBox_SelectedIndexChanged - WF re-renders
            // the bitmap preview; live preview is #500-deferred here).
            int idx = ZoomComboBox.SelectedIndex;
            if (idx < 0) idx = 0;
            _vm.ZoomIndex = idx;
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
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.PaletteAddress == 0) return;
            uint reloadAddr = _vm.PaletteAddress;
            int reloadMax = _vm.MaxPaletteCount;
            int reloadIdx = _vm.PaletteIndex;

            _undoService.Begin("Edit Palette");
            try
            {
                ReadNudsIntoVm();
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();

                // Reload so the swatch images reflect the post-write
                // 5-bit-quantized values (mirrors the
                // ImageMagicCSACreator pattern).
                _vm.IsLoading = true;
                try
                {
                    _vm.LoadEntry(reloadAddr, reloadMax, reloadIdx, null);
                }
                finally
                {
                    _vm.IsLoading = false;
                    _vm.MarkClean();
                }
                UpdateUI();

                CoreState.Services?.ShowInfo("Palette written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ImagePalletView.Write_Click failed: {0}", ex.Message);
                CoreState.Services?.ShowError($"Write failed: {ex.Message}");
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
                if (_vm.IsLoaded)
                {
                    uint addr = _vm.PaletteAddress;
                    int max = _vm.MaxPaletteCount;
                    int idx = _vm.PaletteIndex;
                    _vm.IsLoading = true;
                    try
                    {
                        _vm.LoadEntry(addr, max, idx, null);
                    }
                    finally
                    {
                        _vm.IsLoading = false;
                        _vm.MarkClean();
                    }
                    UpdateUI();
                }
            }
            catch (Exception ex)
            {
                Log.Error("ImagePalletView.Undo_Click failed: {0}", ex.Message);
            }
        }

        // Deferred affordances (all surface as disabled buttons with #500
        // tooltip; the click handlers exist so AXAML wiring stays valid).

        void Import_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ImagePalletView.Import_Click invoked - disabled until #500 lands");

        void Export_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ImagePalletView.Export_Click invoked - disabled until #500 lands");

        void Clipboard_Click(object? sender, RoutedEventArgs e) =>
            Log.Debug("ImagePalletView.Clipboard_Click invoked - disabled until #500 lands");

        // ---- IEditorView ----

        public void NavigateTo(uint address) => JumpTo(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
