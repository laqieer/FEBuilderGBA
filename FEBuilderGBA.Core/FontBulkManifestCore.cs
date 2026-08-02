// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FEBuilderGBA.Core
{
    /// <summary>Parsing contract for a <c>.fontall.txt</c> manifest.</summary>
    public enum FontBulkManifestMode
    {
        /// <summary>
        /// The historical GUI contract: comments and blank lines are ignored, a
        /// header is optional, extra columns are tolerated, width zero is valid,
        /// and hexadecimal filename suffixes may be padded or use either case.
        /// </summary>
        LegacyCompatible,

        /// <summary>
        /// Canonical package contract used by the reproducible font-library
        /// builder.  The header, row shape, spelling and ordering keys are
        /// validated without legacy normalization.
        /// </summary>
        StrictLibrary,
    }

    /// <summary>One parsed or formatted <c>.fontall.txt</c> row.</summary>
    public sealed class FontBulkManifestRow
    {
        public string Character { get; set; } = "";
        public string Type { get; set; } = "";
        public int Width { get; set; }
        public string Filename { get; set; } = "";
        public uint Moji { get; set; }
    }

    /// <summary>Non-throwing manifest parse outcome.</summary>
    public sealed class FontBulkManifestParseResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public List<FontBulkManifestRow> Rows { get; } =
            new List<FontBulkManifestRow>();
    }

    /// <summary>
    /// Shared parser/formatter for the main and ZH bulk import/export paths.
    /// Keeping the legacy and package contracts explicit prevents a strict
    /// package rule from silently changing the GUI's long-standing behavior.
    /// </summary>
    public static class FontBulkManifestCore
    {
        public const string Header = "//char\ttype\tWidth\tFilename";

        public static FontBulkManifestParseResult Parse(
            string manifestText,
            FontBulkManifestMode mode)
            => ParseRows(manifestText, mode, null);

        /// <summary>
        /// Parses rows in source order and invokes <paramref name="visitRow"/>
        /// immediately after each accepted row. Returning a non-empty string
        /// stops parsing. This preserves legacy import timing when a later row
        /// is malformed.
        /// </summary>
        public static FontBulkManifestParseResult ParseRows(
            string manifestText,
            FontBulkManifestMode mode,
            Func<FontBulkManifestRow, string> visitRow)
        {
            var result = new FontBulkManifestParseResult();
            if (manifestText == null)
            {
                result.Error = "No manifest data.";
                return result;
            }

            try
            {
                if (mode == FontBulkManifestMode.StrictLibrary
                    && manifestText.IndexOf('\r') >= 0)
                {
                    result.Error =
                        "Strict font manifests use LF line endings only.";
                    return result;
                }
                string normalized = mode == FontBulkManifestMode.LegacyCompatible
                    ? manifestText.Replace("\r\n", "\n")
                    : manifestText;
                string[] lines = normalized.Split('\n');
                int firstDataLine = 0;
                if (mode == FontBulkManifestMode.StrictLibrary)
                {
                    if (lines.Length == 0
                        || !string.Equals(lines[0], Header, StringComparison.Ordinal))
                    {
                        result.Error =
                            "Strict font manifest must begin with the canonical header.";
                        return result;
                    }
                    firstDataLine = 1;
                }

                var keys = mode == FontBulkManifestMode.StrictLibrary
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : null;
                uint priorMoji = 0;
                int priorStyleRank = -1;
                bool hasPriorStrictRow = false;

                for (int i = firstDataLine; i < lines.Length; i++)
                {
                    string line = mode == FontBulkManifestMode.LegacyCompatible
                        ? lines[i].TrimEnd('\r')
                        : lines[i];

                    // A final LF is canonical and therefore leaves one terminal
                    // empty Split element.  Interior empty rows are not canonical.
                    if (mode == FontBulkManifestMode.StrictLibrary
                        && i == lines.Length - 1
                        && line.Length == 0)
                    {
                        continue;
                    }

                    if (mode == FontBulkManifestMode.LegacyCompatible)
                    {
                        if (line.Trim().Length == 0) continue;
                        if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                    }
                    else
                    {
                        if (line.Length == 0)
                            return Fail(result, i + 1,
                                "blank rows are not allowed");
                        if (line.StartsWith("//", StringComparison.Ordinal))
                            return Fail(result, i + 1,
                                "comments are not allowed after the header");
                    }

                    string[] columns = line.Split('\t');
                    if ((mode == FontBulkManifestMode.StrictLibrary
                            && columns.Length != 4)
                        || (mode == FontBulkManifestMode.LegacyCompatible
                            && columns.Length < 4))
                    {
                        return Fail(result, i + 1,
                            mode == FontBulkManifestMode.StrictLibrary
                                ? "expected exactly four tab-separated columns"
                                : "expected at least four tab-separated columns");
                    }

                    string type = mode == FontBulkManifestMode.LegacyCompatible
                        ? columns[1].Trim()
                        : columns[1];
                    if (type != "item" && type != "text")
                        return Fail(result, i + 1,
                            "type must be 'item' or 'text'");

                    string widthText = mode == FontBulkManifestMode.LegacyCompatible
                        ? columns[2].Trim()
                        : columns[2];
                    NumberStyles widthStyles =
                        mode == FontBulkManifestMode.LegacyCompatible
                            ? NumberStyles.Integer
                            : NumberStyles.None;
                    int width;
                    bool parsedWidth =
                        mode == FontBulkManifestMode.LegacyCompatible
                            // Preserve the historical importer exactly: it used
                            // int.TryParse(trimmedText) under the current culture.
                            ? int.TryParse(widthText, out width)
                            : int.TryParse(widthText, widthStyles,
                                CultureInfo.InvariantCulture, out width);
                    if (!parsedWidth)
                    {
                        return Fail(result, i + 1, "width is not an integer");
                    }
                    int minimumWidth =
                        mode == FontBulkManifestMode.StrictLibrary ? 1 : 0;
                    if (width < minimumWidth || width > FontGlyphRenderCore.GLYPH_W)
                    {
                        return Fail(result, i + 1,
                            "width is outside the accepted range "
                            + minimumWidth.ToString(CultureInfo.InvariantCulture)
                            + ".."
                            + FontGlyphRenderCore.GLYPH_W.ToString(
                                CultureInfo.InvariantCulture));
                    }
                    if (mode == FontBulkManifestMode.StrictLibrary
                        && !string.Equals(
                            widthText,
                            width.ToString(CultureInfo.InvariantCulture),
                            StringComparison.Ordinal))
                    {
                        return Fail(result, i + 1,
                            "width is not canonically formatted");
                    }

                    string filename = mode == FontBulkManifestMode.LegacyCompatible
                        ? columns[3].Trim()
                        : columns[3];
                    if (filename.Length == 0)
                        return Fail(result, i + 1, "filename is empty");
                    if (!TryParseMojiFromFilename(filename, out uint moji))
                        return Fail(result, i + 1,
                            "filename has no hexadecimal moji suffix");

                    if (mode == FontBulkManifestMode.StrictLibrary)
                    {
                        if (!IsSingleUnicodeScalar(columns[0]))
                            return Fail(result, i + 1,
                                "character must contain exactly one Unicode scalar");

                        string canonical = CanonicalFilename(type, moji);
                        if (!string.Equals(filename, canonical,
                            StringComparison.Ordinal))
                        {
                            return Fail(result, i + 1,
                                "filename is not canonical (expected '"
                                + canonical + "')");
                        }

                        string key = moji.ToString("X8",
                            CultureInfo.InvariantCulture) + "\0" + type;
                        if (!keys.Add(key))
                            return Fail(result, i + 1,
                                "duplicate moji/style pair");
                        int styleRank = type == "item" ? 0 : 1;
                        if (hasPriorStrictRow
                            && (moji < priorMoji
                                || (moji == priorMoji
                                    && styleRank <= priorStyleRank)))
                        {
                            return Fail(result, i + 1,
                                "rows must be ordered by numeric moji, then item before text");
                        }
                        priorMoji = moji;
                        priorStyleRank = styleRank;
                        hasPriorStrictRow = true;
                    }

                    var row = new FontBulkManifestRow
                    {
                        Character = columns[0],
                        Type = type,
                        Width = width,
                        Filename = filename,
                        Moji = moji,
                    };
                    result.Rows.Add(row);
                    if (visitRow != null)
                    {
                        string visitError = visitRow(row);
                        if (!string.IsNullOrEmpty(visitError))
                        {
                            result.Rows.Clear();
                            result.Error = visitError;
                            return result;
                        }
                    }
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Rows.Clear();
                result.Error = "Font manifest parse failed: " + ex.Message;
                return result;
            }
        }

        public static bool TryParse(
            string manifestText,
            FontBulkManifestMode mode,
            out List<FontBulkManifestRow> rows,
            out string error)
        {
            FontBulkManifestParseResult parsed = Parse(manifestText, mode);
            rows = parsed.Rows;
            error = parsed.Error;
            return parsed.Success;
        }

        /// <summary>Format rows with the canonical header and LF endings.</summary>
        public static string Format(
            IEnumerable<FontBulkManifestRow> rows,
            FontBulkManifestMode mode = FontBulkManifestMode.LegacyCompatible)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var sb = new StringBuilder();
            var strictKeys = mode == FontBulkManifestMode.StrictLibrary
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;
            uint priorMoji = 0;
            int priorStyleRank = -1;
            bool hasPriorStrictRow = false;
            sb.Append(Header);
            sb.Append('\n');
            foreach (FontBulkManifestRow row in rows)
            {
                if (row == null)
                    throw new ArgumentException("Manifest rows cannot be null.",
                        nameof(rows));
                string filename = row.Filename ?? "";
                if (mode == FontBulkManifestMode.StrictLibrary)
                {
                    if (row.Type != "item" && row.Type != "text")
                        throw new ArgumentException("Invalid strict font style.",
                            nameof(rows));
                    if (row.Width < 1 || row.Width > 16)
                        throw new ArgumentException("Invalid strict font width.",
                            nameof(rows));
                    if (!IsSingleUnicodeScalar(row.Character))
                        throw new ArgumentException(
                            "Strict manifest characters must be one Unicode scalar.",
                            nameof(rows));
                    string expected = CanonicalFilename(row.Type, row.Moji);
                    if (filename.Length == 0) filename = expected;
                    if (!string.Equals(filename, expected,
                        StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "Strict manifest filename is not canonical.",
                            nameof(rows));
                    }
                    string key = row.Moji.ToString(
                        "X8", CultureInfo.InvariantCulture)
                        + "\0" + row.Type;
                    if (!strictKeys.Add(key))
                        throw new ArgumentException(
                            "Strict manifest rows contain a duplicate moji/style pair.",
                            nameof(rows));
                    int styleRank = row.Type == "item" ? 0 : 1;
                    if (hasPriorStrictRow
                        && (row.Moji < priorMoji
                            || (row.Moji == priorMoji
                                && styleRank <= priorStyleRank)))
                    {
                        throw new ArgumentException(
                            "Strict manifest rows must be ordered by numeric moji, then item before text.",
                            nameof(rows));
                    }
                    priorMoji = row.Moji;
                    priorStyleRank = styleRank;
                    hasPriorStrictRow = true;
                }

                sb.Append(row.Character);
                sb.Append('\t');
                sb.Append(row.Type);
                sb.Append('\t');
                sb.Append(row.Width.ToString(CultureInfo.InvariantCulture));
                sb.Append('\t');
                sb.Append(filename);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string CanonicalFilename(string type, uint moji)
            => type + "_" + U.ToHexString(moji) + ".png";

        /// <summary>
        /// Recover the engine moji from the final underscore-delimited
        /// hexadecimal suffix.  This deliberately retains the legacy acceptance
        /// of padded and mixed-case hex.
        /// </summary>
        public static bool TryParseMojiFromFilename(
            string pngName,
            out uint moji)
        {
            moji = 0;
            if (string.IsNullOrEmpty(pngName)) return false;
            int dot = pngName.LastIndexOf('.');
            string stem = dot >= 0 ? pngName.Substring(0, dot) : pngName;
            int underscore = stem.LastIndexOf('_');
            if (underscore < 0 || underscore + 1 >= stem.Length) return false;
            return uint.TryParse(
                stem.Substring(underscore + 1),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out moji);
        }

        static FontBulkManifestParseResult Fail(
            FontBulkManifestParseResult result,
            int line,
            string detail)
        {
            result.Rows.Clear();
            result.Error = "Invalid font manifest row "
                + line.ToString(CultureInfo.InvariantCulture)
                + ": " + detail + ".";
            return result;
        }

        static bool IsSingleUnicodeScalar(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Length == 1)
                return !char.IsSurrogate(text[0]);
            return text.Length == 2 && char.IsSurrogatePair(text, 0);
        }
    }
}
