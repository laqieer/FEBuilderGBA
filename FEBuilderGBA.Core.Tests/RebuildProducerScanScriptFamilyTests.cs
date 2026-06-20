// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice 2aa — the four FE8 ScanScript-family
// data-path forms, each a verbatim port of its WinForms MakeAllDataLength:
//   MonsterWMapProbabilityForm.cs:156  -> EmitMonsterWMapProbability
//   EventBattleTalkForm.cs:95          -> EmitEventBattleTalk
//   WorldMapEventPointerForm.cs:247    -> EmitWorldMapEventPointer
//   EventHaikuForm.cs:99               -> EmitEventHaiku
//
// Each reuses the slice-2u EmitScanScript block-emitter for its per-entry /
// fixed event-script trace. The producer body gates their dispatch on
// IsEventScriptDisasmReady and re-reports them in NotYetPorted when unwired —
// these tests wire CoreState.EventScript/CommentCache so the full path runs.
//
// Coverage per form: a valid main IFR table emits the right addr/length/pointer/
// type; the per-entry ScanScript trace emits the referenced-script block; near-
// EOF doesn't throw; a zeroed ROM emits nothing.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerScanScriptFamilyTests
    {
        const int RomSize = 0x1100000; // 17MB — FE8 LoadLow minimum + scratch.

        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("synthetic-fe8u.gba", new byte[RomSize], "BE8E01");
            return rom;
        }

        static EventScript BuildEventScript(params EventScript.Script[] scripts)
        {
            var es = new EventScript();
            var prop = typeof(EventScript).GetProperty("Scripts");
            prop!.SetValue(es, scripts);
            return es;
        }

        static EventScript.Script TermCommand()
            => EventScript.ParseScriptLine("0A000000\tENDA [TERM]");

        static void Write32(ROM rom, uint addr, uint value) => rom.write_u32(addr, value);
        static uint Ptr(uint offset) => offset | 0x08000000u;

        /// <summary>Run with CoreState wired to a fresh FE8U ROM + a single-ENDA
        /// EventScript (so EmitScanScript's disasm prerequisite is satisfied),
        /// restoring prior CoreState afterwards. Also clears the InputFormRef-style
        /// data-count cache is NOT needed (Core producer never uses it).</summary>
        static void WithEnv(Action<ROM> body)
        {
            var rom = MakeFe8uRom();
            var es = BuildEventScript(TermCommand());
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            var prevComment = CoreState.CommentCache;
            try
            {
                CoreState.ROM = rom;
                CoreState.EventScript = es;
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

        /// <summary>Plant a one-command ENDA event script behind the pointer slot,
        /// returning the event addr.</summary>
        static uint PlantScript(ROM rom, uint pointerSlot, uint eventAddr)
        {
            Write32(rom, pointerSlot, Ptr(eventAddr));
            rom.write_u32(eventAddr + 0, 0x0000000A); // ENDA
            return eventAddr;
        }

        // =================================================================
        // EmitMonsterWMapProbability
        // =================================================================

        [Fact]
        public void EmitMonsterWMapProbability_EmitsFiveTablesAndSkirmishScripts()
        {
            WithEnv(rom =>
            {
                var ri = rom.RomInfo;
                // Five flat IFR tables. base-point rule i<0x9 (block 1): give it a few
                // rows then it stops at 0x9 anyway. The other four use rule i<0xB.
                uint basePoint = 0x00900000u;
                uint stage1 = 0x00901000u, stage2 = 0x00902000u;
                uint prob1 = 0x00903000u, prob2 = 0x00904000u;
                Write32(rom, ri.monster_wmap_base_point_pointer, Ptr(basePoint));
                Write32(rom, ri.monster_wmap_stage_1_pointer, Ptr(stage1));
                Write32(rom, ri.monster_wmap_stage_2_pointer, Ptr(stage2));
                Write32(rom, ri.monster_wmap_probability_1_pointer, Ptr(prob1));
                Write32(rom, ri.monster_wmap_probability_2_pointer, Ptr(prob2));

                // Two skirmish events.
                uint startEvt = PlantScript(rom, ri.worldmap_skirmish_startevent_pointer, 0x00910000u);
                uint endEvt = PlantScript(rom, ri.worldmap_skirmish_endevent_pointer, 0x00911000u);

                var list = new List<Address>();
                RebuildProducerCore.EmitMonsterWMapProbability(rom, list);

                // base-point table: block 1, rule i<0x9 => count 9, length = 1*(9+1)=10.
                Assert.Contains(list, a => a.Addr == basePoint && a.Info == "MonsterWMapProbability"
                    && a.BlockSize == 1 && a.Length == 10 && a.Pointer == ri.monster_wmap_base_point_pointer);
                // stage tables: block 1, rule i<0xB => count 11, length = 12.
                Assert.Contains(list, a => a.Addr == stage1 && a.Info == "MonsterWMapStageEirika"
                    && a.BlockSize == 1 && a.Length == 12);
                Assert.Contains(list, a => a.Addr == stage2 && a.Info == "MonsterWMapStageEphraim"
                    && a.BlockSize == 1 && a.Length == 12);
                // probability tables: block 9, rule i<0xB => count 11, length = 9*12=108.
                Assert.Contains(list, a => a.Addr == prob1 && a.Info == "MonsterWMapProbabilityEirika"
                    && a.BlockSize == 9 && a.Length == 9u * 12u);
                Assert.Contains(list, a => a.Addr == prob2 && a.Info == "MonsterWMapProbabilityEphraim"
                    && a.BlockSize == 9 && a.Length == 9u * 12u);

                // Both skirmish scripts traced.
                Assert.Contains(list, a => a.Addr == startEvt && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                Assert.Contains(list, a => a.Addr == endEvt && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
            });
        }

        [Fact]
        public void EmitMonsterWMapProbability_ZeroedRom_EmitsNothing()
        {
            WithEnv(rom =>
            {
                // RomInfo pointer slots all read 0 in a zeroed ROM -> nothing resolves.
                var list = new List<Address>();
                RebuildProducerCore.EmitMonsterWMapProbability(rom, list);
                Assert.Empty(list);
            });
        }

        // =================================================================
        // EmitEventBattleTalk
        // =================================================================

        [Fact]
        public void EmitEventBattleTalk_EmitsMainIfrAndPerEntryScript()
        {
            WithEnv(rom =>
            {
                var ri = rom.RomInfo;
                uint tableBase = 0x00920000u;     // block 16, PI {12}
                Write32(rom, ri.event_ballte_talk_pointer, Ptr(tableBase));

                // Row 0: unit id 0x10, special-event pointer at +12 -> a script.
                rom.write_u8(tableBase + 0, 0x10);
                uint script = 0x00921000u;
                Write32(rom, tableBase + 12, Ptr(script));
                rom.write_u32(script + 0, 0x0000000A); // ENDA
                // Row 1: terminator (u16(addr)==0xFFFF stops the IFR walk).
                rom.write_u16(tableBase + 16, 0xFFFF);

                var list = new List<Address>();
                RebuildProducerCore.EmitEventBattleTalk(rom, list);

                // Main IFR: block 16, count 1 (row 0 only), length = 16*(1+1)=32, PI {12}.
                var ifr = list.Single(a => a.Addr == tableBase && a.Info == "EventBattleTalkForm");
                Assert.Equal(16u, ifr.BlockSize);
                Assert.Equal(16u * 2u, ifr.Length);
                Assert.Equal(ri.event_ballte_talk_pointer, ifr.Pointer);

                // Per-entry traced script.
                Assert.Contains(list, a => a.Addr == script && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
            });
        }

        [Fact]
        public void EmitEventBattleTalk_EntryWithUnsafeEventPointer_SkipsTrace()
        {
            WithEnv(rom =>
            {
                var ri = rom.RomInfo;
                uint tableBase = 0x00920000u;
                Write32(rom, ri.event_ballte_talk_pointer, Ptr(tableBase));
                rom.write_u8(tableBase + 0, 0x10);
                Write32(rom, tableBase + 12, 0x00000000u); // NULL event pointer -> not isSafetyPointer
                rom.write_u16(tableBase + 16, 0xFFFF);     // terminator

                var list = new List<Address>();
                RebuildProducerCore.EmitEventBattleTalk(rom, list);

                // Main IFR still emitted; no EVENTSCRIPT block.
                Assert.Contains(list, a => a.Addr == tableBase && a.Info == "EventBattleTalkForm");
                Assert.DoesNotContain(list, a => a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
            });
        }

        // =================================================================
        // EmitEventHaiku
        // =================================================================

        [Fact]
        public void EmitEventHaiku_EmitsMainIfrAndPerEntryScript()
        {
            WithEnv(rom =>
            {
                var ri = rom.RomInfo;
                uint tableBase = 0x00930000u;     // block 12, PI {8}
                Write32(rom, ri.event_haiku_pointer, Ptr(tableBase));

                rom.write_u8(tableBase + 0, 0x10); // unit id
                uint script = 0x00931000u;
                Write32(rom, tableBase + 8, Ptr(script));  // event pointer at +8
                rom.write_u32(script + 0, 0x0000000A);     // ENDA
                rom.write_u16(tableBase + 12, 0xFFFF);     // terminator

                var list = new List<Address>();
                RebuildProducerCore.EmitEventHaiku(rom, list);

                var ifr = list.Single(a => a.Addr == tableBase && a.Info == "Haiku");
                Assert.Equal(12u, ifr.BlockSize);
                Assert.Equal(12u * 2u, ifr.Length);   // count 1 -> 12*(1+1)
                Assert.Equal(ri.event_haiku_pointer, ifr.Pointer);

                Assert.Contains(list, a => a.Addr == script && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
            });
        }

        // =================================================================
        // EmitWorldMapEventPointer
        // =================================================================

        [Fact]
        public void EmitWorldMapEventPointer_EmitsTwoTablesPerEntryAndThreeFixedEvents()
        {
            WithEnv(rom =>
            {
                var ri = rom.RomInfo;
                // "Before" table: block 4, rule i==0?true:isPointer(u32). Row 0 = a pointer
                // to a script; row 1 = a non-pointer -> walk stops at count 1.
                uint beforeBase = 0x00940000u;
                Write32(rom, ri.worldmap_event_on_stageclear_pointer, Ptr(beforeBase));
                uint beforeScript = 0x00941000u;
                Write32(rom, beforeBase + 0, Ptr(beforeScript));
                rom.write_u32(beforeScript + 0, 0x0000000A); // ENDA
                Write32(rom, beforeBase + 4, 0x00000000u);   // non-pointer -> stop

                // "After" table.
                uint afterBase = 0x00942000u;
                Write32(rom, ri.worldmap_event_on_stageselect_pointer, Ptr(afterBase));
                uint afterScript = 0x00943000u;
                Write32(rom, afterBase + 0, Ptr(afterScript));
                rom.write_u32(afterScript + 0, 0x0000000A);
                Write32(rom, afterBase + 4, 0x00000000u);

                // Three fixed events.
                uint opScript = PlantScript(rom, ri.oping_event_pointer, 0x00944000u);
                uint ed1Script = PlantScript(rom, ri.ending1_event_pointer, 0x00945000u);
                uint ed2Script = PlantScript(rom, ri.ending2_event_pointer, 0x00946000u);

                var list = new List<Address>();
                RebuildProducerCore.EmitWorldMapEventPointer(rom, list);

                // Both IFR tables present (block 4, PI {0}, count 1 -> length 8).
                Assert.Contains(list, a => a.Addr == beforeBase && a.Info == "WorldMapEvent Before"
                    && a.BlockSize == 4 && a.Length == 8 && a.Pointer == ri.worldmap_event_on_stageclear_pointer);
                Assert.Contains(list, a => a.Addr == afterBase && a.Info == "WorldMapEvent After"
                    && a.BlockSize == 4 && a.Length == 8 && a.Pointer == ri.worldmap_event_on_stageselect_pointer);

                // Per-entry traced scripts.
                Assert.Contains(list, a => a.Addr == beforeScript && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                Assert.Contains(list, a => a.Addr == afterScript && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                // Three fixed events traced.
                Assert.Contains(list, a => a.Addr == opScript && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                Assert.Contains(list, a => a.Addr == ed1Script && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                Assert.Contains(list, a => a.Addr == ed2Script && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
            });
        }

        [Fact]
        public void EmitWorldMapEventPointer_ZeroedRom_EmitsNothing()
        {
            WithEnv(rom =>
            {
                var list = new List<Address>();
                RebuildProducerCore.EmitWorldMapEventPointer(rom, list);
                Assert.Empty(list);
            });
        }

        // =================================================================
        // Near-EOF safety (all four)
        // =================================================================

        [Fact]
        public void ScanScriptFamily_NearEof_DoesNotThrow()
        {
            WithEnv(rom =>
            {
                var ri = rom.RomInfo;
                uint nearEof = (uint)rom.Data.Length - 4;
                // Point every table/event slot at the very last 4 bytes.
                foreach (uint slot in new[]
                {
                    ri.monster_wmap_base_point_pointer, ri.monster_wmap_stage_1_pointer,
                    ri.monster_wmap_stage_2_pointer, ri.monster_wmap_probability_1_pointer,
                    ri.monster_wmap_probability_2_pointer, ri.worldmap_skirmish_startevent_pointer,
                    ri.worldmap_skirmish_endevent_pointer, ri.event_ballte_talk_pointer,
                    ri.event_haiku_pointer, ri.worldmap_event_on_stageclear_pointer,
                    ri.worldmap_event_on_stageselect_pointer, ri.oping_event_pointer,
                    ri.ending1_event_pointer, ri.ending2_event_pointer,
                })
                {
                    Write32(rom, slot, Ptr(nearEof));
                }

                var list = new List<Address>();
                Exception ex = Record.Exception(() =>
                {
                    RebuildProducerCore.EmitMonsterWMapProbability(rom, list);
                    RebuildProducerCore.EmitEventBattleTalk(rom, list);
                    RebuildProducerCore.EmitWorldMapEventPointer(rom, list);
                    RebuildProducerCore.EmitEventHaiku(rom, list);
                });
                Assert.Null(ex);
            });
        }

        // =================================================================
        // Producer-body gating: disasm unwired -> all four re-reported.
        // =================================================================

        [Fact]
        public void MakeAllStructPointers_FE8_DisasmUnwired_ReReportsAllFour()
        {
            var rom = MakeFe8uRom();
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            var prevComment = CoreState.CommentCache;
            try
            {
                CoreState.ROM = rom;
                CoreState.EventScript = null;   // disasm NOT wired
                CoreState.CommentCache = null;
                var result = RebuildProducerCore.MakeAllStructPointers(rom);
                // The four ScanScript-family forms (and EventCondForm) are re-reported because
                // their script trace would otherwise be silently dropped.
                Assert.Contains("MonsterWMapProbabilityForm", result.NotYetPorted);
                Assert.Contains("EventBattleTalkForm", result.NotYetPorted);
                Assert.Contains("WorldMapEventPointerForm", result.NotYetPorted);
                Assert.Contains("EventHaikuForm", result.NotYetPorted);
                Assert.False(result.IsComplete);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
                CoreState.CommentCache = prevComment;
            }
        }
    }
}
