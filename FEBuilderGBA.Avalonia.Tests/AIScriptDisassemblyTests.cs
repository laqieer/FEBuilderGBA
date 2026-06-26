// SPDX-License-Identifier: GPL-3.0-or-later
// AIScriptViewModel.DisassembleScript regression tests (#757).
//
// Proves the Avalonia AI Script editor shows REAL opcode disassembly
// (mnemonic + decoded args + comment) instead of a raw 16-byte hex dump,
// and that AI scripts decode on a FIXED 16-byte instruction grid:
//   - a known opcode renders its mnemonic / args (not a bare hex line);
//   - an UNKNOWN opcode consumes exactly ONE 16-byte row (width-16
//     regression — it would be four 4-byte rows if the unknown width
//     were the default 4);
//   - EXIT (0x03) followed by a 0x1B/0x1C continuation keeps BOTH slots
//     (the CalcScriptLength range is respected, no early stop);
//   - a ReadByteCount that is not a multiple of 16 surfaces an explicit
//     [partial ...] row;
//   - a malformed DisAseemble throw degrades to a [Disassembly error]
//     row and terminates the loop (no hang).
//
// Marked [Collection("SharedState")] because these tests mutate
// CoreState.ROM / CoreState.AIScript / CoreState.BaseDirectory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AIScriptDisassemblyTests
    {
        // The old hex-dump format every row used to take: "0xADDR: NN NN NN ...".
        // Disassembled rows must NOT match this (they carry a mnemonic).
        static readonly Regex OldHexDumpRow =
            new Regex(@"^0x[0-9A-Fa-f]+: ([0-9A-Fa-f]{2} )+$");

        // ----------------------------------------------------------------
        // 1. Known opcode renders mnemonic + args (NOT a bare hex line).
        // ----------------------------------------------------------------

        [Fact]
        public void DisassembleScript_KnownOpcode_RendersMnemonicAndArgs()
        {
            using var env = new AiDisasmEnv();

            // EXIT opcode: 03 00 FF 11 + 12 zero bytes. byte[3]=0x11 is the
            // EXIT "id" arg, so the row must show both "EXIT" and "id".
            byte[] body = ExitOpcode(0x11);
            var vm = env.LoadVmAt(body);

            IReadOnlyList<string> rows = vm.DisassembleScript();

            Assert.NotEmpty(rows);
            string first = rows[0];
            Assert.DoesNotMatch(OldHexDumpRow, first);
            Assert.Contains("EXIT", first);
            // Decoded arg name (id) and its value (0x11) must be present.
            Assert.Contains("id=", first);
            Assert.Contains("0x11", first);
        }

        [Fact]
        public void DisassembleScript_KnownOpcode_DoNothing_RendersMnemonic()
        {
            using var env = new AiDisasmEnv();

            // DoNothing opcode: 06 00 FF 00 + 12 zero bytes. No non-FIXED args.
            byte[] body = DoNothingOpcode();
            var vm = env.LoadVmAt(body);

            IReadOnlyList<string> rows = vm.DisassembleScript();

            Assert.Single(rows);
            Assert.DoesNotMatch(OldHexDumpRow, rows[0]);
            Assert.Contains("DoNothing", rows[0]);
        }

        // ----------------------------------------------------------------
        // 2. Width-16 regression: an UNKNOWN opcode consumes ONE 16-byte row.
        // ----------------------------------------------------------------

        [Fact]
        public void DisassembleScript_UnknownOpcode_ConsumesExactlyOne16ByteRow()
        {
            using var env = new AiDisasmEnv();

            // 0xEE is > the max AI opcode (0x1C) so it matches NO known
            // script. All-0xEE fills also fail every pointer-arg match, so
            // the row falls through to the Unknown script. With width-16,
            // that's ONE row; with the (wrong) default width-4 it would be
            // FOUR rows.
            byte[] body = new byte[16];
            for (int i = 0; i < 16; i++) body[i] = 0xEE;

            var vm = env.LoadVmAt(body);
            IReadOnlyList<string> rows = vm.DisassembleScript();

            Assert.Single(rows);
            Assert.Equal(16u, vm.ReadByteCount);
        }

        // ----------------------------------------------------------------
        // 3. EXIT (0x03) + 0x1B / 0x1C continuation -> both slots present.
        // ----------------------------------------------------------------

        [Fact]
        public void DisassembleScript_ExitThen0x1BContinuation_KeepsBothSlots()
        {
            using var env = new AiDisasmEnv();

            // EXIT (03 00 FF 00) followed by NoOp (1B 00 FF 22). CalcLength
            // keeps walking past EXIT because 0x1B is a continuation opcode.
            var body = new List<byte>();
            body.AddRange(ExitOpcode(0x00));
            body.AddRange(OpcodeWithArg(0x1B, 0x22)); // NoOp
            body.AddRange(ExitOpcode(0x00));          // real terminator

            var vm = env.LoadVmAt(body.ToArray());
            IReadOnlyList<string> rows = vm.DisassembleScript();

            // At least the EXIT slot and the 0x1B continuation slot must be
            // present (range respected, no early stop on the first EXIT).
            Assert.True(rows.Count >= 2,
                $"Expected >=2 rows (EXIT + continuation), got {rows.Count}");
            Assert.Contains("EXIT", rows[0]);
            Assert.Contains(rows, r => r.Contains("NoOp"));
        }

        [Fact]
        public void DisassembleScript_ExitThen0x1CContinuation_KeepsBothSlots()
        {
            using var env = new AiDisasmEnv();

            var body = new List<byte>();
            body.AddRange(ExitOpcode(0x00));
            body.AddRange(OpcodeWithArg(0x1C, 0x05)); // FE8LABEL
            body.AddRange(ExitOpcode(0x00));

            var vm = env.LoadVmAt(body.ToArray());
            IReadOnlyList<string> rows = vm.DisassembleScript();

            Assert.True(rows.Count >= 2,
                $"Expected >=2 rows (EXIT + 0x1C continuation), got {rows.Count}");
            Assert.Contains("EXIT", rows[0]);
            Assert.Contains(rows, r => r.Contains("FE8LABEL"));
        }

        // ----------------------------------------------------------------
        // 4. ReadByteCount NOT a multiple of 16 -> explicit [partial ...] row.
        // ----------------------------------------------------------------

        [Fact]
        public void DisassembleScript_PartialRemainder_AppendsPartialRow()
        {
            using var env = new AiDisasmEnv();

            // One full DoNothing (16 bytes) + 5 trailing bytes = 21 bytes.
            byte[] body = DoNothingOpcode();
            var vm = env.LoadVmAt(body);
            // Force a ReadByteCount that is NOT a multiple of 16 (16 + 5).
            vm.ReadByteCount = 21;

            IReadOnlyList<string> rows = vm.DisassembleScript();

            Assert.Equal(2, rows.Count);
            Assert.Contains("DoNothing", rows[0]);
            Assert.Contains("[partial 16-byte instruction", rows[1]);
            Assert.Contains("5 bytes", rows[1]);
        }

        // ----------------------------------------------------------------
        // 5. Malformed / DisAseemble-throws -> [Disassembly error] + stop.
        // ----------------------------------------------------------------

        [Fact]
        public void DisassembleScript_DisAseembleThrows_AddsErrorRowAndStops()
        {
            using var env = new AiDisasmEnv();

            byte[] body = DoNothingOpcode();
            var vm = env.LoadVmAt(body);
            vm.ReadByteCount = 16;

            // Drive a REAL throw through the production code path: EventScript.DisAseemble
            // calls `CoreState.CommentCache?.At(addr)` on a match. (#1585 made that call
            // null-safe — a NULL cache no longer throws — so we install a comment cache
            // whose At() THROWS instead, which still propagates a throw out of DisAseemble.)
            // The VM loop must catch it, append a [Disassembly error] row, and terminate.
            IEtcCache? prev = CoreState.CommentCache;
            try
            {
                CoreState.CommentCache = new ThrowingEtcCache();
                IReadOnlyList<string> rows = vm.DisassembleScript();
                Assert.Single(rows);
                Assert.Contains("[Disassembly error]", rows[0]);
            }
            finally
            {
                CoreState.CommentCache = prev;
            }
        }

        /// <summary>An IEtcCache whose comment lookup throws, used to drive a real
        /// DisAseemble throw through the production path now that a null CommentCache is
        /// handled gracefully (#1585).</summary>
        sealed class ThrowingEtcCache : IEtcCache
        {
            public void RemoveOverRange(uint range) { }
            public void RemoveRange(uint start, uint end) { }
            public bool CheckFast(uint num) => false;
            public string At(uint num, string def = "") => throw new InvalidOperationException("boom");
            public string S_At(uint num) => throw new InvalidOperationException("boom");
            public bool TryGetValue(uint num, out string out_data) { out_data = ""; return false; }
            public void Update(uint addr, string comment) { }
            public void Remove(uint addr) { }
        }

        [Fact]
        public void DisassembleScript_NotLoaded_ReturnsEmpty()
        {
            using var env = new AiDisasmEnv();
            byte[] body = DoNothingOpcode();
            var vm = env.LoadVmAt(body);

            // Guard: IsLoaded == false short-circuits to an empty list.
            vm.IsLoaded = false;
            Assert.Empty(vm.DisassembleScript());

            // Guard: CurrentAddr == 0 also short-circuits.
            vm.IsLoaded = true;
            vm.CurrentAddr = 0;
            Assert.Empty(vm.DisassembleScript());
        }

        [Fact]
        public void DisassembleScript_PathologicalRange_DoesNotOverflowOrCrash()
        {
            using var env = new AiDisasmEnv();
            byte[] body = DoNothingOpcode();
            var vm = env.LoadVmAt(body);

            // Hand-typed worst case: CurrentAddr + ReadByteCount near
            // uint.MaxValue. Naive `off + 16 <= end` would wrap and call
            // DisAseemble out of bounds; the (ulong) clamp must keep `end`
            // at the ROM length and skip the loop entirely (no row, no
            // crash). CurrentAddr is non-zero / past ROM end here.
            vm.CurrentAddr = 0xFFFFFFF0;
            vm.ReadByteCount = 0x7FFFFFFF;

            IReadOnlyList<string> rows = vm.DisassembleScript();
            Assert.Empty(rows);
        }

        // ----------------------------------------------------------------
        // 7. Headless UI: AIScriptView.ReloadList_Click populates the
        //    Disassembly ListBox with REAL opcode rows (not the old hex dump).
        // ----------------------------------------------------------------

        [AvaloniaFact]
        public void View_ReloadList_PopulatesDisassemblyWithDecodedRows()
        {
            using var env = new AiDisasmEnv();
            // Plant a real EXIT opcode (03 00 FF 09) + a pointer slot to it.
            env.PlantBody(ExitOpcode(0x09), out uint pointerSlotAddr);

            var view = new AIScriptView();
            var addressBox = view.FindControl<NumericUpDown>("AddressBox");
            var byteCountBox = view.FindControl<NumericUpDown>("ReadByteCountBox");
            var list = view.FindControl<ListBox>("DisassemblyList");
            Assert.NotNull(addressBox);
            Assert.NotNull(byteCountBox);
            Assert.NotNull(list);

            // Drive the view's own VM through LoadEntry (sets IsLoaded /
            // CurrentAddr / ReadByteCount) exactly as a list selection would.
            var vmField = typeof(AIScriptView).GetField(
                "_vm",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(vmField);
            var vm = (AIScriptViewModel)vmField!.GetValue(view)!;
            vm.LoadEntry(pointerSlotAddr);
            Assert.True(vm.IsLoaded);

            // Mirror the UI: the boxes carry the resolved address / length.
            addressBox!.Value = vm.CurrentAddr;
            byteCountBox!.Value = vm.ReadByteCount;

            // The "re-read" handler is private; invoke it the same way the
            // Reload button click would.
            var handler = typeof(AIScriptView).GetMethod(
                "ReloadList_Click",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(handler);
            handler!.Invoke(view, new object?[] { null, new global::Avalonia.Interactivity.RoutedEventArgs() });

            var rows = (list!.ItemsSource as IEnumerable<string>)?.ToList()
                       ?? new List<string>();
            Assert.NotEmpty(rows);
            // Rows must be decoded mnemonics, NOT the old bare hex dump.
            Assert.All(rows, r => Assert.DoesNotMatch(OldHexDumpRow, r));
            Assert.Contains(rows, r => r.Contains("EXIT"));
        }

        // ----------------------------------------------------------------
        // Opcode body helpers (16-byte AI instruction slots, FE8 table).
        // ----------------------------------------------------------------

        // EXIT: 03 00 FF XX 00... -> "EXIT [XX::id]".
        static byte[] ExitOpcode(byte id)
        {
            var b = new byte[16];
            b[0] = 0x03; b[1] = 0x00; b[2] = 0xFF; b[3] = id;
            return b;
        }

        // DoNothing: 06 00 FF 00 00... -> "DoNothing" (no non-FIXED args).
        static byte[] DoNothingOpcode()
        {
            var b = new byte[16];
            b[0] = 0x06; b[1] = 0x00; b[2] = 0xFF;
            return b;
        }

        // Generic single-arg opcode: OP 00 FF XX 00... (matches the 0x1B /
        // 0x1C continuation formats: "1B00FFXX..." / "1C00FFXX...").
        static byte[] OpcodeWithArg(byte op, byte arg)
        {
            var b = new byte[16];
            b[0] = op; b[1] = 0x00; b[2] = 0xFF; b[3] = arg;
            return b;
        }
    }

    /// <summary>
    /// Self-contained synthetic FE8U environment for AI disassembly tests.
    /// Sets CoreState.ROM to a tiny FE8 ROM, CoreState.BaseDirectory to the
    /// test output dir (where config/ is copied), wires a HeadlessEtcCache
    /// for the comment lookup DisAseemble does, and loads a fresh
    /// width-16 CoreState.AIScript against the FE8 AI definitions. Restores
    /// the prior CoreState on Dispose.
    /// </summary>
    sealed class AiDisasmEnv : IDisposable
    {
        const uint ScriptBase = 0x200000; // in-ROM offset for the script body

        readonly ROM? _prevRom;
        readonly EventScript? _prevAi;
        readonly IEtcCache? _prevComment;
        readonly string? _prevBaseDir;

        readonly ROM _rom;
        EventScript? _ai;

        /// <summary>The synthetic FE8U ROM backing this environment (#760
        /// edit/write tests inspect rom.Data directly to verify in-place
        /// writes / undo).</summary>
        public ROM Rom => _rom;

        /// <summary>The fresh width-16 FE8 AI EventScript this env loaded (#763
        /// realloc tests re-assert it as CoreState.AIScript before decoding so
        /// a cross-test stale-but-populated script in the shared collection
        /// cannot break DisassembleScript's lazy-load guard).</summary>
        public EventScript? AiScript => _ai;

        /// <summary>Copy a [addr, addr+length) slice of the ROM, for
        /// before/after byte comparisons in the #760 write tests.</summary>
        public byte[] RomSlice(uint addr, uint length)
        {
            var slice = new byte[(int)length];
            Array.Copy(_rom.Data, (int)addr, slice, 0, (int)length);
            return slice;
        }

        public AiDisasmEnv()
        {
            _prevRom = CoreState.ROM;
            _prevAi = CoreState.AIScript;
            _prevComment = CoreState.CommentCache;
            _prevBaseDir = CoreState.BaseDirectory;

            // BaseDirectory must point at the test output dir so the AI
            // config (config/data/aiscript_FE8.*.txt) resolves.
            string asmDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            CoreState.BaseDirectory = asmDir;

            _rom = new ROM();
            _rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U

            CoreState.ROM = _rom;
            // DisAseemble reads CommentCache.At(offset); a HeadlessEtcCache
            // returns "" and never NREs.
            CoreState.CommentCache = new HeadlessEtcCache();

            // Fresh width-16 AI script against THIS FE8 ROM (deterministic
            // regardless of whatever ROM the shared fixture loaded).
            var ai = new EventScript(16);
            ai.Load(EventScript.EventScriptType.AI);
            CoreState.AIScript = ai;
            _ai = ai;
        }

        /// <summary>
        /// Plant the given AI script body at the fixed in-ROM offset, build
        /// an AIScriptViewModel pointing at it, and set IsLoaded /
        /// CurrentAddr / ReadByteCount as LoadEntry would. Callers that need
        /// a partial remainder simply override vm.ReadByteCount afterward
        /// (the trailing ROM bytes are already valid zero bytes).
        /// </summary>
        public AIScriptViewModel LoadVmAt(byte[] body)
        {
            Array.Copy(body, 0, _rom.Data, (int)ScriptBase, body.Length);

            var vm = new AIScriptViewModel
            {
                CurrentAddr = ScriptBase,
                ReadByteCount = (uint)body.Length,
                IsLoaded = true,
            };
            return vm;
        }

        const uint PointerSlot = 0x100000; // in-ROM pointer slot for LoadEntry

        /// <summary>
        /// Plant a body at the fixed in-ROM offset AND a pointer slot that
        /// points at it (GBA address 0x08000000 + ScriptBase). Returns the
        /// pointer-slot address so the headless view test can drive the
        /// VM's LoadEntry (which follows the slot, computes the byte length
        /// via CalcScriptLength, and sets IsLoaded). Used by the headless
        /// view test, which drives the View instead of the VM directly.
        /// </summary>
        public void PlantBody(byte[] body, out uint pointerSlotAddr)
        {
            Array.Copy(body, 0, _rom.Data, (int)ScriptBase, body.Length);
            // Plant the pointer slot -> 0x08000000 + ScriptBase.
            uint gbaPtr = 0x08000000u + ScriptBase;
            // PointerSlot is uint; index with explicit (int) for clarity and to
            // match the (int)ScriptBase cast used elsewhere in this file.
            _rom.Data[(int)PointerSlot + 0] = (byte)(gbaPtr & 0xFF);
            _rom.Data[(int)PointerSlot + 1] = (byte)((gbaPtr >> 8) & 0xFF);
            _rom.Data[(int)PointerSlot + 2] = (byte)((gbaPtr >> 16) & 0xFF);
            _rom.Data[(int)PointerSlot + 3] = (byte)((gbaPtr >> 24) & 0xFF);
            pointerSlotAddr = PointerSlot;
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.AIScript = _prevAi;
            CoreState.CommentCache = _prevComment;
            // Restore unconditionally (including null) so a null prior value is
            // not leaked as the overridden test dir into later tests.
            CoreState.BaseDirectory = _prevBaseDir;
        }
    }
}
