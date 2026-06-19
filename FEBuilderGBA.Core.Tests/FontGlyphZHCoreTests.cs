// SPDX-License-Identifier: GPL-3.0-or-later
// #1166 Core tests for FontGlyphZHCore — the Chinese (ZH) game-font glyph editor
// seam (direct-reference codeB array + render the 16x13 2bpp glyph + per-glyph
// PNG-equivalent import with validate-all-before-mutate + byte-identical restore).
//
// The ZH ROMs (FE6/FE7/FE8 multibyte) store fonts as a FLAT, directly-referenced
// array: addr = topaddress + CalcCodeB(moji) (no hash-chain). The synthetic ROM is
// a multibyte FE8J ROM that plants a 44-byte glyph struct at topaddress+codeB for a
// known character ('、', engine code 0x8181, codeB 0x54) and exercises the real
// enumerate/render/import code paths deterministically. EnumerateGlyphsZH needs the
// ZH TBL (config/translate/zh_tbl/FE8.tbl), so those tests set CoreState.BaseDirectory
// to the repo root and skip cleanly when it (or the .sln) is absent.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class FontGlyphZHCoreTests
    {
        const uint ROM_LEN = 0x01000000; // 16 MiB (FE8 ZH serif head 0x578020 is in-bounds)
        const uint MOJI_TEN = 0x8181;    // '、' (TBL file 8181 -> LE 0x8181), codeB = 0x54
        const string CHAR_TEN = "、";

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

        // Synthetic multibyte FE8J ROM with one serif glyph planted for '、'.
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF);
            for (uint i = 0; i < 0x600000; i++) data[i] = 0x00; // deterministic header/table region
            rom.LoadLow("synth.gba", data, "BE8J01"); // multibyte FE8J (version 8)

            PlantGlyph(rom, isItem: false, MOJI_TEN, width: 9, fillByte: 0x55);
            return rom;
        }

        // Plant a 44-byte ZH glyph struct at topaddress + CalcCodeB(moji).
        // struct: unk1(0xD) | width | height(0xD) | 0 | 40-byte bitmap.
        static uint PlantGlyph(ROM rom, bool isItem, uint moji, uint width, byte fillByte)
        {
            uint topaddress = FontGlyphZHCore.GetFontPointerZH(rom.RomInfo.version, isItem);
            uint codeB = FontGlyphZHCore.CalcCodeB(moji);
            uint addr = topaddress + codeB;

            U.write_u8(rom.Data, addr + 0, 0xD);
            U.write_u8(rom.Data, addr + 1, width);
            U.write_u8(rom.Data, addr + 2, 0xD);
            U.write_u8(rom.Data, addr + 3, 0);
            for (uint i = 0; i < 40; i++) rom.Data[addr + 4 + i] = fillByte;
            return addr;
        }

        static StubStateScope NewStub() => new StubStateScope();
        sealed class StubStateScope : IDisposable
        {
            readonly ROM _prevRom;
            public StubStateScope() { _prevRom = CoreState.ROM; }
            public void Dispose() { CoreState.ROM = _prevRom; }
        }

        // ---------------- codeB / pointer math ----------------

        [Fact]
        public void CalcCodeB_KnownChar_MatchesStride()
        {
            // '、' -> LE 0x8181: codeA=sjis2=0x81, codeB=sjis1=0x81.
            // ((0x81-0x81)*0x80 + (0x81-0x80))*0x54 = (0 + 1)*0x54 = 0x54.
            Assert.Equal(0x54u, FontGlyphZHCore.CalcCodeB(0x8181));
            // ' ' -> 0x8081 -> ((0)*0x80 + 0)*0x54 = 0.
            Assert.Equal(0u, FontGlyphZHCore.CalcCodeB(0x8081));
            // 2-byte code whose 1st byte (sjis1) is a control code (< 0x1f) has no font.
            // (A single-byte code, sjis1==0, is remapped to the 0x40 half-width row, so
            //  it does NOT take this path — use a real 2-byte control 1st-byte here.)
            Assert.Equal(U.NOT_FOUND, FontGlyphZHCore.CalcCodeB(0x0100));
        }

        [Fact]
        public void GetFontPointerZH_PerVersion()
        {
            Assert.Equal(0x58ef28u, FontGlyphZHCore.GetFontPointerZH(6, isItemFont: true));
            Assert.Equal(0x58ef54u, FontGlyphZHCore.GetFontPointerZH(6, isItemFont: false));
            Assert.Equal(0xbc0698u, FontGlyphZHCore.GetFontPointerZH(7, isItemFont: true));
            Assert.Equal(0xbc06c4u, FontGlyphZHCore.GetFontPointerZH(7, isItemFont: false));
            Assert.Equal(0x577ff4u, FontGlyphZHCore.GetFontPointerZH(8, isItemFont: true));
            Assert.Equal(0x578020u, FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false));
            Assert.Equal(0u, FontGlyphZHCore.GetFontPointerZH(5, isItemFont: false));
        }

        [Fact]
        public void IsZHRom_MultibyteFE8_True()
        {
            using var _ = NewStub();
            ROM rom = MakeRom();
            Assert.True(FontGlyphZHCore.IsZHRom(rom));
        }

        [Fact]
        public void IsZHRom_NullOrNonMultibyte_False()
        {
            Assert.False(FontGlyphZHCore.IsZHRom(null));

            using var _ = NewStub();
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01"); // FE8U is NOT multibyte
            Assert.False(FontGlyphZHCore.IsZHRom(rom));
        }

        [Fact]
        public void FindGlyphZH_ReturnsSlotAddress()
        {
            using var _ = NewStub();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            uint expected = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false) + 0x54;
            Assert.Equal(expected, FontGlyphZHCore.FindGlyphZH(rom, isItemFont: false, MOJI_TEN));
        }

        // ---------------- Render ----------------

        [Fact]
        public void RenderGlyphZH_ValidGlyph_Returns16x13NonNull()
        {
            using var _ = NewStub();
            using var svc = new ImageServiceScope();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            uint addr = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false) + 0x54;
            using IImage img = FontGlyphZHCore.RenderGlyphZH(rom, addr, isItemFont: false);
            Assert.NotNull(img);
            Assert.Equal(FontGlyphZHCore.GLYPH_W, img!.Width);  // 16
            Assert.Equal(FontGlyphZHCore.GLYPH_H, img.Height);  // 13
        }

        [Fact]
        public void RenderGlyphZH_BadAddr_ReturnsNull()
        {
            using var _ = NewStub();
            using var svc = new ImageServiceScope();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            // 4 bytes before EOF: the 44-byte struct does not fit -> null, no throw.
            using IImage img = FontGlyphZHCore.RenderGlyphZH(rom, ROM_LEN - 4, isItemFont: false);
            Assert.Null(img);
        }

        [Fact]
        public void RenderGlyphZH_NullRom_ReturnsNull()
        {
            using var svc = new ImageServiceScope();
            Assert.Null(FontGlyphZHCore.RenderGlyphZH(null, 0x578020 + 0x54, isItemFont: false));
        }

        [Fact]
        public void RenderGlyphZH_NullImageService_ReturnsNull()
        {
            using var _ = NewStub();
            var prevSvc = CoreState.ImageService;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                CoreState.ImageService = null;
                uint addr = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false) + 0x54;
                Assert.Null(FontGlyphZHCore.RenderGlyphZH(rom, addr, isItemFont: false));
            }
            finally { CoreState.ImageService = prevSvc; }
        }

        // ---------------- Pack / encode ----------------

        [Fact]
        public void PackGlyphZHBytes_RoundTripsThe2bppFormat()
        {
            // 16x13 index buffer where each pixel = (x % 4) so all 4 colors appear.
            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            for (int y = 0; y < FontGlyphZHCore.GLYPH_H; y++)
                for (int x = 0; x < FontGlyphZHCore.GLYPH_W; x++)
                    idx[y * FontGlyphZHCore.GLYPH_W + x] = (byte)(x & 0x03);

            byte[] packed = FontGlyphZHCore.PackGlyphZHBytes(idx, FontGlyphZHCore.GLYPH_W);
            Assert.NotNull(packed);
            Assert.Equal(40, packed!.Length);
            // First byte packs px (0,1,2,3) low-bits-first => 0b11_10_01_00 = 0xE4.
            Assert.Equal(0xE4, packed[0]);
        }

        [Fact]
        public void PackGlyphZHBytes_OutOfRangeIndex_ReturnsNull()
        {
            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            idx[5] = 7; // > 3 — outside the 4-color ZH font palette
            Assert.Null(FontGlyphZHCore.PackGlyphZHBytes(idx, FontGlyphZHCore.GLYPH_W));
        }

        [Theory]
        [InlineData(13)] // typical CJK width — NOT a multiple of 4 (the ZH continuous-stream case)
        [InlineData(16)] // full width
        [InlineData(9)]  // narrow
        public void RenderPack_RoundTrips_ContinuousStream(int renderWidth)
        {
            using var _ = NewStub();
            using var svc = new ImageServiceScope();

            // A 16x13 index buffer; only the first `renderWidth` columns carry the
            // glyph (the rest is background) — that's what the render produces.
            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            int seed = 1;
            for (int y = 0; y < FontGlyphZHCore.GLYPH_H; y++)
                for (int x = 0; x < FontGlyphZHCore.GLYPH_W; x++)
                {
                    if (x < renderWidth)
                    {
                        seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                        idx[y * FontGlyphZHCore.GLYPH_W + x] = (byte)(seed & 0x03);
                    }
                }

            byte[] packed = FontGlyphZHCore.PackGlyphZHBytes(idx, renderWidth);
            Assert.NotNull(packed);
            Assert.Equal(40, packed!.Length);

            // Render the packed bytes back at the same width; the visible pixels must
            // match the source exactly (continuous 2bpp stream wraps at renderWidth).
            using var img = FontGlyphZHCore.RenderGlyphZHBytes(packed, isItemFont: false, renderWidth);
            Assert.NotNull(img);
            byte[] rgba = img!.GetPixelData();

            // Re-derive the palette index from the rendered RGBA (index 0 = transparent).
            for (int y = 0; y < FontGlyphZHCore.GLYPH_H; y++)
            {
                for (int x = 0; x < renderWidth; x++)
                {
                    // Stop once we've consumed all 40 packed bytes (160 px).
                    int linear = y * renderWidth + x;
                    if (linear >= 40 * 4) break;

                    int po = ((y * FontGlyphZHCore.GLYPH_W) + x) * 4;
                    byte a = rgba[po + 3];
                    int srcIdx = idx[y * FontGlyphZHCore.GLYPH_W + x];
                    if (srcIdx == 0)
                        Assert.Equal(0, a); // background -> transparent
                    else
                        Assert.Equal(255, a); // foreground -> opaque
                }
            }
        }

        // ---------------- Import ----------------

        [Fact]
        public void ImportGlyphZH_ExistingGlyph_InPlaceUpdate_RoundTrips()
        {
            using var _ = NewStub();
            ROM rom = MakeRom();
            CoreState.ROM = rom;

            // Fill all 16 columns so the derived width is 16 (serif renderWidth == 16),
            // matching the width ImportGlyphZH packs at.
            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            for (int y = 0; y < FontGlyphZHCore.GLYPH_H; y++)
                for (int x = 0; x < FontGlyphZHCore.GLYPH_W; x++)
                    idx[y * FontGlyphZHCore.GLYPH_W + x] = (byte)(((x + y) & 0x02) | 0x01); // 1 or 3, never 0
            byte[] expected = FontGlyphZHCore.PackGlyphZHBytes(idx, FontGlyphZHCore.GLYPH_W);
            Assert.NotNull(expected);

            string err = FontGlyphZHCore.ImportGlyphZH(rom, isItemFont: false, MOJI_TEN, idx,
                FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H);
            Assert.Equal("", err);

            uint addr = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false) + 0x54;
            byte[] written = rom.getBinaryData(addr + 4, 40);
            Assert.Equal(expected, written);
            // header stays {0xD, width, 0xD, 0x00}
            Assert.Equal(0xDu, rom.u8(addr + 0));
            Assert.Equal(0xDu, rom.u8(addr + 2));
            Assert.Equal(0x0u, rom.u8(addr + 3));
        }

        [Fact]
        public void ImportGlyphZH_ExplicitWidth_SerifWritesVerbatim()
        {
            using var _ = NewStub();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            for (int i = 0; i < idx.Length; i++) idx[i] = (byte)(i & 0x03);

            // Serif: stored width == explicit width (no -1 quirk).
            string err = FontGlyphZHCore.ImportGlyphZH(rom, isItemFont: false, MOJI_TEN, idx,
                FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H, explicitWidth: 7);
            Assert.Equal("", err);
            uint addr = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false) + 0x54;
            Assert.Equal(7u, rom.u8(addr + 1));
        }

        [Fact]
        public void ImportGlyphZH_ExplicitWidth_ItemSubtractsOne()
        {
            using var _ = NewStub();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            // Plant an item-font glyph slot too.
            PlantGlyph(rom, isItem: true, MOJI_TEN, width: 9, fillByte: 0x00);

            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            for (int i = 0; i < idx.Length; i++) idx[i] = (byte)(i & 0x03);

            // Item: stored width = explicit - 1 (the WF "+1" quirk).
            string err = FontGlyphZHCore.ImportGlyphZH(rom, isItemFont: true, MOJI_TEN, idx,
                FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H, explicitWidth: 7);
            Assert.Equal("", err);
            uint addr = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: true) + 0x54;
            Assert.Equal(6u, rom.u8(addr + 1));
        }

        [Fact]
        public void ImportGlyphZH_WrongDims_NoMutation()
        {
            using var _ = NewStub();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            byte[] snap = (byte[])rom.Data.Clone();

            byte[] idx = new byte[16 * 16]; // wrong size (height must be 13)
            string err = FontGlyphZHCore.ImportGlyphZH(rom, isItemFont: false, MOJI_TEN, idx, 16, 16);
            Assert.NotEqual("", err);
            Assert.Equal(snap, rom.Data);
        }

        [Fact]
        public void ImportGlyphZH_OutOfRangeColor_NoMutation()
        {
            using var _ = NewStub();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            byte[] snap = (byte[])rom.Data.Clone();

            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            idx[0] = 9; // outside 0..3
            string err = FontGlyphZHCore.ImportGlyphZH(rom, isItemFont: false, MOJI_TEN, idx,
                FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H);
            Assert.NotEqual("", err);
            Assert.Equal(snap, rom.Data);
        }

        [Fact]
        public void ImportGlyphZH_NonZHRom_ReturnsError_NoMutation()
        {
            using var _ = NewStub();
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01"); // FE8U not multibyte
            CoreState.ROM = rom;
            byte[] snap = (byte[])rom.Data.Clone();

            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            string err = FontGlyphZHCore.ImportGlyphZH(rom, isItemFont: false, MOJI_TEN, idx,
                FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H);
            Assert.NotEqual("", err);
            Assert.Equal(snap, rom.Data);
        }

        [Fact]
        public void ImportGlyphZH_NullRom_ReturnsError_NoThrow()
        {
            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            string err = FontGlyphZHCore.ImportGlyphZH(null, isItemFont: false, MOJI_TEN, idx,
                FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H);
            Assert.NotEqual("", err);
        }

        [Fact]
        public void ImportGlyphZH_ControlCode_CannotRegister_NoMutation()
        {
            using var _ = NewStub();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            byte[] snap = (byte[])rom.Data.Clone();

            // moji 0x0100: 1st byte (sjis1) = 0x01 < 0x1f -> CalcCodeB = NOT_FOUND.
            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            string err = FontGlyphZHCore.ImportGlyphZH(rom, isItemFont: false, 0x0100, idx,
                FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H);
            Assert.NotEqual("", err);
            Assert.Equal(snap, rom.Data);
        }

        // ---- Undo round-trip (mirror Undo.RollbackROM in reverse) ----

        static Undo.UndoData NewUd(ROM rom) => new Undo.UndoData
        {
            time = System.DateTime.Now,
            name = "fontzh-test",
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
        public void ImportGlyphZH_UndoRestoresByteIdentical()
        {
            using var _ = NewStub();
            ROM rom = MakeRom();
            CoreState.ROM = rom;
            byte[] before = (byte[])rom.Data.Clone();

            byte[] idx = new byte[FontGlyphZHCore.GLYPH_W * FontGlyphZHCore.GLYPH_H];
            for (int i = 0; i < idx.Length; i++) idx[i] = (byte)((i * 3) & 0x03);

            var ud = NewUd(rom);
            using (ROM.BeginUndoScope(ud))
            {
                Assert.Equal("", FontGlyphZHCore.ImportGlyphZH(rom, isItemFont: false, MOJI_TEN, idx,
                    FontGlyphZHCore.GLYPH_W, FontGlyphZHCore.GLYPH_H));
            }
            Assert.NotEqual(before, rom.Data); // import mutated the ROM
            Assert.NotEmpty(ud.list);          // and recorded undo positions

            RollbackReplay(rom, ud);
            Assert.Equal(before, rom.Data);    // undo restored it byte-identical
        }

        // ---------------- Enumeration (needs the ZH TBL; skips when absent) ----------------

        static string? FindRepoRoot()
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? dir = System.IO.Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }

        [Fact]
        public void EnumerateGlyphsZH_FindsPlantedGlyph_WithDecodedName()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // skip
            string tbl = System.IO.Path.Combine(repoRoot, "config", "translate", "zh_tbl", "FE8.tbl");
            if (!System.IO.File.Exists(tbl)) return; // skip when the TBL is absent

            using var _ = NewStub();
            var prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                var list = FontGlyphZHCore.EnumerateGlyphsZH(rom, isItemFont: false);
                Assert.NotEmpty(list);

                uint addr = FontGlyphZHCore.GetFontPointerZH(8, isItemFont: false) + 0x54;
                var hit = list.Find(g => g.Addr == addr);
                Assert.NotNull(hit);
                Assert.Equal(9, hit!.Width);
                Assert.Equal(CHAR_TEN, hit.Name); // '、' decoded from the ZH TBL
                Assert.Equal(MOJI_TEN, hit.Moji); // moji carried straight from the TBL map

                // The TBL-carried Moji must equal the per-char re-encode it replaced
                // (EncodeMoji == the old MojiFromChar path) — the #1166 perf fix builds
                // the encoder ONCE per enumeration instead of once per glyph, with
                // identical results.
                Assert.Equal(FontGlyphZHCore.EncodeMoji(rom, hit.Name), hit.Moji);
            }
            finally { CoreState.BaseDirectory = prevBase; }
        }

        [Fact]
        public void EnumerateGlyphsZH_MojiRoundTripsToCorrectCodeB()
        {
            // The Moji carried in each entry MUST re-derive the SAME slot address it was
            // enumerated at — proving the once-built-encoder optimization keeps the moji
            // usable for re-import (ImportGlyphZH(moji) -> CalcCodeB(moji) -> same slot).
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // skip
            string tbl = System.IO.Path.Combine(repoRoot, "config", "translate", "zh_tbl", "FE8.tbl");
            if (!System.IO.File.Exists(tbl)) return; // skip when the TBL is absent

            using var _ = NewStub();
            var prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                var list = FontGlyphZHCore.EnumerateGlyphsZH(rom, isItemFont: false);
                Assert.NotEmpty(list);
                foreach (var g in list)
                    Assert.Equal(g.Addr, FontGlyphZHCore.FindGlyphZH(rom, isItemFont: false, g.Moji));
            }
            finally { CoreState.BaseDirectory = prevBase; }
        }

        [Fact]
        public void EnumerateGlyphsZH_NonZHRom_ReturnsEmpty()
        {
            using var _ = NewStub();
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0x00);
            rom.LoadLow("synth.gba", data, "BE8E01"); // FE8U not multibyte
            CoreState.ROM = rom;
            Assert.Empty(FontGlyphZHCore.EnumerateGlyphsZH(rom, isItemFont: false));
        }

        [Fact]
        public void EnumerateGlyphsZH_NullRom_ReturnsEmpty()
        {
            Assert.Empty(FontGlyphZHCore.EnumerateGlyphsZH(null, isItemFont: false));
        }

        // ---------------- Real CN ROM (skipped when absent) ----------------

        static string? FindRom(string romName)
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return null;
            string path = System.IO.Path.Combine(repoRoot, "roms", romName);
            return System.IO.File.Exists(path) ? path : null;
        }

        [Fact]
        public void EnumerateAndRender_RealCN_FE8()
        {
            string? path = FindRom("火焰之纹章 - 圣魔之光石.gba");
            if (path == null) return; // skip when CN ROM absent

            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            using var _ = NewStub();
            using var svc = new ImageServiceScope();
            var prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                var rom = new ROM();
                if (!rom.Load(path, out string _)) return;
                CoreState.ROM = rom;
                if (!FontGlyphZHCore.IsZHRom(rom)) return; // not a recognized CN build

                var serif = FontGlyphZHCore.EnumerateGlyphsZH(rom, isItemFont: false);
                var item = FontGlyphZHCore.EnumerateGlyphsZH(rom, isItemFont: true);
                Assert.NotEmpty(serif);
                Assert.NotEmpty(item);

                using IImage img = FontGlyphZHCore.RenderGlyphZH(rom, serif[0].Addr, isItemFont: false);
                Assert.NotNull(img);
                Assert.Equal(16, img!.Width);
                Assert.Equal(13, img.Height);
            }
            finally { CoreState.BaseDirectory = prevBase; }
        }
    }
}
