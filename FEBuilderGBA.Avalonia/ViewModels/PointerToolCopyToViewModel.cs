using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolCopyToViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _sourceAddress = string.Empty;
        string _copyMode = string.Empty;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The address value being copied.</summary>
        public string SourceAddress { get => _sourceAddress; set => SetField(ref _sourceAddress, value); }
        /// <summary>Copy mode: "Pointer", "Clipboard", "LittleEndian", "Hex", or "NoDoll".</summary>
        public string CopyMode { get => _copyMode; set => SetField(ref _copyMode, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>
        /// Mirror of WF <c>PointerToolCopyToForm.Init(uint addr)</c>: store the
        /// address as a `0xXXXXXXXX`-formatted string in <see cref="SourceAddress"/>.
        /// Called by <c>PointerToolCopyToView.NavigateTo</c> when the parent
        /// PointerTool opens this dialog with a seeded address (mirrors WF
        /// <c>PointerToolCopyToForm f.Init(...); f.ShowDialog();</c>).
        /// </summary>
        public void Init(uint addr)
        {
            SourceAddress = $"0x{addr:X08}";
        }

        /// <summary>
        /// Get the source address formatted as a GBA pointer string
        /// (<c>0x08XXXXXX</c>). Mirrors WF
        /// <c>PointerToolCopyToForm.CopyPointer_Click</c>.
        /// Returns <c>null</c> when <see cref="SourceAddress"/> cannot be parsed
        /// — callers should suppress the copy operation in that case rather
        /// than copying a misleading "0x08000000" silently.
        /// </summary>
        public string? GetAsPointer()
        {
            if (!TryParseSourceAddress(out uint val)) return null;
            uint pointer = U.toPointer(val);
            return $"0x{pointer:X08}";
        }

        /// <summary>
        /// Mirror of WF <c>PointerToolCopyToForm.CopyLittleEndian_Click</c>:
        /// returns the GBA pointer with its byte order reversed (this is what
        /// the WF impl actually copies — a 4-byte little-endian SWAP, not a
        /// space-separated byte string).
        /// Returns <c>null</c> when the source address cannot be parsed.
        /// </summary>
        public string? GetAsLittleEndian()
        {
            if (!TryParseSourceAddress(out uint val)) return null;
            uint pointer = U.toPointer(val);
            uint le = ((pointer & 0xFF) << 24)
                    | ((pointer & 0xFF00) << 8)
                    | ((pointer & 0xFF0000) >> 8)
                    | ((pointer & 0xFF000000) >> 24);
            return $"0x{le:X08}";
        }

        /// <summary>
        /// Mirror of WF
        /// <c>PointerToolCopyToForm.CopyNoDollGBARadBreakPoint_Click</c>:
        /// returns the address formatted as a no$gba read-breakpoint
        /// expression <c>[0x08XXXXXX]?</c>.
        /// Returns <c>null</c> when the source address cannot be parsed.
        /// </summary>
        public string? GetAsNoDollGBARadBreakPoint()
        {
            if (!TryParseSourceAddress(out uint val)) return null;
            uint pointer = U.toPointer(val);
            return $"[0x{pointer:X08}]?";
        }

        /// <summary>
        /// Get the source address as a raw hex string suitable for direct
        /// clipboard copy (mirrors WF
        /// <c>PointerToolCopyToForm.CopyClipboard_Click</c> which copies the
        /// raw <c>ValueTextBox.Text</c> verbatim — no normalisation). This
        /// preserves user-typed formatting (e.g. "ABCDEF" stays as "ABCDEF",
        /// not "0x00ABCDEF") for parity with the WinForms behaviour.
        /// </summary>
        public string GetAsClipboardText()
        {
            // WF copies ValueTextBox.Text verbatim. We only trim outer
            // whitespace; the underlying value (with or without the 0x
            // prefix, leading zeros, casing) is preserved exactly as the
            // user sees it in the textbox.
            return (SourceAddress ?? string.Empty).Trim();
        }

        /// <summary>
        /// Try to parse the underlying address into a ROM offset suitable for
        /// navigating to the Hex Editor. Mirrors WF
        /// <c>PointerToolCopyToForm.HexButton_Click</c> which calls
        /// <c>U.toOffset(this.Address)</c> before opening the editor.
        /// Returns <c>false</c> (and <paramref name="offset"/> = 0) when the
        /// source address cannot be parsed; callers must guard navigation.
        /// </summary>
        public bool TryGetOffsetForHexJump(out uint offset)
        {
            offset = 0;
            if (!TryParseSourceAddress(out uint val)) return false;
            // The input could already be either offset or pointer form;
            // U.toOffset is idempotent for offsets.
            offset = U.toOffset(val);
            return true;
        }

        /// <summary>
        /// Legacy accessor for tests that pre-date the nullable-result
        /// refactor. Returns 0 when the address fails to parse — internal
        /// callers should use <see cref="TryGetOffsetForHexJump"/> instead so
        /// invalid input is detected rather than silently routed to offset 0.
        /// </summary>
        public uint GetOffsetForHexJump()
        {
            return TryGetOffsetForHexJump(out uint offset) ? offset : 0;
        }

        /// <summary>
        /// Try to parse <see cref="SourceAddress"/> into a uint. Mirrors the
        /// behaviour the rest of the VM relied on previously while returning
        /// a boolean success indicator so callers can refuse the action
        /// (rather than silently using 0). Public so the view code-behind
        /// can disable the Hex button when the textbox is unparseable.
        /// </summary>
        public bool TryParseSourceAddress(out uint address)
        {
            return TryParseAddress(SourceAddress, out address);
        }

        /// <summary>
        /// Parse a hex string into a uint. Accepts an optional <c>0x</c>
        /// prefix and tolerates surrounding whitespace. Returns <c>true</c>
        /// only on a clean parse — failure callers should refuse the
        /// operation rather than fall back to 0.
        /// </summary>
        public static bool TryParseAddress(string? raw, out uint address)
        {
            address = 0;
            string text = (raw ?? "").Trim();
            if (text.Length == 0) return false;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out address);
        }
    }
}
