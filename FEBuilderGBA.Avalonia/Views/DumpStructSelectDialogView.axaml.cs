// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia view for the Struct Dump Selector dispatcher dialog.
// Mirrors WinForms `DumpStructSelectDialogForm`. Eleven action buttons each
// invoke a side effect (clipboard write / WindowManager.Navigate / open the
// text-display dialog) and update the VM's SelectedFunc to track the action.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input.Platform;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DumpStructSelectDialogView : TranslatedWindow, IEditorView
    {
        readonly DumpStructSelectDialogViewModel _vm = new();

        public string ViewTitle => R._("Data Address Editor");
        public bool IsLoaded => _vm.IsLoaded;

        /// <summary>Public accessor for the VM — used by parity/unit tests.</summary>
        public DumpStructSelectDialogViewModel ViewModel => _vm;

        public DumpStructSelectDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            UpdateAddressLabel();
        }

        /// <summary>
        /// Initialise the dispatcher for a specific address. Mirrors the
        /// WinForms <c>DumpStructSelectDialogForm.Init(uint addr)</c> method
        /// (the dialog is opened by other editors that pass the focused row's
        /// address). When opened standalone from MainWindow the dispatcher
        /// shows address 0 until <see cref="NavigateTo(uint)"/> is called.
        /// </summary>
        public void Init(uint addr)
        {
            _vm.LoadAddress(addr);
            UpdateAddressLabel();
        }

        void UpdateAddressLabel()
        {
            if (AddrLabel != null)
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        // ===================================================================
        // Hex Editor group — WinForms label3
        // ===================================================================

        void BinaryButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Binary;
                WindowManager.Instance.Navigate<HexEditorView>(_vm.CurrentAddr);
            }
            catch (Exception ex)
            {
                Log.Error("DumpStructSelectDialogView.BinaryButton failed: {0}", ex.Message);
            }
        }

        // ===================================================================
        // Clipboard group — WinForms label2
        // ===================================================================

        async void CopyPointer_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Clipbord_Pointer;
                await SetClipboardAsync(_vm.MakeCopyPointerText(_vm.CurrentAddr));
            }
            catch (Exception ex)
            {
                Log.Error("DumpStructSelectDialogView.CopyPointer failed: {0}", ex.Message);
            }
        }

        async void CopyClipboard_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Clipbord_Copy;
                await SetClipboardAsync(_vm.MakeCopyAddressText(_vm.CurrentAddr));
            }
            catch (Exception ex)
            {
                Log.Error("DumpStructSelectDialogView.CopyClipboard failed: {0}", ex.Message);
            }
        }

        async void CopyLittleEndian_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Clipbord_LittleEndian;
                await SetClipboardAsync(_vm.MakeCopyLittleEndianText(_vm.CurrentAddr));
            }
            catch (Exception ex)
            {
                Log.Error("DumpStructSelectDialogView.CopyLittleEndian failed: {0}", ex.Message);
            }
        }

        async void CopyNoDollGBARadBreakPoint_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Clipbord_NoDollBreakPoint;
                await SetClipboardAsync(_vm.MakeCopyNoDollBreakpointText(_vm.CurrentAddr));
            }
            catch (Exception ex)
            {
                Log.Error("DumpStructSelectDialogView.CopyNoDollGBARadBreakPoint failed: {0}", ex.Message);
            }
        }

        // ===================================================================
        // Data Export group — WinForms label1
        //
        // The WF MakeTSV/MakeEA/MakNMM family relies on the source editor's
        // InputFormRef widget tree to discover the struct schema (which
        // NumericUpDown fields exist, what type each is). The Avalonia
        // dispatcher is opened standalone, so we fall back to an honest stub
        // hex dump labelled with a banner. Tracking issue: follow-up to #439.
        // ===================================================================

        void CSVButton_Click(object? sender, RoutedEventArgs e)
            => OpenExportDialog(DumpStructSelectDialogViewModel.Func.Func_CSV, "CSV", ".csv");

        void TSVButton_Click(object? sender, RoutedEventArgs e)
            => OpenExportDialog(DumpStructSelectDialogViewModel.Func.Func_TSV, "TSV", ".tsv");

        void EAALLButton_Click(object? sender, RoutedEventArgs e)
            => OpenExportDialog(DumpStructSelectDialogViewModel.Func.Func_EA, "EA", ".event");

        void STRUCTButton_Click(object? sender, RoutedEventArgs e)
            => OpenExportDialog(DumpStructSelectDialogViewModel.Func.Func_STRUCT, "STRUCT", ".h");

        void NMMButton_Click(object? sender, RoutedEventArgs e)
            => OpenExportDialog(DumpStructSelectDialogViewModel.Func.Func_NMM, "NMM", ".nmm");

        async void OpenExportDialog(DumpStructSelectDialogViewModel.Func func, string formatName, string ext)
        {
            try
            {
                _vm.SelectedFunc = func;
                string filename = "DumpStructSelectDialog_" + U.ToHexString8(_vm.CurrentAddr) + ext;
                string text = _vm.MakeStubExportText(formatName);
                var dialog = new DumpStructSelectToTextDialogView();
                dialog.SetContent(filename, text);
                // ShowDialog with this as owner so the preview is modal and
                // honors the target view's WindowStartupLocation="CenterOwner".
                // (Per Copilot inline review on PR #494.)
                await dialog.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log.Error("DumpStructSelectDialogView.OpenExportDialog({0}) failed: {1}", formatName, ex.Message);
            }
        }

        // ===================================================================
        // Import group — WinForms label6
        //
        // The WF ImportTSV reads a TSV/CSV and writes ROM bytes — but it
        // requires the source editor's InputFormRef widget tree to know which
        // NumericUpDown column maps to which struct field. The Avalonia
        // dispatcher is opened standalone, so Import is gated behind a
        // follow-up issue. The button stays in the UI for density parity.
        // ===================================================================

        void ImportButton_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Import;
            CoreState.Services?.ShowInfo(R._(
                "Import is not yet implemented in the Avalonia dispatcher. The WinForms editor and the CLI --import-data command remain the supported routes."));
        }

        // ===================================================================
        // Cancel — closes the dispatcher with Func_Cancel.
        // ===================================================================

        void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Cancel;
            Close();
        }

        async System.Threading.Tasks.Task SetClipboardAsync(string text)
        {
            try
            {
                IClipboard? clipboard = Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                Log.Error("DumpStructSelectDialogView.SetClipboardAsync failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => Init(address);
        public void SelectFirstItem() { }
    }
}
