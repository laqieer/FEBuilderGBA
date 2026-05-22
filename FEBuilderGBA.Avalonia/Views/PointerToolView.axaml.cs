// SPDX-License-Identifier: GPL-3.0-or-later
// PointerToolView code-behind. Gap-sweep #438 rebuild — wires the
// three Phase 4 jumps (Batch, CopyTo on address double-click, self-CLI
// placeholder), surfaces 8 new controls (Load Other ROM button, Write
// button, LDR Address + Reference textboxes, 4 per-result warning
// indicators), and ensures every ROM-mutating handler runs inside a
// _undoService.Begin / Commit / Rollback scope.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolView : TranslatedWindow, IEditorView
    {
        readonly PointerToolViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Pointer Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public PointerToolView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        /// <summary>
        /// Batch button — opens the PointerToolBatchInput dialog. Mirrors
        /// WF <c>PointerToolForm.BatchButton_Click</c>. (#438 Phase 4 jump #1)
        /// </summary>
        void Batch_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // WF gates the batch on having loaded an other-ROM first.
                // Mirror that affordance with a friendly message rather
                // than silently opening an empty dialog.
                if (string.IsNullOrEmpty(_vm.OtherRomName))
                {
                    CoreState.Services?.ShowError(
                        "Load an Other ROM first before opening Batch.");
                    return;
                }
                WindowManager.Instance.Open<PointerToolBatchInputView>();
            }
            catch (Exception ex)
            {
                Log.Error("PointerToolView.Batch_Click: {0}", ex.Message);
            }
        }

        /// <summary>
        /// What Is button — invokes the VM's address-type lookup and shows the
        /// result. Mirrors WF <c>PointerToolForm.WhatIsButton_Click</c>.
        /// </summary>
        void WhatIs_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.AddressInput = AddressTextBox.Text ?? "";
                string addrText = _vm.AddressInput.Trim();
                if (addrText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    addrText = addrText.Substring(2);
                if (!uint.TryParse(addrText, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
                {
                    CoreState.Services?.ShowError("Invalid address.");
                    return;
                }
                string hint = _vm.LookupAddressType(addr);
                CoreState.Services?.ShowInfo(hint);
            }
            catch (Exception ex)
            {
                Log.Error("PointerToolView.WhatIs_Click: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Search button — fallback path retained for legacy callers; also
        /// invoked by the address input on focus loss / Enter. Just runs the
        /// VM's RunSearch over the current ROM (and other-ROM when loaded).
        /// </summary>
        void Search_Click(object? sender, RoutedEventArgs e)
        {
            _vm.AddressInput = AddressTextBox.Text ?? "";
            _vm.RunSearch();
        }

        /// <summary>
        /// Write button — invoke WritePointerValue inside an undo scope so
        /// the operation is undoable. Mirrors WF, which writes via the
        /// global ROM with the Undo object held by the parent form.
        /// </summary>
        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.IsLoading) return;
            _undoService.Begin("PointerTool Write");
            try
            {
                _vm.WritePointerValue();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch
            {
                _undoService.Rollback();
                throw;
            }
        }

        /// <summary>
        /// LoadOtherRom button — opens a file picker for a .gba / .bin ROM,
        /// then delegates to <c>_vm.LoadOtherRom(path)</c>. Mirrors WF
        /// <c>PointerToolForm.LoadOtherROMButton_Click</c>.
        /// </summary>
        async void LoadOtherRom_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var storage = StorageProvider;
                if (storage == null) return;
                var gbaFiles = new FilePickerFileType("GBA ROMs") { Patterns = new[] { "*.gba" } };
                var binFiles = new FilePickerFileType("Binary files") { Patterns = new[] { "*.bin" } };
                var allFiles = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Other ROM",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { gbaFiles, binFiles, allFiles },
                });
                if (files.Count == 0) return;
                string? path = files[0].TryGetLocalPath();
                if (path == null) return;
                _vm.LoadOtherRom(path);
            }
            catch (Exception ex)
            {
                Log.Error("PointerToolView.LoadOtherRom_Click: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Address double-click handler — mirrors WF
        /// <c>PointerToolForm.OtherROMAddress_MouseDoubleClick</c>: opens
        /// PointerToolCopyToView seeded with the textbox's address value.
        /// Wired from all read-only address textboxes (PointerTextBox,
        /// LittleEndianTextBox, FirstReferenceTextBox, DataAddressTextBox,
        /// OtherRomAddressTextBox, OtherRomRefTextBox, LdrAddressTextBox,
        /// LdrRefTextBox). (#438 Phase 4 jump #2)
        /// </summary>
        void AddressDoubleClick(object? sender, TappedEventArgs e)
        {
            try
            {
                if (sender is not TextBox tb) return;
                string text = (tb.Text ?? "").Trim();
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    text = text.Substring(2);
                if (!uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
                    return;
                WindowManager.Instance.Navigate<PointerToolCopyToView>(addr);
            }
            catch (Exception ex)
            {
                Log.Error("PointerToolView.AddressDoubleClick: {0}", ex.Message);
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address)
        {
            _vm.IsLoading = true;
            _vm.AddressInput = $"0x{address:X08}";
            AddressTextBox.Text = _vm.AddressInput;
            _vm.RunSearch();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        public void SelectFirstItem() { }
    }
}
