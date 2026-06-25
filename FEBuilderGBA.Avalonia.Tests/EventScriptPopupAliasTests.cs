// SPDX-License-Identifier: GPL-3.0-or-later
// EventScriptPopupViewModel arg-ALIAS handling tests (#1422).
//
// The Avalonia Event Script popup must mirror WinForms alias behavior:
//   - OnCommandSelected hides FIXED + alias args (only PRIMARY args are
//     editable rows) — an alias rendered as its own row would let the user
//     leave the alias byte-position stale;
//   - WriteCommand propagates a primary edit to EVERY alias sharing the
//     primary's Symbol (port of EventScriptForm.WriteAliasScriptEditSetTables),
//     so the primary and its alias byte-positions can never diverge;
//   - pointer-typed 4-byte args are stored via write_p32 (GBA pointer form),
//     matching WinForms WriteOneScriptEditSetTables;
//   - a normal no-alias command still writes correctly (regression).
//
// The VM's _disassembledCodes / _commandOffsets are populated via reflection
// with a synthetic OneCode built from a real ParseScriptLine definition (the
// same path installed EVENTSCRIPT_* alias patches take), so the test exercises
// the VM's REAL integration of EventScriptAliasCore without depending on the
// live disassembler matching custom bytes.
//
// Marked [Collection("SharedState")] — mutates CoreState.ROM / CommentCache /
// Undo.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class EventScriptPopupAliasTests : IDisposable
    {
        const uint CmdOffset = 0x200000;

        readonly ROM? _prevRom;
        readonly Undo? _prevUndo;
        readonly object? _prevComment;
        readonly ROM _rom;

        public EventScriptPopupAliasTests()
        {
            _prevRom = CoreState.ROM;
            _prevUndo = CoreState.Undo;
            _prevComment = CoreState.CommentCache;

            _rom = new ROM();
            _rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U
            CoreState.ROM = _rom;
            CoreState.Undo = new Undo();
            CoreState.CommentCache = new HeadlessEtcCache();
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.Undo = _prevUndo;
            CoreState.CommentCache = (IEtcCache?)_prevComment;
        }

        // Inject a synthetic disassembled command into the VM's private state and
        // plant its bytes in the ROM at CmdOffset.
        EventScriptPopupViewModel MakeVmWith(EventScript.Script script, byte[] bytes)
        {
            Array.Copy(bytes, 0, _rom.Data, (int)CmdOffset, bytes.Length);

            var code = new EventScript.OneCode
            {
                Script = script,
                ByteData = (byte[])bytes.Clone(),
                Comment = "",
            };

            var vm = new EventScriptPopupViewModel();
            SetPrivateList(vm, "_disassembledCodes", new List<EventScript.OneCode> { code });
            SetPrivateList(vm, "_commandOffsets", new List<uint> { CmdOffset });
            // The Commands display list needs one entry so index 0 is valid.
            vm.Commands.Add("0x200000: CMD");
            return vm;
        }

        static void SetPrivateList<T>(EventScriptPopupViewModel vm, string field, List<T> value)
        {
            var f = typeof(EventScriptPopupViewModel).GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var list = (System.Collections.IList)f!.GetValue(vm)!;
            list.Clear();
            foreach (var item in value) list.Add(item);
        }

        // "400DXXYY00010000400DXXYY" : X(byte2) Y(byte3) ... X(byte10) Y(byte11).
        // The second X/Y groups become aliases of the first.
        static EventScript.Script AliasScript() =>
            EventScript.ParseScriptLine("400DXXYY00010000400DXXYY\tCMD [X:UNIT:Unit1][Y:UNIT:Unit2]");

        // ----------------------------------------------------------------
        // 1. OnCommandSelected hides FIXED + alias rows — only primaries shown.
        // ----------------------------------------------------------------

        [Fact]
        public void OnCommandSelected_AliasCommand_ShowsOnlyPrimaryRows()
        {
            var script = AliasScript();
            Assert.NotNull(script);
            var bytes = new byte[script.Size];
            var vm = MakeVmWith(script, bytes);

            vm.SelectedCommandIndex = 0; // triggers OnCommandSelected

            // Two editable rows only: primary X + primary Y. Alias rows hidden.
            Assert.Equal(2, vm.CommandArgs.Count);
            var symbols = vm.CommandArgs.Select(a => a.Name).ToList();
            Assert.Contains(symbols, n => n == "Unit1" || n == "X");
            Assert.Contains(symbols, n => n == "Unit2" || n == "Y");

            // Each row maps to a primary arg (Alias == NOT_FOUND).
            foreach (var entry in vm.CommandArgs)
            {
                Assert.True(entry.SourceArgIndex >= 0 && entry.SourceArgIndex < script.Args.Length);
                Assert.Equal(U.NOT_FOUND, script.Args[entry.SourceArgIndex].Alias);
            }
        }

        // ----------------------------------------------------------------
        // 2. WriteCommand propagates a primary edit to the alias byte-position.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteCommand_EditPrimary_PropagatesToAliasBytePosition()
        {
            var script = AliasScript();
            var bytes = new byte[script.Size];
            var vm = MakeVmWith(script, bytes);
            vm.SelectedCommandIndex = 0;

            // Find the primary X row (its alias is at byte 10; primary X is byte 2).
            var xRow = vm.CommandArgs.First(a => script.Args[a.SourceArgIndex].Symbol == 'X');
            int primaryPos = script.Args[xRow.SourceArgIndex].Position; // 2
            int aliasPos = script.Args
                .First(a => a.Symbol == 'X' && a.Alias != U.NOT_FOUND).Position; // 10
            Assert.Equal(2, primaryPos);
            Assert.Equal(10, aliasPos);

            // Edit ONLY the primary row (leave the alias — which isn't even shown).
            xRow.Value = 0x42;

            Assert.True(vm.WriteCommand());

            // Both the primary AND the alias byte-position now hold 0x42 — no stale byte.
            Assert.Equal(0x42, _rom.Data[CmdOffset + primaryPos]);
            Assert.Equal(0x42, _rom.Data[CmdOffset + aliasPos]);
        }

        // ----------------------------------------------------------------
        // 3. Multiple aliases (3 occurrences) all updated.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteCommand_ThreeOccurrences_AllAliasBytesUpdated()
        {
            // X at bytes 2, 6, 10 (three groups of the same symbol). Y at byte 3.
            var script = EventScript.ParseScriptLine(
                "400DXXYY00000000XX000000XX000000\tCMD [X:UNIT:U1][Y:UNIT:U2]");
            Assert.NotNull(script);
            // Sanity: exactly three X groups (one primary + two aliases).
            var xs = script.Args.Where(a => a.Symbol == 'X' && a.Type != EventScript.ArgType.FIXED).ToList();
            Assert.Equal(3, xs.Count);

            var bytes = new byte[script.Size];
            var vm = MakeVmWith(script, bytes);
            vm.SelectedCommandIndex = 0;

            var xRow = vm.CommandArgs.First(a => script.Args[a.SourceArgIndex].Symbol == 'X');
            xRow.Value = 0x7E;
            Assert.True(vm.WriteCommand());

            foreach (var a in xs)
                Assert.Equal(0x7E, _rom.Data[CmdOffset + a.Position]);
        }

        // ----------------------------------------------------------------
        // 4. Regression: a normal no-alias command writes correctly.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteCommand_NoAlias_WritesPrimaryOnly()
        {
            // 400DXXYY : one X (byte2) + one Y (byte3), no repeats → no aliases.
            var script = EventScript.ParseScriptLine("400DXXYY\tCMD [X:UNIT:U1][Y:UNIT:U2]");
            Assert.NotNull(script);
            var bytes = new byte[script.Size];
            var vm = MakeVmWith(script, bytes);
            vm.SelectedCommandIndex = 0;

            Assert.Equal(2, vm.CommandArgs.Count); // X + Y, both primary

            var xRow = vm.CommandArgs.First(a => script.Args[a.SourceArgIndex].Symbol == 'X');
            var yRow = vm.CommandArgs.First(a => script.Args[a.SourceArgIndex].Symbol == 'Y');
            xRow.Value = 0x11;
            yRow.Value = 0x22;
            Assert.True(vm.WriteCommand());

            Assert.Equal(0x11, _rom.Data[CmdOffset + 2]);
            Assert.Equal(0x22, _rom.Data[CmdOffset + 3]);
        }

        // ----------------------------------------------------------------
        // 4b. A duplicated SourceArgIndex (two rows -> same primary) is rejected
        //     even though the count check still passes — guards a 1:1 mapping.
        // ----------------------------------------------------------------

        [Fact]
        public void WriteCommand_DuplicateSourceArgIndex_RefusedNoMutation()
        {
            var script = AliasScript(); // two primaries (X, Y)
            var bytes = new byte[script.Size];
            bytes[2] = 0x01; bytes[3] = 0x02; bytes[10] = 0x01; bytes[11] = 0x02;
            var vm = MakeVmWith(script, bytes);
            vm.SelectedCommandIndex = 0;
            Assert.Equal(2, vm.CommandArgs.Count);

            // Corrupt the mapping: point BOTH rows at the same primary index.
            int dup = vm.CommandArgs[0].SourceArgIndex;
            vm.CommandArgs[1].SourceArgIndex = dup;
            vm.CommandArgs[0].Value = 0x33;

            byte[] before = U.getBinaryData(_rom.Data, CmdOffset, script.Size);
            Assert.False(vm.WriteCommand()); // count matches (2==2) but not 1:1 -> refused
            byte[] after = U.getBinaryData(_rom.Data, CmdOffset, script.Size);
            Assert.Equal(before, after); // no partial mutation
        }

        // ----------------------------------------------------------------
        // 5. The alias write is undoable (class-C undo coverage preserved).
        // ----------------------------------------------------------------

        [Fact]
        public void WriteCommand_AliasPropagation_IsUndoable()
        {
            var script = AliasScript();
            var bytes = new byte[script.Size];
            var vm = MakeVmWith(script, bytes);
            vm.SelectedCommandIndex = 0;

            int primaryPos = 2, aliasPos = 10;
            byte origPrimary = _rom.Data[CmdOffset + primaryPos];
            byte origAlias = _rom.Data[CmdOffset + aliasPos];

            var xRow = vm.CommandArgs.First(a => script.Args[a.SourceArgIndex].Symbol == 'X');
            xRow.Value = 0x55;
            Assert.True(vm.WriteCommand());
            Assert.Equal(0x55, _rom.Data[CmdOffset + primaryPos]);
            Assert.Equal(0x55, _rom.Data[CmdOffset + aliasPos]);

            // Undo restores BOTH byte-positions (the UndoService committed one record).
            CoreState.Undo!.RunUndo();
            Assert.Equal(origPrimary, _rom.Data[CmdOffset + primaryPos]);
            Assert.Equal(origAlias, _rom.Data[CmdOffset + aliasPos]);
        }
    }
}
