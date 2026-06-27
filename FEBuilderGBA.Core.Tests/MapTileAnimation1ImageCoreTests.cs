// SPDX-License-Identifier: GPL-3.0-or-later
// #1602 Core tests for MapTileAnimation1ImageCore — the Map Tile Animation
// Type 1 image preview + single-PNG Import/Export + batch (.mapanime1.txt) seam.
//
// The anime1 graphics block is RAW UNCOMPRESSED 4bpp at the entry's +4 pointer,
// sized by the entry's +2 u16 length (the INVERSE of anime2). These tests use a
// synthetic ROM + a stub image service (for GBAColorToRGBA) and validate:
//   * CalcEntryHeight matches the WF CalcHeight(256, len) formula
//   * single import keeps the +2 length authoritative (UNCHANGED) and repoints +4
//   * a wrong-size image mutates ZERO bytes (byte-identical ROM + error)
//   * the written RAW bytes round-trip byte-identically to the encoded tiles
//   * a fault (free-space exhaustion) restores byte-identical
using System;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapTileAnimation1ImageCoreTests
    {
        // Entry near the ROM start; palette just after it; free 0xFF space after
        // the midpoint for WriteRawToROM to land in.
        const uint ENTRY_ADDR = 0x400;
        const uint PAL_ADDR = 0x500;
        const uint ROM_LEN = 0x20000; // 128 KiB

        // A 256x8 sheet = 1024 bytes (one tile row). CalcHeight(256, 1024) == 8.
        const int SHEET_W = 256;
        const int SHEET_H = 8;
        const uint SHEET_LEN = (uint)(SHEET_W * SHEET_H / 2); // 1024

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF); // free-space marker
            for (uint i = 0; i < 0x1000; i++) data[i] = 0x00; // deterministic header/entry region

            // Entry: wait=0x10@+0, length=SHEET_LEN@+2, pointer=0x08000800@+4.
            data[ENTRY_ADDR + 0] = 0x10;
            data[ENTRY_ADDR + 1] = 0x00;
            data[ENTRY_ADDR + 2] = (byte)(SHEET_LEN & 0xFF);
            data[ENTRY_ADDR + 3] = (byte)((SHEET_LEN >> 8) & 0xFF);
            // +4 points at 0x800 (safe, inside the zeroed region).
            data[ENTRY_ADDR + 4] = 0x00;
            data[ENTRY_ADDR + 5] = 0x08;
            data[ENTRY_ADDR + 6] = 0x00;
            data[ENTRY_ADDR + 7] = 0x08;

            // A 16-color palette at PAL_ADDR: distinct GBA colors so remap is
            // unambiguous. color i = i*0x0421 (spreads R/G/B bits).
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(i | (i << 5) | (i << 10));
                data[PAL_ADDR + i * 2] = (byte)(c & 0xFF);
                data[PAL_ADDR + i * 2 + 1] = (byte)((c >> 8) & 0xFF);
            }
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        static IDisposable EnsureImageService()
        {
            var prev = CoreState.ImageService;
            if (prev == null) CoreState.ImageService = new StubImageService();
            return new RestoreImageService(prev);
        }

        sealed class RestoreImageService : IDisposable
        {
            readonly IImageService _prev;
            public RestoreImageService(IImageService prev) { _prev = prev; }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        // Build RGBA pixels whose colors match the synthetic palette (entry i ->
        // palette[i%16]) so RemapToExistingPalette picks index i%16 exactly.
        static byte[] MakeRgbaFromPalette(int w, int h)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                int idx = i % 16;
                ushort c = (ushort)(idx | (idx << 5) | (idx << 10));
                byte r = (byte)((c & 0x1F) << 3);
                byte g = (byte)(((c >> 5) & 0x1F) << 3);
                byte b = (byte)(((c >> 10) & 0x1F) << 3);
                rgba[i * 4 + 0] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                // index 0 must be alpha<128 (transparent) so it remaps to 0.
                rgba[i * 4 + 3] = (idx == 0) ? (byte)0 : (byte)255;
            }
            return rgba;
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1024, 8)]   // 8 rows exact
        [InlineData(128, 8)]    // exactly one row
        [InlineData(129, 8)]    // WF rounds up 1->2 then aligns 2->8 then /8*8 = 8
        [InlineData(1025, 16)]  // 9 rows -> align to 16
        [InlineData(4096, 32)]  // 32 rows exact
        public void CalcEntryHeight_MatchesWFFormula(int length, int expected)
        {
            Assert.Equal(expected, MapTileAnimation1ImageCore.CalcEntryHeight(length));
        }

        [Fact]
        public void ImportEntryImage_KeepsLengthAuthoritative_RepointsP4()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                uint lenBefore = rom.u16(ENTRY_ADDR + 2);
                uint p4Before = rom.u32(ENTRY_ADDR + 4);
                byte[] rgba = MakeRgbaFromPalette(SHEET_W, SHEET_H);

                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, SHEET_W, SHEET_H, PAL_ADDR, 0);

                Assert.Equal("", err);
                // +2 length is UNCHANGED (single-import parity).
                Assert.Equal(lenBefore, rom.u16(ENTRY_ADDR + 2));
                // +4 repointed to a fresh pointer.
                uint p4After = rom.u32(ENTRY_ADDR + 4);
                Assert.True(U.isPointer(p4After), $"P4 not a pointer: 0x{p4After:X08}");
                Assert.NotEqual(p4Before, p4After);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportEntryImage_RoundTrips_RawBytesByteIdentical()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                byte[] rgba = MakeRgbaFromPalette(SHEET_W, SHEET_H);
                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, SHEET_W, SHEET_H, PAL_ADDR, 0);
                Assert.Equal("", err);

                // The RAW bytes written at the new +4 must equal the bytes we'd
                // encode independently (decode -> encode -> ROM round-trip).
                byte[] palette = ImageUtilCore.GetPalette(rom, PAL_ADDR, 16);
                byte[] indexed = ImageImportCore.RemapToExistingPalette(rgba, SHEET_W, SHEET_H, palette, 16);
                byte[] expected = ImageImportCore.EncodeDirectTiles4bpp(indexed, SHEET_W, SHEET_H);

                uint newP4 = U.toOffset(rom.u32(ENTRY_ADDR + 4));
                byte[] actual = rom.getBinaryData(newP4, expected.Length);
                Assert.Equal(expected, actual);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportEntryImage_WrongSize_NoMutation()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                // Height 16 does NOT match CalcHeight(256, SHEET_LEN)=8 -> reject.
                byte[] rgba = MakeRgbaFromPalette(SHEET_W, 16);
                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, SHEET_W, 16, PAL_ADDR, 0);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportEntryImage_WrongWidth_NoMutation()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                // Width must be exactly 256.
                byte[] rgba = MakeRgbaFromPalette(128, SHEET_H);
                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, 128, SHEET_H, PAL_ADDR, 0);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data); // byte-identical
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportEntryImage_NoFreeSpace_RestoresByteIdentical()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                // A ROM at the 32MB cap fully packed with a NON-free byte (0x01)
                // forces FindFreeSpace AND AppendToRomEnd (already at the cap) to
                // fail -> snapshot restore. (FindFreeSpace treats 0x00 and 0xFF
                // runs as free, so the filler must be neither.)
                var rom = new ROM();
                byte[] data = new byte[0x2000000]; // 32 MiB at the cap
                Array.Fill(data, (byte)0x01);
                // entry + palette at the start so the import gets past validation.
                data[ENTRY_ADDR + 2] = (byte)(SHEET_LEN & 0xFF);
                data[ENTRY_ADDR + 3] = (byte)((SHEET_LEN >> 8) & 0xFF);
                data[ENTRY_ADDR + 4] = 0x00; data[ENTRY_ADDR + 5] = 0x08;
                data[ENTRY_ADDR + 6] = 0x00; data[ENTRY_ADDR + 7] = 0x08;
                for (int i = 0; i < 16; i++)
                {
                    ushort c = (ushort)(i | (i << 5) | (i << 10));
                    data[PAL_ADDR + i * 2] = (byte)(c & 0xFF);
                    data[PAL_ADDR + i * 2 + 1] = (byte)((c >> 8) & 0xFF);
                }
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                byte[] rgba = MakeRgbaFromPalette(SHEET_W, SHEET_H);
                string err = MapTileAnimation1ImageCore.ImportEntryImage(
                    rom, ENTRY_ADDR, rgba, SHEET_W, SHEET_H, PAL_ADDR, 0);

                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical restore
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void RenderEntryImage_ValidEntry_ReturnsImage()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                uint p4 = rom.u32(ENTRY_ADDR + 4);
                var img = MapTileAnimation1ImageCore.RenderEntryImage(rom, p4, SHEET_LEN, PAL_ADDR, 0);
                Assert.NotNull(img);
                Assert.Equal(256, img.Width);
                Assert.Equal(8, img.Height);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // ----------------------------------------------------------------
        // Batch import (.mapanime1.txt). The corruption case from the Copilot
        // review: an all-transparent (all-0x00 encoded) frame followed by
        // another frame + the entry table must NOT alias — each entry must
        // point to distinct, correct, non-overlapping data.
        // ----------------------------------------------------------------

        // An all-transparent frame: every pixel alpha<128 -> remap to index 0 ->
        // EncodeDirectTiles4bpp emits all 0x00 bytes. (256x8 = 1024 zero bytes.)
        static byte[] MakeRgbaAllTransparent(int w, int h)
        {
            return new byte[w * h * 4]; // all zero -> alpha 0 -> transparent
        }

        [Fact]
        public void ImportBatchTxt_AllZeroFrameThenAnother_NoAlias_DistinctData()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            string txt = Path.GetTempFileName();
            string png0 = txt + "_0.png"; // all-transparent
            string png1 = txt + "_1.png"; // patterned
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // A pointer SLOT to repoint: use a dword inside the zeroed region.
                uint pointerSlot = 0x300;

                File.WriteAllLines(txt, new[]
                {
                    "//wait\tfilename\tlength",
                    "16\t" + Path.GetFileName(png0),
                    "32\t" + Path.GetFileName(png1),
                });

                // loadRgba returns frame 0 = all transparent (encodes to all 0x00),
                // frame 1 = patterned (distinct non-zero bytes).
                bool LoadRgba(string path, out byte[] rgba, out int w, out int h)
                {
                    w = SHEET_W; h = SHEET_H;
                    rgba = path.EndsWith("_0.png")
                        ? MakeRgbaAllTransparent(SHEET_W, SHEET_H)
                        : MakeRgbaFromPalette(SHEET_W, SHEET_H);
                    return true;
                }

                string err = MapTileAnimation1ImageCore.ImportBatchTxt(
                    rom, pointerSlot, txt, PAL_ADDR, 0, LoadRgba);
                Assert.Equal("", err);

                // The slot now points at the entry table.
                uint tableAddr = U.toOffset(rom.u32(pointerSlot));
                Assert.True(U.isSafetyOffset(tableAddr, rom));

                // Row 0 = all-zero frame; row 1 = patterned; row 2 = terminator.
                uint p0 = U.toOffset(rom.u32(tableAddr + 0 * 8 + 4));
                uint len0 = rom.u16(tableAddr + 0 * 8 + 2);
                uint p1 = U.toOffset(rom.u32(tableAddr + 1 * 8 + 4));
                uint len1 = rom.u16(tableAddr + 1 * 8 + 2);
                // Terminator row 2 = all zero.
                Assert.Equal(0u, rom.u32(tableAddr + 2 * 8 + 4));

                Assert.Equal((uint)SHEET_LEN, len0);
                Assert.Equal((uint)SHEET_LEN, len1);
                // The two image blocks must NOT overlap (no aliasing).
                Assert.True(p0 + len0 <= p1 || p1 + len1 <= p0,
                    $"blocks overlap: p0=0x{p0:X} len0={len0} p1=0x{p1:X} len1={len1}");

                // Frame 0's bytes are all 0x00 (the all-transparent encode); frame
                // 1's bytes are the patterned encode and must remain intact (NOT
                // overwritten by a later write that re-found the zero region).
                byte[] block0 = rom.getBinaryData(p0, (int)len0);
                Assert.All(block0, b => Assert.Equal(0, b));

                byte[] expected1 = ImageImportCore.EncodeDirectTiles4bpp(
                    ImageImportCore.RemapToExistingPalette(
                        MakeRgbaFromPalette(SHEET_W, SHEET_H), SHEET_W, SHEET_H,
                        ImageUtilCore.GetPalette(rom, PAL_ADDR, 16), 16),
                    SHEET_W, SHEET_H);
                byte[] block1 = rom.getBinaryData(p1, (int)len1);
                Assert.Equal(expected1, block1);
            }
            finally
            {
                CoreState.ROM = savedRom;
                TryDelete(txt); TryDelete(png0); TryDelete(png1);
            }
        }

        [Fact]
        public void ImportBatchTxt_WaitOverU16_Rejected_NoMutation()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            string txt = Path.GetTempFileName();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                File.WriteAllLines(txt, new[] { "70000\tframe.png" }); // wait > 0xFFFF
                bool LoadRgba(string p, out byte[] rgba, out int w, out int h)
                { w = SHEET_W; h = SHEET_H; rgba = MakeRgbaFromPalette(SHEET_W, SHEET_H); return true; }

                string err = MapTileAnimation1ImageCore.ImportBatchTxt(rom, 0x300, txt, PAL_ADDR, 0, LoadRgba);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data); // byte-identical, no mutation
            }
            finally { CoreState.ROM = savedRom; TryDelete(txt); }
        }

        [Fact]
        public void ImportBatchTxt_MissingImage_NoMutation()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            string txt = Path.GetTempFileName();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                File.WriteAllLines(txt, new[] { "16\tmissing.png" });
                bool LoadRgba(string p, out byte[] rgba, out int w, out int h)
                { rgba = Array.Empty<byte>(); w = 0; h = 0; return false; } // load fails

                string err = MapTileAnimation1ImageCore.ImportBatchTxt(rom, 0x300, txt, PAL_ADDR, 0, LoadRgba);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data); // byte-identical, no mutation
            }
            finally { CoreState.ROM = savedRom; TryDelete(txt); }
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

        [Fact]
        public void RenderEntryImage_BadPointer_ReturnsNull()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // 0 pointer / out-of-range -> null, never throws.
                Assert.Null(MapTileAnimation1ImageCore.RenderEntryImage(rom, 0, SHEET_LEN, PAL_ADDR, 0));
                Assert.Null(MapTileAnimation1ImageCore.RenderEntryImage(rom, 0xFFFFFFFF, SHEET_LEN, PAL_ADDR, 0));
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // #1602 deferred half: composited-map GIF export (WF ExportGif).
        // -----------------------------------------------------------------

        [Fact]
        public void ReadFrameBytes_ReturnsRawBlockOfCorrectLength()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Entry points at 0x08000800; length = SHEET_LEN (1024). Height 8 ⇒
                // 128*8 = 1024 bytes.
                byte[] frame = MapTileAnimation1ImageCore.ReadFrameBytes(rom, 0x08000800, SHEET_LEN);
                Assert.NotNull(frame);
                Assert.Equal((int)SHEET_LEN, frame.Length);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ReadFrameBytes_BadPointerOrZeroLength_ReturnsNull()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                Assert.Null(MapTileAnimation1ImageCore.ReadFrameBytes(rom, 0, SHEET_LEN));
                Assert.Null(MapTileAnimation1ImageCore.ReadFrameBytes(rom, 0x08000800, 0));
                Assert.Null(MapTileAnimation1ImageCore.ReadFrameBytes(null, 0x08000800, SHEET_LEN));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ExportGif_NullRomOrNoPlist_ReturnsError_NoFile()
        {
            using var _ = EnsureImageService();
            string gif = Path.GetTempFileName();
            TryDelete(gif);
            try
            {
                // null ROM ⇒ localized error, no file.
                Assert.NotEqual("", MapTileAnimation1ImageCore.ExportGif(null, 1, gif));
                Assert.False(File.Exists(gif));

                ROM rom = MakeRom();
                // PLIST 0 ⇒ error, no file.
                Assert.NotEqual("", MapTileAnimation1ImageCore.ExportGif(rom, 0, gif));
                Assert.False(File.Exists(gif));
            }
            finally { TryDelete(gif); }
        }

        [Fact]
        public void ExportGif_UnresolvablePlist_ReturnsError_NoFile()
        {
            var savedRom = CoreState.ROM;
            using var _ = EnsureImageService();
            string gif = Path.GetTempFileName();
            TryDelete(gif);
            try
            {
                ROM rom = MakeRom(); // synthetic ROM has no map referencing any plist.
                CoreState.ROM = rom;
                // A non-zero plist that no map uses ⇒ ResolveGifContext null ⇒ error.
                string err = MapTileAnimation1ImageCore.ExportGif(rom, 0x12, gif);
                Assert.NotEqual("", err);
                Assert.False(File.Exists(gif));
            }
            finally { CoreState.ROM = savedRom; TryDelete(gif); }
        }

        // Real-ROM integration: export a composited-map GIF for the FIRST anime1
        // PLIST in FE8U and assert it is a valid multi-frame GIF89a. Skips
        // gracefully when the ROM is absent or the ROM has no anime1 entries.
        [Fact]
        public void ExportGif_RealRomFE8U_ProducesValidMultiFrameGif()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip

            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            if (CoreState.ImageService == null) CoreState.ImageService = new StubImageService();
            string gif = Path.Combine(Path.GetTempPath(), "maptileanim1_" + Guid.NewGuid().ToString("N") + ".gif");
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip
                CoreState.ROM = rom;

                // Pick the first non-broken anime1 PLIST (mirrors the WF/Avalonia
                // default selection).
                var plistRows = MapTileAnimation1Core.BuildPlistList(rom);
                uint plist = 0;
                foreach (var row in plistRows)
                {
                    if (row.IsBroken) continue;
                    plist = row.Plist;
                    break;
                }
                if (plist == 0) return; // no usable anime1 PLIST — skip

                string err = MapTileAnimation1ImageCore.ExportGif(rom, plist, gif);
                Assert.Equal("", err);
                Assert.True(File.Exists(gif), "GIF file was not created");

                byte[] bytes = File.ReadAllBytes(gif);
                // GIF89a magic.
                Assert.True(bytes.Length > 6);
                Assert.Equal((byte)'G', bytes[0]);
                Assert.Equal((byte)'I', bytes[1]);
                Assert.Equal((byte)'F', bytes[2]);
                Assert.Equal((byte)'8', bytes[3]);
                Assert.Equal((byte)'9', bytes[4]);
                Assert.Equal((byte)'a', bytes[5]);

                // Count frames = number of Graphic Control Extension blocks
                // (0x21 0xF9), one per encoded frame.
                int frameCount = 0;
                for (int i = 0; i + 1 < bytes.Length; i++)
                    if (bytes[i] == 0x21 && bytes[i + 1] == 0xF9) frameCount++;
                Assert.True(frameCount >= 1, $"Expected >= 1 GIF frame, found {frameCount}");
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedSvc;
                TryDelete(gif);
            }
        }

        // Walk up from the test assembly to find roms/<name> next to the .sln.
        static string FindRom(string romName)
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
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
