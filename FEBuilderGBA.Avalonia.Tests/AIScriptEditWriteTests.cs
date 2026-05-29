// SPDX-License-Identifier: GPL-3.0-or-later
// AIScriptViewModel opcode hex-edit + in-place Write-back tests (#760).
//
// Proves the Avalonia AI Script editor can hand-edit an instruction's bytes
// and persist a SAME-SIZE slice back to the ROM:
//   - SerializeScript() is an EXACT concatenation of every decoded
//     instruction's ByteData (no EXIT terminator append / normalization), so
//     a freshly-loaded script round-trips to the original ReadByteCount bytes;
//   - UpdateRow() re-decodes a hand-edited 16-byte instruction, mutating only
//     the targeted row, and rejects over-length / non-hex input without
//     touching the model;
//   - WriteScript() writes the same-size slice through rom.write_range (so the
//     surrounding UndoService scope tracks it as one entry), and refuses any
//     length change, unsafe/header offset, or out-of-range slice;
//   - an empty model never writes;
//   - the write is undoable via CoreState.Undo.RunUndo();
//   - DisassembleScript() re-read after a committed write reflects the
//     persisted bytes.
//
// Reuses the AiDisasmEnv synthetic FE8U environment from
// AIScriptDisassemblyTests.cs (#757). Marked [Collection("SharedState")]
// because the suite mutates CoreState.ROM / CoreState.AIScript /
// CoreState.CommentCache / CoreState.Undo / CoreState.BaseDirectory.
using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AIScriptEditWriteTests
    {
        // 16-byte AI instruction helpers (FE8 table). Attack05's byte[1] is
        // the PROBABILITY arg; byte[2]=0xFF is FIXED.
        static byte[] Attack05(byte probability)
        {
            var b = new byte[16];
            b[0] = 0x05; b[1] = probability; b[2] = 0xFF;
            return b;
        }

        static byte[] ExitOpcode(byte id)
        {
            var b = new byte[16];
            b[0] = 0x03; b[1] = 0x00; b[2] = 0xFF; b[3] = id;
            return b;
        }

        // A space-separated hex dump of a 16-byte instruction (the form
        // GetRowHex emits and the View pushes into the Binary Code box).
        static string HexLine(params byte[] firstBytes)
        {
            var b = new byte[16];
            Array.Copy(firstBytes, b, Math.Min(firstBytes.Length, 16));
            return string.Join(" ", b.Select(x => x.ToString("X2")));
        }

        // ----------------------------------------------------------------
        // 1. SerializeScript round-trip identity.
        // ----------------------------------------------------------------

        [Fact]
        public void SerializeScript_FreshLoad_EqualsOriginalRomBytes()
        {
            using var env = new AiDisasmEnv();

            // Two real instructions: Attack05 + EXIT (32 bytes total).
            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());

            // Must disassemble first to populate the editable model.
            vm.DisassembleScript();

            byte[] serialized = vm.SerializeScript();

            Assert.Equal((int)vm.ReadByteCount, serialized.Length);
            byte[] original = env.RomSlice(vm.CurrentAddr, vm.ReadByteCount);
            Assert.Equal(original, serialized);
        }

        // ----------------------------------------------------------------
        // 2. UpdateRow param edit (probability 0x64 -> 0x32) keeps length.
        // ----------------------------------------------------------------

        [Fact]
        public void UpdateRow_ProbabilityEdit_ReflectsValueAndKeepsLength()
        {
            using var env = new AiDisasmEnv();

            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());
            vm.DisassembleScript();

            int lenBefore = vm.SerializeScript().Length;

            // Edit row 0's probability from 0x64 (100) to 0x32 (50).
            string? line = vm.UpdateRow(0, HexLine(0x05, 0x32, 0xFF));
            Assert.NotNull(line);
            // PROBABILITY renders as decimal "50" or hex "0x32" depending on
            // the arg's IsDecimal flag — accept either, both encode 0x32.
            Assert.True(line!.Contains("0x32") || line.Contains("50"),
                $"Expected edited probability (0x32/50) in row, got: {line}");
            Assert.Contains("Attack05", line);

            byte[] serialized = vm.SerializeScript();
            Assert.Equal(0x32, serialized[1]);
            // Same-size: editing a value must not change total length.
            Assert.Equal(lenBefore, serialized.Length);
            // The EXIT row (second slot) is untouched.
            Assert.Equal(0x03, serialized[16]);
        }

        // ----------------------------------------------------------------
        // 3. UpdateRow invalid input: no mutation.
        // ----------------------------------------------------------------

        [Fact]
        public void UpdateRow_InvalidInput_ReturnsNullAndLeavesModelUnchanged()
        {
            using var env = new AiDisasmEnv();

            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());
            vm.DisassembleScript();

            byte[] before = vm.SerializeScript();

            // Too long: 17 bytes (> one 16-byte instruction) -> null, no change.
            string tooLong = string.Join(" ",
                Enumerable.Range(0, 17).Select(_ => "00"));
            Assert.Null(vm.UpdateRow(0, tooLong));
            Assert.Equal(before, vm.SerializeScript());

            // Non-hex -> null, no change.
            Assert.Null(vm.UpdateRow(0, "zz zz zz"));
            Assert.Equal(before, vm.SerializeScript());

            // Empty -> null, no change.
            Assert.Null(vm.UpdateRow(0, ""));
            Assert.Equal(before, vm.SerializeScript());

            // Out-of-range index -> null, no change.
            Assert.Null(vm.UpdateRow(99, HexLine(0x05, 0x32, 0xFF)));
            Assert.Equal(before, vm.SerializeScript());
        }

        [Fact]
        public void UpdateRow_ShortInstruction_RightPadsToSixteenBytes()
        {
            using var env = new AiDisasmEnv();

            var vm = env.LoadVmAt(Attack05(0x64));
            vm.DisassembleScript();

            // Only 3 bytes typed: must right-pad to a full 16-byte slot.
            string? line = vm.UpdateRow(0, "05 32 FF");
            Assert.NotNull(line);

            // SerializeScript now appends a WF-parity EXIT terminator because
            // the single Attack05 row does not end in 0x03 (#763), so the
            // serialized length is 32 = 16 (padded Attack05) + 16 (EXIT).
            byte[] serialized = vm.SerializeScript();
            Assert.Equal(32, serialized.Length);
            // The edited row occupies the FIRST 16-byte slot, right-padded.
            Assert.Equal(0x05, serialized[0]);
            Assert.Equal(0x32, serialized[1]);
            Assert.Equal(0xFF, serialized[2]);
            for (int i = 3; i < 16; i++)
                Assert.Equal(0x00, serialized[i]);
            // The appended EXIT occupies the second slot.
            Assert.Equal(0x03, serialized[16]);
        }

        // ----------------------------------------------------------------
        // 4. WriteScript same-size success + forced mismatch refusal.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteScript_SameSizeEdit_WritesBytes()
        {
            using var env = new AiDisasmEnv();
            using var undo = new UndoScope();

            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());
            vm.DisassembleScript();

            Assert.NotNull(vm.UpdateRow(0, HexLine(0x05, 0x32, 0xFF)));

            bool ok = vm.WriteScript();
            Assert.True(ok);
            Assert.Equal(0x32, env.Rom.Data[vm.CurrentAddr + 1]);
        }

        [Fact]
        public void WriteScript_LengthMismatch_RefusesAndLeavesRomUnchanged()
        {
            using var env = new AiDisasmEnv();
            using var undo = new UndoScope();

            var vm = env.LoadVmAt(Attack05(0x64));
            vm.DisassembleScript();

            Assert.NotNull(vm.UpdateRow(0, HexLine(0x05, 0x32, 0xFF)));

            // Snapshot the ROM slice, then force a length mismatch: the
            // serialized model is 16 bytes but ReadByteCount now says 8.
            byte[] snapshot = env.RomSlice(vm.CurrentAddr, 16);
            vm.ReadByteCount = 8;

            Assert.False(vm.WriteScript());
            // Nothing written: the original 0x64 probability survives.
            Assert.Equal(snapshot, env.RomSlice(vm.CurrentAddr, 16));
            Assert.Equal(0x64, env.Rom.Data[vm.CurrentAddr + 1]);
        }

        [Fact]
        public void WriteScript_OutOfRange_RefusesAndLeavesRomUnchanged()
        {
            using var env = new AiDisasmEnv();
            using var undo = new UndoScope();

            var vm = env.LoadVmAt(Attack05(0x64));
            vm.DisassembleScript();
            Assert.NotNull(vm.UpdateRow(0, HexLine(0x05, 0x32, 0xFF)));

            // Point CurrentAddr so the 16-byte slice runs off the ROM end.
            // ReadByteCount stays 16 (matches serialized) so only the
            // bounds guard fires.
            vm.CurrentAddr = (uint)env.Rom.Data.Length - 8;
            Assert.False(vm.WriteScript());
        }

        // ----------------------------------------------------------------
        // 5. Empty model never writes.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteScript_EmptyModel_ReturnsFalse()
        {
            using var env = new AiDisasmEnv();
            using var undo = new UndoScope();

            // A VM that never disassembled has an empty model.
            var vm = new AIScriptViewModel
            {
                CurrentAddr = 0x200000,
                ReadByteCount = 0,
                IsLoaded = true,
            };
            Assert.Empty(vm.SerializeScript());
            Assert.False(vm.WriteScript());
        }

        // ----------------------------------------------------------------
        // 5b. A header / low (unsafe) CurrentAddr is refused with no mutation.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteScript_UnsafeHeaderOffset_RefusesAndDoesNotMutate()
        {
            using var env = new AiDisasmEnv();

            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray()); // loaded at the safe ScriptBase
            vm.DisassembleScript();

            // Redirect the write target to a header/low offset below the
            // U.isSafetyOffset floor (a mistyped Address box). Length stays equal
            // to ReadByteCount, so only the safety-offset guard can reject it.
            const uint unsafeAddr = 0x100;
            vm.CurrentAddr = unsafeAddr;
            byte[] before = env.RomSlice(unsafeAddr, vm.ReadByteCount);

            Assert.False(vm.WriteScript());
            Assert.Equal(before, env.RomSlice(unsafeAddr, vm.ReadByteCount));
        }

        // ----------------------------------------------------------------
        // 6. Undo restores the original bytes.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteScript_UnderUndoScope_IsUndoable()
        {
            using var env = new AiDisasmEnv();

            IDisposable? scope = null;
            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                var body = new List<byte>();
                body.AddRange(Attack05(0x64));
                body.AddRange(ExitOpcode(0x00));
                var vm = env.LoadVmAt(body.ToArray());
                vm.DisassembleScript();

                byte[] original = env.RomSlice(vm.CurrentAddr, vm.ReadByteCount);

                Assert.NotNull(vm.UpdateRow(0, HexLine(0x05, 0x32, 0xFF)));

                // Drive the UndoService exactly as the View does.
                var u = new UndoService();
                u.Begin("Edit AI Script");
                bool ok = vm.WriteScript();
                Assert.True(ok);
                u.Commit();

                // The edit is persisted.
                Assert.Equal(0x32, env.Rom.Data[vm.CurrentAddr + 1]);

                // Undo it: ROM bytes return to the original slice.
                CoreState.Undo!.RunUndo();
                Assert.Equal(original, env.RomSlice(vm.CurrentAddr, vm.ReadByteCount));
                Assert.Equal(0x64, env.Rom.Data[vm.CurrentAddr + 1]);
            }
            finally
            {
                scope?.Dispose();
                CoreState.Undo = prevUndo;
            }
        }

        // ----------------------------------------------------------------
        // 7. Re-disassemble after a committed write reflects persisted bytes.
        // ----------------------------------------------------------------

        [Fact]
        public void DisassembleScript_AfterCommittedWrite_ReflectsEditedValue()
        {
            using var env = new AiDisasmEnv();

            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                var body = new List<byte>();
                body.AddRange(Attack05(0x64));
                body.AddRange(ExitOpcode(0x00));
                var vm = env.LoadVmAt(body.ToArray());
                vm.DisassembleScript();

                Assert.NotNull(vm.UpdateRow(0, HexLine(0x05, 0x32, 0xFF)));

                var u = new UndoService();
                u.Begin("Edit AI Script");
                Assert.True(vm.WriteScript());
                u.Commit();

                // Re-read from ROM: the edited probability must persist.
                IReadOnlyList<string> rows = vm.DisassembleScript();
                Assert.NotEmpty(rows);
                Assert.True(rows[0].Contains("0x32") || rows[0].Contains("50"),
                    $"Re-read row must show the persisted probability, got: {rows[0]}");
                Assert.Equal(0x32, vm.SerializeScript()[1]);
            }
            finally
            {
                CoreState.Undo = prevUndo;
            }
        }

        // ----------------------------------------------------------------
        // GetDisplayLines reflects in-memory edits (no ROM re-read).
        // ----------------------------------------------------------------

        [Fact]
        public void GetDisplayLines_AfterUpdateRow_ReflectsEditWithoutRomReread()
        {
            using var env = new AiDisasmEnv();

            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());
            vm.DisassembleScript();

            // Edit in memory only (no Write). ROM still holds 0x64.
            Assert.NotNull(vm.UpdateRow(0, HexLine(0x05, 0x32, 0xFF)));
            Assert.Equal(0x64, env.Rom.Data[vm.CurrentAddr + 1]); // ROM unchanged

            IReadOnlyList<string> lines = vm.GetDisplayLines();
            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].Contains("0x32") || lines[0].Contains("50"),
                $"GetDisplayLines must show the in-memory edit, got: {lines[0]}");
        }

        // ================================================================
        // #763 — New/Remove opcode editing + any-size Write (realloc + repoint)
        // ================================================================

        // ----------------------------------------------------------------
        // 763.1 SerializeScript appends EXIT when the last opcode != 0x03;
        //       no double-append when the script already ends in EXIT.
        // ----------------------------------------------------------------

        [Fact]
        public void SerializeScript_AppendsExit_WhenLastOpcodeNotExit()
        {
            using var env = new AiDisasmEnv();

            // Single Attack05 (first byte 0x05 != 0x03) -> serialize must add a
            // 16-byte EXIT terminator (WF parity).
            var vm = env.LoadVmAt(Attack05(0x64));
            vm.DisassembleScript();

            byte[] serialized = vm.SerializeScript();
            Assert.Equal(32, serialized.Length);          // 16 (Attack05) + 16 (EXIT)
            Assert.Equal(0x05, serialized[0]);
            Assert.Equal(0x03, serialized[16]);            // appended EXIT opcode
            Assert.Equal(0xFF, serialized[18]);            // EXIT FIXED byte
        }

        [Fact]
        public void SerializeScript_NoDoubleAppend_WhenAlreadyEndsInExit()
        {
            using var env = new AiDisasmEnv();

            // Attack05 + EXIT: last opcode IS 0x03 -> no extra terminator.
            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());
            vm.DisassembleScript();

            byte[] serialized = vm.SerializeScript();
            Assert.Equal(32, serialized.Length);           // unchanged — no double EXIT
            Assert.Equal(0x05, serialized[0]);
            Assert.Equal(0x03, serialized[16]);
        }

        // ----------------------------------------------------------------
        // 763.2 InsertRow grows by one; decoded opcode placed AFTER index;
        //       JisageReorder applied (row offsets re-laid contiguously).
        // ----------------------------------------------------------------

        [Fact]
        public void InsertRow_AddsDecodedOpcodeAfterIndex_AndRebuildsOffsets()
        {
            using var env = new AiDisasmEnv();

            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());
            vm.DisassembleScript();
            Assert.Equal(2, vm.RowCount);

            // Insert a DoNothing (06 00 FF) AFTER row 0.
            string? line = vm.InsertRow(0, HexLine(0x06, 0x00, 0xFF));
            Assert.NotNull(line);
            Assert.Contains("DoNothing", line!);
            Assert.Equal(3, vm.RowCount);

            // The new row is index 1 (after index 0); the EXIT shifted to 2.
            IReadOnlyList<string> rows = vm.GetDisplayLines();
            Assert.Contains("Attack05", rows[0]);
            Assert.Contains("DoNothing", rows[1]);
            Assert.Contains("EXIT", rows[2]);

            // Offsets are re-laid contiguously at CurrentAddr + i*16.
            Assert.Contains($"0x{vm.CurrentAddr:X06}", rows[0]);
            Assert.Contains($"0x{vm.CurrentAddr + 16:X06}", rows[1]);
            Assert.Contains($"0x{vm.CurrentAddr + 32:X06}", rows[2]);
        }

        [Fact]
        public void InsertRow_NegativeIndex_AppendsAtEnd()
        {
            using var env = new AiDisasmEnv();

            var vm = env.LoadVmAt(Attack05(0x64));
            vm.DisassembleScript();
            Assert.Equal(1, vm.RowCount);

            // SelectedIndex < 0 -> Add at the end (WF parity).
            string? line = vm.InsertRow(-1, HexLine(0x06, 0x00, 0xFF));
            Assert.NotNull(line);
            Assert.Contains("DoNothing", line!);
            Assert.Equal(2, vm.RowCount);
            Assert.Contains("DoNothing", vm.GetDisplayLines()[1]);
        }

        [Fact]
        public void InsertRow_InvalidBytes_ReturnsNullAndLeavesModelUnchanged()
        {
            using var env = new AiDisasmEnv();

            var vm = env.LoadVmAt(Attack05(0x64));
            vm.DisassembleScript();
            int before = vm.RowCount;

            Assert.Null(vm.InsertRow(0, ""));                 // empty
            Assert.Null(vm.InsertRow(0, "zz"));               // non-hex
            Assert.Null(vm.InsertRow(0,                       // over one instruction
                string.Join(" ", Enumerable.Range(0, 17).Select(_ => "00"))));
            Assert.Equal(before, vm.RowCount);
        }

        // ----------------------------------------------------------------
        // 763.3 RemoveRow shrinks by one; refuses when only one opcode left.
        // ----------------------------------------------------------------

        [Fact]
        public void RemoveRow_ShrinksByOne_AndRefusesLastInstruction()
        {
            using var env = new AiDisasmEnv();

            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());
            vm.DisassembleScript();
            Assert.Equal(2, vm.RowCount);

            // Remove row 0 -> only EXIT remains.
            Assert.True(vm.RemoveRow(0));
            Assert.Equal(1, vm.RowCount);
            Assert.Contains("EXIT", vm.GetDisplayLines()[0]);

            // Refuse to remove the last remaining instruction (never empty).
            Assert.False(vm.RemoveRow(0));
            Assert.Equal(1, vm.RowCount);

            // Out-of-range index is also refused.
            Assert.False(vm.RemoveRow(99));
        }

        // ----------------------------------------------------------------
        // 763.4 Write after Insert (size grew) with a real UndoData → bytes
        //       appended at a safe addr; rom.p32(BaseAddr) == newAddr;
        //       CurrentAddr / ReadByteCount updated.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteScript_AfterInsert_RelocatesAndRepointsSlot()
        {
            using var env = new AiDisasmEnv();

            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                AIScriptViewModel vm = LoadViaPointerSlot(env,
                    Concat(Attack05(0x64), ExitOpcode(0x00)), out uint baseAddr);
                vm.DisassembleScript();

                uint origCurrent = vm.CurrentAddr;
                uint origByteCount = vm.ReadByteCount; // 32

                // Insert a DoNothing after row 0: 3 opcodes, still ends in EXIT
                // -> serialized length = 48 (grew from 32).
                Assert.NotNull(vm.InsertRow(0, HexLine(0x06, 0x00, 0xFF)));

                var ud = CoreState.Undo!.NewUndoData("ins");
                bool ok;
                using (ROM.BeginUndoScope(ud))
                {
                    ok = vm.WriteScript(ud);
                }
                CoreState.Undo.Push(ud);

                Assert.True(ok);
                // Relocated: CurrentAddr moved off the original slot.
                Assert.NotEqual(origCurrent, vm.CurrentAddr);
                Assert.True(U.isSafetyOffset(vm.CurrentAddr));
                Assert.Equal(48u, vm.ReadByteCount);
                Assert.NotEqual(origByteCount, vm.ReadByteCount);

                // The AI pointer slot now points at the new script location.
                Assert.Equal(vm.CurrentAddr, env.Rom.p32(baseAddr));

                // The relocated body carries the inserted DoNothing + EXIT.
                Assert.Equal(0x05, env.Rom.Data[vm.CurrentAddr + 0]);
                Assert.Equal(0x06, env.Rom.Data[vm.CurrentAddr + 16]);
                Assert.Equal(0x03, env.Rom.Data[vm.CurrentAddr + 32]);
            }
            finally
            {
                CoreState.Undo = prevUndo;
            }
        }

        // ----------------------------------------------------------------
        // 763.5 Write after Remove (size shrank) → relocates + repoints.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteScript_AfterRemove_RelocatesAndRepointsSlot()
        {
            using var env = new AiDisasmEnv();

            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                // Three opcodes ending in EXIT (48 bytes).
                byte[] body = Concat(Attack05(0x64), DoNothingBody(), ExitOpcode(0x00));
                AIScriptViewModel vm = LoadViaPointerSlot(env, body, out uint baseAddr);
                vm.DisassembleScript();
                Assert.Equal(3, vm.RowCount);
                uint origByteCount = vm.ReadByteCount; // 48

                // Remove the middle DoNothing -> 2 opcodes ending in EXIT = 32.
                Assert.True(vm.RemoveRow(1));

                var ud = CoreState.Undo!.NewUndoData("rem");
                bool ok;
                using (ROM.BeginUndoScope(ud))
                {
                    ok = vm.WriteScript(ud);
                }
                CoreState.Undo.Push(ud);

                Assert.True(ok);
                Assert.Equal(32u, vm.ReadByteCount);
                Assert.NotEqual(origByteCount, vm.ReadByteCount);
                Assert.True(U.isSafetyOffset(vm.CurrentAddr));
                Assert.Equal(vm.CurrentAddr, env.Rom.p32(baseAddr));

                // Relocated body: Attack05 then EXIT (DoNothing gone).
                Assert.Equal(0x05, env.Rom.Data[vm.CurrentAddr + 0]);
                Assert.Equal(0x03, env.Rom.Data[vm.CurrentAddr + 16]);
            }
            finally
            {
                CoreState.Undo = prevUndo;
            }
        }

        // ----------------------------------------------------------------
        // 763.6 Undo: a realloc Write under a Core undo scope → RunUndo()
        //       restores rom.p32(BaseAddr) to the ORIGINAL slot value.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteScript_Realloc_IsUndoable_RestoresOriginalSlot()
        {
            using var env = new AiDisasmEnv();

            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                AIScriptViewModel vm = LoadViaPointerSlot(env,
                    Concat(Attack05(0x64), ExitOpcode(0x00)), out uint baseAddr);
                vm.DisassembleScript();

                uint origSlotValue = env.Rom.p32(baseAddr); // points at original script

                Assert.NotNull(vm.InsertRow(0, HexLine(0x06, 0x00, 0xFF)));

                var ud = CoreState.Undo!.NewUndoData("ins");
                using (ROM.BeginUndoScope(ud))
                {
                    Assert.True(vm.WriteScript(ud));
                }
                CoreState.Undo.Push(ud);

                // The slot was repointed away from the original.
                Assert.NotEqual(origSlotValue, env.Rom.p32(baseAddr));

                // Undo: the AI pointer slot returns to the ORIGINAL value.
                CoreState.Undo.RunUndo();
                Assert.Equal(origSlotValue, env.Rom.p32(baseAddr));
            }
            finally
            {
                CoreState.Undo = prevUndo;
            }
        }

        // ----------------------------------------------------------------
        // 763.7 Same-size Write (no structural edit) still in-place: no
        //       relocation, CurrentAddr unchanged.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteScript_SameSizeNoStructuralEdit_StaysInPlace()
        {
            using var env = new AiDisasmEnv();

            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                AIScriptViewModel vm = LoadViaPointerSlot(env,
                    Concat(Attack05(0x64), ExitOpcode(0x00)), out uint baseAddr);
                vm.DisassembleScript();

                uint origCurrent = vm.CurrentAddr;
                uint origByteCount = vm.ReadByteCount;
                uint origSlotValue = env.Rom.p32(baseAddr);

                // Value-only edit keeps the row count (ends in EXIT -> 32 bytes).
                Assert.NotNull(vm.UpdateRow(0, HexLine(0x05, 0x32, 0xFF)));

                var ud = CoreState.Undo!.NewUndoData("same");
                using (ROM.BeginUndoScope(ud))
                {
                    Assert.True(vm.WriteScript(ud));
                }
                CoreState.Undo.Push(ud);

                // Strictly in-place: no relocation, slot untouched.
                Assert.Equal(origCurrent, vm.CurrentAddr);
                Assert.Equal(origByteCount, vm.ReadByteCount);
                Assert.Equal(origSlotValue, env.Rom.p32(baseAddr));
                // The edited byte landed in place.
                Assert.Equal(0x32, env.Rom.Data[origCurrent + 1]);
            }
            finally
            {
                CoreState.Undo = prevUndo;
            }
        }

        // ----------------------------------------------------------------
        // 763.8 Size-changed Write with undoData == null → false, no mutation.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteScript_SizeChanged_NullUndoData_RefusesNoMutation()
        {
            using var env = new AiDisasmEnv();

            AIScriptViewModel vm = LoadViaPointerSlot(env,
                Concat(Attack05(0x64), ExitOpcode(0x00)), out uint baseAddr);
            vm.DisassembleScript();

            uint origSlotValue = env.Rom.p32(baseAddr);
            uint origLength = (uint)env.Rom.Data.Length;

            // Grow the script (Insert) then attempt a Write with NO undoData.
            Assert.NotNull(vm.InsertRow(0, HexLine(0x06, 0x00, 0xFF)));

            Assert.False(vm.WriteScript(null));

            // Nothing relocated, repointed, or grown.
            Assert.Equal(origSlotValue, env.Rom.p32(baseAddr));
            Assert.Equal(origLength, (uint)env.Rom.Data.Length);
        }

        // ----------------------------------------------------------------
        // Copilot review (stale-model data-loss): LoadEntry must reset the
        // editable model, so opcodes from a previously-loaded entry cannot be
        // serialized into / repointed onto the newly-selected entry.
        // ----------------------------------------------------------------

        [Fact]
        public void LoadEntry_ResetsEditableModel_PreventingStaleWrite()
        {
            using var env = new AiDisasmEnv();
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;

            // Load + disassemble an entry: the editable model is populated.
            var body = new System.Collections.Generic.List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());
            vm.DisassembleScript();
            Assert.True(vm.HasDisassembly);

            // Select a different entry via LoadEntry (as OnSelected does). The
            // model must reset so a Write cannot carry the old opcodes over.
            env.PlantBody(body.ToArray(), out uint slot);
            vm.LoadEntry(slot);

            Assert.False(vm.HasDisassembly);          // model cleared
            Assert.Empty(vm.SerializeScript());        // nothing stale to write
            Assert.False(vm.WriteScript(CoreState.Undo?.NewUndoData("x"))); // Write blocked
        }

        // ----------------------------------------------------------------
        // Copilot review: New on an UN-loaded model (no Re-read) must NOT
        // insert — otherwise a later Write would serialize just the new opcode
        // and repoint the AI slot, dropping all existing opcodes.
        // ----------------------------------------------------------------

        [AvaloniaFact]
        public void View_New_WithoutReadList_DoesNotInsert()
        {
            using var env = new AiDisasmEnv();
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;

            var view = new AIScriptView();
            var list = view.FindControl<ListBox>("DisassemblyList");
            var asmBox = view.FindControl<TextBox>("AsmBox");
            Assert.NotNull(list);
            Assert.NotNull(asmBox);

            // No Re-read: the model is empty. Provide a valid opcode anyway.
            asmBox!.Text = HexLine(0x05, 0x32, 0xFF);
            Invoke(view, "New_Click");

            // Guard fired: nothing was inserted.
            var items = list!.ItemsSource as System.Collections.IEnumerable;
            int count = 0;
            if (items != null) foreach (var _ in items) count++;
            Assert.Equal(0, count);
        }

        // ----------------------------------------------------------------
        // 763.9 [AvaloniaFact] View New -> Write persists a relocated script:
        //       rom.p32(BaseAddr) changed + the list grew by one row.
        // ----------------------------------------------------------------

        [AvaloniaFact]
        public void View_New_ThenWrite_RelocatesAndGrowsList()
        {
            using var env = new AiDisasmEnv();

            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                var body = new List<byte>();
                body.AddRange(Attack05(0x64));
                body.AddRange(ExitOpcode(0x00));
                env.PlantBody(body.ToArray(), out uint pointerSlotAddr);

                var view = new AIScriptView();
                var asmBox = view.FindControl<TextBox>("AsmBox");
                var list = view.FindControl<ListBox>("DisassemblyList");
                Assert.NotNull(asmBox);
                Assert.NotNull(list);

                var vmField = typeof(AIScriptView).GetField(
                    "_vm",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(vmField);
                var vm = (AIScriptViewModel)vmField!.GetValue(view)!;

                // Re-assert env's ROM as active (defensive vs shared-collection churn).
                CoreState.ROM = env.Rom;
                vm.LoadEntry(pointerSlotAddr);
                Assert.True(vm.IsLoaded);

                var addressBox = view.FindControl<NumericUpDown>("AddressBox");
                var byteCountBox = view.FindControl<NumericUpDown>("ReadByteCountBox");
                addressBox!.Value = vm.CurrentAddr;
                byteCountBox!.Value = vm.ReadByteCount;

                // Re-read populates the list + model.
                Invoke(view, "ReloadList_Click");
                int rowsBefore = (list!.ItemsSource as IEnumerable<string>)?.Count() ?? 0;
                Assert.Equal(2, rowsBefore);

                uint origSlotValue = env.Rom.p32(vm.BaseAddr);

                // Select row 0, type a new 16-byte instruction, New -> Write.
                list.SelectedIndex = 0;
                asmBox!.Text = HexLine(0x06, 0x00, 0xFF); // DoNothing
                Invoke(view, "New_Click");
                Invoke(view, "Write_Click");

                // The list grew by one row (Attack05 + DoNothing + EXIT = 3).
                int rowsAfter = (list.ItemsSource as IEnumerable<string>)?.Count() ?? 0;
                Assert.Equal(3, rowsAfter);

                // The AI pointer slot was repointed to the relocated script.
                Assert.NotEqual(origSlotValue, env.Rom.p32(vm.BaseAddr));
                Assert.Equal(vm.CurrentAddr, env.Rom.p32(vm.BaseAddr));
            }
            finally
            {
                CoreState.Undo = prevUndo;
            }
        }

        // ----------------------------------------------------------------
        // Helpers for the #763 realloc tests.
        // ----------------------------------------------------------------

        // DoNothing 16-byte body (06 00 FF ...), distinct from ExitOpcode.
        static byte[] DoNothingBody()
        {
            var b = new byte[16];
            b[0] = 0x06; b[1] = 0x00; b[2] = 0xFF;
            return b;
        }

        static byte[] Concat(params byte[][] parts)
        {
            var list = new List<byte>();
            foreach (var p in parts) list.AddRange(p);
            return list.ToArray();
        }

        // Plant a body + pointer slot, follow the slot via LoadEntry (sets
        // BaseAddr / CurrentAddr / ReadByteCount / IsLoaded) and return the VM.
        // The realloc Write path needs a real BaseAddr (the AI pointer slot),
        // which LoadVmAt does NOT set.
        static AIScriptViewModel LoadViaPointerSlot(AiDisasmEnv env, byte[] body, out uint baseAddr)
        {
            env.PlantBody(body, out uint pointerSlotAddr);
            baseAddr = pointerSlotAddr;
            // Defensive (matches the AvaloniaFact below): re-assert the env's
            // ROM + fresh width-16 AI script as the active ones before LoadEntry
            // / DisassembleScript, so the pointer-slot follow and opcode decode
            // resolve deterministically regardless of any cross-test CoreState
            // churn in the shared [Collection("SharedState")] (the full-suite
            // run interleaves classes that reassign CoreState.ROM / AIScript).
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;
            var vm = new AIScriptViewModel();
            vm.LoadEntry(pointerSlotAddr);
            return vm;
        }

        // ----------------------------------------------------------------
        // Headless UI: select row 0 -> Binary Code box shows hex; Update +
        // Write persist the edited byte to rom.Data.
        // ----------------------------------------------------------------

        [AvaloniaFact]
        public void View_SelectRow_PopulatesBinaryCode_AndWritePersists()
        {
            using var env = new AiDisasmEnv();

            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                // Plant Attack05 + EXIT and a pointer slot to it.
                var body = new List<byte>();
                body.AddRange(Attack05(0x64));
                body.AddRange(ExitOpcode(0x00));
                env.PlantBody(body.ToArray(), out uint pointerSlotAddr);

                var view = new AIScriptView();
                var asmBox = view.FindControl<TextBox>("AsmBox");
                var list = view.FindControl<ListBox>("DisassemblyList");
                Assert.NotNull(asmBox);
                Assert.NotNull(list);

                // Drive the view's VM through LoadEntry as a list selection would.
                var vmField = typeof(AIScriptView).GetField(
                    "_vm",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(vmField);
                var vm = (AIScriptViewModel)vmField!.GetValue(view)!;
                // Defensive: constructing the View re-enters list/VM init (the
                // FilterCombo SelectionChanged fires LoadList). Re-assert the
                // env's ROM as the active one before LoadEntry so the pointer
                // slot follow resolves deterministically regardless of any
                // cross-test CoreState churn in the shared collection.
                CoreState.ROM = env.Rom;
                vm.LoadEntry(pointerSlotAddr);
                Assert.True(vm.IsLoaded);

                var addressBox = view.FindControl<NumericUpDown>("AddressBox");
                var byteCountBox = view.FindControl<NumericUpDown>("ReadByteCountBox");
                addressBox!.Value = vm.CurrentAddr;
                byteCountBox!.Value = vm.ReadByteCount;

                // Re-read populates the Disassembly list (and the model).
                Invoke(view, "ReloadList_Click");

                // Select row 0 -> Binary Code box shows non-empty hex.
                list!.SelectedIndex = 0;
                Assert.False(string.IsNullOrWhiteSpace(asmBox!.Text));
                Assert.Contains("05", asmBox.Text!);

                // Hand-edit the probability and Update + Write.
                asmBox.Text = HexLine(0x05, 0x32, 0xFF);
                Invoke(view, "Update_Click");
                Invoke(view, "Write_Click");

                // The Write persisted the edited byte to the ROM.
                Assert.Equal(0x32, env.Rom.Data[vm.CurrentAddr + 1]);
            }
            finally
            {
                CoreState.Undo = prevUndo;
            }
        }

        static void Invoke(AIScriptView view, string method)
        {
            var m = typeof(AIScriptView).GetMethod(
                method,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(m);
            m!.Invoke(view, new object?[] { null, new global::Avalonia.Interactivity.RoutedEventArgs() });
        }

        /// <summary>
        /// Sets a fresh CoreState.Undo for the duration of a write test and
        /// restores the prior value on Dispose. WriteScript itself does not
        /// require an undo (rom.write_range no-ops the ambient scope when none
        /// is open), but tests that want a clean undo buffer use this.
        /// </summary>
        sealed class UndoScope : IDisposable
        {
            readonly Undo? _prev;
            public UndoScope()
            {
                _prev = CoreState.Undo;
                CoreState.Undo = new Undo();
            }
            public void Dispose() => CoreState.Undo = _prev;
        }
    }
}
