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
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolAnimationCreatorView : TranslatedWindow, IEditorView
    {
        readonly ToolAnimationCreatorViewViewModel _vm = new();
        readonly UndoService _undoService = new();

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
            _vm.SelectedFrame = FramesList.SelectedItem as EditableMapActionFrame;
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
