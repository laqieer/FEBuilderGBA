// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for PointerToolAutoSearchCore (#1113) — the cross-platform port of
// WF PointerToolForm.AutoSearch cross-ROM auto-tracking.
//
// Three heuristics covered: (a) ASM-map symbol NAME search, (b) source<->target
// LDR-literal-pool-map symmetry, (c) direct grep + the AutoSearch orchestration.
// All buffers are synthetic and hand-laid; the LDR-map test documents its Thumb
// byte layout inline. Every test asserts the never-throw contract by exercising
// null / short / truncated buffers.
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class PointerToolAutoSearchCoreTests
    {
        // Build a non-multibyte FE8U synthetic ROM (for asmmap line loading).
        static ROM MakeFe8uRom()
        {
            var data = new byte[0x1000000];
            var rom = new ROM();
            Assert.True(rom.LoadLow("autosearch-fe8u.gba", data, "BE8E01"));
            return rom;
        }

        static AsmMapSymbolFile MapFromLines(ROM rom, params string[] lines)
        {
            var emptyRom = new ROM(); // version 0 -> empty map
            var map = new AsmMapSymbolFile(emptyRom);
            map.LoadFromLines(rom, lines);
            return map;
        }

        // Write a GBA pointer (0x08xxxxxx) at `off` in little-endian byte order.
        static void WritePtr(byte[] buf, int off, uint pointer)
        {
            buf[off + 0] = (byte)(pointer & 0xFF);
            buf[off + 1] = (byte)((pointer >> 8) & 0xFF);
            buf[off + 2] = (byte)((pointer >> 16) & 0xFF);
            buf[off + 3] = (byte)((pointer >> 24) & 0xFF);
        }

        static void WriteU16(byte[] buf, int off, ushort v)
        {
            buf[off + 0] = (byte)(v & 0xFF);
            buf[off + 1] = (byte)((v >> 8) & 0xFF);
        }

        // ---- 1: SearchName ---------------------------------------------------

        [Fact]
        public void SearchName_ResolvesSyntheticNameAndMissesUnknown()
        {
            var rom = MakeFe8uRom();
            var map = MapFromLines(rom, "08001000\tFoo", "08002000\tBar");

            Assert.Equal(0x08001000u, map.SearchName("Foo"));
            Assert.Equal(0x08002000u, map.SearchName("Bar"));
            Assert.Equal(U.NOT_FOUND, map.SearchName("Nope"));
            Assert.Equal(U.NOT_FOUND, map.SearchName(""));
            Assert.Equal(U.NOT_FOUND, map.SearchName(null));
        }

        // ---- 2: GetName ------------------------------------------------------

        [Fact]
        public void GetName_ExactPointerOrEmpty()
        {
            var rom = MakeFe8uRom();
            var map = MapFromLines(rom, "08001000\tFoo");

            Assert.Equal("Foo", map.GetName(0x08001000u));
            // Offset form normalizes to the pointer.
            Assert.Equal("Foo", map.GetName(0x00001000u));
            Assert.Equal("", map.GetName(0x08009999u));
        }

        [Fact]
        public void GetName_OddThumbPointer_ResolvesToSymbol()
        {
            // #1118: a real Thumb function pointer is odd (...01 / ...03). GetName
            // must clear the Thumb bit (ProgramAddrToPlain) before lookup so the
            // symbol at the even base still resolves.
            var rom = MakeFe8uRom();
            var map = MapFromLines(rom, "08001000\tFoo", "08002002\tBar");

            // Symbol at 0x08001000, queried with the Thumb bit set (...01).
            Assert.Equal("Foo", map.GetName(0x08001001u));
            // Symbol at a halfword-aligned base 0x08002002, queried as ...03.
            Assert.Equal("Bar", map.GetName(0x08002003u));
            // Sanity: the even-base queries still work.
            Assert.Equal("Foo", map.GetName(0x08001000u));
            Assert.Equal("Bar", map.GetName(0x08002002u));
        }

        // ---- 3 + 4: SearchAsmMapName -----------------------------------------

        [Fact]
        public void SearchAsmMapName_NameResolvesAcrossMapsWithReference()
        {
            var rom = MakeFe8uRom();
            var sourceMap = MapFromLines(rom, "08001000\tFoo");
            var targetMap = MapFromLines(rom, "08002000\tFoo");

            // Target buffer with a byte-swapped pointer to 0x08002000 at offset 0x300.
            var targetData = new byte[0x1000];
            WritePtr(targetData, 0x300, 0x08002000u);

            var r = PointerToolAutoSearchCore.SearchAsmMapName(0x08001000u, sourceMap, targetMap, targetData);
            Assert.True(r.Found);
            Assert.Equal("name", r.Hit);
            Assert.Equal("Foo", r.SymbolName);
            Assert.Equal(0x08002000u, r.DirectAddr);
            Assert.Equal(0x300u, r.DirectRef);
        }

        [Fact]
        public void SearchAsmMapName_EmptySourceName_NotFound()
        {
            var rom = MakeFe8uRom();
            var sourceMap = MapFromLines(rom, "08001000\tFoo");
            var targetMap = MapFromLines(rom, "08002000\tFoo");
            var targetData = new byte[0x1000];

            // Source pointer 0x09999999 has no symbol name -> NotFound.
            var r = PointerToolAutoSearchCore.SearchAsmMapName(0x09999999u, sourceMap, targetMap, targetData);
            Assert.False(r.Found);
            Assert.Equal("none", r.Hit);
        }

        [Fact]
        public void SearchAsmMapName_TargetNameExistsButNoRef_StillFound_RefNotFound()
        {
            var rom = MakeFe8uRom();
            var sourceMap = MapFromLines(rom, "08001000\tFoo");
            var targetMap = MapFromLines(rom, "08002000\tFoo");

            // Target buffer with NO reference to 0x08002000 anywhere.
            var targetData = new byte[0x1000];

            var r = PointerToolAutoSearchCore.SearchAsmMapName(0x08001000u, sourceMap, targetMap, targetData);
            // WF parity: name hit is still a found result even without a ref.
            Assert.True(r.Found);
            Assert.Equal("name", r.Hit);
            Assert.Equal(0x08002000u, r.DirectAddr);
            Assert.Equal(U.NOT_FOUND, r.DirectRef);
        }

        // ---- 5: LDR-map symmetry ---------------------------------------------
        //
        // Build the SMALLEST function MakeLDRMap recognizes and place it at TWO
        // different offsets in source vs target, both LDR-loading a (distinct)
        // data pointer. Then assert FindOtherROMDataWithLDR maps the source LDR
        // target to the corresponding TARGET LDR data address.
        //
        // Thumb layout of one function (relative to its base offset B):
        //   B+0:  PUSH {lr}            = 0xB500  (Format14 -> CodeType.PUSH)
        //   B+2:  LDR r0, [pc, #imm]   = 0x48xx  (Format6 -> CodeType.LDR)
        //   B+4:  BX lr                = 0x4770  (Format5 -> CodeType.BXJMP)
        //   ...   (padding to align the literal pool word to 4 bytes)
        //   slot: <4-byte data pointer>  (the literal that the LDR loads)
        //
        // ParseLDRPointer: slot = Padding4(toOffset(toPointer(B+2) + 2 + (imm8<<2))).
        // For B aligned to 4 and B+2 the LDR: toPointer(B+2)+2 = B+4. Choosing
        // imm8 so the slot lands at B+8 (one word after BX@B+4, with B+6 padding):
        //   B+4 + (imm8<<2) padded to 4 == B+8  ->  imm8<<2 = 4  ->  imm8 = 1.
        // So the LDR halfword = 0x4801 (LDR r0,[pc,#4]) and the literal sits at B+8.
        static byte[] MakeLdrFuncBuffer(int funcBase, uint dataPointer, int totalLen)
        {
            var buf = new byte[totalLen];
            WriteU16(buf, funcBase + 0, 0xB500);      // PUSH {lr}
            WriteU16(buf, funcBase + 2, 0x4801);      // LDR r0,[pc,#4] -> literal at base+8
            WriteU16(buf, funcBase + 4, 0x4770);      // BX lr
            WriteU16(buf, funcBase + 6, 0x46C0);      // NOP (MOV r8,r8) padding
            WritePtr(buf, funcBase + 8, dataPointer); // literal pool word
            return buf;
        }

        [Fact]
        public void FindOtherROMDataWithLDR_MapsSymmetricFunctionAcrossOffsets()
        {
            // Source: function at offset 0x200 loads data pointer 0x08003000.
            // Target: SAME function shape at offset 0x400 loads data pointer 0x08005000.
            // The two function BODIES (PUSH/LDR/BX/NOP, 8 bytes) are byte-identical
            // because the LDR is pc-relative; only the literal-pool word differs.
            int srcBase = 0x200;
            int tgtBase = 0x400;
            uint srcDataPtr = 0x08003000u;
            uint tgtDataPtr = 0x08005000u;

            var sourceData = MakeLdrFuncBuffer(srcBase, srcDataPtr, 0x1000);
            var targetData = MakeLdrFuncBuffer(tgtBase, tgtDataPtr, 0x1000);

            var sourceLdr = DisassemblerTrumb.MakeLDRMap(sourceData, 0x100, 0);
            var targetLdr = DisassemblerTrumb.MakeLDRMap(targetData, 0x100, 0);

            // Confirm MakeLDRMap picked up our crafted LDR entries (the literal
            // word at base+8 with our data pointers).
            Assert.Contains(sourceLdr, p => p.ldr_data == srcDataPtr && p.ldr_address == (uint)(srcBase + 2));
            Assert.Contains(targetLdr, p => p.ldr_data == tgtDataPtr && p.ldr_address == (uint)(tgtBase + 2));

            // Track srcDataPtr: the source func at srcBase loads it. Symmetry must
            // surface the TARGET func's data pointer (tgtDataPtr) and its slot.
            bool r = PointerToolAutoSearchCore.FindOtherROMDataWithLDR(
                sourceData, targetData, sourceLdr, targetLdr,
                srcDataPtr, slide: 0, testMatchSize: 8, grepPattern: false, isCodeType: true,
                out uint outAddr, out uint outRef);

            Assert.True(r);
            Assert.Equal(tgtDataPtr, outAddr);
            // outRef is the target literal-pool slot offset (tgtBase + 8).
            Assert.Equal((uint)(tgtBase + 8), outRef);
        }

        // ---- 5b: cached-map LDR lookup == GrepPointerAllOnLDR (#1118) ---------
        //
        // The baseline SearchOtherRom now reads the cached _targetLdrMap instead
        // of calling U.GrepPointerAllOnLDR per click. This proves the cached-map
        // lookup yields the IDENTICAL slot offset GrepPointerAllOnLDR returns:
        // for a buffer with an LDR loading `dataPtr`, the first MakeLDRMap entry
        // whose ldr_data == toPointer(dataPtr) has ldr_data_address equal to
        // GrepPointerAllOnLDR(buf, dataPtr)[0].

        [Fact]
        public void CachedLdrMap_SlotOffset_MatchesGrepPointerAllOnLDR()
        {
            // Buffer with a func at 0x200 whose LDR loads data pointer 0x08003000
            // from the literal slot at 0x208.
            uint dataPtr = 0x08003000u;
            var buf = MakeLdrFuncBuffer(0x200, dataPtr, 0x1000);

            // GrepPointerAllOnLDR returns the literal-pool SLOT offsets pointing
            // to dataPtr.
            var grepHits = U.GrepPointerAllOnLDR(buf, dataPtr);
            Assert.NotEmpty(grepHits);

            // Cached-map equivalent: first MakeLDRMap entry whose loaded word ==
            // toPointer(dataPtr); its ldr_data_address is the slot offset.
            var map = DisassemblerTrumb.MakeLDRMap(buf, 0x100, 0);
            uint needPtr = U.toPointer(dataPtr);
            DisassemblerTrumb.LDRPointer mapHit = null;
            foreach (var p in map)
            {
                if (p != null && p.ldr_data == needPtr) { mapHit = p; break; }
            }
            Assert.NotNull(mapHit);

            // The cached map's slot offset equals GrepPointerAllOnLDR's first hit.
            Assert.Equal(grepHits[0], mapHit.ldr_data_address);
            // Sanity: that slot is the crafted base+8.
            Assert.Equal((uint)(0x200 + 8), mapHit.ldr_data_address);
        }

        // ---- 6: Direct grep --------------------------------------------------

        [Fact]
        public void FindOtherROMData_FindsUniqueDataBlockInTarget()
        {
            // Unique 16-byte block at source offset 0x200; same block at target
            // offset 0x554. sourcePointer points to 0x08000200.
            var block = new byte[16];
            for (int i = 0; i < block.Length; i++) block[i] = (byte)(0x40 + i);

            var sourceData = new byte[0x1000];
            var targetData = new byte[0x1000];
            Array.Copy(block, 0, sourceData, 0x200, block.Length);
            Array.Copy(block, 0, targetData, 0x554, block.Length); // even offset (blocksize 2 grep)

            // A reference to the found data address (0x08000554) at an even offset
            // >= 0x100 so the ref-grep (now mode-aware DGrep) locates it.
            WritePtr(targetData, 0x300, 0x08000554u);

            bool r = PointerToolAutoSearchCore.FindOtherROMData(
                sourceData, targetData, 0x08000200u, slide: 0, testMatchSize: 16,
                grepPattern: false, isCodeType: false, out uint outAddr, out uint outRef);

            Assert.True(r);
            Assert.Equal(U.toPointer(0x554u), outAddr);
            // outRef comes from DGrep (exact, start 0, blocksize 2).
            Assert.Equal(0x300u, outRef);
        }

        // ---- 6c: ref-grep is mode-aware DGrep, not defaults U.Grep -----------
        //
        // WF FindOtherROMData finds the cross-ROM reference via DGrep (start 0,
        // blocksize 2), NOT the name path's defaults U.Grep (start 0x100). This
        // test proves the change: a reference at an even offset BELOW 0x100 is
        // now findable, where the old GrepBigEndianPointerRef (start 0x100) would
        // have returned NOT_FOUND.

        [Fact]
        public void FindOtherROMData_RefGrep_FindsReferenceBelow0x100()
        {
            var block = new byte[16];
            for (int i = 0; i < block.Length; i++) block[i] = (byte)(0x70 + i);

            var sourceData = new byte[0x1000];
            var targetData = new byte[0x1000];
            Array.Copy(block, 0, sourceData, 0x200, block.Length);
            Array.Copy(block, 0, targetData, 0x600, block.Length); // even offset

            // ONLY reference to 0x08000600 sits at offset 0x40 — below the old
            // U.Grep default start (0x100), but found by DGrep (start 0).
            WritePtr(targetData, 0x40, 0x08000600u);

            bool r = PointerToolAutoSearchCore.FindOtherROMData(
                sourceData, targetData, 0x08000200u, slide: 0, testMatchSize: 16,
                grepPattern: false, isCodeType: false, out uint outAddr, out uint outRef);

            Assert.True(r);
            Assert.Equal(U.toPointer(0x600u), outAddr);
            Assert.Equal(0x40u, outRef);
        }

        // ---- 6b: large-window disambiguation (#1118 Copilot review) ----------
        //
        // Proves the match WINDOW SIZE is load-bearing: a SHORT prefix exists at
        // an earlier WRONG target offset (a decoy), but the full WF-sized window
        // uniquely resolves the CORRECT target. This is exactly what the verbatim
        // WF SizeTable (index 0 = 0x100 = 512 bytes, widening by SHRINKING) buys
        // — a wide window rejects the short-prefix decoy.

        [Fact]
        public void FindOtherROMData_LargeWindowDisambiguatesShortPrefixDecoy()
        {
            // Source: a distinctive 0x40-byte block at offset 0x400. The first 6
            // bytes are a common prefix; the full 0x40 bytes are unique.
            var block = new byte[0x40];
            for (int i = 0; i < block.Length; i++) block[i] = (byte)(0xC0 + i);
            byte[] prefix6 = { block[0], block[1], block[2], block[3], block[4], block[5] };

            var sourceData = new byte[0x1000];
            var targetData = new byte[0x1000];
            Array.Copy(block, 0, sourceData, 0x400, block.Length);

            // Decoy: the 6-byte prefix at an EARLIER even offset (0x200), followed
            // by DIFFERENT bytes so the FULL 0x40 window does NOT match here.
            Array.Copy(prefix6, 0, targetData, 0x200, prefix6.Length);
            for (int i = prefix6.Length; i < 0x40; i++) targetData[0x200 + i] = (byte)(0x10 + i);

            // Correct target: the FULL unique 0x40-byte block at a LATER even
            // offset (0x900).
            Array.Copy(block, 0, targetData, 0x900, block.Length);

            // Large window (0x40) rejects the short-prefix decoy and resolves to
            // the full-block target at 0x900.
            bool rBig = PointerToolAutoSearchCore.FindOtherROMData(
                sourceData, targetData, 0x08000400u, slide: 0, testMatchSize: 0x40,
                grepPattern: false, isCodeType: false, out uint bigAddr, out uint _);
            Assert.True(rBig);
            Assert.Equal(U.toPointer(0x900u), bigAddr);

            // Tiny window (6) matches the decoy at 0x200 first — proving the
            // window size is load-bearing. Deterministic: U.Grep (blocksize 2)
            // scans even offsets ascending and 0x200 < 0x900 both hold the prefix.
            bool rTiny = PointerToolAutoSearchCore.FindOtherROMData(
                sourceData, targetData, 0x08000400u, slide: 0, testMatchSize: 6,
                grepPattern: false, isCodeType: false, out uint tinyAddr, out uint _);
            Assert.True(rTiny);
            Assert.Equal(U.toPointer(0x200u), tinyAddr);
        }

        // ---- 6d: slide-loop uses PATTERN grep (#1118 Copilot review) ---------
        //
        // WF AutoSearch leaves GrepType=1 (pattern) when entering the skipSearch
        // slide loop, so slid attempts are pointer/code-masked. This test proves
        // the slide path needs pattern masking: a data block contains a GBA
        // POINTER word that DIFFERS between source and target (a cross-version
        // pointer). makeSkipDataByPointer masks that word as a wildcard, so a
        // PATTERN grep matches while an EXACT grep does not. The read is at a
        // non-zero slide (2, from SlideTable) to exercise the slide path.
        //
        // Byte layout of the 0x10-byte block (need read from source at +slide):
        //   [0..3]  marker bytes (common to both buffers)
        //   [4..7]  GBA POINTER word — 0x08001111 in source, 0x08002222 in target
        //           (4-aligned within need, so makeSkipDataByPointer masks it)
        //   [8..F]  common tail bytes
        // EXACT grep of the source bytes mismatches at [4..7]; PATTERN grep masks
        // [4..7] and matches the target block.

        [Fact]
        public void FindOtherROMData_PatternSlide_MatchesWhenExactSlideMisses()
        {
            var sourceData = new byte[0x2000];
            var targetData = new byte[0x2000];

            // Build the common parts of the 0x10-byte block.
            byte[] BuildBlock(uint ptrWord)
            {
                var blk = new byte[0x10];
                blk[0] = 0x5A; blk[1] = 0x5B; blk[2] = 0x5C; blk[3] = 0x5D; // marker
                // [4..7] = a GBA pointer word (little-endian).
                blk[4] = (byte)(ptrWord & 0xFF);
                blk[5] = (byte)((ptrWord >> 8) & 0xFF);
                blk[6] = (byte)((ptrWord >> 16) & 0xFF);
                blk[7] = (byte)((ptrWord >> 24) & 0xFF);
                for (int i = 8; i < 0x10; i++) blk[i] = (byte)(0xA0 + i); // common tail
                return blk;
            }

            // Source block at offset 0x402; sourcePointer 0x400 + slide 2 -> 0x402.
            var srcBlock = BuildBlock(0x08001111u);   // source pointer word
            Array.Copy(srcBlock, 0, sourceData, 0x402, srcBlock.Length);

            // Target block at offset 0x800: identical EXCEPT the pointer word.
            var tgtBlock = BuildBlock(0x08002222u);   // DIFFERENT pointer word
            Array.Copy(tgtBlock, 0, targetData, 0x800, tgtBlock.Length);

            const uint sourcePointer = 0x08000400u;
            const int slide = 2;            // a non-zero SlideTable value
            const int testMatchSize = 0x10;

            // EXACT grep at the slide MISSES (the pointer word differs).
            bool rExact = PointerToolAutoSearchCore.FindOtherROMData(
                sourceData, targetData, sourcePointer, slide, testMatchSize,
                grepPattern: false, isCodeType: false, out uint _, out uint _);
            Assert.False(rExact);

            // PATTERN grep at the SAME slide MATCHES (the pointer word is masked).
            bool rPattern = PointerToolAutoSearchCore.FindOtherROMData(
                sourceData, targetData, sourcePointer, slide, testMatchSize,
                grepPattern: true, isCodeType: false, out uint patAddr, out uint _);
            Assert.True(rPattern);
            // Un-slid reported address: target block 0x800 minus slide 2 = 0x7FE.
            Assert.Equal(U.toPointer(0x7FEu), patAddr);
        }

        // ---- 7: AutoSearch no-match ------------------------------------------

        [Fact]
        public void AutoSearch_NoMatch_ReturnsNotFound_NoThrow()
        {
            var sourceData = new byte[0x1000];
            var targetData = new byte[0x1000];
            // Put unique source data NOT present in the (all-zero) target.
            for (int i = 0; i < 0x40; i++) sourceData[0x300 + i] = (byte)(0x11 + i);

            var r = PointerToolAutoSearchCore.AutoSearch(
                sourceData, targetData, 0x08000300u, 0x102u, null, null, warningLevel: 1);

            Assert.False(r.Found);
            Assert.Equal("none", r.Hit);
            Assert.Equal(U.NOT_FOUND, r.DirectAddr);
            Assert.Equal(U.NOT_FOUND, r.LdrAddr);
        }

        [Fact]
        public void AutoSearch_FindsDirectMatch_AcrossBuffers()
        {
            // A unique data block present in both buffers at different offsets.
            var block = new byte[0x20];
            for (int i = 0; i < block.Length; i++) block[i] = (byte)(0x80 + i);

            var sourceData = new byte[0x2000];
            var targetData = new byte[0x2000];
            Array.Copy(block, 0, sourceData, 0x400, block.Length);
            Array.Copy(block, 0, targetData, 0x800, block.Length);

            // Also place a reference to the target data block so warningLevel 1
            // accepts it even if the very-far / zero-region heuristics would
            // otherwise warn. The ref offset must be >= 0x100 because U.Grep's
            // default start is 0x100.
            WritePtr(targetData, 0x100, 0x08000800u);

            var r = PointerToolAutoSearchCore.AutoSearch(
                sourceData, targetData, 0x08000400u, 0x102u, null, null, warningLevel: 1);

            Assert.True(r.Found);
            Assert.Equal(U.toPointer(0x800u), r.DirectAddr);
        }

        // ---- 7c: Thumb-bit normalization covers ...01 AND ...03 (#1118) ------
        //
        // A Thumb function pointer is even_base|1: masking bit0 recovers the even
        // base. ...01 decrements to a 4-aligned base; ...03 decrements to a
        // HALFWORD-aligned (2-but-not-4) base. AutoSearch must mask ONLY bit0 for
        // BOTH. The OLD `%4==1` check missed ...03 entirely (it left the address
        // odd, treating a valid Thumb pointer as data). This test places the
        // source data block at the EVEN BASE each odd input decrements to
        // (oddAddr & ~1) and asserts it still resolves — for both ...01 and ...03.

        [Theory]
        [InlineData(0x08000401u)] // ...01 -> base 0x...400 — WF %4==1 handled this
        [InlineData(0x08000403u)] // ...03 -> base 0x...402 — WF %4==1 MISSED this
        public void AutoSearch_OddThumbAddress_NormalizesBit0_AndResolves(uint oddAddr)
        {
            // The even base bit0-masking recovers (...01->...400, ...03->...402).
            uint baseOffset = (oddAddr & ~1u) - 0x08000000u; // 0x400 or 0x402
            const uint targetOffset = 0x800;

            var block = new byte[0x20];
            for (int i = 0; i < block.Length; i++) block[i] = (byte)(0x80 + i);

            var sourceData = new byte[0x2000];
            var targetData = new byte[0x2000];
            Array.Copy(block, 0, sourceData, (int)baseOffset, block.Length);
            Array.Copy(block, 0, targetData, (int)targetOffset, block.Length);
            // Reference to the target block so warningLevel 1 accepts it.
            WritePtr(targetData, 0x100, U.toPointer(targetOffset));

            var r = PointerToolAutoSearchCore.AutoSearch(
                sourceData, targetData, oddAddr, 0x102u, null, null, warningLevel: 1);

            // Both ...01 and ...03 mask bit0 to their even base and find the
            // target at 0x800 — proving the bit0 mask is a strict superset of
            // WF's %4==1 (which would have left ...03 odd and failed).
            Assert.True(r.Found);
            Assert.Equal(U.toPointer(targetOffset), r.DirectAddr);
        }

        // ---- 8: Null / short guards ------------------------------------------

        [Fact]
        public void AutoSearch_NullAndShortBuffers_NotFound_NoThrow()
        {
            var ok = new byte[0x1000];

            Assert.False(PointerToolAutoSearchCore.AutoSearch(null, ok, 0x08000200u, 0x102u, null, null).Found);
            Assert.False(PointerToolAutoSearchCore.AutoSearch(ok, null, 0x08000200u, 0x102u, null, null).Found);
            // Buffers below the 0x400 minimum.
            Assert.False(PointerToolAutoSearchCore.AutoSearch(new byte[0x100], ok, 0x08000200u, 0x102u, null, null).Found);
            Assert.False(PointerToolAutoSearchCore.AutoSearch(ok, new byte[0x100], 0x08000200u, 0x102u, null, null).Found);
            // Zero address.
            Assert.False(PointerToolAutoSearchCore.AutoSearch(ok, ok, 0u, 0x102u, null, null).Found);
        }

        [Fact]
        public void FindOtherROMData_NullBuffers_False_NoThrow()
        {
            Assert.False(PointerToolAutoSearchCore.FindOtherROMData(null, new byte[16], 0x08000200u, 0, 16, false, false, out _, out _));
            Assert.False(PointerToolAutoSearchCore.FindOtherROMData(new byte[16], null, 0x08000200u, 0, 16, false, false, out _, out _));
            // Non-pointer source.
            Assert.False(PointerToolAutoSearchCore.FindOtherROMData(new byte[0x1000], new byte[0x1000], 0x00000001u, 0, 16, false, false, out _, out _));
        }

        // ---- 9: Truncated / misaligned target buffer (WU2b hardening) --------

        [Fact]
        public void MakeLDRMap_TruncatedBuffer_NoThrow()
        {
            // A buffer that ends mid-literal: an LDR at offset 0x102 pointing to a
            // literal slot near the end. WU2b guards `pointer + 4 <= limit` so the
            // u32 read is never OOB.
            var buf = new byte[0x110];
            WriteU16(buf, 0x100, 0xB500);      // PUSH
            WriteU16(buf, 0x102, 0x4801);      // LDR r0,[pc,#4] -> literal at 0x108
            WriteU16(buf, 0x104, 0x4770);      // BX lr
            // Literal slot would be at 0x108..0x10C, which IS in-bounds here.
            // Now also place a pathological LDR whose literal would fall in the
            // final 3 bytes (out of bounds): LDR at 0x10A with imm pushing it past.
            WriteU16(buf, 0x10A, 0x48FF);      // LDR r0,[pc,#0x3FC] -> way OOB literal

            // Must not throw despite the OOB literal slot.
            var ex = Record.Exception(() => DisassemblerTrumb.MakeLDRMap(buf, 0x100, 0));
            Assert.Null(ex);
        }

        [Fact]
        public void AutoSearch_TruncatedTargetBuffer_NotFound_NoThrow()
        {
            // Source is a normal buffer; target is exactly 0x400 (the minimum) so
            // MakeLDRMap / grep run on a tight buffer. No match -> NotFound, no throw.
            var sourceData = new byte[0x1000];
            for (int i = 0; i < 0x40; i++) sourceData[0x300 + i] = (byte)(0x55 + i);
            var targetData = new byte[0x400]; // all zero, exactly minimum length

            var ex = Record.Exception(() =>
            {
                var r = PointerToolAutoSearchCore.AutoSearch(sourceData, targetData, 0x08000300u, 0x102u, null, null);
                Assert.False(r.Found);
            });
            Assert.Null(ex);
        }
    }
}
