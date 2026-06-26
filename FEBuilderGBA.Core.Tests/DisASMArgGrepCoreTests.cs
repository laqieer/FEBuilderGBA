// SPDX-License-Identifier: GPL-3.0-or-later
// #1463 — DisASMArgGrepCore parity tests.
//
// WinForms DisASMDumpAllArgGrepForm.Grep()/IsSearchRegister() does a
// register-flow scan: find a mov/ldr line that mentions the target register,
// then within AllowRows rows find the call to the target function, then emit the
// argument-setup block (the call line optionally dropped). The old Avalonia view
// did a flat case-insensitive substring grep and surfaced none of the 5 options.
//
// These tests use fully-synthetic disassembly-line arrays (mirroring the real
// "  0x........:  opcode ..." format produced by DisassemblerCore) and pin each
// of the 5 options + the register-flow semantics, INCLUDING the WinForms
// imperfections: case-SENSITIVE matching, literal-substring register tests, the
// retained address-prefix in emitted lines (lines[n].Trim()), and the line-0
// anchor quirk (regLine==0 is the "no anchor" sentinel).
//
// A leading header line is prepended to every synthetic array so anchors sit at
// index >= 1 (real DisassembleToLines() always emits header/comment lines first).

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class DisASMArgGrepCoreTests
    {
        const string Header = "; Disassembly header";
        static string Reg(string r) => DisASMArgGrepCore.BuildSearchReg(r);

        // ---- IsSearchRegister gate ----------------------------------------

        [Fact]
        public void IsSearchRegister_RequiresMovOrLdrOpcode()
        {
            Assert.False(DisASMArgGrepCore.IsSearchRegister("  0x08000000:  add r0, r1", Reg("r0")));
            Assert.True(DisASMArgGrepCore.IsSearchRegister("  0x08000000:  mov r0, #0x10", Reg("r0")));
            Assert.True(DisASMArgGrepCore.IsSearchRegister("  0x08000000:  ldr r0, [pc, #4]", Reg("r0")));
        }

        [Fact]
        public void IsSearchRegister_RequiresRegisterToken()
        {
            Assert.False(DisASMArgGrepCore.IsSearchRegister("  0x08000000:  mov r1, #0x10", Reg("r0")));
        }

        [Fact]
        public void IsSearchRegister_IsCaseSensitive_VerbatimWinForms()
        {
            // uppercase MOV / R0 must NOT match (WinForms plain IndexOf)
            Assert.False(DisASMArgGrepCore.IsSearchRegister("  0x08000000:  MOV R0, #0x10", Reg("r0")));
        }

        [Fact]
        public void IsSearchRegister_LiteralSubstring_RegisterPrefixMatchesWider_VerbatimWinForms()
        {
            // searching " r1" also matches " r10" because WinForms uses a literal
            // substring test, not a tokenized register parse. Pin the imperfection.
            Assert.True(DisASMArgGrepCore.IsSearchRegister("  0x08000000:  mov r10, #0x10", Reg("r1")));
        }

        // ---- NormalizeSearchFunction --------------------------------------

        [Fact]
        public void NormalizeSearchFunction_HexAddress_ConvertsToPointerString()
        {
            string norm = DisASMArgGrepCore.NormalizeSearchFunction("D01FC");
            Assert.Equal(U.To0xHexString(U.toPointer(U.atoh("D01FC"))), norm);
            Assert.StartsWith("0x", norm);
        }

        [Fact]
        public void NormalizeSearchFunction_SymbolName_PassesThroughUnchanged()
        {
            Assert.Equal("m4aSongNumStart", DisASMArgGrepCore.NormalizeSearchFunction("m4aSongNumStart"));
        }

        [Fact]
        public void NormalizeSearchFunction_Null_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, DisASMArgGrepCore.NormalizeSearchFunction(null));
        }

        // ---- Grep: basic register-set -> call block emission --------------

        [Fact]
        public void Grep_BasicBlock_EmitsRegisterSetThroughCall()
        {
            var lines = new List<string>
            {
                Header,
                "  0x08000000:  push {lr}",
                "  0x08000002:  mov r0, #0x05",      // register-set anchor
                "  0x08000004:  bl  myFunc",          // call to target function
                "  0x08000006:  pop {pc}",
            };

            string ret = DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 5, false, false);
            var outLines = SplitNonEmpty(ret);

            // lines[n].Trim() keeps the address prefix; only outer whitespace is removed.
            Assert.Equal(2, outLines.Count);
            Assert.Equal("0x08000002:  mov r0, #0x05", outLines[0]);
            Assert.Equal("0x08000004:  bl  myFunc", outLines[1]);
        }

        // ---- Option 1: hide function call --------------------------------

        [Fact]
        public void Grep_HideFunctionCall_DropsTheCallLine()
        {
            var lines = new List<string>
            {
                Header,
                "  0x08000002:  mov r0, #0x05",
                "  0x08000004:  bl  myFunc",
            };

            string ret = DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 5, true, false);
            var outLines = SplitNonEmpty(ret);

            Assert.Single(outLines);
            Assert.Equal("0x08000002:  mov r0, #0x05", outLines[0]);   // call line dropped
        }

        // ---- Option 2: hide unknown arg (register-set with '(') ----------

        [Fact]
        public void Grep_HideUnknownArg_SkipsRegisterSetsContainingParen()
        {
            var lines = new List<string>
            {
                Header,
                "  0x08000002:  mov r0, #0x05 ; (unknown)",   // '(' -> skipped as anchor
                "  0x08000004:  bl  myFunc",
            };

            // With hideUnknownArg, the register-set is skipped, so no call is anchored.
            string ret = DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 5, false, true);
            Assert.Equal(string.Empty, ret);

            // Without the filter, the same input emits the block.
            string ret2 = DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 5, false, false);
            Assert.NotEqual(string.Empty, ret2);
        }

        // ---- Option 3: allowed-rows window overflow ----------------------

        [Fact]
        public void Grep_WindowOverflow_DropsCandidateAndDoesNotEmit()
        {
            var lines = new List<string>
            {
                Header,
                "  0x08000000:  mov r0, #0x05",       // anchor at index 1
                "  0x08000002:  nop",
                "  0x08000004:  nop",
                "  0x08000006:  nop",
                "  0x08000008:  bl  myFunc",           // index 5, allow=2 -> too far
            };

            // allowNumber = 2 -> candidate dropped before the call is reached.
            string ret = DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 2, false, false);
            Assert.Equal(string.Empty, ret);

            // allowNumber = 5 -> within window -> emits.
            string ret2 = DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 5, false, false);
            Assert.NotEqual(string.Empty, ret2);
        }

        // ---- Option 4: register selection (different register) -----------

        [Fact]
        public void Grep_RegisterSelection_OnlyAnchorsOnChosenRegister()
        {
            var lines = new List<string>
            {
                Header,
                "  0x08000000:  mov r2, #0x05",       // r2 set, not r0
                "  0x08000002:  bl  myFunc",
            };

            // Searching r0 -> no anchor -> no result.
            Assert.Equal(string.Empty, DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 5, false, false));
            // Searching r2 -> anchors -> emits.
            Assert.NotEqual(string.Empty, DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r2"), 5, false, false));
        }

        // ---- Option 5: target function (case-sensitive, exact substring) -

        [Fact]
        public void Grep_TargetFunction_IsCaseSensitive()
        {
            var lines = new List<string>
            {
                Header,
                "  0x08000002:  mov r0, #0x05",
                "  0x08000004:  bl  MYFUNC",           // uppercase
            };

            // Searching lowercase "myFunc" must NOT match the uppercase call.
            Assert.Equal(string.Empty, DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 5, false, false));
            // Exact case matches.
            Assert.NotEqual(string.Empty, DisASMArgGrepCore.Grep(lines, "MYFUNC", Reg("r0"), 5, false, false));
        }

        // ---- Re-anchor on a closer register-set --------------------------

        [Fact]
        public void Grep_ReAnchorsOnCloserRegisterSet()
        {
            var lines = new List<string>
            {
                Header,
                "  0x08000000:  mov r0, #0x01",       // first anchor (far)
                "  0x08000002:  nop",
                "  0x08000004:  mov r0, #0x99",       // closer anchor re-sets regLine
                "  0x08000006:  bl  myFunc",
            };

            string ret = DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 10, false, false);
            var outLines = SplitNonEmpty(ret);

            // Block begins at the CLOSER register-set (#0x99), not the first.
            Assert.Equal(2, outLines.Count);
            Assert.Equal("0x08000004:  mov r0, #0x99", outLines[0]);
            Assert.Equal("0x08000006:  bl  myFunc", outLines[1]);
        }

        // ---- Multiple independent blocks separated by a blank line --------

        [Fact]
        public void Grep_EmitsMultipleBlocks_SeparatedByBlankLine()
        {
            var lines = new List<string>
            {
                Header,
                "  0x08000000:  mov r0, #0x01",
                "  0x08000002:  bl  myFunc",
                "  0x08000004:  nop",
                "  0x08000006:  mov r0, #0x02",
                "  0x08000008:  bl  myFunc",
            };

            string ret = DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 5, false, false);

            // Two "bl  myFunc" occurrences => two blocks.
            int callCount = CountOccurrences(ret, "bl  myFunc");
            Assert.Equal(2, callCount);
            // separator blank line present
            Assert.Contains("\n\n", ret.Replace("\r\n", "\n"));
        }

        // ---- WinForms line-0 anchor quirk (preserved verbatim) -----------

        [Fact]
        public void Grep_RegisterSetOnLineZero_NeverAnchors_VerbatimWinForms()
        {
            // regLine==0 is the "no anchor" sentinel, so a register-set at index 0
            // can never anchor. This is a faithful WinForms quirk; real disassembly
            // always has header lines first so it does not bite in practice.
            var lines = new List<string>
            {
                "  0x08000000:  mov r0, #0x05",       // index 0 -> cannot anchor
                "  0x08000002:  bl  myFunc",
            };
            Assert.Equal(string.Empty, DisASMArgGrepCore.Grep(lines, "myFunc", Reg("r0"), 5, false, false));
        }

        // ---- Null / empty input safety -----------------------------------

        [Fact]
        public void Grep_NullLines_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, DisASMArgGrepCore.Grep(null, "myFunc", Reg("r0"), 5, false, false));
        }

        static List<string> SplitNonEmpty(string ret)
        {
            return ret.Replace("\r\n", "\n")
                      .Split('\n')
                      .Where(l => l.Length > 0)
                      .ToList();
        }

        static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }
    }
}
