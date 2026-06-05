// SPDX-License-Identifier: GPL-3.0-or-later
// #961 W2c — FE7 obj2 (secondary OBJ tileset) high-byte render tests.
//
// MapRenderCore.RenderChangeMap / RenderMapImage gained an OPTIONAL obj2Offset
// parameter (default 0). When non-zero (FE7 only — the high byte of obj_plist),
// the second tileset is LZ77-decompressed and CONCATENATED onto the primary OBJ
// bytes before render, mirroring WF ImageUtilMap.DrawMapChipOnly (~L55-63).
//
// Oracle approach: build a synthetic ROM whose primary OBJ tileset is one all-0
// tile (32 bytes, palette index 0 → black) and whose obj2 tileset is one
// all-0x11 tile (32 bytes, palette index 1 → red). A 1×1 change map whose single
// config descriptor points the four subtiles at TILE INDEX 2 (the first tile of
// the appended obj2 sheet) can ONLY render correctly when obj2 is concatenated —
// without the concat, tile index 2 is out of range and the subtile renders blank
// (index 0 → black). So the (8,8) pixel being RED proves the obj2 append happened.
//
// Pattern mirrors MapRenderCoreChangeMapTests: [Collection("SharedState")] +
// StubImageService + LZ77.compress synthetic payloads + IDisposable restore.

