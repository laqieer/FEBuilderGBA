using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MapExportCsv.Serialize"/> (#658 slice B).
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
    }
}
