using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FEBuilderGBA
{
    /// <summary>
    /// Pure, testable Core helper for Tiled (<c>.tmx</c> / <c>.tsx</c>) import/export
    /// in the Avalonia Visual Map Editor (#1387, follow-up to #1382). Mirrors
    /// <see cref="MapExportCsv"/>: no Avalonia / System.Drawing dependencies, so the
    /// parse/emit + encoding decoders are unit-testable in <c>FEBuilderGBA.Core.Tests</c>.
    ///
    /// <para><b>GID ↔ MAR convention (documented, reproducible)</b></para>
    /// <list type="bullet">
    ///   <item><description><c>gid - 1 == chipsetIndex == MAR &gt;&gt; 2</c>
    ///     (the existing <see cref="MapEditorTilesetCore.MarToChipsetIndex"/> /
    ///     <see cref="MapEditorTilesetCore.ChipsetIndexToMar"/> = <c>&gt;&gt;2</c> / <c>&lt;&lt;2</c>).</description></item>
    ///   <item><description>The chipset image is a <see cref="MapEditorTilesetCore.PALETTE_COLUMNS"/>-column
    ///     (32) grid, each chipset <see cref="MapEditorTilesetCore.CHIPSET_PIXEL_SIZE"/>×16 px —
    ///     exactly what <see cref="MapEditorTilesetCore.RenderChipsetPalette"/> renders. So the emitted
    ///     <c>.tsx</c> uses <c>columns=32, tilewidth=tileheight=16, firstgid=1</c>.</description></item>
    ///   <item><description><b>Empty cells:</b> Tiled <c>gid 0</c> ("empty") imports to <b>MAR 0</b>
    ///     (chipset 0) so the grid stays exactly <c>W×H</c> for the
    ///     <see cref="MapEditorTilesetCore.TryStageGridEdit"/> / <c>ApplyMapGrid</c> path. Export emits
    ///     MAR 0 as <c>gid 1</c> (<c>(0&gt;&gt;2)+1</c>, matching WinForms <c>SaveAsTMX</c>). Therefore
    ///     <b>textual GID round-trip is NOT claimed for empty cells</b> — round-trip equality is on the
    ///     decoded MAR grid, with empty tiles normalized to chipset 0.</description></item>
    ///   <item><description>Tiled per-tile flip/rotation flags (the top 4 bits of a 32-bit GID) are
    ///     masked off on import (GBA chipset indices carry no per-tile flip), matching the WinForms
    ///     <c>m &amp; 0xffff</c> reduction.</description></item>
    /// </list>
    /// </summary>
    public static class MapTmxCore
    {
        /// <summary>Tiled GID flip/rotation flag mask (top 4 bits of a 32-bit GID).</summary>
        const uint TMX_FLIP_FLAGS = 0x80000000u | 0x40000000u | 0x20000000u | 0x10000000u;

        /// <summary>Tile pixel size for the emitted Tiled project (matches WinForms <c>SaveAsTMX</c>).</summary>
        public const int TILE_PIXELS = MapEditorTilesetCore.CHIPSET_PIXEL_SIZE; // 16

        /// <summary>Tileset column count for the emitted <c>.tsx</c> (32-wide chipset grid).</summary>
        public const int TILESET_COLUMNS = MapEditorTilesetCore.PALETTE_COLUMNS; // 32

        /// <summary>
        /// Hard upper bound on a decoded/decompressed tile-layer payload. The largest
        /// useful layer is 64×64 tiles × 4 bytes/GID = 16384 bytes; the cap adds generous
        /// slack so a hostile (zip-bomb / oversized-base64) TMX fails fast instead of
        /// allocating unbounded memory. ParseTmx independently rejects by tile count.
        /// </summary>
        const int MAX_DECODED_BYTES = 64 * 64 * 4 * 4; // 65,536 bytes (64 KB) = 4x the 16 KB max layer

        /// <summary>
        /// Parse a Tiled <c>.tmx</c> document's first tile layer into width, height and a
        /// row-major array of MAR values. Handles all common <c>&lt;data&gt;</c> encodings:
        /// plain CSV, the default <c>&lt;tile gid=".."/&gt;</c> XML, Base64, Base64+gzip and
        /// Base64+zlib (mirrors WinForms <c>MapEditorForm.ImportTMXData</c>).
        ///
        /// <para>Returns false + a human-readable <paramref name="error"/> on any parse or
        /// validation failure. Never throws on malformed input.</para>
        /// </summary>
        /// <param name="xml">The full <c>.tmx</c> XML text.</param>
        /// <param name="width">Parsed map width (tiles).</param>
        /// <param name="height">Parsed map height (tiles).</param>
        /// <param name="mars">Row-major MAR values, length == width*height.</param>
        /// <param name="error">Human-readable error message, or null on success.</param>
        public static bool ParseTmx(string xml, out int width, out int height, out ushort[] mars, out string error)
        {
            width = 0;
            height = 0;
            mars = null;
            error = null;

            if (string.IsNullOrWhiteSpace(xml))
            {
                error = "TMX is empty";
                return false;
            }

            XDocument doc;
            try
            {
                // Safe parse: no DTD processing, no external entity / resource resolution.
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                };
                using (var sr = new StringReader(xml))
                using (var reader = XmlReader.Create(sr, settings))
                {
                    doc = XDocument.Load(reader);
                }
            }
            catch (Exception ex)
            {
                error = "malformed TMX XML: " + ex.Message;
                return false;
            }

            XElement map = doc.Root;
            if (map == null || !string.Equals(map.Name.LocalName, "map", StringComparison.OrdinalIgnoreCase))
            {
                error = "missing <map> root element";
                return false;
            }

            // Infinite maps store chunked data we do not support.
            if (string.Equals((string)map.Attribute("infinite"), "1", StringComparison.Ordinal))
            {
                error = "infinite Tiled maps are not supported (set Map -> Map Properties -> Infinite = false)";
                return false;
            }

            if (!TryGetIntAttr(map, "width", out width) || !TryGetIntAttr(map, "height", out height))
            {
                error = "missing or non-numeric map width/height";
                return false;
            }
            if (width <= 0 || height <= 0 || width > 64 || height > 64)
            {
                error = $"invalid dimensions {width}x{height} (must be 1..64 in each dimension)";
                return false;
            }

            // Validate the tileset reference(s): a single tileset with firstgid==1.
            // (Avalonia maps are single-chipset; multi-tileset / non-1 firstgid would
            // make the gid->chipset mapping ambiguous, so reject explicitly.)
            var tilesets = new List<XElement>(map.ElementsLocal("tileset"));
            if (tilesets.Count > 1)
            {
                error = "multiple <tileset> elements are not supported (Avalonia maps use a single chipset)";
                return false;
            }
            if (tilesets.Count == 1)
            {
                // firstgid defaults to 1 in Tiled, but if present it MUST be 1: a
                // missing/non-numeric value is rejected explicitly so the gid->chipset
                // mapping assumption (gid-1 == chipsetIndex) can never be silently wrong.
                var firstgidAttr = tilesets[0].Attribute("firstgid");
                if (firstgidAttr != null)
                {
                    if (!int.TryParse(firstgidAttr.Value.Trim(), out int firstgid) || firstgid != 1)
                    {
                        error = $"unsupported firstgid=\"{firstgidAttr.Value}\" (only firstgid=1 is supported)";
                        return false;
                    }
                }
            }

            // First tile layer's <data>.
            XElement layer = null;
            foreach (var l in map.ElementsLocal("layer")) { layer = l; break; }
            if (layer == null)
            {
                error = "no <layer> element found";
                return false;
            }

            // If the layer declares its own width/height, they MUST match the map's.
            // A layer whose dimensions disagree (even with a coincidentally-matching
            // raw tile count) is an authoring error, not a valid import.
            if (TryGetIntAttr(layer, "width", out int layerW) && layerW != width)
            {
                error = $"layer width {layerW} does not match map width {width}";
                return false;
            }
            if (TryGetIntAttr(layer, "height", out int layerH) && layerH != height)
            {
                error = $"layer height {layerH} does not match map height {height}";
                return false;
            }

            XElement data = null;
            foreach (var d in layer.ElementsLocal("data")) { data = d; break; }
            if (data == null)
            {
                error = "the first <layer> has no <data> element";
                return false;
            }

            string encoding = ((string)data.Attribute("encoding") ?? "").Trim().ToLowerInvariant();
            string compression = ((string)data.Attribute("compression") ?? "").Trim().ToLowerInvariant();

            uint[] gids;
            if (encoding == "csv")
            {
                if (!DecodeCsv(data.Value, out gids, out error)) return false;
            }
            else if (encoding == "base64")
            {
                if (!DecodeBase64(data.Value, compression, out gids, out error)) return false;
            }
            else if (encoding == "")
            {
                // Default Tiled XML: <tile gid=".."/> children.
                if (!DecodeTileElements(data, out gids, out error)) return false;
            }
            else
            {
                error = $"unsupported <data> encoding \"{encoding}\"";
                return false;
            }

            int expected = width * height;
            if (gids.Length != expected)
            {
                error = $"tile count {gids.Length} does not match map dimensions {width}x{height} ({expected})";
                return false;
            }

            mars = new ushort[expected];
            for (int i = 0; i < expected; i++)
            {
                if (!GidToMar(gids[i], out ushort mar, out error))
                {
                    error = $"tile {i}: {error}";
                    return false;
                }
                mars[i] = mar;
            }
            return true;
        }

        /// <summary>
        /// Convert a Tiled GID (with optional flip flags) to a MAR value.
        /// <c>gid 0</c> (empty) → MAR 0; otherwise <c>MAR = (gid - 1) &lt;&lt; 2</c>.
        /// Rejects gids whose chipset index is outside [0, CHIPSET_COUNT).
        /// </summary>
        public static bool GidToMar(uint rawGid, out ushort mar, out string error)
        {
            mar = 0;
            error = null;
            uint gid = rawGid & ~TMX_FLIP_FLAGS; // strip flip/rotation flags
            if (gid == 0)
            {
                mar = 0; // empty cell -> chipset 0
                return true;
            }
            long chipsetIndex = (long)gid - 1;
            if (chipsetIndex < 0 || chipsetIndex >= MapEditorTilesetCore.CHIPSET_COUNT)
            {
                error = $"gid {gid} maps to chipset index {chipsetIndex}, outside [0, {MapEditorTilesetCore.CHIPSET_COUNT})";
                return false;
            }
            mar = MapEditorTilesetCore.ChipsetIndexToMar((int)chipsetIndex);
            return true;
        }

        /// <summary>Convert a MAR value to its Tiled GID: <c>(MAR &gt;&gt; 2) + 1</c>.</summary>
        public static int MarToGid(ushort mar) => MapEditorTilesetCore.MarToChipsetIndex(mar) + 1;

        /// <summary>
        /// Serialize map data (the in-memory <c>width|height|u16[]</c> cache, as produced by
        /// <c>MapDecompressCore</c> / consumed by <see cref="MapExportCsv.Serialize"/>) to a
        /// Tiled <c>.tmx</c> using the default <c>&lt;tile gid=".."/&gt;</c> XML layer encoding
        /// (matching WinForms <c>SaveAsTMX</c>). Returns empty string for null/undersized input.
        /// </summary>
        /// <param name="mapData">width|height|row-major u16 MAR cache.</param>
        /// <param name="tilesetSource">The <c>.tsx</c> source filename to reference (e.g. <c>"foo.tsx"</c>).</param>
        public static string SerializeTmx(byte[] mapData, string tilesetSource)
        {
            if (mapData == null || mapData.Length < 2) return "";
            int w = mapData[0];
            int h = mapData[1];
            if (w == 0 || h == 0) return "";
            int needed = 2 + w * h * 2;
            if (mapData.Length < needed) return "";
            if (string.IsNullOrEmpty(tilesetSource)) tilesetSource = "tileset.tsx";

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<map version=\"1.0\" orientation=\"orthogonal\" renderorder=\"right-down\" width=\"{w}\" height=\"{h}\" tilewidth=\"{TILE_PIXELS}\" tileheight=\"{TILE_PIXELS}\">");
            sb.AppendLine($" <tileset firstgid=\"1\" source=\"{XmlEscapeAttr(tilesetSource)}\"/>");
            sb.AppendLine($" <layer name=\"Tile Layer 1\" width=\"{w}\" height=\"{h}\">");
            sb.AppendLine("  <data>");
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int offset = 2 + (y * w + x) * 2;
                    ushort mar = (ushort)(mapData[offset] | (mapData[offset + 1] << 8));
                    sb.AppendLine($"   <tile gid=\"{MarToGid(mar)}\"/>");
                }
            }
            sb.AppendLine("  </data>");
            sb.AppendLine(" </layer>");
            sb.AppendLine("</map>");
            return sb.ToString();
        }

        /// <summary>
        /// Serialize a Tiled <c>.tsx</c> tileset referencing the chipset PNG. The chipset image
        /// is a 32-column grid of 16×16 chipsets (see <see cref="MapEditorTilesetCore.RenderChipsetPalette"/>).
        /// </summary>
        /// <param name="imageSource">The chipset PNG filename to reference (e.g. <c>"foo.png"</c>).</param>
        /// <param name="imageWidthPx">PNG width in pixels.</param>
        /// <param name="imageHeightPx">PNG height in pixels.</param>
        /// <param name="tileCount">Number of chipsets in the image (rows*columns).</param>
        public static string SerializeTsx(string imageSource, int imageWidthPx, int imageHeightPx, int tileCount)
        {
            if (string.IsNullOrEmpty(imageSource)) imageSource = "tileset.png";
            if (tileCount < 0) tileCount = 0;
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<tileset version=\"1.0\" name=\"chipset\" tilewidth=\"{TILE_PIXELS}\" tileheight=\"{TILE_PIXELS}\" tilecount=\"{tileCount}\" columns=\"{TILESET_COLUMNS}\">");
            sb.AppendLine($" <image source=\"{XmlEscapeAttr(imageSource)}\" trans=\"ff00ff\" width=\"{imageWidthPx}\" height=\"{imageHeightPx}\"/>");
            sb.AppendLine("</tileset>");
            return sb.ToString();
        }

        // ---- encoding decoders ----

        static bool DecodeCsv(string text, out uint[] gids, out string error)
        {
            gids = Array.Empty<uint>();
            error = null;
            var list = new List<uint>();
            // Tiled CSV may span many lines; split on commas and whitespace.
            string[] tokens = text.Split(new[] { ',', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string tok in tokens)
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                if (!uint.TryParse(t, out uint v))
                {
                    error = $"non-numeric CSV gid '{t}'";
                    return false;
                }
                list.Add(v);
            }
            gids = list.ToArray();
            return true;
        }

        static bool DecodeTileElements(XElement data, out uint[] gids, out string error)
        {
            gids = Array.Empty<uint>();
            error = null;
            var list = new List<uint>();
            foreach (var tile in data.ElementsLocal("tile"))
            {
                string g = (string)tile.Attribute("gid");
                if (g == null) { list.Add(0); continue; } // <tile/> with no gid == empty
                if (!uint.TryParse(g.Trim(), out uint v))
                {
                    error = $"non-numeric <tile gid> '{g}'";
                    return false;
                }
                list.Add(v);
            }
            gids = list.ToArray();
            return true;
        }

        static bool DecodeBase64(string text, string compression, out uint[] gids, out string error)
        {
            gids = Array.Empty<uint>();
            error = null;

            // Guard against an oversized/hostile base64 block BEFORE allocating any
            // payload-sized buffer. The useful decoded layer is tiny (<=64*64*4 bytes);
            // base64 inflates 4:3 and compressed payloads are smaller still, so
            // MAX_DECODED_BYTES of raw <data> characters is a generous ceiling. Check
            // the raw input length first (cheap, no allocation), then strip whitespace.
            if (text != null && text.Length > MAX_DECODED_BYTES)
            {
                error = $"base64 <data> payload too large ({text.Length} chars, max {MAX_DECODED_BYTES})";
                return false;
            }
            // Strip ALL whitespace from the base64 payload (Tiled indents the block).
            var sb = new StringBuilder(text == null ? 0 : text.Length);
            if (text != null)
            {
                foreach (char c in text)
                {
                    if (char.IsWhiteSpace(c)) continue;
                    sb.Append(c);
                }
            }
            byte[] data;
            try
            {
                data = Convert.FromBase64String(sb.ToString());
            }
            catch (Exception ex)
            {
                error = "invalid base64 <data>: " + ex.Message;
                return false;
            }

            byte[] raw;
            try
            {
                if (compression == "zlib")
                    raw = InflateZlib(data);
                else if (compression == "gzip")
                    raw = InflateGzip(data);
                else if (compression == "")
                    raw = data;
                else
                {
                    error = $"unsupported <data> compression \"{compression}\"";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = $"failed to decompress {compression} <data>: " + ex.Message;
                return false;
            }

            if (raw.Length % 4 != 0)
            {
                error = $"decoded data length {raw.Length} is not a multiple of 4";
                return false;
            }
            var list = new List<uint>(raw.Length / 4);
            for (int i = 0; i + 4 <= raw.Length; i += 4)
            {
                uint m = (uint)(raw[i] | (raw[i + 1] << 8) | (raw[i + 2] << 16) | (raw[i + 3] << 24));
                list.Add(m);
            }
            gids = list.ToArray();
            return true;
        }

        static byte[] InflateZlib(byte[] input)
        {
            using (var src = new MemoryStream(input))
            using (var z = new ZLibStream(src, CompressionMode.Decompress))
                return InflateBounded(z);
        }

        static byte[] InflateGzip(byte[] input)
        {
            using (var src = new MemoryStream(input))
            using (var g = new GZipStream(src, CompressionMode.Decompress))
                return InflateBounded(g);
        }

        /// <summary>
        /// Copy a decompression stream into memory with a hard byte cap so a crafted
        /// "zip bomb" payload cannot exhaust memory: reads one byte past
        /// <see cref="MAX_DECODED_BYTES"/> and throws if the stream is still producing.
        /// </summary>
        static byte[] InflateBounded(System.IO.Stream decompressed)
        {
            using (var dst = new MemoryStream())
            {
                byte[] buf = new byte[8192];
                int total = 0;
                int n;
                while ((n = decompressed.Read(buf, 0, buf.Length)) > 0)
                {
                    total += n;
                    if (total > MAX_DECODED_BYTES)
                        throw new InvalidDataException(
                            $"decompressed data exceeds the maximum tile-layer size ({MAX_DECODED_BYTES} bytes)");
                    dst.Write(buf, 0, n);
                }
                return dst.ToArray();
            }
        }

        // ---- small helpers ----

        static bool TryGetIntAttr(XElement el, string name, out int value)
        {
            value = 0;
            var a = el.Attribute(name);
            if (a == null) return false;
            return int.TryParse(a.Value.Trim(), out value);
        }

        static string XmlEscapeAttr(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        // local-name-agnostic element enumeration (TMX files have no namespace, but be safe).
        static IEnumerable<XElement> ElementsLocal(this XElement parent, string localName)
        {
            foreach (var e in parent.Elements())
                if (string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                    yield return e;
        }
    }
}
