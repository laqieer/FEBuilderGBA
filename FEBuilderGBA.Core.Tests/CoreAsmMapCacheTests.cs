// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for CoreAsmMapCache (#1035) — the production IAsmMapCache that
// backs the Unit/Class/Item "[HardCoding]" warning hyperlink.
//
// Verifies the lazy-invalidation contract:
//   - ClearCache() invalidates so the next read re-scans (per-ROM reload clears
//     stale flags).
//   - the scan runs lazily on the first IsHardCode* read, exactly once until the
//     next ClearCache().
//   - IsHardCode* is bounds-safe for ids 0 and 255 ((byte) cast in WF parity).
using System;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class CoreAsmMapCacheTests
    {
        static ROM MakeFe8uRom(uint idAddr, byte idValue)
        {
            var data = new byte[0x1000000];
            data[idAddr] = idValue;
            var rom = new ROM();
            Assert.True(rom.LoadLow("hc-cache-fe8u.gba", data, "BE8E01"));
            return rom;
        }

        static (string root, string verDir) MakeTempPatchDir()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "fe_hc_cache_" + Guid.NewGuid().ToString("N"));
            string verDir = Path.Combine(root, "config", "patch2", "FE8U");
            Directory.CreateDirectory(verDir);
            return (root, verDir);
        }

        static void WriteUnitPatch(string verDir, string name, uint addr, string addrType = "UNIT")
        {
            File.WriteAllLines(Path.Combine(verDir, "PATCH_" + name + ".txt"), new[]
            {
                "TYPE=ADDR",
                "ADDRESS=0x" + addr.ToString("X"),
                "ADDRESS_TYPE=" + addrType,
            });
        }

        [Fact]
        public void LazyScanOnFirstRead_FlagsSeededUnit()
        {
            var rom = MakeFe8uRom(0x2000, 7);
            var (root, verDir) = MakeTempPatchDir();
            string savedBase = CoreState.BaseDirectory;
            string savedLang = CoreState.Language;
            try
            {
                CoreState.BaseDirectory = root;
                CoreState.Language = "en";
                WriteUnitPatch(verDir, "U", 0x2000);

                var cache = new CoreAsmMapCache(rom);
                Assert.True(cache.IsHardCodeUnit(7));
                Assert.False(cache.IsHardCodeUnit(8));
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        [Fact]
        public void LazyScanOnce_NewPatchNotSeenUntilClearCache()
        {
            var rom = MakeFe8uRom(0x2000, 7);
            var (root, verDir) = MakeTempPatchDir();
            string savedBase = CoreState.BaseDirectory;
            string savedLang = CoreState.Language;
            try
            {
                CoreState.BaseDirectory = root;
                CoreState.Language = "en";
                WriteUnitPatch(verDir, "U1", 0x2000); // -> unit 7

                var cache = new CoreAsmMapCache(rom);
                Assert.True(cache.IsHardCodeUnit(7)); // first read scans

                // Add a SECOND patch + set its id byte. Without ClearCache the
                // cache must NOT rescan, so unit 9 stays unflagged.
                rom.Data[0x2100] = 9;
                WriteUnitPatch(verDir, "U2", 0x2100); // -> unit 9 (after rescan)
                Assert.False(cache.IsHardCodeUnit(9)); // still cached

                // ClearCache invalidates -> next read rescans and sees both.
                cache.ClearCache();
                Assert.True(cache.IsHardCodeUnit(9));
                Assert.True(cache.IsHardCodeUnit(7));
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        [Fact]
        public void ClearCache_ReloadClearsStaleFlags()
        {
            var rom = MakeFe8uRom(0x2000, 7);
            var (root, verDir) = MakeTempPatchDir();
            string savedBase = CoreState.BaseDirectory;
            string savedLang = CoreState.Language;
            try
            {
                CoreState.BaseDirectory = root;
                CoreState.Language = "en";
                WriteUnitPatch(verDir, "U1", 0x2000); // -> unit 7

                var cache = new CoreAsmMapCache(rom);
                Assert.True(cache.IsHardCodeUnit(7));

                // Remove the patch + clear the id byte, then ClearCache. The
                // stale unit-7 flag must be gone after the rescan.
                File.Delete(Path.Combine(verDir, "PATCH_U1.txt"));
                rom.Data[0x2000] = 0;
                cache.ClearCache();
                Assert.False(cache.IsHardCodeUnit(7));
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        [Fact]
        public void ItemCoverage_AllThreeArrays()
        {
            // Seed a UNIT, a CLASS, and an ITEM patch and confirm all three
            // IsHardCode* readers reflect them.
            var rom = new ROM();
            var data = new byte[0x1000000];
            data[0x2000] = 0x10; // unit
            data[0x2400] = 0x20; // class
            data[0x2800] = 0x30; // item
            rom.LoadLow("hc-cov.gba", data, "BE8E01");

            var (root, verDir) = MakeTempPatchDir();
            string savedBase = CoreState.BaseDirectory;
            string savedLang = CoreState.Language;
            try
            {
                CoreState.BaseDirectory = root;
                CoreState.Language = "en";
                WriteUnitPatch(verDir, "U", 0x2000, "UNIT");
                WriteUnitPatch(verDir, "C", 0x2400, "CLASS");
                WriteUnitPatch(verDir, "I", 0x2800, "ITEM");

                var cache = new CoreAsmMapCache(rom);
                Assert.True(cache.IsHardCodeUnit(0x10));
                Assert.True(cache.IsHardCodeClass(0x20));
                Assert.True(cache.IsHardCodeItem(0x30));
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        [Fact]
        public void BoundsSafe_Id0_And_255()
        {
            // No patches -> all false, but the (byte) cast must not throw for the
            // edge ids 0 and 255 (or 256 wrapping to 0).
            var rom = MakeFe8uRom(0x2000, 7);
            var (root, _) = MakeTempPatchDir();
            string savedBase = CoreState.BaseDirectory;
            string savedLang = CoreState.Language;
            try
            {
                CoreState.BaseDirectory = root;
                CoreState.Language = "en";
                var cache = new CoreAsmMapCache(rom);
                Assert.False(cache.IsHardCodeUnit(0));
                Assert.False(cache.IsHardCodeUnit(255));
                Assert.False(cache.IsHardCodeClass(0));
                Assert.False(cache.IsHardCodeClass(255));
                Assert.False(cache.IsHardCodeItem(0));
                Assert.False(cache.IsHardCodeItem(255));
                Assert.False(cache.IsHardCodeUnit(256)); // wraps to 0 via (byte)
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        [Fact]
        public void ClearCacheOnly_DoesNotEagerScan()
        {
            // ClearCache must not scan eagerly — verified indirectly: a fresh
            // cache that is ClearCache()'d (without any read) and then read still
            // returns the correct seeded result, proving the scan happens on read.
            var rom = MakeFe8uRom(0x2000, 0x2A);
            var (root, verDir) = MakeTempPatchDir();
            string savedBase = CoreState.BaseDirectory;
            string savedLang = CoreState.Language;
            try
            {
                CoreState.BaseDirectory = root;
                CoreState.Language = "en";
                var cache = new CoreAsmMapCache(rom);
                cache.ClearCache(); // no scan yet
                WriteUnitPatch(verDir, "U", 0x2000); // patch added AFTER ClearCache
                // First actual read scans and must see the patch.
                Assert.True(cache.IsHardCodeUnit(0x2A));
            }
            finally
            {
                CoreState.BaseDirectory = savedBase;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
