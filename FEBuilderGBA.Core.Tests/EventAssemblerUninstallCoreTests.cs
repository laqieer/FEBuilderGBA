using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="EventAssemblerUninstallCore"/>, the GUI-free in-place
    /// uninstall of an applied Event Assembler patch (#1242, follow-up to #1170).
    ///
    /// Two layers of provability:
    ///  1. Always-runnable, deterministic tests of the revert logic itself —
    ///     validation (not-found / empty / too-small clean ROM → localized error,
    ///     zero mutation) and a hand-built BinMapping round-trip that proves the
    ///     byte-for-byte restore + undo without needing any compiler.
    ///  2. An opportunistic REAL ColorzCore round-trip
    ///     (compile+insert via <see cref="EventAssemblerCompileCore"/> → uninstall with
    ///     the pre-insert bytes as the clean original → assert restoration) that runs
    ///     where the EA toolchain works and SKIPS cleanly otherwise (same gating as the
    ///     existing EventAssemblerCompileCore real-compile test).
    /// </summary>
    [Collection("SharedState")]
    public class EventAssemblerUninstallCoreTests
    {
        static ROM CreateTestRom(int size = 0x200)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            return rom;
        }

        // FE8-detected synthetic ROM (no roms/*.gba dependency) — required for the
        // real EA compile path to get a valid game code. Mirrors the helper in
        // EventAssemblerCompileCoreTests.
        static ROM CreateFE8Rom()
        {
            var data = new byte[0x1000000]; // 16 MB — minimum for FE8U detection
            byte[] code = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            Array.Copy(code, 0, data, 0xAC, code.Length);

            var rom = new ROM();
            bool ok = rom.LoadLow("synthetic-FE8.gba", data, "BE8E01");
            CoreState.ROM = rom;
            return ok ? rom : null;
        }

        static Undo.UndoData NewUndo(ROM rom) => new Undo.UndoData
        {
            time = DateTime.Now,
            name = "test",
            list = new List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        // ---- Validation: never throws, zero mutation on bad input ----------------

        [Fact]
        public void Uninstall_NoRomLoaded_ReturnsError_NoThrow()
        {
            CoreState.ROM = null;
            var result = EventAssemblerUninstallCore.Uninstall("whatever.event", new byte[16], NewUnboundUndo());
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
        }

        [Fact]
        public void Uninstall_MissingEventFile_ReturnsError_NoMutation()
        {
            var rom = CreateTestRom();
            byte[] before = (byte[])rom.Data.Clone();

            var result = EventAssemblerUninstallCore.Uninstall(
                Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid() + ".event"),
                new byte[rom.Data.Length], NewUndo(rom));

            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
            Assert.Equal(before, rom.Data); // untouched
        }

        [Fact]
        public void Uninstall_EmptyCleanRom_ReturnsError_NoMutation()
        {
            // An empty clean ROM is rejected before the trace even runs, so a bare
            // (RomInfo-less) ROM is sufficient and the .event need not be traceable.
            var rom = CreateTestRom();
            string eaFile = WriteTinyOrgEvent(0x1000, "0xAA 0xBB");
            byte[] before = (byte[])rom.Data.Clone();
            try
            {
                var result = EventAssemblerUninstallCore.Uninstall(eaFile, new byte[0], NewUndo(rom));
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
                Assert.Equal(before, rom.Data);
            }
            finally { TryDelete(eaFile); }
        }

        [Fact]
        public void Uninstall_CleanRomTooSmall_ReturnsError_NoMutation()
        {
            // Needs a traceable ORG range, so use a RomInfo-bearing FE8 ROM. The ORG
            // (0x900000) lies past a tiny clean ROM → rejected by the size sanity check.
            var rom = CreateFE8Rom();
            Assert.NotNull(rom);
            string eaFile = WriteTinyOrgEvent(0x900000, "0x11 0x22 0x33 0x44");
            byte[] before = (byte[])rom.Data.Clone();
            try
            {
                var result = EventAssemblerUninstallCore.Uninstall(eaFile, new byte[0x10], NewUndo(rom));
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
                Assert.Equal(before, rom.Data);
            }
            finally { TryDelete(eaFile); }
        }

        // ---- Pure-synthetic revert round-trip (ALWAYS runs, no compiler) ----------
        //
        // Drives UninstallPatchInner directly via the public Uninstall() entry: an EA
        // file with a single ORG produces one length-0 ("unknown") BinMapping at that
        // address; the revert auto-lengths it against the clean ROM and restores those
        // bytes. We craft a known clean vs patched divergence and assert the patched
        // bytes are rewritten to the clean ones, undoably.
        [Fact]
        public void Uninstall_OrgRange_RestoresBytesFromCleanRom_Undoable()
        {
            // Trace needs a RomInfo-bearing ROM. Snapshot its bytes as the "clean"
            // original, then simulate an applied patch on top.
            var rom = CreateFE8Rom();
            Assert.NotNull(rom);

            // Clean ROM = the ROM exactly as it is before we simulate the patch.
            byte[] clean = (byte[])rom.Data.Clone();

            // Simulate an applied patch: 4 changed bytes at a safe high offset.
            uint patchAddr = 0x800000;
            byte[] patched = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            for (int i = 0; i < patched.Length; i++)
                rom.write_u8(patchAddr + (uint)i, patched[i]);

            byte[] beforeRevert = (byte[])rom.Data.Clone();
            Assert.NotEqual(clean[patchAddr], rom.Data[patchAddr]); // guard: they differ

            string eaFile = WriteTinyOrgEvent(patchAddr, null); // ORG only → length-unknown mapping
            var undo = NewUndo(rom);
            try
            {
                var result = EventAssemblerUninstallCore.Uninstall(eaFile, clean, undo);

                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(result.RangeCount >= 1);
                Assert.True(result.BytesReverted >= 4);

                // The patched bytes are now restored to the clean-original bytes.
                Assert.Equal(clean[patchAddr + 0], (byte)rom.u8(patchAddr + 0));
                Assert.Equal(clean[patchAddr + 1], (byte)rom.u8(patchAddr + 1));
                Assert.Equal(clean[patchAddr + 2], (byte)rom.u8(patchAddr + 2));
                Assert.Equal(clean[patchAddr + 3], (byte)rom.u8(patchAddr + 3));

                // The revert was recorded → undoable: rolling back restores the patch.
                Assert.NotEmpty(undo.list);
                CoreState.Undo = new Undo();
                CoreState.Undo.Push(undo);
                CoreState.Undo.RunUndo();
                Assert.Equal(beforeRevert, rom.Data);
            }
            finally { TryDelete(eaFile); CoreState.Undo = null; }
        }

        // ---- Real ColorzCore round-trip (OPPORTUNISTIC — skips if EA unavailable) --
        //
        // Compile+insert a tiny ORG/BYTE .event into an FE8 ROM, snapshot the
        // pre-insert bytes, then uninstall with those pre-insert bytes as the clean
        // original → assert the inserted range is restored. Gated exactly like the
        // existing EventAssemblerCompileCore real-compile test (CI builds ColorzCore.exe
        // but lacks the EA raws, so a compile failure SKIPS rather than fails).
        [SkippableFact]
        public void Uninstall_RealColorzCoreRoundTrip_RestoresInsertedBytes()
        {
            string exe = FindBuiltColorzCore();
            Skip.If(exe == null,
                "ColorzCore.exe not built — skipping the real compile+uninstall round-trip (the deterministic synthetic revert + validation tests still cover the uninstall logic).");

            var rom = CreateFE8Rom();
            Assert.NotNull(rom);
            Assert.Equal("FE8", rom.RomInfo.TitleToFilename);

            // Clean original = the ROM bytes BEFORE any insert.
            byte[] cleanOriginal = (byte[])rom.Data.Clone();

            string eaDir = Path.Combine(Path.GetTempPath(), "febuilder-ea-uninst-" + Path.GetRandomFileName());
            Directory.CreateDirectory(eaDir);
            string eaFile = Path.Combine(eaDir, "tiny.event");
            // ORG must be >= 0x200 so the uninstall parser's U.isSafetyOffset gate
            // accepts it (the EA compiler itself has no such floor, but the trace does).
            File.WriteAllText(eaFile, "ORG 0x1000\r\nBYTE 0xAA 0xBB 0xCC 0xDD\r\n");

            var savedConfig = CoreState.Config;
            var savedUndo = CoreState.Undo;
            CoreState.Config = new Config { ["event_assembler"] = exe };
            CoreState.Undo = new Undo();

            try
            {
                // 1) Compile+insert via the #1170 helper.
                var insertUndo = CoreState.Undo.NewUndoData("rt-insert");
                var compile = EventAssemblerCompileCore.CompileAndInsert(
                    rom, eaFile, EventAssemblerCompileCore.FreeAreaMode.None,
                    insertUndo, SymbolUtil.DebugSymbol.None);

                Skip.IfNot(compile.Success,
                    "real EA compile unavailable in this environment (ColorzCore.exe present but compile failed — likely missing EA raws); skipping the uninstall round-trip. Output: " + compile.ErrorMessage);

                // Guard: the insert actually changed the bytes we will revert.
                Assert.Equal(0xAAu, rom.u8(0x1000));
                Assert.Equal(0xBBu, rom.u8(0x1001));
                Assert.Equal(0xCCu, rom.u8(0x1002));
                Assert.Equal(0xDDu, rom.u8(0x1003));
                Assert.NotEqual(cleanOriginal[0x1000], rom.Data[0x1000]);

                // 2) Uninstall in place using the PRE-insert bytes as the clean original.
                var uninstallUndo = CoreState.Undo.NewUndoData("rt-uninstall");
                var result = EventAssemblerUninstallCore.Uninstall(eaFile, cleanOriginal, uninstallUndo);

                Assert.True(result.Success, result.ErrorMessage);

                // 3) The inserted range is restored to its pre-insert (clean) bytes.
                Assert.Equal(cleanOriginal[0x1000], (byte)rom.u8(0x1000));
                Assert.Equal(cleanOriginal[0x1001], (byte)rom.u8(0x1001));
                Assert.Equal(cleanOriginal[0x1002], (byte)rom.u8(0x1002));
                Assert.Equal(cleanOriginal[0x1003], (byte)rom.u8(0x1003));

                // And the uninstall is itself undoable.
                Assert.NotEmpty(uninstallUndo.list);
            }
            finally
            {
                CoreState.Config = savedConfig;
                CoreState.Undo = savedUndo;
                try { Directory.Delete(eaDir, true); } catch { }
            }
        }

        // ---- EAUtilCore parser coverage (the extracted parse-only port) ----------

        [Fact]
        public void EAUtilCore_ParsesOrgAndLynHook_IntoDataList()
        {
            // ParseORG validates the offset via U.isSafetyOffset(uint), which reads
            // CoreState.ROM.Data.Length (matching the WinForms parser, where Program.ROM
            // is always loaded). Seed a ROM large enough that 0x123456 is a valid offset.
            CreateTestRom(0x200000);

            // A small .event exercising the ORG keyword and the HINT=LYN_HOOK form —
            // both produce ORG-addressed Data entries the trace consumes. The LYN_HOOK
            // hint must sit on a line with real content (ParseLynHook scans the raw
            // line, but a comment-only line is skipped after comment-stripping — exactly
            // as in real EA output where the hint trails a directive).
            string path = Path.Combine(Path.GetTempPath(), "ea-parse-" + Path.GetRandomFileName() + ".event");
            File.WriteAllText(path,
                "ORG 0x123456\r\n" +
                "BYTE 0x00 0x01 // HINT=LYN_HOOK=0x8000\r\n");
            try
            {
                var ea = new EAUtilCore(path);
                Assert.NotNull(ea.DataList);

                bool hasOrgAt123456 = false;
                bool hasHookAt8000 = false;
                foreach (var d in ea.DataList)
                {
                    if (d.DataType == EAUtilCore.DataEnum.ORG)
                    {
                        // ORG 0x123456 → U.toOffset(0x123456) == 0x123456 (already an offset).
                        if (d.ORGAddr == 0x123456u) hasOrgAt123456 = true;
                        if (d.ORGAddr == 0x8000u) hasHookAt8000 = true;
                    }
                }
                Assert.True(hasOrgAt123456, "ORG 0x123456 was not parsed into an ORG Data entry.");
                Assert.True(hasHookAt8000, "HINT=LYN_HOOK=0x8000 was not parsed into an ORG Data entry.");
            }
            finally { TryDelete(path); }
        }

        [Fact]
        public void EAUtilCore_IsFBGTemp_DetectsTempWrapper()
        {
            Assert.True(EAUtilCore.IsFBGTemp("_FBG_Temp_123.event"));
            Assert.False(EAUtilCore.IsFBGTemp("real.event"));
        }

        // ---- helpers -------------------------------------------------------------

        // An undo not bound to a specific ROM (for the no-ROM-loaded validation test).
        static Undo.UndoData NewUnboundUndo() => new Undo.UndoData
        {
            time = DateTime.Now,
            name = "test",
            list = new List<Undo.UndoPostion>(),
            filesize = 0,
        };

        // Write a minimal .event: an ORG line, optionally followed by a BYTE line.
        static string WriteTinyOrgEvent(uint org, string byteArgs)
        {
            string path = Path.Combine(Path.GetTempPath(), "ea-uninst-" + Path.GetRandomFileName() + ".event");
            string body = "ORG 0x" + org.ToString("X") + "\r\n";
            if (!string.IsNullOrEmpty(byteArgs))
                body += "BYTE " + byteArgs + "\r\n";
            File.WriteAllText(path, body);
            return path;
        }

        static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        static string FindBuiltColorzCore()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && dir != null; i++)
            {
                // ColorzCore.csproj sets BaseOutputPath=bin/Core, so a `-c Core` build
                // lands in bin/Core/Core/net6.0/, while a `-c Release|Debug` build lands
                // in bin/Core/{config}/net6.0/. Check all layouts so the round-trip runs
                // wherever the submodule was built.
                foreach (string config in new[] { "Core", "Release", "Debug" })
                {
                    foreach (string name in new[] { "ColorzCore.exe", "ColorzCore" })
                    {
                        string p = Path.Combine(dir, "tools", "ColorzCore", "ColorzCore",
                            "bin", "Core", config, "net6.0", name);
                        if (File.Exists(p)) return p;

                        // Standard output path (no BaseOutputPath override).
                        p = Path.Combine(dir, "tools", "ColorzCore", "ColorzCore",
                            "bin", config, "net6.0", name);
                        if (File.Exists(p)) return p;
                    }
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
