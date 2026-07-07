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
//     WF PointerToolCopyToForm.HexButton_Click). The button is also disabled
//     when the source address is unparseable or unsafe (mirrors WF
//     `HexButton.Enabled = U.isSafetyOffset(U.toOffset(addr))` gate).
using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input.Platform;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PointerToolCopyToView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly PointerToolCopyToViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Pointer Tool - Copy To";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Pointer Tool - Copy To", 449, 404, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public PointerToolCopyToView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
            // Re-evaluate the Hex button gate whenever the source address
            // text changes — mirrors WF where Init runs the gate once and
            // the textbox is otherwise read-only.
            _vm.PropertyChanged += (_, ev) =>
            {
                if (ev.PropertyName == nameof(PointerToolCopyToViewModel.SourceAddress))
                    UpdateHexButtonEnabled();
            };
            UpdateHexButtonEnabled();
        }

        async void CopyPointer_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "Pointer";
            string? payload = _vm.GetAsPointer();
            if (payload == null)
            {
                CoreState.Services?.ShowError("Invalid address.");
                return;
            }
            await SetClipboardAsync(payload);
            DialogResult = "Pointer"; RequestClose();
        }

        async void CopyClipboard_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "Clipboard";
            // WF copies the textbox content verbatim, so we don't gate on
            // the parser here — the user explicitly asked for "whatever is
            // in the box". An empty clipboard payload is also fine (matches
            // WF which would copy the empty string). The
            // allowEmpty: true flag tells SetClipboardAsync not to bail
            // out on empty input, mirroring WF
            // `U.SetClipboardText(this.ValueTextBox.Text)`.
            await SetClipboardAsync(_vm.GetAsClipboardText(), allowEmpty: true);
            DialogResult = "Clipboard"; RequestClose();
        }

        async void CopyLittleEndian_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "LittleEndian";
            string? payload = _vm.GetAsLittleEndian();
            if (payload == null)
            {
                CoreState.Services?.ShowError("Invalid address.");
                return;
            }
            await SetClipboardAsync(payload);
            DialogResult = "LittleEndian"; RequestClose();
        }

        void HexButton_Click(object? sender, RoutedEventArgs e)
        {
            // WF: open the Hex Editor at the offset, but ONLY when
            // U.isSafetyOffset(U.toOffset(addr)) is true. AV mirrors this with
            // an explicit safety check inside the handler (in addition to the
            // IsEnabled gate set by UpdateHexButtonEnabled) so a stale
            // double-click race or scripted invocation cannot bypass the
            // guard.
            try
            {
                _vm.CopyMode = "Hex";
                if (!_vm.TryGetOffsetForHexJump(out uint offset))
                {
                    CoreState.Services?.ShowError("Invalid address.");
                    return;
                }
                var rom = CoreState.ROM;
                if (rom == null || !U.isSafetyOffset(offset, rom))
                {
                    CoreState.Services?.ShowError(
                        $"Offset 0x{offset:X08} is outside the safe ROM range.");
                    return;
                }
                WindowManager.Instance.Navigate<HexEditorView>(offset);
                DialogResult = "Hex"; RequestClose();
            }
            catch (Exception ex)
            {
                Log.ErrorF("PointerToolCopyToView.HexButton_Click: {0}", ex.Message);
            }
        }

        async void CopyNoDoll_Click(object? sender, RoutedEventArgs e)
        {
            _vm.CopyMode = "NoDoll";
            string? payload = _vm.GetAsNoDollGBARadBreakPoint();
            if (payload == null)
            {
                CoreState.Services?.ShowError("Invalid address.");
                return;
            }
            await SetClipboardAsync(payload);
            DialogResult = "NoDoll"; RequestClose();
        }

        async System.Threading.Tasks.Task SetClipboardAsync(string text, bool allowEmpty = false)
        {
            try
            {
                IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null) return;
                // Mirrors WF U.SetClipboardText which copies the string raw
                // (including the empty string). For the formatted copy modes
                // (Pointer / LittleEndian / NoDoll) we skip on empty because
                // the caller should already have raised an "Invalid address"
                // dialog and returned; the empty payload would just be a
                // confusing no-op. The Clipboard mode passes
                // allowEmpty: true to preserve verbatim WF behaviour.
                if (string.IsNullOrEmpty(text) && !allowEmpty) return;
                await clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                Log.ErrorF("PointerToolCopyToView.SetClipboardAsync failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update the Hex Editor button's enabled state to mirror WF's
        /// <c>HexButton.Enabled = U.isSafetyOffset(U.toOffset(addr))</c> gate
        /// inside <c>PointerToolCopyToForm.Init</c>. We re-run it on every
        /// SourceAddress change so the affordance stays correct if the
        /// (otherwise read-only) textbox is ever populated dynamically.
        /// </summary>
        void UpdateHexButtonEnabled()
        {
            if (HexButton == null) return;
            var rom = CoreState.ROM;
            HexButton.IsEnabled =
                rom != null &&
                _vm.TryGetOffsetForHexJump(out uint offset) &&
                U.isSafetyOffset(offset, rom);
        }

        /// <summary>
        /// Mirror of WF <c>PointerToolCopyToForm.Init(addr)</c>: seed the
        /// SourceAddress so the textbox displays the value the caller wants
        /// to copy. Called by <c>WindowManager.Navigate&lt;PointerToolCopyToView&gt;(addr)</c>.
        /// </summary>
        public void NavigateTo(uint address)
        {
            _vm.Init(address);
            UpdateHexButtonEnabled();
        }

        public void SelectFirstItem() { }
    }
}
