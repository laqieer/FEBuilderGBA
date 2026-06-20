// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for RebuildProducerCore slice 2u — EventCondForm + the Core ScanScript
// BLOCK-emitter (EmitEventCond / EmitScanScript), a verbatim port of
// EventScriptForm.ScanScript + EventCondForm.MakeAllDataLength.
//
// Two layers (mirroring EventScriptReferenceScannerTests):
//   1. EmitScanScript synthetic tests — plant a small event script in a scratch
//      region of a wired FE8U ROM and assert the emitted Address set (the
//      EVENTSCRIPT block + the per-arg RecycleOldData blocks), the POINTER_EVENT
//      recursion, the self-cycle alias, and near-EOF safety.
//   2. EmitEventCond integration test — plant the full map-setting -> event-plist
//      -> cond-block -> event-script chain (the MapEventUnitCoreNewAllocTests
//      recipe) and assert the EVENTCOND_*/EVENTTRAP frame + the EventCond Frame
//      block + a traced event-script block.
//   3. NotYetPorted coverage — EventCondForm is dropped, ScanScript siblings stay.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class RebuildProducerEventCondTests
    {
        const int RomSize = 0x1100000; // 17MB — FE8 LoadLow minimum + scratch.
        const uint Scratch = 0x00900000u;

        static ROM MakeFe8uRom()
        {
            var rom = new ROM();
            rom.LoadLow("synthetic-fe8u.gba", new byte[RomSize], "BE8E01");
            return rom;
        }

        static ROM MakeVersionedRom(string versionString, int size = 0x0200_0000)
        {
            var rom = new ROM();
            bool ok = rom.LoadLow("fake.gba", new byte[size], versionString);
            Assert.True(ok, "LoadLow did not recognize version string: " + versionString);
            return rom;
        }

        static EventScript BuildEventScript(params EventScript.Script[] scripts)
        {
            var es = new EventScript();
            var prop = typeof(EventScript).GetProperty("Scripts");
            prop!.SetValue(es, scripts);
            return es;
        }

        static void Write32(ROM rom, uint addr, uint value) => rom.write_u32(addr, value);
        static uint Ptr(uint offset) => offset | 0x08000000u;

        /// <summary>Run an action with CoreState wired to a fresh FE8U ROM + the given
        /// EventScript, restoring prior CoreState afterwards.</summary>
        static void WithEnv(EventScript es, Action<ROM> body) => WithEnv(MakeFe8uRom(), es, body);

        static void WithEnv(ROM rom, EventScript es, Action<ROM> body)
        {
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

        // ---- script-line factories (real FE schema opcodes don't matter for the
        //      disassembler — only the arg layout the test plants does) ----
        static EventScript.Script TermCommand()
            => EventScript.ParseScriptLine("0A000000\tENDA [TERM]");
        static EventScript.Script CallCommand()  // 4-byte POINTER_EVENT at offset 4
            => EventScript.ParseScriptLine("03000000XXXXXXXX\tCALL [X:POINTER_EVENT:Target]");
        static EventScript.Script LoadUnitCommand() // 4-byte POINTER_UNIT at offset 4
            => EventScript.ParseScriptLine("12000000XXXXXXXX\tLOADUNIT [XXXX:POINTER_UNIT:Units]");
        static EventScript.Script AiCoordCommand() // 4-byte POINTER_AICOORDINATE at offset 4
            => EventScript.ParseScriptLine("40000000XXXXXXXX\tAICOORD [XXXX:POINTER_AICOORDINATE:Coord]");

        // =================================================================
        // EmitScanScript — the block-emitter
        // =================================================================

        [Fact]
        public void EmitScanScript_SingleTermEvent_EmitsOneEventScriptBlock()
        {
            var es = BuildEventScript(TermCommand());
            WithEnv(es, rom =>
            {
                // Slot at Scratch holds a pointer to the event at Scratch+0x100.
                uint eventAddr = Scratch + 0x100;
                Write32(rom, Scratch, Ptr(eventAddr));
                rom.write_u32(eventAddr + 0, 0x0000000A); // ENDA

                var list = new List<Address>();
                var trace = new List<uint>();
                RebuildProducerCore.EmitScanScript(rom, list, Scratch, true, false, "Evt", trace);

                var evt = Assert.Single(list);
                Assert.Equal(eventAddr, evt.Addr);
                Assert.Equal(Address.DataTypeEnum.EVENTSCRIPT, evt.DataType);
                Assert.Equal(Scratch, evt.Pointer);     // the SLOT, not the target
                Assert.Equal(4u, evt.Length);           // one 4-byte ENDA command
                Assert.Contains(eventAddr, trace);
            });
        }

        [Fact]
        public void EmitScanScript_PointerEventArg_RecursesAndEmitsNested()
        {
            var es = BuildEventScript(CallCommand(), TermCommand());
            WithEnv(es, rom =>
            {
                uint eventAddr = Scratch + 0x100;
                uint nested = Scratch + 0x200;
                Write32(rom, Scratch, Ptr(eventAddr));

                // CALL <nested> ; ENDA
                rom.write_u32(eventAddr + 0, 0x00000003);
                Write32(rom, eventAddr + 4, Ptr(nested));
                rom.write_u32(eventAddr + 8, 0x0000000A);

                // nested: ENDA
                rom.write_u32(nested + 0, 0x0000000A);

                var list = new List<Address>();
                RebuildProducerCore.EmitScanScript(rom, list, Scratch, true, false, "Evt", new List<uint>());

                // Two EVENTSCRIPT blocks: the outer (addr=eventAddr) and the nested (addr=nested).
                Assert.Contains(list, a => a.Addr == eventAddr && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                Assert.Contains(list, a => a.Addr == nested && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                // The nested block's slot pointer is the CALL arg field (eventAddr + 4).
                var nestedBlock = list.Single(a => a.Addr == nested);
                Assert.Equal(eventAddr + 4, nestedBlock.Pointer);
            });
        }

        [Fact]
        public void EmitScanScript_SelfCycle_Terminates_AndEmitsAliasPointer()
        {
            // A CALL whose POINTER_EVENT arg points back to the event start: the
            // tracelist guard must stop the recursion and emit a zero-length alias.
            var es = BuildEventScript(CallCommand(), TermCommand());
            WithEnv(es, rom =>
            {
                uint eventAddr = Scratch + 0x100;
                Write32(rom, Scratch, Ptr(eventAddr));
                // CALL <eventAddr> (self) ; ENDA
                rom.write_u32(eventAddr + 0, 0x00000003);
                Write32(rom, eventAddr + 4, Ptr(eventAddr));
                rom.write_u32(eventAddr + 8, 0x0000000A);

                var list = new List<Address>();
                Exception ex = Record.Exception(() =>
                    RebuildProducerCore.EmitScanScript(rom, list, Scratch, true, false, "Evt", new List<uint>()));
                Assert.Null(ex); // no hang / no throw

                // The self-reference emits a zero-length alias EVENTSCRIPT for the CALL arg slot.
                Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.EVENTSCRIPT
                                           && a.Pointer == eventAddr + 4 && a.Length == 0);
            });
        }

        [Fact]
        public void EmitScanScript_PointerUnitArg_EmitsRecycleOldUnitsBlock()
        {
            var es = BuildEventScript(LoadUnitCommand(), TermCommand());
            WithEnv(es, rom =>
            {
                uint eventAddr = Scratch + 0x100;
                uint unitList = Scratch + 0x300;
                Write32(rom, Scratch, Ptr(eventAddr));
                // LOADUNIT <unitList> ; ENDA
                rom.write_u32(eventAddr + 0, 0x00000012);
                Write32(rom, eventAddr + 4, Ptr(unitList));
                rom.write_u32(eventAddr + 8, 0x0000000A);
                // unit list: one non-terminator row then a terminator (u8==0).
                rom.Data[unitList + 0] = 0x10;

                var list = new List<Address>();
                RebuildProducerCore.EmitScanScript(rom, list, Scratch, true, false, "Evt", new List<uint>());

                // The EventUnit IFR block (EmitRecycleOldUnits) is emitted behind the LOADUNIT arg.
                Assert.Contains(list, a => a.Addr == unitList
                                           && a.DataType == Address.DataTypeEnum.InputFormRef);
            });
        }

        [Fact]
        public void EmitScanScript_AiCoordinateArg_EmitsFixed4ByteBinBlock()
        {
            var es = BuildEventScript(AiCoordCommand(), TermCommand());
            WithEnv(es, rom =>
            {
                uint eventAddr = Scratch + 0x100;
                uint coord = Scratch + 0x400;
                Write32(rom, Scratch, Ptr(eventAddr));
                rom.write_u32(eventAddr + 0, 0x00000040);
                Write32(rom, eventAddr + 4, Ptr(coord));
                rom.write_u32(eventAddr + 8, 0x0000000A);

                var list = new List<Address>();
                RebuildProducerCore.EmitScanScript(rom, list, Scratch, true, false, "Evt", new List<uint>());

                // AIASMCoordinateForm.RecycleOldData = AddPointer(4, BIN).
                Assert.Contains(list, a => a.Addr == coord && a.Length == 4
                                           && a.DataType == Address.DataTypeEnum.BIN);
            });
        }

        [Fact]
        public void EmitScanScript_IsWithEventUnitFalse_DoesNotDispatchSubData()
        {
            var es = BuildEventScript(LoadUnitCommand(), TermCommand());
            WithEnv(es, rom =>
            {
                uint eventAddr = Scratch + 0x100;
                uint unitList = Scratch + 0x300;
                Write32(rom, Scratch, Ptr(eventAddr));
                rom.write_u32(eventAddr + 0, 0x00000012);
                Write32(rom, eventAddr + 4, Ptr(unitList));
                rom.write_u32(eventAddr + 8, 0x0000000A);
                rom.Data[unitList + 0] = 0x10;

                var list = new List<Address>();
                // isWithEventUnit:false — the POINTER_UNIT dispatch is skipped (WF nop branch).
                RebuildProducerCore.EmitScanScript(rom, list, Scratch, false, false, "Evt", new List<uint>());

                Assert.DoesNotContain(list, a => a.Addr == unitList);
                Assert.Contains(list, a => a.Addr == eventAddr); // the script block is still emitted
            });
        }

        [Fact]
        public void EmitScanScript_NearEof_DoesNotThrow()
        {
            var es = BuildEventScript(TermCommand());
            WithEnv(es, rom =>
            {
                // Slot in the very last 4 bytes; pointer derefs to a near-EOF event.
                uint slot = (uint)rom.Data.Length - 4;
                uint eventAddr = (uint)rom.Data.Length - 4;
                Write32(rom, slot, Ptr(eventAddr));

                var list = new List<Address>();
                Exception ex = Record.Exception(() =>
                    RebuildProducerCore.EmitScanScript(rom, list, slot, true, false, "Evt", new List<uint>()));
                Assert.Null(ex);
            });
        }

        // =================================================================
        // EmitEventCond — the full producer form
        // =================================================================

        [Fact]
        public void EmitEventCond_ThrowsWhenDisasmNotWired()
        {
            var rom = MakeFe8uRom();
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            var prevComment = CoreState.CommentCache;
            try
            {
                CoreState.ROM = rom;
                CoreState.EventScript = null;     // not wired
                CoreState.CommentCache = null;
                var list = new List<Address>();
                Assert.Throws<InvalidOperationException>(() => RebuildProducerCore.EmitEventCond(rom, list));
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
                CoreState.CommentCache = prevComment;
            }
        }

        [Fact]
        public void EmitEventCond_FE8_EmitsCondFrameAndTracedScript()
        {
            // Plant a full chain: map setting -> event plist -> cond block -> a TALK
            // cond record whose event pointer leads to a one-command ENDA script.
            var es = BuildEventScript(TermCommand());
            WithEnv(es, rom =>
            {
                const uint mapId = 0;
                uint mapTableBase = 0x00800000u;
                uint mapSize = rom.RomInfo.map_setting_datasize;
                Write32(rom, rom.RomInfo.map_setting_pointer, Ptr(mapTableBase));
                uint mapRecord = mapTableBase + mapId * mapSize;
                Write32(rom, mapRecord + 0, 0x08123456u); // first dword => valid map row
                const byte eventPlist = 7;
                rom.Data[mapRecord + rom.RomInfo.map_setting_event_plist_pos] = eventPlist;

                uint eventTableBase = 0x00810000u;
                Write32(rom, rom.RomInfo.map_event_pointer, Ptr(eventTableBase));
                uint condBlock = 0x00820000u;
                Write32(rom, eventTableBase + eventPlist * 4u, Ptr(condBlock));

                // Cond slots: index 1 is TALK (FE8 layout). Plant one talk record:
                // type!=0 at +0, event pointer at +4, then a terminator record (all zero).
                var slots = MapEventUnitCore.GetCondSlots(rom);
                int talkIdx = slots.FindIndex(s => s.Type == MapEventUnitCore.CondType.Talk);
                Assert.True(talkIdx >= 0);

                uint talkTable = 0x00830000u;
                uint talkSize = rom.RomInfo.eventcond_talk_size;
                uint scriptAddr = 0x00840000u;
                rom.Data[talkTable + 0] = 0x02;                 // type != 0
                Write32(rom, talkTable + 4, Ptr(scriptAddr));   // event pointer
                // terminator record at talkTable + talkSize is left zero (u8==0 stops the walk).
                Write32(rom, condBlock + (uint)talkIdx * 4u, Ptr(talkTable));

                rom.write_u32(scriptAddr + 0, 0x0000000A);      // ENDA

                var list = new List<Address>();
                RebuildProducerCore.EmitEventCond(rom, list);

                // (1) The TALK cond frame block at the cond slot.
                Assert.Contains(list, a => a.Addr == talkTable
                                           && a.DataType == Address.DataTypeEnum.EVENTCOND_TALK);
                // (2) The traced event-script block.
                Assert.Contains(list, a => a.Addr == scriptAddr
                                           && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                // (3) The trailing EventCond Frame block: slots.Count * 4 bytes at the cond block.
                Assert.Contains(list, a => a.Addr == condBlock
                                           && a.Length == (uint)slots.Count * 4u
                                           && a.DataType == Address.DataTypeEnum.POINTER);
            });
        }

        [Fact]
        public void EmitEventCond_FE8_TutorialSlot_EmitsPointerTypeFrame()
        {
            var es = BuildEventScript(TermCommand());
            WithEnv(es, rom =>
            {
                const uint mapId = 0;
                uint mapTableBase = 0x00800000u;
                uint mapSize = rom.RomInfo.map_setting_datasize;
                Write32(rom, rom.RomInfo.map_setting_pointer, Ptr(mapTableBase));
                uint mapRecord = mapTableBase + mapId * mapSize;
                Write32(rom, mapRecord + 0, 0x08123456u);
                const byte eventPlist = 7;
                rom.Data[mapRecord + rom.RomInfo.map_setting_event_plist_pos] = eventPlist;

                uint eventTableBase = 0x00810000u;
                Write32(rom, rom.RomInfo.map_event_pointer, Ptr(eventTableBase));
                uint condBlock = 0x00820000u;
                Write32(rom, eventTableBase + eventPlist * 4u, Ptr(condBlock));

                var slots = MapEventUnitCore.GetCondSlots(rom);
                int tutorialIdx = slots.FindIndex(s => s.Type == MapEventUnitCore.CondType.Tutorial);
                Assert.True(tutorialIdx >= 0, "FE8 must have a Tutorial slot");

                uint tutTable = 0x00860000u;
                // NOTE: the tutorial walk uses BOTH u8(base)==0 as the terminator AND
                // p32(base+0) as the event pointer (same field). A pointer whose LOW byte
                // is 0 would falsely terminate the walk — real tutorial pointers never
                // have a zero low byte, so plant one with a non-zero low byte (+4).
                uint scriptAddr = 0x00870004u;
                // Tutorial: 4-byte records, ptr at +0, stop when u8==0.
                Write32(rom, tutTable + 0, Ptr(scriptAddr));
                // tutTable + 4 left zero -> terminator.
                Write32(rom, condBlock + (uint)tutorialIdx * 4u, Ptr(tutTable));
                rom.write_u32(scriptAddr + 0, 0x0000000A);

                var list = new List<Address>();
                RebuildProducerCore.EmitEventCond(rom, list);

                // The tutorial frame is emitted as POINTER (not EVENTCOND_*).
                Assert.Contains(list, a => a.Addr == tutTable
                                           && a.DataType == Address.DataTypeEnum.POINTER);
                Assert.Contains(list, a => a.Addr == scriptAddr
                                           && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
            });
        }

        [Fact]
        public void EmitEventCond_FE7_ShortTurnType1_Uses12ByteStride()
        {
            // FE7 turn records: a type==1 record advances by 12 (the short-turn event);
            // a non-1 type advances by eventcond_tern_size. Assert both the script trace
            // and that the TURN frame length covers BOTH records (= short stride applied).
            var es = BuildEventScript(TermCommand());
            var rom = MakeVersionedRom("AE7E01"); // FE7U
            // FE7U LoadLow recognized? fall back assert inside WithEnv.
            WithEnv(rom, es, r =>
            {
                Assert.Equal(7, r.RomInfo.version);
                const uint mapId = 0;
                uint mapTableBase = 0x00800000u;
                uint mapSize = r.RomInfo.map_setting_datasize;
                Write32(r, r.RomInfo.map_setting_pointer, Ptr(mapTableBase));
                uint mapRecord = mapTableBase + mapId * mapSize;
                Write32(r, mapRecord + 0, 0x08123456u);
                const byte eventPlist = 7;
                r.Data[mapRecord + r.RomInfo.map_setting_event_plist_pos] = eventPlist;

                uint eventTableBase = 0x00810000u;
                Write32(r, r.RomInfo.map_event_pointer, Ptr(eventTableBase));
                uint condBlock = 0x00820000u;
                Write32(r, eventTableBase + eventPlist * 4u, Ptr(condBlock));

                var slots = MapEventUnitCore.GetCondSlots(r);
                int turnIdx = slots.FindIndex(s => s.Type == MapEventUnitCore.CondType.Turn);
                Assert.True(turnIdx >= 0);

                uint turnTable = 0x00830000u;
                uint ternSize = r.RomInfo.eventcond_tern_size;
                uint script1 = 0x00840000u;
                uint script2 = 0x00850000u;

                // record 0: type==1 (short turn) -> stride 12.
                r.Data[turnTable + 0] = 0x01;
                Write32(r, turnTable + 4, Ptr(script1));
                // record 1 at turnTable + 12: type==2 -> stride ternSize.
                r.Data[turnTable + 12 + 0] = 0x02;
                Write32(r, turnTable + 12 + 4, Ptr(script2));
                // terminator at turnTable + 12 + ternSize: u8==0.
                Write32(r, condBlock + (uint)turnIdx * 4u, Ptr(turnTable));

                r.write_u32(script1 + 0, 0x0000000A);
                r.write_u32(script2 + 0, 0x0000000A);

                var list = new List<Address>();
                RebuildProducerCore.EmitEventCond(r, list);

                // Both scripts traced (proves the 12-byte short-turn stride advanced to record 1).
                Assert.Contains(list, a => a.Addr == script1 && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                Assert.Contains(list, a => a.Addr == script2 && a.DataType == Address.DataTypeEnum.EVENTSCRIPT);
                // TURN frame length = (last base - start) + ternSize = (12 + ternSize - 0) + ternSize.
                var frame = list.Single(a => a.Addr == turnTable && a.DataType == Address.DataTypeEnum.EVENTCOND_TURN);
                Assert.Equal(12u + ternSize + ternSize, frame.Length);
            });
        }

        [Fact]
        public void EmitEventCond_FE6_TalkType0x0D_EmitsAsmFunction()
        {
            // FE6 talk record type 0x0D -> Address.AddFunction at base_addr + 8.
            var es = BuildEventScript(TermCommand());
            var rom = MakeVersionedRom("AFEJ01"); // FE6
            WithEnv(rom, es, r =>
            {
                Assert.Equal(6, r.RomInfo.version);
                const uint mapId = 0;
                uint mapTableBase = 0x00800000u;
                uint mapSize = r.RomInfo.map_setting_datasize;
                Write32(r, r.RomInfo.map_setting_pointer, Ptr(mapTableBase));
                uint mapRecord = mapTableBase + mapId * mapSize;
                Write32(r, mapRecord + 0, 0x08123456u);
                const byte eventPlist = 7;
                r.Data[mapRecord + r.RomInfo.map_setting_event_plist_pos] = eventPlist;

                uint eventTableBase = 0x00810000u;
                Write32(r, r.RomInfo.map_event_pointer, Ptr(eventTableBase));
                uint condBlock = 0x00820000u;
                Write32(r, eventTableBase + eventPlist * 4u, Ptr(condBlock));

                var slots = MapEventUnitCore.GetCondSlots(r);
                int talkIdx = slots.FindIndex(s => s.Type == MapEventUnitCore.CondType.Talk);
                Assert.True(talkIdx >= 0);

                uint talkTable = 0x00830000u;
                uint talkSize = r.RomInfo.eventcond_talk_size;
                uint asmTarget = 0x00880001u; // odd thumb pointer
                r.Data[talkTable + 0] = 0x0D;                 // FE6 ASM type
                Write32(r, talkTable + 4, Ptr(0x00890000u));  // event pointer (some script)
                Write32(r, talkTable + 8, Ptr(asmTarget));    // ASM pointer at +8
                Write32(r, condBlock + (uint)talkIdx * 4u, Ptr(talkTable));
                r.write_u32(0x00890000u, 0x0000000A);         // ENDA

                var list = new List<Address>();
                RebuildProducerCore.EmitEventCond(r, list);

                // The ASM function block at +8 is emitted (AddFunction strips the thumb LSB).
                Assert.Contains(list, a => a.DataType == Address.DataTypeEnum.ASM
                                           && a.Addr == U.toOffset(asmTarget) - 1);
            });
        }

        // =================================================================
        // GetEventAddrForMap / ResolvePlistToEventAddr out-pointer overloads
        // =================================================================

        [Fact]
        public void ResolvePlistToEventAddr_OutPointer_ReturnsSlotAddress()
        {
            var rom = MakeFe8uRom();
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                uint tableBase = 0x00810000u;
                Write32(rom, rom.RomInfo.map_event_pointer, Ptr(tableBase));
                const uint plist = 7;
                uint condBlock = 0x00820000u;
                uint slot = tableBase + plist * 4u;
                Write32(rom, slot, Ptr(condBlock));

                uint addr = MapEventUnitCore.ResolvePlistToEventAddr(rom, plist, out uint outPointer);
                Assert.Equal(condBlock, addr);
                Assert.Equal(slot, outPointer);
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        // =================================================================
        // NotYetPorted coverage
        // =================================================================

        [Fact]
        public void GetNotYetPortedForms_DropsSlice2uForm_KeepsDeferredSiblings()
        {
            string[] notYet = RebuildProducerCore.GetNotYetPortedForms();
            // EventCondForm is now ported (EmitEventCond + EmitScanScript).
            Assert.DoesNotContain("EventCondForm", notYet);
            // Its ScanScript-dependent siblings stay tracked (scoped out of slice 2u).
            Assert.Contains("MonsterWMapProbabilityForm", notYet);
            Assert.Contains("WorldMapEventPointerForm", notYet);
            Assert.Contains("EventBattleTalkForm", notYet);
            Assert.Contains("EventHaikuForm", notYet);
        }

        [Fact]
        public void GetNotYetPortedForms_HasNoDuplicates_AfterSlice2u()
        {
            string[] raw = RebuildProducerCore.GetNotYetPortedFormsRaw();
            var dups = raw.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            Assert.Empty(dups);
        }
    }
}
