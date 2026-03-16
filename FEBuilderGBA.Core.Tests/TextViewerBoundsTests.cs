using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for ROM pointer bounds checking behavior used by TextViewerViewModel.
    /// Verifies that ROM reads on truncated/small buffers return safe defaults
    /// instead of throwing exceptions.
    /// </summary>
    [Collection("SharedState")]
    public class TextViewerBoundsTests
    {
        [Fact]
        public void P32_ReturnsZero_WhenAddressExceedsDataLength()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[16]);
            // Address 20 is beyond 16-byte buffer
            uint result = rom.p32(20);
            Assert.Equal(0u, result);
        }

        [Fact]
        public void P32_ReturnsZero_WhenAddressAtBoundary()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[16]);
            // Address 16 is exactly at the end
            uint result = rom.p32(16);
            Assert.Equal(0u, result);
        }

        [Fact]
        public void IsSafetyOffset_ReturnsFalse_ForAddressBeyondData()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x400]);
            // Address beyond data length
            Assert.False(U.isSafetyOffset(0x500, rom));
        }

        [Fact]
        public void IsSafetyOffset_ReturnsFalse_ForAddressBelowMinimum()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x400]);
            // Address below 0x200 minimum
            Assert.False(U.isSafetyOffset(0x100, rom));
        }

        [Fact]
        public void IsSafetyOffset_ReturnsTrue_ForValidAddress()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x400]);
            Assert.True(U.isSafetyOffset(0x200, rom));
            Assert.True(U.isSafetyOffset(0x3FF, rom));
        }

        [Fact]
        public void BoundsCheck_Pattern_PreventsOutOfRange()
        {
            // Simulate the bounds-check pattern used in TextViewerViewModel:
            // if (addr + 4 > rom.Data.Length) return;
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[8]);
            uint addr = 6; // 6 + 4 = 10 > 8 -- should fail
            bool safe = (addr + 4 <= (uint)rom.Data.Length);
            Assert.False(safe);
        }

        [Fact]
        public void BoundsCheck_Pattern_AllowsValidRead()
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[8]);
            uint addr = 4; // 4 + 4 = 8 <= 8 -- should pass
            bool safe = (addr + 4 <= (uint)rom.Data.Length);
            Assert.True(safe);
        }

        [Fact]
        public void U32_DoesNotThrow_WhenReadingWithinBounds()
        {
            var rom = new ROM();
            var data = new byte[16];
            data[4] = 0x12;
            data[5] = 0x34;
            data[6] = 0x56;
            data[7] = 0x78;
            rom.SwapNewROMDataDirect(data);
            uint val = rom.u32(4);
            Assert.Equal(0x78563412u, val);
        }
    }
}
