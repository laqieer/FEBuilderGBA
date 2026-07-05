using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MapTmxCore"/> — Tiled (.tmx/.tsx) import/export for
    /// the Avalonia Visual Map Editor (#1387). Covers the GID↔MAR convention, every
    /// supported <c>&lt;data&gt;</c> encoding (CSV / default XML / base64 / base64+gzip /
    /// base64+zlib), validation refusals, and a .tmx→grid→.tmx re-emit round-trip.
    /// </summary>
    public class MapTmxCoreTests
    {
        // ---- helpers ----

        /// <summary>Build a width|height|row-major u16 MAR cache.</summary>
        static byte[] MakeMapData(int w, int h, ushort[] mars)
        {
            byte[] d = new byte[2 + w * h * 2];
            d[0] = (byte)w;
            d[1] = (byte)h;
            for (int i = 0; i < mars.Length; i++)
            {
                d[2 + i * 2] = (byte)(mars[i] & 0xFF);
                d[2 + i * 2 + 1] = (byte)(mars[i] >> 8);
            }
            return d;
        }

        static string Base64DataLayer(uint[] gids, string compression)
        {
            byte[] raw = new byte[gids.Length * 4];
            for (int i = 0; i < gids.Length; i++)
            {
                raw[i * 4 + 0] = (byte)(gids[i] & 0xFF);
                raw[i * 4 + 1] = (byte)((gids[i] >> 8) & 0xFF);
                raw[i * 4 + 2] = (byte)((gids[i] >> 16) & 0xFF);
                raw[i * 4 + 3] = (byte)((gids[i] >> 24) & 0xFF);
            }
            byte[] payload;
            if (compression == "gzip")
            {
                using var ms = new MemoryStream();
                using (var g = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) g.Write(raw, 0, raw.Length);
                payload = ms.ToArray();
            }
            else if (compression == "zlib")
            {
                using var ms = new MemoryStream();
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
                payload = ms.ToArray();
            }
            else payload = raw;

            string b64 = Convert.ToBase64String(payload);
            string compAttr = compression == "" ? "" : $" compression=\"{compression}\"";
            return $"   <data encoding=\"base64\"{compAttr}>\n    {b64}\n   </data>";
        }

        static string WrapTmx(int w, int h, string dataInner, string extra = "")
        {
            return
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                $"<map version=\"1.0\" orientation=\"orthogonal\" width=\"{w}\" height=\"{h}\" tilewidth=\"16\" tileheight=\"16\"{extra}>\n" +
                " <tileset firstgid=\"1\" source=\"t.tsx\"/>\n" +
                $" <layer name=\"L\" width=\"{w}\" height=\"{h}\">\n" +
                dataInner + "\n" +
                " </layer>\n" +
                "</map>\n";
        }

        // ---- GID <-> MAR ----

        [Theory]
        [InlineData(0, (ushort)0)]      // empty -> chipset 0
        [InlineData(1, (ushort)0)]      // gid 1 -> chipset 0 -> MAR 0
        [InlineData(2, (ushort)4)]      // gid 2 -> chipset 1 -> MAR 4
        [InlineData(5, (ushort)16)]     // gid 5 -> chipset 4 -> MAR 16
        public void GidToMar_MapsCorrectly(uint gid, ushort expectedMar)
        {
            Assert.True(MapTmxCore.GidToMar(gid, out ushort mar, out string err), err);
            Assert.Equal(expectedMar, mar);
        }

        [Fact]
        public void GidToMar_StripsFlipFlags()
        {
            // gid 2 with all flip flags set -> still chipset 1 -> MAR 4
            uint flipped = 2u | 0x80000000u | 0x40000000u | 0x20000000u | 0x10000000u;
            Assert.True(MapTmxCore.GidToMar(flipped, out ushort mar, out _));
            Assert.Equal((ushort)4, mar);
        }

        [Fact]
        public void GidToMar_RejectsOutOfRange()
        {
            // chipset index must be < 1024; gid 1025 -> index 1024 -> reject.
            Assert.False(MapTmxCore.GidToMar(1025, out _, out string err));
            Assert.Contains("outside", err);
        }

        [Theory]
        [InlineData((ushort)0, 1)]
        [InlineData((ushort)4, 2)]
        [InlineData((ushort)16, 5)]
        public void MarToGid_MapsCorrectly(ushort mar, int expectedGid)
        {
            Assert.Equal(expectedGid, MapTmxCore.MarToGid(mar));
        }

        [Fact]
        public void GidMar_RoundTrip()
        {
            for (int chipset = 0; chipset < 1024; chipset++)
            {
                ushort mar = MapEditorTilesetCore.ChipsetIndexToMar(chipset);
                int gid = MapTmxCore.MarToGid(mar);
                Assert.True(MapTmxCore.GidToMar((uint)gid, out ushort back, out _));
                Assert.Equal(mar, back);
            }
        }

        // ---- ParseTmx: each encoding ----

        [Fact]
        public void ParseTmx_Csv_Parses()
        {
            // 2x2, gids 1,2,3,4 -> MAR 0,4,8,12
            string inner = "   <data encoding=\"csv\">\n1,2,\n3,4\n   </data>";
            string xml = WrapTmx(2, 2, inner);
            Assert.True(MapTmxCore.ParseTmx(xml, out int w, out int h, out ushort[] mars, out string err), err);
            Assert.Equal(2, w);
            Assert.Equal(2, h);
            Assert.Equal(new ushort[] { 0, 4, 8, 12 }, mars);
        }

        [Fact]
        public void ParseTmx_DefaultXml_Parses()
        {
            string inner =
                "   <data>\n" +
                "    <tile gid=\"1\"/>\n" +
                "    <tile gid=\"2\"/>\n" +
                "    <tile gid=\"3\"/>\n" +
                "    <tile gid=\"4\"/>\n" +
                "   </data>";
            string xml = WrapTmx(2, 2, inner);
            Assert.True(MapTmxCore.ParseTmx(xml, out _, out _, out ushort[] mars, out string err), err);
            Assert.Equal(new ushort[] { 0, 4, 8, 12 }, mars);
        }

        [Fact]
        public void ParseTmx_Base64_Plain_Parses()
        {
            string inner = Base64DataLayer(new uint[] { 1, 2, 3, 4 }, "");
            string xml = WrapTmx(2, 2, inner);
            Assert.True(MapTmxCore.ParseTmx(xml, out _, out _, out ushort[] mars, out string err), err);
            Assert.Equal(new ushort[] { 0, 4, 8, 12 }, mars);
        }

        [Fact]
        public void ParseTmx_Base64_Gzip_Parses()
        {
            string inner = Base64DataLayer(new uint[] { 1, 2, 3, 4 }, "gzip");
            string xml = WrapTmx(2, 2, inner);
            Assert.True(MapTmxCore.ParseTmx(xml, out _, out _, out ushort[] mars, out string err), err);
            Assert.Equal(new ushort[] { 0, 4, 8, 12 }, mars);
        }

        [Fact]
        public void ParseTmx_Base64_Zlib_Parses()
        {
            string inner = Base64DataLayer(new uint[] { 1, 2, 3, 4 }, "zlib");
            string xml = WrapTmx(2, 2, inner);
            Assert.True(MapTmxCore.ParseTmx(xml, out _, out _, out ushort[] mars, out string err), err);
            Assert.Equal(new ushort[] { 0, 4, 8, 12 }, mars);
        }

        // ---- ParseTmx: refusals ----

        [Fact]
        public void ParseTmx_Empty_Fails()
        {
            Assert.False(MapTmxCore.ParseTmx("", out _, out _, out _, out string err));
            Assert.Contains("empty", err);
        }

        [Fact]
        public void ParseTmx_BadXml_Fails()
        {
            Assert.False(MapTmxCore.ParseTmx("<map><not closed", out _, out _, out _, out string err));
            Assert.Contains("malformed", err);
        }

        [Fact]
        public void ParseTmx_ZeroDim_Fails()
        {
            string inner = "   <data encoding=\"csv\">\n   </data>";
            string xml = WrapTmx(0, 0, inner);
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("invalid dimensions", err);
        }

        [Fact]
        public void ParseTmx_OversizedDim_Fails()
        {
            string inner = "   <data encoding=\"csv\">\n1\n   </data>";
            string xml = WrapTmx(65, 1, inner);
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("invalid dimensions", err);
        }

        [Fact]
        public void ParseTmx_DimensionMismatch_Refused()
        {
            // declares 2x2 but only 3 gids
            string inner = "   <data encoding=\"csv\">\n1,2,3\n   </data>";
            string xml = WrapTmx(2, 2, inner);
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("does not match", err);
        }

        [Fact]
        public void ParseTmx_Infinite_Refused()
        {
            string inner = "   <data encoding=\"csv\">\n1\n   </data>";
            string xml = WrapTmx(1, 1, inner, extra: " infinite=\"1\"");
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("infinite", err);
        }

        [Fact]
        public void ParseTmx_UnsupportedZstd_Fails()
        {
            string inner = "   <data encoding=\"base64\" compression=\"zstd\">\nAAAA\n   </data>";
            string xml = WrapTmx(1, 1, inner);
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("zstd", err);
        }

        [Fact]
        public void ParseTmx_MultiTileset_Refused()
        {
            string xml =
                "<?xml version=\"1.0\"?>\n" +
                "<map width=\"1\" height=\"1\" tilewidth=\"16\" tileheight=\"16\">\n" +
                " <tileset firstgid=\"1\" source=\"a.tsx\"/>\n" +
                " <tileset firstgid=\"100\" source=\"b.tsx\"/>\n" +
                " <layer width=\"1\" height=\"1\"><data encoding=\"csv\">1</data></layer>\n" +
                "</map>";
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("multiple", err);
        }

        [Fact]
        public void ParseTmx_NonOneFirstgid_Refused()
        {
            string xml =
                "<?xml version=\"1.0\"?>\n" +
                "<map width=\"1\" height=\"1\" tilewidth=\"16\" tileheight=\"16\">\n" +
                " <tileset firstgid=\"7\" source=\"a.tsx\"/>\n" +
                " <layer width=\"1\" height=\"1\"><data encoding=\"csv\">1</data></layer>\n" +
                "</map>";
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("firstgid", err);
        }

        [Fact]
        public void ParseTmx_OversizedBase64Payload_Refused()
        {
            // A base64 block far larger than any 64x64 layer must fail fast (no OOM).
            string big = new string('A', 64 * 64 * 4 * 4 + 16);
            string inner = $"   <data encoding=\"base64\">\n{big}\n   </data>";
            string xml = WrapTmx(2, 2, inner);
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("too large", err);
        }

        [Fact]
        public void ParseTmx_GzipZipBomb_Refused()
        {
            // Compress a payload that inflates well beyond the cap; decode must throw
            // internally and surface a clean "decompress" error, not OOM.
            byte[] huge = new byte[64 * 64 * 4 * 8]; // 512 KB of zeros -> tiny gzip
            using var ms = new MemoryStream();
            using (var g = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) g.Write(huge, 0, huge.Length);
            string b64 = Convert.ToBase64String(ms.ToArray());
            string inner = $"   <data encoding=\"base64\" compression=\"gzip\">\n{b64}\n   </data>";
            string xml = WrapTmx(2, 2, inner);
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("decompress", err, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParseTmx_LayerDimMismatch_Refused()
        {
            // map says 2x2 but the layer declares width=3 (raw tile count would still be 4).
            string xml =
                "<?xml version=\"1.0\"?>\n" +
                "<map width=\"2\" height=\"2\" tilewidth=\"16\" tileheight=\"16\">\n" +
                " <tileset firstgid=\"1\" source=\"t.tsx\"/>\n" +
                " <layer width=\"3\" height=\"2\"><data encoding=\"csv\">1,2,3,4</data></layer>\n" +
                "</map>";
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("layer width", err);
        }

        [Fact]
        public void ParseTmx_NonNumericFirstgid_Refused()
        {
            string xml =
                "<?xml version=\"1.0\"?>\n" +
                "<map width=\"1\" height=\"1\" tilewidth=\"16\" tileheight=\"16\">\n" +
                " <tileset firstgid=\"abc\" source=\"t.tsx\"/>\n" +
                " <layer width=\"1\" height=\"1\"><data encoding=\"csv\">1</data></layer>\n" +
                "</map>";
            Assert.False(MapTmxCore.ParseTmx(xml, out _, out _, out _, out string err));
            Assert.Contains("firstgid", err);
        }

        [Fact]
        public void ParseTmx_MissingFirstgid_Accepted()
        {
            // firstgid omitted -> Tiled defaults to 1 -> we accept it.
            string xml =
                "<?xml version=\"1.0\"?>\n" +
                "<map width=\"1\" height=\"1\" tilewidth=\"16\" tileheight=\"16\">\n" +
                " <tileset source=\"t.tsx\"/>\n" +
                " <layer width=\"1\" height=\"1\"><data encoding=\"csv\">2</data></layer>\n" +
                "</map>";
            Assert.True(MapTmxCore.ParseTmx(xml, out _, out _, out ushort[] mars, out string err), err);
            Assert.Equal(new ushort[] { 4 }, mars); // gid 2 -> MAR 4
        }

        // ---- Serialize ----

        [Fact]
        public void SerializeTmx_EmitsTileGids()
        {
            byte[] data = MakeMapData(2, 1, new ushort[] { 0, 4 });
            string tmx = MapTmxCore.SerializeTmx(data, "foo.tsx");
            Assert.Contains("width=\"2\" height=\"1\"", tmx);
            Assert.Contains("source=\"foo.tsx\"", tmx);
            Assert.Contains("<tile gid=\"1\"/>", tmx); // MAR 0 -> gid 1
            Assert.Contains("<tile gid=\"2\"/>", tmx); // MAR 4 -> gid 2
        }

        [Fact]
        public void SerializeTmx_NullOrUndersized_ReturnsEmpty()
        {
            Assert.Equal("", MapTmxCore.SerializeTmx(null, "t.tsx"));
            Assert.Equal("", MapTmxCore.SerializeTmx(new byte[] { 2, 2 }, "t.tsx")); // header only, no data
        }

        [Fact]
        public void SerializeTsx_ShapeIsCorrect()
        {
            string tsx = MapTmxCore.SerializeTsx("foo.png", 512, 80, 100);
            Assert.Contains("columns=\"32\"", tsx);
            Assert.Contains("tilewidth=\"16\"", tsx);
            Assert.Contains("tileheight=\"16\"", tsx);
            Assert.Contains("tilecount=\"100\"", tsx);
            Assert.Contains("source=\"foo.png\"", tsx);
            Assert.Contains("width=\"512\"", tsx);
            Assert.Contains("height=\"80\"", tsx);
        }

        // ---- round-trip ----

        [Fact]
        public void RoundTrip_TmxToGridToTmx_MarGridEqual()
        {
            ushort[] mars = { 0, 4, 8, 12, 16, 20 };
            byte[] data = MakeMapData(3, 2, mars);
            string tmx1 = MapTmxCore.SerializeTmx(data, "x.tsx");

            Assert.True(MapTmxCore.ParseTmx(tmx1, out int w, out int h, out ushort[] back, out string err), err);
            Assert.Equal(3, w);
            Assert.Equal(2, h);
            Assert.Equal(mars, back);

            // re-emit from the parsed grid and assert the .tmx text is identical
            byte[] data2 = MakeMapData(w, h, back);
            string tmx2 = MapTmxCore.SerializeTmx(data2, "x.tsx");
            Assert.Equal(tmx1, tmx2);
        }

        [Fact]
        public void RoundTrip_EmptyCellsNormalizeToChipset0()
        {
            // gid 0 (empty) and gid 1 both decode to MAR 0; on re-export both become gid 1.
            string inner = "   <data encoding=\"csv\">\n0,1\n   </data>";
            string xml = WrapTmx(2, 1, inner);
            Assert.True(MapTmxCore.ParseTmx(xml, out _, out _, out ushort[] mars, out string err), err);
            Assert.Equal(new ushort[] { 0, 0 }, mars);

            byte[] data = MakeMapData(2, 1, mars);
            string tmx = MapTmxCore.SerializeTmx(data, "x.tsx");
            // both cells re-emit as gid 1 (documented empty-cell normalization)
            int gid1Count = 0;
            int idx = 0;
            while ((idx = tmx.IndexOf("<tile gid=\"1\"/>", idx, StringComparison.Ordinal)) >= 0) { gid1Count++; idx += 1; }
            Assert.Equal(2, gid1Count);
        }

        // ================= .tmj (Tiled JSON) — #1796 =================

        static string TmjArray(int w, int h, uint[] gids, string tileset = "t.tsx", bool includeFirstgid = true)
        {
            string fg = includeFirstgid ? "\"firstgid\":1," : "";
            return "{\"type\":\"map\",\"width\":" + w + ",\"height\":" + h + ",\"infinite\":false," +
                   "\"tilesets\":[{" + fg + "\"source\":\"" + tileset + "\"}]," +
                   "\"layers\":[{\"type\":\"tilelayer\",\"width\":" + w + ",\"height\":" + h +
                   ",\"data\":[" + string.Join(",", gids) + "]}]}";
        }

        static string TmjBase64(int w, int h, uint[] gids, string compression)
        {
            byte[] raw = new byte[gids.Length * 4];
            for (int i = 0; i < gids.Length; i++)
            {
                raw[i * 4 + 0] = (byte)(gids[i] & 0xFF);
                raw[i * 4 + 1] = (byte)((gids[i] >> 8) & 0xFF);
                raw[i * 4 + 2] = (byte)((gids[i] >> 16) & 0xFF);
                raw[i * 4 + 3] = (byte)((gids[i] >> 24) & 0xFF);
            }
            byte[] payload;
            if (compression == "gzip")
            {
                using var ms = new MemoryStream();
                using (var g = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) g.Write(raw, 0, raw.Length);
                payload = ms.ToArray();
            }
            else if (compression == "zlib")
            {
                using var ms = new MemoryStream();
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
                payload = ms.ToArray();
            }
            else payload = raw;
            string b64 = Convert.ToBase64String(payload);
            string compProp = compression == "" ? "" : ",\"compression\":\"" + compression + "\"";
            return "{\"type\":\"map\",\"width\":" + w + ",\"height\":" + h + ",\"infinite\":false," +
                   "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                   "\"layers\":[{\"type\":\"tilelayer\",\"width\":" + w + ",\"height\":" + h +
                   ",\"encoding\":\"base64\"" + compProp + ",\"data\":\"" + b64 + "\"}]}";
        }

        // ---- ParseTmj: encodings ----

        [Fact]
        public void ParseTmj_Array_Parses()
        {
            string json = TmjArray(2, 1, new uint[] { 1, 2 }); // gid1->MAR0, gid2->MAR4
            Assert.True(MapTmxCore.ParseTmj(json, out int w, out int h, out ushort[] mars, out string err), err);
            Assert.Equal(2, w);
            Assert.Equal(1, h);
            Assert.Equal(new ushort[] { 0, 4 }, mars);
        }

        [Theory]
        [InlineData("")]
        [InlineData("gzip")]
        [InlineData("zlib")]
        public void ParseTmj_Base64_Parses(string compression)
        {
            string json = TmjBase64(2, 1, new uint[] { 1, 2 }, compression);
            Assert.True(MapTmxCore.ParseTmj(json, out _, out _, out ushort[] mars, out string err), err);
            Assert.Equal(new ushort[] { 0, 4 }, mars);
        }

        // ---- ParseTmj: refusals (never throw) ----

        [Fact]
        public void ParseTmj_Empty_Fails()
        {
            Assert.False(MapTmxCore.ParseTmj("", out _, out _, out _, out string err));
            Assert.False(MapTmxCore.ParseTmj("   ", out _, out _, out _, out _));
            Assert.NotNull(err);
        }

        [Fact]
        public void ParseTmj_BadJson_Fails_NoThrow()
        {
            Assert.False(MapTmxCore.ParseTmj("{ not valid json", out _, out _, out _, out string err));
            Assert.False(MapTmxCore.ParseTmj("[1,2,3]", out _, out _, out _, out _)); // root not an object
            Assert.NotNull(err);
        }

        [Fact]
        public void ParseTmj_ZeroOrOversizedDim_Fails()
        {
            Assert.False(MapTmxCore.ParseTmj(TmjArray(0, 0, new uint[0]), out _, out _, out _, out _));
            Assert.False(MapTmxCore.ParseTmj(TmjArray(65, 1, new uint[65]), out _, out _, out _, out _));
        }

        [Fact]
        public void ParseTmj_Infinite_Refused()
        {
            string json = "{\"width\":2,\"height\":1,\"infinite\":true," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"data\":[1,1]}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("infinite", err);
        }

        [Fact]
        public void ParseTmj_MultiTileset_Refused()
        {
            string json = "{\"width\":1,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"a.tsx\"},{\"firstgid\":2,\"source\":\"b.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"data\":[1]}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("tileset", err);
        }

        [Fact]
        public void ParseTmj_NonOneFirstgid_Refused()
        {
            string json = "{\"width\":1,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":5,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"data\":[1]}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("firstgid", err);
        }

        [Fact]
        public void ParseTmj_NonNumericFirstgid_Refused()
        {
            // A present-but-non-numeric firstgid ("5" as a string) must be rejected,
            // never silently treated as firstgid==1 (mirrors ParseTmx).
            string json = "{\"width\":1,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":\"5\",\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"data\":[1]}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("firstgid", err);
        }

        [Fact]
        public void ParseTmj_MissingFirstgid_Accepted()
        {
            // firstgid absent -> defaults to 1 (mirrors ParseTmx_MissingFirstgid_Accepted).
            string json = TmjArray(2, 1, new uint[] { 1, 2 }, includeFirstgid: false);
            Assert.True(MapTmxCore.ParseTmj(json, out _, out _, out ushort[] mars, out string err), err);
            Assert.Equal(new ushort[] { 0, 4 }, mars);
        }

        [Fact]
        public void ParseTmj_ArrayCountMismatch_Refused()
        {
            // Declares 2x1 but only 1 gid — rejected before allocation.
            string json = "{\"width\":2,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"data\":[1]}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("tile count", err);
        }

        [Fact]
        public void ParseTmj_HugeArraySmallDims_RefusedWithoutThrow()
        {
            // width=1,height=1 but a 5000-element data array (still under the char cap):
            // the GetArrayLength()!=w*h check must reject BEFORE allocating a tile grid.
            uint[] many = new uint[5000];
            for (int i = 0; i < many.Length; i++) many[i] = 1;
            string json = "{\"width\":1,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"data\":[" + string.Join(",", many) + "]}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("tile count", err);
        }

        [Fact]
        public void ParseTmj_OversizedRawPayload_RefusedBeforeParse()
        {
            // A payload larger than the 512 KB raw cap is rejected up-front (no parse/alloc).
            string big = new string('0', 512 * 1024 + 16);
            Assert.False(MapTmxCore.ParseTmj(big, out _, out _, out _, out string err));
            Assert.Contains("too large", err);
        }

        [Theory]
        [InlineData("\"2\"")]   // width as a string
        [InlineData("2.5")]      // width as a fractional number
        public void ParseTmj_TypeConfusionWidth_Fails(string widthLiteral)
        {
            string json = "{\"width\":" + widthLiteral + ",\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"data\":[1,2]}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.NotNull(err);
        }

        [Fact]
        public void ParseTmj_NegativeGid_Fails()
        {
            string json = "{\"width\":2,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"data\":[1,-3]}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.NotNull(err);
        }

        [Fact]
        public void ParseTmj_StringDataWithoutBase64Encoding_Fails()
        {
            string json = "{\"width\":1,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"data\":\"AQAAAA==\"}]}"; // no encoding
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("base64", err);
        }

        [Fact]
        public void ParseTmj_UnsupportedCompression_Fails()
        {
            string json = "{\"width\":1,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"encoding\":\"base64\",\"compression\":\"zstd\",\"data\":\"AQAAAA==\"}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("zstd", err);
        }

        [Fact]
        public void ParseTmj_NoTileLayer_Fails()
        {
            string json = "{\"width\":1,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"objectgroup\"}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("tilelayer", err);
        }

        [Fact]
        public void ParseTmj_LayerDimMismatch_Refused()
        {
            string json = "{\"width\":2,\"height\":1,\"infinite\":false," +
                          "\"tilesets\":[{\"firstgid\":1,\"source\":\"t.tsx\"}]," +
                          "\"layers\":[{\"type\":\"tilelayer\",\"width\":3,\"height\":1,\"data\":[1,2]}]}";
            Assert.False(MapTmxCore.ParseTmj(json, out _, out _, out _, out string err));
            Assert.Contains("layer width", err);
        }

        // ---- SerializeTmj ----

        [Fact]
        public void SerializeTmj_EmitsCanonicalFieldsAndGidArray()
        {
            byte[] data = MakeMapData(2, 1, new ushort[] { 0, 4 });
            string tmj = MapTmxCore.SerializeTmj(data, "foo.tsx");
            using var doc = System.Text.Json.JsonDocument.Parse(tmj); // must be valid JSON
            var root = doc.RootElement;
            Assert.Equal("map", root.GetProperty("type").GetString());
            Assert.Equal(2, root.GetProperty("width").GetInt32());
            Assert.Equal(1, root.GetProperty("height").GetInt32());
            Assert.False(root.GetProperty("infinite").GetBoolean());
            Assert.Equal("foo.tsx", root.GetProperty("tilesets")[0].GetProperty("source").GetString());
            var data0 = root.GetProperty("layers")[0].GetProperty("data");
            Assert.Equal(1, data0[0].GetInt32()); // MAR 0 -> gid 1
            Assert.Equal(2, data0[1].GetInt32()); // MAR 4 -> gid 2
        }

        [Fact]
        public void SerializeTmj_NullOrUndersized_ReturnsEmpty()
        {
            Assert.Equal("", MapTmxCore.SerializeTmj(null, "t.tsx"));
            Assert.Equal("", MapTmxCore.SerializeTmj(new byte[] { 2, 2 }, "t.tsx"));
        }

        // ---- round-trips ----

        [Fact]
        public void RoundTrip_TmjToGridToTmj_MarGridEqual()
        {
            ushort[] mars = { 0, 4, 8, 12, 16, 20 };
            byte[] data = MakeMapData(3, 2, mars);
            string tmj1 = MapTmxCore.SerializeTmj(data, "x.tsx");
            Assert.True(MapTmxCore.ParseTmj(tmj1, out int w, out int h, out ushort[] back, out string err), err);
            Assert.Equal(3, w);
            Assert.Equal(2, h);
            Assert.Equal(mars, back);
            byte[] data2 = MakeMapData(w, h, back);
            string tmj2 = MapTmxCore.SerializeTmj(data2, "x.tsx");
            Assert.Equal(tmj1, tmj2);
        }

        [Fact]
        public void RoundTrip_CrossFormat_TmxToTmj_MarGridEqual()
        {
            ushort[] mars = { 0, 4, 8, 12 };
            byte[] data = MakeMapData(2, 2, mars);
            // .tmx -> grid -> .tmj -> grid must preserve the MAR grid.
            string tmx = MapTmxCore.SerializeTmx(data, "x.tsx");
            Assert.True(MapTmxCore.ParseTmx(tmx, out int w1, out int h1, out ushort[] g1, out string e1), e1);
            string tmj = MapTmxCore.SerializeTmj(MakeMapData(w1, h1, g1), "x.tsx");
            Assert.True(MapTmxCore.ParseTmj(tmj, out int w2, out int h2, out ushort[] g2, out string e2), e2);
            Assert.Equal(mars, g2);
            Assert.Equal(w1, w2);
            Assert.Equal(h1, h2);
        }

        // ---- LooksLikeTmj: import dispatch decision (#1796) ----

        [Theory]
        [InlineData("map.tmj", "<map/>", true)]      // .tmj extension wins even over XML content
        [InlineData("MAP.TMJ", "", true)]            // case-insensitive extension
        [InlineData("map.tmx", "<?xml?><map/>", false)] // .tmx extension, XML content
        [InlineData("map.tmx", "{\"type\":\"map\"}", true)]  // extension .tmx but JSON content -> sniff wins
        [InlineData("", "   \r\n{ }", true)]         // leading whitespace then '{'
        [InlineData("", "\uFEFF{ }", true)]          // UTF-8 BOM then '{'
        [InlineData("", "<map/>", false)]            // XML content, no name
        [InlineData(null, null, false)]              // null-safe
        public void LooksLikeTmj_DetectsJson(string fileName, string text, bool expected)
        {
            Assert.Equal(expected, MapTmxCore.LooksLikeTmj(fileName, text));
        }
    }
}
