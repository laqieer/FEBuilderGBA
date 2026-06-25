// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for EventScriptAliasCore (#1422) — the pure READ-ONLY helper that drives
// the cross-platform Event Script popup editor's alias handling.
//
// Ground truth (WinForms): EventScript.IsFixedArg drops FIXED + alias args from
// the editable parameter list; EventScriptForm.WriteAliasScriptEditSetTables
// propagates a primary arg's value to EVERY non-FIXED alias sharing its Symbol.
//
// Two construction styles:
//   1. ParseScriptLine fixtures — faithful to how installed EVENTSCRIPT_* patches
//      define alias commands (a symbol char repeated in two separate byte groups
//      becomes primary + alias). Mirrors EVENTSCRIPT_GetSupport:31 et al.
//   2. Hand-built Arg[] — for ordering edge cases (FIXED before primary, three
//      occurrences interleaved with another symbol).

using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class EventScriptAliasCoreTests
    {
        static EventScript.OneCode MakeCode(EventScript.Arg[] args, int size)
        {
            var script = new EventScript.Script
            {
                Args = args,
                Size = size,
                Data = new byte[size],
                Info = new string[0],
            };
            return new EventScript.OneCode
            {
                Script = script,
                ByteData = new byte[size],
            };
        }

        static EventScript.Arg Arg(EventScript.ArgType type, char symbol, int pos, int size, uint alias)
            => new EventScript.Arg { Type = type, Symbol = symbol, Position = pos, Size = size, Alias = alias, Name = symbol.ToString() };

        // ---- ParseScriptLine fixtures (faithful to installed patches) ----

        // "400DXXYY00010000400DXXYY" : X at byte 2, Y at byte 3, then X again at
        // byte 10, Y again at byte 11 — the second X/Y groups are aliases.
        const string AliasLine =
            "400DXXYY00010000400DXXYY\tCMD [X:UNIT:Unit1][Y:UNIT:Unit2]";

        [Fact]
        public void ParseScriptLine_RepeatedSymbol_SecondOccurrenceIsAlias()
        {
            var script = EventScript.ParseScriptLine(AliasLine);
            Assert.NotNull(script);

            // Collect the X args (symbol 'X', non-FIXED).
            var xArgs = script.Args.Where(a => a.Symbol == 'X' && a.Type != EventScript.ArgType.FIXED).ToList();
            Assert.Equal(2, xArgs.Count);
            Assert.Equal(U.NOT_FOUND, xArgs[0].Alias);          // primary
            Assert.NotEqual(U.NOT_FOUND, xArgs[1].Alias);       // alias
        }

        [Fact]
        public void EnumerateEditableArgIndices_HidesFixedAndAliasRows()
        {
            var script = EventScript.ParseScriptLine(AliasLine);
            var code = new EventScript.OneCode { Script = script, ByteData = new byte[script.Size] };

            var editable = EventScriptAliasCore.EnumerateEditableArgIndices(code).ToList();

            // Exactly two editable rows: primary X and primary Y. No FIXED, no alias rows.
            Assert.Equal(2, editable.Count);
            foreach (int idx in editable)
            {
                var a = script.Args[idx];
                Assert.NotEqual(EventScript.ArgType.FIXED, a.Type);
                Assert.Equal(U.NOT_FOUND, a.Alias); // only primaries are editable
            }
            // The two editable symbols are X and Y.
            var symbols = editable.Select(i => script.Args[i].Symbol).ToHashSet();
            Assert.Contains('X', symbols);
            Assert.Contains('Y', symbols);
        }

        [Fact]
        public void EnumerateAliasWriteTargets_PrimaryPlusEveryAliasSameSymbol()
        {
            var script = EventScript.ParseScriptLine(AliasLine);
            var code = new EventScript.OneCode { Script = script, ByteData = new byte[script.Size] };

            // Find the primary X (editable) index.
            int primaryX = EventScriptAliasCore.EnumerateEditableArgIndices(code)
                .First(i => script.Args[i].Symbol == 'X');

            var targets = EventScriptAliasCore.EnumerateAliasWriteTargets(code, primaryX).ToList();

            // Primary X (byte 2) + alias X (byte 10) = 2 targets, all symbol 'X'.
            Assert.Equal(2, targets.Count);
            Assert.All(targets, t => Assert.Equal('X', t.Symbol));
            var positions = targets.Select(t => t.Position).ToHashSet();
            Assert.Contains(2, positions);
            Assert.Contains(10, positions);
            // The unrelated symbol Y is never a target.
            Assert.DoesNotContain(targets, t => t.Symbol == 'Y');
        }

        // ---- Hand-built Arg[] edge cases ----

        [Fact]
        public void EnumerateEditableArgIndices_FixedBeforePrimary_MapsToCorrectIndex()
        {
            // Args: [0] FIXED, [1] primary X, [2] alias X, [3] primary Y
            var args = new[]
            {
                Arg(EventScript.ArgType.FIXED, '\0', 0, 2, U.NOT_FOUND),
                Arg(EventScript.ArgType.UNIT, 'X', 2, 1, U.NOT_FOUND),
                Arg(EventScript.ArgType.UNIT, 'X', 3, 1, 1u),
                Arg(EventScript.ArgType.UNIT, 'Y', 4, 1, U.NOT_FOUND),
            };
            var code = MakeCode(args, 5);

            var editable = EventScriptAliasCore.EnumerateEditableArgIndices(code).ToList();

            // Editable rows must be the primary X (index 1) and primary Y (index 3) —
            // NOT positions 0/1. This is the exact mapping SourceArgIndex protects.
            Assert.Equal(new[] { 1, 3 }, editable);
        }

        [Fact]
        public void EnumerateAliasWriteTargets_ThreeOccurrences_Interleaved_AllReturned()
        {
            // X X Y X — three X groups (one primary, two aliases) interleaved with a Y.
            var args = new[]
            {
                Arg(EventScript.ArgType.UNIT, 'X', 0, 1, U.NOT_FOUND), // [0] primary X
                Arg(EventScript.ArgType.UNIT, 'X', 1, 1, 0u),          // [1] alias X
                Arg(EventScript.ArgType.UNIT, 'Y', 2, 1, U.NOT_FOUND), // [2] primary Y
                Arg(EventScript.ArgType.UNIT, 'X', 3, 1, 0u),          // [3] alias X
            };
            var code = MakeCode(args, 4);

            var targets = EventScriptAliasCore.EnumerateAliasWriteTargets(code, 0).ToList();

            // Primary X + both alias X = 3 targets at positions 0,1,3. Y untouched.
            Assert.Equal(3, targets.Count);
            Assert.All(targets, t => Assert.Equal('X', t.Symbol));
            Assert.Equal(new[] { 0, 1, 3 }, targets.Select(t => t.Position).ToArray());
            Assert.DoesNotContain(targets, t => t.Symbol == 'Y');
        }

        [Fact]
        public void EnumerateAliasWriteTargets_NoAlias_ReturnsOnlyPrimary()
        {
            var args = new[]
            {
                Arg(EventScript.ArgType.UNIT, 'X', 0, 1, U.NOT_FOUND),
                Arg(EventScript.ArgType.UNIT, 'Y', 1, 1, U.NOT_FOUND),
            };
            var code = MakeCode(args, 2);

            var targets = EventScriptAliasCore.EnumerateAliasWriteTargets(code, 0).ToList();
            Assert.Single(targets);
            Assert.Equal('X', targets[0].Symbol);
        }

        [Fact]
        public void EnumerateAliasWriteTargets_OnAliasIndex_ReturnsEmpty()
        {
            // Asking the helper to "drive" from an alias index is invalid → empty (only
            // a primary may propagate). Guards against a caller passing an alias row.
            var args = new[]
            {
                Arg(EventScript.ArgType.UNIT, 'X', 0, 1, U.NOT_FOUND), // primary
                Arg(EventScript.ArgType.UNIT, 'X', 1, 1, 0u),          // alias
            };
            var code = MakeCode(args, 2);

            Assert.Empty(EventScriptAliasCore.EnumerateAliasWriteTargets(code, 1));
        }

        [Fact]
        public void EnumerateAliasWriteTargets_OnFixed_ReturnsEmpty()
        {
            var args = new[] { Arg(EventScript.ArgType.FIXED, '\0', 0, 2, U.NOT_FOUND) };
            var code = MakeCode(args, 2);
            Assert.Empty(EventScriptAliasCore.EnumerateAliasWriteTargets(code, 0));
        }

        [Fact]
        public void Helpers_NullSafe_NoThrow()
        {
            var emptyCode = new EventScript.OneCode { Script = null, ByteData = null };
            Assert.Empty(EventScriptAliasCore.EnumerateEditableArgIndices(emptyCode));
            Assert.Equal(0, EventScriptAliasCore.CountEditableArgs(emptyCode));
            Assert.Empty(EventScriptAliasCore.EnumerateAliasWriteTargets(emptyCode, 0));
            Assert.Empty(EventScriptAliasCore.EnumerateAliasWriteTargets(null, 0));
        }
    }
}
