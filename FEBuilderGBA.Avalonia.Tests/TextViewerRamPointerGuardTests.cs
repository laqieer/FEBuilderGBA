// SPDX-License-Identifier: GPL-3.0-or-later
// #1425 — Avalonia Text Editor RAM-pointer write guard.
//
// WinForms TextForm.WriteText (TextForm.cs:466-470) REFUSES to write to a text
// slot whose CURRENT pointer points into IW/EW-RAM (raw 0x02/0x03 or the
// unHuffman-patched 0x82/0x83 forms) — those slots hold runtime-RAM text
// installed by patches — returning U.NOT_FOUND with NO ROM mutation. The
// Avalonia TextViewerViewModel.WriteText previously lacked that guard and would
// silently repoint such a slot to fresh ROM text. These tests pin the ported
// guard: WriteText throws EncodeAbortedException (the WF-faithful no-mutation
// abort, which the View rolls back) and leaves the ROM byte-identical, for all
// four RAM-pointer forms; a normal ROM-pointer slot still writes (no regression).
//
// The fixture ROM does not contain a RAM-pointer slot naturally, so each test
// PREPARES one by writing a RAM-form pointer into a chosen text slot, snapshots
// the ROM, runs the guarded write, asserts no mutation, then RESTORES the slot
// (the fixture ROM is shared across the SharedState collection).
using System;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class TextViewerRamPointerGuardTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public TextViewerRamPointerGuardTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        bool SkipNoRom()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return true;
            }
            return false;
        }

        // ============================================================
        // Static truth table for the ported Is_RAMPointerArea helper.
        // ============================================================

        [Theory]
        // IW-RAM (0x03......) and EW-RAM (0x02......) raw forms.
        [InlineData(0x03001000u, true)]
        [InlineData(0x03000000u, true)]
        [InlineData(0x02001000u, true)]
        [InlineData(0x02000000u, true)]
        // unHuffman-patched IW (0x83......) / EW (0x82......) forms.
        [InlineData(0x83001000u, true)]
        [InlineData(0x82001000u, true)]
        // Normal ROM pointer (0x08......) and a plain offset are NOT RAM areas.
        [InlineData(0x08001000u, false)]
        [InlineData(0x00001000u, false)]
        [InlineData(0u, false)]
        public void Is_RAMPointerArea_MatchesWinFormsTruthTable(uint addr, bool expected)
        {
            Assert.Equal(expected, TextViewerViewModel.Is_RAMPointerArea(addr));
        }

        // ============================================================
        // WriteText REFUSES RAM-pointer slots (no mutation), all 4 forms.
        // ============================================================

        [Theory]
        [InlineData(0x03001000u)] // raw IW-RAM
        [InlineData(0x02001000u)] // raw EW-RAM
        [InlineData(0x83001000u)] // unHuffman IW-RAM
        [InlineData(0x82001000u)] // unHuffman EW-RAM
        public void WriteText_RamPointerSlot_AbortsWithNoMutation(uint ramPointer)
        {
            if (SkipNoRom()) return;
            ROM rom = _fixture.ROM!;

            var vm = new TextViewerViewModel();
            uint textBase = vm.ResolveTextTableBase();
            Assert.NotEqual(0u, textBase);

            // Use a high-but-in-range text id so we don't disturb id 0 (write-
            // protected) or the commonly-tested low ids. Confirm the slot is in
            // ROM bounds before preparing it.
            uint id = 0x40;
            uint slot = textBase + id * 4;
            if (slot + 4 > (uint)rom.Data.Length)
            {
                _output.WriteLine("Chosen slot out of ROM bounds; skipping.");
                return;
            }

            uint originalSlotValue = rom.u32(slot);
            try
            {
                // PREPARE: plant a RAM-form pointer in the slot directly (no undo —
                // this is test scaffolding, restored in finally).
                rom.write_u32(slot, ramPointer);
                Assert.Equal(ramPointer, rom.u32(slot)); // sanity

                byte[] dataBefore = (byte[])rom.Data.Clone();

                // ACT: the guard must refuse with NO mutation, regardless of the
                // text content (use plain ASCII that encodes cleanly so we are
                // testing the RAM guard, not the bad-char path).
                var ex = Assert.Throws<TextViewerViewModel.EncodeAbortedException>(
                    () => vm.WriteText(id, "Hello"));

                // The message is the WF-faithful RAM-area refusal.
                Assert.Contains("RAM", ex.Message, StringComparison.OrdinalIgnoreCase);

                // ASSERT: byte-identical — pointer unchanged, no append, no writes.
                Assert.Equal(ramPointer, rom.u32(slot));
                Assert.Equal(dataBefore.Length, rom.Data.Length);
                Assert.True(dataBefore.SequenceEqual(rom.Data),
                    "ROM bytes mutated despite RAM-pointer abort");
            }
            finally
            {
                // RESTORE the original slot value for the shared fixture ROM.
                rom.write_u32(slot, originalSlotValue);
            }
        }

        // ============================================================
        // Ordering: the RAM guard fires BEFORE encoding / the AntiHuffman
        // prompt path. A RAM-pointer slot + bad-character text must abort via
        // the RAM refusal WITHOUT ever invoking the AntiHuffman callback. This
        // is what proves the guard is placed before FETextEncoder.Encode
        // (Copilot plan-review finding) — an encodable-text-only test would
        // pass even if the guard were placed after encoding.
        // ============================================================

        [Fact]
        public void WriteText_RamPointerSlot_BadCharText_RefusesBeforeAntiHuffman()
        {
            if (SkipNoRom()) return;
            ROM rom = _fixture.ROM!;

            var vm = new TextViewerViewModel();

            // Bad-character text: a CJK string that cannot be Huffman-encoded on
            // English/most ROMs. Skip if it encodes cleanly on this ROM (then the
            // AntiHuffman path is never reachable and the ordering claim is moot).
            string badText = "日本語";
            if (vm.PeekEncodeError(badText) == null)
            {
                _output.WriteLine("Bad-char text encoded cleanly on this ROM; skipping ordering test.");
                return;
            }

            uint textBase = vm.ResolveTextTableBase();
            Assert.NotEqual(0u, textBase);

            uint id = 0x42;
            uint slot = textBase + id * 4;
            if (slot + 4 > (uint)rom.Data.Length)
            {
                _output.WriteLine("Chosen slot out of ROM bounds; skipping.");
                return;
            }

            uint originalSlotValue = rom.u32(slot);
            try
            {
                rom.write_u32(slot, 0x03001000u); // plant a RAM (IW) pointer
                byte[] dataBefore = (byte[])rom.Data.Clone();

                // Fail the test if the AntiHuffman callback is invoked — the RAM
                // guard MUST short-circuit before encoding ever runs.
                vm.AntiHuffmanPromptCallback = _ =>
                {
                    Assert.Fail("AntiHuffman callback invoked — RAM guard ran AFTER encoding");
                    return false;
                };

                var ex = Assert.Throws<TextViewerViewModel.EncodeAbortedException>(
                    () => vm.WriteText(id, badText));

                // It aborted via the RAM-area refusal, NOT the bad-char refusal.
                Assert.Contains("RAM", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.True(dataBefore.SequenceEqual(rom.Data),
                    "ROM bytes mutated despite RAM-pointer abort");
            }
            finally
            {
                rom.write_u32(slot, originalSlotValue);
            }
        }

        // ============================================================
        // View-layer ordering (#1425 Copilot PR-review finding): the View's
        // OnWriteTextClick runs a bad-character / AntiHuffman pre-flight BEFORE
        // calling WriteText. For a RAM-pointer slot it must refuse FIRST — never
        // showing the AntiHuffman popup / Patch Manager — using the read-only
        // IsCurrentSlotRamPointer helper the View now consults. These tests pin
        // that helper: true for every RAM-pointer slot form (so the View short-
        // circuits the pre-flight), false for a normal ROM-pointer slot.
        // ============================================================

        [Theory]
        [InlineData(0x03001000u)] // raw IW-RAM
        [InlineData(0x02001000u)] // raw EW-RAM
        [InlineData(0x83001000u)] // unHuffman IW-RAM
        [InlineData(0x82001000u)] // unHuffman EW-RAM
        public void IsCurrentSlotRamPointer_TrueForRamSlot_SoViewSkipsBadCharPreflight(uint ramPointer)
        {
            if (SkipNoRom()) return;
            ROM rom = _fixture.ROM!;

            var vm = new TextViewerViewModel();
            uint textBase = vm.ResolveTextTableBase();
            Assert.NotEqual(0u, textBase);

            uint id = 0x43;
            uint slot = textBase + id * 4;
            if (slot + 4 > (uint)rom.Data.Length)
            {
                _output.WriteLine("Chosen slot out of ROM bounds; skipping.");
                return;
            }

            uint originalSlotValue = rom.u32(slot);
            try
            {
                rom.write_u32(slot, ramPointer);

                // The View consults this BEFORE PeekEncodeError; true => refuse first,
                // no bad-char popup. (Read-only: no ROM mutation.)
                byte[] dataBefore = (byte[])rom.Data.Clone();
                Assert.True(vm.IsCurrentSlotRamPointer(id));
                Assert.True(dataBefore.SequenceEqual(rom.Data),
                    "IsCurrentSlotRamPointer mutated the ROM (must be read-only)");
            }
            finally
            {
                rom.write_u32(slot, originalSlotValue);
            }
        }

        [Fact]
        public void IsCurrentSlotRamPointer_FalseForNormalSlot()
        {
            if (SkipNoRom()) return;
            ROM rom = _fixture.ROM!;

            var vm = new TextViewerViewModel();
            uint textBase = vm.ResolveTextTableBase();
            Assert.NotEqual(0u, textBase);

            uint id = 0x44;
            uint slot = textBase + id * 4;
            if (slot + 4 > (uint)rom.Data.Length)
            {
                _output.WriteLine("Chosen slot out of ROM bounds; skipping.");
                return;
            }

            // The slot's natural pointer is a normal ROM pointer on this ROM; the
            // helper must return false so the View proceeds to its normal pre-flight.
            if (TextViewerViewModel.Is_RAMPointerArea(rom.u32(slot)))
            {
                _output.WriteLine("Slot is a RAM pointer on this ROM; skipping.");
                return;
            }
            Assert.False(vm.IsCurrentSlotRamPointer(id));
        }

        [Fact]
        public void IsCurrentSlotRamPointer_NoRom_ReturnsFalse()
        {
            // Headless / unloaded ROM: the helper is a safe no-op returning false
            // (the View then proceeds; WriteText surfaces the no-ROM error).
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new TextViewerViewModel();
                Assert.False(vm.IsCurrentSlotRamPointer(1));
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // ============================================================
        // Regression: a NORMAL ROM-pointer slot still writes successfully.
        // ============================================================

        [Fact]
        public void WriteText_NormalRomPointerSlot_WritesSuccessfully()
        {
            if (SkipNoRom()) return;
            ROM rom = _fixture.ROM!;

            var vm = new TextViewerViewModel();
            uint textBase = vm.ResolveTextTableBase();
            Assert.NotEqual(0u, textBase);

            // Pick a slot whose current pointer is a normal ROM pointer (NOT a RAM
            // area). Most slots are; assert the guard does not falsely fire.
            uint id = 0x41;
            uint slot = textBase + id * 4;
            if (slot + 4 > (uint)rom.Data.Length)
            {
                _output.WriteLine("Chosen slot out of ROM bounds; skipping.");
                return;
            }

            uint current = rom.u32(slot);
            if (TextViewerViewModel.Is_RAMPointerArea(current))
            {
                _output.WriteLine("Slot is a RAM pointer on this ROM; skipping regression case.");
                return;
            }

            // Write empty text — encoder-independent (the empty-text branch points
            // the slot at text id 0's pointer), so this proves the guard does NOT
            // fire on a NORMAL ROM-pointer slot regardless of ROM language. WriteText
            // must NOT throw (no RAM refusal) and the slot must be updated.
            uint slotBefore = rom.u32(slot);
            try
            {
                // Must NOT throw — the guard only fires for RAM-area slots.
                vm.WriteText(id, "");

                // The empty-text branch repointed the slot to text id 0's pointer;
                // it must still be a NON-RAM pointer (the guard never converted it).
                uint slotAfter = rom.u32(slot);
                uint text0Pointer = rom.u32(textBase);
                Assert.Equal(text0Pointer, slotAfter);
                Assert.False(TextViewerViewModel.Is_RAMPointerArea(slotAfter),
                    "Normal slot was turned into a RAM pointer");
            }
            finally
            {
                // Restore the original slot value for the shared fixture ROM (the
                // empty-text branch only rewrites the 4-byte slot pointer in place).
                rom.write_u32(slot, slotBefore);
            }
        }
    }
}
