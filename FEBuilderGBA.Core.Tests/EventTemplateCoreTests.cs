// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for EventTemplateCore (#1434) — the cross-platform Event Template
// code generator ported from the WinForms EventTemplate1-6 / Templates forms.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class EventTemplateCoreTests
    {
        // FE8U synthetic ROM (full RomInfo incl. term/toplevel codes); no fixture.
        static ROM MakeFE8U()
        {
            var rom = new ROM();
            rom.LoadLow("evt-fe8u.gba", new byte[0x1000000], "BE8E01");
            return rom;
        }

        // FE8J synthetic ROM (is_multibyte => {J} lines kept, {U} dropped).
        static ROM MakeFE8J()
        {
            var rom = new ROM();
            rom.LoadLow("evt-fe8j.gba", new byte[0x1000000], "BE8J01");
            return rom;
        }

        // ---- LineToEventByte ----------------------------------------------

        [Fact]
        public void LineToEventByte_ParsesLeadingHex_StopsAtComment()
        {
            byte[] b = EventTemplateCore.LineToEventByte("4005020000000000\t//comment");
            Assert.Equal(new byte[] { 0x40, 0x05, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00 }, b);
        }

        [Fact]
        public void LineToEventByte_StopsAtFirstNonHex()
        {
            // 'X' placeholder is non-hex; only the leading clean pair survives.
            byte[] b = EventTemplateCore.LineToEventByte("40XXXX");
            Assert.Equal(new byte[] { 0x40 }, b);
        }

        [Fact]
        public void LineToEventByte_OddLength_DropsTrailingNibble()
        {
            byte[] b = EventTemplateCore.LineToEventByte("400");
            Assert.Equal(new byte[] { 0x40 }, b);
        }

        [Fact]
        public void LineToEventByte_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Empty(EventTemplateCore.LineToEventByte(null));
            Assert.Empty(EventTemplateCore.LineToEventByte(""));
            Assert.Empty(EventTemplateCore.LineToEventByte("   "));
        }

        // ---- ConverteventTextToBin (pure parse, temp file) ----------------

        [Fact]
        public void ConverteventTextToBin_LangFilter_KeepsUDropsJ_ForFE8U()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp,
                    "AABBCCDD\t//jline\t{J}\n" +
                    "11223344\t//uline\t{U}\n");
                ROM rom = MakeFE8U();
                byte[] b = EventTemplateCore.ConverteventTextToBin(rom, tmp,
                    EventTemplateCore.TermCode.NoTerm);
                // {J} dropped for the ASCII (non-multibyte) FE8U ROM.
                Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, b);
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void ConverteventTextToBin_LangFilter_KeepsJDropsU_ForFE8J()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp,
                    "AABBCCDD\t//jline\t{J}\n" +
                    "11223344\t//uline\t{U}\n");
                ROM rom = MakeFE8J();
                byte[] b = EventTemplateCore.ConverteventTextToBin(rom, tmp,
                    EventTemplateCore.TermCode.NoTerm);
                Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, b);
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void ConverteventTextToBin_Substitutes_XXXX_and_YYYY()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "0102XXXXYYYY\t//x\n");
                ROM rom = MakeFE8U();
                byte[] b = EventTemplateCore.ConverteventTextToBin(rom, tmp,
                    EventTemplateCore.TermCode.NoTerm, "AABB", "CCDD");
                Assert.Equal(new byte[] { 0x01, 0x02, 0xAA, 0xBB, 0xCC, 0xDD }, b);
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void ConverteventTextToBin_AppendsTerminator_OnDefaultAndSimple()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "01020304\t//x\n");
                ROM rom = MakeFE8U();
                byte[] term = rom.RomInfo.Default_event_script_term_code;
                Assert.NotNull(term);

                byte[] def = EventTemplateCore.ConverteventTextToBin(rom, tmp,
                    EventTemplateCore.TermCode.DefaultTermCode);
                Assert.Equal(4 + term.Length, def.Length);

                byte[] simple = EventTemplateCore.ConverteventTextToBin(rom, tmp,
                    EventTemplateCore.TermCode.SimpleTermCode);
                Assert.Equal(4 + term.Length, simple.Length);

                byte[] none = EventTemplateCore.ConverteventTextToBin(rom, tmp,
                    EventTemplateCore.TermCode.NoTerm);
                Assert.Equal(4, none.Length);
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void ConverteventTextToBin_SkipsShortLines()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "0102\t//tooshort\n01020304\t//ok\n");
                ROM rom = MakeFE8U();
                byte[] b = EventTemplateCore.ConverteventTextToBin(rom, tmp,
                    EventTemplateCore.TermCode.NoTerm);
                Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, b);
            }
            finally { File.Delete(tmp); }
        }

        // ---- RequiresEditorContext ----------------------------------------

        [Fact]
        public void RequiresEditorContext_True_WhenPlaceholderPresent()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "0102XXXX\t//placeholder\n");
                ROM rom = MakeFE8U();
                Assert.True(EventTemplateCore.RequiresEditorContext(rom, tmp));
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void RequiresEditorContext_False_WhenPlaceholderOnlyInOtherLang()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                // Placeholder lives only on a {J} line; for FE8U that line is dropped.
                File.WriteAllText(tmp,
                    "0102XXXX\t//jonly\t{J}\n" +
                    "01020304\t//uok\t{U}\n");
                ROM rom = MakeFE8U();
                Assert.False(EventTemplateCore.RequiresEditorContext(rom, tmp));
            }
            finally { File.Delete(tmp); }
        }

        // ---- GetToplevelBlank ---------------------------------------------

        [Fact]
        public void GetToplevelBlank_ReturnsRomToplevelCode()
        {
            ROM rom = MakeFE8U();
            byte[] blank = EventTemplateCore.GetToplevelBlank(rom);
            Assert.Equal(rom.RomInfo.Default_event_script_toplevel_code, blank);
            Assert.True(blank.Length >= 4);
        }

        [Fact]
        public void GetToplevelBlank_NullRom_ReturnsEmpty()
        {
            Assert.Empty(EventTemplateCore.GetToplevelBlank(null));
        }

        // ---- TryGenerateButton — accurate failure reasons --------------------

        [Fact]
        public void TryGenerateButton_NoRom_ReturnsNoRom()
        {
            var btn = EventTemplateCore.GetTemplateButtons(1)[0];
            var r = EventTemplateCore.TryGenerateButton(null, btn, out byte[] bytes);
            Assert.Equal(EventTemplateCore.GenerateResult.NoRom, r);
            Assert.Null(bytes);
        }

        [Fact]
        public void TryGenerateButton_Blank_ReturnsOk()
        {
            ROM rom = MakeFE8U();
            var btn = EventTemplateCore.GetTemplateButtons(1)[0]; // BLANK
            var r = EventTemplateCore.TryGenerateButton(rom, btn, out byte[] bytes);
            Assert.Equal(EventTemplateCore.GenerateResult.Ok, r);
            Assert.NotNull(bytes);
            Assert.True(bytes.Length >= 4);
        }

        [Fact]
        public void TryGenerateButton_MissingConfig_ReturnsConfigNotFound_NotContext()
        {
            // Point BaseDirectory at an empty temp dir so the config can't resolve;
            // the result must be ConfigNotFound, NOT the misleading editor-context
            // status (the regression Copilot flagged).
            string prevBase = CoreState.BaseDirectory;
            string tmpDir = Path.Combine(Path.GetTempPath(), "evt-nocfg-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            try
            {
                CoreState.BaseDirectory = tmpDir;
                ROM rom = MakeFE8U();
                var btn = new EventTemplateCore.TemplateButton(
                    "VILLAGE_TALK", "template_event_VILLAGE_TALK_",
                    EventTemplateCore.TermCode.DefaultTermCode, false);
                var r = EventTemplateCore.TryGenerateButton(rom, btn, out byte[] bytes);
                Assert.Equal(EventTemplateCore.GenerateResult.ConfigNotFound, r);
                Assert.Null(bytes);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                try { Directory.Delete(tmpDir, true); } catch { }
            }
        }

        // ---- Template 1-6 button parity (against WinForms button bodies) --

        [Theory]
        [InlineData(1, 5)]   // BLANK + 4 VILLAGE
        [InlineData(2, 7)]   // BLANK + 6 ENTER/GameOver/Desert
        [InlineData(3, 7)]   // BLANK + 6 reinforcement/talk/gameover
        [InlineData(4, 4)]   // BLANK + 3 talk
        [InlineData(5, 1)]   // BLANK only
        [InlineData(6, 2)]   // BLANK + GAMEOVER
        public void GetTemplateButtons_CountMatchesWinForms(int templateNumber, int expectedCount)
        {
            var btns = EventTemplateCore.GetTemplateButtons(templateNumber);
            Assert.Equal(expectedCount, btns.Count);
            // First button is always BLANK.
            Assert.True(btns[0].IsBlank);
            Assert.Equal("BLANK", btns[0].Key);
        }

        [Fact]
        public void GetTemplateButtons_Template1_MappingMatchesWinForms()
        {
            var btns = EventTemplateCore.GetTemplateButtons(1);
            // Non-blank buttons use DefaultTermCode + the VILLAGE config prefixes.
            for (int i = 1; i < btns.Count; i++)
            {
                Assert.False(btns[i].IsBlank);
                Assert.StartsWith("template_event_VILLAGE_", btns[i].ConfigType);
                Assert.Equal(EventTemplateCore.TermCode.DefaultTermCode, btns[i].Term);
            }
        }

        [Fact]
        public void GetTemplateButtons_Template2_UsesSimpleTermCode()
        {
            var btns = EventTemplateCore.GetTemplateButtons(2);
            for (int i = 1; i < btns.Count; i++)
            {
                Assert.Equal(EventTemplateCore.TermCode.SimpleTermCode, btns[i].Term);
            }
        }

        // ---- Real-ROM smoke: generation + disassembly round-trip ----------

        [Fact]
        public void RealRom_FE8U_GenerateButtons_ProducesDisassemblableBytes()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip when no ROM available

            var savedRom = CoreState.ROM;
            var savedEs = CoreState.EventScript;
            var savedEnc = CoreState.SystemTextEncoder;
            var savedComment = CoreState.CommentCache;
            var savedBaseDir = CoreState.BaseDirectory;
            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                CoreState.BaseDirectory = FindRepoConfigBase() ?? asmDir;

                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                if (CoreState.CommentCache == null)
                {
                    CoreState.CommentCache = new HeadlessEtcCache();
                }
                CoreState.EventScript = null; // force fresh load

                // BLANK always works.
                var blankBtn = EventTemplateCore.GetTemplateButtons(1)[0];
                byte[] blank = EventTemplateCore.GenerateButton(rom, blankBtn);
                Assert.NotNull(blank);
                Assert.True(blank.Length >= 4);

                // VILLAGE_TALK produces real bytes that disassemble.
                var villageTalk = EventTemplateCore.GetTemplateButtons(1)[1];
                byte[] gen = EventTemplateCore.GenerateButton(rom, villageTalk);
                Assert.NotNull(gen);
                Assert.True(gen.Length > 4);

                var preview = EventTemplateCore.DisassemblePreview(rom, gen);
                Assert.NotEmpty(preview);
                Assert.Contains(preview, l => l.Contains("\t//"));
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.EventScript = savedEs;
                CoreState.SystemTextEncoder = savedEnc;
                CoreState.CommentCache = savedComment;
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        [Fact]
        public void RealRom_FE8U_Browser_ContextRequiredTemplates_DoNotEmitPartialBytes()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip

            var savedRom = CoreState.ROM;
            var savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = FindRepoConfigBase()
                    ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;

                var templates = EventTemplateCore.LoadBrowserTemplates(rom);
                Assert.NotEmpty(templates);

                bool sawContextRequired = false;
                foreach (var et in templates)
                {
                    byte[] bytes = EventTemplateCore.GenerateBrowserTemplate(rom, et);
                    if (et.RequiresContext)
                    {
                        sawContextRequired = true;
                        // Never emit partial/truncated bytes for context-required.
                        Assert.Null(bytes);
                    }
                }
                // The shipped FE8 list contains _COND_/PREPARATION placeholders.
                Assert.True(sawContextRequired);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        // ---- #1585: codes-returning helpers (in-editor template insert) ----

        [Fact]
        public void TryGenerateButtonCodes_NoRom_ReturnsNoRom_EmptyCodes()
        {
            var btn = EventTemplateCore.GetTemplateButtons(1)[0];
            var r = EventTemplateCore.TryGenerateButtonCodes(null, btn, out var codes);
            Assert.Equal(EventTemplateCore.GenerateResult.NoRom, r);
            Assert.NotNull(codes);
            Assert.Empty(codes);
        }

        [Fact]
        public void DisassembleToCodes_NullOrEmpty_ReturnsEmpty()
        {
            var rom = MakeFE8U();
            Assert.Empty(EventTemplateCore.DisassembleToCodes(rom, null));
            Assert.Empty(EventTemplateCore.DisassembleToCodes(rom, new byte[0]));
            Assert.Empty(EventTemplateCore.DisassembleToCodes(null, new byte[] { 0, 0, 0, 0 }));
        }

        [Fact]
        public void DisassembleToCodes_ShortTail_DoesNotFabricateBytes()
        {
            // Copilot PR review: DisAseemble synthesizes a full 4-byte UNKNOWN even when
            // fewer than Script.Size bytes remain (a 2-byte tail), whose ByteData is
            // zero-filled to 4 bytes. The bounds guard must stop before adding such a
            // command so the concatenated OneCode bytes never exceed the input length.
            var rom = MakeFE8U();
            var savedEs = CoreState.EventScript;
            var savedComment = CoreState.CommentCache;
            try
            {
                // DisAseemble dereferences CoreState.CommentCache for each command comment.
                if (CoreState.CommentCache == null)
                    CoreState.CommentCache = new HeadlessEtcCache();

                // Build a minimal vocabulary with one 4-byte command so the rest decodes as
                // 4-byte UNKNOWNs; a 6-byte blob = one 4-byte command + a 2-byte short tail.
                var es = new EventScript();
                typeof(EventScript).GetProperty("Scripts")!.SetValue(es, new[]
                {
                    EventScript.ParseScriptLine("0100XXXX\tCMD [X:UNIT:Units]"),
                });
                CoreState.EventScript = es;

                byte[] blob = { 0x01, 0x00, 0x00, 0x00, 0xAB, 0xCD }; // CMD + 2 stray bytes
                var codes = EventTemplateCore.DisassembleToCodes(rom, blob);

                // Concatenated bytes must NOT exceed the 6-byte input (no fabricated tail).
                int total = 0;
                foreach (var c in codes) total += c.ByteData?.Length ?? 0;
                Assert.True(total <= blob.Length,
                    $"DisassembleToCodes fabricated bytes: {total} > {blob.Length}");
            }
            finally
            {
                CoreState.EventScript = savedEs;
                CoreState.CommentCache = savedComment;
            }
        }

        [Fact]
        public void RealRom_FE8U_TryGenerateButtonCodes_ReturnsRoundTrippingCodes()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip when no ROM available

            var savedRom = CoreState.ROM;
            var savedEs = CoreState.EventScript;
            var savedEnc = CoreState.SystemTextEncoder;
            var savedComment = CoreState.CommentCache;
            var savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = FindRepoConfigBase()
                    ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                if (CoreState.CommentCache == null) CoreState.CommentCache = new HeadlessEtcCache();
                CoreState.EventScript = null;

                // VILLAGE_TALK is placeholder-free → Ok + non-empty disassembled codes.
                var villageTalk = EventTemplateCore.GetTemplateButtons(1)[1];
                var r = EventTemplateCore.TryGenerateButtonCodes(rom, villageTalk, out var codes);
                Assert.Equal(EventTemplateCore.GenerateResult.Ok, r);
                Assert.NotEmpty(codes);

                // Codes round-trip back to the same bytes as TryGenerateButton (no loss).
                EventTemplateCore.TryGenerateButton(rom, villageTalk, out byte[] bin);
                var concat = new List<byte>();
                foreach (var c in codes)
                    if (c?.ByteData != null) concat.AddRange(c.ByteData);
                Assert.Equal(bin, concat.ToArray());
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.EventScript = savedEs;
                CoreState.SystemTextEncoder = savedEnc;
                CoreState.CommentCache = savedComment;
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        [Fact]
        public void RealRom_FE8U_BrowserCodes_ContextRequired_ReturnsEmptyAndGated()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip

            var savedRom = CoreState.ROM;
            var savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = FindRepoConfigBase()
                    ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;

                var templates = EventTemplateCore.LoadBrowserTemplates(rom);
                Assert.NotEmpty(templates);

                bool sawContextRequired = false;
                foreach (var et in templates)
                {
                    var r = EventTemplateCore.TryGenerateBrowserTemplateCodes(rom, et, out var codes);
                    Assert.NotNull(codes);
                    if (et.RequiresContext)
                    {
                        sawContextRequired = true;
                        // context-required NEVER yields partial codes.
                        Assert.Equal(EventTemplateCore.GenerateResult.RequiresEditorContext, r);
                        Assert.Empty(codes);
                    }
                }
                Assert.True(sawContextRequired);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        // ---- helpers ------------------------------------------------------

        static string FindRom(string romName)
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = Path.Combine(dir, "roms", romName);
                    if (File.Exists(path)) return path;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        // Repo root that has config/data (so template_event_* configs resolve).
        static string FindRepoConfigBase()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")) &&
                    Directory.Exists(Path.Combine(dir, "config", "data")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
