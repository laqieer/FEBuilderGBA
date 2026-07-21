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
        public void TryResolveMapTileset_PlistBasePointerSlotStraddlesEof_FailsWithoutThrowing()
        {
            ROM template = BuiltInRandomMapTestFixture.CreateRom();
            uint pointerSlot = template.RomInfo.map_obj_pointer;
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[checked((int)pointerSlot + 3)], "BE8E01");
            BuiltInRandomMapTestFixture.WriteU16(rom, 4, 1);
            rom.Data[6] = 1;
            rom.Data[7] = 1;
            rom.Data[8] = 1;

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(
                rom,
                0,
                out MapTilesetSnapshot snapshot,
                out string error);

            Assert.False(ok);
            Assert.Null(snapshot);
            Assert.NotEmpty(error);
        }

        [Fact]
        public void TryResolveMapTileset_TruncatedFixedPaletteSnapshot_Fails()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] obj = MakeBytes(64, seed: 1);
            byte[] pal = MakeBytes(2 * 16 * 16, seed: 2);
            byte[] cfg = MakeBytes(24, seed: 3);
            ushort[] mars = new ushort[15 * 10];
            uint addr = BuiltInRandomMapTestFixture.WriteMap(
                rom, 0, tilesetSlot: 1, obj, pal, cfg, 15, 10, mars);

            uint paletteTable = rom.p32(rom.RomInfo.map_pal_pointer);
            uint truncatedPaletteAddr = (uint)rom.Data.Length - 511;
            BuiltInRandomMapTestFixture.WriteU32(
                rom,
                paletteTable + 4,
                0x08000000u + truncatedPaletteAddr);

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(
                rom,
                addr,
                out MapTilesetSnapshot snapshot,
                out string error);

            Assert.False(ok);
            Assert.Null(snapshot);
            Assert.Contains("512-byte", error);
        }

        [Fact]
        public void TryResolveMapTileset_ValidSecondaryObj_AppendsBytes()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] primaryObj = MakeBytes(64, seed: 1);
            byte[] secondaryObj = MakeBytes(32, seed: 2);
            byte[] pal = MakeBytes(2 * 16 * 16, seed: 3);
            byte[] cfg = MakeBytes(24, seed: 4);
            ushort[] mars = new ushort[15 * 10];
            uint addr = BuiltInRandomMapTestFixture.WriteMap(
                rom,
                mapIndex: 0,
                tilesetSlot: 1,
                primaryObj,
                pal,
                cfg,
                15,
                10,
                mars,
                secondaryObjSlot: 2,
                secondaryObjRaw: secondaryObj);

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(
                rom,
                addr,
                out MapTilesetSnapshot snapshot,
                out string error);

            Assert.True(ok, error);
            byte[] expected = new byte[primaryObj.Length + secondaryObj.Length];
            System.Array.Copy(primaryObj, 0, expected, 0, primaryObj.Length);
            System.Array.Copy(secondaryObj, 0, expected, primaryObj.Length, secondaryObj.Length);
            Assert.Equal(expected, snapshot.ObjData);
        }

        [Fact]
        public void TryResolveMapTileset_NonzeroUnresolvedSecondaryObj_Fails()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] obj = MakeBytes(64, seed: 1);
            byte[] pal = MakeBytes(2 * 16 * 16, seed: 2);
            byte[] cfg = MakeBytes(24, seed: 3);
            ushort[] mars = new ushort[15 * 10];
            uint addr = BuiltInRandomMapTestFixture.WriteMap(
                rom, 0, tilesetSlot: 1, obj, pal, cfg, 15, 10, mars);
            rom.Data[addr + 5] = 2;

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(
                rom,
                addr,
                out MapTilesetSnapshot snapshot,
                out string error);

            Assert.False(ok);
            Assert.Null(snapshot);
            Assert.Contains("Secondary OBJ PLIST 2", error);
        }

        [Fact]
        public void TryResolveMapTileset_TruncatedPrimaryObjStream_Fails()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] obj = MakeBytes(64, seed: 1);
            byte[] pal = MakeBytes(2 * 16 * 16, seed: 2);
            byte[] cfg = MakeBytes(24, seed: 3);
            ushort[] mars = new ushort[15 * 10];
            uint addr = BuiltInRandomMapTestFixture.WriteMap(
                rom, 0, tilesetSlot: 1, obj, pal, cfg, 15, 10, mars);
            RepointToTruncatedCompressedStreamAtEof(
                rom,
                rom.RomInfo.map_obj_pointer,
                plist: 1,
                obj);

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(
                rom,
                addr,
                out MapTilesetSnapshot snapshot,
                out string error);

            Assert.False(ok);
            Assert.Null(snapshot);
            Assert.Contains("Primary OBJ", error);
            Assert.Contains("complete valid LZ77", error);
        }

        [Fact]
        public void TryResolveMapTileset_TruncatedSecondaryObjStream_Fails()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] primaryObj = MakeBytes(64, seed: 1);
            byte[] secondaryObj = MakeBytes(32, seed: 2);
            byte[] pal = MakeBytes(2 * 16 * 16, seed: 3);
            byte[] cfg = MakeBytes(24, seed: 4);
            ushort[] mars = new ushort[15 * 10];
            uint addr = BuiltInRandomMapTestFixture.WriteMap(
                rom,
                mapIndex: 0,
                tilesetSlot: 1,
                primaryObj,
                pal,
                cfg,
                15,
                10,
                mars,
                secondaryObjSlot: 2,
                secondaryObjRaw: secondaryObj);
            RepointToTruncatedCompressedStreamAtEof(
                rom,
                rom.RomInfo.map_obj_pointer,
                plist: 2,
                secondaryObj);

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(
                rom,
                addr,
                out MapTilesetSnapshot snapshot,
                out string error);

            Assert.False(ok);
            Assert.Null(snapshot);
            Assert.Contains("Secondary OBJ", error);
            Assert.Contains("complete valid LZ77", error);
        }

        [Fact]
        public void TryResolveMapTileset_TruncatedConfigStream_Fails()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] obj = MakeBytes(64, seed: 1);
            byte[] pal = MakeBytes(2 * 16 * 16, seed: 2);
            byte[] cfg = MakeBytes(24, seed: 3);
            ushort[] mars = new ushort[15 * 10];
            uint addr = BuiltInRandomMapTestFixture.WriteMap(
                rom, 0, tilesetSlot: 1, obj, pal, cfg, 15, 10, mars);
            RepointToTruncatedCompressedStreamAtEof(
                rom,
                rom.RomInfo.map_config_pointer,
                plist: 1,
                cfg);

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(
                rom,
                addr,
                out MapTilesetSnapshot snapshot,
                out string error);

            Assert.False(ok);
            Assert.Null(snapshot);
            Assert.Contains("Config", error);
            Assert.Contains("complete valid LZ77", error);
        }

        [Fact]
        public void TryResolveMapTileset_TruncatedMapStream_Fails()
        {
            ROM rom = BuiltInRandomMapTestFixture.CreateRom();
            byte[] obj = MakeBytes(64, seed: 1);
            byte[] pal = MakeBytes(2 * 16 * 16, seed: 2);
            byte[] cfg = MakeBytes(24, seed: 3);
            ushort[] mars = new ushort[15 * 10];
            uint addr = BuiltInRandomMapTestFixture.WriteMap(
                rom, 0, tilesetSlot: 1, obj, pal, cfg, 15, 10, mars);
            byte[] mapData = BuiltInRandomMapTestFixture.BuildMapBuffer(15, 10, mars);
            RepointToTruncatedCompressedStreamAtEof(
                rom,
                rom.RomInfo.map_map_pointer_pointer,
                plist: 1,
                mapData);

            bool ok = BuiltInRandomMapTilesetCore.TryResolveMapTileset(
                rom,
                addr,
                out MapTilesetSnapshot snapshot,
                out string error);

            Assert.False(ok);
            Assert.Null(snapshot);
            Assert.Contains("MAP", error);
            Assert.Contains("complete valid LZ77", error);
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
        [InlineData(1, false)]           // non-canonical: low MAR bits must be zero
        [InlineData(2, false)]
        [InlineData(3, false)]
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

        static void RepointToTruncatedCompressedStreamAtEof(
            ROM rom,
            uint tablePointerAddress,
            int plist,
            byte[] rawData)
        {
            byte[] compressed = LZ77.compress(rawData);
            uint completeLength = LZ77.getCompressedSize(compressed, 0);
            Assert.True(completeLength > 1);
            int truncatedLength = checked((int)completeLength - 1);
            uint truncatedOffset = checked((uint)(rom.Data.Length - truncatedLength));
            System.Array.Copy(compressed, 0, rom.Data, truncatedOffset, truncatedLength);

            uint tableOffset = rom.p32(tablePointerAddress);
            BuiltInRandomMapTestFixture.WriteU32(
                rom,
                tableOffset + checked((uint)plist * 4),
                0x08000000u + truncatedOffset);
            Assert.Equal(0u, LZ77.getCompressedSize(rom.Data, truncatedOffset));
        }
    }
}
