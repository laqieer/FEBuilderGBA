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
//   - WriteScript() writes the same-size slice through rom.write_u8 (so the
//     surrounding UndoService scope tracks it), and refuses any length change
//     or out-of-range slice;
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

            byte[] serialized = vm.SerializeScript();
            Assert.Equal(16, serialized.Length);
            Assert.Equal(0x05, serialized[0]);
            Assert.Equal(0x32, serialized[1]);
            Assert.Equal(0xFF, serialized[2]);
            for (int i = 3; i < 16; i++)
                Assert.Equal(0x00, serialized[i]);
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
        /// require an undo (rom.write_u8 no-ops the ambient scope when none is
        /// open), but tests that want a clean undo buffer use this.
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
