// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice s2pf-2 (#1261) — the PatchForm producer
// address resolver + CalcAddrLength HELPERS (option-B epic, sub-slice 2 of 11).
//
// This slice ports the producer-side 4-arg address resolver
//   RebuildProducerCore.ResolvePatchAddress(rom, addrstring, appnedSize, startOffset, basedir)
// (a thin wrapper over the read-only Core port PatchMacroAddressResolverCore.Resolve,
//  the faithful port of WF PatchForm.convertBinAddressString @:3000) and
//   RebuildProducerCore.CalcAddrLength(patch)
// (verbatim port of WF PatchForm.CalcAddrLength @:6197).
//
// Coverage:
//   1. ResolvePatchAddress — per-macro-form on a synthetic ROM:
//        plain hex, $0x deref, $GREP (+ align/skip byte-parity), $P32, $TEXTID,
//        $XGREP, $GREP_ENABLE_POINTER, the FAITHFUL $FREEAREA -> NOT_FOUND carve-out,
//        the PRODUCER-FAITHFUL $EndWeaponDebuffTable3/4/5 bounded scans (NOT carved
//        out — they are real STRUCT DATACOUNT params), plus the appnedSize-is-irrelevant
//        invariant and the startOffset threading.
//   2. CalcAddrLength — COMBO/bytes param strings -> expected length, matching WF.
//
// All tests use synthetic in-memory ROMs (rom.LoadLow) so they run without any
// real GBA ROM file, mirroring PatchMacroAddressResolverCoreTests.

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerPatchResolverTests
    {
        // ---- ROM builder (same idiom as PatchMacroAddressResolverCoreTests) ----
        // 16 MiB zero-filled FE8U ROM (LoadLow minimum for BE8E01).
        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[0x1000000];
            bool ok = rom.LoadLow("x.gba", data, "BE8E01");
            Assert.True(ok, "LoadLow did not recognize BE8E01");
            return rom;
        }

        // Build a PatchSt from raw key/value lines (mirrors a real LoadPatch result).
        static PatchInstallCore.PatchSt MakePatch(params (string key, string value)[] kv)
        {
            var p = new PatchInstallCore.PatchSt
            {
                Name = "p",
                PatchFileName = "p.txt",
                Param = new Dictionary<string, string>()
            };
            foreach (var (key, value) in kv)
            {
                p.Param[key] = value;
            }
            return p;
        }

        // ====================================================================
        // 1. ResolvePatchAddress — per-macro-form
        // ====================================================================

        // ---- plain hex literal (ADDR callsite uses startOffset=0x100) -------

        [Fact]
        public void ResolvePatchAddress_PlainHexLiteral_StripsBase()
        {
            var rom = MakeRom();
            // GBA pointer 0x08001234 -> toOffset -> 0x1234. appnedSize=0, startOffset=0x100 (ADDR).
            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "0x08001234", 0, 0x100, "");
            Assert.Equal(0x00001234u, result);
        }

        // ---- $0x pointer dereference ---------------------------------------

        [Fact]
        public void ResolvePatchAddress_DollarHexDeref_ReturnsPointedOffset()
        {
            var rom = MakeRom();
            const uint slot = 0x1000;
            const uint target = 0x2000;
            U.write_u32(rom.Data, slot, U.toPointer(target));

            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$0x1000", 0, 0x100, "");
            Assert.Equal(target, result);
        }

        // ---- $GREP exact-match (byte tokens use 0xNN) ----------------------

        [Fact]
        public void ResolvePatchAddress_Grep_FindsExactPattern()
        {
            var rom = MakeRom();
            rom.Data[0x2000] = 0xAA;
            rom.Data[0x2001] = 0xBB;
            rom.Data[0x2002] = 0xCC;

            // SWITCH callsite shape: appnedSize=8, startOffset=0.
            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP1 0xAA 0xBB 0xCC", 8, 0, "");
            Assert.Equal(0x2000u, result);
        }

        [Fact]
        public void ResolvePatchAddress_Grep_PatternNotPresent_ReturnsNotFound()
        {
            var rom = MakeRom();
            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP1 0xDE 0xAD 0xBE 0xEF", 8, 0, "");
            Assert.Equal(U.NOT_FOUND, result);
        }

        // ---- $GREP align + skip BYTE-PARITY ---------------------------------
        // The DATACOUNT end-delta scan uses $GREP<align>ENDA+<skip>. Plant a
        // pattern at an aligned offset and assert the EXACT endpoint
        // (= match + pattern length + skip), proving align/skip group parsing
        // matches WF (a mis-read of Groups[2]/[4] would shift the endpoint and
        // mis-size the struct).

        [Fact]
        public void ResolvePatchAddress_GrepAlignSkip_ENDA_ExactEndpoint()
        {
            var rom = MakeRom();
            // Pattern 0x11 0x22 0x33 at 0x4000 (4-aligned). ENDA = address just after
            // the match; +6 skip => endpoint = 0x4000 + 3 (len) + 6 (skip) = 0x4009.
            rom.Data[0x4000] = 0x11;
            rom.Data[0x4001] = 0x22;
            rom.Data[0x4002] = 0x33;

            // $GREP4ENDA+6 : align=4 (Groups[2]), ENDA (Groups[3]), skip=6 (Groups[4]).
            // startOffset=struct_address style (use 0x100, below the planted pattern).
            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP4ENDA+6 0x11 0x22 0x33", 8, 0x100, "");
            Assert.Equal(0x4009u, result);
        }

        [Fact]
        public void ResolvePatchAddress_GrepAlign_RespectsAlignment()
        {
            var rom = MakeRom();
            // Plant the pattern at a NON-4-aligned offset (0x5002). A 4-aligned GREP
            // must NOT match it -> NOT_FOUND. This proves Groups[2] (align) is honored.
            rom.Data[0x5002] = 0x77;
            rom.Data[0x5003] = 0x88;
            rom.Data[0x5004] = 0x99;

            uint aligned4 = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP4 0x77 0x88 0x99", 8, 0x100, "");
            Assert.Equal(U.NOT_FOUND, aligned4);

            // The same pattern WITH align=1 finds it at 0x5002.
            uint aligned1 = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP1 0x77 0x88 0x99", 8, 0x100, "");
            Assert.Equal(0x5002u, aligned1);
        }

        // ---- $XGREP wildcard -----------------------------------------------

        [Fact]
        public void ResolvePatchAddress_Xgrep_WildcardMatchesAnyByte()
        {
            var rom = MakeRom();
            rom.Data[0x6000] = 0xAA;
            rom.Data[0x6001] = 0x42; // wildcard slot
            rom.Data[0x6002] = 0xCC;

            // Start search just below 0x6000 so the 0xAA at 0x6000 is the first hit.
            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$XGREP1 0xAA X 0xCC", 8, 0x5FFF, "");
            Assert.Equal(0x6000u, result);
        }

        // ---- $P32 -----------------------------------------------------------

        [Fact]
        public void ResolvePatchAddress_P32_DerefsPointerAtAddress()
        {
            var rom = MakeRom();
            const uint slot = 0x1100u;
            const uint target = 0x2200u;
            U.write_u32(rom.Data, slot, U.toPointer(target));

            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$P32 0x1100", 8, 0, "");
            Assert.Equal(target, result);
        }

        // ---- $TEXTID --------------------------------------------------------

        [Fact]
        public void ResolvePatchAddress_Textid_ReturnsActualTextDataAddress()
        {
            var rom = MakeRom();
            // text_pointer is the base-of-base for $TEXTID; plant the text table base there.
            uint textPointerSlot = rom.RomInfo.text_pointer;
            uint textBase = 0x60000u;
            U.write_u32(rom.Data, textPointerSlot, U.toPointer(textBase));

            // text id 3 -> pointer table entry at textBase + 3*4 -> text data offset
            uint textDataOffset = 0x70000u;
            U.write_u32(rom.Data, textBase + 3 * 4, U.toPointer(textDataOffset));

            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$TEXTID 0x3", 0, 0x100, "");
            Assert.Equal(textDataOffset, result);
        }

        [Fact]
        public void ResolvePatchAddress_TextidP_ReturnsPointerTableEntryAddress()
        {
            var rom = MakeRom();
            uint textPointerSlot = rom.RomInfo.text_pointer;
            uint textBase = 0x50000u;
            U.write_u32(rom.Data, textPointerSlot, U.toPointer(textBase));

            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$TEXTID_P 0x5", 0, 0x100, "");
            Assert.Equal(textBase + 5 * 4, result);
        }

        // ---- $GREP_ENABLE_POINTER ------------------------------------------

        [Fact]
        public void ResolvePatchAddress_GrepEnablePointer_StopsAtFirstNonPointer()
        {
            var rom = MakeRom();
            const uint pStart = 0x1000u;
            U.write_u32(rom.Data, pStart, U.toPointer(0x80000u)); // valid pointer
            // pStart+4 is zero -> not a pointer -> stop.

            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP_ENABLE_POINTER ", 0, pStart, "");
            Assert.Equal(pStart + 4, result);
        }

        // ---- FAITHFUL CARVE-OUT -> NOT_FOUND ($FREEAREA only) ---------------

        [Fact]
        public void ResolvePatchAddress_FreeArea_ReturnsNotFound()
        {
            var rom = MakeRom();
            // $FREEAREA is the WF run-time allocator (PatchForm.cs:3027 MoveToFreeSapceForm.
            // SearchFreeSpaceOne) — carved to NOT_FOUND at rebuild time. It never appears as a
            // producer ADDRESS/POINTER/DATACOUNT param (only BIN/free-area install metadata),
            // so the carve-out is faithful for the producer. Test BOTH the appnedSize==0 and
            // appnedSize!=0 WF callsite shapes: both must be NOT_FOUND.
            Assert.Equal(U.NOT_FOUND, RebuildProducerCore.ResolvePatchAddress(rom, "$FREEAREA", 0, 0x100, ""));
            Assert.Equal(U.NOT_FOUND, RebuildProducerCore.ResolvePatchAddress(rom, "$FREEAREA", 8, 0x100, ""));
        }

        // ---- $EndWeaponDebuffTable3/4/5 — PRODUCER-FAITHFUL bounded scans ----
        // These ARE real STRUCT DATACOUNT params in FE8U
        // (config/patch2/FE8U/skill/PATCH_defWeaponDebuffsTable*.txt). WF routes them
        // through PatchUtil.GetEndWeaponDebuffTable3/4/5 (bounded ROM scans), so the
        // producer MUST resolve them — NOT_FOUND would silently drop the struct. The
        // start_offset is the STRUCT struct_address (WF DATACOUNT callsite passes it).

        [Fact]
        public void ResolvePatchAddress_EndWeaponDebuffTable3_ScansToFirstTerminatorRow()
        {
            var rom = MakeRom();
            // struct_address = 0x100000. WF skips the leading 0x00*3 header, then greps for
            // the FIRST of {000000, FFFF00, FFFFFF} from struct_address+3 within a 3*256 window.
            const uint structAddr = 0x100000u;
            // Two non-zero, non-terminator 3-byte rows at +3..+8, then FFFFFF terminator at +9.
            rom.Data[structAddr + 3] = 0x01; rom.Data[structAddr + 4] = 0x02; rom.Data[structAddr + 5] = 0x03;
            rom.Data[structAddr + 6] = 0x04; rom.Data[structAddr + 7] = 0x05; rom.Data[structAddr + 8] = 0x06;
            rom.Data[structAddr + 9] = 0xFF; rom.Data[structAddr + 10] = 0xFF; rom.Data[structAddr + 11] = 0xFF;
            // (bytes +12.. are zero -> a 000000 row would match at +12, but the FFFFFF row at +9
            //  is earlier, so the min-found endpoint is structAddr+9.)

            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$EndWeaponDebuffTable3 0x00", 8, structAddr, "");
            Assert.Equal(structAddr + 9, result);
            // The end-delta the STRUCT arm will compute: (end - struct_address) = 9 bytes = 3 rows of DATASIZE=3.
            Assert.NotEqual(U.NOT_FOUND, result);
        }

        [Fact]
        public void ResolvePatchAddress_EndWeaponDebuffTable4_ScansToFirstTerminatorRow()
        {
            var rom = MakeRom();
            // Table4 skips a leading 0x00*4 header (stride 4) before the same terminator grep.
            const uint structAddr = 0x110000u;
            // Non-zero rows at +4..+9, then FFFF00 terminator at +10.
            rom.Data[structAddr + 4] = 0x11; rom.Data[structAddr + 5] = 0x22; rom.Data[structAddr + 6] = 0x33;
            rom.Data[structAddr + 7] = 0x44; rom.Data[structAddr + 8] = 0x55; rom.Data[structAddr + 9] = 0x66;
            rom.Data[structAddr + 10] = 0xFF; rom.Data[structAddr + 11] = 0xFF; rom.Data[structAddr + 12] = 0x00;

            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$EndWeaponDebuffTable4 0x00", 8, structAddr, "");
            Assert.Equal(structAddr + 10, result);
        }

        [Fact]
        public void ResolvePatchAddress_EndWeaponDebuffTable5_WalksUntilHighNibbleSet()
        {
            var rom = MakeRom();
            // Table5: skip leading 0x00*4, then walk u32 rows until byte[+3] high nibble != 0.
            const uint structAddr = 0x120000u;
            // Rows from structAddr+4. Each row is 4 bytes; the scan checks rom.u8(addr+3).
            // Row at +4: [.. .. .. 0x0X] high nibble 0 -> keep going.
            rom.Data[structAddr + 4 + 3] = 0x05; // high nibble 0 -> continue
            // Row at +8: [.. .. .. 0x3X]? give it a high nibble != 0 to terminate at +8.
            rom.Data[structAddr + 8 + 3] = 0x30; // (0x30 & 0xF0) != 0 -> break, return this addr

            uint result = RebuildProducerCore.ResolvePatchAddress(rom, "$EndWeaponDebuffTable5 0x00", 8, structAddr, "");
            Assert.Equal(structAddr + 8, result);
        }

        // ---- appnedSize is IRRELEVANT to the resolved value (invariant) -----

        [Fact]
        public void ResolvePatchAddress_AppnedSize_DoesNotAffectResult()
        {
            var rom = MakeRom();
            rom.Data[0x2000] = 0xAA;
            rom.Data[0x2001] = 0xBB;
            rom.Data[0x2002] = 0xCC;

            // The only WF branch that read appnedSize ($FREEAREA) is carved out, so every
            // OTHER macro must resolve identically regardless of the appnedSize argument.
            uint a = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP1 0xAA 0xBB 0xCC", 0, 0x100, "");
            uint b = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP1 0xAA 0xBB 0xCC", 8, 0x100, "");
            uint c = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP1 0xAA 0xBB 0xCC", 0x1234, 0x100, "");
            Assert.Equal(0x2000u, a);
            Assert.Equal(a, b);
            Assert.Equal(a, c);
        }

        // ---- startOffset threading (start_offset narrows the GREP scan) -----

        [Fact]
        public void ResolvePatchAddress_StartOffset_NarrowsGrepScan()
        {
            var rom = MakeRom();
            // Two copies of the pattern; startOffset above the first hit must skip it
            // and return the SECOND. This proves startOffset is threaded into the scan
            // (the WF start_offset arg, e.g. STRUCT DATACOUNT passes struct_address).
            rom.Data[0x3000] = 0xEE;
            rom.Data[0x3001] = 0xFF;
            rom.Data[0x8000] = 0xEE;
            rom.Data[0x8001] = 0xFF;

            uint fromStart = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP1 0xEE 0xFF", 8, 0x100, "");
            Assert.Equal(0x3000u, fromStart);

            uint fromMid = RebuildProducerCore.ResolvePatchAddress(rom, "$GREP1 0xEE 0xFF", 8, 0x4000, "");
            Assert.Equal(0x8000u, fromMid);
        }

        // ---- guards ---------------------------------------------------------

        [Fact]
        public void ResolvePatchAddress_NullRom_ReturnsNotFound_NoThrow()
        {
            uint result = RebuildProducerCore.ResolvePatchAddress(null, "$GREP1 0xAA", 0, 0x100, "");
            Assert.Equal(U.NOT_FOUND, result);
        }

        [Fact]
        public void ResolvePatchAddress_EmptyAddrstring_ReturnsNotFound()
        {
            var rom = MakeRom();
            Assert.Equal(U.NOT_FOUND, RebuildProducerCore.ResolvePatchAddress(rom, "", 0, 0x100, ""));
        }

        // ====================================================================
        // 2. CalcAddrLength (WF :6197) — COMBO|bytes parse
        // ====================================================================

        [Fact]
        public void CalcAddrLength_NoCombo_ReturnsOne()
        {
            // No COMBO key -> U.at returns "" -> length 1.
            var patch = MakePatch(("TYPE", "ADDR"), ("ADDRESS", "0x100"));
            Assert.Equal(1u, RebuildProducerCore.CalcAddrLength(patch));
        }

        [Fact]
        public void CalcAddrLength_EmptyCombo_ReturnsOne()
        {
            var patch = MakePatch(("COMBO", ""));
            Assert.Equal(1u, RebuildProducerCore.CalcAddrLength(patch));
        }

        [Fact]
        public void CalcAddrLength_ComboWithoutPipe_ReturnsOne()
        {
            // No '|' -> Split yields 1 part (< 2) -> length 1.
            var patch = MakePatch(("COMBO", "label"));
            Assert.Equal(1u, RebuildProducerCore.CalcAddrLength(patch));
        }

        [Fact]
        public void CalcAddrLength_SingleByteSecondSection_ReturnsOne()
        {
            // "name|AA" -> second section "AA" splits to 1 token -> length 1.
            var patch = MakePatch(("COMBO", "name|AA"));
            Assert.Equal(1u, RebuildProducerCore.CalcAddrLength(patch));
        }

        [Fact]
        public void CalcAddrLength_MultiByteSecondSection_ReturnsTokenCount()
        {
            // "name|AA BB CC CD" -> second section has 4 space-separated tokens -> length 4.
            var patch = MakePatch(("COMBO", "name|AA BB CC CD"));
            Assert.Equal(4u, RebuildProducerCore.CalcAddrLength(patch));
        }

        [Fact]
        public void CalcAddrLength_OnlyFirstSectionUsesSecondPipe_IgnoresThirdSection()
        {
            // Only the SECOND '|'-section is counted; a 3rd section is ignored.
            // "name|AA BB|junk extra here" -> second section "AA BB" -> length 2.
            var patch = MakePatch(("COMBO", "name|AA BB|junk extra here"));
            Assert.Equal(2u, RebuildProducerCore.CalcAddrLength(patch));
        }

        [Fact]
        public void CalcAddrLength_NullPatchOrParam_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.CalcAddrLength(null));
            var noParam = new PatchInstallCore.PatchSt { Name = "p", PatchFileName = "p.txt", Param = null };
            Assert.Throws<ArgumentNullException>(() => RebuildProducerCore.CalcAddrLength(noParam));
        }
    }
}
