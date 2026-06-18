// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for UseFlagScanCore (#1192) — the strictly READ-ONLY per-chapter
// flag-usage aggregator that backs the Avalonia "Flags-Used-in-Chapter" tool.
//
// SYNTHETIC scratch-ROM tests build a minimal FE8 chapter and assert the three
// in-scope flag sources are each found:
//   (1) EVENT_COND_*  — the flag field (u16 @ +2) of a Turn condition record,
//   (2) EVENTSCRIPT   — an ArgType.FLAG arg in the event script that the Turn
//                       record's event pointer reaches, and
//   (3) MAPCHANGE     — the flag field (u16 @ +5) of a map-change record,
//   (4) HAIKU         — the flag field (u16 @ +4) of a death-quote record, and
//   (5) BATTTLE_TALK  — the flag field (u16 @ +6) of a battle-conversation record.
// Plus guard tests: id 0 is dropped, a foreign/empty ROM yields an empty list,
// the result is sorted by flag id, the dedup invariant holds, and the
// haiku/battletalk rows are scoped to the record's own chapter (#1253).

using System.Collections.Generic;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class UseFlagScanCoreTests
    {
        const int RomSize = 0x1000000; // 16MB — FE8 LoadLow minimum.

        // Layout (FE8U): map_setting_datasize=148, event_plist_pos=116,
        // eventcond_tern_size=12, eventcond_talk_size=16,
        // cond slot 0 = Turn, slot 1 = Talk.
        const uint MapTableBase   = 0x00700000u;
        const uint EventTableBase = 0x00710000u;
        const uint CondBlock      = 0x00720000u;
        const uint TurnRecord     = 0x00730000u;
        const uint EventScriptAddr= 0x00740000u;
        const uint ChangeTable    = 0x00750000u;
        const uint ChangeData     = 0x00760000u;
        // Second event-script tree, reached from the Talk cond slot — references
        // the SAME ScriptFlag, to prove cross-TREE dedup (one EVENTSCRIPT row).
        const uint TalkRecord      = 0x00770000u;
        const uint EventScriptAddr2= 0x00780000u;
        // FE8 Haiku table (event_haiku_pointer → 12-byte records, flag@+4,
        // chapter@+3) and BattleTalk table (event_ballte_talk_pointer → 16-byte
        // records, flag@+6, chapter@+4).
        const uint HaikuTable      = 0x00790000u;
        const uint BattleTalkTable = 0x007A0000u;

        const uint CondFlag      = 0x0011; // Turn record flag (u16 @ +2)
        const uint ScriptFlag    = 0x0022; // event-script FLAG arg
        const uint ChangeFlag    = 0x0033; // map-change record flag (u16 @ +5)
        const uint HaikuFlag     = 0x0044; // FE8 Haiku record flag (u16 @ +4)
        const uint BattleTalkFlag= 0x0055; // FE8 BattleTalk record flag (u16 @ +6)
        const uint HaikuOtherChapterFlag = 0x0066; // Haiku flag in a DIFFERENT chapter

        // A command "0100 XXXX" — opcode 0x0001 then a 2-byte FLAG arg at byte 2.
        static EventScript.Script FlagCommand()
            => EventScript.ParseScriptLine("0100XXXX\tSETFLAG [XXXX:FLAG:Flag]");

        // Terminator "0A 00 00 00".
        static EventScript.Script TermCommand()
            => EventScript.ParseScriptLine("0A000000\tENDA [TERM]");

        static EventScript BuildEventScript(params EventScript.Script[] scripts)
        {
            var es = new EventScript();
            typeof(EventScript).GetProperty("Scripts")!.SetValue(es, scripts);
            return es;
        }

        static void WriteU32(ROM rom, uint offset, uint value)
        {
            rom.Data[offset + 0] = (byte)(value & 0xFF);
            rom.Data[offset + 1] = (byte)((value >> 8) & 0xFF);
            rom.Data[offset + 2] = (byte)((value >> 16) & 0xFF);
            rom.Data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>
        /// Build a synthetic FE8 chapter (map id 0) wiring all three flag
        /// sources, run <paramref name="body"/> with CoreState fully wired (the
        /// disasm path needs ROM + EventScript + CommentCache), then restore.
        /// </summary>
        static void WithChapter(System.Action<ROM> body)
        {
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            var prevComment = CoreState.CommentCache;
            try
            {
                var rom = new ROM();
                rom.LoadLow("flagscan-fe8u.gba", new byte[RomSize], "BE8E01");

                uint dataSize = rom.RomInfo.map_setting_datasize;
                uint eventPlistPos = rom.RomInfo.map_setting_event_plist_pos;

                // 1) Map setting table: one map (id 0), event_plist = 1,
                //    mapchange_plist (offset 11) = 3.
                WriteU32(rom, rom.RomInfo.map_setting_pointer, MapTableBase | 0x08000000u);
                WriteU32(rom, MapTableBase + 0, 0x08123456u); // first dword = pointer → valid
                rom.Data[MapTableBase + eventPlistPos] = 1;
                rom.Data[MapTableBase + 11] = 3;              // mapchange plist
                WriteU32(rom, MapTableBase + dataSize, 0x00000000u); // terminator row

                // 2) Event pointer table: entry[1] → cond block.
                WriteU32(rom, rom.RomInfo.map_event_pointer, EventTableBase | 0x08000000u);
                WriteU32(rom, EventTableBase + 1 * 4, CondBlock | 0x08000000u);

                // 3) Cond block slot 0 (Turn) → a 12-byte turn record:
                //    header non-zero, type=0, flag @ +2, event ptr @ +4.
                WriteU32(rom, CondBlock + 0, TurnRecord | 0x08000000u);
                rom.Data[TurnRecord + 0] = 0x02; // non-zero header / type != 1
                rom.write_u16(TurnRecord + 2, (ushort)CondFlag);
                WriteU32(rom, TurnRecord + 4, EventScriptAddr | 0x08000000u);
                // Next turn record header = 0 → terminates the slot walk.
                WriteU32(rom, TurnRecord + 12, 0x00000000u);

                // 3b) Cond block slot 1 (Talk, stride eventcond_talk_size=16) → a
                //     Talk record whose event ptr reaches a SECOND tree. This makes
                //     ScriptFlag referenced from TWO DIFFERENT trees (Turn + Talk) so
                //     the test proves CROSS-TREE dedup → still ONE EVENTSCRIPT row.
                WriteU32(rom, CondBlock + 4, TalkRecord | 0x08000000u);
                rom.Data[TalkRecord + 0] = 0x05; // non-zero header
                rom.write_u16(TalkRecord + 2, 0x00); // talk record's own flag = 0 (none)
                WriteU32(rom, TalkRecord + 4, EventScriptAddr2 | 0x08000000u);
                // Next Talk record header word = 0 → terminates the slot walk.
                WriteU32(rom, TalkRecord + 16, 0x00000000u);

                // 4) Tree 1 (from Turn): SETFLAG ScriptFlag ; SETFLAG ScriptFlag ; ENDA.
                //    The SAME flag is referenced TWICE within this one tree.
                rom.write_u16(EventScriptAddr + 0, 0x0001);
                rom.write_u16(EventScriptAddr + 2, (ushort)ScriptFlag);
                rom.write_u16(EventScriptAddr + 4, 0x0001);
                rom.write_u16(EventScriptAddr + 6, (ushort)ScriptFlag);
                WriteU32(rom, EventScriptAddr + 8, 0x0000000A);

                // 4b) Tree 2 (from Talk): SETFLAG ScriptFlag ; ENDA — the same flag
                //     from a DIFFERENT tree.
                rom.write_u16(EventScriptAddr2 + 0, 0x0001);
                rom.write_u16(EventScriptAddr2 + 2, (ushort)ScriptFlag);
                WriteU32(rom, EventScriptAddr2 + 4, 0x0000000A);

                // 5) Map-change PLIST table: entry[3] → change data block.
                WriteU32(rom, rom.RomInfo.map_mapchange_pointer, ChangeTable | 0x08000000u);
                WriteU32(rom, ChangeTable + 3 * 4, ChangeData | 0x08000000u);
                // 6) Change data: one 12-byte record (first byte != 0xFF),
                //    flag @ +5; next record's first byte = 0xFF → terminates.
                rom.Data[ChangeData + 0] = 0x01;
                rom.write_u16(ChangeData + 5, (ushort)ChangeFlag);
                rom.Data[ChangeData + 12] = 0xFF;

                // 7) FE8 Haiku table (event_haiku_pointer): 12-byte records,
                //    flag@+4, chapter@+3. Record 0 belongs to chapter 0 (this
                //    chapter); record 1 to chapter 5 (a DIFFERENT chapter, to prove
                //    the chapter-scope filter excludes it); record 2 = 0xFFFF
                //    sentinel terminates. First byte non-0xFFFF so the table starts.
                WriteU32(rom, rom.RomInfo.event_haiku_pointer, HaikuTable | 0x08000000u);
                rom.Data[HaikuTable + 0] = 0x01;                 // unit id (non-zero, not 0xFFFF)
                rom.Data[HaikuTable + 3] = 0x00;                 // chapter = 0 (this chapter)
                rom.write_u16(HaikuTable + 4, (ushort)HaikuFlag);
                rom.Data[HaikuTable + 12 + 0] = 0x02;            // record 1 unit id
                rom.Data[HaikuTable + 12 + 3] = 0x05;            // chapter = 5 (other chapter)
                rom.write_u16(HaikuTable + 12 + 4, (ushort)HaikuOtherChapterFlag);
                rom.write_u16(HaikuTable + 24 + 0, 0xFFFF);      // record 2 sentinel → terminates

                // 8) FE8 BattleTalk table (event_ballte_talk_pointer): 16-byte
                //    records, flag@+6, chapter@+4. Record 0 = chapter 0; record 1 =
                //    0xFFFF sentinel terminates.
                WriteU32(rom, rom.RomInfo.event_ballte_talk_pointer, BattleTalkTable | 0x08000000u);
                rom.write_u16(BattleTalkTable + 0, 0x0101);      // unit ids (non-zero, not 0xFFFF)
                rom.Data[BattleTalkTable + 4] = 0x00;            // chapter = 0 (this chapter)
                rom.write_u16(BattleTalkTable + 6, (ushort)BattleTalkFlag);
                rom.write_u16(BattleTalkTable + 16 + 0, 0xFFFF); // record 1 sentinel → terminates

                CoreState.ROM = rom;
                CoreState.EventScript = BuildEventScript(FlagCommand(), TermCommand());
                CoreState.CommentCache = new HeadlessEtcCache();

                body(rom);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
                CoreState.CommentCache = prevComment;
            }
        }

        [Fact]
        public void Scan_FindsEventCondFlag()
        {
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.Contains(list, u =>
                    u.ID == CondFlag && u.DataType == FELintCore.Type.EVENT_COND_TURN);
            });
        }

        [Fact]
        public void Scan_FindsEventScriptFlag()
        {
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.Contains(list, u =>
                    u.ID == ScriptFlag && u.DataType == FELintCore.Type.EVENTSCRIPT);
            });
        }

        [Fact]
        public void Scan_EventScriptFlag_DedupedPerChapterAndType()
        {
            // ScriptFlag is referenced from TWO different commands in tree 1 (Turn)
            // AND from tree 2 (Talk). Without chapter-level dedup that would be 3
            // EVENTSCRIPT rows (and a real chapter, with the flag across ~8 trees,
            // showed it 8x — PR #1254). The scan collapses to ONE row per
            // (flag id, source type), so ScriptFlag yields EXACTLY ONE EVENTSCRIPT
            // row regardless of how many trees/commands reference it.
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                var scriptRows = list.FindAll(u =>
                    u.ID == ScriptFlag && u.DataType == FELintCore.Type.EVENTSCRIPT);
                Assert.Single(scriptRows);
                // First occurrence wins (tree 1, the Turn slot's event script).
                Assert.Equal(EventScriptAddr, scriptRows[0].Addr);
            });
        }

        [Fact]
        public void Scan_NoFlagRepeatsWithinASourceType()
        {
            // Count-sanity / dedup invariant: across the whole returned list, no
            // (flag id, DataType) pair appears more than once — the property that
            // keeps the flat list readable (PR #1254 regression: 0x07 x8).
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                var seen = new System.Collections.Generic.HashSet<(uint, int)>();
                foreach (var u in list)
                    Assert.True(seen.Add((u.ID, (int)u.DataType)),
                        $"duplicate row for flag 0x{u.ID:X} type {u.DataType}");
            });
        }

        [Fact]
        public void Scan_FindsMapChangeFlag()
        {
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.Contains(list, u =>
                    u.ID == ChangeFlag && u.DataType == FELintCore.Type.MAPCHANGE);
            });
        }

        [Fact]
        public void Scan_FindsHaikuFlag()
        {
            // #1253: the FE8 Haiku record for THIS chapter (chapter byte = 0)
            // contributes a HAIKU-typed row for its flag.
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.Contains(list, u =>
                    u.ID == HaikuFlag && u.DataType == FELintCore.Type.HAIKU);
            });
        }

        [Fact]
        public void Scan_FindsBattleTalkFlag()
        {
            // #1253: the FE8 BattleTalk record for THIS chapter (chapter byte = 0)
            // contributes a BATTTLE_TALK-typed row for its flag.
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.Contains(list, u =>
                    u.ID == BattleTalkFlag && u.DataType == FELintCore.Type.BATTTLE_TALK);
            });
        }

        [Fact]
        public void Scan_Haiku_OtherChapterFlag_IsExcluded()
        {
            // The Haiku table's record 1 belongs to chapter 5; scanning chapter 0
            // must NOT surface its flag (WF ToolUseFlagForm scopes haiku/battletalk
            // rows to the selected chapter).
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.DoesNotContain(list, u => u.ID == HaikuOtherChapterFlag);
            });
        }

        [Fact]
        public void Scan_Haiku_OtherChapterFlag_AppearsForItsOwnChapter()
        {
            // Conversely, scanning chapter 5 surfaces record 1's flag (with the
            // HAIKU type) — proving the scope filter keys on the record's chapter
            // field, not a blanket include/exclude.
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 5u);
                Assert.Contains(list, u =>
                    u.ID == HaikuOtherChapterFlag && u.DataType == FELintCore.Type.HAIKU);
            });
        }

        [Fact]
        public void Scan_HaikuAndBattleTalk_RespectDedupInvariant()
        {
            // The #1192 dedup invariant must still hold with the new sources wired:
            // no (flag id, source type) pair repeats across the whole list.
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                var seen = new HashSet<(uint, int)>();
                foreach (var u in list)
                    Assert.True(seen.Add((u.ID, (int)u.DataType)),
                        $"duplicate row for flag 0x{u.ID:X} type {u.DataType}");
            });
        }

        [Fact]
        public void Scan_ResultIsSortedByFlagId()
        {
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.NotEmpty(list);
                for (int i = 1; i < list.Count; i++)
                    Assert.True(list[i - 1].ID <= list[i].ID,
                        "results must be sorted ascending by flag id");
            });
        }

        [Fact]
        public void Scan_NeverYieldsFlagZero()
        {
            WithChapter(rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.DoesNotContain(list, u => u.ID == 0);
            });
        }

        [Fact]
        public void Scan_NullRom_ReturnsEmpty()
        {
            var list = UseFlagScanCore.Scan(null, 0u);
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Fact]
        public void Scan_EmptyChapter_ReturnsEmptyWithoutThrowing()
        {
            // A bare FE8 ROM with no map/event data: every sub-scan must bail
            // safely and the merged list is empty (not a throw).
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            var prevComment = CoreState.CommentCache;
            try
            {
                var rom = new ROM();
                rom.LoadLow("empty-fe8u.gba", new byte[RomSize], "BE8E01");
                CoreState.ROM = rom;
                CoreState.EventScript = BuildEventScript(FlagCommand(), TermCommand());
                CoreState.CommentCache = new HeadlessEtcCache();

                var ex = Record.Exception(() =>
                {
                    var list = UseFlagScanCore.Scan(rom, 0u);
                    Assert.Empty(list);
                });
                Assert.Null(ex);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
                CoreState.CommentCache = prevComment;
            }
        }

        [Fact]
        public void MakeFlagIDArray_MapChange_FindsRecordFlag()
        {
            // Direct unit test of the MapChangeCore.MakeFlagIDArray seam in
            // isolation (no event-script env needed).
            WithChapter(rom =>
            {
                var list = new List<UseFlagIDCore>();
                MapChangeCore.MakeFlagIDArray(rom, 0u, list);
                Assert.Contains(list, u =>
                    u.ID == ChangeFlag && u.DataType == FELintCore.Type.MAPCHANGE);
            });
        }

        // =================================================================
        // Real-ROM regression test for the PR #1254 duplicate-row bug
        // (skipped when the ROM is absent — e.g. CI without roms/).
        // =================================================================

        /// <summary>
        /// PR #1254 regression guard on the REAL FE8U chapter 0: the per-tree dedup
        /// (d23ab511e) listed flag 0x07 ~8 times (one per cond-slot event tree).
        /// With chapter-level (flag id, source type) dedup, NO flag may repeat
        /// within a source type, and the row count is well below the per-tree
        /// inflated total. Asserts the invariant + that the list is non-trivial.
        /// </summary>
        [Fact]
        public void RealRom_FE8U_Chapter0_NoDuplicateFlagPerType()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip when ROM absent

            WithRealRomEnv(romPath, rom =>
            {
                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.NotEmpty(list); // chapter 0 uses flags — non-stub proof

                // The core invariant: one row per (flag id, source type).
                var seen = new HashSet<(uint, int)>();
                foreach (var u in list)
                    Assert.True(seen.Add((u.ID, (int)u.DataType)),
                        $"duplicate row for flag 0x{u.ID:X} type {u.DataType} " +
                        "(PR #1254 regression)");
            });
        }

        static void WithRealRomEnv(string romPath, System.Action<ROM> body)
        {
            var savedRom = CoreState.ROM;
            var savedEs = CoreState.EventScript;
            var savedEnc = CoreState.SystemTextEncoder;
            var savedComment = CoreState.CommentCache;
            var savedBaseDir = CoreState.BaseDirectory;
            try
            {
                string asmDir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                CoreState.BaseDirectory = asmDir;

                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                if (CoreState.CommentCache == null)
                    CoreState.CommentCache = new HeadlessEtcCache();

                var es = new EventScript();
                es.Load(EventScript.EventScriptType.Event);
                CoreState.EventScript = es;

                body(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.EventScript = savedEs;
                CoreState.SystemTextEncoder = savedEnc;
                CoreState.CommentCache = savedComment;
                if (savedBaseDir != null)
                    CoreState.BaseDirectory = savedBaseDir;
            }
        }

        // =================================================================
        // #1256 — FE6 / FE7-specific synthetic-ROM coverage.
        //
        // The FE8-only fixture above never exercises the FE6/FE7 BattleTalk
        // u16==0||0xFFFF terminator nor the FE7 tutorial-Haiku tables, which is
        // how the wrong (0xFFFF-only) terminator slipped through. These build a
        // minimal versioned ROM with ONLY the relevant table wired — the
        // EventCond / EventScript / MapChange sub-scans bail safely on the
        // un-wired ROM (proven by Scan_EmptyChapter_*), so the returned list
        // isolates the haiku/battletalk rows.
        // =================================================================

        // Minimal versioned ROM (same sizing as EventCondCoreTests.MakeMinimalRom).
        static ROM MakeVersionedRom(int version)
        {
            (string vs, int ms) = version switch
            {
                6 => ("AFEJ01", 0x800000),
                7 => ("AE7E01", 0x1000000),
                _ => ("BE8E01", 0x1000000),
            };
            var rom = new ROM();
            rom.LoadLow("useflag-synth.gba", new byte[ms], vs);
            return rom;
        }

        // Run body with CoreState.ROM set to a freshly built versioned ROM, then
        // restore. EventScript/CommentCache stay null so AppendEventScriptFlags
        // bails (it requires CoreState.EventScript) — the scan reduces to the
        // table-driven HAIKU/BATTTLE_TALK rows under test.
        static void WithVersionedRom(int version, System.Action<ROM> body)
        {
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            var prevComment = CoreState.CommentCache;
            try
            {
                var rom = MakeVersionedRom(version);
                Assert.Equal(version, rom.RomInfo.version); // guard: the right layout loaded
                CoreState.ROM = rom;
                CoreState.EventScript = null;
                CoreState.CommentCache = null;
                body(rom);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
                CoreState.CommentCache = prevComment;
            }
        }

        // Helper: point a RomInfo pointer field at baseOffset (as a GBA 0x08-pointer).
        static void WirePointer(ROM rom, uint pointerField, uint baseOffset)
            => WriteU32(rom, pointerField, baseOffset | 0x08000000u);

        [Theory]
        [InlineData(7)] // EventBattleTalkFE7Form.Init main: bs16, flag@+12, ch@+2, unit u16@+0
        [InlineData(6)] // EventBattleTalkFE6Form.Init main: bs12, flag@+8,  ch@+2, unit u16@+0
        public void Scan_BattleTalk_StopsOnU16ZeroTerminator(int version)
        {
            // #1256: the FE6/FE7 BattleTalk table terminates on unit u16 == 0
            // (NOT only 0xFFFF). A record placed AFTER a u16==0 terminator must
            // NOT be collected — with the old 0xFFFF-only terminator the walk
            // over-read past the zero and harvested its garbage flag.
            uint blockSize = (uint)(version == 7 ? 16 : 12);
            uint flagOffset = (uint)(version == 7 ? 12 : 8);
            const uint BaseOffset = 0x00300000u;
            const uint BeforeFlag = 0x0071; // live record's flag (must be collected)
            const uint AfterFlag  = 0x0072; // post-terminator flag (must NOT be collected)

            WithVersionedRom(version, rom =>
            {
                WirePointer(rom, rom.RomInfo.event_ballte_talk_pointer, BaseOffset);

                // Record 0 (live): unit u16 != 0, chapter byte @ +2 = 0, flag set.
                rom.write_u16(BaseOffset + 0, 0x0101);
                rom.Data[BaseOffset + 2] = 0x00; // chapter = 0 (this chapter)
                rom.write_u16(BaseOffset + flagOffset, (ushort)BeforeFlag);

                // Record 1: unit u16 == 0 → TERMINATES the table.
                rom.write_u16(BaseOffset + blockSize + 0, 0x0000);

                // Record 2 (AFTER terminator): a fully-formed record whose flag must
                // never be reached.
                uint rec2 = BaseOffset + 2 * blockSize;
                rom.write_u16(rec2 + 0, 0x0202);
                rom.Data[rec2 + 2] = 0x00;
                rom.write_u16(rec2 + flagOffset, (ushort)AfterFlag);

                var list = UseFlagScanCore.Scan(rom, 0u);

                Assert.Contains(list, u =>
                    u.ID == BeforeFlag && u.DataType == FELintCore.Type.BATTTLE_TALK);
                Assert.DoesNotContain(list, u => u.ID == AfterFlag);
            });
        }

        [Fact]
        public void Scan_FE6BattleTalk_SecondaryTable_StopsOnU16ZeroTerminator()
        {
            // FE6 EventBattleTalkFE6Form.N_Init secondary table: bs16, flag@+8,
            // ch@+1, unit u16@+0 — also terminates on u16==0||0xFFFF (#1256).
            const uint Base2 = 0x00320000u;
            const uint BeforeFlag = 0x0081;
            const uint AfterFlag  = 0x0082;

            WithVersionedRom(6, rom =>
            {
                WirePointer(rom, rom.RomInfo.event_ballte_talk2_pointer, Base2);

                // Record 0 (live): chapter @ +1 = 0, flag @ +8.
                rom.write_u16(Base2 + 0, 0x0303);
                rom.Data[Base2 + 1] = 0x00;
                rom.write_u16(Base2 + 8, (ushort)BeforeFlag);
                // Record 1: u16 == 0 terminates.
                rom.write_u16(Base2 + 16, 0x0000);
                // Record 2 (after terminator): must not be collected.
                rom.write_u16(Base2 + 32, 0x0404);
                rom.Data[Base2 + 32 + 1] = 0x00;
                rom.write_u16(Base2 + 32 + 8, (ushort)AfterFlag);

                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.Contains(list, u =>
                    u.ID == BeforeFlag && u.DataType == FELintCore.Type.BATTTLE_TALK);
                Assert.DoesNotContain(list, u => u.ID == AfterFlag);
            });
        }

        [Fact]
        public void Scan_FE7TutorialHaiku_FlagIsCollectedAndStopsOnU8Zero()
        {
            // FE7 EventHaikuFE7Form.N1_Init tutorial table (event_haiku_tutorial_1):
            // bs12, flag@+8, ch@+1, unit u8@+0 — terminates on u8==0. Proves the
            // FE7-only tutorial-Haiku path (never reached by the FE8 fixture).
            const uint TutBase = 0x00340000u;
            const uint TutFlag   = 0x0091; // live tutorial-haiku flag
            const uint AfterFlag = 0x0092; // post-terminator flag

            WithVersionedRom(7, rom =>
            {
                WirePointer(rom, rom.RomInfo.event_haiku_tutorial_1_pointer, TutBase);

                // Record 0 (live): unit u8 != 0, chapter @ +1 = 0, flag @ +8.
                rom.Data[TutBase + 0] = 0x01;
                rom.Data[TutBase + 1] = 0x00; // chapter = 0
                rom.write_u16(TutBase + 8, (ushort)TutFlag);
                // Record 1: unit u8 == 0 → terminates.
                rom.Data[TutBase + 12] = 0x00;
                // Record 2 (after terminator): must not be collected.
                rom.Data[TutBase + 24 + 0] = 0x02;
                rom.Data[TutBase + 24 + 1] = 0x00;
                rom.write_u16(TutBase + 24 + 8, (ushort)AfterFlag);

                var list = UseFlagScanCore.Scan(rom, 0u);
                Assert.Contains(list, u =>
                    u.ID == TutFlag && u.DataType == FELintCore.Type.HAIKU);
                Assert.DoesNotContain(list, u => u.ID == AfterFlag);
            });
        }

        static string FindRom(string romName)
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dir = System.IO.Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = System.IO.Path.Combine(dir, "roms", romName);
                    if (System.IO.File.Exists(path)) return path;
                    break;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
