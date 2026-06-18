using System;
using System.IO;
using System.Linq;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="SaveDataCollectorCore"/> (#1235 — the Avalonia
    /// Problem-Report tool's GUI-free emulator save / backup collector). Asserts:
    ///  - <c>.sav</c> / <c>.emulator*.s*</c> save-state files next to the ROM are
    ///    discovered and copied into the temp dir;
    ///  - the space-&gt;underscore base-name fallback some emulators use is honored;
    ///  - no$gba's <c>&lt;emulatorDir&gt;/BATTERY/&lt;rom&gt;.sav</c> is collected when
    ///    no sibling <c>.sav</c> exists;
    ///  - the UPS-delta helper round-trips (make from a clean ROM → apply → equals
    ///    the current ROM bytes);
    ///  - the never-throws contract on missing inputs.
    /// </summary>
    public class SaveDataCollectorCoreTests
    {
        static string MakeTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "febuilder_savsrch_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* best-effort */ }
        }

        [Fact]
        public void CollectSaveData_FindsSiblingSavAndEmulatorStates()
        {
            string romDir = MakeTempDir();
            string tempDir = MakeTempDir();
            try
            {
                string romPath = Path.Combine(romDir, "MyHack.gba");
                File.WriteAllBytes(romPath, new byte[] { 1, 2, 3 });

                // Sibling save-state files the WF PickupSaveData loop discovers.
                File.WriteAllBytes(Path.Combine(romDir, "MyHack.sav"), new byte[] { 9 });
                File.WriteAllBytes(Path.Combine(romDir, "MyHack.emulator.sav"), new byte[] { 8 });
                File.WriteAllBytes(Path.Combine(romDir, "MyHack.ss1"), new byte[] { 7 });
                File.WriteAllBytes(Path.Combine(romDir, "MyHack.emulator1.sgm"), new byte[] { 6 });
                // A non-matching file that must NOT be collected.
                File.WriteAllBytes(Path.Combine(romDir, "Unrelated.sav"), new byte[] { 0 });

                var collected = SaveDataCollectorCore.CollectSaveData(romPath, "", tempDir);

                Assert.Contains("MyHack.sav", collected);
                Assert.Contains("MyHack.emulator.sav", collected);
                Assert.Contains("MyHack.ss1", collected);
                Assert.Contains("MyHack.emulator1.sgm", collected);
                Assert.DoesNotContain("Unrelated.sav", collected);

                // Each collected name actually landed in the temp dir.
                foreach (string name in collected)
                {
                    Assert.True(File.Exists(Path.Combine(tempDir, name)), name + " should be copied");
                }
            }
            finally
            {
                TryDeleteDir(romDir);
                TryDeleteDir(tempDir);
            }
        }

        [Fact]
        public void CollectSaveData_SpaceToUnderscoreFallback()
        {
            string romDir = MakeTempDir();
            string tempDir = MakeTempDir();
            try
            {
                // ROM name has a space; the emulator wrote the save with an underscore.
                string romPath = Path.Combine(romDir, "My Hack.gba");
                File.WriteAllBytes(romPath, new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(romDir, "My_Hack.sav"), new byte[] { 9 });

                var collected = SaveDataCollectorCore.CollectSaveData(romPath, "", tempDir);

                Assert.Contains("My_Hack.sav", collected);
                Assert.True(File.Exists(Path.Combine(tempDir, "My_Hack.sav")));
            }
            finally
            {
                TryDeleteDir(romDir);
                TryDeleteDir(tempDir);
            }
        }

        [Fact]
        public void CollectSaveData_NoDollarGba_BatteryDir()
        {
            string romDir = MakeTempDir();
            string emuDir = MakeTempDir();
            string tempDir = MakeTempDir();
            try
            {
                // No sibling .sav next to the ROM — must fall through to BATTERY/.
                string romPath = Path.Combine(romDir, "MyHack.gba");
                File.WriteAllBytes(romPath, new byte[] { 1 });

                // emulatorConfigDir is a PATH TO THE EMULATOR EXE; BATTERY sits next to it.
                string emuExe = Path.Combine(emuDir, "NO$GBA.EXE");
                File.WriteAllBytes(emuExe, new byte[] { 0 });
                string batteryDir = Path.Combine(emuDir, "BATTERY");
                Directory.CreateDirectory(batteryDir);
                File.WriteAllBytes(Path.Combine(batteryDir, "MyHack.sav"), new byte[] { 5 });

                var collected = SaveDataCollectorCore.CollectSaveData(romPath, emuExe, tempDir);

                Assert.Contains("MyHack.sav", collected);
                Assert.True(File.Exists(Path.Combine(tempDir, "MyHack.sav")));
            }
            finally
            {
                TryDeleteDir(romDir);
                TryDeleteDir(emuDir);
                TryDeleteDir(tempDir);
            }
        }

        [Fact]
        public void CollectSaveData_NoSaves_ReturnsEmpty_NoThrow()
        {
            string romDir = MakeTempDir();
            string tempDir = MakeTempDir();
            try
            {
                string romPath = Path.Combine(romDir, "MyHack.gba");
                File.WriteAllBytes(romPath, new byte[] { 1 });

                var collected = SaveDataCollectorCore.CollectSaveData(romPath, "", tempDir);
                Assert.Empty(collected);
            }
            finally
            {
                TryDeleteDir(romDir);
                TryDeleteDir(tempDir);
            }
        }

        [Fact]
        public void PickupOneFile_CopiesExplicitPath()
        {
            string srcDir = MakeTempDir();
            string tempDir = MakeTempDir();
            try
            {
                string src = Path.Combine(srcDir, "picked.sav");
                File.WriteAllBytes(src, new byte[] { 1, 2, 3, 4 });

                string name = SaveDataCollectorCore.PickupOneFile(tempDir, src);

                Assert.Equal("picked.sav", name);
                Assert.True(File.Exists(Path.Combine(tempDir, "picked.sav")));

                // Missing file -> null, no throw.
                Assert.Null(SaveDataCollectorCore.PickupOneFile(tempDir, Path.Combine(srcDir, "nope.sav")));
            }
            finally
            {
                TryDeleteDir(srcDir);
                TryDeleteDir(tempDir);
            }
        }

        [Fact]
        public void MakeBackupUps_RoundTrips_CleanToCurrent()
        {
            string cleanDir = MakeTempDir();
            string tempDir = MakeTempDir();
            try
            {
                // A "clean" ROM and a "current" (modified) ROM of equal length.
                byte[] clean = new byte[4096];
                byte[] current = new byte[4096];
                var rng = new Random(1235);
                rng.NextBytes(clean);
                Array.Copy(clean, current, clean.Length);
                // Mutate a handful of bytes so the delta is non-trivial.
                current[10] ^= 0xFF;
                current[100] ^= 0x0F;
                current[4095] ^= 0xAA;

                string cleanPath = Path.Combine(cleanDir, "Clean.gba");
                File.WriteAllBytes(cleanPath, clean);

                string upsName = SaveDataCollectorCore.MakeBackupUps(cleanPath, current, tempDir);

                Assert.Equal("Clean.ups", upsName);
                string upsPath = Path.Combine(tempDir, upsName);
                Assert.True(File.Exists(upsPath));

                // Apply the delta to the clean ROM => must equal the current ROM.
                byte[] patch = File.ReadAllBytes(upsPath);
                byte[] applied = UPSUtilCore.ApplyUPS(clean, patch, out string err);
                Assert.True(string.IsNullOrEmpty(err), "apply error: " + err);
                Assert.NotNull(applied);
                Assert.Equal(current, applied);
            }
            finally
            {
                TryDeleteDir(cleanDir);
                TryDeleteDir(tempDir);
            }
        }

        [Fact]
        public void MakeBackupUps_MissingClean_ReturnsNull_NoThrow()
        {
            string tempDir = MakeTempDir();
            try
            {
                string name = SaveDataCollectorCore.MakeBackupUps(
                    Path.Combine(tempDir, "does-not-exist.gba"), new byte[] { 1, 2 }, tempDir);
                Assert.Null(name);
            }
            finally
            {
                TryDeleteDir(tempDir);
            }
        }
    }
}
