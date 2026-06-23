using System;
using System.Text;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// Serializes Visual Map Editor map data (header + row-major u16 MAR values)
    /// to a CSV string. Used by the Avalonia MapEditorView export button (#658).
    /// </summary>
    /// <remarks>
    /// Input layout matches the in-memory cache populated by
    /// <c>MapEditorViewModel</c> (and produced by <c>MapDecompressCore</c>):
    /// <list type="bullet">
    ///   <item><description>Byte 0: map width (tiles)</description></item>
    ///   <item><description>Byte 1: map height (tiles)</description></item>
    ///   <item><description>Bytes 2..: width*height little-endian u16 MAR values, row-major</description></item>
    /// </list>
    /// CSV format produced:
    /// <code>
    /// # FEBuilderGBA Map Export: width=N, height=M
    /// &lt;row 0 MAR values, comma-separated decimal&gt;
    /// &lt;row 1 MAR values, comma-separated decimal&gt;
    /// ...
    /// </code>
    /// </remarks>
    public static class MapExportCsv
    {
        /// <summary>
        /// Parse a CSV string produced by <see cref="Serialize"/> back into width,
        /// height, and a row-major array of MAR values. This is the inverse of
        /// <see cref="Serialize"/> and round-trips it losslessly.
        /// Returns false + error on any parse or validation failure.
        /// </summary>
        /// <param name="csv">CSV text (as written by <see cref="Serialize"/>).</param>
        /// <param name="width">Parsed map width (tiles).</param>
        /// <param name="height">Parsed map height (tiles).</param>
        /// <param name="mars">Row-major MAR values, length == width*height.</param>
        /// <param name="error">Human-readable error message, or null on success.</param>
        public static bool Parse(string csv, out int width, out int height, out ushort[] mars, out string error)
        {
            width = 0;
            height = 0;
            mars = null;
            error = null;

            if (string.IsNullOrEmpty(csv))
            {
                error = "CSV is empty";
                return false;
            }

            // Split on newlines; strip \r
            string[] allLines = csv.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            // Find and parse header (first non-empty line)
            string header = null;
            int headerLineIdx = -1;
            for (int i = 0; i < allLines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(allLines[i]))
                {
                    header = allLines[i].Trim();
                    headerLineIdx = i;
                    break;
                }
            }
            if (header == null)
            {
                error = "missing or malformed header (expected '# FEBuilderGBA Map Export: width=N, height=M')";
                return false;
            }

            var headerMatch = Regex.Match(header,
                @"width\s*=\s*(\d+).*height\s*=\s*(\d+)",
                RegexOptions.IgnoreCase);
            if (!headerMatch.Success)
            {
                error = "missing or malformed header (expected '# FEBuilderGBA Map Export: width=N, height=M')";
                return false;
            }

            if (!int.TryParse(headerMatch.Groups[1].Value, out width) ||
                !int.TryParse(headerMatch.Groups[2].Value, out height))
            {
                error = "missing or malformed header (expected '# FEBuilderGBA Map Export: width=N, height=M')";
                return false;
            }

            if (width <= 0 || height <= 0 || width > 64 || height > 64)
            {
                error = $"invalid dimensions {width}x{height} (must be 1..64 in each dimension)";
                return false;
            }

            // Collect non-empty data lines after the header
            var dataLines = new System.Collections.Generic.List<string>();
            for (int i = headerLineIdx + 1; i < allLines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(allLines[i]))
                    dataLines.Add(allLines[i]);
            }

            if (dataLines.Count != height)
            {
                error = $"expected {height} data rows but found {dataLines.Count}";
                return false;
            }

            mars = new ushort[width * height];
            for (int row = 0; row < height; row++)
            {
                string[] cells = dataLines[row].Split(',');
                if (cells.Length != width)
                {
                    error = $"row {row}: expected {width} cells but found {cells.Length}";
                    return false;
                }
                for (int col = 0; col < width; col++)
                {
                    string cell = cells[col].Trim();
                    if (!int.TryParse(cell, out int val))
                    {
                        error = $"row {row}, column {col}: non-numeric value '{cell}'";
                        return false;
                    }
                    if (val < 0 || val > 65535)
                    {
                        error = $"row {row}, column {col}: value {val} out of range [0, 65535]";
                        return false;
                    }
                    mars[row * width + col] = (ushort)val;
                }
            }

            return true;
        }

        /// <summary>
        /// Serialize map data to CSV. Returns empty string when input is null,
        /// undersized, or has zero dimensions (callers should treat empty as
        /// "no data to export").
        /// </summary>
        public static string Serialize(byte[] mapData)
        {
            if (mapData == null || mapData.Length < 2) return "";
            int w = mapData[0];
            int h = mapData[1];
            if (w == 0 || h == 0) return "";
            int needed = 2 + w * h * 2;
            if (mapData.Length < needed) return "";

            var sb = new StringBuilder();
            sb.AppendLine($"# FEBuilderGBA Map Export: width={w}, height={h}");
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int offset = 2 + (y * w + x) * 2;
                    ushort mar = (ushort)(mapData[offset] | (mapData[offset + 1] << 8));
                    if (x > 0) sb.Append(',');
                    sb.Append(mar);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
