using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="EventScriptEditorCore"/> (#1435) — the cross-platform
    /// event-script structural-editing engine. Covers the pure list mutations
    /// (insert/delete/move/template/import + indentation), serialization with
    /// auto-terminator selection, and the ROM-mutating <see cref="EventScriptEditorCore.WriteAll"/>
    /// path (in-place, relocate+repoint round-trip, no-reference refusal, no-free-space,
    /// invalid address) per the Copilot plan review.
    ///
    /// Synthetic disassemblers are built from <see cref="EventScript.ParseScriptLine"/>
    /// (same idiom as <c>EventScriptDisassemblyTests</c>); ROMs use
    /// <c>ROM.LoadLow("test.gba", data, "NAZO")</c> (ROMFE0 with real RomInfo, so
    /// default terminator codes and <c>FindFreeSpace</c> behave) like
    /// <c>RepointAllReferencesTests</c>.
    /// </summary>
    [Collection("SharedState")]
    public class EventScriptEditorCoreTests : IDisposable
    {
        readonly IEtcCache _prevComment;
        readonly EventScript _prevEs;
        readonly ROM _prevRom;

        public EventScriptEditorCoreTests()
        {
            _prevComment = CoreState.CommentCache;
            _prevEs = CoreState.EventScript;
            _prevRom = CoreState.ROM;
            if (CoreState.CommentCache == null)
                CoreState.CommentCache = new HeadlessEtcCache();
        }

        public void Dispose()
        {
            CoreState.CommentCache = _prevComment;
            CoreState.EventScript = _prevEs;
            CoreState.ROM = _prevRom;
        }

        // ── helpers ────────────────────────────────────────────────────

        static EventScript BuildEs(params EventScript.Script[] scripts)
        {
            var es = new EventScript();
            typeof(EventScript).GetProperty("Scripts")!.SetValue(es, scripts);
            return es;
        }

        // A 4-command vocabulary: a generic 4-byte command, a unit-arg command,
        // a pointer-arg CALL, and a 4-byte TERM (ENDA).
        static EventScript StdEs()
        {
            return BuildEs(
                EventScript.ParseScriptLine("0100XXXX\tLOAD1 [X:UNIT:Units]"),
                EventScript.ParseScriptLine("0200XXXX\tMOVE [X:UNIT:Units]"),
                EventScript.ParseScriptLine("03000000XXXXXXXX\tCALL [X:POINTER_EVENT:Target]"),
                EventScript.ParseScriptLine("0A000000\tENDA [TERM]"));
        }

        static ROM MakeRom(EventScript es, int size = 0x200000)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[size], "NAZO");
            CoreState.ROM = rom;          // U.isSafetyPointer / SearchEveneLength helpers read CoreState.ROM
            CoreState.EventScript = es;

            // ROMFE0 ("NAZO") leaves the default-terminator arrays null. Real ROMs
            // always populate them; seed deterministic values so Serialize selects
            // the correct terminator per event kind (term=0A, toplevel=0B, mapterm=0C).
            SetTerm("Default_event_script_term_code", new byte[] { 0x0A, 0x00, 0x00, 0x00 });
            SetTerm("Default_event_script_toplevel_code", new byte[] { 0x0B, 0x00, 0x00, 0x00 });
            SetTerm("Default_event_script_mapterm_code", new byte[] { 0x0C, 0x00, 0x00, 0x00 });

            void SetTerm(string prop, byte[] value)
            {
                var p = typeof(ROMFEINFO).GetProperty(prop);
                if (p != null && p.GetValue(rom.RomInfo) == null)
                    p.SetValue(rom.RomInfo, value);
            }
            return rom;
        }

        static void WriteBytes(ROM rom, uint addr, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) rom.Data[addr + i] = bytes[i];
        }

        static void WriteWord(ROM rom, uint addr, uint v)
        {
            rom.Data[addr + 0] = (byte)(v & 0xFF);
            rom.Data[addr + 1] = (byte)((v >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((v >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((v >> 24) & 0xFF);
        }

        static EventScript.OneCode Code(EventScript es, params byte[] bytes)
            => es.DisAseemble(bytes, 0);

        // ── list mutations ─────────────────────────────────────────────

        [Fact]
        public void Insert_NoSelection_AppendsToEnd()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x01, 0x00, 0x01, 0x00) });

            int sel = ed.Insert(-1, Code(es, 0x02, 0x00, 0x02, 0x00));

            Assert.Equal(2, ed.Count);
            Assert.Equal(1, sel);                                  // appended at end
            Assert.Equal(0x02, ed.Codes[1].ByteData[0]);
        }

        [Fact]
        public void Insert_AfterSelectedRegularCommand()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[]
            {
                Code(es, 0x01, 0x00, 0x01, 0x00),
                Code(es, 0x02, 0x00, 0x02, 0x00),
            });

            int sel = ed.Insert(0, Code(es, 0x02, 0x00, 0x09, 0x00));

            Assert.Equal(3, ed.Count);
            Assert.Equal(1, sel);                                  // inserted AFTER index 0
            Assert.Equal(0x09, ed.Codes[1].ByteData[2]);
        }

        [Fact]
        public void Insert_BeforeSelectedTermCommand()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[]
            {
                Code(es, 0x01, 0x00, 0x01, 0x00),
                Code(es, 0x0A, 0x00, 0x00, 0x00),                  // ENDA (TERM) at index 1
            });
            Assert.True(ed.IsTermAt(1));

            int sel = ed.Insert(1, Code(es, 0x02, 0x00, 0x02, 0x00));

            Assert.Equal(3, ed.Count);
            Assert.Equal(1, sel);                                  // inserted BEFORE the term
            Assert.Equal(0x02, ed.Codes[1].ByteData[0]);          // new code lands at 1
            Assert.True(ed.IsTermAt(2));                           // term pushed to 2
        }

        [Fact]
        public void Delete_RemovesAndSelectsAbove()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[]
            {
                Code(es, 0x01, 0x00, 0x01, 0x00),
                Code(es, 0x02, 0x00, 0x02, 0x00),
                Code(es, 0x0A, 0x00, 0x00, 0x00),
            });

            int sel = ed.Delete(1);

            Assert.Equal(2, ed.Count);
            Assert.Equal(0, sel);
            Assert.Equal(0x0A, ed.Codes[1].ByteData[0]);
        }

        [Fact]
        public void MoveUp_And_MoveDown_SwapAndClampAtEdges()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[]
            {
                Code(es, 0x01, 0x00, 0x01, 0x00),
                Code(es, 0x02, 0x00, 0x02, 0x00),
            });

            int up = ed.MoveUp(1);
            Assert.Equal(0, up);
            Assert.Equal(0x02, ed.Codes[0].ByteData[0]);
            Assert.Equal(0x01, ed.Codes[1].ByteData[0]);

            int down = ed.MoveDown(0);
            Assert.Equal(1, down);
            Assert.Equal(0x01, ed.Codes[0].ByteData[0]);

            // Boundary no-ops
            Assert.Equal(0, ed.MoveUp(0));       // already at top
            Assert.Equal(1, ed.MoveDown(1));     // already at bottom
        }

        [Fact]
        public void InsertRange_InsertsTemplateBlock()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x0A, 0x00, 0x00, 0x00) });

            var template = new List<EventScript.OneCode>
            {
                Code(es, 0x01, 0x00, 0x01, 0x00),
                Code(es, 0x02, 0x00, 0x02, 0x00),
            };
            int lastSel = ed.InsertRange(0, template);

            Assert.Equal(3, ed.Count);
            Assert.Equal(1, lastSel);
            Assert.Equal(0x01, ed.Codes[0].ByteData[0]);
            Assert.Equal(0x02, ed.Codes[1].ByteData[0]);
        }

        // ── new-code construction ──────────────────────────────────────

        [Fact]
        public void CloneScriptDefaultByte_AppliesTimingDefaults()
        {
            // FADESPEED arg at position 1: zero in template → default 16.
            var fadeScript = EventScript.ParseScriptLine("05XX0000\tFADE [X:FADESPEED:Speed]");
            byte[] def = EventScriptEditorCore.CloneScriptDefaultByte(fadeScript);
            Assert.Equal(16, def[1]);                              // FADESPEED default
        }

        [Fact]
        public void NewCodeFromScript_ProducesDisassemblableCode()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            var code = ed.NewCodeFromScript(es.Scripts[0]);        // LOAD1
            Assert.NotNull(code);
            Assert.Contains("LOAD1", EventScript.makeCommandComboText(code.Script, false));
        }

        // ── text import ────────────────────────────────────────────────

        [Fact]
        public void LineToEventByte_StopsAtFirstNonHex_AndIgnoresTrailingComment()
        {
            byte[] b = EventScriptEditorCore.LineToEventByte("0100 4200 // a unit load comment");
            // Parsing is contiguous-pair until first non-hex: "01 00" then space stops it.
            Assert.Equal(new byte[] { 0x01, 0x00 }, b);
        }

        [Fact]
        public void ImportFromText_AppendsEachLineWithAtLeast4Bytes()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(Array.Empty<EventScript.OneCode>());

            string text = "01000100\n0200" /*too short*/ + "\n0A000000\n";
            int imported = ed.ImportFromText(text, insertPoint: -1, clear: false);

            Assert.Equal(2, imported);                             // the 2-byte line is skipped
            Assert.Equal(2, ed.Count);
            Assert.Equal(0x01, ed.Codes[0].ByteData[0]);
            Assert.Equal(0x0A, ed.Codes[1].ByteData[0]);
        }

        [Fact]
        public void ImportFromText_Clear_ReplacesList()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x02, 0x00, 0x02, 0x00) });

            int imported = ed.ImportFromText("01000100\n", insertPoint: -1, clear: true);

            Assert.Equal(1, imported);
            Assert.Single(ed.Codes);
            Assert.Equal(0x01, ed.Codes[0].ByteData[0]);
        }

        // ── serialization / terminator selection ───────────────────────

        [Fact]
        public void Serialize_AppendsTermWhenAbsent_Normal()
        {
            var es = StdEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x01, 0x00, 0x01, 0x00) });

            byte[] data = ed.Serialize(rom, isWorldMapEvent: false, isTopLevelEvent: false);

            // 4 command bytes + the default term code (ROMFE0 term code length).
            byte[] term = rom.RomInfo.Default_event_script_term_code;
            Assert.Equal(4 + term.Length, data.Length);
            Assert.Equal(0x01, data[0]);
        }

        [Fact]
        public void Serialize_NoExtraTermWhenAlreadyPresent()
        {
            var es = StdEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[]
            {
                Code(es, 0x01, 0x00, 0x01, 0x00),
                Code(es, 0x0A, 0x00, 0x00, 0x00),                  // TERM already present
            });

            byte[] data = ed.Serialize(rom, false, false);
            Assert.Equal(8, data.Length);                          // no extra term appended
        }

        [Fact]
        public void Serialize_TopLevelUsesTopLevelTerm()
        {
            var es = StdEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x01, 0x00, 0x01, 0x00) });

            byte[] data = ed.Serialize(rom, isWorldMapEvent: false, isTopLevelEvent: true);
            byte[] term = rom.RomInfo.Default_event_script_toplevel_code;
            Assert.Equal(4 + term.Length, data.Length);
        }

        // ── #1585: script-type-aware terminator (Procs / AI) ───────────

        // A Procs-style vocabulary: a generic 8-byte command + the 8-byte Procs `End`
        // TERM (0000000000000000). Mirrors the shipped 6c_script_ALL.txt {TERM} command.
        static EventScript ProcsEs()
        {
            return BuildEs(
                EventScript.ParseScriptLine("1100XXXX00000000\tPROC1 [X:UNIT:Units]"),
                EventScript.ParseScriptLine("0000000000000000\tEnd (Deletes Self) [TERM]"));
        }

        [Fact]
        public void Serialize_Procs_AppendsProcsEnd_NotEventTerminator()
        {
            var es = ProcsEs();
            var rom = MakeRom(es);
            // Procs-typed editor: a terminal-less list must get the Procs `End`, NOT the
            // FE event terminator 0A000000 (Copilot #1585 finding #1).
            var ed = new EventScriptEditorCore(es, EventScript.EventScriptType.Procs);
            ed.SetCodes(new[] { Code(es, 0x11, 0x00, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00) });

            byte[] data = ed.Serialize(rom, false, false);

            // 8 command bytes + 8-byte Procs End (NOT the 4-byte FE event term 0A000000).
            Assert.Equal(16, data.Length);
            // tail is the Procs End: all-zero 8 bytes.
            for (int i = 8; i < 16; i++) Assert.Equal(0x00, data[i]);
            // and it is NOT the event terminator byte 0x0A anywhere in the appended tail.
            Assert.NotEqual(0x0A, data[8]);
        }

        [Fact]
        public void Serialize_Procs_NoExtraTermWhenProcsEndPresent()
        {
            var es = ProcsEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es, EventScript.EventScriptType.Procs);
            ed.SetCodes(new[]
            {
                Code(es, 0x11, 0x00, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00),
                Code(es, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00),  // Procs End present
            });

            byte[] data = ed.Serialize(rom, false, false);
            Assert.Equal(16, data.Length);  // no extra terminator appended
        }

        [Fact]
        public void Serialize_Procs_NoTermInVocabulary_Throws()
        {
            // A Procs vocabulary with NO {TERM} command — refuse rather than invent an
            // FE event terminator (#1585 finding #1).
            var es = BuildEs(EventScript.ParseScriptLine("1100XXXX00000000\tPROC1 [X:UNIT:Units]"));
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es, EventScript.EventScriptType.Procs);
            ed.SetCodes(new[] { Code(es, 0x11, 0x00, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00) });

            Assert.Throws<EventScriptEditorCore.MissingTerminatorException>(
                () => ed.Serialize(rom, false, false));
        }

        [Fact]
        public void WriteAll_Procs_NoTermInVocabulary_RefusesNoTerminator_RomUnchanged()
        {
            var es = BuildEs(EventScript.ParseScriptLine("1100XXXX00000000\tPROC1 [X:UNIT:Units]"));
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es, EventScript.EventScriptType.Procs);
            ed.SetCodes(new[] { Code(es, 0x11, 0x00, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00) });

            byte[] before = (byte[])rom.Data.Clone();
            var undo = new Undo.UndoData { name = "t", list = new List<Undo.UndoPostion>() };
            var result = ed.WriteAll(rom, 0x1000, false, false, undo, out uint _);

            Assert.Equal(EventScriptEditorCore.WriteResult.NoTerminator, result);
            Assert.Equal(before, rom.Data);  // byte-identical, nothing written
        }

        [Fact]
        public void Serialize_Procs_PicksConcreteEnd_NotPlaceholderEnd2End3()
        {
            // Copilot #1589: the shipped Procs vocabulary has multiple SAME-LENGTH TERM
            // commands — End (0000000000000000), End2 (00001000ZZZZZZZZ), End3
            // (00080000ZZZZZZZZ). EventScript.Load sorts by size with a NON-stable sort, so
            // "first shortest TERM" is non-deterministic. FindFamilyTermBytes must
            // DETERMINISTICALLY pick the concrete all-hex End (all-zero bytes), never the
            // placeholder End2/End3 (non-zero leading bytes).
            var es = BuildEs(
                EventScript.ParseScriptLine("1100XXXX00000000\tPROC1 [X:UNIT:Units]"),
                EventScript.ParseScriptLine("00001000ZZZZZZZZ\tEnd2 [ZZZZZZZZ::Unk1] (Deletes Self) [TERM]"),
                EventScript.ParseScriptLine("00080000ZZZZZZZZ\tEnd3 [ZZZZZZZZ::Unk1] (Deletes Self) [TERM]"),
                EventScript.ParseScriptLine("0000000000000000\tEnd (Deletes Self) [TERM]"));
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es, EventScript.EventScriptType.Procs);
            ed.SetCodes(new[] { Code(es, 0x11, 0x00, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00) });

            byte[] data = ed.Serialize(rom, false, false);

            // 8 command bytes + the canonical all-zero End (NOT End2 0x10 @ byte 2 or End3
            // 0x08 @ byte 1).
            Assert.Equal(16, data.Length);
            for (int i = 8; i < 16; i++) Assert.Equal(0x00, data[i]);
        }

        [Fact]
        public void Serialize_DefaultCtorStillEvent_AppendsEventTerminator()
        {
            // Regression guard: the legacy single-arg ctor remains an Event editor, so
            // existing Event behavior (FE event terminator) is unchanged.
            var es = StdEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es); // default => Event
            ed.SetCodes(new[] { Code(es, 0x01, 0x00, 0x01, 0x00) });

            byte[] data = ed.Serialize(rom, false, false);
            byte[] term = rom.RomInfo.Default_event_script_term_code;
            Assert.Equal(4 + term.Length, data.Length);
            Assert.Equal(0x0A, data[4]); // FE event terminator
        }

        // ── WriteAll: in-place ─────────────────────────────────────────

        [Fact]
        public void WriteAll_InPlace_WhenItFits_ZeroFillsTail()
        {
            var es = StdEs();
            var rom = MakeRom(es);

            // Original region: LOAD1, MOVE, ENDA (12 bytes) at 0x1000.
            uint baseOff = 0x1000;
            WriteBytes(rom, baseOff, new byte[]
            {
                0x01, 0x00, 0x01, 0x00,
                0x02, 0x00, 0x02, 0x00,
                0x0A, 0x00, 0x00, 0x00,
            });

            var ed = new EventScriptEditorCore(es);
            ed.BuildFromRom(rom, baseOff);
            Assert.Equal(3, ed.Count);

            // Delete the MOVE → list shrinks → must fit in place.
            ed.Delete(1);
            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            var result = ed.WriteAll(rom, baseOff, false, false, undo, out uint newAddr);

            Assert.Equal(EventScriptEditorCore.WriteResult.InPlace, result);
            Assert.Equal(baseOff, newAddr);
            Assert.Equal(0x01, rom.Data[baseOff + 0]);             // LOAD1
            Assert.Equal(0x0A, rom.Data[baseOff + 4]);             // ENDA now at +4
            Assert.Equal(0x00, rom.Data[baseOff + 8]);             // tail zero-filled
            Assert.Equal(0x00, rom.Data[baseOff + 11]);
        }

        // ── WriteAll: relocate + repoint round-trip ────────────────────

        [Fact]
        public void WriteAll_Relocate_WhenGrown_RepointsRawPointer_AndRoundTrips()
        {
            var es = StdEs();
            var rom = MakeRom(es);

            uint baseOff = 0x1000;
            uint basePtr = 0x08000000 + baseOff;

            // Original region: just LOAD1 + ENDA (8 bytes).
            WriteBytes(rom, baseOff, new byte[]
            {
                0x01, 0x00, 0x01, 0x00,
                0x0A, 0x00, 0x00, 0x00,
            });
            // A raw pointer somewhere else referencing the script base.
            uint ownerSlot = 0x4000;
            WriteWord(rom, ownerSlot, basePtr);

            var ed = new EventScriptEditorCore(es);
            ed.BuildFromRom(rom, baseOff);
            Assert.Equal(2, ed.Count);

            // Insert two MOVE commands → grows past the 8-byte region → must relocate.
            ed.Insert(0, Code(es, 0x02, 0x00, 0x02, 0x00));
            ed.Insert(1, Code(es, 0x02, 0x00, 0x03, 0x00));
            Assert.Equal(4, ed.Count);

            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            var result = ed.WriteAll(rom, baseOff, false, false, undo, out uint newAddr);

            Assert.Equal(EventScriptEditorCore.WriteResult.Relocated, result);
            Assert.NotEqual(baseOff, newAddr);

            // The owner pointer was repointed to the new offset.
            uint newSlotVal = (uint)(rom.Data[ownerSlot]
                | (rom.Data[ownerSlot + 1] << 8)
                | (rom.Data[ownerSlot + 2] << 16)
                | (rom.Data[ownerSlot + 3] << 24));
            Assert.Equal(0x08000000 + newAddr, newSlotVal);

            // The relocated bytes match the serialized script.
            byte[] expected = ed.Serialize(rom, false, false);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], rom.Data[newAddr + i]);

            // The old region was zero-filled.
            Assert.Equal(0x00, rom.Data[baseOff]);
        }

        // ── WriteAll: no-reference refusal (Copilot finding #2) ────────

        [Fact]
        public void WriteAll_Relocate_NoReference_Refuses_AndLeavesSourceIntact()
        {
            var es = StdEs();
            var rom = MakeRom(es);

            uint baseOff = 0x1000;
            byte[] original = { 0x01, 0x00, 0x01, 0x00, 0x0A, 0x00, 0x00, 0x00 };
            WriteBytes(rom, baseOff, original);
            // NO pointer anywhere references baseOff.

            var ed = new EventScriptEditorCore(es);
            ed.BuildFromRom(rom, baseOff);
            ed.Insert(0, Code(es, 0x02, 0x00, 0x02, 0x00));
            ed.Insert(1, Code(es, 0x02, 0x00, 0x03, 0x00));        // grow → would relocate

            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            var result = ed.WriteAll(rom, baseOff, false, false, undo, out uint newAddr);

            Assert.Equal(EventScriptEditorCore.WriteResult.NoReferenceRefused, result);
            Assert.Equal(baseOff, newAddr);
            // Source bytes untouched (no destructive clear).
            for (int i = 0; i < original.Length; i++)
                Assert.Equal(original[i], rom.Data[baseOff + i]);
        }

        [Fact]
        public void WriteAll_Relocate_NoReference_OptOut_Relocates()
        {
            var es = StdEs();
            var rom = MakeRom(es);

            uint baseOff = 0x1000;
            WriteBytes(rom, baseOff, new byte[] { 0x01, 0x00, 0x01, 0x00, 0x0A, 0x00, 0x00, 0x00 });

            var ed = new EventScriptEditorCore(es) { RefuseRelocateWithoutReference = false };
            ed.BuildFromRom(rom, baseOff);
            ed.Insert(0, Code(es, 0x02, 0x00, 0x02, 0x00));
            ed.Insert(1, Code(es, 0x02, 0x00, 0x03, 0x00));

            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            var result = ed.WriteAll(rom, baseOff, false, false, undo, out uint newAddr);

            Assert.Equal(EventScriptEditorCore.WriteResult.Relocated, result);
            Assert.NotEqual(baseOff, newAddr);
        }

        // ── WriteAll: guards ───────────────────────────────────────────

        [Fact]
        public void WriteAll_EmptyList_IsNoOp()
        {
            var es = StdEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es);

            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            var result = ed.WriteAll(rom, 0x1000, false, false, undo, out _);
            Assert.Equal(EventScriptEditorCore.WriteResult.NoOp, result);
        }

        [Fact]
        public void WriteAll_ZeroAddress_IsNoOp()
        {
            var es = StdEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x0A, 0x00, 0x00, 0x00) });

            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            var result = ed.WriteAll(rom, 0x0, false, false, undo, out _);
            Assert.Equal(EventScriptEditorCore.WriteResult.NoOp, result);
        }

        [Fact]
        public void WriteAll_DangerZoneAddress_RefusesUnsafe()
        {
            var es = StdEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x0A, 0x00, 0x00, 0x00) });

            byte[] before = U.getBinaryData(rom.Data, 0x100, 8);
            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            // 0x100 is < 0x200 → danger zone → isSafetyOffset false.
            var result = ed.WriteAll(rom, 0x100, false, false, undo, out _);
            Assert.Equal(EventScriptEditorCore.WriteResult.UnsafeAddress, result);
            Assert.Equal(before, U.getBinaryData(rom.Data, 0x100, 8)); // untouched
        }

        [Fact]
        public void WriteAll_UnalignedAddress_RefusesUnsafe()
        {
            var es = StdEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x0A, 0x00, 0x00, 0x00) });

            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            // 0x1002 is not 4-byte aligned.
            var result = ed.WriteAll(rom, 0x1002, false, false, undo, out _);
            Assert.Equal(EventScriptEditorCore.WriteResult.UnsafeAddress, result);
        }

        [Fact]
        public void Serialize_WorldMapUsesWorldMapTerm()
        {
            var es = StdEs();
            var rom = MakeRom(es);
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x01, 0x00, 0x01, 0x00) });

            byte[] data = ed.Serialize(rom, isWorldMapEvent: true, isTopLevelEvent: false);
            byte[] term = rom.RomInfo.Default_event_script_mapterm_code;
            Assert.Equal(4 + term.Length, data.Length);
            // Confirms the mapterm code (0x0C in our seed) was appended, not the normal term.
            Assert.Equal(term[0], data[4]);
        }

        [Fact]
        public void SetCodes_DeepClones_CallerListNotMutatedByRelocate()
        {
            var es = StdEs();
            var rom = MakeRom(es);

            uint baseOff = 0x1000;
            uint basePtr = 0x08000000 + baseOff;
            WriteBytes(rom, baseOff, new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x08, 0x0A, 0x00, 0x00, 0x00 });
            uint ownerSlot = 0x4000;
            WriteWord(rom, ownerSlot, basePtr);

            // A CALL command whose pointer arg == basePtr, held by the CALLER.
            var callerCode = Code(es, 0x03, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x08);
            byte[] callerBytesBefore = (byte[])callerCode.ByteData.Clone();

            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { callerCode });          // deep-cloned into the editor
            // Grow so it relocates (NotifyChangePointer mutates the editor's ByteData).
            ed.Insert(0, Code(es, 0x02, 0x00, 0x02, 0x00));

            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            ed.WriteAll(rom, baseOff, false, false, undo, out _); // refs==0 path won't fire (ownerSlot refs base) — relocates

            // The caller's OneCode.ByteData is UNCHANGED — the editor mutated its own clone.
            Assert.Equal(callerBytesBefore, callerCode.ByteData);
        }

        [Fact]
        public void InsertRange_DeepClones_CallerTemplateNotMutatedByRelocate()
        {
            var es = StdEs();
            var rom = MakeRom(es);

            uint baseOff = 0x1000;
            uint basePtr = 0x08000000 + baseOff;
            WriteBytes(rom, baseOff, new byte[] { 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 });
            WriteWord(rom, 0x4000, basePtr); // inbound reference so relocation is allowed

            var ed = new EventScriptEditorCore(es);
            ed.BuildFromRom(rom, baseOff);

            // A caller-owned template containing a CALL whose pointer == basePtr.
            var templateCode = Code(es, 0x03, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x08);
            byte[] templateBytesBefore = (byte[])templateCode.ByteData.Clone();
            ed.InsertRange(0, new List<EventScript.OneCode> { templateCode });

            // Grow further so WriteAll relocates and runs NotifyChangePointer over the list.
            ed.Insert(0, Code(es, 0x02, 0x00, 0x02, 0x00));

            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            ed.WriteAll(rom, baseOff, false, false, undo, out _);

            // The caller's template OneCode bytes are untouched (engine cloned on insert).
            Assert.Equal(templateBytesBefore, templateCode.ByteData);
        }

        [Fact]
        public void SetCodes_And_InsertRange_SkipNulls_NoThrow()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);

            // A list containing nulls must not be stored (would NRE in JisageReorder).
            ed.SetCodes(new EventScript.OneCode[]
            {
                Code(es, 0x01, 0x00, 0x01, 0x00),
                null,
                Code(es, 0x0A, 0x00, 0x00, 0x00),
            });
            Assert.Equal(2, ed.Count);

            int lastSel = ed.InsertRange(0, new List<EventScript.OneCode>
            {
                null,
                Code(es, 0x02, 0x00, 0x02, 0x00),
            });
            Assert.Equal(3, ed.Count);          // only the non-null was inserted
            Assert.Equal(0, lastSel);            // 1 inserted → insertedPoint + 0
        }

        [Fact]
        public void InsertRange_AllNull_ReturnsValidSelectionIndex_NoThrow()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x01, 0x00, 0x01, 0x00) });

            // Inserting at the end (index 1) with an all-null list inserts nothing; the
            // returned index must be a valid selection (not point past the unchanged list).
            int sel = ed.InsertRange(1, new List<EventScript.OneCode> { null, null });
            Assert.Equal(1, ed.Count);                 // nothing inserted
            Assert.InRange(sel, -1, ed.Count - 1);     // valid selection index
        }

        [Fact]
        public void WriteAll_InPlace_NearEof_ClampsOriginalSize_NoOutOfBounds()
        {
            var es = StdEs();
            // Small ROM whose end is just past a single LOAD1, so ScanLength's synthetic
            // UNKNOWN would otherwise overstate the region past EOF.
            var rom = MakeRom(es, size: 0x2010);
            uint baseOff = 0x2000;                      // 16 bytes from end
            WriteBytes(rom, baseOff, new byte[] { 0x01, 0x00, 0x00, 0x00 }); // LOAD1, no term

            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[] { Code(es, 0x01, 0x00, 0x00, 0x00) });

            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            // Must not throw an out-of-bounds write; either writes in place within bounds or
            // relocates/refuses — never past EOF.
            var ex = Record.Exception(() => ed.WriteAll(rom, baseOff, false, false, undo, out _));
            Assert.Null(ex);
        }

        [Fact]
        public void WriteAll_NestedInOuterAmbientScope_PreservesOuterScope()
        {
            var es = StdEs();
            var rom = MakeRom(es);
            uint baseOff = 0x1000;
            WriteBytes(rom, baseOff, new byte[] { 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 });

            var ed = new EventScriptEditorCore(es);
            ed.BuildFromRom(rom, baseOff);
            ed.Delete(ed.Count - 1); // shrink → in-place write

            var outer = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            var inner = new Undo.UndoData { list = new List<Undo.UndoPostion>() };

            using (ROM.BeginUndoScope(outer))
            {
                // A write through the OUTER scope before the nested WriteAll.
                rom.write_u8(0x5000, 0xAB);
                Assert.Same(outer, ROM.GetAmbientUndoData());

                // WriteAll opens + restores its own scope; the outer must survive.
                ed.WriteAll(rom, baseOff, false, false, inner, out _);
                Assert.Same(outer, ROM.GetAmbientUndoData()); // outer restored

                // A subsequent write still records into the OUTER scope.
                int outerBefore = outer.list.Count;
                rom.write_u8(0x5001, 0xCD);
                Assert.True(outer.list.Count > outerBefore);
            }
            Assert.Null(ROM.GetAmbientUndoData()); // fully unwound
            Assert.True(inner.list.Count > 0);      // WriteAll recorded into its own buffer
        }

        // ── round-trip: export → import ────────────────────────────────

        [Fact]
        public void ExportToText_RoundTrips_ThroughImportFromText()
        {
            var es = StdEs();
            var ed = new EventScriptEditorCore(es);
            ed.SetCodes(new[]
            {
                Code(es, 0x01, 0x00, 0x01, 0x00),
                Code(es, 0x02, 0x00, 0x02, 0x00),
                Code(es, 0x0A, 0x00, 0x00, 0x00),
            });

            string text = ed.ExportToText();

            var ed2 = new EventScriptEditorCore(es);
            int imported = ed2.ImportFromText(text, -1, true);

            Assert.Equal(3, imported);
            for (int i = 0; i < ed.Count; i++)
                Assert.Equal(ed.Codes[i].ByteData, ed2.Codes[i].ByteData);
        }
    }
}
