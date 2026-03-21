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
    }
}
