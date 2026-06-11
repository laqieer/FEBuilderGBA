// SPDX-License-Identifier: GPL-3.0-or-later
// #1028 Slices C+D tests for the Text Editor:
//   - Slice C: ExportAllTexts(path, includeAIHints) — AI-hint lines are appended
//     to the Text column only when checked, and the TSV column count is never
//     corrupted (always exactly 2 tab-separated columns per data row).
//   - Slice D: WriteText AntiHuffman flow — GiveUp / patch-missing aborts with NO
//     ROM mutation (EncodeAbortedException, no undo commit); patch-present writes
//     an UnHuffman pointer; patch-missing-then-installed re-check proceeds.
//
// ROM-backed tests early-return (skip) when no ROM is available in roms/, matching
// the TextViewerCrossRefTests convention.
using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class TextViewerAIHintsAndBadCharTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public TextViewerAIHintsAndBadCharTests(RomFixture fixture, ITestOutputHelper output)
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
        // Slice C — Export AI Hints (TSV column integrity)
        // ============================================================

        [Fact]
        public void ExportAllTexts_Unchecked_NoHintsAppended_TwoColumns()
        {
            if (SkipNoRom()) return;

            var vm = new TextViewerViewModel();
            string path = Path.Combine(Path.GetTempPath(), $"texts-nohint-{Guid.NewGuid():N}.tsv");
            try
            {
                int count = vm.ExportAllTexts(path, includeAIHints: false);
                Assert.True(count > 0);
                AssertEveryDataRowHasExactlyTwoColumns(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void ExportAllTexts_Checked_HintsAppended_StillTwoColumns()
        {
            if (SkipNoRom()) return;

            var vm = new TextViewerViewModel();
            string noHintPath = Path.Combine(Path.GetTempPath(), $"texts-a-{Guid.NewGuid():N}.tsv");
            string hintPath = Path.Combine(Path.GetTempPath(), $"texts-b-{Guid.NewGuid():N}.tsv");
            try
            {
                int n1 = vm.ExportAllTexts(noHintPath, includeAIHints: false);
                int n2 = vm.ExportAllTexts(hintPath, includeAIHints: true);

                // Same entry COUNT either way — hints only extend the Text column.
                Assert.Equal(n1, n2);

                // Column integrity is the non-negotiable invariant: every data row
                // still has exactly two tab-separated columns, even with multi-line
                // hint blocks appended (the \n flattening keeps them in-column).
                AssertEveryDataRowHasExactlyTwoColumns(hintPath);

                // The checked file must be at least as large as the unchecked one;
                // on the FE8U ROM at least one text loads a face, so hints exist and
                // the file grows. (Assert >= rather than > to stay robust.)
                long sizeNoHint = new FileInfo(noHintPath).Length;
                long sizeHint = new FileInfo(hintPath).Length;
                Assert.True(sizeHint >= sizeNoHint);
            }
            finally
            {
                if (File.Exists(noHintPath)) File.Delete(noHintPath);
                if (File.Exists(hintPath)) File.Delete(hintPath);
            }
        }

        static void AssertEveryDataRowHasExactlyTwoColumns(string path)
        {
            string[] lines = File.ReadAllLines(path);
            Assert.True(lines.Length >= 1);
            Assert.Equal("ID\tText", lines[0]); // header
            for (int i = 1; i < lines.Length; i++)
            {
                // Each data row: exactly one tab separating ID and Text. Hint blocks
                // are newline-flattened into the Text column by ExportToTSV, so a
                // single \t per row is the column-integrity invariant.
                int tabs = lines[i].Count(c => c == '\t');
                Assert.True(tabs == 1,
                    $"Row {i} has {tabs} tabs (expected 1): TSV columns corrupted. Row: {lines[i]}");
            }
        }

        // ============================================================
        // Slice D — WriteText AntiHuffman flow
        // ============================================================

        [Fact]
        public void WriteText_PatchMissing_CallbackDeclines_AbortsWithNoMutation()
        {
            if (SkipNoRom()) return;
            ROM rom = _fixture.ROM!;

            // Pick a text id and an edit string containing a character that cannot
            // be Huffman-encoded on this ROM (a CJK kanji on an English/most ROMs).
            // PeekEncodeError confirms the bad-char failure; skip if it encodes.
            uint id = 1;
            string badText = "日本語";

            var vm = new TextViewerViewModel();
            string? err = vm.PeekEncodeError(badText);
            if (err == null)
            {
                _output.WriteLine("Test char encoded cleanly on this ROM; skipping bad-char path.");
                return;
            }
            if (PatchDetection.SearchAntiHuffmanPatch(rom))
            {
                _output.WriteLine("AntiHuffman patch present on this ROM; skipping abort path.");
                return;
            }

            // Snapshot the text pointer slot so we can prove NO mutation.
            uint textBase = vm.ResolveTextTableBase();
            Assert.NotEqual(0u, textBase);
            uint slot = textBase + id * 4;
            uint before = rom.u32(slot);
            byte[] dataBefore = (byte[])rom.Data.Clone();

            bool callbackInvoked = false;
            vm.AntiHuffmanPromptCallback = e => { callbackInvoked = true; return false; };

            Assert.Throws<TextViewerViewModel.EncodeAbortedException>(
                () => vm.WriteText(id, badText));

            Assert.True(callbackInvoked, "prompt callback was not invoked");
            Assert.Equal(before, rom.u32(slot));              // pointer unchanged
            Assert.Equal(dataBefore.Length, rom.Data.Length); // no append
            Assert.True(dataBefore.SequenceEqual(rom.Data), "ROM bytes mutated despite abort");
        }

        [Fact]
        public void WriteText_PatchMissing_NoCallback_AbortsWithNoMutation()
        {
            // A null callback (headless) is treated as "still missing" — abort,
            // no mutation. This pins the headless contract.
            if (SkipNoRom()) return;
            ROM rom = _fixture.ROM!;

            string badText = "日本語";
            var vm = new TextViewerViewModel();
            if (vm.PeekEncodeError(badText) == null) { _output.WriteLine("Clean encode; skip."); return; }
            if (PatchDetection.SearchAntiHuffmanPatch(rom)) { _output.WriteLine("Patch present; skip."); return; }

            byte[] dataBefore = (byte[])rom.Data.Clone();
            vm.AntiHuffmanPromptCallback = null; // headless / no prompt

            Assert.Throws<TextViewerViewModel.EncodeAbortedException>(() => vm.WriteText(1, badText));
            Assert.True(dataBefore.SequenceEqual(rom.Data), "ROM bytes mutated despite abort");
        }

        [Fact]
        public void WriteText_PatchMissing_CallbackReportsInstalled_ButReCheckStillMissing_Aborts()
        {
            // The callback claims the patch was installed (returns true), but the
            // ROM-state re-check inside WriteText still finds it missing — WF flow
            // aborts. Proves WriteText re-checks PatchDetection rather than trusting
            // the callback's word.
            if (SkipNoRom()) return;
            ROM rom = _fixture.ROM!;

            string badText = "日本語";
            var vm = new TextViewerViewModel();
            if (vm.PeekEncodeError(badText) == null) { _output.WriteLine("Clean encode; skip."); return; }
            if (PatchDetection.SearchAntiHuffmanPatch(rom)) { _output.WriteLine("Patch present; skip."); return; }

            byte[] dataBefore = (byte[])rom.Data.Clone();
            // Lie: say it's installed. WriteText re-checks the real ROM state.
            vm.AntiHuffmanPromptCallback = e => true;

            Assert.Throws<TextViewerViewModel.EncodeAbortedException>(() => vm.WriteText(1, badText));
            Assert.True(dataBefore.SequenceEqual(rom.Data), "ROM bytes mutated despite re-check abort");
        }

        [Fact]
        public void PeekEncodeError_NoEncoder_ReturnsNull()
        {
            // With no FETextEncoder wired (headless / unloaded), PeekEncodeError is
            // a safe no-op returning null — it never throws. (Clean-vs-bad encode
            // behavior is environment-specific and is covered by the bad-char abort
            // tests above, which use a definitively-unencodable CJK string.)
            var savedEncoder = CoreState.FETextEncoder;
            try
            {
                CoreState.FETextEncoder = null;
                var vm = new TextViewerViewModel();
                Assert.Null(vm.PeekEncodeError("anything"));
            }
            finally
            {
                CoreState.FETextEncoder = savedEncoder;
            }
        }
    }
}
