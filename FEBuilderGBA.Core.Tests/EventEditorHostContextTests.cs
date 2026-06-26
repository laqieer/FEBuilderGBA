// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for EventEditorHostContext (#1591) — the cross-platform Alloc-Event host
// context (map-id provider + label allocator) + EventTemplateCore's context-aware
// substitution overloads. All GUI-free.

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class EventEditorHostContextTests
    {
        static ROM MakeFE8U()
        {
            var rom = new ROM();
            rom.LoadLow("evt-host-fe8u.gba", new byte[0x1000000], "BE8E01");
            return rom;
        }

        // A minimal fake host: a fixed map-id (or "no map") + an explicit set of
        // already-used label ids. Lets the substitution be tested entirely offline.
        sealed class FakeHost : IEventEditorHostContext
        {
            readonly bool _hasMap;
            readonly uint _mapid;
            readonly HashSet<uint> _usedLabels;

            public FakeHost(bool hasMap, uint mapid, params uint[] usedLabels)
            {
                _hasMap = hasMap;
                _mapid = mapid;
                _usedLabels = new HashSet<uint>(usedLabels ?? Array.Empty<uint>());
            }

            public bool TryGetMapID(out uint mapid)
            {
                mapid = _hasMap ? _mapid : 0;
                return _hasMap;
            }

            public bool IsUseLabelID(uint labelID) => _usedLabels.Contains(labelID);
        }

        // ---- formatter ports (verbatim EventTemplateImpl) -------------------

        [Fact]
        public void ToPointerToString_FormatsLittleEndianPointer()
        {
            // 0x5908D8 -> pointer 0x08591FD8? No: toPointer adds 0x08000000.
            // Use a known value: addr 0x123456 -> pointer 0x08123456 ->
            // little-endian bytes 56 34 12 08.
            string s = EventEditorHostContext.ToPointerToString(0x123456);
            Assert.Equal("56341208", s);
        }

        [Fact]
        public void ToPointerToString_NotFound_UsesInvalidateSentinel()
        {
            // U.NOT_FOUND -> INVALIDATE_UNIT_POINTER (0xFFFFFF) -> pointer
            // 0x08FFFFFF -> little-endian FF FF FF 08.
            string s = EventEditorHostContext.ToPointerToString(U.NOT_FOUND);
            Assert.Equal("FFFFFF08", s);
        }

        [Fact]
        public void ToUShortToString_FormatsFirst4HexOfLittleEndian()
        {
            // 0x9922 -> little-endian first 4 hex = "2299".
            string s = EventEditorHostContext.ToUShortToString(0x9922);
            Assert.Equal("2299", s);
        }

        // ---- label allocator -----------------------------------------------

        [Fact]
        public void GetUnuseLabelID_ReturnsStart_WhenNoneUsed()
        {
            var host = new FakeHost(hasMap: false, mapid: 0);
            Assert.Equal(0x9000u, EventEditorHostContext.GetUnuseLabelID(host, 0x9000));
        }

        [Fact]
        public void GetUnuseLabelID_SkipsUsedIds()
        {
            var host = new FakeHost(hasMap: false, mapid: 0, 0x9000, 0x9001);
            Assert.Equal(0x9002u, EventEditorHostContext.GetUnuseLabelID(host, 0x9000));
        }

        [Fact]
        public void GetUnuseLabelID_TwoDistinctLabels_LikeCondTemplate()
        {
            // _COND_ allocates labelX from 0x9000 then labelY from labelX+1, so the
            // two are always distinct even when intervening ids are used.
            var host = new FakeHost(hasMap: false, mapid: 0, 0x9000);
            uint x = EventEditorHostContext.GetUnuseLabelID(host, 0x9000);
            uint y = EventEditorHostContext.GetUnuseLabelID(host, x + 1);
            Assert.Equal(0x9001u, x);
            Assert.Equal(0x9002u, y);
            Assert.NotEqual(x, y);
        }

        [Fact]
        public void GetUnuseLabelID_NullHost_ReturnsSentinel()
        {
            Assert.Equal(0xFFFFu, EventEditorHostContext.GetUnuseLabelID(null, 0x9000));
        }

        // ---- TryGenerateBrowserTemplateWithContext: substitution + gate -----

        static EventTemplateCore.BrowserTemplate MakeTemplate(string filename) =>
            new EventTemplateCore.BrowserTemplate { Filename = filename, Info = "t", RequiresContext = true };

        // Build a one-off template config file under a temp BaseDirectory/config/data
        // so the WithContext overload (which Path.Combine's BaseDirectory) finds it.
        static (string baseDir, EventTemplateCore.BrowserTemplate et) StageTemplate(string name, string contents)
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "evt-hostctx-" + Guid.NewGuid().ToString("N"));
            string dataDir = Path.Combine(baseDir, "config", "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, name), contents);
            return (baseDir, MakeTemplate(name));
        }

        [Fact]
        public void WithContext_CondTemplate_SubstitutesLabels()
        {
            // A _COND_ template with XXXX (LABEL) + YYYY (GOTO). With a host present
            // (no map needed), the two labels are substituted from 0x9000.
            var (baseDir, et) = StageTemplate("template_event_COND_FAKE_FE8.txt",
                "400CXXXX0C000000\t//BEQ [cond]\n" +
                "2009YYYY\t//GOTO\n" +
                "2008XXXX\t//LABEL\n");
            string prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8U();
                var host = new FakeHost(hasMap: false, mapid: 0); // no labels used

                var r = EventTemplateCore.TryGenerateBrowserTemplateWithContext(rom, et, host, out byte[] bytes);
                Assert.Equal(EventTemplateCore.GenerateResult.Ok, r);
                Assert.NotNull(bytes);

                // labelX=0x9000 -> "0090", labelY=0x9001 -> "0190".
                // Line1: 40 0C 00 90 0C 00 00 00 ; Line2: 20 09 01 90 ; Line3: 20 08 00 90
                Assert.Equal(new byte[]
                {
                    0x40,0x0C,0x00,0x90,0x0C,0x00,0x00,0x00,
                    0x20,0x09,0x01,0x90,
                    0x20,0x08,0x00,0x90,
                }, bytes);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void WithContext_NullHost_RefusesEmpty_ContextRequired()
        {
            // THE GATE-HOLDS regression test (#1589 invariant): a placeholder template
            // with NO host returns RequiresEditorContext + no bytes.
            var (baseDir, et) = StageTemplate("template_event_COND_FAKE_FE8.txt",
                "400CXXXX0C000000\t//BEQ\n2008XXXX\t//LABEL\n");
            string prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8U();
                var r = EventTemplateCore.TryGenerateBrowserTemplateWithContext(rom, et, null, out byte[] bytes);
                Assert.Equal(EventTemplateCore.GenerateResult.RequiresEditorContext, r);
                Assert.Null(bytes);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void WithContext_PreparationButNoMap_RefusesEmpty()
        {
            // A PREPARATION template needs a map; a host that CANNOT resolve a map must
            // refuse (no silent map-0 pointers — finding #1).
            var (baseDir, et) = StageTemplate("template_event_PREPARATION_FAKE_FE8.txt",
                "402C0100XXXXXXXX20300000\t//LOAD1\n" +
                "412C0100YYYYYYYY20300000\t//LOAD2\n");
            string prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8U();
                var host = new FakeHost(hasMap: false, mapid: 0); // no map

                var r = EventTemplateCore.TryGenerateBrowserTemplateWithContext(rom, et, host, out byte[] bytes);
                Assert.Equal(EventTemplateCore.GenerateResult.RequiresEditorContext, r);
                Assert.Null(bytes);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void WithContext_UnknownPlaceholderFamily_RefusesEmpty()
        {
            // A placeholder file whose name matches NO known family (not _COND_,
            // not PREPARATION, not CALL_END_EVENT) must refuse even WITH a host —
            // we don't know how to substitute it, so emitting risks a truncated
            // command (finding #2).
            var (baseDir, et) = StageTemplate("template_event_MYSTERY_FE8.txt",
                "0102XXXX\t//unknown placeholder\n");
            string prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8U();
                var host = new FakeHost(hasMap: true, mapid: 1);
                var r = EventTemplateCore.TryGenerateBrowserTemplateWithContext(rom, et, host, out byte[] bytes);
                Assert.Equal(EventTemplateCore.GenerateResult.RequiresEditorContext, r);
                Assert.Null(bytes);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void WithContext_PlaceholderFreeTemplate_GeneratesIgnoringHost()
        {
            // A template with no placeholders is ContextKind.None: generates the same
            // bytes with or without a host.
            var (baseDir, et) = StageTemplate("template_event_PLAIN_FE8.txt",
                "01020304\t//plain\n");
            et.RequiresContext = false;
            string prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8U();

                var r1 = EventTemplateCore.TryGenerateBrowserTemplateWithContext(rom, et, null, out byte[] b1);
                var r2 = EventTemplateCore.TryGenerateBrowserTemplateWithContext(rom, et, new FakeHost(true, 0), out byte[] b2);
                Assert.Equal(EventTemplateCore.GenerateResult.Ok, r1);
                Assert.Equal(EventTemplateCore.GenerateResult.Ok, r2);
                Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, b1);
                Assert.Equal(b1, b2);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void ClassifyContextKind_FamilyNameButPlaceholderFree_IsNone()
        {
            // A template that KEEPS a family marker in its name but carries NO active
            // placeholder must classify as None (generatable host-free), not be gated as
            // context-required (Copilot PR re-review).
            var (baseDir, _) = StageTemplate("template_event_COND_NOPLACEHOLDER_FE8.txt",
                "21030A00\t//a real command with no placeholder\n");
            string dataDir = Path.Combine(baseDir, "config", "data");
            string prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8U();
                string file = Path.Combine(dataDir, "template_event_COND_NOPLACEHOLDER_FE8.txt");
                Assert.Equal(EventTemplateCore.ContextKind.None,
                    EventTemplateCore.ClassifyContextKind(rom, file));

                // And it generates WITHOUT a host (the gate must not trip).
                var et = MakeTemplate("template_event_COND_NOPLACEHOLDER_FE8.txt");
                et.RequiresContext = false;
                var r = EventTemplateCore.TryGenerateBrowserTemplateWithContext(rom, et, null, out byte[] bytes);
                Assert.Equal(EventTemplateCore.GenerateResult.Ok, r);
                Assert.Equal(new byte[] { 0x21, 0x03, 0x0A, 0x00 }, bytes);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void ClassifyContextKind_UsesFileNameNotPath()
        {
            // A directory component containing a family marker must NOT misclassify the
            // template — classification keys off the file NAME only (Copilot PR review).
            string baseDir = Path.Combine(Path.GetTempPath(), "evt-pathcls-" + Guid.NewGuid().ToString("N"));
            // The directory name contains "template_event_PREPARATION", but the FILE is a plain template.
            string dir = Path.Combine(baseDir, "template_event_PREPARATION_dir");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "template_event_PLAIN_FE8.txt");
            File.WriteAllText(file, "01020304\t//plain\n");
            string prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8U();
                // Must be None (plain file), NOT Preparation (the dir name's marker).
                Assert.Equal(EventTemplateCore.ContextKind.None,
                    EventTemplateCore.ClassifyContextKind(rom, file));
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void ClassifyContextKind_MatchesFilenameFamilies()
        {
            var (baseDir, _) = StageTemplate("template_event_PLAIN_FE8.txt", "01020304\t//plain\n");
            string dataDir = Path.Combine(baseDir, "config", "data");
            File.WriteAllText(Path.Combine(dataDir, "template_event_CALL_END_EVENT_X.txt"), "400A0000XXXXXXXX\t//call\n");
            File.WriteAllText(Path.Combine(dataDir, "template_event_PREPARATION_X.txt"), "402C0100XXXXXXXX\t//prep\n");
            File.WriteAllText(Path.Combine(dataDir, "template_event_COND_X.txt"), "2008XXXX\t//label\n");
            File.WriteAllText(Path.Combine(dataDir, "template_event_MYSTERY_X.txt"), "0102XXXX\t//mystery\n");
            string prevBase = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = baseDir;
                ROM rom = MakeFE8U();
                string P(string n) => Path.Combine(dataDir, n);
                Assert.Equal(EventTemplateCore.ContextKind.None, EventTemplateCore.ClassifyContextKind(rom, P("template_event_PLAIN_FE8.txt")));
                Assert.Equal(EventTemplateCore.ContextKind.CallEndEvent, EventTemplateCore.ClassifyContextKind(rom, P("template_event_CALL_END_EVENT_X.txt")));
                Assert.Equal(EventTemplateCore.ContextKind.Preparation, EventTemplateCore.ClassifyContextKind(rom, P("template_event_PREPARATION_X.txt")));
                Assert.Equal(EventTemplateCore.ContextKind.Cond, EventTemplateCore.ClassifyContextKind(rom, P("template_event_COND_X.txt")));
                Assert.Equal(EventTemplateCore.ContextKind.Unknown, EventTemplateCore.ClassifyContextKind(rom, P("template_event_MYSTERY_X.txt")));
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        // NOTE: the codes-overload round-trip (TryGenerateBrowserTemplateCodesWithContext)
        // is covered against a REAL ROM + the shipped config in EventTemplateCoreTests
        // (RealRom_FE8U_BrowserCodesWithContext_*), because disassembling the substituted
        // bytes needs the real config-driven event vocabulary, which a synthetic all-zero
        // ROM does not provide.

        // ================================================================
        // EventCond-RECORD Alloc-Event side effects (#1592):
        //   ResolveCallTemplate / CounterReinforcementSideEffects /
        //   IsEventPointerSurface.
        // ================================================================

        // ---- ResolveCallTemplate: Call1 ------------------------------------

        [Fact]
        public void ResolveCallTemplate_Call1_WritesLiteralOne_NoFlag()
        {
            // CALL_1 is always resolvable and writes the literal value 1 into the
            // event-pointer field with NO victory flag (WF EventTemplate*Form
            // CALL_1_button_Click → src_object.Value = 1).
            ROM rom = MakeFE8U();
            var eff = EventEditorHostContext.ResolveCallTemplate(rom, U.NOT_FOUND,
                EventEditorHostContext.AllocTemplateChoice.Call1);
            Assert.True(eff.Resolvable);
            Assert.True(eff.HasEventPtr);
            Assert.Equal(1u, eff.EventPtr);
            Assert.False(eff.SetFlag03);
            Assert.False(eff.CounterReinforcement);
        }

        // ---- ResolveCallTemplate: CALL_EndEvent refuse gates ----------------

        [Fact]
        public void ResolveCallTemplate_CallEndEvent_InvalidMap_Refuses()
        {
            // Invalid-map guard (finding #4): mapid == NOT_FOUND must refuse
            // BEFORE ResolveEndEvent — no wrapped/garbage map id is resolved.
            ROM rom = MakeFE8U();
            var eff = EventEditorHostContext.ResolveCallTemplate(rom, U.NOT_FOUND,
                EventEditorHostContext.AllocTemplateChoice.CallEndEvent);
            Assert.False(eff.Resolvable);
            Assert.False(eff.HasEventPtr);
        }

        [Fact]
        public void ResolveCallTemplate_CallEndEvent_NoEndEvent_Refuses()
        {
            // A ROM with no resolvable chapter END_EVENT pointer must refuse
            // (no silent garbage pointer). The all-zero synthetic ROM has no map
            // chain, so ResolveEndEvent returns NOT_FOUND.
            ROM rom = MakeFE8U();
            var eff = EventEditorHostContext.ResolveCallTemplate(rom, 0,
                EventEditorHostContext.AllocTemplateChoice.CallEndEvent);
            Assert.False(eff.Resolvable);
            Assert.False(eff.HasEventPtr);
        }

        [Fact]
        public void ResolveCallTemplate_NullRom_CallEndEvent_Refuses()
        {
            var eff = EventEditorHostContext.ResolveCallTemplate(null, 0,
                EventEditorHostContext.AllocTemplateChoice.CallEndEvent);
            Assert.False(eff.Resolvable);
        }

        // ---- ResolveCallTemplate: CALL_EndEvent SUCCESS (full chain) --------

        [Fact]
        public void ResolveCallTemplate_CallEndEvent_Resolvable_WritesPointerAndFlag()
        {
            // Build the full map→plist→cond-block→END_EVENT chain so ResolveEndEvent
            // returns a real address; the CALL_EndEvent template must then write
            // U.toPointer(endAddr) into the event-pointer field AND set W2=0x03.
            ROM rom = MakeFE8U();
            const uint mapId = 0;
            const uint endEventOff = 0x40000;   // the chapter end-event target
            BuildEndEventChain(rom, mapId, plist: 1, condBlockOff: 0x30000, endEventOff: endEventOff);

            // sanity: ResolveEndEvent sees the wired end-event.
            Assert.Equal(endEventOff, EventEditorHostContext.ResolveEndEvent(rom, mapId));

            var eff = EventEditorHostContext.ResolveCallTemplate(rom, mapId,
                EventEditorHostContext.AllocTemplateChoice.CallEndEvent);
            Assert.True(eff.Resolvable);
            Assert.True(eff.HasEventPtr);
            Assert.Equal(U.toPointer(endEventOff), eff.EventPtr);
            Assert.True(eff.SetFlag03);
            Assert.False(eff.CounterReinforcement);
        }

        // ---- CounterReinforcementSideEffects --------------------------------

        [Fact]
        public void CounterReinforcementSideEffects_FlagsCounterOnly()
        {
            var eff = EventEditorHostContext.CounterReinforcementSideEffects();
            Assert.True(eff.Resolvable);
            Assert.True(eff.CounterReinforcement);
            Assert.False(eff.HasEventPtr);
            Assert.False(eff.SetFlag03);
        }

        // ---- IsEventPointerSurface gate (finding #3) ------------------------

        [Theory]
        // TURN N02
        [InlineData(MapEventUnitCore.CondType.Turn, 0x02u, true)]
        [InlineData(MapEventUnitCore.CondType.Turn, 0x00u, false)]
        // TALK N03/N04/N0D
        [InlineData(MapEventUnitCore.CondType.Talk, 0x03u, true)]
        [InlineData(MapEventUnitCore.CondType.Talk, 0x04u, true)]
        [InlineData(MapEventUnitCore.CondType.Talk, 0x0Du, true)]
        [InlineData(MapEventUnitCore.CondType.Talk, 0x07u, false)]
        // OBJECT N06/N08 yes; N05/N07 chest + N0A shop NO
        [InlineData(MapEventUnitCore.CondType.Object, 0x06u, true)]
        [InlineData(MapEventUnitCore.CondType.Object, 0x08u, true)]
        [InlineData(MapEventUnitCore.CondType.Object, 0x05u, false)]
        [InlineData(MapEventUnitCore.CondType.Object, 0x07u, false)]
        [InlineData(MapEventUnitCore.CondType.Object, 0x0Au, false)]
        // ALWAYS N01/N0B/N0D/N0E
        [InlineData(MapEventUnitCore.CondType.Always, 0x01u, true)]
        [InlineData(MapEventUnitCore.CondType.Always, 0x0Bu, true)]
        [InlineData(MapEventUnitCore.CondType.Always, 0x0Du, true)]
        [InlineData(MapEventUnitCore.CondType.Always, 0x0Eu, true)]
        [InlineData(MapEventUnitCore.CondType.Always, 0x05u, false)]
        // pointer-only / TRAP / TUTORIAL are never event-pointer surfaces
        [InlineData(MapEventUnitCore.CondType.Trap, 0x01u, false)]
        [InlineData(MapEventUnitCore.CondType.Tutorial, 0x01u, false)]
        [InlineData(MapEventUnitCore.CondType.EndEvent, 0x00u, false)]
        [InlineData(MapEventUnitCore.CondType.PlayerUnit, 0x00u, false)]
        public void IsEventPointerSurface_MatchesNewAllocSurfaces(
            MapEventUnitCore.CondType cat, uint condType, bool expected)
        {
            Assert.Equal(expected, EventEditorHostContext.IsEventPointerSurface(cat, condType));
        }

        // ---- helper: build the map→plist→cond→END_EVENT chain ---------------

        // Wires a synthetic FE8 chain so ResolveEndEvent(rom, mapId) returns
        // endEventOff. The cond block's EndEvent slot is index 19 on FE8.
        static void BuildEndEventChain(ROM rom, uint mapId, uint plist, uint condBlockOff, uint endEventOff)
        {
            var ri = rom.RomInfo;

            // 1) map setting table: map_setting_pointer -> base; map[mapId] at
            //    +map_setting_event_plist_pos holds the plist byte.
            uint mapBase = 0x10000;
            U.write_u32(rom.Data, U.toOffset(ri.map_setting_pointer), U.toPointer(mapBase));
            uint mapAddr = mapBase + mapId * ri.map_setting_datasize;
            // MakeMapIDList / IsMapSettingValid walks rows; we only need GetMapAddr,
            // which indexes directly. Write the event plist byte.
            rom.Data[mapAddr + ri.map_setting_event_plist_pos] = (byte)plist;

            // 2) event pointer table: map_event_pointer -> table base; table[plist]
            //    holds the cond block address.
            uint tableBase = 0x20000;
            U.write_u32(rom.Data, U.toOffset(ri.map_event_pointer), U.toPointer(tableBase));
            U.write_u32(rom.Data, tableBase + plist * 4, U.toPointer(condBlockOff));

            // 3) cond block: slot 19 (FE8 EndEvent) holds the end-event pointer.
            var slots = MapEventUnitCore.GetCondSlots(rom);
            int endIdx = slots.FindIndex(s => s.Type == MapEventUnitCore.CondType.EndEvent);
            Assert.True(endIdx >= 0);
            U.write_u32(rom.Data, condBlockOff + (uint)(endIdx * 4), U.toPointer(endEventOff));
        }
    }
}
