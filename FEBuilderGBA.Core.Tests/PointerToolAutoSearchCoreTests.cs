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

        // ---- 6: Direct grep --------------------------------------------------

        [Fact]
        public void FindOtherROMData_FindsUniqueDataBlockInTarget()
        {
            // Unique 16-byte block at source offset 0x200; same block at target
            // offset 0x555. sourcePointer points to 0x08000200.
            var block = new byte[16];
            for (int i = 0; i < block.Length; i++) block[i] = (byte)(0x40 + i);

            var sourceData = new byte[0x1000];
            var targetData = new byte[0x1000];
            Array.Copy(block, 0, sourceData, 0x200, block.Length);
            Array.Copy(block, 0, targetData, 0x554, block.Length); // even offset (blocksize 2 grep)

            bool r = PointerToolAutoSearchCore.FindOtherROMData(
                sourceData, targetData, 0x08000200u, slide: 0, testMatchSize: 16,
                grepPattern: false, isCodeType: false, out uint outAddr, out uint _);

            Assert.True(r);
            Assert.Equal(U.toPointer(0x554u), outAddr);
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
