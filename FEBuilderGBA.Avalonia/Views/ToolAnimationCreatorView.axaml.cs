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
            if (FramesList != null && FramesList.ItemCount > 0)
                FramesList.SelectedIndex = 0;
        }
    }
}
