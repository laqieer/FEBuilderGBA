namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Shared formatter for converting decoded dialogue text into a
    /// human-readable display string. Mirrors WinForms
    /// <c>TextForm.ConvertEscapeToFEditor</c> +
    /// <c>TextForm.ConvertFEditorToEscape</c>.
    /// </summary>
    /// <remarks>
    /// Extracted from <c>TextViewerViewModel</c> so the conversation viewer
    /// tab can render the same humanised text (escape codes resolved to
    /// <c>[Name]</c> markers) without duplicating the helpers.
    /// </remarks>
    internal static class TextDisplayFormatter
    {
        /// <summary>
        /// Convert raw non-printable control characters (0x00 - 0x1F) into
        /// <c>@XXXX</c> escape form so they round-trip through
        /// <see cref="ConvertEscapeToFEditor"/>. Preserves <c>\n</c> and
        /// <c>\r</c> for normal display.
        /// </summary>
        public static string EscapeRawControlChars(string str)
        {
            if (string.IsNullOrEmpty(str)) return str ?? "";
            var sb = new System.Text.StringBuilder(str.Length);
            foreach (char c in str)
            {
                if (c < 0x20 && c != '\n' && c != '\r')
                    sb.Append($"@{(int)c:X04}");
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Convert <c>@XXXX</c> escape codes into a human-readable
        /// <c>[Name]</c> form via <see cref="CoreState.TextEscape"/>.
        /// </summary>
        public static string ConvertEscapeToFEditor(string str)
        {
            if (string.IsNullOrEmpty(str)) return str ?? "";
            // Handle @0010@0XXX (LoadFace with parameter) before table_replace
            str = RegexCache.Replace(str, @"@0010@0([0-9A-F][0-9A-F][0-9A-F])", "[LoadFace][0x$1]");
            if (CoreState.TextEscape != null)
                str = CoreState.TextEscape.table_replace(str);
            // Convert remaining unknown @XXXX codes
            str = RegexCache.Replace(str, @"@([0-9A-F][0-9A-F][0-9A-F][0-9A-F])", "[0x$1]");
            return str;
        }

        /// <summary>
        /// Strip raw non-printable control characters that were not converted
        /// to <c>@XXXX</c> escape codes by <see cref="FETextDecode"/>.
        /// Preserves <c>\n</c> and <c>\r</c>.
        /// </summary>
        public static string StripControlChars(string str)
        {
            if (string.IsNullOrEmpty(str)) return str ?? "";
            var sb = new System.Text.StringBuilder(str.Length);
            foreach (char c in str)
            {
                if (c < 0x20 && c != '\n' && c != '\r')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