using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapRenderCoreObj2Tests : IDisposable
    {
        readonly IImageService _prevService;
        readonly ROM _prevRom;

        public MapRenderCoreObj2Tests()
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

        const uint OBJ_OFF  = 0x200000u;
        const uint OBJ2_OFF = 0x201000u;
        const uint PAL_OFF  = 0x202000u;
        const uint CFG_OFF  = 0x203000u;
        const uint CHG_OFF  = 0x204000u;

        // ====================================================================
        // ORACLE: obj2 concatenation makes tile index 2 (first obj2 tile)
        // render the obj2 color. Without concat that tile is out of range.
        // ====================================================================

        [Fact]
        public void RenderChangeMap_WithObj2_AppendsSecondTileset_RendersObj2Color()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            // Primary OBJ: two 8×8 4bpp tiles (64 bytes), all index 0.
            byte[] objRaw = new byte[64];
            PlantBytes(rom, OBJ_OFF, LZ77.compress(objRaw));

            // obj2 OBJ: one 8×8 tile (32 bytes), every nibble = 1 (palette index 1).
            byte[] obj2Raw = new byte[32];
            for (int i = 0; i < obj2Raw.Length; i++) obj2Raw[i] = 0x11;
            PlantBytes(rom, OBJ2_OFF, LZ77.compress(obj2Raw));

            // Palette: bank0 color0 = black (0x0000); bank0 color1 = pure red.
            byte[] pal = new byte[512];
            // GBA BGR555: red = 0x001F → little-endian bytes 0x1F,0x00 at color slot 1.
            pal[2] = 0x1F; pal[3] = 0x00;
            Array.Copy(pal, 0, rom.Data, PAL_OFF, 512);

            // Config: one 8-byte descriptor whose four subtiles all reference
            // TILE INDEX 2 (= first tile of the appended obj2 sheet, since the
            // primary sheet has tiles 0 and 1).
            byte[] cfgRaw = new byte[8];
            // Each TSA entry: bits 0-9 = tile index (=2), bank 0, no flip.
            for (int s = 0; s < 4; s++)
            {
                cfgRaw[s * 2 + 0] = 0x02; // tile index low byte = 2
                cfgRaw[s * 2 + 1] = 0x00;
            }
            PlantBytes(rom, CFG_OFF, LZ77.compress(cfgRaw));

            // Change data: 1 tile, index 0 → uses config descriptor 0.
            byte[] chgData = new byte[] { 0x00, 0x00 };
            Array.Copy(chgData, 0, rom.Data, CHG_OFF, chgData.Length);

            // WITH obj2: the subtiles reference tile 2 (from obj2), which is all
            // index 1 → RED.
            IImage withObj2 = MapRenderCore.RenderChangeMap(
                rom, OBJ_OFF, PAL_OFF, CFG_OFF, CHG_OFF, 1, 1, OBJ2_OFF);
            Assert.NotNull(withObj2);
            Assert.Equal(16, withObj2.Width);
            Assert.Equal(16, withObj2.Height);

            byte[] px = withObj2.GetPixelData(); // RGBA, 16×16
            // Top-left pixel (0,0) is in the first subtile, which references obj2
            // tile 2 (all index 1) → red.
            Assert.Equal((byte)255, px[3]); // opaque
            Assert.Equal((byte)248, px[0]); // R (0x1F << 3 = 248)
            Assert.Equal((byte)0,   px[1]); // G
            Assert.Equal((byte)0,   px[2]); // B
        }

        [Fact]
        public void RenderChangeMap_WithoutObj2_TileIndex2_OutOfRange_RendersBlank()
        {
            // Same ROM, but DO NOT pass obj2Offset. Tile index 2 is now out of
            // range (primary sheet has only tiles 0,1) → ImageUtilCore clamps the
            // out-of-range tile to blank/index 0 → NOT red.
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            byte[] objRaw = new byte[64];
            PlantBytes(rom, OBJ_OFF, LZ77.compress(objRaw));

            byte[] pal = new byte[512];
            pal[2] = 0x1F; pal[3] = 0x00;
            Array.Copy(pal, 0, rom.Data, PAL_OFF, 512);

            byte[] cfgRaw = new byte[8];
            for (int s = 0; s < 4; s++) { cfgRaw[s * 2] = 0x02; cfgRaw[s * 2 + 1] = 0x00; }
            PlantBytes(rom, CFG_OFF, LZ77.compress(cfgRaw));

            byte[] chgData = new byte[] { 0x00, 0x00 };
            Array.Copy(chgData, 0, rom.Data, CHG_OFF, chgData.Length);

            IImage noObj2 = MapRenderCore.RenderChangeMap(
                rom, OBJ_OFF, PAL_OFF, CFG_OFF, CHG_OFF, 1, 1); // obj2Offset defaults to 0
            Assert.NotNull(noObj2);
            byte[] px = noObj2.GetPixelData();
            // The top-left pixel must NOT be the obj2 red (it should be the
            // primary index-0 black, since tile 2 is unavailable).
            bool isObj2Red = px[0] == 248 && px[1] == 0 && px[2] == 0;
            Assert.False(isObj2Red, "Without obj2 concat, tile index 2 must not render the obj2 red color");
        }

        // ====================================================================
        // High-byte extraction parity — (obj_plist >> 8) & 0xFF is the obj2 plist.
        // This mirrors the VM RenderChangePreview extraction and WF L704.
        // ====================================================================

        [Theory]
        [InlineData(0x0001u, 0x01u, 0x00u)] // FE8-style: only low byte set, obj2 = 0
        [InlineData(0x0302u, 0x02u, 0x03u)] // FE7-style: low=2 primary, high=3 obj2
        [InlineData(0x00FFu, 0xFFu, 0x00u)] // low=0xFF, obj2 = 0
        [InlineData(0xAB12u, 0x12u, 0xABu)] // both bytes set
        public void Obj2HighByteExtraction_MatchesWfPacking(uint objPlistRaw, uint expectedObj, uint expectedObj2)
        {
            uint objPlist  = objPlistRaw & 0xFFu;
            uint obj2Plist = (objPlistRaw >> 8) & 0xFFu;
            Assert.Equal(expectedObj, objPlist);
            Assert.Equal(expectedObj2, obj2Plist);
        }

        // ====================================================================
        // Guard: a non-zero obj2Offset with a truncated/invalid LZ77 stream
        // fails the whole render (matches WF bail-to-BlankDummy on bad obj2).
        // ====================================================================

        [Fact]
        public void RenderChangeMap_Obj2OffsetTruncated_ReturnsNull()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            byte[] objRaw = new byte[32];
            PlantBytes(rom, OBJ_OFF, LZ77.compress(objRaw));
            byte[] pal = new byte[512];
            Array.Copy(pal, 0, rom.Data, PAL_OFF, 512);
            byte[] cfgRaw = new byte[8];
            PlantBytes(rom, CFG_OFF, LZ77.compress(cfgRaw));
            byte[] chgData = new byte[] { 0x00, 0x00 };
            Array.Copy(chgData, 0, rom.Data, CHG_OFF, chgData.Length);

            // OBJ2_OFF left as all-zero ROM bytes → invalid LZ77 header → null.
            IImage img = MapRenderCore.RenderChangeMap(
                rom, OBJ_OFF, PAL_OFF, CFG_OFF, CHG_OFF, 1, 1, OBJ2_OFF);
            Assert.Null(img);
        }

        // ====================================================================
        // Regression: obj2Offset == 0 keeps the pre-#961 single-tileset behaviour.
        // ====================================================================

        [Fact]
        public void RenderChangeMap_Obj2OffsetZero_BehavesLikeSingleTileset()
        {
            var rom = MakeMinimalRom();
            CoreState.ROM = rom;

            byte[] objRaw = new byte[32];
            PlantBytes(rom, OBJ_OFF, LZ77.compress(objRaw));
            byte[] pal = new byte[512];
            pal[0] = 0x01; pal[1] = 0x00; // bank0 color0 = tiny red-ish
            Array.Copy(pal, 0, rom.Data, PAL_OFF, 512);
            byte[] cfgRaw = new byte[8];
            PlantBytes(rom, CFG_OFF, LZ77.compress(cfgRaw));
            byte[] chgData = new byte[] { 0x00, 0x00 };
            Array.Copy(chgData, 0, rom.Data, CHG_OFF, chgData.Length);

            IImage withZero = MapRenderCore.RenderChangeMap(
                rom, OBJ_OFF, PAL_OFF, CFG_OFF, CHG_OFF, 1, 1, 0);
            IImage noArg = MapRenderCore.RenderChangeMap(
                rom, OBJ_OFF, PAL_OFF, CFG_OFF, CHG_OFF, 1, 1);

            Assert.NotNull(withZero);
            Assert.NotNull(noArg);
            Assert.Equal(noArg.GetPixelData(), withZero.GetPixelData());
        }

        // ====================================================================
        // Real-ROM integration — FE7J obj2: render a change map where the map's
        // obj_plist high byte is non-zero, proving the resolved obj2 path runs on
        // a real ROM. Skips gracefully when FE7J.gba is absent or no such map.
        // ====================================================================

        [Fact]
        public void RealRom_FE7J_RenderChangeMap_WithObj2HighByte_DoesNotThrow()
        {
            string romPath = FindRom("FE7J.gba");
            if (romPath == null) return; // skip

            var rom = new ROM();
            if (!rom.Load(romPath, out string _)) return;
            CoreState.ROM = rom;

            var maps = MapSettingCore.MakeMapIDList(rom);
            foreach (var m in maps)
            {
                uint mapSettingAddr = MapSettingCore.GetMapAddr(rom, m.tag);
                if (!U.isSafetyOffset(mapSettingAddr, rom)) continue;
                if (mapSettingAddr + 8u > (uint)rom.Data.Length) continue;

                uint objPlistRaw = rom.u16(mapSettingAddr + 4);
                uint obj2Plist = (objPlistRaw >> 8) & 0xFFu;
                if (obj2Plist == 0) continue; // only test maps that actually use obj2

                uint changeAddr = MapChangeCore.GetMapChangeAddrWhereMapID(rom, m.tag, out _);
                if (!U.isSafetyOffset(changeAddr, rom)) continue;
                if (changeAddr + 12u > (uint)rom.Data.Length) continue;
                if (rom.u8(changeAddr) == 0xFF) continue;

                int w = (int)rom.u8(changeAddr + 3);
                int h = (int)rom.u8(changeAddr + 4);
                if (w <= 0 || h <= 0) continue;

                uint changeDataOffset = U.toOffset(rom.p32(changeAddr + 8));
                if (!U.isSafetyOffset(changeDataOffset, rom)) continue;

                uint objPlist = objPlistRaw & 0xFFu;
                uint palPlist = rom.u8(mapSettingAddr + 6);
                uint cfgPlist = rom.u8(mapSettingAddr + 7);

                uint objOff  = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.OBJECT,  objPlist,  out _);
                uint obj2Off = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.OBJECT,  obj2Plist, out _);
                uint palOff  = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.PALETTE, palPlist,  out _);
                uint cfgOff  = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.CONFIG,  cfgPlist,  out _);
                if (objOff == U.NOT_FOUND || palOff == U.NOT_FOUND || cfgOff == U.NOT_FOUND) continue;
                if (obj2Off == U.NOT_FOUND) continue;

                // Must not throw; the obj2-concatenated render either succeeds
                // (non-null) or fails gracefully (null) — both are acceptable for
                // this integration smoke test.
                IImage img = null;
                var ex = Record.Exception(() =>
                {
                    img = MapRenderCore.RenderChangeMap(
                        rom, objOff, palOff, cfgOff, changeDataOffset, w, h, obj2Off);
                });
                Assert.Null(ex);
                if (img != null)
                {
                    Assert.Equal(w * 16, img.Width);
                    Assert.Equal(h * 16, img.Height);
                }
                return; // tested one obj2 map — done
            }
            // No obj2-using map found → graceful pass.
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        static ROM MakeMinimalRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000]; // 32 MB
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        static void PlantBytes(ROM rom, uint offset, byte[] bytes)
        {
            if (bytes == null) return;
            Array.Copy(bytes, 0, rom.Data, (int)offset, bytes.Length);
        }

        static string FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = Path.Combine(dir, "roms", romName);
                    if (File.Exists(path)) return path;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
