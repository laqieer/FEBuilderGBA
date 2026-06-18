using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="AsmCompileCore"/>, the GUI-free C/ASM → ELF → lyn → insert
    /// flow behind the Avalonia "Add-via-ASM/C" tool.
    ///
    /// devkitARM is a large external SDK that is NOT built in CI (unlike ColorzCore),
    /// so there is intentionally NO real-compile round-trip here. The deterministic,
    /// always-runnable paths ARE covered: tool-not-found → localized error + zero
    /// mutation, the (pure) argument/glob builders, the hook-register clamp, the
    /// lyn-can't-write-to-ROM guard, and the insert math (write-at-address +
    /// hook-inject) exercised directly with a pre-made binary product.
    /// </summary>
    [Collection("SharedState")]
    public class AsmCompileCoreTests
    {
        static ROM CreateTestRom(int size = 0x400)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            return rom;
        }

        static Undo.UndoData NewUndo(ROM rom) => new Undo.UndoData
        {
            time = DateTime.Now,
            name = "test",
            list = new List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        // ---- Compiler base-name + glob (cross-platform) ------------------------

        [Theory]
        [InlineData(".C", "gcc")]
        [InlineData(".CPP", "g++")]
        [InlineData(".S", "as")]
        [InlineData(".ASM", "as")]
        [InlineData(".TXT", "as")]
        public void CompilerBaseNameForExt_PicksTool(string ext, string expected)
        {
            Assert.Equal(expected, AsmCompileCore.CompilerBaseNameForExt(ext));
        }

        [Theory]
        [InlineData(".C", "gcc")]
        [InlineData(".CPP", "g++")]
        [InlineData(".S", "as")]
        public void CompilerGlobForExt_IsPlatformAware(string ext, string baseName)
        {
            // On Windows the glob carries .exe; on Linux/macOS it does not (else
            // devkitARM resolution silently fails there — Copilot #1/#8).
            string glob = AsmCompileCore.CompilerGlobForExt(ext);
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            Assert.Equal(isWindows ? "*" + baseName + ".exe" : "*" + baseName, glob);
        }

        [Fact]
        public void DevkitArmGlobs_PlatformAware_IncludesExtensionlessFormOffWindows()
        {
            // The non-.exe form MUST be among the globs so Linux/macOS resolution works.
            string[] globs = ToolPathResolver.DevkitArmGlobs("gcc");
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            if (isWindows)
            {
                Assert.Equal(new[] { "*gcc.exe" }, globs);
            }
            else
            {
                Assert.Contains("*gcc", globs);     // extension-less (Linux/macOS) form
                Assert.Contains("*gcc.exe", globs); // cross-build fallback
            }
        }

        // ---- BuildAssembleArgs (pure) -----------------------------------------

        [Fact]
        public void BuildAssembleArgs_Asm_NoCflags()
        {
            string args = AsmCompileCore.BuildAssembleArgs(
                "src.s", "out.elf", ".S", "-c -mthumb -O2", "");

            Assert.Contains("-mthumb-interwork", args);
            Assert.Contains("-o ", args);
            Assert.Contains("src.s", args);
            Assert.Contains("out.elf", args);
            // CFLAGS only apply to .C/.CPP — an .S build must NOT include them.
            Assert.DoesNotContain("-O2", args);
        }

        [Fact]
        public void BuildAssembleArgs_C_AppendsCflags()
        {
            string args = AsmCompileCore.BuildAssembleArgs(
                "src.c", "out.elf", ".C", "-c -mthumb -O2", "");

            Assert.Contains("-c -mthumb -O2", args);
        }

        [Fact]
        public void BuildAssembleArgs_NonexistentFeclib_OmitsIncludeFlag()
        {
            // FEClib include is only added when the file exists on disk.
            string args = AsmCompileCore.BuildAssembleArgs(
                "src.c", "out.elf", ".C", "", Path.Combine(Path.GetTempPath(), "no-such-feclib-" + Guid.NewGuid() + ".s"));

            Assert.DoesNotContain(" -I ", args);
        }

        [Fact]
        public void BuildLynObjectArgs_BasicShape()
        {
            string args = AsmCompileCore.BuildLynObjectArgs("src.s", "out.o", "");
            Assert.Contains("-mthumb-interwork", args);
            Assert.Contains("src.s", args);
            Assert.Contains("-o ", args);
            Assert.Contains("out.o", args);
        }

        // ---- ClampHookRegister -------------------------------------------------

        [Theory]
        [InlineData(0u, 0u)]
        [InlineData(3u, 3u)]
        [InlineData(8u, 8u)]
        [InlineData(9u, 8u)]
        [InlineData(100u, 8u)]
        public void ClampHookRegister_ClampsToR0R8(uint input, uint expected)
        {
            Assert.Equal(expected, AsmCompileCore.ClampHookRegister(input));
        }

        // ---- Tool-not-found: localized error, ZERO mutation -------------------

        [Fact]
        public void Compile_DevkitNotConfigured_ReturnsLocalizedError()
        {
            var savedConfig = CoreState.Config;
            CoreState.Config = null; // no devkitpro_eabi
            string src = Path.Combine(Path.GetTempPath(), "asm-" + Path.GetRandomFileName() + ".s");
            File.WriteAllText(src, ".thumb\r\nnop\r\n");
            try
            {
                var result = AsmCompileCore.Compile(src, AsmCompileCore.CompileMethod.DumpBinary, checkMissingLabel: false);

                Assert.False(result.Success);
                Assert.Equal(AsmCompileCore.GetNotFoundMessage(), result.ErrorMessage);
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { File.Delete(src); } catch { }
            }
        }

        [Fact]
        public void Compile_MissingSourceFile_ReturnsError()
        {
            var result = AsmCompileCore.Compile(
                Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".s"),
                AsmCompileCore.CompileMethod.DumpBinary, checkMissingLabel: false);

            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
        }

        [Fact]
        public void CompileAndInsert_DevkitNotConfigured_NoMutation()
        {
            var rom = CreateTestRom();
            byte[] before = (byte[])rom.Data.Clone();

            var savedConfig = CoreState.Config;
            CoreState.Config = null;
            string src = Path.Combine(Path.GetTempPath(), "asm-" + Path.GetRandomFileName() + ".s");
            File.WriteAllText(src, ".thumb\r\nnop\r\n");
            try
            {
                var undo = NewUndo(rom);
                var result = AsmCompileCore.CompileAndInsert(
                    rom, src, AsmCompileCore.CompileMethod.DumpBinary,
                    AsmCompileCore.InsertMethod.WriteAtAddress, 0x100, U.NOT_FOUND, 3,
                    SymbolUtil.DebugSymbol.None, checkMissingLabel: false, undo);

                Assert.False(result.Success);
                Assert.Equal(AsmCompileCore.GetNotFoundMessage(), result.ErrorMessage);
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { File.Delete(src); } catch { }
            }
        }

        [Fact]
        public void CompileAndInsert_NullRom_ReturnsError()
        {
            var undo = new Undo.UndoData { list = new List<Undo.UndoPostion>() };
            var result = AsmCompileCore.CompileAndInsert(
                null, "x.s", AsmCompileCore.CompileMethod.DumpBinary,
                AsmCompileCore.InsertMethod.WriteAtAddress, 0x100, U.NOT_FOUND, 3,
                SymbolUtil.DebugSymbol.None, checkMissingLabel: false, undo);

            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
        }

        // ---- lyn.event can't be written to the ROM (WF guard) -----------------

        [Fact]
        public void CompileAndInsert_ConvertLynWithWriteMethod_RejectedBeforeCompiling()
        {
            var rom = CreateTestRom();
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            // ConvertLyn + a write method must short-circuit with a localized error
            // BEFORE any compile/mutation (mirrors WF IsMakeLynEventMode guard).
            var result = AsmCompileCore.CompileAndInsert(
                rom, "any.s", AsmCompileCore.CompileMethod.ConvertLyn,
                AsmCompileCore.InsertMethod.WriteAtAddress, 0x100, U.NOT_FOUND, 3,
                SymbolUtil.DebugSymbol.None, checkMissingLabel: false, undo);

            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
            Assert.Equal(before, rom.Data);
            Assert.Empty(undo.list);
        }

        // ---- Insert math: the trickiest correctness surface (no compiler needed) --
        //
        // These exercise the PUBLIC insert primitives (InsertAtAddress /
        // InsertHookInject) directly with a synthetic binary, asserting the byte
        // placement, the resize/append math, the exact hook jump-code (a wrong jump
        // offset is the kind of bug that ships silently), and full undo round-trips.

        [Fact]
        public void InsertAtAddress_WithinRom_WritesBytes_RecordsUndo_NoResize_Undoable()
        {
            var rom = CreateTestRom(0x400);
            int lenBefore = rom.Data.Length;
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            byte[] bin = { 0xAA, 0xBB, 0xCC, 0xDD };
            Assert.True(AsmCompileCore.InsertAtAddress(rom, 0x100, bin, undo));

            Assert.Equal(0xAAu, rom.u8(0x100));
            Assert.Equal(0xBBu, rom.u8(0x101));
            Assert.Equal(0xCCu, rom.u8(0x102));
            Assert.Equal(0xDDu, rom.u8(0x103));
            Assert.Equal(lenBefore, rom.Data.Length); // fit inside → no resize
            Assert.NotEmpty(undo.list);

            // Undo must revert byte-identical.
            CoreState.Undo = new Undo();
            CoreState.Undo.Push(undo);
            CoreState.Undo.RunUndo();
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void InsertAtAddress_BeyondRomEnd_ResizesThenWrites()
        {
            var rom = CreateTestRom(0x100);
            uint addr = 0xF0;
            byte[] bin = new byte[0x40]; // ends at 0x130 > 0x100 → must resize
            for (int i = 0; i < bin.Length; i++) bin[i] = (byte)(i + 1);
            var undo = NewUndo(rom);

            Assert.True(AsmCompileCore.InsertAtAddress(rom, addr, bin, undo));

            Assert.True(rom.Data.Length >= addr + bin.Length);
            Assert.Equal(1u, rom.u8(addr));
            Assert.Equal(0x40u, rom.u8(addr + 0x3F)); // last byte landed
        }

        [Fact]
        public void InsertHookInject_WritesRoutine_AndCorrectJumpCode_Undoable()
        {
            // 0x2000 ROM; hook at a 4-aligned address, routine into a free area.
            var rom = CreateTestRom(0x2000);
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom);

            uint hookAddr = 0x400;     // 4-aligned → no NOP padding
            uint freeArea = 0x1000;
            uint reg = 4;
            byte[] routine = { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 };

            Assert.True(AsmCompileCore.InsertHookInject(rom, hookAddr, freeArea, reg, routine, undo));

            // Routine body landed verbatim in the free area.
            for (uint i = 0; i < routine.Length; i++)
                Assert.Equal((uint)routine[i], rom.u8(freeArea + i));

            // The hook site holds the EXACT thumb jump-code that MakeInjectJump emits
            // for (hookAddr, freeArea, reg) — this is the load-bearing correctness check.
            byte[] expectedJump = DisassemblerTrumb.MakeInjectJump(hookAddr, freeArea, reg);
            for (uint i = 0; i < expectedJump.Length; i++)
                Assert.Equal((uint)expectedJump[i], rom.u8(hookAddr + i));

            // The embedded routine pointer in the jump-code must be toPointer(freeArea)
            // (0x08-based GBA pointer), little-endian at offset +4 of the jump-code.
            uint embeddedPtr = (uint)expectedJump[4]
                | (uint)expectedJump[5] << 8
                | (uint)expectedJump[6] << 16
                | (uint)expectedJump[7] << 24;
            Assert.Equal(U.toPointer(freeArea), embeddedPtr);

            // Both the hook site and the routine body are recorded → fully undoable.
            Assert.NotEmpty(undo.list);
            CoreState.Undo = new Undo();
            CoreState.Undo.Push(undo);
            CoreState.Undo.RunUndo();
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void InsertHookInject_UnalignedHook_AddsNopPadding()
        {
            // An unaligned hook address forces a 2-byte NOP prefix (MakeInjectJump),
            // making the jump-code 10 bytes instead of 8. Verify the leading NOP.
            var rom = CreateTestRom(0x2000);
            var undo = NewUndo(rom);

            uint hookAddr = 0x402;     // NOT 4-aligned → NOP padding
            uint freeArea = 0x1000;
            byte[] routine = { 0xDE, 0xAD };

            AsmCompileCore.InsertHookInject(rom, hookAddr, freeArea, 3, routine, undo);

            byte[] expectedJump = DisassemblerTrumb.MakeInjectJump(hookAddr, freeArea, 3);
            Assert.Equal(10, expectedJump.Length);      // 2 NOP + 4 instr + 4 ptr
            Assert.Equal(0x00, expectedJump[0]);        // NOP
            Assert.Equal(0x00, expectedJump[1]);
            for (uint i = 0; i < expectedJump.Length; i++)
                Assert.Equal((uint)expectedJump[i], rom.u8(hookAddr + i));
        }

        [Fact]
        public void InsertHookInject_FreeAreaBeyondRomEnd_Resizes()
        {
            var rom = CreateTestRom(0x400);
            var undo = NewUndo(rom);

            uint hookAddr = 0x100;
            uint freeArea = 0x3F0;     // routine ends at 0x3F0+0x20 = 0x410 > 0x400
            byte[] routine = new byte[0x20];
            for (int i = 0; i < routine.Length; i++) routine[i] = (byte)(0x80 + i);

            Assert.True(AsmCompileCore.InsertHookInject(rom, hookAddr, freeArea, 3, routine, undo));

            Assert.True(rom.Data.Length >= freeArea + routine.Length);
            Assert.Equal(0x80u, rom.u8(freeArea));
            Assert.Equal((uint)(0x80 + 0x1F), rom.u8(freeArea + 0x1F));
        }

        [Fact]
        public void InsertHookInject_HookNearRomEnd_ResizesForJumpCodeToo()
        {
            // Copilot #6: the HOOK site can be near the ROM end (its jump-code is the
            // furthest write). A body-only resize would let the jump-code write run
            // out of bounds. Hook at the current end with a free area earlier.
            var rom = CreateTestRom(0x400);
            var undo = NewUndo(rom);

            uint freeArea = 0x100;       // body fits inside the existing ROM
            uint hookAddr = 0x3FC;       // 4-aligned, near the 0x400 end; jump-code = 8B → 0x404 > 0x400
            byte[] routine = { 0x10, 0x20, 0x30, 0x40 };

            Assert.True(AsmCompileCore.InsertHookInject(rom, hookAddr, freeArea, 4, routine, undo));

            byte[] expectedJump = DisassemblerTrumb.MakeInjectJump(hookAddr, freeArea, 4);
            Assert.True(rom.Data.Length >= hookAddr + expectedJump.Length); // resized for the jump-code
            for (uint i = 0; i < expectedJump.Length; i++)
                Assert.Equal((uint)expectedJump[i], rom.u8(hookAddr + i));
        }

        // ---- Fault-safety: a mid-insert failure leaves the ROM byte-identical ---
        //
        // Copilot #3/#7: a resize rejection (e.g. > 32 MB) must NOT partially mutate
        // the ROM. The insert primitives return false (no throw) and CompileAndInsert
        // restores a snapshot, so the ROM is byte-identical and undo stays clean.

        [Fact]
        public void InsertAtAddress_ResizeRejected_ReturnsFalse_NoMutation()
        {
            // A write that would push the ROM past 32 MB → write_resize_data returns
            // false → InsertAtAddress returns false with NO write.
            var savedServices = CoreState.Services;
            CoreState.Services = new HeadlessAppServices();
            try
            {
                var rom = CreateTestRom(0x400);
                byte[] before = (byte[])rom.Data.Clone();
                var undo = NewUndo(rom);

                uint addr = 0x01FFFFFC;          // a write here ends past 0x02000000
                byte[] bin = new byte[0x40];
                for (int i = 0; i < bin.Length; i++) bin[i] = 0xEE;

                bool ok = AsmCompileCore.InsertAtAddress(rom, addr, bin, undo);

                Assert.False(ok);
                Assert.Equal(before, rom.Data);  // byte-identical — no partial write
                Assert.Empty(undo.list);
            }
            finally
            {
                CoreState.Services = savedServices;
            }
        }

        [Fact]
        public void InsertHookInject_ResizeRejected_ReturnsFalse_NoMutation()
        {
            var savedServices = CoreState.Services;
            CoreState.Services = new HeadlessAppServices();
            try
            {
                var rom = CreateTestRom(0x400);
                byte[] before = (byte[])rom.Data.Clone();
                var undo = NewUndo(rom);

                uint hookAddr = 0x100;
                uint freeArea = 0x01FFFFF0;      // body ends past 0x02000000 → resize rejected
                byte[] routine = new byte[0x40];

                bool ok = AsmCompileCore.InsertHookInject(rom, hookAddr, freeArea, 3, routine, undo);

                Assert.False(ok);
                Assert.Equal(before, rom.Data);  // no partial write (body or jump)
                Assert.Empty(undo.list);
            }
            finally
            {
                CoreState.Services = savedServices;
            }
        }

        // ---- Address validation (Copilot #4) -----------------------------------

        [Fact]
        public void ValidateInsertAddresses_ZeroTarget_Rejected()
        {
            var rom = CreateTestRom(0x1000);
            string err = AsmCompileCore.ValidateInsertAddresses(
                rom, AsmCompileCore.InsertMethod.WriteAtAddress, 0u, U.NOT_FOUND, 16);
            Assert.NotNull(err);
        }

        [Fact]
        public void ValidateInsertAddresses_InRangeTarget_Ok()
        {
            var rom = CreateTestRom(0x1000);
            string err = AsmCompileCore.ValidateInsertAddresses(
                rom, AsmCompileCore.InsertMethod.WriteAtAddress, 0x200u, U.NOT_FOUND, 16);
            Assert.Null(err);
        }

        [Fact]
        public void ValidateInsertAddresses_AppendAtRomEnd_Ok()
        {
            var rom = CreateTestRom(0x1000);
            string err = AsmCompileCore.ValidateInsertAddresses(
                rom, AsmCompileCore.InsertMethod.WriteAtAddress, (uint)rom.Data.Length, U.NOT_FOUND, 16);
            Assert.Null(err);
        }

        [Fact]
        public void ValidateInsertAddresses_HookInject_ZeroFreeArea_Rejected()
        {
            var rom = CreateTestRom(0x1000);
            string err = AsmCompileCore.ValidateInsertAddresses(
                rom, AsmCompileCore.InsertMethod.HookInject, 0x200u, 0u, 16);
            Assert.NotNull(err);
        }

        [Fact]
        public void CompileAndInsert_ZeroTargetAddress_NoMutation()
        {
            // Full-path guard: a zero target address must be rejected before any write.
            // (devkitARM is absent in CI, so the compile fails first — but with a
            // configured-but-broken devkit we still never mutate. We assert via the
            // pure validator above; here we assert CompileAndInsert never throws and
            // never mutates with a zero address even when the compile can't run.)
            var rom = CreateTestRom(0x400);
            byte[] before = (byte[])rom.Data.Clone();
            var savedConfig = CoreState.Config;
            CoreState.Config = null; // devkit not configured → compile fails cleanly first
            string src = Path.Combine(Path.GetTempPath(), "asm-" + Path.GetRandomFileName() + ".s");
            File.WriteAllText(src, ".thumb\r\nnop\r\n");
            try
            {
                var undo = NewUndo(rom);
                var result = AsmCompileCore.CompileAndInsert(
                    rom, src, AsmCompileCore.CompileMethod.DumpBinary,
                    AsmCompileCore.InsertMethod.WriteAtAddress, 0u, U.NOT_FOUND, 3,
                    SymbolUtil.DebugSymbol.None, checkMissingLabel: false, undo);

                Assert.False(result.Success);
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { File.Delete(src); } catch { }
            }
        }

        // ---- GetCFlags default -------------------------------------------------

        [Fact]
        public void GetCFlags_DefaultsWhenUnset()
        {
            var savedConfig = CoreState.Config;
            CoreState.Config = new Config(); // empty → default
            try
            {
                Assert.Equal("-c -mthumb -O2", AsmCompileCore.GetCFlags());
            }
            finally
            {
                CoreState.Config = savedConfig;
            }
        }

        [Fact]
        public void GetCFlags_HonorsConfiguredValue()
        {
            var savedConfig = CoreState.Config;
            CoreState.Config = new Config { ["CFLAGS"] = "-c -O3" };
            try
            {
                Assert.Equal("-c -O3", AsmCompileCore.GetCFlags());
            }
            finally
            {
                CoreState.Config = savedConfig;
            }
        }

        // ---- FindFileOne: null (not "") when nothing matches ------------------

        [Fact]
        public void FindFileOne_NoMatch_ReturnsNull()
        {
            string emptyDir = Path.Combine(Path.GetTempPath(), "fbg-asm-empty-" + Path.GetRandomFileName());
            Directory.CreateDirectory(emptyDir);
            try
            {
                Assert.Null(AsmCompileCore.FindFileOne(emptyDir, "*nonexistent-tool.exe"));
            }
            finally
            {
                try { Directory.Delete(emptyDir, true); } catch { }
            }
        }

        [Fact]
        public void ResolveCompiler_NonexistentToolDir_ReturnsNull()
        {
            Assert.Null(AsmCompileCore.ResolveCompiler(
                Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid()), ".C"));
        }

        // ---- Per-tool resolution: each external tool not-found → null/error -----
        //
        // devkitARM (gcc/as), lyn, and EA are three SEPARATE external tools; each must
        // fail cleanly with a localized error and ZERO mutation when missing.

        [Fact]
        public void ResolveDevkitArmTools_NotConfigured_ReturnNull()
        {
            var savedConfig = CoreState.Config;
            CoreState.Config = null; // no devkitpro_eabi
            try
            {
                Assert.Null(ToolPathResolver.ResolveDevkitArmDir());
                Assert.Null(ToolPathResolver.ResolveDevkitArmGcc());
                Assert.Null(ToolPathResolver.ResolveDevkitArmGpp());
                Assert.Null(ToolPathResolver.ResolveDevkitArmAs());
            }
            finally
            {
                CoreState.Config = savedConfig;
            }
        }

        [Fact]
        public void ResolveDevkitArmDir_PointsAtMarkerDirectory_WhenConfigured()
        {
            // Create a fake devkitpro_eabi marker file; ResolveDevkitArmDir must return
            // its directory (the tool tree root), matching the WF tooldir derivation.
            string dir = Path.Combine(Path.GetTempPath(), "fbg-devkit-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            string marker = Path.Combine(dir, "arm-none-eabi-objcopy.exe");
            File.WriteAllText(marker, "stub");

            var savedConfig = CoreState.Config;
            CoreState.Config = new Config { ["devkitpro_eabi"] = marker };
            try
            {
                Assert.Equal(dir, ToolPathResolver.ResolveDevkitArmDir());
                // No *gcc.exe in the tree → still null (tool-specific miss).
                Assert.Null(ToolPathResolver.ResolveDevkitArmGcc());
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void Compile_DevkitConfiguredButNoGcc_ReturnsToolMissingError_NoThrow()
        {
            // devkitpro_eabi marker exists but the tree has NO *gcc.exe → the compile
            // must return a clean tool-missing error, never throw.
            string dir = Path.Combine(Path.GetTempPath(), "fbg-devkit2-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            string marker = Path.Combine(dir, "arm-none-eabi-objcopy.exe");
            File.WriteAllText(marker, "stub");
            string src = Path.Combine(dir, "x.c");
            File.WriteAllText(src, "int main(){return 0;}");

            var savedConfig = CoreState.Config;
            CoreState.Config = new Config { ["devkitpro_eabi"] = marker };
            try
            {
                var result = AsmCompileCore.Compile(
                    src, AsmCompileCore.CompileMethod.DumpBinary, checkMissingLabel: false);

                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
                // The error must name the platform-neutral base tool (gcc for a .c),
                // NOT the Windows-only "*gcc.exe" glob — else it misnames the searched
                // tool on Linux/macOS (Copilot #1245 follow-up).
                Assert.Contains("gcc", result.ErrorMessage);
                Assert.DoesNotContain("*gcc.exe", result.ErrorMessage);
                Assert.DoesNotContain("*gcc", result.ErrorMessage);
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void ResolveLyn_NoEventAssembler_ReturnsNull()
        {
            // With no EA exe resolvable (null config + empty base dir), lyn resolution
            // (which keys off the EA tree) returns null.
            var savedConfig = CoreState.Config;
            var savedBaseDir = CoreState.BaseDirectory;
            CoreState.Config = null;
            CoreState.BaseDirectory = Path.Combine(Path.GetTempPath(),
                "fbg-no-ea-" + Path.GetRandomFileName());
            try
            {
                Assert.Null(AsmCompileCore.ResolveLyn());
            }
            finally
            {
                CoreState.Config = savedConfig;
                CoreState.BaseDirectory = savedBaseDir;
            }
        }

        // ---- GoldRoad (legacy @thumb assembler, #1244) ------------------------
        //
        // GoldRoad is a legacy Windows-era assembler that is NOT in CI (like devkitARM),
        // so there is intentionally NO real-compile here. The deterministic paths ARE
        // covered: the pure @thumb/.thumb auto-switch predicate, tool resolution
        // (config path / directory / missing), goldroad-not-found → localized error +
        // ZERO mutation, the pure argument builder, and that a @thumb source with no
        // goldroad_asm fails cleanly without ever touching the ROM.

        // ShouldUseGoldRoadFromText: the WF MainFormUtil.Compile auto-switch, pure.
        [Theory]
        [InlineData("@thumb\r\nmov r0, r0\r\n", true)]                  // bare @thumb → GoldRoad
        [InlineData("@THUMB\r\n", true)]                               // case-insensitive
        [InlineData(".thumb\r\nnop\r\n", false)]                       // .thumb → devkit (gnu as)
        [InlineData(".thumb\r\n@thumb\r\n", false)]                    // .thumb WINS over @thumb
        [InlineData("@thumb and .thumb on one line", false)]          // .thumb present anywhere → devkit
        [InlineData("nop\r\nmov r0, r0\r\n", false)]                   // neither marker → devkit
        [InlineData("", false)]                                        // empty → devkit
        public void ShouldUseGoldRoadFromText_MatchesWfAutoSwitch(string text, bool expected)
        {
            Assert.Equal(expected, AsmCompileCore.ShouldUseGoldRoadFromText(text));
        }

        [Fact]
        public void ShouldUseGoldRoadFromText_Null_ReturnsFalse()
        {
            Assert.False(AsmCompileCore.ShouldUseGoldRoadFromText(null));
        }

        // ShouldUseGoldRoad: ext gate (.ASM only) + the text predicate, with a real file.
        [Theory]
        [InlineData(".asm", "@thumb\r\nnop\r\n", true)]   // .ASM + @thumb → GoldRoad
        [InlineData(".asm", ".thumb\r\nnop\r\n", false)]  // .ASM + .thumb → devkit
        [InlineData(".s", "@thumb\r\nnop\r\n", false)]    // .S is ALWAYS devkit (WF gates on .ASM)
        [InlineData(".c", "@thumb\r\n", false)]           // .C is ALWAYS devkit
        public void ShouldUseGoldRoad_ExtGate_AndContent(string ext, string content, bool expected)
        {
            string src = Path.Combine(Path.GetTempPath(), "fbg-gr-" + Path.GetRandomFileName() + ext);
            File.WriteAllText(src, content);
            try
            {
                Assert.Equal(expected, AsmCompileCore.ShouldUseGoldRoad(src));
            }
            finally
            {
                try { File.Delete(src); } catch { }
            }
        }

        [Fact]
        public void ShouldUseGoldRoad_MissingOrEmptyPath_ReturnsFalse()
        {
            Assert.False(AsmCompileCore.ShouldUseGoldRoad(null));
            Assert.False(AsmCompileCore.ShouldUseGoldRoad(""));
            Assert.False(AsmCompileCore.ShouldUseGoldRoad(
                Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid() + ".asm")));
        }

        // BuildGoldRoadArgs: pure — <source> <output.gba> (bare names, escaped).
        [Fact]
        public void BuildGoldRoadArgs_BasicShape()
        {
            string args = AsmCompileCore.BuildGoldRoadArgs("foo.asm", "foo.gba");
            Assert.Contains("foo.asm", args);
            Assert.Contains("foo.gba", args);
            // The source comes first, the .gba output second (WF order).
            Assert.True(args.IndexOf("foo.asm", StringComparison.Ordinal)
                < args.IndexOf("foo.gba", StringComparison.Ordinal));
        }

        // ResolveGoldRoad: config path / directory / missing.
        [Fact]
        public void ResolveGoldRoad_NotConfigured_ReturnsNull()
        {
            var savedConfig = CoreState.Config;
            CoreState.Config = null;
            try
            {
                Assert.Null(ToolPathResolver.ResolveGoldRoad());
                Assert.Null(AsmCompileCore.ResolveGoldRoad());
                Assert.False(AsmCompileCore.IsGoldRoadAvailable());
            }
            finally
            {
                CoreState.Config = savedConfig;
            }
        }

        [Fact]
        public void ResolveGoldRoad_DirectExePath_ResolvesAndIsAvailable()
        {
            string dir = Path.Combine(Path.GetTempPath(), "fbg-goldroad-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "goldroad.exe");
            File.WriteAllText(exe, "stub");

            var savedConfig = CoreState.Config;
            CoreState.Config = new Config { ["goldroad_asm"] = exe };
            try
            {
                Assert.Equal(exe, ToolPathResolver.ResolveGoldRoad());
                Assert.True(AsmCompileCore.IsGoldRoadAvailable());
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void ResolveGoldRoad_ConfigPointsAtDirectory_FindsPlatformBinary()
        {
            // When the config points at a directory, the platform-aware goldroad/
            // goldroad.exe inside it is found (so a non-Windows layout works too).
            string dir = Path.Combine(Path.GetTempPath(), "fbg-goldroad-dir-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation
                .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            string exeName = isWindows ? "goldroad.exe" : "goldroad";
            string exe = Path.Combine(dir, exeName);
            File.WriteAllText(exe, "stub");

            var savedConfig = CoreState.Config;
            CoreState.Config = new Config { ["goldroad_asm"] = dir };
            try
            {
                Assert.Equal(exe, ToolPathResolver.ResolveGoldRoad());
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void ResolveGoldRoad_ConfiguredButMissing_ReturnsNull()
        {
            var savedConfig = CoreState.Config;
            CoreState.Config = new Config
            {
                ["goldroad_asm"] = Path.Combine(Path.GetTempPath(), "no-goldroad-" + Guid.NewGuid() + ".exe")
            };
            try
            {
                Assert.Null(ToolPathResolver.ResolveGoldRoad());
                Assert.False(AsmCompileCore.IsGoldRoadAvailable());
            }
            finally
            {
                CoreState.Config = savedConfig;
            }
        }

        // GoldRoad not-found: a @thumb .ASM with no goldroad_asm → localized error,
        // ZERO mutation. Note the source routes to GoldRoad (not devkit) PURELY from its
        // content, so even with devkitARM unset the error names goldroad, not devkit.
        [Fact]
        public void Compile_GoldRoadSource_NotConfigured_ReturnsGoldRoadError()
        {
            var savedConfig = CoreState.Config;
            CoreState.Config = new Config(); // no goldroad_asm, no devkitpro_eabi
            string src = Path.Combine(Path.GetTempPath(), "gr-" + Path.GetRandomFileName() + ".asm");
            File.WriteAllText(src, "@thumb\r\nmov r0, r0\r\n");
            try
            {
                var result = AsmCompileCore.Compile(
                    src, AsmCompileCore.CompileMethod.DumpBinary, checkMissingLabel: false);

                Assert.False(result.Success);
                Assert.Equal(AsmCompileCore.GetGoldRoadNotFoundMessage(), result.ErrorMessage);
                // The error names goldroad (the auto-selected tool), NOT devkitpro_eabi.
                Assert.Contains("goldroad", result.ErrorMessage);
                Assert.DoesNotContain("devkitpro_eabi", result.ErrorMessage);
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { File.Delete(src); } catch { }
            }
        }

        [Fact]
        public void CompileAndInsert_GoldRoadSource_NotConfigured_NoMutation()
        {
            var rom = CreateTestRom();
            byte[] before = (byte[])rom.Data.Clone();

            var savedConfig = CoreState.Config;
            CoreState.Config = new Config(); // no goldroad_asm
            string src = Path.Combine(Path.GetTempPath(), "gr-" + Path.GetRandomFileName() + ".asm");
            File.WriteAllText(src, "@thumb\r\nmov r0, r0\r\n");
            try
            {
                var undo = NewUndo(rom);
                var result = AsmCompileCore.CompileAndInsert(
                    rom, src, AsmCompileCore.CompileMethod.DumpBinary,
                    AsmCompileCore.InsertMethod.WriteAtAddress, 0x100, U.NOT_FOUND, 3,
                    SymbolUtil.DebugSymbol.None, checkMissingLabel: false, undo);

                Assert.False(result.Success);
                Assert.Equal(AsmCompileCore.GetGoldRoadNotFoundMessage(), result.ErrorMessage);
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { File.Delete(src); } catch { }
            }
        }

        [Fact]
        public void CompileAndInsert_GoldRoadSource_ConvertLynMethod_NotRejectedByLynGuard_FailsAtToolResolution()
        {
            // A GoldRoad source ALWAYS produces a raw .bin (WF ignores compileType for
            // it), so pairing it with the ConvertLyn method must NOT trip the
            // "lyn.event can't be written to the ROM" guard — it should instead fall
            // through to the GoldRoad compile and fail cleanly at tool resolution
            // (goldroad not configured), with NO mutation.
            var rom = CreateTestRom();
            byte[] before = (byte[])rom.Data.Clone();

            var savedConfig = CoreState.Config;
            CoreState.Config = new Config(); // no goldroad_asm
            string src = Path.Combine(Path.GetTempPath(), "gr-" + Path.GetRandomFileName() + ".asm");
            File.WriteAllText(src, "@thumb\r\nmov r0, r0\r\n");
            try
            {
                var undo = NewUndo(rom);
                var result = AsmCompileCore.CompileAndInsert(
                    rom, src, AsmCompileCore.CompileMethod.ConvertLyn,
                    AsmCompileCore.InsertMethod.WriteAtAddress, 0x100, U.NOT_FOUND, 3,
                    SymbolUtil.DebugSymbol.None, checkMissingLabel: false, undo);

                Assert.False(result.Success);
                // The error is the GoldRoad not-found message, NOT the lyn-can't-write guard.
                Assert.Equal(AsmCompileCore.GetGoldRoadNotFoundMessage(), result.ErrorMessage);
                Assert.Equal(before, rom.Data);
                Assert.Empty(undo.list);
            }
            finally
            {
                CoreState.Config = savedConfig;
                try { File.Delete(src); } catch { }
            }
        }

        [Fact]
        public void GetGoldRoadNotFoundMessage_NamesGoldroad_NotDevkit()
        {
            string msg = AsmCompileCore.GetGoldRoadNotFoundMessage();
            Assert.False(string.IsNullOrEmpty(msg));
            Assert.Contains("goldroad", msg);
            Assert.DoesNotContain("devkitpro_eabi", msg);
        }
    }
}
