// SPDX-License-Identifier: GPL-3.0-or-later
// ImageTSAEditorView code-behind — Avalonia parity raise (gap-sweep #398).
//
// The view exposes the WinForms Init(...) entry point so future callers
// (TSAEditor button on a future GraphicsToolView equivalent) can wire up
// width/height + pointers. Until Init is invoked, Write / PaletteWrite are
// IsEnabled=False (bound to _vm.IsContextLoaded) so the default standalone
// open path stays harmless.
//
// All ROM-write handlers (Write / PaletteWrite) go through
// _undoService.Begin/Commit/Rollback. The Clipboard / MainImageImport /
// MainImageExport buttons are explicit IsEnabled=False stubs whose Click
// handlers short-circuit — covered by KnownGap markers in the AXAML.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Data;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageTSAEditorView : TranslatedWindow, IEditorView
    {
        readonly ImageTSAEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "TSA Tile Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageTSAEditorView()
        {
            InitializeComponent();

            // IsEnabled bindings (`IsEnabled="{Binding IsContextLoaded}"`)
            // need a DataContext that points at the ViewModel. The view
            // itself is the binding source target, but the bindings refer
            // to ViewModel properties — so DataContext = _vm.
            DataContext = _vm;

            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        /// <summary>
        /// Caller-facing entry point that mirrors WinForms
        /// ImageTSAEditorForm.Init. After Init returns, IsContextLoaded
        /// is true and Write / PaletteWrite become enabled.
        ///
        /// This is the surface a future Avalonia caller (TSAEditor button
        /// on a GraphicsToolView equivalent) would invoke after constructing
        /// the view, mirroring the WinForms InputFormRef.JumpForm + f.Init
        /// pattern at ImageFormRef.cs:1001 and GraphicsToolForm.cs:980.
        /// </summary>
        public void Init(uint width8,
                         uint height8,
                         uint zimgPointer,
                         bool isHeaderTSA,
                         bool isLZ77TSA,
                         uint tsaPointer,
                         uint palettePointer,
                         uint paletteAddress,
                         int paletteCount)
        {
            _vm.Init(width8, height8, zimgPointer, isHeaderTSA, isLZ77TSA,
                     tsaPointer, palettePointer, paletteAddress, paletteCount);

            // Seed the palette address spinner from the resolved context
            // so the Palette tab is ready for the first read.
            try
            {
                uint paletteResolved = _vm.ResolveActivePaletteAddress();
                PaletteAddressBox.Value = paletteResolved;
            }
            catch (Exception ex)
            {
                Log.Error("ImageTSAEditorView.Init failed: {0}", ex.Message);
            }

            UpdateInfoLabel();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ImageTSAEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateInfoLabel();
            }
            catch (Exception ex)
            {
                Log.Error("ImageTSAEditorView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateInfoLabel()
        {
            if (_vm.IsContextLoaded)
            {
                InfoBox.Text = string.Format("{0}x{1}",
                    _vm.Width8 * 8, _vm.Height8 * 8);
            }
            else
            {
                InfoBox.Text = string.Empty;
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        // -----------------------------------------------------------------
        // Click handlers — Write / PaletteWrite go through UndoService.
        // -----------------------------------------------------------------

        /// <summary>
        /// Main Write button (top toolbar). In WinForms this writes TSA +
        /// palette in one shot via ImageFormRef.WriteImageData. The TSA-
        /// byte-write path is deferred (KnownGap: TSAByteWrite), so this
        /// button writes the active palette only. Disabled until Init().
        /// </summary>
        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsContextLoaded) return;

            _undoService.Begin("TSA Editor Write");
            try
            {
                PerformPaletteWrite();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"TSA Write failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Palette Write button (Palette tab). Same as Write_Click but
        /// scoped to the palette tab's caption.
        /// </summary>
        void PaletteWrite_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsContextLoaded) return;

            _undoService.Begin("TSA Editor Palette Write");
            try
            {
                PerformPaletteWrite();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                CoreState.Services.ShowError($"Palette Write failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Shared core of Write_Click / PaletteWrite_Click. Pulls the 16
        /// RGB triplets from the AXAML NumericUpDown grid, packs 5-5-5,
        /// and writes 16 ushorts to (paletteAddr + paletteIndex*0x20).
        /// </summary>
        void PerformPaletteWrite()
        {
            uint paletteAddr = (uint)(PaletteAddressBox.Value ?? 0m);
            int paletteIndex = Math.Max(0, PaletteIndexCombo.SelectedIndex);
            var rgb = ReadPaletteFromUI();
            _vm.WritePalette(paletteAddr, paletteIndex, rgb);
        }

        /// <summary>
        /// Collect the 16 R/G/B NumericUpDown values from the AXAML grid
        /// into a 16-entry tuple array suitable for WritePalette.
        /// </summary>
        (byte R, byte G, byte B)[] ReadPaletteFromUI()
        {
            var rgb = new (byte R, byte G, byte B)[16];
            // Unrolled lookup — the AXAML grid declares 16 named pairs.
            NumericUpDown[] rs = { PaletteR1, PaletteR2, PaletteR3, PaletteR4,
                                   PaletteR5, PaletteR6, PaletteR7, PaletteR8,
                                   PaletteR9, PaletteR10, PaletteR11, PaletteR12,
                                   PaletteR13, PaletteR14, PaletteR15, PaletteR16 };
            NumericUpDown[] gs = { PaletteG1, PaletteG2, PaletteG3, PaletteG4,
                                   PaletteG5, PaletteG6, PaletteG7, PaletteG8,
                                   PaletteG9, PaletteG10, PaletteG11, PaletteG12,
                                   PaletteG13, PaletteG14, PaletteG15, PaletteG16 };
            NumericUpDown[] bs = { PaletteB1, PaletteB2, PaletteB3, PaletteB4,
                                   PaletteB5, PaletteB6, PaletteB7, PaletteB8,
                                   PaletteB9, PaletteB10, PaletteB11, PaletteB12,
                                   PaletteB13, PaletteB14, PaletteB15, PaletteB16 };
            for (int i = 0; i < 16; i++)
            {
                rgb[i] = (
                    (byte)Math.Clamp((int)(rs[i].Value ?? 0m), 0, 255),
                    (byte)Math.Clamp((int)(gs[i].Value ?? 0m), 0, 255),
                    (byte)Math.Clamp((int)(bs[i].Value ?? 0m), 0, 255)
                );
            }
            return rgb;
        }

        // -----------------------------------------------------------------
        // Undo / Redo (top toolbar) — global Core.Undo navigation.
        // -----------------------------------------------------------------

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.Undo != null)
                {
                    CoreState.Undo.RunUndo();
                }
            }
            catch (Exception ex)
            {
                CoreState.Services.ShowError($"Undo failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Redo button. Core Undo currently exposes only RunUndo (the
        /// global redo stack is a WinForms-only extension). Surface a
        /// short-circuit message so the button doesn't act as a dead
        /// control; the View_AllButtons_AreWiredOrExplicitlyInert audit
        /// still passes because we have a Click handler. A future Core
        /// Undo.RunRedo will replace this stub.
        /// </summary>
        void Redo_Click(object? sender, RoutedEventArgs e)
        {
            Log.Notify("ImageTSAEditor Redo - Core Undo.RunRedo not yet implemented (mirrors WinForms-only path)");
        }

        /// <summary>
        /// Palette tab UNDO sub-button. Mirrors WF UndoPlaetteButton_Click
        /// which calls PFR.RunUndo (the palette-only undo buffer the WF
        /// PaletteFormRef maintains). In Avalonia we route to the global
        /// Core Undo stack for now — the per-tab palette undo is part of
        /// the deferred KnownGap: PaletteToClipboard cluster.
        /// </summary>
        void PaletteUndo_Click(object? sender, RoutedEventArgs e) => Undo_Click(sender, e);

        /// <summary>
        /// Palette REDO sub-button. Same short-circuit as Redo_Click —
        /// Core Undo.RunRedo is a deferred surface.
        /// </summary>
        void PaletteRedo_Click(object? sender, RoutedEventArgs e) => Redo_Click(sender, e);

        // -----------------------------------------------------------------
        // KnownGap inert stubs — IsEnabled=False in AXAML, Click handler
        // is present so the View_AllButtons_AreWiredOrExplicitlyInert test
        // passes (one or the other is required by the audit; we have both).
        // -----------------------------------------------------------------

        /// <summary>
        /// KnownGap: PaletteToClipboard. WinForms uses
        /// System.Windows.Forms.Clipboard which is WinForms-only; the
        /// Avalonia equivalent (TopLevel.Clipboard async API) is a
        /// separate cross-cutting refactor not in scope here.
        /// </summary>
        void PaletteClipboard_Click(object? sender, RoutedEventArgs e)
        {
            Log.Notify("ImageTSAEditor PaletteClipboard - deferred (KnownGap: PaletteToClipboard)");
        }

        /// <summary>
        /// KnownGap: MainImageImportExport. Mirrors WF image1_Import —
        /// would call into ImageFormRef.ImportImageHandler which is
        /// WinForms-only.
        /// </summary>
        void MainImageImport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Notify("ImageTSAEditor MainImageImport - deferred (KnownGap: MainImageImportExport)");
        }

        /// <summary>
        /// KnownGap: MainImageImportExport. Mirrors WF image1_Export —
        /// would call into ImageFormRef.ExportImageHandler which is
        /// WinForms-only.
        /// </summary>
        void MainImageExport_Click(object? sender, RoutedEventArgs e)
        {
            Log.Notify("ImageTSAEditor MainImageExport - deferred (KnownGap: MainImageImportExport)");
        }
    }
}
