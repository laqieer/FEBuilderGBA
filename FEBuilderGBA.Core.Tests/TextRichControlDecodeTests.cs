// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for TextRichControlDecode (#1108) — the scoped Core decode helpers
// backing the Avalonia Text Editor's two rich-text outgoing jumps:
//   * FindFirstPortraitFaceId — the portrait char-jump (WF TextForm.MakePortait).
//   * LoadEscapeEntries / LoadEscapeCategories — the escape-code insert dialog
//     (WF TextScriptFormCategorySelectForm).
//
// The portrait decode is pure (no ROM, no config) so it's asserted directly. The
// escape/category loaders read config/data/*.txt; when the repo root (with
// config/) is reachable from the test working dir we assert the real-config
// behavior (incl. the detail-mode filter), otherwise we still assert the
// never-throws / empty-list contract.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class TextRichControlDecodeTests
    {
        // ================================================================
        // FindFirstPortraitFaceId — pure decode, no ROM / config.
        // ================================================================

        [Fact]
        public void FindFirstPortraitFaceId_DisplayCode_ReturnsFaceId()
        {
            // @0008 (position) @0010 (display) @0139 (face id + 0x100) -> 0x39.
            Assert.Equal(0x39u, TextRichControlDecode.FindFirstPortraitFaceId("@0008@0010@0139"));
        }

        [Fact]
        public void FindFirstPortraitFaceId_VisitorSentinel_ReturnsFFFF()
        {
            // @0010@FFFF is the "visited character" sentinel — caller does NOT navigate.
            Assert.Equal(0xFFFFu, TextRichControlDecode.FindFirstPortraitFaceId("@0008@0010@FFFF"));
            Assert.Equal(TextRichControlDecode.VisitorSentinel,
                TextRichControlDecode.FindFirstPortraitFaceId("@0008@0010@FFFF"));
        }

        [Fact]
        public void FindFirstPortraitFaceId_Empty_ReturnsNull()
        {
            Assert.Null(TextRichControlDecode.FindFirstPortraitFaceId(""));
            Assert.Null(TextRichControlDecode.FindFirstPortraitFaceId(null));
        }

        [Fact]
        public void FindFirstPortraitFaceId_PlainText_ReturnsNull()
        {
            Assert.Null(TextRichControlDecode.FindFirstPortraitFaceId("hello world"));
        }

        [Fact]
        public void FindFirstPortraitFaceId_Malformed_NoThrow()
        {
            // Malformed escape soup must never throw — returns null (no display code).
            var ex = Record.Exception(() => TextRichControlDecode.FindFirstPortraitFaceId("@0010@@@@"));
            Assert.Null(ex);
            Assert.Null(TextRichControlDecode.FindFirstPortraitFaceId("@0010@@@@"));
        }

        [Fact]
        public void FindFirstPortraitFaceId_DisplayInSecondStep_StillFound()
        {
            // First step is a position+serif, the display code comes later — the
            // first DISPLAY code (@0010@015A) still wins -> 0x5A.
            string text = "@0008@000D@0010@015A";
            Assert.Equal(0x5Au, TextRichControlDecode.FindFirstPortraitFaceId(text));
        }

        // ---- Face-id-0 boundary (Copilot PR #1128 finding): the strict WF-faithful
        // parser splits a Display step only when code2 > 0x100, so @0010@0100 (face
        // id 0) is dropped by the parser and recovered by the post-loop fallback. ----

        [Fact]
        public void FindFirstPortraitFaceId_FaceIdZeroBoundary_ReturnsZero()
        {
            // @0010@0100 = face id 0. The parser drops it (code2 > 0x100 is strict);
            // the fallback recovers exactly this boundary -> 0u.
            Assert.Equal(0u, TextRichControlDecode.FindFirstPortraitFaceId("@0008@0010@0100"));
        }

        [Fact]
        public void FindFirstPortraitFaceId_FaceIdOne_StillParsesViaParser()
        {
            // @0010@0101 = face id 1. code2 (0x101) > 0x100, so the PARSER produces a
            // Display step -> 1u (regression guard: fallback must not be needed here).
            Assert.Equal(1u, TextRichControlDecode.FindFirstPortraitFaceId("@0008@0010@0101"));
        }

        [Fact]
        public void FindFirstPortraitFaceId_WellFormedFirst_ThenZeroBoundary_ParserWins()
        {
            // A well-formed @0010@0139 (-> 0x39) precedes a @0010@0100 boundary. The
            // parser finds the first display (0x39); the fallback ONLY fires when the
            // parser found nothing, so it must NOT override the parser's first-display.
            Assert.Equal(0x39u,
                TextRichControlDecode.FindFirstPortraitFaceId("@0008@0010@0139@000D@0010@0100"));
        }

        [Fact]
        public void FindFirstPortraitFaceId_LargeArgument_ReturnsLargeFaceId()
        {
            // A malformed/patch escape can yield a large face id (here 0xFFFE-0x100 =
            // 0xFEFE, just under the 0xFFFF visitor sentinel). The decode returns it
            // faithfully; it is the JUMP HANDLER's job to overflow-guard the computed
            // address (Copilot BOT finding 1) — this documents the out-of-range input
            // that guard defends against.
            Assert.Equal(0xFEFEu, TextRichControlDecode.FindFirstPortraitFaceId("@0008@0010@FFFE"));
        }

        // ================================================================
        // LoadEscapeEntries / LoadEscapeCategories — never-throws + filter.
        // ================================================================

        // Walk up from the test assembly to the repo root (the dir holding
        // FEBuilderGBA.sln, which also has config/data/). Returns null if not found.
        static string? FindRepoRoot()
        {
            string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")) &&
                    File.Exists(Path.Combine(dir, "config", "data", "text_escape_ALL.txt")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        [Fact]
        public void LoadEscapeEntries_NeverThrows_ReturnsList()
        {
            string savedBase = CoreState.BaseDirectory;
            try
            {
                // Point at a dir with NO config/data — loader must degrade to an
                // empty list without throwing.
                CoreState.BaseDirectory = Path.GetTempPath();
                var entries = TextRichControlDecode.LoadEscapeEntries(true);
                Assert.NotNull(entries);
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
            }
        }

        [Fact]
        public void LoadEscapeEntries_PatchEscape_InfoOrderIsInfoThenFeditorAdv()
        {
            // WF parity (TextScriptFormCategorySelectForm.cs:76):
            //   te.Info = t.Value.info + t.Value.feditorAdv  (info FIRST).
            // Inject a patch escape via a fresh TextEscape and assert the appended
            // entry's Info is "INFO" + "FEDITORADV", not the reverse.
            string savedBase = CoreState.BaseDirectory;
            var savedEscape = CoreState.TextEscape;
            try
            {
                // No shipped config -> the only entry will be the patch escape.
                CoreState.BaseDirectory = Path.GetTempPath();

                var te = new TextEscape();
                te.Add("@9990", "FEDITORADV", "INFO");
                CoreState.TextEscape = te;

                var entries = TextRichControlDecode.LoadEscapeEntries(true);
                var patch = entries.Single(en => en.Code == "@9990");
                Assert.Equal("INFO" + "FEDITORADV", patch.Info);
                Assert.NotEqual("FEDITORADV" + "INFO", patch.Info);
                Assert.Equal("", patch.Category);
            }
            finally
            {
                CoreState.TextEscape = savedEscape;
                CoreState.BaseDirectory = savedBase;
            }
        }

        [Fact]
        public void LoadEscapeCategories_NeverThrows_ReturnsList()
        {
            string savedBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = Path.GetTempPath();
                var cats = TextRichControlDecode.LoadEscapeCategories(true);
                Assert.NotNull(cats);
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
            }
        }

        [Fact]
        public void LoadEscapeEntries_RealConfig_DetailVsNonDetail()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // config not reachable — skip the real-config assertion.

            string savedBase = CoreState.BaseDirectory;
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                CoreState.ROM = null; // headless: ConfigDataFilename falls back to *_ALL.txt

                var detail = TextRichControlDecode.LoadEscapeEntries(true);
                Assert.NotEmpty(detail);
                // The LoadFace display code is present in detail mode.
                Assert.Contains(detail, en => en.Code == "@0010");
                // Detail mode includes the {POSITION} and {MOVE_LOAD} categories.
                Assert.Contains(detail, en => en.Category == "{POSITION}");
                Assert.Contains(detail, en => en.Category == "{MOVE_LOAD}");

                var nonDetail = TextRichControlDecode.LoadEscapeEntries(false);
                // Non-detail filters out {MOVE_LOAD} and {POSITION}.
                Assert.DoesNotContain(nonDetail, en => en.Category == "{MOVE_LOAD}");
                Assert.DoesNotContain(nonDetail, en => en.Category == "{POSITION}");
                Assert.True(nonDetail.Count < detail.Count);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBase;
            }
        }

        [Fact]
        public void LoadEscapeCategories_RealConfig_DetailVsNonDetail()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return;

            string savedBase = CoreState.BaseDirectory;
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.BaseDirectory = repoRoot;
                CoreState.ROM = null;

                var detail = TextRichControlDecode.LoadEscapeCategories(true);
                Assert.NotEmpty(detail);
                // The "show all" {} category + the {DISPLAY} category are present.
                Assert.Contains(detail, c => c.Category == "{}");
                Assert.Contains(detail, c => c.Category == "{DISPLAY}");
                Assert.Contains(detail, c => c.Category == "{POSITION}");
                Assert.Contains(detail, c => c.Category == "{MOVE_LOAD}");

                var nonDetail = TextRichControlDecode.LoadEscapeCategories(false);
                Assert.DoesNotContain(nonDetail, c => c.Category == "{MOVE_LOAD}");
                Assert.DoesNotContain(nonDetail, c => c.Category == "{POSITION}");
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBase;
            }
        }
    }
}
