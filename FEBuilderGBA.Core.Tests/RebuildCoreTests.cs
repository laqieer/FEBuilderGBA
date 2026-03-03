using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class RebuildCoreTests
    {
        [Fact]
        public void FindPointers_DetectsGBAPointers()
        {
            byte[] rom = new byte[256];
            uint ptr = 0x08000080;
            rom[0] = (byte)(ptr & 0xFF);
            rom[1] = (byte)((ptr >> 8) & 0xFF);
            rom[2] = (byte)((ptr >> 16) & 0xFF);
            rom[3] = (byte)((ptr >> 24) & 0xFF);

            var ptrs = RebuildCore.FindPointers(rom);
            Assert.True(ptrs.ContainsKey(0));
            Assert.Equal(ptr, ptrs[0]);
        }

        [Fact]
        public void FindPointers_NullReturnsEmpty()
        {
            var ptrs = RebuildCore.FindPointers(null);
            Assert.Empty(ptrs);
        }

        [Fact]
        public void FindModifiedRegions_DetectsChanges()
        {
            byte[] vanilla = new byte[64];
            byte[] modified = new byte[64];
            // Modify bytes at offset 10-14
            for (int i = 10; i < 15; i++)
                modified[i] = 0xFF;

            var regions = RebuildCore.FindModifiedRegions(vanilla, modified);
            Assert.NotEmpty(regions);
            Assert.Contains(regions, r => r.offset == 10);
        }

        [Fact]
        public void FindModifiedRegions_IdenticalReturnsEmpty()
        {
            byte[] data = new byte[64];
            var regions = RebuildCore.FindModifiedRegions(data, (byte[])data.Clone());
            Assert.Empty(regions);
        }

        [Fact]
        public void FindModifiedRegions_NullReturnsEmpty()
        {
            var regions = RebuildCore.FindModifiedRegions(null, new byte[64]);
            Assert.Empty(regions);
        }

        [Fact]
        public void FindModifiedRegions_ExtendedDataReportsAdditional()
        {
            byte[] vanilla = new byte[32];
            byte[] modified = new byte[64];
            // Even if first 32 bytes are same, the extended part is reported
            var regions = RebuildCore.FindModifiedRegions(vanilla, modified);
            Assert.Contains(regions, r => r.offset == 32);
        }

        [Fact]
        public void FindFreeSpace_FindsFreeRegions()
        {
            byte[] rom = new byte[128];
            // Fill with data
            for (int i = 0; i < rom.Length; i++)
                rom[i] = 0xAA;
            // Create a free region at offset 32-63 (0xFF)
            for (int i = 32; i < 64; i++)
                rom[i] = 0xFF;

            var free = RebuildCore.FindFreeSpace(rom, 16);
            Assert.NotEmpty(free);
            Assert.Contains(free, f => f.offset == 32 && f.length == 32);
        }

        [Fact]
        public void FindFreeSpace_IgnoresSmallRegions()
        {
            byte[] rom = new byte[128];
            for (int i = 0; i < rom.Length; i++)
                rom[i] = 0xAA;
            // Small free region (4 bytes)
            rom[10] = 0xFF; rom[11] = 0xFF; rom[12] = 0xFF; rom[13] = 0xFF;

            var free = RebuildCore.FindFreeSpace(rom, 16);
            Assert.Empty(free);
        }

        [Fact]
        public void Rebuild_ReturnsSuccess()
        {
            byte[] vanilla = new byte[128];
            byte[] modified = new byte[128];
            modified[10] = 0xFF;

            var result = RebuildCore.Rebuild(vanilla, modified);
            Assert.True(result.Success);
            Assert.True(result.BlocksMoved > 0);
        }

        [Fact]
        public void Rebuild_NullReturnsFailure()
        {
            var result = RebuildCore.Rebuild(null, new byte[64]);
            Assert.False(result.Success);
        }
    }
}
