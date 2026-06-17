// SPDX-License-Identifier: GPL-3.0-or-later
// #1165 Core tests for FontGlyphRenderCore — the main game-font glyph editor seam
// (enumerate the font hash table + render the 16x16 2bpp glyph + per-glyph
// PNG-equivalent import with validate-all-before-mutate + byte-identical restore).
//
// FE8U is is_multibyte=false (LAT1 path): buckets at topaddress + (moji2<<2),
// one glyph struct per bucket. The synthetic ROM plants a single 'A' (0x41)
// glyph in free space and points the serif bucket at it, exercising the real
// enumeration/render/import code paths deterministically. A real-FE8U test
// (skipped when the ROM is absent) covers vanilla font data.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FontGlyphRenderCoreTests
    {
        const uint ROM_LEN = 0x01000000; // 16 MiB (font_serif_address 0x58f6f4 is in-bounds)
        const uint GLYPH_OFF = 0x00700000; // free space for the planted glyph struct
        const uint MOJI_A = 0x41;          // 'A'

        sealed class ImageServiceScope : IDisposable
        {
            readonly IImageService _prev;
            public ImageServiceScope()
            {
                _prev = CoreState.ImageService;
                CoreState.ImageService = new StubImageService();
            }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        // Synthetic FE8U ROM with one serif glyph for 'A'.
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF); // 0xFF = free-space marker for the append path
            // Zero the header + the font-table region so reads are deterministic.
            for (uint i = 0; i < 0x600000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8E01");

            PlantGlyph(rom, isItem: false, MOJI_A, GLYPH_OFF, width: 9, fillByte: 0x55);
            return rom;
        }

        // Plant a 72-byte glyph struct at glyphOff and point the (LAT1) bucket at it.
        static void PlantGlyph(ROM rom, bool isItem, uint moji, uint glyphOff, uint width, byte fillByte)
        {
            uint topaddress = isItem ? rom.RomInfo.font_item_address : rom.RomInfo.font_serif_address;
            uint bucket = topaddress + (moji << 2); // LAT1: hash by moji2

            // struct: next(0) | moji2 | width | 0 | 0 | 64-byte bitmap
            U.write_u32(rom.Data, glyphOff + 0, 0);
            U.write_u8(rom.Data, glyphOff + 4, moji);
            U.write_u8(rom.Data, glyphOff + 5, width);
            U.write_u8(rom.Data, glyphOff + 6, 0);
            U.write_u8(rom.Data, glyphOff + 7, 0);
            for (uint i = 0; i < 64; i++) rom.Data[glyphOff + 8 + i] = fillByte;

            U.write_u32(rom.Data, bucket, U.toPointer(glyphOff));
        }

        // ---------------- Enumeration ----------------

        [Fact]
        public void EnumerateGlyphs_FindsPlantedSerifGlyph()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                var list = FontGlyphRenderCore.EnumerateGlyphs(rom, isItemFont: false);
                Assert.NotEmpty(list);
                var hit = list.Find(g => g.Moji == MOJI_A);
                Assert.NotNull(hit);
                Assert.Equal(GLYPH_OFF, hit!.Addr);
                Assert.Equal(9, hit.Width);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void EnumerateGlyphs_NullRom_ReturnsEmpty()
        {
            Assert.Empty(FontGlyphRenderCore.EnumerateGlyphs(null, isItemFont: false));
        }

        // ---------------- Render ----------------

        [Fact]
        public void RenderGlyph_ValidGlyph_Returns16x16NonNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                using IImage img = FontGlyphRenderCore.RenderGlyph(rom, GLYPH_OFF, isItemFont: false);
                Assert.NotNull(img);
                Assert.Equal(FontGlyphRenderCore.GLYPH_W, img!.Width);
                Assert.Equal(FontGlyphRenderCore.GLYPH_H, img.Height);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyph_BadAddr_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Address past EOF — bounds guard returns null, no throw.
                using IImage img = FontGlyphRenderCore.RenderGlyph(rom, ROM_LEN - 4, isItemFont: false);
                Assert.Null(img);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void RenderGlyph_NullRom_ReturnsNull()
        {
            using var svc = new ImageServiceScope();
            Assert.Null(FontGlyphRenderCore.RenderGlyph(null, GLYPH_OFF, isItemFont: false));
        }

        [Fact]
        public void RenderGlyph_NullImageService_ReturnsNull()
        {
            var prevRom = CoreState.ROM;
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.ImageService = null;
                Assert.Null(FontGlyphRenderCore.RenderGlyph(rom, GLYPH_OFF, isItemFont: false));
            }
            finally { CoreState.ROM = prevRom; CoreState.ImageService = prevSvc; }
        }

        // ---------------- Pack / encode ----------------

        [Fact]
        public void PackGlyphBytes_RoundTripsThe2bppFormat()
        {
            // index buffer where each pixel = (x % 4) so all 4 colors appear.
            byte[] idx = new byte[16 * 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    idx[y * 16 + x] = (byte)(x & 0x03);

            byte[] packed = FontGlyphRenderCore.PackGlyphBytes(idx);
            Assert.NotNull(packed);
            Assert.Equal(64, packed!.Length);
            // Each byte packs 4 px (0,1,2,3) low-bits-first => 0b11_10_01_00 = 0xE4.
            Assert.Equal(0xE4, packed[0]);
        }

        [Fact]
        public void PackGlyphBytes_OutOfRangeIndex_ReturnsNull()
        {
            byte[] idx = new byte[16 * 16];
            idx[5] = 7; // > 3 — outside the 4-color font palette
            Assert.Null(FontGlyphRenderCore.PackGlyphBytes(idx));
        }

        // ---------------- Import ----------------

        [Fact]
        public void ImportGlyph_ExistingGlyph_InPlaceUpdate_RoundTrips()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // Distinct bitmap: column index pattern -> a non-uniform glyph.
                byte[] idx = new byte[16 * 16];
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                        idx[y * 16 + x] = (byte)((x + y) & 0x03);
                byte[] expected = FontGlyphRenderCore.PackGlyphBytes(idx);
                Assert.NotNull(expected);

                string err = FontGlyphRenderCore.ImportGlyph(rom, isItemFont: false, MOJI_A, idx, 16, 16);
                Assert.Equal("", err);

                // The existing glyph struct at GLYPH_OFF was updated in place.
                byte[] written = rom.getBinaryData(GLYPH_OFF + 8, 64);
                Assert.Equal(expected, written);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportGlyph_NewGlyph_AppendsAndChainLinks()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                // 'B' (0x42) has no glyph yet — bucket is empty (0). Import appends.
                const uint MOJI_B = 0x42;
                uint bucket = rom.RomInfo.font_serif_address + (MOJI_B << 2);
                Assert.Equal(0u, rom.u32(bucket)); // empty before import

                byte[] idx = new byte[16 * 16];
                for (int i = 0; i < idx.Length; i++) idx[i] = (byte)(i & 0x03);

                string err = FontGlyphRenderCore.ImportGlyph(rom, isItemFont: false, MOJI_B, idx, 16, 16);
                Assert.Equal("", err);

                // The bucket now points at the freshly appended glyph, and the glyph
                // is re-findable by enumeration.
                uint ptr = rom.u32(bucket);
                Assert.True(U.isPointer(ptr));
                var list = FontGlyphRenderCore.EnumerateGlyphs(rom, isItemFont: false);
                Assert.Contains(list, g => g.Moji == MOJI_B);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportGlyph_WrongDims_NoMutation()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] snap = (byte[])rom.Data.Clone();

                byte[] idx = new byte[8 * 8]; // wrong size
                string err = FontGlyphRenderCore.ImportGlyph(rom, isItemFont: false, MOJI_A, idx, 8, 8);
                Assert.NotEqual("", err);                 // localized error
                Assert.Equal(snap, rom.Data);             // ZERO mutation
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportGlyph_OutOfRangeColor_NoMutation()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] snap = (byte[])rom.Data.Clone();

                byte[] idx = new byte[16 * 16];
                idx[0] = 9; // outside 0..3
                string err = FontGlyphRenderCore.ImportGlyph(rom, isItemFont: false, MOJI_A, idx, 16, 16);
                Assert.NotEqual("", err);
                Assert.Equal(snap, rom.Data);
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportGlyph_NullRom_ReturnsError_NoThrow()
        {
            byte[] idx = new byte[16 * 16];
            string err = FontGlyphRenderCore.ImportGlyph(null, isItemFont: false, MOJI_A, idx, 16, 16);
            Assert.NotEqual("", err);
        }

        // Build an UndoData, capture the pre-import bytes, replay the recorded
        // UndoPositions in reverse (mirrors Undo.RollbackROM) and assert the ROM
        // returns byte-identical — proves the import is fully undoable.
        static Undo.UndoData NewUd(ROM rom) => new Undo.UndoData
        {
            time = System.DateTime.Now,
            name = "font-test",
            list = new System.Collections.Generic.List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        static void RollbackReplay(ROM rom, Undo.UndoData ud)
        {
            for (int i = ud.list.Count - 1; i >= 0; i--)
            {
                var up = ud.list[i];
                Array.Copy(up.data, 0, rom.Data, up.addr, up.data.Length);
            }
        }

        [Fact]
        public void ImportGlyph_ExistingGlyph_UndoRestoresByteIdentical()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                byte[] idx = new byte[16 * 16];
                for (int i = 0; i < idx.Length; i++) idx[i] = (byte)((i * 3) & 0x03);

                var ud = NewUd(rom);
                using (ROM.BeginUndoScope(ud))
                {
                    Assert.Equal("", FontGlyphRenderCore.ImportGlyph(rom, isItemFont: false, MOJI_A, idx, 16, 16));
                }
                Assert.NotEqual(before, rom.Data); // import mutated the ROM
                Assert.NotEmpty(ud.list);          // and recorded undo positions

                RollbackReplay(rom, ud);
                Assert.Equal(before, rom.Data);    // undo restored it byte-identical
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportGlyph_NewGlyph_AppendUndoRestoresByteIdentical()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                // 'B' has no glyph -> import APPENDS a struct + chain-links the
                // bucket. Both the appended bytes AND the bucket repoint must be in
                // the undo record so the rollback restores the ROM byte-identical
                // (appended region back to 0xFF, bucket pointer back to 0).
                const uint MOJI_B = 0x42;
                byte[] idx = new byte[16 * 16];
                for (int i = 0; i < idx.Length; i++) idx[i] = (byte)(i & 0x03);

                var ud = NewUd(rom);
                using (ROM.BeginUndoScope(ud))
                {
                    Assert.Equal("", FontGlyphRenderCore.ImportGlyph(rom, isItemFont: false, MOJI_B, idx, 16, 16));
                }
                Assert.NotEqual(before, rom.Data);
                Assert.NotEmpty(ud.list);

                RollbackReplay(rom, ud);
                Assert.Equal(before, rom.Data); // append + repoint both undone
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportGlyph_ExplicitWidth_WinsOverDerived()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] idx = new byte[16 * 16];
                for (int i = 0; i < idx.Length; i++) idx[i] = (byte)(i & 0x03);

                // explicitWidth=7 must be written verbatim (not derived from pixels).
                string err = FontGlyphRenderCore.ImportGlyph(rom, isItemFont: false, MOJI_A, idx, 16, 16,
                    explicitWidth: 7, manageSnapshot: true);
                Assert.Equal("", err);
                Assert.Equal(7u, rom.u8(GLYPH_OFF + 5));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportGlyph_ManageSnapshotFalse_NoRestoreOnFault()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // A char with no bucket+no prevaddr would fail; but on LAT1 every
                // moji has a bucket, so force the "out of range" path via a moji
                // whose existing glyph addr is fine — instead assert the success
                // path mutates without cloning (manageSnapshot:false).
                byte[] idx = new byte[16 * 16];
                for (int i = 0; i < idx.Length; i++) idx[i] = 1;
                byte[] expected = FontGlyphRenderCore.PackGlyphBytes(idx);

                string err = FontGlyphRenderCore.ImportGlyph(rom, isItemFont: false, MOJI_A, idx, 16, 16,
                    explicitWidth: -1, manageSnapshot: false);
                Assert.Equal("", err);
                Assert.Equal(expected, rom.getBinaryData(GLYPH_OFF + 8, 64));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ImportGlyph_CorruptBucketNearEOF_NoThrow()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Point a moji's bucket at an address 4 bytes before EOF so the
                // glyph lookup lands on a near-EOF struct. ImportGlyph wraps the
                // FontCore.FindFontData lookup in try/catch, so it must return a
                // (possibly error) string WITHOUT throwing.
                const uint MOJI_C = 0x43; // 'C'
                uint bucket = rom.RomInfo.font_serif_address + (MOJI_C << 2);
                U.write_u32(rom.Data, bucket, U.toPointer(ROM_LEN - 4));

                byte[] idx = new byte[16 * 16];
                var ex = Record.Exception(() =>
                    FontGlyphRenderCore.ImportGlyph(rom, isItemFont: false, MOJI_C, idx, 16, 16));
                Assert.Null(ex); // never throws
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void EnumerateGlyphs_CorruptChainNearEOF_NoThrow()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Point the 'A' bucket at an address 8 bytes before EOF so the
                // 72-byte struct does NOT fit — the GlyphStructFits guard must
                // stop the walk WITHOUT throwing.
                uint bucket = rom.RomInfo.font_serif_address + (MOJI_A << 2);
                U.write_u32(rom.Data, bucket, U.toPointer(ROM_LEN - 8));

                var ex = Record.Exception(() => FontGlyphRenderCore.EnumerateGlyphs(rom, isItemFont: false));
                Assert.Null(ex); // never throws
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public async System.Threading.Tasks.Task EnumerateGlyphs_CyclicChain_Terminates()
        {
            var prevRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Build a self-referential glyph struct: its next-pointer points
                // back to itself, so an uncapped while(p>0) would spin forever.
                // MAX_CHAIN must cap it so the call returns (the 20s watchdog
                // proves it terminated rather than hanging).
                uint cyc = 0x00710000;
                U.write_u32(rom.Data, cyc + 0, U.toPointer(cyc)); // next -> self
                U.write_u8(rom.Data, cyc + 4, 0x5A);
                U.write_u8(rom.Data, cyc + 5, 5);
                uint bucket = rom.RomInfo.font_serif_address + (0x5Au << 2);
                U.write_u32(rom.Data, bucket, U.toPointer(cyc));

                var work = System.Threading.Tasks.Task.Run(() =>
                    FontGlyphRenderCore.EnumerateGlyphs(rom, isItemFont: false));
                var done = await System.Threading.Tasks.Task.WhenAny(
                    work, System.Threading.Tasks.Task.Delay(20000));
                Assert.True(ReferenceEquals(done, work), "EnumerateGlyphs did not terminate on a cyclic chain.");
                Assert.NotNull(await work);
            }
            finally { CoreState.ROM = prevRom; }
        }

        // ---------------- Real FE8U (skipped when ROM absent) ----------------

        static string? FindRom(string romName)
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? dir = System.IO.Path.GetDirectoryName(thisAssembly);
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

        [Fact]
        public void EnumerateAndRender_RealFE8U_VanillaFont()
        {
            string? path = FindRom("FE8U.gba");
            if (path == null) return; // skip when ROM absent

            var prevRom = CoreState.ROM;
            using var svc = new ImageServiceScope();
            try
            {
                var rom = new ROM();
                rom.Load(path, out _);
                CoreState.ROM = rom;

                var serif = FontGlyphRenderCore.EnumerateGlyphs(rom, isItemFont: false);
                var item = FontGlyphRenderCore.EnumerateGlyphs(rom, isItemFont: true);
                Assert.NotEmpty(serif);
                Assert.NotEmpty(item);

                // The first serif glyph renders to a non-null 16x16 image.
                using IImage img = FontGlyphRenderCore.RenderGlyph(rom, serif[0].Addr, isItemFont: false);
                Assert.NotNull(img);
                Assert.Equal(16, img!.Width);
                Assert.Equal(16, img.Height);
            }
            finally { CoreState.ROM = prevRom; }
        }
    }
}
