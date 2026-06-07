// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for BGReferenceFinder (#990) — the thin ArgType.BG wrapper + per-ROM
// cache over EventScriptReferenceScanner.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class BGReferenceFinderTests
    {
        [Fact]
        public void MakeListByUseBG_NullRom_ReturnsEmpty()
        {
            BGReferenceFinder.ResetCache();
            var list = BGReferenceFinder.MakeListByUseBG(null, 0);
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Fact]
        public void MakeListByUseBG_Gating_NonActiveRom_ReturnsEmpty()
        {
            BGReferenceFinder.ResetCache();
            var prevRom = CoreState.ROM;
            var prevEs = CoreState.EventScript;
            try
            {
                CoreState.ROM = null; // no active ROM -> gating returns empty
                CoreState.EventScript = null;

                var rom = new ROM();
                rom.LoadLow("bg-fe8u.gba", new byte[0x1000000], "BE8E01");
                var list = BGReferenceFinder.MakeListByUseBG(rom, 1);
                Assert.NotNull(list);
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.EventScript = prevEs;
                BGReferenceFinder.ResetCache();
            }
        }

        [Fact]
        public void RealRom_FE8U_MakeListByUseBG_FindsReferencedBg_HighIdEmpty_CacheReuse()
        {
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip

            var savedRom = CoreState.ROM;
            var savedEs = CoreState.EventScript;
            var savedEnc = CoreState.SystemTextEncoder;
            var savedComment = CoreState.CommentCache;
            var savedBaseDir = CoreState.BaseDirectory;
            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
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

                BGReferenceFinder.ResetCache();

                // Discover a referenced BG id directly from the full map.
                var fullMap = EventScriptReferenceScanner.FindAllArgReferences(
                    rom, EventScript.ArgType.BG, keepZeroId: true);
                Assert.NotEmpty(fullMap);

                uint referencedBg = 0;
                bool found = false;
                foreach (var kv in fullMap)
                {
                    if (kv.Value != null && kv.Value.Count > 0)
                    {
                        referencedBg = kv.Key;
                        found = true;
                        break;
                    }
                }
                Assert.True(found, "a referenced BG id must exist in a vanilla FE8U ROM");

                // The wrapper must return a non-empty list for that id.
                var refs = BGReferenceFinder.MakeListByUseBG(rom, referencedBg);
                Assert.NotEmpty(refs);

                // The wrapper returns a COPY (mutating it must not corrupt the cache).
                int before = refs.Count;
                refs.Clear();
                var refsAgain = BGReferenceFinder.MakeListByUseBG(rom, referencedBg);
                Assert.Equal(before, refsAgain.Count);

                // An unused high id must be empty.
                var high = BGReferenceFinder.MakeListByUseBG(rom, 0x7000);
                Assert.Empty(high);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.EventScript = savedEs;
                CoreState.SystemTextEncoder = savedEnc;
                CoreState.CommentCache = savedComment;
                if (savedBaseDir != null)
                    CoreState.BaseDirectory = savedBaseDir;
                BGReferenceFinder.ResetCache();
            }
        }

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
    }
}
