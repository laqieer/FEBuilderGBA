// SPDX-License-Identifier: GPL-3.0-or-later
// #1027 — Tests for TextFreeAreaCore (definitive free-area scan).
//
//   * Scanner-prerequisites guard: when EventScript / CommentCache are NOT wired
//     (or the ROM is not the active CoreState.ROM), the scan returns Status =
//     PrerequisitesMissing with an EMPTY list (NEVER a silent-incomplete list of
//     false positives).
//   * Definitive scan on a real ROM: every referenced id (Unit/Class/Item/EventCond)
//     is NOT in the free list; the result is the complement of the used set.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class TextFreeAreaCoreTests
    {
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

        // =================================================================
        // Scanner-prerequisites guard.
        // =================================================================
        [Fact]
        public void NullRom_PrerequisitesMissing()
        {
            var r = TextFreeAreaCore.FindUnreferencedTextIds(null, null);
            Assert.Equal(TextFreeAreaCore.ScanStatus.PrerequisitesMissing, r.Status);
            Assert.Empty(r.FreeTextIds);
        }

        [Fact]
        public void ForeignRom_NotActiveCoreState_PrerequisitesMissing()
        {
            // Load FE8U but DON'T set it as CoreState.ROM -> prerequisites unmet ->
            // must NOT produce a silent incomplete (false-positive) list.
            string path = FindRom("FE8U.gba");
            if (path == null) return;

            var savedRom = CoreState.ROM;
            var savedEs = CoreState.EventScript;
            var savedComment = CoreState.CommentCache;
            var savedBaseDir = CoreState.BaseDirectory;
            try
            {
                CoreState.ROM = null;
                CoreState.EventScript = null;
                CoreState.CommentCache = null;

                CoreState.BaseDirectory = FindConfigBaseDir();
                var rom = new ROM();
                if (!rom.Load(path, out string _)) return;

                var r = TextFreeAreaCore.FindUnreferencedTextIds(rom, null);
                Assert.Equal(TextFreeAreaCore.ScanStatus.PrerequisitesMissing, r.Status);
                Assert.Empty(r.FreeTextIds);
                Assert.False(TextFreeAreaCore.PrerequisitesMet(rom));
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.EventScript = savedEs;
                CoreState.CommentCache = savedComment;
                if (savedBaseDir != null) CoreState.BaseDirectory = savedBaseDir;
            }
        }

        // =================================================================
        // Definitive scan — prerequisites met.
        // =================================================================
        [Theory]
        [InlineData("FE6.gba")]
        [InlineData("FE7U.gba")]
        [InlineData("FE8U.gba")]
        public void RealRom_PrerequisitesMet_Definitive(string romName)
        {
            bool ran = WithRealRom(romName, rom =>
            {
                Assert.True(TextFreeAreaCore.PrerequisitesMet(rom));
                var r = TextFreeAreaCore.FindUnreferencedTextIds(rom, CoreState.UseTextIDCache);
                Assert.Equal(TextFreeAreaCore.ScanStatus.Definitive, r.Status);
            });
            if (!ran) return;
        }

        [Theory]
        [InlineData("FE6.gba")]
        [InlineData("FE7U.gba")]
        [InlineData("FE8U.gba")]
        public void RealRom_NoReferencedIdIsListedFree(string romName)
        {
            bool ran = WithRealRom(romName, rom =>
            {
                var used = MakeVarsIDArrayCore.BuildFreeAreaUsedSet(rom, CoreState.UseTextIDCache);
                var r = TextFreeAreaCore.FindUnreferencedTextIds(rom, CoreState.UseTextIDCache);
                Assert.Equal(TextFreeAreaCore.ScanStatus.Definitive, r.Status);

                var freeSet = new HashSet<uint>(r.FreeTextIds);
                // Complement invariant: free ∩ used == ∅.
                foreach (uint id in r.FreeTextIds)
                    Assert.DoesNotContain(id, used);
                // And every Unit text id (definitely referenced) is NOT free.
                var info = rom.RomInfo;
                foreach (uint id in RefWalkUnit(rom, info))
                    Assert.DoesNotContain(id, freeSet);
            });
            if (!ran) return;
        }

        // A known-unused, non-empty text slot IS listed free: pick the FIRST slot the
        // definitive scan reports and assert it is NOT in the used set + decodes to
        // non-empty text (the scan's own contract).
        [Theory]
        [InlineData("FE6.gba")]
        [InlineData("FE7U.gba")]
        [InlineData("FE8U.gba")]
        public void RealRom_FreeResultsAreUnusedAndNonEmpty(string romName)
        {
            bool ran = WithRealRom(romName, rom =>
            {
                var used = MakeVarsIDArrayCore.BuildFreeAreaUsedSet(rom, CoreState.UseTextIDCache);
                var r = TextFreeAreaCore.FindUnreferencedTextIds(rom, CoreState.UseTextIDCache);
                Assert.Equal(TextFreeAreaCore.ScanStatus.Definitive, r.Status);

                foreach (uint id in r.FreeTextIds)
                {
                    Assert.DoesNotContain(id, used);
                    string decoded = FETextDecode.Direct(id) ?? "";
                    Assert.False(string.IsNullOrWhiteSpace(decoded),
                        $"free id 0x{id:X} decoded empty");
                }
            });
            if (!ran) return;
        }

        static HashSet<uint> RefWalkUnit(ROM rom, ROMFEINFO info)
        {
            var ids = new HashSet<uint>();
            if (info.unit_pointer == 0 || info.unit_datasize == 0) return ids;
            if (!U.isSafetyOffset(info.unit_pointer + 3, rom)) return ids;
            uint baseAddr = rom.p32(info.unit_pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return ids;
            uint count = info.unit_maxcount != 0 ? info.unit_maxcount : 0x100;
            uint p = baseAddr;
            for (uint i = 0; i < count; i++, p += info.unit_datasize)
            {
                if (!U.isSafetyOffset(p + info.unit_datasize, rom)) break;
                foreach (uint off in new uint[] { 0, 2 })
                {
                    if (!U.isSafetyOffset(p + off + 1, rom)) break;
                    uint id = rom.u16(p + off);
                    if (id == 0 || id >= 0x7FFF) continue;
                    ids.Add(id);
                }
            }
            return ids;
        }
    }
}
