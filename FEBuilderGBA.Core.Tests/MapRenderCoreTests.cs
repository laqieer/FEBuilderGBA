// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for MapRenderCore.RenderMapImage (#855, NV6-PR1).
//
// Covers:
//   * Real-ROM integration on FE6/FE7U/FE8U (skips gracefully when ROMs absent).
//     -- Non-null result, correct pixel dimensions (width*16 × height*16).
//     -- Bank-0/index-0 pixel is OPAQUE (guards MR5: opaqueIndex0=true fix).
//   * Guard/edge-case paths (synthetic ROMs built with LZ77.compress):
//     -- Null ROM → null (no throw).
//     -- Missing ImageService → null (no throw).
//     -- Truncated OBJ LZ77 stream → null.
//     -- Truncated config LZ77 stream → null.
//     -- Truncated MAR LZ77 stream → null.
//     -- width/height == 0 → null.
//     -- Near-EOF offsets → no throw.
//     -- Out-of-range config descOff → no throw (no null; partial canvas returned).
//
// Pattern: [Collection("SharedState")] + IDisposable save/restore, StubImageService
// (reused from BattleAnimeDetailTests), LZ77.compress for synthetic payloads,
// int array indices throughout.

using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapRenderCoreTests : IDisposable
    {
        readonly IImageService _prevService;
        readonly ROM _prevRom;

        public MapRenderCoreTests()
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
        // Helper: find a named ROM file by walking up from the test assembly.
        // Returns null when not found — all callers skip gracefully on null.
        // ====================================================================
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

        // ====================================================================
        // Helper: resolve the four map offsets for map index mapId from a ROM.
        // Returns false when any resolution fails (caller skips the test).
        // Maps the map-setting struct fields exactly as WF MapSettingForm.cs:
        //   obj_plist      = u16(addr + 4)  → resolved via map_obj_pointer table
        //   palette_plist  = u8(addr + 6)   → resolved via map_pal_pointer table
        //   config_plist   = u8(addr + 7)   → resolved via map_config_pointer table
        //   mappointer_plist = u8(addr + 8) → resolved via map_map_pointer_pointer table
        // ====================================================================
        static bool TryResolveMapOffsets(ROM rom, uint mapId,
            out uint objOffset, out uint paletteOffset,
            out uint configOffset, out uint mapOffset)
        {
            objOffset = paletteOffset = configOffset = mapOffset = 0;
            if (rom?.RomInfo == null) return false;

            // Get the map-setting struct address for mapId.
            uint mapSettingBase = rom.p32(rom.RomInfo.map_setting_pointer);
            if (!U.isSafetyOffset(mapSettingBase, rom)) return false;
            uint addr = mapSettingBase + mapId * rom.RomInfo.map_setting_datasize;
            if (!U.isSafetyOffset(addr, rom)) return false;

            // Read plist indices from the struct (same offsets as WF:188-191).
            uint objPlist      = rom.u16(addr + 4);
            uint palPlist      = rom.u8(addr + 6);
            uint cfgPlist      = rom.u8(addr + 7);
            uint mapPlist      = rom.u8(addr + 8);

            // Resolve each plist to a ROM data offset via its pointer table.
            objOffset     = ResolvePlist(rom, rom.RomInfo.map_obj_pointer,        objPlist);
            paletteOffset = ResolvePlist(rom, rom.RomInfo.map_pal_pointer,        palPlist);
            configOffset  = ResolvePlist(rom, rom.RomInfo.map_config_pointer,     cfgPlist);
            mapOffset     = ResolvePlist(rom, rom.RomInfo.map_map_pointer_pointer, mapPlist);

            return objOffset     != U.NOT_FOUND && objOffset     != 0
                && paletteOffset != U.NOT_FOUND && paletteOffset != 0
                && configOffset  != U.NOT_FOUND && configOffset  != 0
                && mapOffset     != U.NOT_FOUND && mapOffset     != 0;
        }

        // Resolve plist index → ROM data offset via the plist pointer table.
        // Equivalent to MapChangeCore.PlistToOffsetAddr but also supports
        // MAP and PALETTE tables (which MapChangeCore.PlistType doesn't enumerate).
        static uint ResolvePlist(ROM rom, uint plistPointer, uint plist)
        {
            if (plistPointer == 0) return U.NOT_FOUND;
            uint base_ = rom.p32(plistPointer);
            if (!U.isSafetyOffset(base_, rom)) return U.NOT_FOUND;
            uint entryAddr = base_ + plist * 4u;
            if (entryAddr + 4u > (uint)rom.Data.Length) return U.NOT_FOUND;
            uint target = rom.p32(entryAddr);
            if (!U.isSafetyOffset(target, rom)) return U.NOT_FOUND;
            return target;
        }

        // ====================================================================
        // Real-ROM integration tests.
        // Each version is skipped gracefully when the ROM is not in roms/.
        // ====================================================================

        [Fact]
        public void RealRom_FE6_Map0_NonNullAndCorrectDimensions()
        {
            string romPath = FindRom("FE6.gba");
            if (romPath == null) return; // skip

            var rom = new ROM();
            if (!rom.Load(romPath, out string _)) return;
            CoreState.ROM = rom;

            if (!TryResolveMapOffsets(rom, 0,
                    out uint objOff, out uint palOff, out uint cfgOff, out uint mapOff))
                return; // skip if map 0 can't be resolved

            IImage img = MapRenderCore.RenderMapImage(rom, objOff, palOff, cfgOff, mapOff);
            Assert.NotNull(img);

            // Verify pixel dimensions = width*16 × height*16.
            byte[] mar = LZ77.decompress(rom.Data, mapOff);
            Assert.NotNull(mar);
            int w = mar[0];
            int h = mar[1];
            Assert.Equal(w * 16, img.Width);
            Assert.Equal(h * 16, img.Height);
        }

        [Fact]
        public void RealRom_FE7U_Map0_NonNullAndCorrectDimensions()
        {
            string romPath = FindRom("FE7U.gba");
            if (romPath == null) return;

            var rom = new ROM();
            if (!rom.Load(romPath, out string _)) return;
            CoreState.ROM = rom;

            if (!TryResolveMapOffsets(rom, 0,
                    out uint objOff, out uint palOff, out uint cfgOff, out uint mapOff))
                return;

            IImage img = MapRenderCore.RenderMapImage(rom, objOff, palOff, cfgOff, mapOff);
            Assert.NotNull(img);

            byte[] mar = LZ77.decompress(rom.Data, mapOff);
            Assert.NotNull(mar);
            int w = mar[0];
            int h = mar[1];
            Assert.Equal(w * 16, img.Width);
            Assert.Equal(h * 16, img.Height);
        }

        [Fact]
        public void RealRom_FE8U_Map0_NonNullAndCorrectDimensions()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return;

            var rom = new ROM();
            if (!rom.Load(romPath, out string _)) return;
            CoreState.ROM = rom;

            if (!TryResolveMapOffsets(rom, 0,
                    out uint objOff, out uint palOff, out uint cfgOff, out uint mapOff))
                return;

            IImage img = MapRenderCore.RenderMapImage(rom, objOff, palOff, cfgOff, mapOff);
            Assert.NotNull(img);

            byte[] mar = LZ77.decompress(rom.Data, mapOff);
            Assert.NotNull(mar);
            int w = mar[0];
            int h = mar[1];
            Assert.Equal(w * 16, img.Width);
            Assert.Equal(h * 16, img.Height);
        }

        // ====================================================================
        // MR5 guard: bank-0 / index-0 pixel is OPAQUE (alpha 255).
        //
        // A synthetic map is rendered with a known palette where palette bank 0,
        // color index 0 = GBA color 0x0001 (R=8, G=0, B=0). The map has a
        // single 16×16 logical tile whose four subtile TSA entries all point to
        // tile 0 with palette bank 0 and no flip. Every pixel in tile 0 is index
        // 0. After rendering, the pixel at (0,0) must be opaque (alpha 255) and
        // carry the decoded color — not transparent (alpha 0).
        // ====================================================================

        [Fact]
        public void OpaqueIndex0_PaletteBank0Color0_IsOpaque()
        {
            var (rom, objOff, palOff, cfgOff, mapOff) = BuildOpaqueIndex0Rom();
            CoreState.ROM = rom;

            IImage img = MapRenderCore.RenderMapImage(rom, objOff, palOff, cfgOff, mapOff);
            Assert.NotNull(img);
            Assert.Equal(16, img.Width);
            Assert.Equal(16, img.Height);

            byte[] pixels = img.GetPixelData(); // RGBA

            // Pixel (0,0) is in the top-left subtile, top-left pixel, index 0.
            // With opaqueIndex0=true: alpha MUST be 255.
            int idx = 0; // pixel (0,0) = offset 0
            byte alpha = pixels[idx + 3];
            Assert.Equal((byte)255, alpha);

            // Verify the color is actually the palette color for index 0, bank 0.
            // GBA color 0x0001: R = (1 & 0x1F) << 3 = 8, G = 0, B = 0.
            Assert.Equal((byte)8,   pixels[idx + 0]); // R
            Assert.Equal((byte)0,   pixels[idx + 1]); // G
            Assert.Equal((byte)0,   pixels[idx + 2]); // B
        }

        // ====================================================================
        // Guard / edge-case tests — all built from synthetic ROMs.
        // ====================================================================

        [Fact]
        public void NullRom_ReturnsNull()
        {
            IImage img = MapRenderCore.RenderMapImage(null, 0x200, 0x400, 0x600, 0x800);
            Assert.Null(img);
        }

        [Fact]
        public void MissingImageService_ReturnsNull()
        {
            CoreState.ImageService = null;
            var rom = BuildMinimalRom();

            // Even with a valid ROM, no ImageService → null.
            IImage img = MapRenderCore.RenderMapImage(rom, 0, 0, 0, 0);
            Assert.Null(img);
        }

        [Fact]
        public void TruncatedObjLZ77_ReturnsNull()
        {
            var rom = BuildMinimalRom();
            CoreState.ROM = rom;
            // Offset 0x200 in our ROM has zero data — LZ77 header is missing/invalid.
            IImage img = MapRenderCore.RenderMapImage(rom, 0x200, 0x400, 0x600, 0x800);
            Assert.Null(img);
        }

        [Fact]
        public void TruncatedConfigLZ77_ReturnsNull()
        {
            var rom = BuildMinimalRom();
            CoreState.ROM = rom;

            // Plant a valid OBJ at 0x200 and palette at 0x400 but leave config at 0x600 as zeros.
            byte[] objData = new byte[32 * 16]; // minimal tiles
            byte[] objCompressed = LZ77.compress(objData);
            PlantBytes(rom, 0x200, objCompressed);

            // Palette: 512 bytes all zeros
            // (address 0x400 in a zeroed ROM is fine for raw read — all zeros,
            // which makes GBA color 0x0000 = black).

            // config at 0x600 stays zeros → LZ77 header invalid → null.
            IImage img = MapRenderCore.RenderMapImage(rom, 0x200, 0x400, 0x600, 0x800);
            Assert.Null(img);
        }

        [Fact]
        public void TruncatedMarLZ77_ReturnsNull()
        {
            var rom = BuildMinimalRom();
            CoreState.ROM = rom;

            byte[] objData = new byte[32 * 16];
            byte[] objCompressed = LZ77.compress(objData);
            PlantBytes(rom, 0x200, objCompressed);

            byte[] cfgData = new byte[8 * 256]; // 256 descriptors of 8 bytes each
            byte[] cfgCompressed = LZ77.compress(cfgData);
            PlantBytes(rom, 0x600, cfgCompressed);

            // mar at 0x800 stays zeros → LZ77 header invalid → null.
            IImage img = MapRenderCore.RenderMapImage(rom, 0x200, 0x400, 0x600, 0x800);
            Assert.Null(img);
        }

        [Fact]
        public void ZeroWidthMap_ReturnsNull()
        {
            var rom = BuildMinimalRom();
            CoreState.ROM = rom;

            byte[] objData = new byte[32 * 16];
            PlantBytes(rom, 0x200, LZ77.compress(objData));
            byte[] cfgData = new byte[8];
            PlantBytes(rom, 0x600, LZ77.compress(cfgData));

            // MAR with width=0, height=5.
            byte[] marData = new byte[] { 0, 5 };
            PlantBytes(rom, 0x800, LZ77.compress(marData));

            IImage img = MapRenderCore.RenderMapImage(rom, 0x200, 0x400, 0x600, 0x800);
            Assert.Null(img);
        }

        [Fact]
        public void ZeroHeightMap_ReturnsNull()
        {
            var rom = BuildMinimalRom();
            CoreState.ROM = rom;

            byte[] objData = new byte[32 * 16];
            PlantBytes(rom, 0x200, LZ77.compress(objData));
            byte[] cfgData = new byte[8];
            PlantBytes(rom, 0x600, LZ77.compress(cfgData));

            // MAR with width=5, height=0.
            byte[] marData = new byte[] { 5, 0 };
            PlantBytes(rom, 0x800, LZ77.compress(marData));

            IImage img = MapRenderCore.RenderMapImage(rom, 0x200, 0x400, 0x600, 0x800);
            Assert.Null(img);
        }

        [Fact]
        public void NearEofOffsets_DoesNotThrow()
        {
            var rom = BuildMinimalRom();
            CoreState.ROM = rom;

            // Pass ROM-end offsets — must return null, never throw.
            IImage img = null;
            var ex = Record.Exception(() =>
            {
                img = MapRenderCore.RenderMapImage(rom,
                    (uint)rom.Data.Length - 1,
                    (uint)rom.Data.Length - 1,
                    (uint)rom.Data.Length - 1,
                    (uint)rom.Data.Length - 1);
            });
            Assert.Null(ex);
            Assert.Null(img);
        }

        [Fact]
        public void OutOfRangeConfigDescOff_DoesNotThrow_ReturnsImage()
        {
            // A map where every MAR entry points to a config offset that is
            // beyond the end of configUZ. The algorithm should skip (treat as
            // zero-filled) rather than returning null, producing a valid
            // (all-zero-TSA) image with the correct dimensions.
            var rom = BuildMinimalRom();
            CoreState.ROM = rom;

            // OBJ: 2 tiles (64 bytes of 4bpp data — tile 0 all zeros).
            byte[] objData = new byte[32 * 2];
            PlantBytes(rom, 0x200, LZ77.compress(objData));

            // Config: only 4 bytes (too small for a full 8-byte descriptor).
            // Any MAR value > 0 will produce descOff >= 2 which exceeds length 4
            // when accessing +7.
            byte[] cfgData = new byte[4];
            PlantBytes(rom, 0x600, LZ77.compress(cfgData));

            // MAR: 3×2 map, all tiles index 0xFFF (descOff = 0x1FFE >> configUZ.Length).
            // width=3, height=2 → 6 tiles.
            byte[] marData = new byte[2 + 6 * 2];
            marData[0] = 3;  // width
            marData[1] = 2;  // height
            for (int i = 0; i < 6; i++)
            {
                marData[2 + i * 2 + 0] = 0xFF;
                marData[2 + i * 2 + 1] = 0x0F; // value 0x0FFF → descOff = 0x1FFE
            }
            PlantBytes(rom, 0x800, LZ77.compress(marData));

            IImage img = null;
            var ex = Record.Exception(() =>
            {
                img = MapRenderCore.RenderMapImage(rom, 0x200, 0x400, 0x600, 0x800);
            });
            Assert.Null(ex);
            // The image must be returned (not null) — the out-of-range entries
            // are skipped and produce an all-zero (blank) canvas.
            Assert.NotNull(img);
            Assert.Equal(3 * 16, img.Width);
            Assert.Equal(2 * 16, img.Height);
        }

        // ====================================================================
        // Synthetic ROM construction helpers
        // ====================================================================

        /// <summary>
        /// Build a minimal 32MB ROM (all-zero data, no version RomInfo) just for
        /// testing guard paths that fail early (null ROM / bad offsets).
        /// </summary>
        static ROM BuildMinimalRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000]; // 32 MB
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        /// <summary>
        /// Build a synthetic ROM for the MR5 opaque-index-0 test.
        ///
        /// Layout:
        ///   0x200000  OBJ tiles — one 8×8 tile (32 bytes), all pixels index 0.
        ///   0x201000  Palette — 512 raw bytes; color 0 in bank 0 = GBA 0x0001
        ///             (R=8, G=0, B=0).
        ///   0x202000  Config — one 8-byte descriptor: four TSA entries all = 0
        ///             (tile 0, palette bank 0, no flip).
        ///   0x203000  Map data — 1×1 logical tile, MAR value = 0 (index into
        ///             config descriptor 0).
        /// </summary>
        static (ROM rom, uint objOff, uint palOff, uint cfgOff, uint mapOff)
            BuildOpaqueIndex0Rom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000000];
            rom.LoadLow("synth.gba", data, "BE8E01");

            const uint OBJ_OFF = 0x200000;
            const uint PAL_OFF = 0x201000;
            const uint CFG_OFF = 0x202000;
            const uint MAP_OFF = 0x203000;

            // OBJ: one 8×8 4bpp tile (32 bytes); all pixels are index 0.
            byte[] objRaw = new byte[32];
            PlantBytes(rom, OBJ_OFF, LZ77.compress(objRaw));

            // Palette (raw, 512 bytes): bank 0, color 0 = GBA 0x0001 (R bit only).
            // GBA color 0x0001: bits 0-4 = R=1, 5-9 = G=0, 10-14 = B=0.
            // Decoded via StubImageService: R = (1 & 0x1F) << 3 = 8, G = 0, B = 0.
            byte[] pal = new byte[512]; // 16 banks × 16 colors × 2 bytes
            pal[0] = 0x01; // GBA color 0x0001, low byte
            pal[1] = 0x00; // GBA color 0x0001, high byte
            Array.Copy(pal, 0, rom.Data, PAL_OFF, 512);

            // Config: one descriptor at offset 0 (m=0 → descOff = 0<<1 = 0).
            // Four TSA entries: all zeros (tile 0, palette bank 0, no flip).
            byte[] cfgRaw = new byte[8];
            PlantBytes(rom, CFG_OFF, LZ77.compress(cfgRaw));

            // Map: 1×1 tile, MAR value = 0.
            byte[] marRaw = new byte[] { 1, 1, 0, 0 }; // width=1, height=1, mar[0]=0
            PlantBytes(rom, MAP_OFF, LZ77.compress(marRaw));

            return (rom, OBJ_OFF, PAL_OFF, CFG_OFF, MAP_OFF);
        }

        static void PlantBytes(ROM rom, uint offset, byte[] bytes)
        {
            if (bytes == null) return;
            Array.Copy(bytes, 0, rom.Data, offset, bytes.Length);
        }
    }
}
