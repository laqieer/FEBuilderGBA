// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RomAnimeMultiFrameCore (#1230) — the In-ROM Magic Animation editor's
// DEFERRED multi-frame `.txt` script export/import + animated-GIF export, on top of
// the single-frame slice (RomAnimeCore, #1176).
//
// Each test plants a synthetic `romanime_` entry of one of the four WF storage
// shapes, then drives ExportTxt -> ImportTxt round-trips, the fault-restore path,
// and ExportGif. The four shapes:
//   FIXEDCOUNT  + multi-palette  : per-frame image/tsa/pal lists; FramePointer == N.
//   FIXEDCOUNT  + palette-anime  : ONE image+tsa, a per-frame PAL list (TSAList==1 &&
//                                  PaletteList>=2).
//   frame-table + COMMONPALETTE  : per-frame image/tsa lists, ONE shared palette
//                                  (TSAList>=2 && PaletteList==1) + the {id,wait} table.
//   frame-table + multi-palette  : per-frame image/tsa/pal lists + the {id,wait} table.
//
// Reuses the synthetic-ROM harness shape from RomAnimeCoreTests (rom.LoadLow + a
// StubImageService). [Collection("SharedState")] because the render reads
// CoreState.ImageService and the import reads CoreState.ROM.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RomAnimeMultiFrameCoreTests
    {
        const int WIDTH_TILES = 8;        // 64 px wide
        const int FRAME_HEIGHT = 8 * 16;  // 128 px tall canvas

        // Pointer SLOTS (addresses that themselves hold pointer-lists / values).
        const uint FRAME_PTR_SLOT = 0x000800;
        const uint TSA_PTR_SLOT   = 0x001000;
        const uint IMAGE_PTR_SLOT = 0x002000;
        const uint PAL_PTR_SLOT   = 0x003000;
        // The pointer LISTS.
        const uint FRAME_TABLE    = 0x008000;
        const uint TSA_LIST       = 0x010000;
        const uint IMAGE_LIST     = 0x011000;
        const uint PAL_LIST       = 0x012000;
        // Per-frame data (two frames worth, distinct).
        const uint TSA_DATA_0     = 0x020000;
        const uint TSA_DATA_1     = 0x020800;
        const uint IMAGE_DATA_0   = 0x021000;
        const uint IMAGE_DATA_1   = 0x021800;
        const uint PAL_DATA_0     = 0x022000;
        const uint PAL_DATA_1     = 0x022800;

        const ushort RED   = 0x001F;
        const ushort GREEN = 0x03E0;
        const ushort BLUE  = 0x7C00;

        // =================================================================
        // Round-trip: FIXEDCOUNT multi-palette (2 frames).
        // =================================================================

        [Fact]
        public void RoundTrip_FixedCountMultiPalette_ReproducesFrames()
        {
            WithRom((rom, dir) =>
            {
                PlantFixedCount(rom, 2, paletteAnime: false);
                RomAnimeCore.RomAnimeEntry e = ResolveFixed(rom, 2, "FIXEDCOUNT");
                Assert.False(e.IsFrameTable);

                string script = Path.Combine(dir, "rt.txt");
                Assert.Equal("", RomAnimeMultiFrameCore.ExportTxt(rom, e, script));
                AssertScriptHasFrames(script, 2);

                // Materialize the script's referenced PNG placeholders (the stub codec
                // writes nothing on Save), then import the just-exported script.
                MaterializeScriptPngs(dir, script);
                bool ok = RomAnimeMultiFrameCore.ImportTxt(rom, e, script, Loader, out string err);
                Assert.True(ok, err);

                // Re-resolve + re-export: still 2 renderable frames.
                RomAnimeCore.RomAnimeEntry e2 = ResolveFixed(rom, 2, "FIXEDCOUNT");
                string script2 = Path.Combine(dir, "rt2.txt");
                Assert.Equal("", RomAnimeMultiFrameCore.ExportTxt(rom, e2, script2));
                AssertScriptHasFrames(script2, 2);
            });
        }

        // =================================================================
        // FIXEDCOUNT palette-animation (TSAList==1, PaletteList>=2).
        // =================================================================

        [Fact]
        public void Import_FixedCountPaletteAnime_WritesOneImagePerFramePalette()
        {
            WithRom((rom, dir) =>
            {
                PlantFixedCount(rom, 2, paletteAnime: true);
                RomAnimeCore.RomAnimeEntry e = ResolveFixed(rom, 2, "");
                Assert.False(e.IsFrameTable);
                Assert.Single(e.TSAList);
                Assert.Equal(2, e.PaletteList.Count);

                string script = WriteScript(dir, "pa.txt", new (uint, string)[]
                {
                    (1, "f0.png"), (1, "f1.png"),
                });
                MakePngFiles(dir, "f0.png", "f1.png");

                bool ok = RomAnimeMultiFrameCore.ImportTxt(rom, e, script, Loader, out string err);
                Assert.True(ok, err);

                // The PALETTE pointer now resolves to a 2-entry list (one per frame).
                RomAnimeCore.RomAnimeEntry e2 = ResolveFixed(rom, 2, "");
                Assert.Equal(2, e2.PaletteList.Count);
                // The IMAGE list still resolves (frame 0's image written + repointed).
                uint img = U.toOffset(rom.u32(IMAGE_PTR_SLOT));
                Assert.True(U.isSafetyOffset(img, rom));
            });
        }

        // =================================================================
        // frame-table COMMONPALETTE (TSAList>=2, PaletteList==1).
        // =================================================================

        [Fact]
        public void RoundTrip_FrameTableCommonPalette_ReproducesTable()
        {
            WithRom((rom, dir) =>
            {
                PlantFrameTable(rom, commonPalette: true);
                RomAnimeCore.RomAnimeEntry e = ResolveFrameTable(rom, "COMMONPALETTE");
                Assert.True(e.IsFrameTable);
                Assert.True(e.TSAList.Count >= 2);
                Assert.Single(e.PaletteList);

                string script = Path.Combine(dir, "ct.txt");
                Assert.Equal("", RomAnimeMultiFrameCore.ExportTxt(rom, e, script));

                MaterializeScriptPngs(dir, script);
                bool ok = RomAnimeMultiFrameCore.ImportTxt(rom, e, script, Loader, out string err);
                Assert.True(ok, err);

                // A fresh frame table is in place + walks to a 0xFFFF terminator.
                uint tableAddr = rom.p32(FRAME_PTR_SLOT);
                Assert.True(U.isSafetyOffset(tableAddr, rom));
                Assert.True(WalkFrameTableCount(rom, tableAddr) >= 2);

                // PALETTE pointer resolves to a single shared 32-byte block (count 1).
                RomAnimeCore.RomAnimeEntry e2 = ResolveFrameTable(rom, "COMMONPALETTE");
                Assert.Single(e2.PaletteList);
            });
        }

        // =================================================================
        // frame-table multi-palette.
        // =================================================================

        [Fact]
        public void RoundTrip_FrameTableMultiPalette_ReproducesTable()
        {
            WithRom((rom, dir) =>
            {
                PlantFrameTable(rom, commonPalette: false);
                RomAnimeCore.RomAnimeEntry e = ResolveFrameTable(rom, "");
                Assert.True(e.IsFrameTable);
                Assert.True(e.PaletteList.Count >= 2);

                string script = Path.Combine(dir, "mt.txt");
                Assert.Equal("", RomAnimeMultiFrameCore.ExportTxt(rom, e, script));

                MaterializeScriptPngs(dir, script);
                bool ok = RomAnimeMultiFrameCore.ImportTxt(rom, e, script, Loader, out string err);
                Assert.True(ok, err);

                uint tableAddr = rom.p32(FRAME_PTR_SLOT);
                Assert.True(U.isSafetyOffset(tableAddr, rom));
                Assert.True(WalkFrameTableCount(rom, tableAddr) >= 2);

                RomAnimeCore.RomAnimeEntry e2 = ResolveFrameTable(rom, "");
                Assert.True(e2.PaletteList.Count >= 2);
            });
        }

        // =================================================================
        // Atomic fault-restore: a failing import mutates ZERO bytes.
        // =================================================================

        [Fact]
        public void Import_WrongFixedCount_MutatesZeroBytes()
        {
            WithRom((rom, dir) =>
            {
                PlantFixedCount(rom, 2, paletteAnime: false);
                RomAnimeCore.RomAnimeEntry e = ResolveFixed(rom, 2, "FIXEDCOUNT");
                byte[] before = (byte[])rom.Data.Clone();

                // Script defines only ONE frame but the entry requires exactly 2.
                string script = WriteScript(dir, "bad.txt", new (uint, string)[] { (1, "only.png") });
                MakePngFiles(dir, "only.png");

                bool ok = RomAnimeMultiFrameCore.ImportTxt(rom, e, script, Loader, out string err);
                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data); // byte-identical
            });
        }

        [Fact]
        public void Import_ForcedNoFreeSpace_MutatesZeroBytes()
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            string dir = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "romanime_mf_" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                // A ROM at the 32 MB max filled with 0x01 (no free runs) -> every write
                // fails -> the import must restore byte-identical.
                const uint MAX = 0x02000000;
                var rom = new ROM();
                byte[] data = new byte[MAX];
                Array.Fill(data, (byte)0x01);
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();

                PlantFixedCount(rom, 2, paletteAnime: false);
                RomAnimeCore.RomAnimeEntry e = ResolveFixed(rom, 2, "FIXEDCOUNT");
                byte[] before = (byte[])rom.Data.Clone();

                string script = WriteScript(dir, "nofree.txt", new (uint, string)[] { (1, "f0.png"), (1, "f1.png") });
                MakePngFiles(dir, "f0.png", "f1.png");

                bool ok = RomAnimeMultiFrameCore.ImportTxt(rom, e, script, Loader, out string err);
                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data);
            }
            finally
            {
                CoreState.ROM = savedRom; CoreState.ImageService = savedSvc;
                TryDeleteDir(dir);
            }
        }

        [Fact]
        public void Import_MissingPng_MutatesZeroBytes()
        {
            WithRom((rom, dir) =>
            {
                PlantFixedCount(rom, 2, paletteAnime: false);
                RomAnimeCore.RomAnimeEntry e = ResolveFixed(rom, 2, "FIXEDCOUNT");
                byte[] before = (byte[])rom.Data.Clone();

                // Script references a PNG that does not exist on disk.
                string script = WriteScript(dir, "miss.txt", new (uint, string)[] { (1, "ghost.png"), (1, "ghost2.png") });

                bool ok = RomAnimeMultiFrameCore.ImportTxt(rom, e, script, Loader, out string err);
                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            });
        }

        // =================================================================
        // ExportGif: produces a valid GIF89a file.
        // =================================================================

        [Fact]
        public void ExportGif_FrameTable_WritesValidGif89a()
        {
            WithRom((rom, dir) =>
            {
                PlantFrameTable(rom, commonPalette: false);
                RomAnimeCore.RomAnimeEntry e = ResolveFrameTable(rom, "");

                string gif = Path.Combine(dir, "anim.gif");
                Assert.Equal("", RomAnimeMultiFrameCore.ExportGif(rom, e, gif));

                Assert.True(File.Exists(gif));
                byte[] bytes = File.ReadAllBytes(gif);
                Assert.True(bytes.Length > 6);
                // GIF89a header.
                Assert.Equal((byte)'G', bytes[0]);
                Assert.Equal((byte)'I', bytes[1]);
                Assert.Equal((byte)'F', bytes[2]);
                Assert.Equal((byte)'8', bytes[3]);
                Assert.Equal((byte)'9', bytes[4]);
                Assert.Equal((byte)'a', bytes[5]);
                // GIF trailer.
                Assert.Equal((byte)0x3B, bytes[bytes.Length - 1]);
            });
        }

        [Fact]
        public void ExportGif_FixedCount_WritesValidGif89a()
        {
            WithRom((rom, dir) =>
            {
                PlantFixedCount(rom, 2, paletteAnime: false);
                RomAnimeCore.RomAnimeEntry e = ResolveFixed(rom, 2, "FIXEDCOUNT");

                string gif = Path.Combine(dir, "fixed.gif");
                Assert.Equal("", RomAnimeMultiFrameCore.ExportGif(rom, e, gif));
                byte[] bytes = File.ReadAllBytes(gif);
                Assert.Equal((byte)'G', bytes[0]);
                Assert.Equal((byte)'a', bytes[5]);
            });
        }

        // =================================================================
        // Guards: null inputs never throw, return errors.
        // =================================================================

        [Fact]
        public void ExportTxt_NullEntry_ReturnsError()
            => Assert.False(string.IsNullOrEmpty(RomAnimeMultiFrameCore.ExportTxt(null, null, "x.txt")));

        [Fact]
        public void ImportTxt_MissingScript_ReturnsErrorNoThrow()
        {
            WithRom((rom, dir) =>
            {
                PlantFixedCount(rom, 2, paletteAnime: false);
                RomAnimeCore.RomAnimeEntry e = ResolveFixed(rom, 2, "FIXEDCOUNT");
                bool ok = RomAnimeMultiFrameCore.ImportTxt(rom, e,
                    Path.Combine(dir, "no-such.txt"), Loader, out string err);
                Assert.False(ok);
                Assert.False(string.IsNullOrEmpty(err));
            });
        }

        // =================================================================
        // The loader. The Core.Tests StubImageService does NOT do real PNG I/O
        // (LoadImage returns null, Save is a no-op), so the loader is SYNTHETIC:
        // it returns deterministic indexed pixels + a 16-color GBA palette for any
        // path that exists on disk, and null for a missing file (so the missing-PNG
        // fault test can exercise the validate-before-mutate path). This mirrors what
        // the View's real ImageImportService.LoadAndQuantizeFromFile yields — quantized
        // indexed bytes + a 32-byte palette — without depending on a real image codec.
        // =================================================================

        static (byte[] indexedPixels, byte[] gbaPalette16, int width, int height)? Loader(string pngPath)
        {
            if (!File.Exists(pngPath)) return null;
            int w = WIDTH_TILES * 8, h = FRAME_HEIGHT;
            byte[] indexed = new byte[w * h];
            // Seed the indices off the filename so distinct PNGs encode to distinct tiles.
            int seed = Math.Abs((Path.GetFileNameWithoutExtension(pngPath) ?? "").GetHashCode()) % 13 + 1;
            for (int i = 0; i < indexed.Length; i++) indexed[i] = (byte)(((i + seed) % 15) + 1);
            return (indexed, MakePalette(RED, GREEN), w, h);
        }

        // =================================================================
        // Harness
        // =================================================================

        static void WithRom(Action<ROM, string> body)
        {
            var savedRom = CoreState.ROM;
            var savedSvc = CoreState.ImageService;
            string dir = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "romanime_mf_" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                var rom = new ROM();
                byte[] data = new byte[0x1000000]; // 16 MB
                rom.LoadLow("synth.gba", data, "BE8E01");
                CoreState.ROM = rom;
                CoreState.ImageService = new StubImageService();
                body(rom, dir);
            }
            finally
            {
                CoreState.ROM = savedRom; CoreState.ImageService = savedSvc;
                TryDeleteDir(dir);
            }
        }

        // ---- entry resolvers ----

        static RomAnimeCore.RomAnimeEntry ResolveFixed(ROM rom, int count, string option)
        {
            string[] fields =
            {
                WIDTH_TILES.ToString(), option, U.ToHexString((uint)count),
                U.ToHexString(TSA_PTR_SLOT), U.ToHexString(IMAGE_PTR_SLOT), U.ToHexString(PAL_PTR_SLOT),
                "efxFixed",
            };
            return RomAnimeCore.Resolve(rom, 0x1000, fields);
        }

        static RomAnimeCore.RomAnimeEntry ResolveFrameTable(ROM rom, string option)
        {
            string[] fields =
            {
                WIDTH_TILES.ToString(), option, U.ToHexString(FRAME_PTR_SLOT),
                U.ToHexString(TSA_PTR_SLOT), U.ToHexString(IMAGE_PTR_SLOT), U.ToHexString(PAL_PTR_SLOT),
                "efxTable",
            };
            return RomAnimeCore.Resolve(rom, 0x2000, fields);
        }

        // ---- planters ----

        // FIXEDCOUNT: FramePointer == count (< 0x100). When paletteAnime, TSA/IMAGE lists
        // are single-entry (one shared image) and the PALETTE list has `count` entries.
        static void PlantFixedCount(ROM rom, int count, bool paletteAnime)
        {
            PlantFrameData(rom);

            int tsaImgCount = paletteAnime ? 1 : count;
            // TSA + IMAGE pointer lists.
            WriteList(rom, TSA_LIST, Pick(TSA_DATA_0, TSA_DATA_1, tsaImgCount));
            WriteList(rom, IMAGE_LIST, Pick(IMAGE_DATA_0, IMAGE_DATA_1, tsaImgCount));
            WriteList(rom, PAL_LIST, Pick(PAL_DATA_0, PAL_DATA_1, count));

            rom.write_p32(TSA_PTR_SLOT, TSA_LIST);
            rom.write_p32(IMAGE_PTR_SLOT, IMAGE_LIST);
            rom.write_p32(PAL_PTR_SLOT, PAL_LIST);
            // FRAME pointer slot is NOT used for FIXEDCOUNT (FramePointer is the count).
        }

        // frame-table: a {u16 id,u16 wait} table {0,1},{1,1},0xFFFF. When commonPalette,
        // PALETTE pointer slot dereferences to a single 32-byte block (count 1) and the
        // TSA/IMAGE lists have 2 entries.
        static void PlantFrameTable(ROM rom, bool commonPalette)
        {
            PlantFrameData(rom);

            // The frame table: ids 0 and 1, each wait 1, then terminator.
            rom.write_u16(FRAME_TABLE + 0, 0); rom.write_u16(FRAME_TABLE + 2, 1);
            rom.write_u16(FRAME_TABLE + 4, 1); rom.write_u16(FRAME_TABLE + 6, 1);
            rom.write_u16(FRAME_TABLE + 8, 0xFFFF); rom.write_u16(FRAME_TABLE + 10, 0);
            rom.write_p32(FRAME_PTR_SLOT, FRAME_TABLE);

            WriteList(rom, TSA_LIST, new[] { TSA_DATA_0, TSA_DATA_1 });
            WriteList(rom, IMAGE_LIST, new[] { IMAGE_DATA_0, IMAGE_DATA_1 });
            rom.write_p32(TSA_PTR_SLOT, TSA_LIST);
            rom.write_p32(IMAGE_PTR_SLOT, IMAGE_LIST);

            if (commonPalette)
            {
                // PAL pointer slot -> directly the 32-byte block (no intermediate list).
                rom.write_p32(PAL_PTR_SLOT, PAL_DATA_0);
            }
            else
            {
                WriteList(rom, PAL_LIST, new[] { PAL_DATA_0, PAL_DATA_1 });
                rom.write_p32(PAL_PTR_SLOT, PAL_LIST);
            }
        }

        // Two frames of distinct TSA/IMAGE (LZ77) + RAW palettes.
        static void PlantFrameData(ROM rom)
        {
            PlantBytes(rom, TSA_DATA_0, LZ77.compress(MakeTsa(0)));
            PlantBytes(rom, TSA_DATA_1, LZ77.compress(MakeTsa(1)));
            PlantBytes(rom, IMAGE_DATA_0, LZ77.compress(MakeImage(1)));
            PlantBytes(rom, IMAGE_DATA_1, LZ77.compress(MakeImage(2)));
            PlantBytes(rom, PAL_DATA_0, MakePalette(RED, GREEN));
            PlantBytes(rom, PAL_DATA_1, MakePalette(BLUE, RED));
        }

        static uint[] Pick(uint a, uint b, int n)
        {
            var arr = new uint[n];
            for (int i = 0; i < n; i++) arr[i] = (i % 2 == 0) ? a : b;
            return arr;
        }

        // Write a per-frame pointer LIST at `listAddr` then a 0 terminator.
        static void WriteList(ROM rom, uint listAddr, uint[] dataAddrs)
        {
            for (int i = 0; i < dataAddrs.Length; i++)
                rom.write_p32(listAddr + (uint)(i * 4), dataAddrs[i]);
            rom.write_u32(listAddr + (uint)(dataAddrs.Length * 4), 0);
        }

        // ---- script/PNG helpers ----

        static string WriteScript(string dir, string name, (uint wait, string png)[] frames)
        {
            string path = Path.Combine(dir, name);
            var lines = new List<string>();
            foreach (var (wait, png) in frames) lines.Add(wait + " " + png);
            File.WriteAllLines(path, lines);
            return path;
        }

        // Write placeholder PNG files (one per name) so the synthetic Loader's
        // File.Exists check passes. The stub codec does no real PNG I/O; the Loader
        // ignores the bytes and returns deterministic synthetic frame data.
        static void MakePngFiles(string dir, params string[] names)
        {
            foreach (string n in names)
                File.WriteAllBytes(Path.Combine(dir, n), new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' });
        }

        // Read a `wait png` script and write a placeholder for every referenced PNG so
        // the synthetic Loader's File.Exists check passes on a round-trip import.
        static void MaterializeScriptPngs(string dir, string scriptPath)
        {
            foreach (string line in File.ReadAllLines(scriptPath))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("//")) continue;
                string[] sp = t.Replace("\t", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length < 2) continue;
                string png = sp[1];
                string full = Path.Combine(dir, png);
                if (!File.Exists(full))
                    File.WriteAllBytes(full, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' });
            }
        }

        static void AssertScriptHasFrames(string scriptPath, int min)
        {
            Assert.True(File.Exists(scriptPath));
            int data = 0;
            foreach (string line in File.ReadAllLines(scriptPath))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("//")) continue;
                data++;
            }
            Assert.True(data >= min, $"expected >= {min} data lines, got {data}");
        }

        static int WalkFrameTableCount(ROM rom, uint addr)
        {
            int n = 0;
            for (; addr + 4 <= rom.Data.Length && n < 1024; n++, addr += 4)
            {
                if (rom.u16(addr) == 0xFFFF) break;
            }
            return n;
        }

        static byte[] MakeTsa(int seed)
        {
            byte[] tsa = new byte[WIDTH_TILES * 2];
            for (int i = 0; i < WIDTH_TILES; i++)
                tsa[i * 2] = (byte)((i + seed) & 0x07);
            return tsa;
        }

        static byte[] MakeImage(int seed)
        {
            byte[] image = new byte[WIDTH_TILES * 32];
            for (int i = 0; i < image.Length; i++) image[i] = (byte)(((i + seed) % 15) + 1);
            return image;
        }

        static byte[] MakePalette(ushort c1, ushort c2)
        {
            byte[] pal = new byte[16 * 2];
            pal[1 * 2] = (byte)(c1 & 0xFF); pal[1 * 2 + 1] = (byte)(c1 >> 8);
            pal[2 * 2] = (byte)(c2 & 0xFF); pal[2 * 2 + 1] = (byte)(c2 >> 8);
            return pal;
        }

        static void PlantBytes(ROM rom, uint addr, byte[] bytes)
            => Array.Copy(bytes, 0, rom.Data, addr, bytes.Length);

        static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
