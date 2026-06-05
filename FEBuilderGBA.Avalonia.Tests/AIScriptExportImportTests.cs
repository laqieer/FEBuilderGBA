// SPDX-License-Identifier: GPL-3.0-or-later
// AIScriptViewModel full byte-stream Export / Import tests (#965).
//
// Proves the Avalonia AI Script editor can dump the loaded script to the
// WF-compatible per-opcode text format and re-import that text losslessly:
//   - ExportToText() emits one line per 16-byte opcode: continuous upper-hex
//     bytes + "\t//" + script name + [arg:value] tokens (+ trailing comment),
//     matching the leading-hex form WF AIScriptForm.EventToTextAll writes and
//     FileToEvent reads;
//   - ImportFromText() parses each line's LEADING hex (stopping at the comment),
//     skips blank / comment-only / <4-byte lines, pads to the fixed 16-byte AI
//     width, decodes via AIScript.DisAseemble, and REPLACES the model — without
//     touching the ROM (Write persists);
//   - Export -> Import -> Export round-trips to byte-identical text, on a
//     synthetic FE8U fixture AND on a real ROM's AI1 script (when present);
//   - an unknown 16-byte opcode round-trips losslessly (Copilot review #4);
//   - malformed input (blank + comment-only + odd-nibble + over-length lines)
//     is tolerated and yields exactly the valid opcode count (Copilot #3).
//
// Reuses the AiDisasmEnv synthetic FE8U environment from
// AIScriptDisassemblyTests.cs. Marked [Collection("SharedState")] because the
// suite mutates CoreState.ROM / CoreState.AIScript / CoreState.CommentCache /
// CoreState.BaseDirectory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AIScriptExportImportTests
    {
        readonly ITestOutputHelper _output;

        public AIScriptExportImportTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // 16-byte AI instruction helpers (FE8 table). Attack05's byte[1] is the
        // PROBABILITY arg; byte[2]=0xFF is FIXED.
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

        static byte[] DoNothing()
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

        // ----------------------------------------------------------------
        // 1. Export line shape (golden): hex prefix + \t// + script name + arg.
        // ----------------------------------------------------------------

        [Fact]
        public void ExportToText_GoldenLineShape_HexThenTabCommentNameAndArg()
        {
            using var env = new AiDisasmEnv();

            // Attack05(0x64) + EXIT(0x00): probability arg renders as [name:val].
            var vm = env.LoadVmAt(Concat(Attack05(0x64), ExitOpcode(0x00)));
            vm.DisassembleScript();

            string text = vm.ExportToText();
            string[] lines = text.Split('\n');

            // Two opcode lines + a trailing empty entry (each line ends with \n).
            Assert.Equal(0x05, Convert.ToByte(lines[0].Substring(0, 2), 16));
            // Hex prefix is 32 chars (16 bytes), then a TAB, then "//".
            Assert.Equal('\t', lines[0][32]);
            Assert.StartsWith("//", lines[0].Substring(33));
            // The script name appears right after //.
            Assert.Contains("Attack05", lines[0]);
            // The probability arg is surfaced as a [name:value] token. 0x64=100.
            Assert.True(lines[0].Contains("0x64") || lines[0].Contains(":100"),
                $"Expected probability token in row, got: {lines[0]}");
            // Bytes are uppercase 2-digit hex with no separators: opcode 0x05,
            // probability 0x64, FIXED 0xFF.
            Assert.Equal("0564FF", lines[0].Substring(0, 6));

            // Second line is the EXIT opcode.
            Assert.Contains("EXIT", lines[1]);
            Assert.Equal("0300FF", lines[1].Substring(0, 6));

            // Trailing entry after the final '\n' is empty.
            Assert.Equal("", lines[2]);
        }

        [Fact]
        public void ExportToText_RowComment_EmittedWhenPresent_OmittedWhenAbsent()
        {
            using var env = new AiDisasmEnv();

            // No comment -> the exported line has NO trailing "  //".
            var vmNoComment = env.LoadVmAt(Attack05(0x64));
            vmNoComment.DisassembleScript();
            string exportNoComment = vmNoComment.ExportToText().Split('\n')[0];
            Assert.DoesNotContain("  //", exportNoComment);

            // Seed a per-row comment at the script offset (DisAseemble reads it
            // from CommentCache.At(offset)), re-disassemble, and assert the
            // exported line carries the comment as a trailing "  //<comment>".
            const string commentText = "guard-the-throne";
            var cache = CoreState.CommentCache as HeadlessEtcCache;
            Assert.NotNull(cache);
            var vm = env.LoadVmAt(Attack05(0x64));
            cache!.Update(vm.CurrentAddr, commentText);
            vm.DisassembleScript();

            string exportWithComment = vm.ExportToText().Split('\n')[0];
            Assert.Contains("  //" + commentText, exportWithComment);
        }

        // ----------------------------------------------------------------
        // 2. Synthetic round-trip: Export -> Import -> Export is byte-identical.
        // ----------------------------------------------------------------

        [Fact]
        public void RoundTrip_Synthetic_ExportImportExport_IsByteIdentical()
        {
            using var env = new AiDisasmEnv();

            byte[] body = Concat(Attack05(0x64), DoNothing(), ExitOpcode(0x00));
            var vm = env.LoadVmAt(body);
            vm.DisassembleScript();

            string export1 = vm.ExportToText();
            byte[] serialized1 = vm.SerializeScript();

            int count = vm.ImportFromText(export1);
            Assert.Equal(3, count);

            string export2 = vm.ExportToText();
            Assert.Equal(export1, export2);

            // The serialized opcode bytes are unchanged by the round-trip.
            byte[] serialized2 = vm.SerializeScript();
            Assert.Equal(serialized1, serialized2);
        }

        // ----------------------------------------------------------------
        // 3. Unknown opcode round-trips losslessly (Copilot review #4).
        // ----------------------------------------------------------------

        [Fact]
        public void RoundTrip_UnknownOpcode_StaysLossless()
        {
            using var env = new AiDisasmEnv();

            // 0xEE > max AI opcode -> falls through to the Unknown (WORD) script.
            byte[] unknown = new byte[16];
            for (int i = 0; i < 16; i++) unknown[i] = 0xEE;
            var vm = env.LoadVmAt(Concat(unknown, ExitOpcode(0x00)));
            vm.DisassembleScript();

            string export1 = vm.ExportToText();
            // The unknown row must still carry its full 16-byte hex prefix.
            string firstLine = export1.Split('\n')[0];
            Assert.Equal("EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE", firstLine.Substring(0, 32));

            Assert.Equal(2, vm.ImportFromText(export1));
            Assert.Equal(export1, vm.ExportToText());
            Assert.Equal(0xEE, vm.SerializeScript()[0]);
        }

        // ----------------------------------------------------------------
        // 4. Malformed-line tolerance (Copilot review #3).
        // ----------------------------------------------------------------

        [Fact]
        public void ImportFromText_MalformedLines_ToleratedAndCountsValidOnly()
        {
            using var env = new AiDisasmEnv();

            // Seed a loaded model so ImportFromText has a live AIScript.
            var vm = env.LoadVmAt(ExitOpcode(0x00));
            vm.DisassembleScript();

            // A file mixing: blank lines, a comment-only line, an odd-nibble
            // line, an over-length line (>16 bytes => 17 bytes), a <4-byte line,
            // and ONE valid opcode.
            string oversize = string.Concat(Enumerable.Repeat("00", 17)); // 34 hex chars
            string text = string.Join("\n", new[]
            {
                "",                                  // blank
                "   ",                               // whitespace-only -> no hex
                "// just a comment",                 // comment-only (starts non-hex)
                "0500FF",                            // 3 bytes < 4 => skipped
                "0500F",                             // odd nibble after 2 pairs -> 2 bytes < 4 => skipped
                oversize,                            // 17 bytes > 16 => rejected
                "0500FF00\t//Attack05[probability:0x00]",  // VALID (4 bytes -> padded to 16)
            });

            int count = vm.ImportFromText(text);
            Assert.Equal(1, count);
            Assert.Equal(1, vm.RowCount);
            Assert.Contains("Attack05", vm.GetDisplayLines()[0]);
            // The single imported opcode is right-padded to a full 16-byte slot.
            Assert.Equal(16, vm.SerializeScript().Length - 16 /* WF EXIT append */);
        }

        [Fact]
        public void ImportFromText_AllInvalid_ReturnsZeroAndLeavesModelUnchanged()
        {
            using var env = new AiDisasmEnv();

            byte[] body = Concat(Attack05(0x64), ExitOpcode(0x00));
            var vm = env.LoadVmAt(body);
            vm.DisassembleScript();
            byte[] before = vm.SerializeScript();
            int rowsBefore = vm.RowCount;

            // Nothing parseable: comments + blanks + short lines only.
            string text = "\n// comment\n   \nAB\n";
            Assert.Equal(0, vm.ImportFromText(text));

            // Model untouched.
            Assert.Equal(rowsBefore, vm.RowCount);
            Assert.Equal(before, vm.SerializeScript());
        }

        // ----------------------------------------------------------------
        // 5. Import accepts the exact exported format (comment-on-line dropped).
        // ----------------------------------------------------------------

        [Fact]
        public void ImportFromText_ParsesExportedHexAndIgnoresCommentTail()
        {
            using var env = new AiDisasmEnv();

            var vm = env.LoadVmAt(ExitOpcode(0x00));
            vm.DisassembleScript();

            // A hand-authored export-format line: 16-byte hex, TAB, // comment.
            string hex = string.Concat(Attack05(0x64).Select(b => b.ToString("X2")));
            string line = hex + "\t//Attack05[probability:0x64]";

            Assert.Equal(1, vm.ImportFromText(line));
            // Only the leading hex was consumed: probability byte is 0x64.
            Assert.Equal(0x64, vm.SerializeScript()[1]);
            Assert.Contains("Attack05", vm.GetDisplayLines()[0]);
        }

        // ----------------------------------------------------------------
        // 6. ExportToText lazily disassembles a loaded-but-unrefreshed model
        //    (Copilot review #2 — never export an empty script after load).
        // ----------------------------------------------------------------

        [Fact]
        public void ExportToText_LoadedButNotDisassembled_LazilyPopulates()
        {
            using var env = new AiDisasmEnv();

            // LoadVmAt sets IsLoaded / CurrentAddr / ReadByteCount but does NOT
            // disassemble. ExportToText must lazily fill the model.
            var vm = env.LoadVmAt(Concat(Attack05(0x64), ExitOpcode(0x00)));
            Assert.False(vm.HasDisassembly);

            string text = vm.ExportToText();
            Assert.False(string.IsNullOrEmpty(text));
            Assert.Contains("Attack05", text);
            Assert.True(vm.HasDisassembly);
        }

        [Fact]
        public void ExportToText_NothingLoaded_ReturnsEmpty()
        {
            using var env = new AiDisasmEnv();

            var vm = new AIScriptViewModel { IsLoaded = false, CurrentAddr = 0 };
            Assert.Equal("", vm.ExportToText());
        }

        // ----------------------------------------------------------------
        // 7. File round-trip via the View's Export/Import handlers (#965 GUI).
        // ----------------------------------------------------------------

        [AvaloniaFact]
        public void View_ImportFromFile_PopulatesDisassemblyList()
        {
            using var env = new AiDisasmEnv();
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;

            // Author an export-format file on disk (3 opcodes).
            byte[] body = Concat(Attack05(0x64), DoNothing(), ExitOpcode(0x00));
            var seed = env.LoadVmAt(body);
            seed.DisassembleScript();
            string exported = seed.ExportToText();

            string tmp = Path.Combine(Path.GetTempPath(),
                $"aiscript_import_{Guid.NewGuid():N}.txt");
            File.WriteAllText(tmp, exported);
            try
            {
                var view = new AIScriptView();
                var list = view.FindControl<ListBox>("DisassemblyList");
                Assert.NotNull(list);

                // Drive the View's own VM through ImportFromText exactly as
                // Import_Click does after the file picker resolves a path, then
                // refresh the list the same way the handler does.
                var vmField = typeof(AIScriptView).GetField(
                    "_vm",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(vmField);
                var vm = (AIScriptViewModel)vmField!.GetValue(view)!;

                int count = vm.ImportFromText(File.ReadAllText(tmp));
                Assert.Equal(3, count);

                list!.ItemsSource = vm.GetDisplayLines();
                var rows = (list.ItemsSource as IEnumerable<string>)?.ToList()
                           ?? new List<string>();
                Assert.Equal(3, rows.Count);
                Assert.Contains(rows, r => r.Contains("Attack05"));
                Assert.Contains(rows, r => r.Contains("DoNothing"));
                Assert.Contains(rows, r => r.Contains("EXIT"));
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* best effort */ }
            }
        }

        // ----------------------------------------------------------------
        // 8. Real-ROM round-trip: load an FE8U AI1 script, Export -> Import ->
        //    Export => byte-identical. Skips when FE8U.gba is unavailable.
        // ----------------------------------------------------------------

        [Fact]
        public void RoundTrip_RealRomFE8U_Ai1Script_ExportImportExport_Identical()
        {
            string? romPath = TestRomLocator.FindRom("FE8U");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found (ROMS_DIR / roms/).");
                return;
            }

            ROM? prevRom = CoreState.ROM;
            EventScript? prevAi = CoreState.AIScript;
            IEtcCache? prevComment = CoreState.CommentCache;
            string? prevBaseDir = CoreState.BaseDirectory;
            try
            {
                string asmDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                CoreState.BaseDirectory = asmDir;

                var rom = new ROM();
                if (!rom.Load(romPath, out _))
                {
                    _output.WriteLine("SKIP: FE8U.gba failed to load.");
                    return;
                }
                CoreState.ROM = rom;
                CoreState.CommentCache = new HeadlessEtcCache();
                var ai = new EventScript(16);
                ai.Load(EventScript.EventScriptType.AI);
                CoreState.AIScript = ai;

                // Walk the AI1 table for the FIRST non-empty script slot.
                var vm = new AIScriptViewModel { FilterIndex = 0 };
                List<AddrResult> list = vm.LoadList();
                Assert.NotEmpty(list);

                bool tested = false;
                foreach (AddrResult entry in list)
                {
                    vm.LoadEntry(entry.addr);
                    if (!vm.IsLoaded || vm.ReadByteCount == 0) continue;

                    var rows = vm.DisassembleScript();
                    if (rows.Count == 0) continue;

                    string export1 = vm.ExportToText();
                    if (string.IsNullOrEmpty(export1)) continue;

                    int count = vm.ImportFromText(export1);
                    Assert.True(count > 0);

                    string export2 = vm.ExportToText();
                    Assert.Equal(export1, export2);
                    tested = true;
                    _output.WriteLine(
                        $"FE8U AI1 slot 0x{entry.addr:X06} -> {count} opcodes, round-trip identical.");
                    break;
                }

                Assert.True(tested, "No non-empty FE8U AI1 script found to round-trip.");
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.AIScript = prevAi;
                CoreState.CommentCache = prevComment;
                CoreState.BaseDirectory = prevBaseDir;
            }
        }
    }
}
