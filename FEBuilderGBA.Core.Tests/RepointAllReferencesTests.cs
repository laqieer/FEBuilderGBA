using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="DataExpansionCore.RepointAllReferences"/> (#781) and
    /// the EOF-hardened <see cref="DisassemblerTrumb.GrepLDRData"/> /
    /// <see cref="U.GrepPointerAllOnLDR"/> scanners it relies on.
    ///
    /// <para>Synthetic ROMs are built with <c>ROM.LoadLow("test.gba", data,
    /// "NAZO")</c> (ROMFE0) — the same minimal-ROM pattern used by
    /// <c>DataExpansionCoreTests</c>.</para>
    ///
    /// <para><b>LDR encoding:</b> a Thumb <c>ldr r0,[pc,#0]</c> instruction is the
    /// halfword <c>0x4800</c> (<c>(0x9 &lt;&lt; 11)</c> opcode, rd=0, imm=0).
    /// <c>DisassemblerTrumb.ParseLDRPointer</c> computes the literal slot as
    /// <c>Padding4(instrOffset + 2 + ((a &amp; 0xff) &lt;&lt; 2))</c>; for
    /// <c>imm=0</c> at a 4-byte-aligned instruction offset <c>I</c> the slot is
    /// <c>I + 4</c>. We place <c>0x4800</c> at <c>I</c> and the target pointer at
    /// <c>I + 4</c>, which makes <c>I + 4</c> BOTH a raw pointer hit and an LDR
    /// literal-pool hit (the de-dup overlap case).</para>
    /// </summary>
    public class RepointAllReferencesTests
    {
        const uint OldOffset = 0x1000;            // table base offset
        const uint OldPtr = 0x08000000 + OldOffset;
        const uint NewOffset = 0x2000;            // relocated table base offset
        const uint NewPtr = 0x08000000 + NewOffset;

        static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            byte[] data = new byte[size];
            rom.LoadLow("test.gba", data, "NAZO");
            return rom;
        }

        static void WriteWord(ROM rom, uint addr, uint value)
        {
            rom.Data[addr + 0] = (byte)(value & 0xFF);
            rom.Data[addr + 1] = (byte)((value >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((value >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>Write a Thumb <c>ldr r0,[pc,#0]</c> (0x4800) at instrOffset.</summary>
        static void WriteLdrPcZero(ROM rom, uint instrOffset)
        {
            rom.Data[instrOffset + 0] = 0x00; // low byte of 0x4800
            rom.Data[instrOffset + 1] = 0x48; // high byte
        }

        // ─────────────────────────────────────────────────────────────
        // Happy path: raw + LDR + overlap, de-dup, repoint, restore
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void RepointAllReferences_RawAndLdr_WithOverlap_ReturnsUniqueCount_AndRepoints()
        {
            var rom = MakeRom();

            // ---- N raw-only pointers to OldPtr at known aligned offsets ----
            uint[] rawSlots = { 0x4000, 0x4010, 0x4020 };
            foreach (uint s in rawSlots)
                WriteWord(rom, s, OldPtr);

            // ---- M LDR literal-pool loads whose literal == OldPtr ----
            // Instruction at I (mult of 4), literal slot at I+4.
            // One of these slots is ALSO listed in rawSlots-independent space,
            // but the LDR slot itself is intrinsically a raw hit too (overlap).
            uint[] ldrInstr = { 0x5000, 0x5010, 0x5020 };
            var ldrSlots = new List<uint>();
            foreach (uint I in ldrInstr)
            {
                WriteLdrPcZero(rom, I);          // 0x4800 at I
                uint slot = I + 4;               // literal pool slot
                WriteWord(rom, slot, OldPtr);    // literal == OldPtr
                ldrSlots.Add(slot);
            }

            // Build the expected unique slot set: raw slots + ldr slots.
            // Every LDR slot is also a raw hit (overlap) — HashSet de-dups.
            var expected = new HashSet<uint>();
            foreach (uint s in rawSlots) expected.Add(s);
            foreach (uint s in ldrSlots) expected.Add(s);

            // An unrelated pointer that must NOT be touched.
            uint unrelatedSlot = 0x6000;
            uint unrelatedPtr = 0x08000000 + 0x9000;
            WriteWord(rom, unrelatedSlot, unrelatedPtr);

            // Sanity: the two scanners independently agree on the slot set.
            var rawHits = new HashSet<uint>(U.GrepPointerAll(rom.Data, OldPtr));
            var ldrHits = new HashSet<uint>(U.GrepPointerAllOnLDR(rom.Data, OldPtr));
            foreach (uint s in ldrSlots)
            {
                Assert.Contains(s, ldrHits);  // each LDR slot found by the LDR scan
                Assert.Contains(s, rawHits);  // …and also by the raw scan (overlap)
            }

            int count = DataExpansionCore.RepointAllReferences(rom, OldPtr, NewPtr, null);

            // Unique slot count = |raw ∪ ldr|.
            Assert.Equal(expected.Count, count);

            // Every expected slot now reads NewPtr. rom.p32 returns the OFFSET
            // form (U.toOffset), and the raw word is U.toPointer(NewPtr).
            foreach (uint s in expected)
            {
                Assert.Equal(NewPtr, rom.u32(s));     // raw 32-bit word
                Assert.Equal(NewOffset, rom.p32(s));  // offset accessor
            }

            // Unrelated pointer untouched.
            Assert.Equal(unrelatedPtr, rom.u32(unrelatedSlot));
        }

        [Fact]
        public void RepointAllReferences_NoOverlapDoubleCount()
        {
            // Stronger overlap assertion: the LDR scan and raw scan both hit the
            // same slot, but RepointAllReferences must count it ONCE.
            var rom = MakeRom();

            uint instr = 0x5000;
            WriteLdrPcZero(rom, instr);
            uint slot = instr + 4;        // both raw + ldr hit
            WriteWord(rom, slot, OldPtr);

            // Confirm both scanners see it.
            Assert.Contains(slot, U.GrepPointerAll(rom.Data, OldPtr));
            Assert.Contains(slot, U.GrepPointerAllOnLDR(rom.Data, OldPtr));

            int count = DataExpansionCore.RepointAllReferences(rom, OldPtr, NewPtr, null);
            Assert.Equal(1, count);                 // counted once, not twice
            Assert.Equal(NewPtr, rom.u32(slot));    // raw word repointed
        }

        // ─────────────────────────────────────────────────────────────
        // Edge: no references
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void RepointAllReferences_NoReferences_ReturnsZero_NoThrow()
        {
            var rom = MakeRom(0x10000);
            // ROM is all 0x00 — no pointer anywhere equals OldPtr.
            int count = DataExpansionCore.RepointAllReferences(rom, OldPtr, NewPtr, null);
            Assert.Equal(0, count);
        }

        [Fact]
        public void RepointAllReferences_NullRom_ReturnsZero()
        {
            Assert.Equal(0, DataExpansionCore.RepointAllReferences(null, OldPtr, NewPtr, null));
        }

        // ─────────────────────────────────────────────────────────────
        // Edge: danger zone (0x0–0x200) refused
        // ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0x00000000u)]
        [InlineData(0x00000004u)]
        [InlineData(0x000000FFu)]
        [InlineData(0x0000010Cu)]   // below the 0x200 isSafetyOffset floor
        [InlineData(0x000001FFu)]
        public void RepointAllReferences_DangerZoneBase_ReturnsZero_NoWrite(uint dangerOffset)
        {
            var rom = MakeRom(0x10000);

            // Place a raw pointer to the danger-zone base somewhere safe so that
            // IF the guard were missing, a write would occur.
            uint dangerPtr = 0x08000000 + dangerOffset;
            uint slot = 0x4000;
            WriteWord(rom, slot, dangerPtr);

            int count = DataExpansionCore.RepointAllReferences(rom, dangerPtr, NewPtr, null);
            Assert.Equal(0, count);
            // The slot must be untouched (still holding the danger-base pointer).
            Assert.Equal(dangerPtr, rom.u32(slot));
        }

        // ─────────────────────────────────────────────────────────────
        // Edge: pointer / LDR-like bytes in the final ~8 bytes of the ROM
        // must not throw IndexOutOfRangeException
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void RepointAllReferences_PointerInFinalBytes_NoThrow()
        {
            // ROM whose VERY LAST word is a raw pointer to OldPtr, and whose
            // last instruction is an LDR (0x4800) at size-8 pointing at that
            // final word (literal slot = I+4 = size-4). The final word is thus
            // BOTH a raw hit and an LDR literal hit sitting right at EOF — the
            // EOF guard must let the literal read at (size-4)+4 == size succeed
            // (check_safety uses `>`), and must NOT throw.
            int size = 0x1010;
            var rom = MakeRom(size);
            uint lastWord = (uint)size - 4;     // 0x100C
            WriteWord(rom, lastWord, OldPtr);    // raw pointer at EOF word
            // LDR r0,[pc,#0] at size-8 whose literal slot would be the final
            // word. The scanner's `pointer < limit` floor excludes the very
            // last word from the LDR pass (pre-existing behavior); the raw pass
            // still catches it. The point here is that the LDR EOF path does
            // NOT throw when an LDR sits near the end of the ROM.
            uint instr = (uint)size - 8;         // 0x1008
            WriteLdrPcZero(rom, instr);          // 0x4800

            var ex = Record.Exception(() =>
            {
                // The raw scan finds the EOF word (GrepPointerAll end == len-4).
                Assert.Contains(lastWord, U.GrepPointerAll(rom.Data, OldPtr));
                // The LDR scan must not throw even though an LDR is near EOF.
                U.GrepPointerAllOnLDR(rom.Data, OldPtr);

                int count = DataExpansionCore.RepointAllReferences(rom, OldPtr, NewPtr, null);
                Assert.True(count >= 1);
                Assert.Equal(NewPtr, rom.u32(lastWord));
            });
            Assert.Null(ex);
        }

        [Fact]
        public void GrepLDRData_LdrInFinalBytes_NoThrow_ReturnsEmptyOrPartial()
        {
            // Build a ROM whose final halfword is an LDR (0x48xx) so the naive
            // literal read would index past the array. The EOF guard must skip
            // it without throwing.
            int size = 0x800;
            var rom = MakeRom(size);
            // Final halfword = LDR with a large imm so the computed slot lands
            // well past EOF (skip), and another LDR right at the boundary.
            rom.Data[size - 2] = 0xFF;  // imm = 0xFF -> slot far past EOF
            rom.Data[size - 1] = 0x48;  // 0x48FF
            rom.Data[size - 4] = 0x00;
            rom.Data[size - 3] = 0x48;  // 0x4800 at size-4 -> slot at size (EOF)

            var ex = Record.Exception(() =>
            {
                var hits = DisassemblerTrumb.GrepLDRData(rom.Data, OldPtr);
                // No literal in this ROM equals OldPtr -> empty.
                Assert.Empty(hits);
            });
            Assert.Null(ex);
        }

        [Fact]
        public void GrepLDRData_TinyRom_ReturnsEmpty_NoUnderflow()
        {
            // A ROM shorter than 4 bytes must not underflow `Length - 4`.
            var hits1 = DisassemblerTrumb.GrepLDRData(new byte[0], OldPtr);
            Assert.Empty(hits1);
            var hits2 = DisassemblerTrumb.GrepLDRData(new byte[3], OldPtr);
            Assert.Empty(hits2);
        }

        [Fact]
        public void GrepPointerAllOnLDR_NullData_ReturnsEmpty()
        {
            Assert.Empty(U.GrepPointerAllOnLDR(null, OldPtr));
        }

        // ─────────────────────────────────────────────────────────────
        // #782 review regression: a reference whose SLOT lands in the
        // danger zone (< 0x200, inside the cartridge header) must be
        // INCLUDED-BUT-SKIPPED — never written — while a normal in-range
        // reference (>= 0x200) in the same ROM is still repointed + counted.
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void RepointAllReferences_DangerZoneSlot_SkippedNotWritten_InRangeStillRepointed()
        {
            var rom = MakeRom();

            // (a) Raw pointer to OldPtr at a DANGER-ZONE slot 0x104 (< 0x200).
            // GrepPointerAll's default start is 0x100, so this slot IS found by
            // the scanner — proving the per-slot gate (not the scan floor) is
            // what skips it.
            uint dangerRawSlot = 0x104;
            WriteWord(rom, dangerRawSlot, OldPtr);

            // (b) An LDR whose literal-pool slot ALSO computes into the danger
            // zone: ldr r0,[pc,#0] at instr 0x100 -> slot Padding4(0x100+2)=0x104.
            // (Same slot as (a); written here for an explicit LDR-path danger
            // hit. The literal at 0x104 already holds OldPtr from (a).)
            WriteLdrPcZero(rom, 0x100);

            // (c) A NORMAL in-range raw pointer (>= 0x200) that MUST be repointed.
            uint inRangeSlot = 0x4000;
            WriteWord(rom, inRangeSlot, OldPtr);

            // Sanity: the scanners DO surface the danger-zone slot (so the gate,
            // not the scan, is responsible for skipping it).
            Assert.Contains(dangerRawSlot, U.GrepPointerAll(rom.Data, OldPtr));
            Assert.Contains(dangerRawSlot, U.GrepPointerAllOnLDR(rom.Data, OldPtr));
            Assert.Contains(inRangeSlot, U.GrepPointerAll(rom.Data, OldPtr));

            int count = DataExpansionCore.RepointAllReferences(rom, OldPtr, NewPtr, null);

            // Only the in-range slot was written -> count == 1 (danger slot excluded).
            Assert.Equal(1, count);

            // The danger-zone header slot is UNCHANGED (still the original OldPtr).
            Assert.Equal(OldPtr, rom.u32(dangerRawSlot));

            // The in-range slot WAS repointed.
            Assert.Equal(NewPtr, rom.u32(inRangeSlot));
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Undo rollback — mutates CoreState.ROM, so isolate via SharedState.
    // ─────────────────────────────────────────────────────────────
    [Collection("SharedState")]
    public class RepointAllReferencesUndoTests : IDisposable
    {
        readonly ROM? _savedRom;

        public RepointAllReferencesUndoTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        const uint OldPtr = 0x08000000 + 0x1000;
        const uint NewPtr = 0x08000000 + 0x2000;

        static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            byte[] data = new byte[size];
            rom.LoadLow("test.gba", data, "NAZO");
            return rom;
        }

        static void WriteWord(ROM rom, uint addr, uint value)
        {
            rom.Data[addr + 0] = (byte)(value & 0xFF);
            rom.Data[addr + 1] = (byte)((value >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((value >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((value >> 24) & 0xFF);
        }

        [Fact]
        public void RepointAllReferences_Rollback_RestoresAllSlots()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;

            // Raw + LDR slots.
            uint[] rawSlots = { 0x4000, 0x4010 };
            foreach (uint s in rawSlots) WriteWord(rom, s, OldPtr);

            uint instr = 0x5000;
            rom.Data[instr] = 0x00; rom.Data[instr + 1] = 0x48;  // ldr r0,[pc,#0]
            uint ldrSlot = instr + 4;
            WriteWord(rom, ldrSlot, OldPtr);

            byte[] snapshot = new byte[rom.Data.Length];
            Array.Copy(rom.Data, snapshot, rom.Data.Length);

            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "RepointAllReferences test",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };

            int count = DataExpansionCore.RepointAllReferences(rom, OldPtr, NewPtr, ud);
            Assert.True(count >= 3);

            // All slots now hold the NewPtr raw word.
            Assert.Equal(NewPtr, rom.u32(rawSlots[0]));
            Assert.Equal(NewPtr, rom.u32(rawSlots[1]));
            Assert.Equal(NewPtr, rom.u32(ldrSlot));

            // Roll back in reverse order (mirrors Undo.RollbackROM).
            for (int i = ud.list.Count - 1; i >= 0; i--)
            {
                var up = ud.list[i];
                Array.Copy(up.data, 0, rom.Data, up.addr, up.data.Length);
            }

            // Every byte must match the pre-repoint snapshot.
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] != rom.Data[i])
                    Assert.Fail($"Byte mismatch at 0x{i:X06}: snapshot=0x{snapshot[i]:X02}, post-rollback=0x{rom.Data[i]:X02}");
            }
        }

        [Fact]
        public void RepointAllReferences_AmbientUndoScope_RecordsWrites()
        {
            // When undo is null, the helper must respect the caller's ambient
            // BeginUndoScope (the WinForms / Avalonia usage pattern).
            var rom = MakeRom();
            CoreState.ROM = rom;

            uint slot = 0x4000;
            WriteWord(rom, slot, OldPtr);

            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "ambient",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };

            int count;
            using (ROM.BeginUndoScope(ud))
            {
                count = DataExpansionCore.RepointAllReferences(rom, OldPtr, NewPtr, null);
            }
            Assert.Equal(1, count);
            Assert.Equal(NewPtr, rom.u32(slot));
            // The ambient scope captured the slot write.
            Assert.Contains(ud.list, p => p.addr == slot);
        }
    }
}
