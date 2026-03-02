using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
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
            byte[] data = new byte[] { 0, 0, 0, 0, 0 };
            Assert.False(UPSUtilCore.IsUPSData(data));
        }

        [Fact]
        public void IsUPSData_TooShort()
        {
            byte[] data = new byte[] { (byte)'U', (byte)'P', (byte)'S' };
            Assert.False(UPSUtilCore.IsUPSData(data));
        }

        [Fact]
        public void IsUPSData_Null()
        {
            Assert.False(UPSUtilCore.IsUPSData(null));
        }

        [Fact]
        public void MakeUPSData_IdenticalData_ProducesValidUPS()
        {
            byte[] src = new byte[] { 1, 2, 3, 4 };
            byte[] dst = new byte[] { 1, 2, 3, 4 };
            byte[] ups = UPSUtilCore.MakeUPSData(src, dst);

            Assert.True(UPSUtilCore.IsUPSData(ups));
            Assert.True(ups.Length >= 16); // header + sizes + CRCs
        }

        [Fact]
        public void MakeUPSData_DifferentData_ProducesValidUPS()
        {
            byte[] src = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            byte[] dst = new byte[] { 1, 2, 0xFF, 4, 5, 0xAA, 7, 8 };
            byte[] ups = UPSUtilCore.MakeUPSData(src, dst);

            Assert.True(UPSUtilCore.IsUPSData(ups));
        }

        [Fact]
        public void Roundtrip_MakeAndApply()
        {
            byte[] src = new byte[256];
            byte[] dst = new byte[256];
            // Set up source
            for (int i = 0; i < 256; i++)
                src[i] = (byte)i;
            // Create modified destination
            Array.Copy(src, dst, 256);
            dst[0] = 0xFF;
            dst[10] = 0xAA;
            dst[100] = 0xBB;
            dst[255] = 0xCC;

            // Create patch
            byte[] upsData = UPSUtilCore.MakeUPSData(src, dst);
            Assert.True(UPSUtilCore.IsUPSData(upsData));

            // Apply patch
            byte[] result = UPSUtilCore.ApplyUPS(src, upsData, out string error);
            Assert.NotNull(result);
            Assert.Equal(dst.Length, result.Length);
            Assert.Equal(dst, result);
        }

        [Fact]
        public void Roundtrip_EmptyToData()
        {
            byte[] src = new byte[64];
            byte[] dst = new byte[64];
            for (int i = 0; i < 64; i++)
                dst[i] = (byte)(i * 2);

            byte[] upsData = UPSUtilCore.MakeUPSData(src, dst);
            byte[] result = UPSUtilCore.ApplyUPS(src, upsData, out string error);
            Assert.NotNull(result);
            Assert.Equal(dst, result);
        }

        [Fact]
        public void ApplyUPS_InvalidData_ReturnsNull()
        {
            byte[] src = new byte[64];
            byte[] badPatch = new byte[] { 0, 0, 0 };
            byte[] result = UPSUtilCore.ApplyUPS(src, badPatch, out string error);
            Assert.Null(result);
            Assert.NotNull(error);
        }

        [Fact]
        public void ApplyUPS_WrongSource_ReturnsNull()
        {
            byte[] src = new byte[64];
            byte[] dst = new byte[64];
            dst[0] = 0xFF;
            byte[] upsData = UPSUtilCore.MakeUPSData(src, dst);

            // Try applying to different source
            byte[] wrongSrc = new byte[64];
            wrongSrc[0] = 0x01;
            byte[] result = UPSUtilCore.ApplyUPS(wrongSrc, upsData, out string error);
            Assert.Null(result);
            Assert.Contains("CRC mismatch", error);
        }

        [Fact]
        public void Roundtrip_DifferentSizes()
        {
            byte[] src = new byte[100];
            byte[] dst = new byte[200];
            for (int i = 0; i < 100; i++)
                src[i] = (byte)i;
            for (int i = 0; i < 200; i++)
                dst[i] = (byte)(i + 1);

            byte[] upsData = UPSUtilCore.MakeUPSData(src, dst);
            byte[] result = UPSUtilCore.ApplyUPS(src, upsData, out string error);
            Assert.NotNull(result);
            Assert.Equal(dst.Length, result.Length);
            Assert.Equal(dst, result);
        }
    }
}
