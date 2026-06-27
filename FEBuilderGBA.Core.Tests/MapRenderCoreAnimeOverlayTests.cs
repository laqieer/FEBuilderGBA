// SPDX-License-Identifier: GPL-3.0-or-later
// #1602 (deferred GIF half) tests for MapRenderCore.RenderMapImage's
// anime1 OVERLAY parameter — the Map Tile Animation Type 1 composited-map
// frame patch (WF ImageUtilMap.DrawMapChipOnly ~L72:
//   U.ArrayPatch(anime.change_bitmap_bytes, 0, objUZ, 32*(8/2)*4*16)).
//
// A synthetic 1×1-tile map renders OBJ tile 0. The anime overlay patches the
// OBJ tile bytes at ANIME1_OBJ_PATCH_OFFSET (=8192). To make the patch land on
// the rendered tile, the test points the rendered tile at the patched region.
// We prove:
//   * a null/empty overlay leaves the render byte-identical to the no-overlay
//     render (no-op);
//   * a real overlay at offset 8192 changes the rendered pixels for a tile that
//     reads from that region (the patch is actually applied before TSA decode);
//   * an out-of-range overlay destination offset is a no-op (no throw).

using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapRenderCoreAnimeOverlayTests : IDisposable
    {
        readonly IImageService _prevService;
        readonly ROM _prevRom;

        public MapRenderCoreAnimeOverlayTests()
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

        const uint OBJ_OFF = 0x200000;
        const uint PAL_OFF = 0x201000;
        const uint CFG_OFF = 0x202000;
        const uint MAP_OFF = 0x203000;

        // ANIME1_OBJ_PATCH_OFFSET = 8192 bytes = 256 tiles of 32 bytes each.
        // To render the patched tile we point config TSA tile-index at tile 256
        // (= 8192 / 32), so the rendered subtile reads the patched bytes.
        const int PATCHED_TILE_INDEX = MapRenderCore.ANIME1_OBJ_PATCH_OFFSET / 32; // 256

        // Build a synthetic ROM whose single rendered logical tile reads OBJ tile
        // #256 (the first tile inside the patch region). OBJ buffer is large
        // enough to contain that tile.
        static ROM BuildRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            rom.LoadLow("synth.gba", data, "BE8E01");

            // OBJ: (PATCHED_TILE_INDEX + 4) tiles, all index 0 initially.
            byte[] objRaw = new byte[32 * (PATCHED_TILE_INDEX + 4)];
            Array.Copy(LZ77.compress(objRaw), 0, rom.Data, OBJ_OFF, LZ77.compress(objRaw).Length);

            // Palette (raw 512 bytes): give a few distinct colors so a changed
            // index is visibly different. color i = i*0x0421 spreads R/G/B bits.
            byte[] pal = new byte[512];
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(i | (i << 5) | (i << 10));
                pal[i * 2] = (byte)(c & 0xFF);
                pal[i * 2 + 1] = (byte)((c >> 8) & 0xFF);
            }
            Array.Copy(pal, 0, rom.Data, PAL_OFF, 512);

            // Config: one descriptor (m=0 → descOff 0). Four TSA entries all point
            // at tile PATCHED_TILE_INDEX, bank 0, no flip.
            byte[] cfgRaw = new byte[8];
            ushort tsa = (ushort)(PATCHED_TILE_INDEX & 0x3FF); // tile index bits 0-9
            for (int s = 0; s < 4; s++)
            {
                cfgRaw[s * 2] = (byte)(tsa & 0xFF);
                cfgRaw[s * 2 + 1] = (byte)(tsa >> 8);
            }
            Array.Copy(LZ77.compress(cfgRaw), 0, rom.Data, CFG_OFF, LZ77.compress(cfgRaw).Length);

            // Map: 1×1 logical tile, MAR value 0.
            byte[] marRaw = new byte[] { 1, 1, 0, 0 };
            Array.Copy(LZ77.compress(marRaw), 0, rom.Data, MAP_OFF, LZ77.compress(marRaw).Length);

            return rom;
        }

        [Fact]
        public void NullOverlay_RendersIdenticalToNoOverlay()
        {
            var rom = BuildRom();
            CoreState.ROM = rom;

            using IImage baseImg = MapRenderCore.RenderMapImage(rom, OBJ_OFF, PAL_OFF, CFG_OFF, MAP_OFF);
            using IImage nullOverlayImg = MapRenderCore.RenderMapImage(
                rom, OBJ_OFF, PAL_OFF, CFG_OFF, MAP_OFF, 0, null, MapRenderCore.ANIME1_OBJ_PATCH_OFFSET);

            Assert.NotNull(baseImg);
            Assert.NotNull(nullOverlayImg);
            Assert.Equal(baseImg.GetPixelData(), nullOverlayImg.GetPixelData());
        }

        [Fact]
        public void Overlay_ChangesRenderedPixelsForPatchedTile()
        {
            var rom = BuildRom();
            CoreState.ROM = rom;

            using IImage baseImg = MapRenderCore.RenderMapImage(rom, OBJ_OFF, PAL_OFF, CFG_OFF, MAP_OFF);
            byte[] basePixels = baseImg.GetPixelData();

            // Overlay: 32 bytes (one tile) of 4bpp index 0x33 — every pixel index 3.
            // The renderer patches these into the OBJ buffer at offset 8192 (= the
            // start of tile #256), which the rendered tile reads.
            byte[] overlay = new byte[32];
            Array.Fill(overlay, (byte)0x33);

            using IImage overImg = MapRenderCore.RenderMapImage(
                rom, OBJ_OFF, PAL_OFF, CFG_OFF, MAP_OFF, 0, overlay, MapRenderCore.ANIME1_OBJ_PATCH_OFFSET);
            byte[] overPixels = overImg.GetPixelData();

            Assert.NotNull(overImg);
            Assert.Equal(baseImg.Width, overImg.Width);
            Assert.Equal(baseImg.Height, overImg.Height);
            // The overlay flips index 0 → index 3, so at least one pixel changed.
            Assert.NotEqual(basePixels, overPixels);
        }

        [Fact]
        public void OverlayOutOfRangeOffset_IsNoOp()
        {
            var rom = BuildRom();
            CoreState.ROM = rom;

            using IImage baseImg = MapRenderCore.RenderMapImage(rom, OBJ_OFF, PAL_OFF, CFG_OFF, MAP_OFF);
            byte[] overlay = new byte[32];
            Array.Fill(overlay, (byte)0x33);

            IImage img = null;
            var ex = Record.Exception(() =>
            {
                // dest offset far past the OBJ buffer end → bounded copy is a no-op.
                img = MapRenderCore.RenderMapImage(
                    rom, OBJ_OFF, PAL_OFF, CFG_OFF, MAP_OFF, 0, overlay, 0x7FFFFFFF);
            });
            Assert.Null(ex);
            Assert.NotNull(img);
            using (img)
            {
                Assert.Equal(baseImg.GetPixelData(), img.GetPixelData());
            }
        }
    }
}
