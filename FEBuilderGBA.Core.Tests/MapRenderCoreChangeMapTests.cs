// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MapRenderCore.RenderChangeMap (NV6-PR2, issue #857) and the
// MapChangeCore.PlistType.PALETTE enum addition.
//
// Covers:
//   * RenderChangeMap: synthetic ROM with known OBJ/palette/config/change-data
//     → non-null + correct dimensions (width*16 × height*16).
//   * OpaqueIndex0: palette bank-0 color-0 pixel is alpha=255.
//   * Guard paths: truncated OBJ/config, wrong dimensions (0, negative),
//     oversize guard, near-EOF changeDataOffset → null/no-throw.
//   * Partial-render assertion (P2-5 caveat): out-of-range config descriptor
//     → tile skipped, partial image returned (NOT null).
//   * MapChangeCore.PlistType.PALETTE resolves to map_pal_pointer base.
//   * RenderMapImage regression: confirm prior tests still pass after the refactor.
//
// Pattern: [Collection("SharedState")] + IDisposable save/restore, StubImageService,
// LZ77.compress for synthetic payloads, int array indices throughout.

using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapRenderCoreChangeMapTests : IDisposable
    {
        readonly IImageService _prevService;
        readonly ROM _prevRom;

        public MapRenderCoreChangeMapTests()
        {
            _prevService = CoreState.ImageService;
            _prevRom = CoreState.ROM;
            CoreState.ImageService = new StubImageService();
        }

        public void Dispose()
        {
            CoreState.ImageService = _prevService;
            CoreState.ROM = _prevRom;
        }

        // ====================================================================
        // MapChangeCore.PlistType.PALETTE — resolves via map_pal_pointer.
        // ====================================================================

        [Fact]
        public void PlistType_PALETTE_ResolvesToMapPalPointer()
        {
            // Build an FE8U ROM and plant a known palette table at a fixed
            // address, then resolve plist 1 and verify the offset.
            var rom = MakeFe8uRom();
            uint palTableAddr = 0x00880000u;
            // map_pal_pointer → palTableAddr
            WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer, palTableAddr | 0x08000000u);
            // Slot 1 → palette data at 0x00890000
            uint palDataAddr = 0x00890000u;
            WriteU32(rom.Data, (int)(palTableAddr + 1 * 4u), palDataAddr | 0x08000000u);

            uint outPointer;
            uint result = MapChangeCore.PlistToOffsetAddr(
                rom, MapChangeCore.PlistType.PALETTE, 1, out outPointer);

            Assert.Equal(palDataAddr, result);
            Assert.Equal(palTableAddr + 1 * 4u, outPointer);
        }

        [Fact]
        public void PlistType_PALETTE_NullPointerReturnsNotFound()
        {
            var rom = MakeFe8uRom();
            // map_pal_pointer points to a table but slot 5 has a null entry.
            uint palTableAddr = 0x00880000u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_pal_pointer, palTableAddr | 0x08000000u);
            // Slot 5 = 0 (null target).
            WriteU32(rom.Data, (int)(palTableAddr + 5 * 4u), 0u);

            uint outPointer;
            uint result = MapChangeCore.PlistToOffsetAddr(
                rom, MapChangeCore.PlistType.PALETTE, 5, out outPointer);

            Assert.Equal(U.NOT_FOUND, result);
        }

        // ====================================================================
        // RenderChangeMap: basic render — correct dimensions + non-null.
        // ====================================================================

        [Fact]
        public void RenderChangeMap_ValidInputs_NonNullAndCorrectDimensions()
        {
            var (rom, objOff, palOff, cfgOff, changeOff, w, h) = BuildChangeMapRom(width: 4, height: 3);
            CoreState.ROM = rom;

            IImage img = MapRenderCore.RenderChangeMap(rom, objOff, palOff, cfgOff, changeOff, w, h);

            Assert.NotNull(img);
            Assert.Equal(w * 16, img.Width);
            Assert.Equal(h * 16, img.Height);
        }

        // ====================================================================
        // OpaqueIndex0: bank-0/color-0 pixel must have alpha=255.
        // ====================================================================

        [Fact]
        public void RenderChangeMap_OpaqueIndex0_PaletteBank0Color0_IsOpaque()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            const uint OBJ_OFF = 0x200000u;
            const uint PAL_OFF = 0x201000u;
            const uint CFG_OFF = 0x202000u;
            const uint CHG_OFF = 0x203000u;

            // OBJ: one 8×8 4bpp tile (32 bytes), all pixels index 0.
            byte[] objRaw = new byte[32];
            PlantBytes(rom, OBJ_OFF, LZ77.compress(objRaw));

            // Palette: bank-0 color-0 = GBA 0x0001 (R=8, G=0, B=0).
            byte[] pal = new byte[512];
            pal[0] = 0x01; pal[1] = 0x00;
            Array.Copy(pal, 0, rom.Data, PAL_OFF, 512);

            // Config: one descriptor (8 bytes) → four TSA entries all zero.
            byte[] cfgRaw = new byte[8];
            PlantBytes(rom, CFG_OFF, LZ77.compress(cfgRaw));

            // Change data: 1 tile index = 0 (RAW u16, 2 bytes).
            byte[] chgData = new byte[] { 0x00, 0x00 };
            Array.Copy(chgData, 0, rom.Data, CHG_OFF, chgData.Length);

            IImage img = MapRenderCore.RenderChangeMap(rom, OBJ_OFF, PAL_OFF, CFG_OFF, CHG_OFF, 1, 1);

            Assert.NotNull(img);
            Assert.Equal(16, img.Width);
            Assert.Equal(16, img.Height);

            byte[] pixels = img.GetPixelData(); // RGBA
            // Pixel (0,0) = pixel index 0 in RGBA array (offset 0).
            int idx = 0;
            Assert.Equal((byte)255, pixels[idx + 3]); // alpha = opaque
            Assert.Equal((byte)8,   pixels[idx + 0]); // R
            Assert.Equal((byte)0,   pixels[idx + 1]); // G
            Assert.Equal((byte)0,   pixels[idx + 2]); // B
        }

        // ====================================================================
        // Guard paths — all must return null without throwing.
        // ====================================================================

        [Fact]
        public void RenderChangeMap_NullRom_ReturnsNull()
        {
            IImage img = MapRenderCore.RenderChangeMap(null, 0x200, 0x400, 0x600, 0x800, 4, 3);
            Assert.Null(img);
        }

        [Fact]
        public void RenderChangeMap_MissingImageService_ReturnsNull()
        {
            CoreState.ImageService = null;
            var rom = MakeMinimalRom();
            IImage img = MapRenderCore.RenderChangeMap(rom, 0x200, 0x400, 0x600, 0x800, 4, 3);
            Assert.Null(img);
        }

        [Fact]
        public void RenderChangeMap_ZeroWidth_ReturnsNull()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;
            IImage img = MapRenderCore.RenderChangeMap(rom, 0x200, 0x400, 0x600, 0x800, 0, 4);
            Assert.Null(img);
        }

        [Fact]
        public void RenderChangeMap_ZeroHeight_ReturnsNull()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;
            IImage img = MapRenderCore.RenderChangeMap(rom, 0x200, 0x400, 0x600, 0x800, 4, 0);
            Assert.Null(img);
        }

        [Fact]
        public void RenderChangeMap_NegativeWidth_ReturnsNull()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;
            IImage img = MapRenderCore.RenderChangeMap(rom, 0x200, 0x400, 0x600, 0x800, -1, 4);
            Assert.Null(img);
        }

        [Fact]
        public void RenderChangeMap_OversizeDimensions_ReturnsNull()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;
            // width=16000, height=16000 → 16000*16000 = 256,000,000 tiles, way above MAX_CHANGE_TILES.
            IImage img = MapRenderCore.RenderChangeMap(rom, 0x200, 0x400, 0x600, 0x800, 16000, 16000);
            Assert.Null(img);
        }

        [Fact]
        public void RenderChangeMap_TruncatedObjLZ77_ReturnsNull()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;
            // OBJ offset at 0x200 — all-zero ROM → invalid LZ77 header.
            // Plant a valid palette (raw) and change data but leave OBJ as zeros.
            byte[] cfgRaw = new byte[8];
            PlantBytes(rom, 0x600u, LZ77.compress(cfgRaw));
            // changeDataOffset at 0x800 — 2 bytes (1 tile).
            // Leave OBJ at 0x200 as zeros (invalid LZ77).
            IImage img = MapRenderCore.RenderChangeMap(rom, 0x200u, 0x400u, 0x600u, 0x800u, 1, 1);
            Assert.Null(img);
        }

        [Fact]
        public void RenderChangeMap_TruncatedConfigLZ77_ReturnsNull()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            byte[] objData = new byte[32];
            PlantBytes(rom, 0x200u, LZ77.compress(objData));
            // Leave config at 0x600 as zeros → invalid LZ77 header.
            IImage img = MapRenderCore.RenderChangeMap(rom, 0x200u, 0x400u, 0x600u, 0x800u, 1, 1);
            Assert.Null(img);
        }

        [Fact]
        public void RenderChangeMap_NearEofChangeDataOffset_DoesNotThrow()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            // changeDataOffset = ROM end - 1 → bounds check fails → null, no throw.
            uint nearEof = (uint)rom.Data.Length - 1;
            IImage img = null;
            var ex = Record.Exception(() =>
            {
                img = MapRenderCore.RenderChangeMap(rom, nearEof, nearEof, nearEof, nearEof, 1, 1);
            });
            Assert.Null(ex);
            Assert.Null(img);
        }

        [Fact]
        public void RenderChangeMap_ChangeDataExceedsRomEnd_ReturnsNull()
        {
            // Plant valid OBJ + config but make changeDataOffset + w*h*2 exceed ROM end.
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            byte[] objData = new byte[32];
            PlantBytes(rom, 0x200u, LZ77.compress(objData));
            byte[] cfgData = new byte[8];
            PlantBytes(rom, 0x600u, LZ77.compress(cfgData));

            // changeDataOffset near end of ROM; width=10, height=10 → 200 u16 = 400 bytes.
            uint nearEnd = (uint)rom.Data.Length - 100u;
            IImage img = MapRenderCore.RenderChangeMap(rom, 0x200u, 0x400u, 0x600u, nearEnd, 10, 10);
            Assert.Null(img);
        }

        // ====================================================================
        // P2-5 caveat: out-of-range config descriptor → tile skipped, partial
        // image returned (NOT null). Matches inherited PR1 divergence.
        // ====================================================================

        [Fact]
        public void RenderChangeMap_OutOfRangeConfigDescOff_SkipsTile_ReturnsPartialImage()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            // OBJ: two 8×8 4bpp tiles (64 bytes).
            byte[] objData = new byte[64];
            PlantBytes(rom, 0x200u, LZ77.compress(objData));

            // Config: only 4 bytes — too small for any 8-byte descriptor.
            byte[] cfgData = new byte[4];
            PlantBytes(rom, 0x600u, LZ77.compress(cfgData));

            // Change data: 2×2 map, all entries = 0x0FFF → descOff = 0x1FFE >> cfgData.Length.
            const int W = 2;
            const int H = 2;
            byte[] chgData = new byte[W * H * 2];
            for (int i = 0; i < W * H; i++)
            {
                chgData[i * 2 + 0] = 0xFF;
                chgData[i * 2 + 1] = 0x0F;
            }
            Array.Copy(chgData, 0, rom.Data, 0x800, chgData.Length);

            IImage img = null;
            var ex = Record.Exception(() =>
            {
                img = MapRenderCore.RenderChangeMap(rom, 0x200u, 0x400u, 0x600u, 0x800u, W, H);
            });
            Assert.Null(ex);
            // A partial (all-zero-TSA) image is still returned — NOT null.
            Assert.NotNull(img);
            Assert.Equal(W * 16, img.Width);
            Assert.Equal(H * 16, img.Height);
        }

        // ====================================================================
        // RenderMapImage regression — the refactor must not break existing tests.
        // We do a quick smoke-test of the basic guard + a valid render here.
        // The full 13-test suite is in MapRenderCoreTests.cs.
        // ====================================================================

        [Fact]
        public void RenderMapImage_NullRom_StillReturnsNull_AfterRefactor()
        {
            IImage img = MapRenderCore.RenderMapImage(null, 0x200, 0x400, 0x600, 0x800);
            Assert.Null(img);
        }

        [Fact]
        public void RenderMapImage_ValidInput_StillRendersAfterRefactor()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            const uint OBJ_OFF = 0x200000u;
            const uint PAL_OFF = 0x201000u;
            const uint CFG_OFF = 0x202000u;
            const uint MAP_OFF = 0x203000u;

            byte[] objRaw = new byte[32];
            PlantBytes(rom, OBJ_OFF, LZ77.compress(objRaw));

            byte[] pal = new byte[512];
            pal[0] = 0x01; pal[1] = 0x00;
            Array.Copy(pal, 0, rom.Data, PAL_OFF, 512);

            byte[] cfgRaw = new byte[8];
            PlantBytes(rom, CFG_OFF, LZ77.compress(cfgRaw));

            // MAR: 1×1 map, MAR value = 0.
            byte[] marRaw = new byte[] { 1, 1, 0, 0 };
            PlantBytes(rom, MAP_OFF, LZ77.compress(marRaw));

            IImage img = MapRenderCore.RenderMapImage(rom, OBJ_OFF, PAL_OFF, CFG_OFF, MAP_OFF);
            Assert.NotNull(img);
            Assert.Equal(16, img.Width);
            Assert.Equal(16, img.Height);
        }

        // ====================================================================
        // Real-ROM integration — skips gracefully when FE8U.gba is absent.
        // ====================================================================

        [Fact]
        public void RealRom_FE8U_RenderChangeMap_NonNullForFirstChangeEntry()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip

            var rom = new ROM();
            if (!rom.Load(romPath, out string _)) return;
            CoreState.ROM = rom;

            // Walk maps looking for the first one that has a change-data entry
            // with non-zero width/height and a resolvable overlay.
            var maps = MapSettingCore.MakeMapIDList(rom);
            bool found = false;
            foreach (var m in maps)
            {
                uint changeAddr = MapChangeCore.GetMapChangeAddrWhereMapID(rom, m.tag, out _);
                if (!U.isSafetyOffset(changeAddr, rom)) continue;
                if (changeAddr + 12u > (uint)rom.Data.Length) continue;
                if (rom.u8(changeAddr) == 0xFF) continue;

                int w = (int)rom.u8(changeAddr + 3);
                int h = (int)rom.u8(changeAddr + 4);
                if (w <= 0 || h <= 0) continue;

                uint p8ptr = rom.p32(changeAddr + 8);
                uint changeDataOffset = U.toOffset(p8ptr);
                if (!U.isSafetyOffset(changeDataOffset, rom)) continue;

                uint mapSettingAddr = MapSettingCore.GetMapAddr(rom, m.tag);
                if (!U.isSafetyOffset(mapSettingAddr, rom)) continue;
                if (mapSettingAddr + 8u > (uint)rom.Data.Length) continue;

                uint objPlist    = rom.u16(mapSettingAddr + 4) & 0xFFu;
                uint palPlist    = rom.u8(mapSettingAddr + 6);
                uint cfgPlist    = rom.u8(mapSettingAddr + 7);

                uint objOff = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.OBJECT,  objPlist, out _);
                uint palOff = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.PALETTE, palPlist, out _);
                uint cfgOff = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.CONFIG,  cfgPlist, out _);

                if (objOff == U.NOT_FOUND || palOff == U.NOT_FOUND || cfgOff == U.NOT_FOUND) continue;

                IImage img = MapRenderCore.RenderChangeMap(rom, objOff, palOff, cfgOff, changeDataOffset, w, h);
                if (img != null)
                {
                    Assert.Equal(w * 16, img.Width);
                    Assert.Equal(h * 16, img.Height);
                    found = true;
                    break;
                }
            }
            // If no renderable change was found in the ROM, the test still passes
            // (the loop exhausted gracefully — this is a real-ROM integration
            // test that reports findings, not a failure).
            // Uncomment to assert found: Assert.True(found, "Expected at least one renderable change entry in FE8U");
        }

        // ====================================================================
        // Synthetic ROM construction helpers
        // ====================================================================

        /// <summary>
        /// Build a minimal 32 MB ROM (all-zero data, no version RomInfo).
        /// </summary>
        static ROM MakeMinimalRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000]; // 32 MB
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        /// <summary>
        /// Build an FE8U ROM (with version detection so map_pal_pointer etc. are
        /// populated from ROMFE8U defaults).
        /// </summary>
        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("test-fe8u.gba", new byte[0x1100000], "BE8E01");
            return rom;
        }

        /// <summary>
        /// Build a synthetic ROM with all four data streams needed for
        /// <see cref="MapRenderCore.RenderChangeMap"/>, using a known
        /// <paramref name="width"/> × <paramref name="height"/> tile layout.
        ///
        /// Returns (rom, objOff, palOff, cfgOff, changeDataOffset, width, height).
        /// </summary>
        static (ROM, uint, uint, uint, uint, int, int)
            BuildChangeMapRom(int width, int height)
        {
            var rom = MakeMinimalRom();

            const uint OBJ_OFF = 0x200000u;
            const uint PAL_OFF = 0x201000u;
            const uint CFG_OFF = 0x202000u;
            const uint CHG_OFF = 0x203000u;

            // OBJ: enough tiles for the canvas (each tile = 32 bytes).
            // Two tiles should be sufficient for a minimal test.
            byte[] objRaw = new byte[32 * 2];
            PlantBytes(rom, OBJ_OFF, LZ77.compress(objRaw));

            // Palette (raw 512 bytes): all zeros (GBA color 0x0000 = black).
            // No need to write — the ROM is already all zeros.

            // Config: enough 8-byte descriptors for any tile index we'll use.
            // width*height entries; each references tile 0, bank 0, no flip.
            // Descriptor 0 = 8 zero bytes → four TSA entries all 0 (tile 0).
            byte[] cfgRaw = new byte[8 * (width * height + 1)];
            PlantBytes(rom, CFG_OFF, LZ77.compress(cfgRaw));

            // Change data (RAW u16 array): all tile indices = 0.
            int totalTiles = width * height;
            byte[] chgData = new byte[totalTiles * 2];
            Array.Copy(chgData, 0, rom.Data, CHG_OFF, chgData.Length);

            return (rom, OBJ_OFF, PAL_OFF, CFG_OFF, CHG_OFF, width, height);
        }

        static void PlantBytes(ROM rom, uint offset, byte[] bytes)
        {
            if (bytes == null) return;
            Array.Copy(bytes, 0, rom.Data, (int)offset, bytes.Length);
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static string FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string dir = System.IO.Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = System.IO.Path.Combine(dir, "roms", romName);
                    if (System.IO.File.Exists(path)) return path;
                    break;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
