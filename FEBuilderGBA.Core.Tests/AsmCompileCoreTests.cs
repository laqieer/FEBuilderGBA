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

        // ---- CompilerGlobForExt -----------------------------------------------

        [Theory]
        [InlineData(".C", "*gcc.exe")]
        [InlineData(".CPP", "*g++.exe")]
        [InlineData(".S", "*as.exe")]
        [InlineData(".ASM", "*as.exe")]
        [InlineData(".TXT", "*as.exe")]
        public void CompilerGlobForExt_PicksTool(string ext, string expected)
        {
            Assert.Equal(expected, AsmCompileCore.CompilerGlobForExt(ext));
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
            AsmCompileCore.InsertAtAddress(rom, 0x100, bin, undo);

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

            AsmCompileCore.InsertAtAddress(rom, addr, bin, undo);

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

            AsmCompileCore.InsertHookInject(rom, hookAddr, freeArea, reg, routine, undo);

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

            AsmCompileCore.InsertHookInject(rom, hookAddr, freeArea, 3, routine, undo);

            Assert.True(rom.Data.Length >= freeArea + routine.Length);
            Assert.Equal(0x80u, rom.u8(freeArea));
            Assert.Equal((uint)(0x80 + 0x1F), rom.u8(freeArea + 0x1F));
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
    }
}
