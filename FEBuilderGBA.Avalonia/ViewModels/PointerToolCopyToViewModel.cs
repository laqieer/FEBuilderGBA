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
        /// </summary>
        public string GetAsPointer()
        {
            uint val = ParseAddress(SourceAddress);
            uint pointer = U.toPointer(val);
            return $"0x{pointer:X08}";
        }

        /// <summary>
        /// Mirror of WF <c>PointerToolCopyToForm.CopyLittleEndian_Click</c>:
        /// returns the GBA pointer with its byte order reversed (this is what
        /// the WF impl actually copies — a 4-byte little-endian SWAP, not a
        /// space-separated byte string).
        /// </summary>
        public string GetAsLittleEndian()
        {
            uint val = ParseAddress(SourceAddress);
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
        /// </summary>
        public string GetAsNoDollGBARadBreakPoint()
        {
            uint val = ParseAddress(SourceAddress);
            uint pointer = U.toPointer(val);
            return $"[0x{pointer:X08}]?";
        }

        /// <summary>
        /// Get the source address as a raw hex string suitable for direct
        /// clipboard copy (mirrors WF
        /// <c>PointerToolCopyToForm.CopyClipboard_Click</c> which copies the
        /// raw <c>ValueTextBox.Text</c>). Returns the trimmed, normalised
        /// representation of <see cref="SourceAddress"/>.
        /// </summary>
        public string GetAsClipboardText()
        {
            string text = (SourceAddress ?? "").Trim();
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            // If the user typed plain hex (no 0x prefix), normalise to the
            // 0xXXXXXXXX form so the clipboard receives a consistent format.
            if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                uint val = ParseAddress(text);
                return $"0x{val:X08}";
            }
            return text;
        }

        /// <summary>
        /// Get the underlying address (offset form, no <c>0x08000000</c> base)
        /// as a uint for callers that need to navigate to the Hex Editor.
        /// Mirrors WF <c>PointerToolCopyToForm.HexButton_Click</c> which calls
        /// <c>U.toOffset(this.Address)</c> before opening the editor.
        /// </summary>
        public uint GetOffsetForHexJump()
        {
            uint val = ParseAddress(SourceAddress);
            // The input could already be either offset or pointer form;
            // U.toOffset is idempotent for offsets.
            return U.toOffset(val);
        }

        /// <summary>Parse a hex string into a uint. Returns 0 on failure.</summary>
        static uint ParseAddress(string raw)
        {
            string text = (raw ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }
    }
}
