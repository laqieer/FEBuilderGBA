// SPDX-License-Identifier: GPL-3.0-or-later
// PointerToolCopyToView code-behind. (#438 Copilot CLI review point 2 + 3)
//
// The pre-#438 implementation had NavigateTo empty and the 5 copy buttons
// merely setting _vm.CopyMode then closing the window — so the dialog was
// structurally present but functionally a no-op. This rewrite:
//   - Init(uint) populates SourceAddress (mirrors WF
//     PointerToolCopyToForm.Init).
//   - 4 clipboard buttons SetClipboardAsync the correct WF-format payload
//     (raw text, pointer, little-endian swap, no$gba breakpoint).
//   - HexButton Navigates to HexEditorView at the address offset (mirrors
//     WF PointerToolCopyToForm.HexButton_Click).
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input.Platform;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolCopyToView : TranslatedWindow, IEditorView
    {
        readonly PointerToolCopyToViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Pointer Tool - Copy To";
        public bool IsLoaded => _vm.IsLoaded;

        public PointerToolCopyToView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        async void CopyPointer_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "Pointer";
            await SetClipboardAsync(_vm.GetAsPointer());
            Close("Pointer");
        }

        async void CopyClipboard_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "Clipboard";
            await SetClipboardAsync(_vm.GetAsClipboardText());
            Close("Clipboard");
        }

        async void CopyLittleEndian_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "LittleEndian";
            await SetClipboardAsync(_vm.GetAsLittleEndian());
            Close("LittleEndian");
        }

        void HexButton_Click(object? sender, RoutedEventArgs e)
        {
            // WF: open the Hex Editor at the offset. AV mirrors this via
            // WindowManager.Navigate<HexEditorView>(offset).
            try
            {
                _vm.CopyMode = "Hex";
                uint offset = _vm.GetOffsetForHexJump();
                WindowManager.Instance.Navigate<HexEditorView>(offset);
                Close("Hex");
            }
            catch (Exception ex)
            {
                Log.Error("PointerToolCopyToView.HexButton_Click: {0}", ex.Message);
            }
        }

        async void CopyNoDoll_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "NoDoll";
            await SetClipboardAsync(_vm.GetAsNoDollGBARadBreakPoint());
            Close("NoDoll");
        }

        async System.Threading.Tasks.Task SetClipboardAsync(string text)
        {
            try
            {
                IClipboard? clipboard = Clipboard;
                if (clipboard != null && !string.IsNullOrEmpty(text))
                    await clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                Log.Error("PointerToolCopyToView.SetClipboardAsync failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Mirror of WF <c>PointerToolCopyToForm.Init(addr)</c>: seed the
        /// SourceAddress so the textbox displays the value the caller wants
        /// to copy. Called by <c>WindowManager.Navigate&lt;PointerToolCopyToView&gt;(addr)</c>.
        /// </summary>
        public void NavigateTo(uint address)
        {
            _vm.Init(address);
        }

        public void SelectFirstItem() { }
    }
}
