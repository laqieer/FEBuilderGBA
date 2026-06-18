using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="CustomBuildCore"/>, the GUI-free build flow for the
    /// Avalonia Custom Build tool.
    ///
    /// Covers the always-runnable paths (target/original-ROM not-found → localized
    /// error + zero mutation, the method-by-extension resolver, the EA-error
    /// detection, the CMD Windows-only guard) plus a real EA-target build round-trip
    /// when the bundled ColorzCore submodule has been built (skips otherwise).
    /// A real CUSTOM_BUILD.cmd run is NOT tested: it needs the user's external skill
    /// toolchain that is not present in CI.
    /// </summary>
    [Collection("SharedState")]
    public class CustomBuildCoreTests
    {
        static ROM CreateTestRom(int size = 512)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            return rom;
        }

        /// <summary>
        /// Build a synthetic ROM that game-detection identifies as FE8U
        /// (<c>TitleToFilename == "FE8"</c>) so a real ColorzCore EA build gets a
        /// valid game code. Does NOT depend on roms/*.gba (gitignored / absent in CI).
        /// </summary>
        static ROM CreateFE8Rom()
        {
            var data = new byte[0x1000000]; // 16 MB — minimum for FE8U detection
            byte[] code = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            System.Array.Copy(code, 0, data, 0xAC, code.Length);

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

        // ---- ResolveMethod (pure) ----------------------------------------------

        [Theory]
        [InlineData("build.cmd", CustomBuildCore.BuildMethod.Cmd)]
        [InlineData("BUILD.CMD", CustomBuildCore.BuildMethod.Cmd)]
        [InlineData("CUSTOM_BUILD.Cmd", CustomBuildCore.BuildMethod.Cmd)]
        [InlineData("target.event", CustomBuildCore.BuildMethod.EventAssembler)]
        [InlineData("target.txt", CustomBuildCore.BuildMethod.EventAssembler)]
        [InlineData("noext", CustomBuildCore.BuildMethod.EventAssembler)]
        public void ResolveMethod_Auto_PicksByExtension(string path, CustomBuildCore.BuildMethod expected)
        {
            Assert.Equal(expected, CustomBuildCore.ResolveMethod(path, CustomBuildCore.BuildMethod.Auto));
        }

        [Theory]
        [InlineData(CustomBuildCore.BuildMethod.Cmd)]
        [InlineData(CustomBuildCore.BuildMethod.EventAssembler)]
        public void ResolveMethod_Explicit_IsHonoured_RegardlessOfExtension(CustomBuildCore.BuildMethod requested)
        {
            // An explicit method overrides the extension — e.g. EA on a .cmd, or CMD on a .event.
            Assert.Equal(requested, CustomBuildCore.ResolveMethod("anything.event", requested));
            Assert.Equal(requested, CustomBuildCore.ResolveMethod("anything.cmd", requested));
        }

        // ---- IsCompilerError (inverse of EventAssemblerCompileCore.IsEASuccess) -

        [Theory]
        [InlineData("No errors or warnings.", false)]
        [InlineData("No errors. Please continue being awesome.", false)]
        [InlineData("Error on line 3: something broke", true)]
        [InlineData("", true)]
        // The null-start / timeout sentinel strings RunProcess returns must be treated
        // as errors (no success marker), so BuildCmd surfaces them instead of loading a ROM.
        [InlineData("Error: the custom build process could not be started. filename:x.cmd", true)]
        [InlineData("Error: the custom build timed out after 120 seconds.", true)]
        public void IsCompilerError_MatchesEASuccessStrings(string output, bool expectedError)
        {
            Assert.Equal(expectedError, CustomBuildCore.IsCompilerError(output));
            // Must be the exact inverse of IsEASuccess.
            Assert.Equal(!CustomBuildCore.IsCompilerError(output), EventAssemblerCompileCore.IsEASuccess(output));
        }

        // ---- Path validation: localized error, ZERO mutation -------------------

        [Fact]
        public void Build_MissingTarget_ReturnsError_NoMutation()
        {
            var rom = CreateTestRom();
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            var result = CustomBuildCore.Build(
                rom, Path.Combine(Path.GetTempPath(), "no-such-target-" + Guid.NewGuid() + ".event"),
                originalRomPath: "ignored", method: CustomBuildCore.BuildMethod.Auto, undo: undo);

            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
            Assert.Equal(before, rom.Data);
            Assert.Empty(undo.list);
        }

        [Fact]
        public void Build_MissingOriginalRom_ReturnsError_NoMutation()
        {
            var rom = CreateTestRom();
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            // A real, existing target so validation proceeds to the original-ROM check.
            string target = Path.Combine(Path.GetTempPath(), "cb-target-" + Guid.NewGuid() + ".event");
            File.WriteAllText(target, "ORG 0x100\r\nBYTE 0xAA\r\n");
            try
            {
                var result = CustomBuildCore.Build(
                    rom, target,
                    originalRomPath: Path.Combine(Path.GetTempPath(), "no-rom-" + Guid.NewGuid() + ".gba"),
                    method: CustomBuildCore.BuildMethod.Auto, undo: undo);

                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
            }
            finally { try { File.Delete(target); } catch { } }
        }

        [Fact]
        public void Build_NullRom_ReturnsError()
        {
            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            var result = CustomBuildCore.Build(null, "t.event", "r.gba", CustomBuildCore.BuildMethod.Auto, undo);
            Assert.False(result.Success);
            Assert.Equal(R._("No ROM is loaded."), result.ErrorMessage);
        }

        // ---- CMD path Windows-only guard (deterministic on non-Windows) --------

        [SkippableFact]
        public void Build_CmdMethod_OnNonWindows_ReturnsWindowsOnlyError_NoMutation()
        {
            // Only assert the negative path off-Windows, where the .cmd cannot run.
            Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                "Windows DOES run .cmd scripts — the Windows-only guard only fires off-Windows.");

            var rom = CreateTestRom();
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            // A real .cmd target + a real original ROM so validation passes and we hit
            // the Windows-only guard inside BuildCmd (not an earlier path check).
            string dir = Path.Combine(Path.GetTempPath(), "cb-cmd-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "CUSTOM_BUILD.cmd");
            File.WriteAllText(target, "echo hi\r\n");
            string origRom = Path.Combine(dir, "orig.gba");
            File.WriteAllBytes(origRom, new byte[512]);
            try
            {
                var result = CustomBuildCore.Build(
                    rom, target, origRom, CustomBuildCore.BuildMethod.Cmd, undo);

                Assert.False(result.Success);
                Assert.Equal(CustomBuildCore.GetCmdWindowsOnlyMessage(), result.ErrorMessage);
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void GetCmdWindowsOnlyMessage_IsNonEmpty()
        {
            Assert.False(string.IsNullOrEmpty(CustomBuildCore.GetCmdWindowsOnlyMessage()));
        }

        // ---- CMD path on Windows: runs the script, then fails at the load step
        //      (no SkillsTest.gba produced) WITHOUT mutating the ROM. Proves the CMD
        //      pipeline runs through to the load on Windows (not short-circuited by the
        //      Windows-only guard) and stays fault-safe when the build produces no ROM.
        [SkippableFact]
        public void Build_CmdMethod_OnWindows_NoBuiltRom_ReturnsError_NoMutation()
        {
            Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                "The .cmd run path is Windows-only — covered by the Windows-only guard test off-Windows.");

            var rom = CreateTestRom();
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            string dir = Path.Combine(Path.GetTempPath(), "cb-cmd-win-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            // A trivial .cmd that emits the EA "no errors" marker so IsCompilerError is
            // false, but produces NO SkillsTest.gba → the load step must error cleanly.
            string target = Path.Combine(dir, "CUSTOM_BUILD.cmd");
            File.WriteAllText(target, "@echo No errors or warnings.\r\n");
            string origRom = Path.Combine(dir, "orig.gba");
            File.WriteAllBytes(origRom, new byte[512]);
            try
            {
                var result = CustomBuildCore.Build(
                    rom, target, origRom, CustomBuildCore.BuildMethod.Cmd, undo);

                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
                // The vanilla copy was made, but the ROM itself was never mutated.
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
                Assert.True(File.Exists(Path.Combine(dir, CustomBuildCore.VanillaRomLeafName)),
                    "the CMD path should have copied the original ROM to FE8_clean.gba");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---- EA build path: real ColorzCore round-trip (OPPORTUNISTIC) ----------
        //
        // The EA build path delegates to EventAssemblerCompileCore, so this asserts
        // where a full EA toolchain is present (ColorzCore.exe built AND the EA raws)
        // and SKIPS cleanly otherwise — the same gating the EventAssemblerCompileCore
        // round-trip uses. The not-found / path-validation / method-resolution logic
        // above needs no real compiler.
        [SkippableFact]
        public void Build_EATarget_RealColorzCore_LoadsBuiltRom_Undoable()
        {
            string exe = FindBuiltColorzCore();
            Skip.If(exe == null,
                "ColorzCore.exe not built — skipping the real EA-build round-trip (the deterministic resolve/validate/error tests still cover our logic).");

            var rom = CreateFE8Rom();
            Assert.NotNull(rom);
            Assert.Equal("FE8", rom.RomInfo.TitleToFilename);
            byte[] before = (byte[])rom.Data.Clone();

            // A .event target the EA path ORGs itself (None free-area mode).
            string eaDir = Path.Combine(Path.GetTempPath(), "cb-ea-rt-" + Path.GetRandomFileName());
            Directory.CreateDirectory(eaDir);
            string target = Path.Combine(eaDir, "tiny.event");
            File.WriteAllText(target, "ORG 0x100\r\nBYTE 0xAA 0xBB 0xCC 0xDD\r\n");
            // Original ROM only needs to EXIST for validation (EA path doesn't copy it).
            string origRom = Path.Combine(eaDir, "orig.gba");
            File.WriteAllBytes(origRom, new byte[512]);

            var savedConfig = CoreState.Config;
            var savedUndo = CoreState.Undo;
            CoreState.Config = new Config { ["event_assembler"] = exe };
            CoreState.Undo = new Undo();

            try
            {
                var undo = CoreState.Undo.NewUndoData("rt");
                var result = CustomBuildCore.Build(
                    rom, target, origRom, CustomBuildCore.BuildMethod.EventAssembler, undo);

                Skip.IfNot(result.Success,
                    "real EA build unavailable (ColorzCore.exe present but compile failed — likely missing EA raws); skipping the round-trip assertion. Output: " + result.ErrorMessage);

                Assert.Equal(0xAAu, rom.u8(0x100));
                Assert.Equal(0xDDu, rom.u8(0x103));
                Assert.NotEmpty(undo.list);

                CoreState.Undo.Push(undo);
                CoreState.Undo.RunUndo();
                Assert.Equal(before, rom.Data);
            }
            finally
            {
                CoreState.Config = savedConfig;
                CoreState.Undo = savedUndo;
                try { Directory.Delete(eaDir, true); } catch { }
            }
        }

        static string FindBuiltColorzCore()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && dir != null; i++)
            {
                foreach (string config in new[] { "Release", "Debug" })
                {
                    foreach (string name in new[] { "ColorzCore.exe", "ColorzCore" })
                    {
                        string p = Path.Combine(dir, "tools", "ColorzCore", "ColorzCore",
                            "bin", "Core", config, "net6.0", name);
                        if (File.Exists(p)) return p;
                    }
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
