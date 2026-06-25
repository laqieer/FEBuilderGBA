using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Headless tests for <see cref="MoveToFreeSpaceViewViewModel.ExecuteMove"/>
    /// (issue #1410 — RELEASE-BLOCKER: the Avalonia Move-to-Free-Space tool copied
    /// a block to free space and wiped the source to <c>0xFF</c> but NEVER repointed
    /// references → guaranteed silent ROM corruption).
    ///
    /// <para>The fix calls <see cref="DataExpansionCore.RepointAllReferences"/> after
    /// the copy and BEFORE the destructive clear, refuses to clear when 0 references
    /// are found, and relocates lint/comment caches — mirroring the WinForms
    /// <c>MoveToFreeSapceForm.RunButton_Click</c> ground truth.</para>
    ///
    /// <para>Synthetic ROMs are built with <c>ROM.LoadLow("test.gba", data, "NAZO")</c>
    /// (ROMFE0) — the same minimal-ROM pattern used by <c>RepointAllReferencesTests</c>.
    /// A Thumb <c>ldr r0,[pc,#0]</c> is the halfword <c>0x4800</c>; for that opcode at
    /// a 4-byte-aligned instruction offset <c>I</c> the literal slot is <c>I + 4</c>,
    /// which is BOTH a raw pointer hit and an LDR literal-pool hit.</para>
    /// </summary>
    [Collection("SharedState")]
    public class MoveToFreeSpaceViewModelTests : IDisposable
    {
        // Source block lives at a safe, distinctive offset. The early ROM up to
        // FreeStart is reserved (non-free 0x01) so the VM's FindFreeSpace — which
        // treats 0x00/0xFF as free and scans from offset 0 — resolves to a
        // realistic, deterministic destination PAST the source block (never the
        // 0x0 cartridge-header danger zone).
        const uint SrcOffset = 0x40000;
        const uint BlockSize = 0x20;
        const uint SrcPtr = 0x08000000 + SrcOffset;
        // First offset the VM may allocate free space at (kept past the source
        // block and all reference slots).
        const uint FreeStart = 0x80000;

        readonly ROM? _savedRom;
        readonly Undo? _savedUndo;

        public MoveToFreeSpaceViewModelTests()
        {
            _savedRom = CoreState.ROM;
            _savedUndo = CoreState.Undo;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Undo = _savedUndo;
        }

        static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            byte[] data = new byte[size];
            // Reserve [0, FreeStart) as non-free (0x01) so FindFreeSpace lands
            // at a deterministic offset >= FreeStart. The source block + every
            // reference slot are written into this reserved region afterward.
            for (uint i = 0; i < FreeStart && i < size; i++)
                data[i] = 0x01;
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

        /// <summary>Fill the source block with a recognizable, non-zero/non-0xFF pattern.</summary>
        static byte[] WriteSourceBlock(ROM rom, uint srcOffset, uint size)
        {
            byte[] pattern = new byte[size];
            for (uint i = 0; i < size; i++)
                pattern[i] = (byte)(0x10 + (i & 0x3F));
            for (uint i = 0; i < size; i++)
                rom.Data[srcOffset + i] = pattern[i];
            return pattern;
        }

        /// <summary>
        /// Build a ROM with a recognizable source block at <see cref="SrcOffset"/> and
        /// both a raw pointer and an LDR literal-pool reference to it. Returns the
        /// source-block pattern and the list of reference SLOTS (each holding SrcPtr).
        /// </summary>
        static (byte[] pattern, List<uint> refSlots) MakeRomWithReferences()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            byte[] pattern = WriteSourceBlock(rom, SrcOffset, BlockSize);

            var refSlots = new List<uint>();

            // (a) Two raw 32-bit pointers to the source block.
            foreach (uint slot in new uint[] { 0x4000, 0x4010 })
            {
                WriteWord(rom, slot, SrcPtr);
                refSlots.Add(slot);
            }

            // (b) An LDR literal-pool load whose literal == SrcPtr.
            uint instr = 0x5000;
            WriteLdrPcZero(rom, instr);
            uint ldrSlot = instr + 4;
            WriteWord(rom, ldrSlot, SrcPtr);
            refSlots.Add(ldrSlot);

            return (pattern, refSlots);
        }

        static MoveToFreeSpaceViewViewModel MakeVm()
        {
            return new MoveToFreeSpaceViewViewModel
            {
                CurrentAddress = $"0x{SrcOffset:X08}",
                DataSize = $"0x{BlockSize:X}",
            };
        }

        // ─────────────────────────────────────────────────────────────
        // 1. References ARE repointed; source IS cleared; copy is byte-correct.
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void ExecuteMove_WithReferences_RepointsAll_ClearsSource_CopyIsByteCorrect()
        {
            var (pattern, refSlots) = MakeRomWithReferences();
            var rom = CoreState.ROM!;
            var vm = MakeVm();

            vm.ExecuteMove();

            Assert.Equal("Moved", vm.DialogResult);

            // Destination resolved from the VM's reported free-space address.
            Assert.False(string.IsNullOrEmpty(vm.NewAddress));
            uint dst = Convert.ToUInt32(vm.NewAddress.Substring(2), 16);
            Assert.True(dst >= FreeStart, $"dst 0x{dst:X} must land in the free region (>= 0x{FreeStart:X}).");
            uint dstPtr = 0x08000000 + dst;

            // (1) Copy is byte-correct at the destination.
            for (uint i = 0; i < BlockSize; i++)
                Assert.Equal(pattern[i], rom.Data[dst + i]);

            // (2) Every reference now points at the destination (NOT the wiped source).
            foreach (uint slot in refSlots)
                Assert.Equal(dstPtr, rom.u32(slot));

            // (3) The source block was cleared to 0xFF (safe — refs were repointed first).
            for (uint i = 0; i < BlockSize; i++)
                Assert.Equal(0xFF, rom.Data[SrcOffset + i]);
        }

        // ─────────────────────────────────────────────────────────────
        // 2. THE BUG: with NO references, the source must NOT be cleared.
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void ExecuteMove_NoReferences_DoesNotClearSource_AndWarns()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            byte[] pattern = WriteSourceBlock(rom, SrcOffset, BlockSize);

            var vm = MakeVm();
            vm.ExecuteMove();

            // The destructive clear is refused: the source block is intact.
            for (uint i = 0; i < BlockSize; i++)
                Assert.Equal(pattern[i], rom.Data[SrcOffset + i]);

            // Result is NOT "Moved" and the warning is surfaced to the user.
            Assert.NotEqual("Moved", vm.DialogResult);
            Assert.Equal("NoReferences", vm.DialogResult);
            Assert.Contains("NOT cleared", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }

        // ─────────────────────────────────────────────────────────────
        // 3. No-reference path is copy-only — destination written, source intact.
        //    (Copilot plan-review request: make the no-ref side-effect explicit.)
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void ExecuteMove_NoReferences_CopiesToFreeSpace_ButLeavesSourceValid()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            byte[] pattern = WriteSourceBlock(rom, SrcOffset, BlockSize);

            var vm = MakeVm();
            vm.ExecuteMove();

            uint dst = Convert.ToUInt32(vm.NewAddress.Substring(2), 16);

            // The block WAS copied into free space (copy precedes the repoint scan)…
            for (uint i = 0; i < BlockSize; i++)
                Assert.Equal(pattern[i], rom.Data[dst + i]);

            // …but the source is preserved (no orphaning) so the ROM stays valid.
            for (uint i = 0; i < BlockSize; i++)
                Assert.Equal(pattern[i], rom.Data[SrcOffset + i]);
        }

        // ─────────────────────────────────────────────────────────────
        // 4. Undo integration: copy + repoint + source-clear are ONE undo group.
        //    (Copilot plan-review request.)
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void ExecuteMove_UnderUndoScope_RollsBackCopyRepointAndClear_Together()
        {
            var (pattern, refSlots) = MakeRomWithReferences();
            var rom = CoreState.ROM!;

            byte[] snapshot = new byte[rom.Data.Length];
            Array.Copy(rom.Data, snapshot, rom.Data.Length);

            var vm = MakeVm();

            // Drive the UndoService exactly as MoveToFreeSpaceView.Move_Click does.
            var u = new UndoService();
            u.Begin("Move to Free Space");
            vm.ExecuteMove();
            u.Commit();

            Assert.Equal("Moved", vm.DialogResult);
            uint dst = Convert.ToUInt32(vm.NewAddress.Substring(2), 16);
            uint dstPtr = 0x08000000 + dst;

            // Sanity: all three mutations landed (copy, repoint, clear).
            Assert.Equal(pattern[0], rom.Data[dst]);
            foreach (uint slot in refSlots)
                Assert.Equal(dstPtr, rom.u32(slot));
            Assert.Equal(0xFF, rom.Data[SrcOffset]);

            // One RunUndo must reverse the WHOLE operation as a single group.
            CoreState.Undo!.RunUndo();

            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] != rom.Data[i])
                    Assert.Fail($"Byte mismatch at 0x{i:X06} after undo: " +
                        $"snapshot=0x{snapshot[i]:X02}, post-undo=0x{rom.Data[i]:X02}");
            }

            // Specifically: source restored, references restored, destination cleared.
            for (uint i = 0; i < BlockSize; i++)
                Assert.Equal(pattern[i], rom.Data[SrcOffset + i]);
            foreach (uint slot in refSlots)
                Assert.Equal(SrcPtr, rom.u32(slot));
        }

        // ─────────────────────────────────────────────────────────────
        // 5. Invalid input guards (unchanged behavior — regression coverage).
        // ─────────────────────────────────────────────────────────────

        [Fact]
        public void ExecuteMove_NoRomLoaded_ReportsAndDoesNothing()
        {
            CoreState.ROM = null;
            var vm = MakeVm();
            vm.ExecuteMove();
            Assert.NotEqual("Moved", vm.DialogResult);
            Assert.Contains("No ROM", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExecuteMove_ZeroOrInvalidSize_ReportsAndDoesNothing()
        {
            var rom = MakeRom();
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var vm = new MoveToFreeSpaceViewViewModel
            {
                CurrentAddress = $"0x{SrcOffset:X08}",
                DataSize = "0x0",
            };
            vm.ExecuteMove();
            Assert.NotEqual("Moved", vm.DialogResult);
        }
    }
}
