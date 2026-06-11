// SPDX-License-Identifier: GPL-3.0-or-later
// #1027 — Parity tests for MakeVarsIDArrayCore (the Text Editor "used text id"
// union) + the new collectors it folds in (menu / status-rmenu / worldmap-event /
// FE8N-Ver3-skill / asmmap-symbol / cache-enumeration).
//
// Parity strategy (same as ExportFilterCoreTests / BGReferenceFinderTests): WF runs
// in WinForms, so we cannot call WF U.MakeVarsIDArray from Core.Tests. Instead we:
//   * Re-derive expected fixed-table ids from the ROM bytes with the SAME WF offset
//     lists and assert the union is a SUPERSET (it folds in MORE: events/menus/etc).
//   * Assert structural invariants: non-empty on a ROM that has the data, every id in
//     (0, 0x7FFF), every Unit/Class/Item text id present, EventCond contribution
//     present (the union must be a strict superset of the fixed-table-only set).
//   * Synthetic ROMs for the cache enumeration (incl. FE8 reserved ranges).

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MakeVarsIDArrayCoreTests
    {
        // ---- real-ROM full-init harness (mirrors ExportFilterCoreTests) ----
        static string FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(thisAssembly);
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

        // Resolve a base directory whose config/data/ is present. The test bin dir
        // only gets config.xml copied, so for the EventScript.Load (real-ROM) path we
        // walk up to the repo root (the FEBuilderGBA.sln dir, which has config/data).
        static string FindConfigBaseDir()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "config", "data")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        static bool WithRealRom(string romName, System.Action<ROM> body)
        {
            string romPath = FindRom(romName);
            if (romPath == null) return false;

            var savedRom = CoreState.ROM;
            var savedEs = CoreState.EventScript;
            var savedEnc = CoreState.SystemTextEncoder;
            var savedComment = CoreState.CommentCache;
            var savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.BaseDirectory = FindConfigBaseDir();

                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return false;
                CoreState.ROM = rom;
                CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
                CoreState.CommentCache = new HeadlessEtcCache();
                var es = new EventScript();
                es.Load(EventScript.EventScriptType.Event);
                CoreState.EventScript = es;

                body(rom);
                return true;
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.EventScript = savedEs;
                CoreState.SystemTextEncoder = savedEnc;
                CoreState.CommentCache = savedComment;
                if (savedBaseDir != null) CoreState.BaseDirectory = savedBaseDir;
            }
        }

        // Independent fixed-table reference walk (WF AppendVarsID_Low parity).
        static HashSet<uint> RefWalk(ROM rom, uint pointerField, uint entrySize, uint count, uint[] offsets)
        {
            var ids = new HashSet<uint>();
            if (pointerField == 0 || entrySize == 0) return ids;
            if (!U.isSafetyOffset(pointerField + 3, rom)) return ids;
            uint baseAddr = rom.p32(pointerField);
            if (!U.isSafetyOffset(baseAddr, rom)) return ids;
            uint p = baseAddr;
            for (uint i = 0; i < count; i++, p += entrySize)
            {
                if (!U.isSafetyOffset(p + entrySize, rom)) break;
                foreach (uint off in offsets)
                {
                    if (!U.isSafetyOffset(p + off + 1, rom)) break;
                    uint id = rom.u16(p + off);
                    if (id == 0 || id >= 0x7FFF) continue;
                    ids.Add(id);
                }
            }
            return ids;
        }

        static void AssertAllInRange(IEnumerable<uint> ids)
        {
            foreach (uint id in ids)
                Assert.True(id >= 1 && id < 0x7FFF, $"id 0x{id:X} out of WF range");
        }

        // =================================================================
        // Null / structural.
        // =================================================================
        [Fact]
        public void NullRom_ReturnsEmpty()
        {
            var r = MakeVarsIDArrayCore.BuildAllUsedRefs(null);
            Assert.Empty(r.TextIds);
            Assert.Empty(r.SongIds);
        }

        // =================================================================
        // Real-ROM parity (FE6 / FE7U / FE8U).
        // =================================================================
        [Theory]
        [InlineData("FE6.gba")]
        [InlineData("FE7U.gba")]
        [InlineData("FE8U.gba")]
        public void RealRom_Union_NonEmpty_AllInRange(string romName)
        {
            bool ran = WithRealRom(romName, rom =>
            {
                var r = MakeVarsIDArrayCore.BuildAllUsedRefs(rom);
                Assert.NotEmpty(r.TextIds);
                AssertAllInRange(r.TextIds);
                AssertAllInRange(r.SongIds);
            });
            if (!ran) return; // ROM absent locally — CI supplies it.
        }

        [Theory]
        [InlineData("FE6.gba")]
        [InlineData("FE7U.gba")]
        [InlineData("FE8U.gba")]
        public void RealRom_Union_ContainsEveryUnitClassItemTextId(string romName)
        {
            bool ran = WithRealRom(romName, rom =>
            {
                var r = MakeVarsIDArrayCore.BuildAllUsedRefs(rom);
                var info = rom.RomInfo;

                var unit = RefWalk(rom, info.unit_pointer, info.unit_datasize,
                    info.unit_maxcount != 0 ? info.unit_maxcount : 0x100, new uint[] { 0, 2 });
                var cls = RefWalk(rom, info.class_pointer, info.class_datasize, 0x100, new uint[] { 0, 2 });
                var item = RefWalk(rom, info.item_pointer, info.item_datasize, 0x100, new uint[] { 0, 2, 4 });

                foreach (uint id in unit) Assert.Contains(id, r.TextIds);
                foreach (uint id in cls) Assert.Contains(id, r.TextIds);
                foreach (uint id in item) Assert.Contains(id, r.TextIds);
            });
            if (!ran) return;
        }

        [Theory]
        [InlineData("FE6.gba")]
        [InlineData("FE7U.gba")]
        [InlineData("FE8U.gba")]
        public void RealRom_Union_IsSupersetOfEventCondTextIds(string romName)
        {
            bool ran = WithRealRom(romName, rom =>
            {
                var eventOnly = new HashSet<uint>();
                EventScriptReferenceScanner.CollectEventCondTextIds(rom, eventOnly);

                var r = MakeVarsIDArrayCore.BuildAllUsedRefs(rom);
                foreach (uint id in eventOnly)
                    Assert.Contains(id, r.TextIds);
            });
            if (!ran) return;
        }

        [Theory]
        [InlineData("FE6.gba")]
        [InlineData("FE7U.gba")]
        [InlineData("FE8U.gba")]
        public void RealRom_FreeAreaUsedSet_ContainsZeroAndUnion(string romName)
        {
            bool ran = WithRealRom(romName, rom =>
            {
                var used = MakeVarsIDArrayCore.BuildFreeAreaUsedSet(rom, CoreState.UseTextIDCache);
                Assert.Contains(0u, used); // WF seeds textmap[0] = true
                var r = MakeVarsIDArrayCore.BuildAllUsedRefs(rom);
                foreach (uint id in r.TextIds) Assert.Contains(id, used);
                foreach (uint id in r.SongIds) Assert.Contains(id, used);
            });
            if (!ran) return;
        }

        // FE8J_skill — FE8N skill systems. The union must include the skill-config
        // text ids (FE8N Ver1/2 or Ver3) when a skill system is installed.
        [Fact]
        public void RealRom_FE8JSkill_Union_NonEmpty()
        {
            bool ran = WithRealRom("FE8J_skill.gba", rom =>
            {
                var r = MakeVarsIDArrayCore.BuildAllUsedRefs(rom);
                Assert.NotEmpty(r.TextIds);
                AssertAllInRange(r.TextIds);
            });
            if (!ran) return;
        }

        // =================================================================
        // AsmMap symbol reader — FE8U should have TEXTBATCH + EVENT records.
        // =================================================================
        [Fact]
        public void RealRom_FE8U_AsmMapReader_ContributesTextIds()
        {
            bool ran = WithRealRom("FE8U.gba", rom =>
            {
                var ids = new HashSet<uint>();
                AsmMapTextSymbolReader.CollectUsedTextIds(rom, ids);
                // FE8 asmmap has &TEXTBATCH (TextBatch3..6, etc.) records that
                // resolve to non-empty text-id runs.
                AssertAllInRange(ids);
                // Not asserting non-empty unconditionally (depends on the shipped
                // config), but the union must contain whatever the reader found.
                var r = MakeVarsIDArrayCore.BuildAllUsedRefs(rom);
                foreach (uint id in ids) Assert.Contains(id, r.TextIds);
            });
            if (!ran) return;
        }

        // =================================================================
        // Cache enumeration (synthetic — incl. FE8 reserved ranges).
        // =================================================================
        [Fact]
        public void CacheEnumeration_DefaultInterface_IsEmpty()
        {
            ITextIDCache cache = new StubCache();
            Assert.Empty(cache.EnumerateUsedTextIds(null));
        }

        [Fact]
        public void CacheEnumeration_Core_FE8NonMultibyte_IncludesReservedRange()
        {
            // FE8U (non-multibyte) reserves 0xE00..0xFFF.
            WithFE8Rom(isMultibyte: false, (rom, cache) =>
            {
                var ids = new HashSet<uint>(cache.EnumerateUsedTextIds(rom));
                Assert.Contains(0xE00u, ids);
                Assert.Contains(0xFFFu, ids);
                Assert.DoesNotContain(0x1000u, ids);
                // 0 must never leak (AppendTextID guard).
                Assert.DoesNotContain(0u, ids);
            });
        }

        [Fact]
        public void CacheEnumeration_Core_FE8Multibyte_IncludesSmallerReservedRange()
        {
            // FE8J (multibyte) reserves 0xE00..0xEFF only.
            WithFE8Rom(isMultibyte: true, (rom, cache) =>
            {
                var ids = new HashSet<uint>(cache.EnumerateUsedTextIds(rom));
                Assert.Contains(0xE00u, ids);
                Assert.Contains(0xEFFu, ids);
                Assert.DoesNotContain(0xF00u, ids);
            });
        }

        // Load a real FE8 ROM (version==8 + correct is_multibyte), construct a Core
        // text-id cache, run the body, then RESTORE CoreState.BaseDirectory. Skips
        // (no-op) when ROMs are absent locally (CI supplies them).
        static void WithFE8Rom(bool isMultibyte, System.Action<ROM, TextIDCacheCore> body)
        {
            string romName = isMultibyte ? "FE8J.gba" : "FE8U.gba";
            string path = FindRom(romName);
            if (path == null) return;
            var saved = CoreState.BaseDirectory;
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.BaseDirectory = FindConfigBaseDir();
                var rom = new ROM();
                if (!rom.Load(path, out string _)) return;
                Assert.Equal(8, rom.RomInfo.version);
                Assert.Equal(isMultibyte, rom.RomInfo.is_multibyte);
                // TextIDCacheCore() resolves config/data/textid_ via CoreState.ROM —
                // set it so ConfigDataFilename finds textid_FE8.txt (not the missing
                // textid_ALL.txt fallback that trips the DEBUG IsRequiredFileExist).
                CoreState.ROM = rom;
                var cache = new TextIDCacheCore();
                body(rom, cache);
            }
            finally
            {
                CoreState.BaseDirectory = saved;
                CoreState.ROM = savedRom;
            }
        }

        sealed class StubCache : ITextIDCache
        {
            public void Update(uint textid, string comment) { }
            public void Save(string romBaseFilename) { }
            public string GetName(uint textid) => "";
            // EnumerateUsedTextIds intentionally NOT overridden — exercises the
            // interface default body (#1027).
        }
    }
}
