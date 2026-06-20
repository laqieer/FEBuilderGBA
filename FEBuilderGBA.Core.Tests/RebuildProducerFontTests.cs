using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the slice-2ab ROM-rebuild producer FontForm port
    /// (<see cref="RebuildProducerCore.EmitFont"/> and friends). The producer reproduces
    /// <c>FontForm.MakeAllDataLength</c> VERBATIM (NOT via the editor enumerators
    /// FontGlyphRenderCore/FontGlyphZHCore, which diverge): the non-ZH item/text font hash-chain walks
    /// (JP-SJIS / UTF8 / LAT1), the fixed-size status-font loop, and the ZH direct-reference codeB walk.
    /// Synthetic NULL-RomInfo ROMs drive the explicit-address seams; a versioned ROM exercises the
    /// is_multibyte/ZH gate.
    /// </summary>
    [Collection("SharedState")]
    public class RebuildProducerFontTests : IDisposable
    {
        readonly ROM _savedRom = CoreState.ROM;
        readonly ISystemTextEncoder _savedEncoder = CoreState.SystemTextEncoder;
        readonly TextEncodingEnum _savedEncoding = CoreState.TextEncoding;

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.SystemTextEncoder = _savedEncoder;
            CoreState.TextEncoding = _savedEncoding;
        }

        // ---- helpers -------------------------------------------------------

        static ROM CreateTestRom(int size = 0x8000)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
            return rom;
        }

        static uint Ptr(uint offset) => offset | 0x08000000u;

        static ROM MakeVersionedRom(string versionString, int size = 0x0200_0000)
        {
            var rom = new ROM();
            bool ok = rom.LoadLow("fake.gba", new byte[size], versionString);
            Assert.True(ok, "LoadLow did not recognize version string: " + versionString);
            return rom;
        }

        // A glyph hash-table entry: next u32 + moji2 + width + nazo3 + nazo4 + 64-byte bitmap.
        static void WriteGlyph(ROM rom, uint addr, uint nextPtr, byte moji2, byte width)
        {
            rom.write_u32(addr + 0, nextPtr);
            rom.write_u8(addr + 4, moji2);
            rom.write_u8(addr + 5, width);
            // +6/+7 + the 64-byte bitmap stay zero (irrelevant to the producer's addr/length/pointer/type).
        }

        // A minimal ISystemTextEncoder whose GetTBLEncodeDicLow() returns a fixed { char -> moji } map
        // (the ZH producer path consumes only this — Decode/Encode are unused by EmitFontZH).
        sealed class FakeTblEncoder : ISystemTextEncoder
        {
            readonly Dictionary<string, uint> _map;
            public FakeTblEncoder(Dictionary<string, uint> map) { _map = map; }
            public string Decode(byte[] str) => "";
            public string Decode(byte[] str, int start, int len) => "";
            public byte[] Encode(string str) => Array.Empty<byte>();
            public Dictionary<string, uint> GetTBLEncodeDicLow() => _map;
        }

        // ====================================================================
        // EmitFontInner — LAT1 (English) branch
        // ====================================================================

        [Fact]
        public void EmitFontInner_Lat1_EmitsTopPointerEntryAndPerGlyphFontEntries()
        {
            var rom = CreateTestRom();
            uint topaddress = 0x1000;
            // LAT1: bucket index = moji2; fontlist = topaddress + (moji2<<2). Seed bucket 0x41 ('A').
            uint moji2 = 0x41;
            uint fontlist = topaddress + (moji2 << 2);
            uint glyph = 0x2000;
            rom.write_u32(fontlist, Ptr(glyph));
            WriteGlyph(rom, glyph, nextPtr: 0, moji2: (byte)moji2, width: 7); // single-entry chain (next=0)

            var list = new List<Address>();
            RebuildProducerCore.EmitFontInnerAt(rom, list, isItemFont: true, topaddress,
                PRIORITY_CODE.LAT1, isMultibyte: false);

            // Top POINTER table entry: (topaddress, 4*0xff, NOT_FOUND, "FontItem", POINTER).
            Address ptrEntry = list.Single(a => a.DataType == Address.DataTypeEnum.POINTER);
            Assert.Equal(topaddress, ptrEntry.Addr);
            Assert.Equal(4u * 0xffu, ptrEntry.Length);
            Assert.Equal(U.NOT_FOUND, ptrEntry.Pointer);

            // One per-glyph FONT entry: (glyph, 8+64, before_pointer=fontlist, FONT).
            Address fontEntry = list.Single(a => a.DataType == Address.DataTypeEnum.FONT);
            Assert.Equal(glyph, fontEntry.Addr);
            Assert.Equal(8u + 64u, fontEntry.Length);
            Assert.Equal(fontlist, fontEntry.Pointer);
        }

        [Fact]
        public void EmitFontInner_Lat1_WalksMultiEntryChain_WithBeforePointerThreading()
        {
            var rom = CreateTestRom();
            uint topaddress = 0x1000;
            uint moji2 = 0x42;
            uint fontlist = topaddress + (moji2 << 2);
            uint g0 = 0x2000, g1 = 0x2100, g2 = 0x2200;
            rom.write_u32(fontlist, Ptr(g0));
            WriteGlyph(rom, g0, nextPtr: Ptr(g1), moji2: (byte)moji2, width: 5);
            WriteGlyph(rom, g1, nextPtr: Ptr(g2), moji2: (byte)moji2, width: 6);
            WriteGlyph(rom, g2, nextPtr: 0, moji2: (byte)moji2, width: 7);

            var list = new List<Address>();
            RebuildProducerCore.EmitFontInnerAt(rom, list, isItemFont: false, topaddress,
                PRIORITY_CODE.LAT1, isMultibyte: false);

            var fonts = list.Where(a => a.DataType == Address.DataTypeEnum.FONT).OrderBy(a => a.Addr).ToList();
            Assert.Equal(3, fonts.Count);
            // before_pointer threads: g0<-fontlist, g1<-g0, g2<-g1 (WF "before_pointer=p" each step).
            Assert.Equal(fontlist, fonts[0].Pointer);
            Assert.Equal(g0, fonts[1].Pointer);
            Assert.Equal(g1, fonts[2].Pointer);
            Assert.All(fonts, a => Assert.Equal(8u + 64u, a.Length));
        }

        // ====================================================================
        // EmitFontInner — JP-SJIS (multibyte) branch
        // ====================================================================

        [Fact]
        public void EmitFontInner_JpSjis_TopPointerLengthAndBucketBase()
        {
            var rom = CreateTestRom();
            uint topaddress = 0x1000;
            // JP: bucket index = moji1 (0x1f..0xff); fontlist = topaddress + (moji1<<2) - 0x100.
            uint moji1 = 0x82;
            uint fontlist = topaddress + (moji1 << 2) - 0x100;
            uint glyph = 0x2000;
            rom.write_u32(fontlist, Ptr(glyph));
            WriteGlyph(rom, glyph, nextPtr: 0, moji2: 0xA0, width: 8);

            var list = new List<Address>();
            RebuildProducerCore.EmitFontInnerAt(rom, list, isItemFont: true, topaddress,
                PRIORITY_CODE.SJIS, isMultibyte: true);

            // JP top POINTER length is 4*(0xff-0x1f) (NOT 4*0xff).
            Address ptrEntry = list.Single(a => a.DataType == Address.DataTypeEnum.POINTER);
            Assert.Equal(topaddress, ptrEntry.Addr);
            Assert.Equal(4u * (0xffu - 0x1fu), ptrEntry.Length);

            // The glyph is reached from the - 0x100 bucket base; before_pointer = that fontlist.
            Address fontEntry = list.Single(a => a.DataType == Address.DataTypeEnum.FONT);
            Assert.Equal(glyph, fontEntry.Addr);
            Assert.Equal(8u + 64u, fontEntry.Length);
            Assert.Equal(fontlist, fontEntry.Pointer);
        }

        // ====================================================================
        // EmitFontInner — UTF8 branch
        // ====================================================================

        [Fact]
        public void EmitFontInner_Utf8_TopPointerLengthAndBucketBase()
        {
            var rom = CreateTestRom();
            uint topaddress = 0x1000;
            // UTF8: bucket index = moji1 (0x00..0xff); fontlist = topaddress + (moji1<<2) (NO -0x100).
            uint moji1 = 0x10;
            uint fontlist = topaddress + (moji1 << 2);
            uint glyph = 0x2000;
            rom.write_u32(fontlist, Ptr(glyph));
            WriteGlyph(rom, glyph, nextPtr: 0, moji2: 0x20, width: 8);
            // UTF8 reads u8(p+6)/u8(p+7) for the name; leave them zero (name is informational only).

            var list = new List<Address>();
            RebuildProducerCore.EmitFontInnerAt(rom, list, isItemFont: false, topaddress,
                PRIORITY_CODE.UTF8, isMultibyte: false);

            Address ptrEntry = list.Single(a => a.DataType == Address.DataTypeEnum.POINTER);
            Assert.Equal(4u * 0xffu, ptrEntry.Length);

            Address fontEntry = list.Single(a => a.DataType == Address.DataTypeEnum.FONT);
            Assert.Equal(glyph, fontEntry.Addr);
            Assert.Equal(fontlist, fontEntry.Pointer); // bucket base = topaddress + (moji1<<2), no -0x100
        }

        // ====================================================================
        // EmitFontStatusFont — fixed-size pointer loop
        // ====================================================================

        [Fact]
        public void EmitFontStatusFont_EmitsOneFontPerSlot()
        {
            var rom = CreateTestRom();
            uint toppointer = 0x0400;
            uint table = 0x1000;
            rom.write_u32(toppointer, Ptr(table));
            uint f0 = 0x2000, f1 = 0x2100, f2 = 0x2200;
            rom.write_u32(table + 0, Ptr(f0));
            rom.write_u32(table + 4, Ptr(f1));
            rom.write_u32(table + 8, Ptr(f2));

            var list = new List<Address>();
            RebuildProducerCore.EmitFontStatusFont(rom, list, toppointer, count: 3);

            var fonts = list.Where(a => a.DataType == Address.DataTypeEnum.FONT).OrderBy(a => a.Addr).ToList();
            Assert.Equal(3, fonts.Count);
            // pointer field = the SLOT address (table + i*4), length 8+64.
            Assert.Equal(table + 0, fonts[0].Pointer);
            Assert.Equal(table + 4, fonts[1].Pointer);
            Assert.Equal(table + 8, fonts[2].Pointer);
            Assert.All(fonts, a => Assert.Equal(8u + 64u, a.Length));
        }

        [Fact]
        public void EmitFontStatusFont_SkipsUnsafeSlotsButContinues()
        {
            var rom = CreateTestRom();
            uint toppointer = 0x0400;
            uint table = 0x1000;
            rom.write_u32(toppointer, Ptr(table));
            rom.write_u32(table + 0, Ptr(0x2000));
            rom.write_u32(table + 4, 0);           // unsafe (0) -> WF "continue", NOT a stop
            rom.write_u32(table + 8, Ptr(0x2200));

            var list = new List<Address>();
            RebuildProducerCore.EmitFontStatusFont(rom, list, toppointer, count: 3);

            // The 0-slot is skipped but the loop continues to slot 2 (count-driven, not terminator).
            var fonts = list.Where(a => a.DataType == Address.DataTypeEnum.FONT).Select(a => a.Addr).ToList();
            Assert.Equal(2, fonts.Count);
            Assert.Contains(0x2000u, fonts);
            Assert.Contains(0x2200u, fonts);
        }

        [Fact]
        public void EmitFontStatusFont_UnsafeTopPointer_EmitsNothing()
        {
            var rom = CreateTestRom();
            var list = new List<Address>();
            // toppointer 0 is an unsafe offset -> immediate return (WF first guard).
            RebuildProducerCore.EmitFontStatusFont(rom, list, toppointer: 0, count: 10);
            Assert.Empty(list);
        }

        // ====================================================================
        // EmitFontZH — direct-reference codeB walk
        // ====================================================================

        [Fact]
        public void EmitFontZHInner_EmitsOneFontCnPerCodeBKey()
        {
            var rom = CreateTestRom(0x40000);
            uint topaddress = 0x1000;
            // Two codeB keys.
            var codeBMap = new Dictionary<uint, string> { { 0x00, "A" }, { 0x54, "B" } };

            var list = new List<Address>();
            RebuildProducerCore.EmitFontZHInnerAt(list, isItemFont: true, topaddress, codeBMap);

            var cn = list.Where(a => a.DataType == Address.DataTypeEnum.FONTCN).OrderBy(a => a.Addr).ToList();
            Assert.Equal(2, cn.Count);
            Assert.Equal(topaddress + 0x00, cn[0].Addr);
            Assert.Equal(topaddress + 0x54, cn[1].Addr);
            // fontSize = 4 + 40 = 44; pointer = NOT_FOUND (direct-ref, no slot).
            Assert.All(cn, a => Assert.Equal(4u + 40u, a.Length));
            Assert.All(cn, a => Assert.Equal(U.NOT_FOUND, a.Pointer));
        }

        // ====================================================================
        // EmitFont gate: is_multibyte && ZH_TBL -> ZH branch; else inner+status
        // ====================================================================

        [Fact]
        public void EmitFont_NonMultibyteFE8U_TakesInnerWalkNotZh()
        {
            var rom = MakeVersionedRom("BE8E01"); // FE8U, non-multibyte
            CoreState.ROM = rom;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
            CoreState.TextEncoding = TextEncodingEnum.ZH_TBL; // would only matter if is_multibyte

            var list = new List<Address>();
            RebuildProducerCore.EmitFont(rom, list);

            // FE8U is NOT multibyte, so the ZH gate is false even with ZH_TBL -> non-ZH path: there must be
            // a top POINTER table entry (the inner walk's hash-table pointer) and NO FONTCN entries.
            Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.POINTER);
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.FONTCN);
        }

        [Fact]
        public void EmitFont_MultibyteZhTbl_TakesZhBranch()
        {
            // FE8J is multibyte; with TextEncoding=ZH_TBL the ZH branch fires.
            var rom = MakeVersionedRom("BE8J01");
            CoreState.ROM = rom;
            // The producer maps codeA = (moji & 0xff) [low byte], codeB = (moji >> 8) & 0xff [high byte],
            // then CalcCodeBRaw = ((codeA-0x81)*0x80 + (codeB-0x80)) * 0x54. Pick moji with low byte >= 0x81
            // and high byte >= 0x80 so the offset stays small and positive -> topaddress + offset lands
            // in-bounds of the 32 MiB ROM (else AddAddress would skip the unsafe address and emit nothing).
            var map = new Dictionary<string, uint>
            {
                { "你", 0x81A1u }, // codeA=0xA1, codeB=0x81 -> small positive stride
                { "好", 0x81A2u },
            };
            CoreState.SystemTextEncoder = new FakeTblEncoder(map);
            CoreState.TextEncoding = TextEncodingEnum.ZH_TBL;

            var list = new List<Address>();
            RebuildProducerCore.EmitFont(rom, list);

            // ZH path: FONTCN entries (one per codeB key per item/text font = 2 keys * 2 fonts = 4),
            // and NO top hash POINTER table entry / FONT entries (those are the non-ZH path only).
            var cn = list.Where(a => a.DataType == Address.DataTypeEnum.FONTCN).ToList();
            Assert.NotEmpty(cn);
            Assert.All(cn, a => Assert.Equal(4u + 40u, a.Length));
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.FONT);
        }

        // ====================================================================
        // Robustness: near-EOF / zeroed
        // ====================================================================

        [Fact]
        public void EmitFontInner_NearEofChainPointer_DoesNotThrow()
        {
            var rom = CreateTestRom(0x2080);
            uint topaddress = 0x1000;
            uint moji2 = 0x41;
            uint fontlist = topaddress + (moji2 << 2);
            // Point the bucket at a glyph whose 8-byte header does NOT fully fit (Data.Length - 4).
            uint glyph = (uint)rom.Data.Length - 4;
            rom.write_u32(fontlist, Ptr(glyph));

            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitFontInnerAt(
                rom, list, isItemFont: true, topaddress, PRIORITY_CODE.LAT1, isMultibyte: false));
            Assert.Null(ex);
            // The glyph header doesn't fit -> FontStructFits stops the chain; no FONT entry emitted.
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.FONT);
        }

        [Fact]
        public void EmitFont_ZeroedRom_EmitsNoGlyphEntries()
        {
            // A versioned ROM with a valid RomInfo but all-zero data: the font pointer resolves to a
            // RomInfo address, but every bucket is 0 -> no glyphs. (The top POINTER entry only emits when
            // the resolved font pointer is a safe offset; for an all-zero FE8U it may or may not, but NO
            // FONT/FONTCN glyph entries can exist over zeroed buckets.)
            var rom = MakeVersionedRom("BE8E01");
            CoreState.ROM = rom;
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
            CoreState.TextEncoding = TextEncodingEnum.Auto;

            var list = new List<Address>();
            var ex = Record.Exception(() => RebuildProducerCore.EmitFont(rom, list));
            Assert.Null(ex);
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.FONT);
            Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.FONTCN);
        }
    }
}
