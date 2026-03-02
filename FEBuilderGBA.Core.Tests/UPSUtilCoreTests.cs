using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class UPSUtilCoreTests
    {
        [Fact]
        public void IsUPSData_ValidHeader()
        {
            byte[] data = new byte[] { (byte)'U', (byte)'P', (byte)'S', (byte)'1', 0 };
            Assert.True(UPSUtilCore.IsUPSData(data));
        }

        [Fact]
        public void IsUPSData_InvalidHeader()
        {
            Assert.False(UPSUtilCore.IsUPSData(new byte[] { 0, 0, 0, 0, 0 }));
        }

        [Fact]
        public void IsUPSData_Null()
        {
            Assert.False(UPSUtilCore.IsUPSData(null));
        }

        [Fact]
        public void MakeAndApply_Roundtrip()
        {
            byte[] src = new byte[256];
            byte[] dst = new byte[256];
            for (int i = 0; i < 256; i++) src[i] = (byte)i;
            System.Array.Copy(src, dst, 256);
            dst[0] = 0xFF;
            dst[100] = 0xBB;

            byte[] upsData = UPSUtilCore.MakeUPSData(src, dst);
            Assert.True(UPSUtilCore.IsUPSData(upsData));

            byte[] result = UPSUtilCore.ApplyUPS(src, upsData, out string error);
            Assert.NotNull(result);
            Assert.Equal(dst, result);
        }

        [Fact]
        public void CRC32_BasicCalc()
        {
            var crc = new UPSUtilCore.CRC32();
            uint val = crc.Calc(new byte[] { 1, 2, 3, 4 });
            Assert.NotEqual(0u, val);
        }

        [Fact]
        public void CRC32_SameInput_SameOutput()
        {
            var crc = new UPSUtilCore.CRC32();
            byte[] data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            Assert.Equal(crc.Calc(data), crc.Calc(data));
        }
    }
}
