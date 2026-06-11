// SPDX-License-Identifier: GPL-3.0-or-later
// #1028 Slice A — TextIDCacheCore (ITextIDCache) temp-config round-trip tests.
//
// Verifies the Core text-id reference cache mirrors the WinForms EtcCacheTextID
// behavior: Update sets / overwrites / removes a per-text-id comment; GetName
// reads it back; Save persists to config/etc/<rom>/textid_.txt only when the
// cache is non-empty (an emptied cache is NOT written/deleted via Save — WF parity).
using System;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class TextIDCacheCoreTests : IDisposable
    {
        readonly string _tempDir;
        readonly string _prevBaseDir;
        readonly ROM _prevRom;

        // The TextIDCacheCore ctor + Save use ConfigEtcFilename, which resolves the
        // ROM title from CoreState.ROM (null => "_"). Pass "_" as the rom base
        // filename to Save so it writes the SAME file the ctor re-loads.
        const string RomBase = "_";

        public TextIDCacheCoreTests()
        {
            _prevBaseDir = CoreState.BaseDirectory;
            _prevRom = CoreState.ROM;

            _tempDir = Path.Combine(Path.GetTempPath(), "fe_textidcache_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            CoreState.BaseDirectory = _tempDir;
            CoreState.ROM = null; // ConfigEtcFilename / ConfigDataFilename => romtitle "_"

            // The ctor loads the shipped SYSTEM names via ConfigDataFilename, which
            // (when no per-ROM file exists) falls back to config/data/textid_ALL.txt
            // and asserts it exists. Seed a minimal one in the temp config so the ctor
            // succeeds headlessly; it also lets GetName's system-name fallback be tested.
            string dataDir = Path.Combine(_tempDir, "config", "data");
            Directory.CreateDirectory(dataDir);
            // LoadDicResource format: "<hexId>=<name>" lines.
            File.WriteAllText(Path.Combine(dataDir, "textid_ALL.txt"), "200=SystemName200\r\n");
        }

        public void Dispose()
        {
            CoreState.BaseDirectory = _prevBaseDir;
            CoreState.ROM = _prevRom;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        // <base>/config/etc/_/textid_.txt — the file Save writes / the ctor reads.
        string TsvPath() => Path.Combine(_tempDir, "config", "etc", "_", "textid_.txt");

        [Fact]
        public void Update_SetsComment_GetNameReturnsIt()
        {
            var cache = new TextIDCacheCore();
            cache.Update(0x100, "hello");
            Assert.Equal("hello", cache.GetName(0x100));
        }

        [Fact]
        public void Update_OverwritesExistingComment()
        {
            var cache = new TextIDCacheCore();
            cache.Update(0x100, "x");
            cache.Update(0x100, "y");
            Assert.Equal("y", cache.GetName(0x100));
        }

        [Fact]
        public void Update_EmptyComment_RemovesEntry()
        {
            var cache = new TextIDCacheCore();
            cache.Update(0x100, "x");
            cache.Update(0x100, "");
            Assert.Equal("", cache.GetName(0x100));
        }

        [Fact]
        public void GetName_UnknownId_ReturnsEmpty()
        {
            var cache = new TextIDCacheCore();
            Assert.Equal("", cache.GetName(0x999));
        }

        [Fact]
        public void GetName_FallsBackToSystemName()
        {
            // 0x200 has no user comment but is a shipped system name (seeded in ctor).
            var cache = new TextIDCacheCore();
            Assert.Equal("SystemName200", cache.GetName(0x200));
        }

        [Fact]
        public void GetName_UserCommentOverridesSystemName()
        {
            // The user comment takes precedence over the shipped system name.
            var cache = new TextIDCacheCore();
            cache.Update(0x200, "UserOverride");
            Assert.Equal("UserOverride", cache.GetName(0x200));
        }

        [Fact]
        public void Save_WritesTsv_AndReloadRoundTrips()
        {
            // Write a comment + Save.
            var cache = new TextIDCacheCore();
            cache.Update(0x123, "round trip");
            cache.Save(RomBase);

            Assert.True(File.Exists(TsvPath()), "Save should have written the user TSV");

            // A fresh cache (same BaseDirectory/ROM) re-loads the persisted comment.
            var reloaded = new TextIDCacheCore();
            Assert.Equal("round trip", reloaded.GetName(0x123));
        }

        [Fact]
        public void Save_EmptyCache_NoExistingFile_WritesNothing()
        {
            // An empty cache with no prior TSV: Save creates nothing (and the
            // delete branch is a harmless no-op since there is no file to remove).
            var cache = new TextIDCacheCore();
            cache.Save(RomBase);
            Assert.False(File.Exists(TsvPath()), "Empty-cache Save must not create a TSV");
        }

        [Fact]
        public void Save_AfterClearingLastEntry_DeletesTsv()
        {
            // #1028 Slice A review fix (PR #1104): the immediate-save path must
            // persist REMOVAL. Update(id,"x") -> Save -> file exists; then
            // Update(id,"") (empties the cache) -> Save -> file is DELETED, and a
            // fresh cache over the same base reads "" for the id. This is an
            // INTENTIONAL divergence from WinForms EtcCacheTextID.Save (no-op on
            // empty) — without it the cleared comment reappears after reload.
            var cache = new TextIDCacheCore();
            cache.Update(0x123, "x");
            cache.Save(RomBase);
            Assert.True(File.Exists(TsvPath()), "Save should have written the user TSV");

            cache.Update(0x123, ""); // clears the only entry -> cache now empty
            cache.Save(RomBase);
            Assert.False(File.Exists(TsvPath()), "Emptied-cache Save must DELETE the per-ROM TSV");

            // A fresh cache over the same base must NOT resurrect the cleared comment.
            var reloaded = new TextIDCacheCore();
            Assert.Equal("", reloaded.GetName(0x123));
        }
    }
}
