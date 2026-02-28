using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    public class CoreStateTests
    {
        [Fact]
        public void CoreState_DefaultServices_IsHeadless()
        {
            // CoreState.Services defaults to HeadlessAppServices
            Assert.NotNull(CoreState.Services);
            Assert.IsType<HeadlessAppServices>(CoreState.Services);
        }

        [Fact]
        public void CoreState_ROM_IsNullByDefault()
        {
            // Before initialization, ROM should be null
            var savedRom = CoreState.ROM;
            CoreState.ROM = null;
            Assert.Null(CoreState.ROM);
            CoreState.ROM = savedRom;
        }

        [Fact]
        public void CoreInterfaces_IEtcCache_CanBeImplemented()
        {
            // Verify the interface is usable
            IEtcCache cache = new TestEtcCache();
            cache.RemoveOverRange(100);
            cache.RemoveRange(0, 100);
        }

        [Fact]
        public void CoreInterfaces_ISystemTextEncoder_CanBeImplemented()
        {
            ISystemTextEncoder encoder = new TestTextEncoder();
            string result = encoder.Decode(new byte[] { 0x41, 0x42, 0x43 }, 0, 3);
            Assert.Equal("ABC", result);
        }

        [Fact]
        public void CoreInterfaces_IAsmMapCache_CanBeImplemented()
        {
            IAsmMapCache cache = new TestAsmMapCache();
            cache.ClearCache(); // Should not throw
        }

        [Fact]
        public void ROM_LoadLow_DetectsVersionFromSignature()
        {
            // Create a minimal ROM-like byte array with FE8U signature
            byte[] data = new byte[0x1000000]; // 16MB
            // Write "BE8E01" at offset 0xAC (the version signature location)
            byte[] sig = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            System.Array.Copy(sig, 0, data, 0xAC, sig.Length);

            ROM rom = new ROM();
            bool success = rom.LoadLow("test.gba", data, "BE8E01");

            Assert.True(success);
            Assert.NotNull(rom.RomInfo);
            Assert.Equal("FE8U", rom.RomInfo.VersionToFilename);
        }

        [Fact]
        public void ROM_BasicReadWrite()
        {
            byte[] data = new byte[256];
            data[0] = 0x42;
            data[4] = 0x78;
            data[5] = 0x56;
            data[6] = 0x34;
            data[7] = 0x12;

            ROM rom = new ROM();
            rom.LoadLow("test.gba", data, "NAZO");

            Assert.Equal((uint)0x42, rom.u8(0));
            Assert.Equal((uint)0x12345678, rom.u32(4));

            rom.write_u8(0, 0xFF);
            Assert.Equal((uint)0xFF, rom.u8(0));
            Assert.True(rom.Modified);
        }

        [Fact]
        public void Undo_UndoPostion_CreatesWithByteArray()
        {
            var pos = new Undo.UndoPostion(0x10, new byte[] { 1, 2, 3 });
            Assert.Equal((uint)0x10, pos.addr);
            Assert.Equal(new byte[] { 1, 2, 3 }, pos.data);
        }

        [Fact]
        public void HeadlessAppServices_ShowError_WritesToStderr()
        {
            var services = new HeadlessAppServices();
            // Should not throw
            services.ShowError("test error");
            services.ShowInfo("test info");
            Assert.False(services.ShowQuestion("test question"));
            Assert.False(services.ShowYesNo("test yesno"));
            Assert.True(services.IsMainThread());
        }

        // --- Test implementations ---
        private class TestEtcCache : IEtcCache
        {
            public void RemoveOverRange(uint range) { }
            public void RemoveRange(uint start, uint end) { }
        }

        private class TestTextEncoder : ISystemTextEncoder
        {
            public string Decode(byte[] str, int start, int len)
            {
                return System.Text.Encoding.ASCII.GetString(str, start, len);
            }
        }

        private class TestAsmMapCache : IAsmMapCache
        {
            public void ClearCache() { }
        }
    }
}
