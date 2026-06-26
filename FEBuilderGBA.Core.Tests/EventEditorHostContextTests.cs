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
    }
}
