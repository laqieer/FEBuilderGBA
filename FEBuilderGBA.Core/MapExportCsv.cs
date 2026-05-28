using System.Text;

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
