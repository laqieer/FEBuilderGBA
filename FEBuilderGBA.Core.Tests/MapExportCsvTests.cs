using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MapExportCsv.Serialize"/> (#658 slice B) and
    /// <see cref="MapExportCsv.Parse"/> (#1382).
    /// </summary>
    public class MapExportCsvTests
    {
        [Fact]
        public void Serialize_2x2Map_ProducesExpectedCsv()
        {
            // header w=2 h=2, then 4 u16 MAR values
            // (0,0)=0x0001, (1,0)=0x0002, (0,1)=0x0003, (1,1)=0x0004
            byte[] data = new byte[2 + 4 * 2];
            data[0] = 2; data[1] = 2;
            data[2] = 1; data[3] = 0;  // 1
            data[4] = 2; data[5] = 0;  // 2
            data[6] = 3; data[7] = 0;  // 3
            data[8] = 4; data[9] = 0;  // 4

            string csv = MapExportCsv.Serialize(data);
            Assert.Contains("width=2, height=2", csv);
            Assert.Contains("1,2", csv);
            Assert.Contains("3,4", csv);
        }

        [Fact]
        public void Serialize_NullData_ReturnsEmpty()
        {
            Assert.Equal("", MapExportCsv.Serialize(null));
        }

        [Fact]
        public void Serialize_ZeroDimensions_ReturnsEmpty()
        {
            byte[] data = new byte[2];
            data[0] = 0; data[1] = 0;
            Assert.Equal("", MapExportCsv.Serialize(data));
        }

        [Fact]
        public void Serialize_UndersizedBuffer_ReturnsEmpty()
        {
            // Claims 2x2 (=8 body bytes) but only provides 4 body bytes — must be rejected.
            byte[] data = new byte[2 + 2 * 2];
            data[0] = 2; data[1] = 2;
            data[2] = 1; data[3] = 0;
            data[4] = 2; data[5] = 0;
            Assert.Equal("", MapExportCsv.Serialize(data));
        }

        [Fact]
        public void Serialize_OnlyHeaderByte_ReturnsEmpty()
        {
            byte[] data = new byte[1];
            data[0] = 4;
            Assert.Equal("", MapExportCsv.Serialize(data));
        }

        [Fact]
        public void Serialize_HighMarValues_PreservesLittleEndianness()
        {
            // 1x1 map, MAR=0x12AB → little-endian bytes AB, 12.
            byte[] data = new byte[2 + 1 * 2];
            data[0] = 1; data[1] = 1;
            data[2] = 0xAB; data[3] = 0x12;

            string csv = MapExportCsv.Serialize(data);
            Assert.Contains("width=1, height=1", csv);
            // 0x12AB = 4779
            Assert.Contains("4779", csv);
        }

        [Fact]
        public void Serialize_RowSeparation_OneLinePerRow()
        {
            // 1x3 map: 3 rows × 1 col, each row should print on its own line.
            byte[] data = new byte[2 + 3 * 2];
            data[0] = 1; data[1] = 3;
            data[2] = 7; data[3] = 0;   // row 0
            data[4] = 8; data[5] = 0;   // row 1
            data[6] = 9; data[7] = 0;   // row 2

            string csv = MapExportCsv.Serialize(data);
            // Split on either CRLF or LF; expect header + 3 data rows + trailing blank.
            string[] lines = csv.Replace("\r\n", "\n").Split('\n');
            Assert.StartsWith("# FEBuilderGBA Map Export", lines[0]);
            Assert.Equal("7", lines[1]);
            Assert.Equal("8", lines[2]);
            Assert.Equal("9", lines[3]);
        }

        // =====================================================================
        // Parse tests (#1382)
        // =====================================================================

        [Fact]
        public void Parse_Valid2x2_RoundTrips()
        {
            // Build a 2x2 map buffer and Serialize it, then Parse it back.
            byte[] data = new byte[2 + 4 * 2];
            data[0] = 2; data[1] = 2;
            data[2] = 1; data[3] = 0;  // (0,0)=1
            data[4] = 2; data[5] = 0;  // (1,0)=2
            data[6] = 3; data[7] = 0;  // (0,1)=3
            data[8] = 4; data[9] = 0;  // (1,1)=4

            string csv = MapExportCsv.Serialize(data);
            bool ok = MapExportCsv.Parse(csv, out int w, out int h, out ushort[] mars, out string err);

            Assert.True(ok, err);
            Assert.Equal(2, w);
            Assert.Equal(2, h);
            Assert.NotNull(mars);
            Assert.Equal(4, mars.Length);
            Assert.Equal(1, mars[0]);
            Assert.Equal(2, mars[1]);
            Assert.Equal(3, mars[2]);
            Assert.Equal(4, mars[3]);
        }

        [Fact]
        public void Parse_LosslessRoundTrip_SerializedCsvMatchesOriginal()
        {
            // Build buffer → Serialize → Parse → reconstruct buffer → re-Serialize → assert equal.
            byte[] data = new byte[2 + 4 * 2];
            data[0] = 2; data[1] = 2;
            data[2] = 0xAB; data[3] = 0x12;  // MAR 0x12AB
            data[4] = 0x00; data[5] = 0x80;  // MAR 0x8000
            data[6] = 0xFF; data[7] = 0xFF;  // MAR 0xFFFF
            data[8] = 0x01; data[9] = 0x00;  // MAR 0x0001

            string csv1 = MapExportCsv.Serialize(data);
            bool ok = MapExportCsv.Parse(csv1, out int w, out int h, out ushort[] mars, out string err);
            Assert.True(ok, err);

            // Reconstruct the buffer from parsed mars (header + row-major u16 LE).
            byte[] rebuilt = new byte[2 + w * h * 2];
            rebuilt[0] = (byte)w;
            rebuilt[1] = (byte)h;
            for (int i = 0; i < mars.Length; i++)
            {
                rebuilt[2 + i * 2] = (byte)(mars[i] & 0xFF);
                rebuilt[2 + i * 2 + 1] = (byte)((mars[i] >> 8) & 0xFF);
            }

            string csv2 = MapExportCsv.Serialize(rebuilt);
            Assert.Equal(csv1, csv2);
        }

        [Fact]
        public void Parse_NullCsv_ReturnsError()
        {
            bool ok = MapExportCsv.Parse(null, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.NotNull(err);
        }

        [Fact]
        public void Parse_EmptyCsv_ReturnsError()
        {
            bool ok = MapExportCsv.Parse("", out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.NotNull(err);
        }

        [Fact]
        public void Parse_MissingHeader_ReturnsError()
        {
            string csv = "1,2\n3,4\n";
            bool ok = MapExportCsv.Parse(csv, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.Contains("header", err, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parse_MalformedHeader_ReturnsError()
        {
            string csv = "# Not the right header format\n1,2\n3,4\n";
            bool ok = MapExportCsv.Parse(csv, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.Contains("header", err, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parse_NonNumericCell_ReturnsError()
        {
            string csv = "# FEBuilderGBA Map Export: width=2, height=2\n1,abc\n3,4\n";
            bool ok = MapExportCsv.Parse(csv, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.NotNull(err);
            Assert.Contains("abc", err);
        }

        [Fact]
        public void Parse_ValueOutOfRange_ReturnsError()
        {
            string csv = "# FEBuilderGBA Map Export: width=2, height=2\n1,99999\n3,4\n";
            bool ok = MapExportCsv.Parse(csv, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.NotNull(err);
            Assert.Contains("99999", err);
        }

        [Fact]
        public void Parse_WrongCellCount_ReturnsError()
        {
            // 2x2 but row 0 has 3 cells
            string csv = "# FEBuilderGBA Map Export: width=2, height=2\n1,2,3\n4,5\n";
            bool ok = MapExportCsv.Parse(csv, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.NotNull(err);
            Assert.Contains("row 0", err);
        }

        [Fact]
        public void Parse_TooFewRows_ReturnsError()
        {
            // 2x2 but only 1 data row
            string csv = "# FEBuilderGBA Map Export: width=2, height=2\n1,2\n";
            bool ok = MapExportCsv.Parse(csv, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.NotNull(err);
        }

        [Fact]
        public void Parse_TooManyRows_ReturnsError()
        {
            // 2x2 but 3 data rows
            string csv = "# FEBuilderGBA Map Export: width=2, height=2\n1,2\n3,4\n5,6\n";
            bool ok = MapExportCsv.Parse(csv, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.NotNull(err);
        }

        [Fact]
        public void Parse_BlankRowInsideGrid_ReturnsError()
        {
            // 1x3 grid with a blank line in the MIDDLE — must be rejected
            // (a skipped blank would otherwise shift rows up; Copilot finding #2).
            string csv = "# FEBuilderGBA Map Export: width=1, height=3\n7\n\n9\n";
            bool ok = MapExportCsv.Parse(csv, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.NotNull(err);
        }

        [Fact]
        public void Parse_DataLineLookingLikeHeader_RejectedWhenRealHeaderMissing()
        {
            // A bare 'width=2, height=2' line (no '# FEBuilderGBA Map Export:' prefix)
            // must NOT be accepted as the header (anchored header; Copilot finding #1).
            string csv = "width=2, height=2\n1,2\n3,4\n";
            bool ok = MapExportCsv.Parse(csv, out _, out _, out _, out string err);
            Assert.False(ok);
            Assert.Contains("header", err, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Parse_LeadingBom_Tolerated()
        {
            // A UTF-8 BOM prepended to the header must be tolerated and round-trip.
            string csv = "﻿# FEBuilderGBA Map Export: width=2, height=2\n1,2\n3,4\n";
            bool ok = MapExportCsv.Parse(csv, out int w, out int h, out ushort[] mars, out string err);
            Assert.True(ok, err);
            Assert.Equal(2, w);
            Assert.Equal(2, h);
            Assert.Equal(new ushort[] { 1, 2, 3, 4 }, mars);
        }
    }
}
