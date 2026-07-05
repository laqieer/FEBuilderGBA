using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
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
        /// Hard upper bound on the raw <c>.tmj</c> JSON text length. A valid 64×64 map
        /// (the largest we accept) is well under 100 KB even fully indented, so this
        /// generous 512 KB ceiling never rejects a real map but caps a hostile file
        /// (e.g. one declaring <c>width:1,height:1</c> but carrying a multi-megabyte
        /// <c>data</c> array) BEFORE it is handed to <see cref="JsonDocument.Parse"/> —
        /// a cheap, zero-allocation first line of defence. <see cref="ParseTmj"/> then
        /// independently rejects by array length before allocating the tile grid.
        /// </summary>
        const int MAX_TMJ_CHARS = 512 * 1024;

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

            return TryGidsToMars(gids, width, height, out mars, out error);
        }

        /// <summary>
        /// Shared tail for <see cref="ParseTmx"/> / <see cref="ParseTmj"/>: validate the
        /// decoded GID count against <paramref name="width"/>×<paramref name="height"/> and
        /// convert each GID to a MAR via <see cref="GidToMar"/>. Never throws.
        /// </summary>
        static bool TryGidsToMars(uint[] gids, int width, int height, out ushort[] mars, out string error)
        {
            mars = null;
            error = null;
            int expected = width * height;
            if (gids == null || gids.Length != expected)
            {
                error = $"tile count {(gids?.Length ?? 0)} does not match map dimensions {width}x{height} ({expected})";
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
        /// Parse a Tiled <c>.tmj</c> (JSON) document's first tile layer into width, height
        /// and a row-major array of MAR values — the JSON sibling of <see cref="ParseTmx"/>
        /// (#1796). Handles both tile-layer <c>data</c> representations: a native JSON GID
        /// array (default / <c>"csv"</c> encoding in Tiled) and a Base64 string
        /// (<c>"base64"</c> encoding, optional <c>gzip</c>/<c>zlib</c> compression — the
        /// same binary blob as <c>.tmx</c>, decoded by the shared <see cref="DecodeBase64"/>).
        ///
        /// <para>Never throws on malformed input: every JSON access is guarded
        /// (<c>TryGetProperty</c> + <c>ValueKind</c> + <c>TryGet*</c>, one outer try/catch),
        /// mirroring the repo idiom in <c>DecompAssetValidatorCore</c>. Applies the same
        /// validations and refusals as <see cref="ParseTmx"/> (dimensions 1..64, single
        /// <c>firstgid==1</c> tileset, layer dimension match, tile-count match) and rejects
        /// oversized payloads BEFORE allocating (raw length cap + array-length check).</para>
        /// </summary>
        /// <param name="json">The full <c>.tmj</c> JSON text.</param>
        /// <param name="width">Parsed map width (tiles).</param>
        /// <param name="height">Parsed map height (tiles).</param>
        /// <param name="mars">Row-major MAR values, length == width*height.</param>
        /// <param name="error">Human-readable error message, or null on success.</param>
        public static bool ParseTmj(string json, out int width, out int height, out ushort[] mars, out string error)
        {
            width = 0;
            height = 0;
            mars = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "TMJ is empty";
                return false;
            }
            // Reject a hostile oversized payload BEFORE parsing (cheap, no allocation).
            if (json.Length > MAX_TMJ_CHARS)
            {
                error = $"TMJ payload too large ({json.Length} chars, max {MAX_TMJ_CHARS})";
                return false;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    error = "TMJ root is not a JSON object";
                    return false;
                }

                // Infinite maps store chunked data we do not support.
                if (root.TryGetProperty("infinite", out JsonElement inf) && inf.ValueKind == JsonValueKind.True)
                {
                    error = "infinite Tiled maps are not supported (set Map -> Map Properties -> Infinite = false)";
                    return false;
                }

                if (!TryGetJsonInt(root, "width", out width) || !TryGetJsonInt(root, "height", out height))
                {
                    error = "missing or non-numeric map width/height";
                    return false;
                }
                if (width <= 0 || height <= 0 || width > 64 || height > 64)
                {
                    error = $"invalid dimensions {width}x{height} (must be 1..64 in each dimension)";
                    return false;
                }

                // Validate the tileset reference(s): at most one, and firstgid==1 if present
                // (so the gid->chipset mapping assumption gid-1==chipsetIndex is never wrong).
                if (root.TryGetProperty("tilesets", out JsonElement tilesets) && tilesets.ValueKind == JsonValueKind.Array)
                {
                    if (tilesets.GetArrayLength() > 1)
                    {
                        error = "multiple tilesets are not supported (Avalonia maps use a single chipset)";
                        return false;
                    }
                    foreach (JsonElement ts in tilesets.EnumerateArray())
                    {
                        if (ts.ValueKind == JsonValueKind.Object
                            && ts.TryGetProperty("firstgid", out JsonElement fg)
                            && fg.ValueKind == JsonValueKind.Number)
                        {
                            if (!fg.TryGetInt32(out int firstgid) || firstgid != 1)
                            {
                                error = $"unsupported firstgid={fg} (only firstgid=1 is supported)";
                                return false;
                            }
                        }
                    }
                }

                // First tile layer.
                JsonElement layer = default;
                bool foundLayer = false;
                if (root.TryGetProperty("layers", out JsonElement layers) && layers.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement l in layers.EnumerateArray())
                    {
                        if (l.ValueKind == JsonValueKind.Object
                            && l.TryGetProperty("type", out JsonElement lt)
                            && lt.ValueKind == JsonValueKind.String
                            && string.Equals(lt.GetString(), "tilelayer", StringComparison.Ordinal))
                        {
                            layer = l;
                            foundLayer = true;
                            break;
                        }
                    }
                }
                if (!foundLayer)
                {
                    error = "no tilelayer found";
                    return false;
                }

                // If the layer declares its own width/height, they MUST match the map's.
                if (TryGetJsonInt(layer, "width", out int layerW) && layerW != width)
                {
                    error = $"layer width {layerW} does not match map width {width}";
                    return false;
                }
                if (TryGetJsonInt(layer, "height", out int layerH) && layerH != height)
                {
                    error = $"layer height {layerH} does not match map height {height}";
                    return false;
                }

                if (!layer.TryGetProperty("data", out JsonElement dataEl))
                {
                    error = "the first tilelayer has no data";
                    return false;
                }

                int expected = width * height;
                uint[] gids;
                if (dataEl.ValueKind == JsonValueKind.Array)
                {
                    // Reject a mismatched (possibly hostile huge) array BEFORE allocating.
                    int actual = dataEl.GetArrayLength();
                    if (actual != expected)
                    {
                        error = $"tile count {actual} does not match map dimensions {width}x{height} ({expected})";
                        return false;
                    }
                    gids = new uint[expected];
                    int idx = 0;
                    foreach (JsonElement g in dataEl.EnumerateArray())
                    {
                        if (g.ValueKind != JsonValueKind.Number || !g.TryGetUInt32(out uint v))
                        {
                            error = $"tile {idx}: non-integer or out-of-range gid";
                            return false;
                        }
                        gids[idx++] = v;
                    }
                }
                else if (dataEl.ValueKind == JsonValueKind.String)
                {
                    // Base64-encoded layer data: require encoding=="base64", reuse the
                    // shared base64/gzip/zlib decoder (which caps size & inflate).
                    string encoding = "";
                    if (layer.TryGetProperty("encoding", out JsonElement enc) && enc.ValueKind == JsonValueKind.String)
                        encoding = (enc.GetString() ?? "").Trim().ToLowerInvariant();
                    if (encoding != "base64")
                    {
                        error = $"string tilelayer data requires encoding=\"base64\" (got \"{encoding}\")";
                        return false;
                    }
                    string compression = "";
                    if (layer.TryGetProperty("compression", out JsonElement comp) && comp.ValueKind == JsonValueKind.String)
                        compression = (comp.GetString() ?? "").Trim().ToLowerInvariant();
                    if (!DecodeBase64(dataEl.GetString(), compression, out gids, out error))
                        return false;
                }
                else
                {
                    error = "tilelayer data must be a GID array or a base64 string";
                    return false;
                }

                return TryGidsToMars(gids, width, height, out mars, out error);
            }
            catch (Exception ex)
            {
                error = "malformed TMJ JSON: " + ex.Message;
                width = 0;
                height = 0;
                mars = null;
                return false;
            }
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
        /// Serialize map data (the in-memory <c>width|height|u16[]</c> cache) to a Tiled
        /// <c>.tmj</c> (JSON) map — the JSON sibling of <see cref="SerializeTmx"/> (#1796).
        /// Emits the canonical Tiled fields (<c>type</c>, <c>version</c>, orientation, layer
        /// id/name/visible/opacity, …) so other Tiled-JSON consumers accept it, references
        /// the external <c>.tsx</c> tileset by source (fully supported by Tiled), and writes
        /// the tile layer as a plain GID array via <see cref="MarToGid"/>. Returns empty
        /// string for null/undersized input (same guard as <see cref="SerializeTmx"/>).
        /// </summary>
        /// <param name="mapData">width|height|row-major u16 MAR cache.</param>
        /// <param name="tilesetSource">The <c>.tsx</c> source filename to reference (e.g. <c>"foo.tsx"</c>).</param>
        public static string SerializeTmj(byte[] mapData, string tilesetSource)
        {
            if (mapData == null || mapData.Length < 2) return "";
            int w = mapData[0];
            int h = mapData[1];
            if (w == 0 || h == 0) return "";
            int needed = 2 + w * h * 2;
            if (mapData.Length < needed) return "";
            if (string.IsNullOrEmpty(tilesetSource)) tilesetSource = "tileset.tsx";

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
            {
                writer.WriteStartObject();
                writer.WriteString("type", "map");
                writer.WriteString("version", "1.0");
                writer.WriteString("tiledversion", "1.10");
                writer.WriteString("orientation", "orthogonal");
                writer.WriteString("renderorder", "right-down");
                writer.WriteNumber("width", w);
                writer.WriteNumber("height", h);
                writer.WriteNumber("tilewidth", TILE_PIXELS);
                writer.WriteNumber("tileheight", TILE_PIXELS);
                writer.WriteBoolean("infinite", false);
                writer.WriteNumber("nextlayerid", 2);
                writer.WriteNumber("nextobjectid", 1);

                writer.WritePropertyName("tilesets");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteNumber("firstgid", 1);
                writer.WriteString("source", tilesetSource);
                writer.WriteEndObject();
                writer.WriteEndArray();

                writer.WritePropertyName("layers");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteNumber("id", 1);
                writer.WriteString("name", "Tile Layer 1");
                writer.WriteString("type", "tilelayer");
                writer.WriteNumber("x", 0);
                writer.WriteNumber("y", 0);
                writer.WriteNumber("width", w);
                writer.WriteNumber("height", h);
                writer.WriteBoolean("visible", true);
                writer.WriteNumber("opacity", 1);
                writer.WritePropertyName("data");
                writer.WriteStartArray();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int offset = 2 + (y * w + x) * 2;
                        ushort mar = (ushort)(mapData[offset] | (mapData[offset + 1] << 8));
                        writer.WriteNumberValue(MarToGid(mar));
                    }
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
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

        /// <summary>
        /// Null-safe read of an integer JSON property. Returns false (leaving
        /// <paramref name="value"/>=0) when the parent is not an object, the property is
        /// absent, is not a JSON number, or is not a 32-bit integer. Never throws.
        /// </summary>
        static bool TryGetJsonInt(JsonElement el, string name, out int value)
        {
            value = 0;
            if (el.ValueKind != JsonValueKind.Object) return false;
            if (!el.TryGetProperty(name, out JsonElement p)) return false;
            if (p.ValueKind != JsonValueKind.Number) return false;
            return p.TryGetInt32(out value);
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
