// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia view for the Struct Dump Selector dispatcher dialog.
// Mirrors WinForms `DumpStructSelectDialogForm`. Eleven action buttons each
// invoke a side effect (clipboard write / WindowManager.Navigate / open the
// text-display dialog) and update the VM's SelectedFunc to track the action.
using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input.Platform;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DumpStructSelectDialogView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly DumpStructSelectDialogViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => R._("Data Address Editor");
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Data Address Editor", 720, 860, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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
                Log.ErrorF("DumpStructSelectDialogView.BinaryButton failed: {0}", ex.Message);
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
                Log.ErrorF("DumpStructSelectDialogView.CopyPointer failed: {0}", ex.Message);
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
                Log.ErrorF("DumpStructSelectDialogView.CopyClipboard failed: {0}", ex.Message);
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
                Log.ErrorF("DumpStructSelectDialogView.CopyLittleEndian failed: {0}", ex.Message);
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
                Log.ErrorF("DumpStructSelectDialogView.CopyNoDollGBARadBreakPoint failed: {0}", ex.Message);
            }
        }

        // ===================================================================
        // Data Export group — WinForms label1
        //
        // CSV/TSV/EA now write a REAL file via the SAME Core struct-data seam
        // the CLI --export-data command uses: TableExportImportHelper resolves
        // the table from the focused address alone (StructExportCore.ResolveTableAt),
        // shows a file-save dialog, and writes StructExportCore.ExportToTSV/CSV/EA
        // output (byte-identical to the CLI) — addressing the DumpStruct
        // export/import portion of #439 (not the issue's full parity scope).
        // STRUCT/NMM are also backed by the Core seam (#1012): they open the text
        // preview dialog whose body is _vm.MakeExportText, which now returns the
        // struct-aware FormatSTRUCT (.h C-header) / FormatNMM (No$gba memory map)
        // output for a resolved table. When the address resolves to NO known
        // table, ALL formats fall back to the honest hex-dump preview banner.
        // ===================================================================

        void CSVButton_Click(object? sender, RoutedEventArgs e)
            => ExportSelected(DumpStructSelectDialogViewModel.Func.Func_CSV, "CSV", ".csv");

        void TSVButton_Click(object? sender, RoutedEventArgs e)
            => ExportSelected(DumpStructSelectDialogViewModel.Func.Func_TSV, "TSV", ".tsv");

        void EAALLButton_Click(object? sender, RoutedEventArgs e)
            => ExportSelected(DumpStructSelectDialogViewModel.Func.Func_EA, "EA", ".event");

        void STRUCTButton_Click(object? sender, RoutedEventArgs e)
            => OpenPreviewDialog(DumpStructSelectDialogViewModel.Func.Func_STRUCT, "STRUCT", ".h");

        void NMMButton_Click(object? sender, RoutedEventArgs e)
            => OpenPreviewDialog(DumpStructSelectDialogViewModel.Func.Func_NMM, "NMM", ".nmm");

        /// <summary>
        /// Struct-aware export for CSV/TSV/EA. When the focused address falls
        /// inside a known ROM struct table, write a REAL file via the Core seam
        /// (file-save dialog + StructExportCore.ExportTo{Format}). Otherwise fall
        /// back to the honest hex-dump preview banner so the action is never silent.
        /// </summary>
        async void ExportSelected(DumpStructSelectDialogViewModel.Func func, string formatName, string ext)
        {
            try
            {
                _vm.SelectedFunc = func;

                // Only the struct-aware formats with a resolvable table write a
                // real file; everything else honestly shows the hex-dump preview.
                if (_vm.ResolvedTableName() != null)
                {
                    await TableExportImportHelper.ExportTableByAddressAsync(TopLevel.GetTopLevel(this) as Window, _vm.CurrentAddr, formatName);
                    return;
                }

                OpenPreviewDialog(func, formatName, ext);
            }
            catch (Exception ex)
            {
                Log.ErrorF("DumpStructSelectDialogView.ExportSelected({0}) failed: {1}", formatName, ex.Message);
            }
        }

        async void OpenPreviewDialog(DumpStructSelectDialogViewModel.Func func, string formatName, string ext)
        {
            try
            {
                _vm.SelectedFunc = func;
                // Fold the resolved struct-table name (if any) into the preview
                // filename so the user sees which table the export came from.
                // The table name is NEVER injected into `text` — the export body
                // is the raw struct-aware formatter output (or hex fallback).
                string? tableName = _vm.ResolvedTableName();
                string baseName = string.IsNullOrEmpty(tableName)
                    ? "DumpStructSelectDialog_" + U.ToHexString8(_vm.CurrentAddr)
                    : "DumpStructSelectDialog_" + tableName + "_" + U.ToHexString8(_vm.CurrentAddr);
                string filename = baseName + ext;
                string text = _vm.MakeExportText(formatName);
                // ShowDialog with this as owner so the preview is modal and
                // honors the target view's WindowStartupLocation="CenterOwner".
                // (Per Copilot inline review on PR #494.)
                await WindowManager.Instance.OpenModal<DumpStructSelectToTextDialogView>(
                    TopLevel.GetTopLevel(this) as Window,
                    dialog => dialog.SetContent(filename, text));
            }
            catch (Exception ex)
            {
                Log.ErrorF("DumpStructSelectDialogView.OpenPreviewDialog({0}) failed: {1}", formatName, ex.Message);
            }
        }

        // ===================================================================
        // Import group — WinForms label6
        //
        // Import reads a TSV and writes ROM bytes via the SAME Core seam the CLI
        // --import-data command uses: TableExportImportHelper resolves the table
        // from the focused address alone (StructExportCore.ResolveTableAt), opens
        // a file dialog, parses with StructExportCore.ImportFromTSV (hex-index
        // from the first column), and writes via StructExportCore.WriteTable —
        // all inside an UndoService Begin/Commit scope (Rollback on failure), so
        // the mutation is undoable and never half-applied. Addresses #439
        // (DumpStruct export/import portion — the issue's broader Avalonia↔WinForms
        // parity criteria, e.g. density / navigation targets / ko translations, are
        // not all covered here). When the focused address resolves to no known
        // table, the user is told so.
        // ===================================================================

        async void ImportButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Import;
                await TableExportImportHelper.ImportTableByAddressAsync(
                    TopLevel.GetTopLevel(this) as Window, _vm.CurrentAddr, _undoService, UpdateAddressLabel);
            }
            catch (Exception ex)
            {
                Log.ErrorF("DumpStructSelectDialogView.ImportButton failed: {0}", ex.Message);
            }
        }

        // ===================================================================
        // Cancel — closes the dispatcher with Func_Cancel.
        // ===================================================================

        void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            _vm.SelectedFunc = DumpStructSelectDialogViewModel.Func.Func_Cancel;
            RequestClose();
        }

        async System.Threading.Tasks.Task SetClipboardAsync(string text)
        {
            try
            {
                IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                Log.ErrorF("DumpStructSelectDialogView.SetClipboardAsync failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => Init(address);
        public void SelectFirstItem() { }
    }
}
