// SPDX-License-Identifier: GPL-3.0-or-later
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BuiltInRandomMapTilesetCoreTests
    {
        [Fact]
        public void TryResolveMapTileset_ValidEntry_ResolvesAllFourBuffers()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] obj = MakeBytes(64, seed: 1);
            byte[] pal = MakeBytes(2 * 16 * 16, seed: 2); // palette resolution always raw-copies this fixed size
            byte[] cfg = MakeBytes(24, seed: 3);
            ushort[] mars = new ushort[15 * 10];
            for (int i = 0; i < mars.Length; i++) mars[i] = (ushort)((i % 4) * 4);

            uint addr = BuiltInRandomMapTestFixture.WriteMap(rom, 0, tilesetSlot: 1, obj, pal, cfg, 15, 10, mars);

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(rom, addr, out MapTilesetSnapshot snapshot, out string error);

            Assert.True(ok, error);
            Assert.Equal(15, snapshot.Width);
            Assert.Equal(10, snapshot.Height);
            Assert.Equal(obj, snapshot.ObjData);
            Assert.Equal(pal, snapshot.PaletteData);
            Assert.Equal(cfg, snapshot.ConfigData);
            Assert.False(snapshot.Fingerprint.IsEmpty);
        }

        [Fact]
        public void TryResolveMapTileset_UnresolvedPlist_Fails()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            // Entry address inside the table but never populated by WriteMap: all PLIST
            // bytes are zero, so every PLIST resolves to U.NOT_FOUND.
            uint entryAddr = 0x00600000;
            BuiltInRandomMapTestFixture.WriteU32(rom, entryAddr + 0, 0x08000001);

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(rom, entryAddr, out MapTilesetSnapshot snapshot, out string error);

            Assert.False(ok);
            Assert.Null(snapshot);
            Assert.NotEmpty(error);
        }

        [Fact]
        public void TryResolveMapTileset_NullRom_Fails()
        {
            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(null, 0, out MapTilesetSnapshot snapshot, out string error);
            Assert.False(ok);
            Assert.NotEmpty(error);
        }

        [Fact]
        public void Fingerprint_SameBytesSameVersion_AreEqual()
        {
            byte[] obj = MakeBytes(64, 1);
            byte[] pal = MakeBytes(32, 2);
            byte[] cfg = MakeBytes(24, 3);

            var a = TilesetFingerprint.Compute(8, obj, pal, cfg);
            var b = TilesetFingerprint.Compute(8, (byte[])obj.Clone(), (byte[])pal.Clone(), (byte[])cfg.Clone());

            Assert.Equal(a, b);
            Assert.True(a == b);
        }

        [Fact]
        public void Fingerprint_DifferentBytes_AreDifferent()
        {
            byte[] obj = MakeBytes(64, 1);
            byte[] pal = MakeBytes(32, 2);
            byte[] cfg = MakeBytes(24, 3);
            byte[] cfg2 = MakeBytes(24, 4);

            var a = TilesetFingerprint.Compute(8, obj, pal, cfg);
            var b = TilesetFingerprint.Compute(8, obj, pal, cfg2);

            Assert.NotEqual(a, b);
            Assert.True(a != b);
        }

        [Fact]
        public void Fingerprint_DifferentRomVersion_AreDifferent()
        {
            byte[] obj = MakeBytes(64, 1);
            byte[] pal = MakeBytes(32, 2);
            byte[] cfg = MakeBytes(24, 3);

            var a = TilesetFingerprint.Compute(7, obj, pal, cfg);
            var b = TilesetFingerprint.Compute(8, obj, pal, cfg);

            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Fingerprint_BoundaryShiftBetweenBuffers_DoesNotCollide()
        {
            // Concatenating [A,B] vs [A+1byte, B-1byte] must not collide, proving the
            // length-prefix framing actually guards buffer-boundary shifts.
            byte[] obj1 = new byte[] { 1, 2, 3 };
            byte[] pal1 = new byte[] { 4, 5 };
            byte[] obj2 = new byte[] { 1, 2, 3, 4 };
            byte[] pal2 = new byte[] { 5 };
            byte[] cfg = new byte[] { 9 };

            var a = TilesetFingerprint.Compute(8, obj1, pal1, cfg);
            var b = TilesetFingerprint.Compute(8, obj2, pal2, cfg);

            Assert.NotEqual(a, b);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(4, true)]
        [InlineData(1020 << 2, true)]  // last valid chipset index * 4
        [InlineData(0xFFFC, false)]     // chipset index 16383, out of CHIPSET_COUNT range
        public void IsMarRenderable_RespectsChipsetRangeAndConfigLength(int marValueInt, bool expectedInRangeGivenLargeConfig)
        {
            ushort marValue = (ushort)marValueInt;
            byte[] largeConfig = new byte[0x2100];
            Assert.Equal(expectedInRangeGivenLargeConfig, BuiltInRandomMapTilesetCore.IsMarRenderable(marValue, largeConfig));
        }

        [Fact]
        public void IsMarRenderable_TsaBlockOutOfRange_ReturnsFalse()
        {
            byte[] tinyConfig = new byte[4]; // too small for MAR=0's TSA block (needs 8 bytes)
            Assert.False(BuiltInRandomMapTilesetCore.IsMarRenderable(0, tinyConfig));
        }

        [Fact]
        public void IsMarRenderable_NullConfig_ReturnsFalse()
        {
            Assert.False(BuiltInRandomMapTilesetCore.IsMarRenderable(0, null));
        }

        static byte[] MakeBytes(int length, int seed)
        {
            byte[] result = new byte[length];
            var rng = new System.Random(seed);
            rng.NextBytes(result);
            return result;
        }
    }
}
