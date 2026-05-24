using System;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Shared helper methods used across multiple Avalonia editor views.
    /// </summary>
    internal static class ViewHelpers
    {
        /// <summary>
        /// Parse a hex string (with optional "0x" prefix) to a uint value.
        /// Returns 0 if the input is null, empty, or not a valid hex number.
        /// </summary>
        internal static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        }

        /// <summary>
        /// Try-parse variant of <see cref="ParseHexText(string?)"/>. Returns
        /// <c>false</c> when the input is whitespace-only OR when the body
        /// (after stripping any 0x prefix) is not a valid hex literal. An
        /// empty/null input returns <c>(true, 0)</c> — callers that need to
        /// reject empty input must check the value separately.
        /// </summary>
        internal static bool TryParseHexText(string? text, out uint value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return true;
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return true;
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[2..];
            return uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out value);
        }
    }
}
