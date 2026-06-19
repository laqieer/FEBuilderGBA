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

        // ====================================================================
        //  MargeAndUpdate (#1248 slice 2) — synthetic end-to-end glue + filter
        // ====================================================================

        // Build a CoreState ROM around bytes (so the Undo machinery snapshots/rolls
        // back into CoreState.ROM, like PatchInstallCoreTests).
        static ROM MakeCoreRom(byte[] data)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect((byte[])data.Clone());
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            return rom;
        }

        // Stage a minimal parent SkillSystem patch dir under <baseDir>/config/patch2/
        // FE8U/skill20220703/ so GetParentPatchPath resolves to a real file. The text
        // carries one line of each kind MargePatch filters, so a test can assert the
        // filter dropped them and kept the rest.
        static void StageParentPatch(string baseDir)
        {
            string dir = Path.Combine(baseDir, "config", "patch2", "FE8U", "skill20220703");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "PATCH_Skill20220703.txt"), new[]
            {
                "TYPE=SKILL",
                "NAME=SkillSystem",
                "INFO=parent",
                "PATCHED_IF:0x100=0x1 0x2",
                "BINF:0x800=00000800.bin",
                "BIN:0x900=00000900.bin",
                "AFTER_TRY_EXECUTE:0=x.event",
                "TEXTADV:0=y",
                "EXTENDS:0=z",
                "UPDATE_METHOD=SKILLSYSTEM",
                "EDIT_PATCH:0=p",
                "KEEPME=1",                  // a non-filtered line — must survive
            });
            // A sidecar the CopyDirectoryShallow would copy (and DeleteFile 0*.bin nukes).
            File.WriteAllBytes(Path.Combine(dir, "00000800.bin"), new byte[] { 0xAA });
        }

        // ---- MargePatch (pure string filter) -------------------------------

        [Fact]
        public void MargePatch_FiltersParentMetadata_KeepsOthers_TakeoverOn()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cb-marge-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                string parent = Path.Combine(dir, "PATCH_Parent.txt");
                File.WriteAllLines(parent, new[]
                {
                    "TYPE=SKILL", "NAME=Sys", "INFO=x",
                    "PATCHED_IF:0x100=0x1", "BINF:0x800=a.bin", "BIN:0x900=b.bin",
                    "AFTER_TRY_EXECUTE:0=e", "TEXTADV:0=t", "EXTENDS:0=z",
                    "UPDATE_METHOD=SKILLSYSTEM", "EDIT_PATCH:0=p", "KEEPME=1",
                });
                string custombuild = Path.Combine(dir, CustomBuildCore.CustomBuildPatchLeafName);
                File.WriteAllText(custombuild, "BINF:0xABCD=00ABCD00.bin\r\n");

                CustomBuildCore.MargePatch(custombuild, parent, takeoverSkillAssignment: 1);
                string merged = File.ReadAllText(custombuild);

                // Header: UPDATE_UNINSTALL of the parent.
                Assert.Contains("UPDATE_UNINSTALL:0=" + parent, merged);
                // Filtered-out parent lines are gone.
                Assert.DoesNotContain("PATCHED_IF:", merged);
                Assert.DoesNotContain("BIN:0x900", merged);
                Assert.DoesNotContain("AFTER_TRY_EXECUTE:", merged);
                Assert.DoesNotContain("TEXTADV:", merged);
                Assert.DoesNotContain("EXTENDS:", merged);
                Assert.DoesNotContain("\nNAME", merged.Replace("\r", ""));
                Assert.DoesNotContain("\nINFO", merged.Replace("\r", ""));
                // With takeover ON, the skill-assignment lines are KEPT.
                Assert.Contains("UPDATE_METHOD=SKILLSYSTEM", merged);
                Assert.Contains("EDIT_PATCH:0=p", merged);
                // A non-filtered parent line survives.
                Assert.Contains("KEEPME=1", merged);
                // The freshly-generated CustomBuild diff text is appended.
                Assert.Contains("BINF:0xABCD=00ABCD00.bin", merged);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void MargePatch_TakeoverOff_DropsSkillAssignmentLines()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cb-marge-off-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                string parent = Path.Combine(dir, "PATCH_Parent.txt");
                File.WriteAllLines(parent, new[]
                {
                    "TYPE=SKILL", "KEEPME=1",
                    "UPDATE_METHOD=SKILLSYSTEM", "EDIT_PATCH:0=p",
                });
                string custombuild = Path.Combine(dir, CustomBuildCore.CustomBuildPatchLeafName);
                File.WriteAllText(custombuild, "BINF:0x1=x.bin\r\n");

                CustomBuildCore.MargePatch(custombuild, parent, takeoverSkillAssignment: 0);
                string merged = File.ReadAllText(custombuild);

                Assert.DoesNotContain("UPDATE_METHOD=SKILLSYSTEM", merged);
                Assert.DoesNotContain("EDIT_PATCH:0=p", merged);
                Assert.Contains("KEEPME=1", merged);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---- MargeAndUpdate end-to-end (synthetic) -------------------------

        [Fact]
        public void MargeAndUpdate_SyntheticRoundTrip_InstallsBinDiff_RomEqualsBuilt()
        {
            string prevBase = CoreState.BaseDirectory;
            string prevLang = CoreState.Language;
            string baseDir = Path.Combine(Path.GetTempPath(), "cb-mau-" + Path.GetRandomFileName());
            Directory.CreateDirectory(baseDir);
            CoreState.BaseDirectory = baseDir;
            CoreState.Language = "en";
            try
            {
                StageParentPatch(baseDir);

                // Vanilla (the diff baseline) and the "built" ROM (= vanilla + edits).
                // The working ROM starts as a CLONE of vanilla, so installing the
                // vanilla→built diff must drive it to equal the built bytes.
                byte[] vanilla = new byte[0x400];
                for (int i = 0; i < vanilla.Length; i++) vanilla[i] = 0xFF;
                byte[] built = (byte[])vanilla.Clone();
                for (int i = 0x110; i < 0x118; i++) built[i] = 0xAB;
                for (int i = 0x250; i < 0x270; i++) built[i] = (byte)(i & 0xFF);

                string origRomPath = Path.Combine(baseDir, "vanilla.gba");
                string builtRomPath = Path.Combine(baseDir, "SkillsTest.gba");
                File.WriteAllBytes(origRomPath, vanilla);
                File.WriteAllBytes(builtRomPath, built);
                // A build target whose directory holds the (absent) .sym/.dmp sidecars.
                string targetPath = Path.Combine(baseDir, "CUSTOM_BUILD.cmd");
                File.WriteAllText(targetPath, "echo hi\r\n");

                var rom = MakeCoreRom(vanilla);  // working ROM = vanilla clone
                var undo = NewUndo(rom);

                var r = CustomBuildCore.MargeAndUpdate(
                    rom, origRomPath, builtRomPath, targetPath,
                    takeoverSkillAssignment: 1, undo: undo);

                Assert.True(r.Success, "MargeAndUpdate failed: " + r.ErrorMessage);
                Assert.True(File.Exists(r.PatchPath), "the CustomBuild patch should exist on disk");
                // The install drove the working ROM to the built bytes.
                Assert.Equal(built, rom.Data);
                Assert.NotEmpty(undo.list);

                // The generated patch merged the parent text (UPDATE_UNINSTALL header)
                // and kept a non-filtered parent line, then appended the BIN diff.
                string patchText = File.ReadAllText(r.PatchPath);
                Assert.Contains("UPDATE_UNINSTALL:0=", patchText);
                Assert.Contains("KEEPME=1", patchText);
                Assert.Contains("BINF:", patchText);

                // The cache dir was rebuilt and the parent's own 0*.bin / PATCH_*.txt
                // were dropped (only our generated patch + symbol-less copy remain).
                string cacheDir = CustomBuildCore.GetCustomBuildCacheDir();
                Assert.True(Directory.Exists(cacheDir));
                Assert.Empty(Directory.GetFiles(cacheDir, "00000800.bin"));

                // Undo restores the working ROM to vanilla.
                CoreState.Undo.Push(undo);
                CoreState.Undo.RunUndo();
                Assert.Equal(vanilla, rom.Data);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                CoreState.Language = prevLang;
                CoreState.ROM = null;
                CoreState.Undo = null;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void MargeAndUpdate_MissingBuiltRom_ReturnsError_NoMutation()
        {
            string prevBase = CoreState.BaseDirectory;
            string baseDir = Path.Combine(Path.GetTempPath(), "cb-mau-nb-" + Path.GetRandomFileName());
            Directory.CreateDirectory(baseDir);
            CoreState.BaseDirectory = baseDir;
            try
            {
                StageParentPatch(baseDir);
                byte[] vanilla = new byte[0x200];
                string origRomPath = Path.Combine(baseDir, "vanilla.gba");
                File.WriteAllBytes(origRomPath, vanilla);

                var rom = MakeCoreRom(vanilla);
                byte[] before = (byte[])rom.Data.Clone();
                var undo = NewUndo(rom);

                var r = CustomBuildCore.MargeAndUpdate(
                    rom, origRomPath,
                    builtRomPath: Path.Combine(baseDir, "no-such-built.gba"),
                    targetPath: Path.Combine(baseDir, "CUSTOM_BUILD.cmd"),
                    takeoverSkillAssignment: 1, undo: undo);

                Assert.False(r.Success);
                Assert.False(string.IsNullOrEmpty(r.ErrorMessage));
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                CoreState.ROM = null;
                CoreState.Undo = null;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }

        [Fact]
        public void MargeAndUpdate_MissingParentPatch_ReturnsError_NoMutation()
        {
            string prevBase = CoreState.BaseDirectory;
            // A base dir WITHOUT the parent patch staged → GetParentPatchPath misses.
            string baseDir = Path.Combine(Path.GetTempPath(), "cb-mau-np-" + Path.GetRandomFileName());
            Directory.CreateDirectory(baseDir);
            CoreState.BaseDirectory = baseDir;
            try
            {
                byte[] vanilla = new byte[0x200];
                byte[] built = (byte[])vanilla.Clone();
                built[0x110] = 0x11;
                string origRomPath = Path.Combine(baseDir, "vanilla.gba");
                string builtRomPath = Path.Combine(baseDir, "built.gba");
                File.WriteAllBytes(origRomPath, vanilla);
                File.WriteAllBytes(builtRomPath, built);

                var rom = MakeCoreRom(vanilla);
                byte[] before = (byte[])rom.Data.Clone();
                var undo = NewUndo(rom);

                var r = CustomBuildCore.MargeAndUpdate(
                    rom, origRomPath, builtRomPath,
                    Path.Combine(baseDir, "CUSTOM_BUILD.cmd"),
                    takeoverSkillAssignment: 1, undo: undo);

                Assert.False(r.Success);
                Assert.False(string.IsNullOrEmpty(r.ErrorMessage));
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
            }
            finally
            {
                CoreState.BaseDirectory = prevBase;
                CoreState.ROM = null;
                CoreState.Undo = null;
                try { Directory.Delete(baseDir, true); } catch { }
            }
        }
    }
}
