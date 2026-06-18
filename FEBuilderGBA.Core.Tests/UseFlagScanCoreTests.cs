// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for UseFlagScanCore (#1192) — the strictly READ-ONLY per-chapter
// flag-usage aggregator that backs the Avalonia "Flags-Used-in-Chapter" tool.
//
// SYNTHETIC scratch-ROM tests build a minimal FE8 chapter and assert the three
// in-scope flag sources are each found:
//   (1) EVENT_COND_*  — the flag field (u16 @ +2) of a Turn condition record,
//   (2) EVENTSCRIPT   — an ArgType.FLAG arg in the event script that the Turn
//                       record's event pointer reaches, and
//   (3) MAPCHANGE     — the flag field (u16 @ +5) of a map-change record.
// Plus guard tests: id 0 is dropped, a foreign/empty ROM yields an empty list,
// and the result is sorted by flag id.
//
// DEFERRED (#1253): the Haiku/BattleTalk scanners are NOT part of this slice.

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
        // eventcond_tern_size=12, cond slot 0 = Turn.
        const uint MapTableBase   = 0x00700000u;
        const uint EventTableBase = 0x00710000u;
        const uint CondBlock      = 0x00720000u;
        const uint TurnRecord     = 0x00730000u;
        const uint EventScriptAddr= 0x00740000u;
        const uint ChangeTable    = 0x00750000u;
        const uint ChangeData     = 0x00760000u;

        const uint CondFlag   = 0x0011; // Turn record flag (u16 @ +2)
        const uint ScriptFlag = 0x0022; // event-script FLAG arg
        const uint ChangeFlag = 0x0033; // map-change record flag (u16 @ +5)

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

                // 4) Event script: SETFLAG ScriptFlag ; ENDA.
                rom.write_u16(EventScriptAddr + 0, 0x0001);
                rom.write_u16(EventScriptAddr + 2, (ushort)ScriptFlag);
                WriteU32(rom, EventScriptAddr + 4, 0x0000000A);

                // 5) Map-change PLIST table: entry[3] → change data block.
                WriteU32(rom, rom.RomInfo.map_mapchange_pointer, ChangeTable | 0x08000000u);
                WriteU32(rom, ChangeTable + 3 * 4, ChangeData | 0x08000000u);
                // 6) Change data: one 12-byte record (first byte != 0xFF),
                //    flag @ +5; next record's first byte = 0xFF → terminates.
                rom.Data[ChangeData + 0] = 0x01;
                rom.write_u16(ChangeData + 5, (ushort)ChangeFlag);
                rom.Data[ChangeData + 12] = 0xFF;

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
    }
}
