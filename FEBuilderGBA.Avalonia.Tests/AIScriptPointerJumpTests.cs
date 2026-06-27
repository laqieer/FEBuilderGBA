// SPDX-License-Identifier: GPL-3.0-or-later
// AIScript POINTER_AI* parameter-jump tests (#1600).
//
// Proves the Avalonia AI Script editor can route a POINTER_AI* opcode
// parameter to the matching AI sub-editor with the real script pointer, and
// that the resolved/allocated pointer is written into the editor's IN-MEMORY
// model (OneCode.ByteData) — NOT directly to ROM — so a later WriteScript
// serializes it consistently and pending row edits survive a jump.
//
// Covers:
//   - AIScriptPointerJumpCore.ClassifyArg maps the 5 POINTER_AI* ArgTypes;
//   - AllocIfNeed: value 0 -> allocates a safe 4-byte block; valid value ->
//     unchanged; Units/Tiles -> never allocate;
//   - WritePointerIntoBytes writes only the 4 arg bytes;
//   - VM ApplyPointerJump resolves the right kind + pointer and mutates only
//     the selected row's ByteData;
//   - a pointer jump is PRESERVED by a subsequent SerializeScript;
//   - a pre-existing unsaved row edit is NOT lost by a pointer jump;
//   - ApplyPointerJump is a no-op (kind None) for a non-AI arg.
//
// Reuses the AiDisasmEnv synthetic FE8U environment from
// AIScriptDisassemblyTests.cs. Marked [Collection("SharedState")] because it
// mutates CoreState.ROM / CoreState.AIScript / CoreState.CommentCache /
// CoreState.BaseDirectory.
using System;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AIScriptPointerJumpTests
    {
        // --- FE8U AI opcode bodies (from config/data/aiscript_FE8.*.txt) -----

        // Attack05: 05 XX FF 00 00 00 00 00 YYYYYYYY 00 00 00 00
        // POINTER_AIUNIT at byte offset 8.
        static byte[] Attack05WithUnitPtr(uint gbaPtr)
        {
            var b = new byte[16];
            b[0] = 0x05; b[1] = 0x10; b[2] = 0xFF;
            WriteLe(b, 8, gbaPtr);
            return b;
        }

        // Move-towards-tiles 1A: 1A 00 FF 00 00 00 00 00 YYYYYYYY 00 00 00 00
        // POINTER_AITILE at byte offset 8.
        static byte[] Tile1AWithTilePtr(uint gbaPtr)
        {
            var b = new byte[16];
            b[0] = 0x1A; b[1] = 0x00; b[2] = 0xFF;
            WriteLe(b, 8, gbaPtr);
            return b;
        }

        // FE8U Coordinate: 01 00 FF 00 00 00 00 00 A9 F9 03 08 VVVVVVVV
        // POINTER_AICOORDINATE at byte offset 12.
        static byte[] CoordinateFE8U(uint gbaPtr)
            => OpcodeWith0308Disc(new byte[] { 0xA9, 0xF9, 0x03, 0x08 }, gbaPtr);

        // FE8U Range: 01 00 FF 00 00 00 00 00 A5 F4 03 08 VVVVVVVV
        static byte[] RangeFE8U(uint gbaPtr)
            => OpcodeWith0308Disc(new byte[] { 0xA5, 0xF4, 0x03, 0x08 }, gbaPtr);

        // FE8U CallTalk: 01 00 FF 00 00 00 00 00 ED F4 03 08 VVVVVVVV
        static byte[] CallTalkFE8U(uint gbaPtr)
            => OpcodeWith0308Disc(new byte[] { 0xED, 0xF4, 0x03, 0x08 }, gbaPtr);

        static byte[] OpcodeWith0308Disc(byte[] disc4, uint gbaPtr)
        {
            var b = new byte[16];
            b[0] = 0x01; b[1] = 0x00; b[2] = 0xFF;
            Array.Copy(disc4, 0, b, 8, 4); // discriminator at bytes 8..11
            WriteLe(b, 12, gbaPtr);        // pointer arg at byte 12
            return b;
        }

        static void WriteLe(byte[] b, int pos, uint v)
        {
            b[pos + 0] = (byte)(v & 0xFF);
            b[pos + 1] = (byte)((v >> 8) & 0xFF);
            b[pos + 2] = (byte)((v >> 16) & 0xFF);
            b[pos + 3] = (byte)((v >> 24) & 0xFF);
        }

        static uint ReadLe(byte[] b, int pos)
            => (uint)(b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24));

        // ----------------------------------------------------------------
        // 1. ClassifyArg covers all 5 POINTER_AI* types and nothing else.
        // ----------------------------------------------------------------

        [Fact]
        public void ClassifyArg_MapsEachPointerType()
        {
            Assert.Equal(AiPointerKind.Units, ClassifyType(EventScript.ArgType.POINTER_AIUNIT));
            Assert.Equal(AiPointerKind.Tiles, ClassifyType(EventScript.ArgType.POINTER_AITILE));
            Assert.Equal(AiPointerKind.Coordinate, ClassifyType(EventScript.ArgType.POINTER_AICOORDINATE));
            Assert.Equal(AiPointerKind.Range, ClassifyType(EventScript.ArgType.POINTER_AIRANGE));
            Assert.Equal(AiPointerKind.CallTalk, ClassifyType(EventScript.ArgType.POINTER_AICALLTALK));
            // A non-AI pointer type is None.
            Assert.Equal(AiPointerKind.None, ClassifyType(EventScript.ArgType.POINTER_EVENT));
            Assert.Equal(AiPointerKind.None, ClassifyType(EventScript.ArgType.UNIT));
        }

        static AiPointerKind ClassifyType(EventScript.ArgType t)
        {
            var arg = new EventScript.Arg { Type = t, Position = 0, Size = 4 };
            return AIScriptPointerJumpCore.ClassifyArg(arg);
        }

        // ----------------------------------------------------------------
        // 2. AllocIfNeed: null -> allocate; valid -> unchanged; Units/Tiles -> never.
        // ----------------------------------------------------------------

        [Fact]
        public void AllocIfNeed_NullCoordinate_AllocatesSafeFourByteBlock()
        {
            using var env = new AiDisasmEnv();
            ROM rom = env.Rom;

            bool ok = AIScriptPointerJumpCore.AllocIfNeed(
                rom, AiPointerKind.Coordinate, 0, null,
                out uint newPtr, out bool allocated);

            Assert.True(ok);
            Assert.True(allocated);
            uint off = U.toOffset(newPtr);
            Assert.True(U.isSafetyOffset(off + 4, rom),
                "Allocated coordinate block must be a safe in-bounds offset.");
        }

        [Fact]
        public void AllocIfNeed_ValidCoordinate_KeepsPointer()
        {
            using var env = new AiDisasmEnv();
            ROM rom = env.Rom;

            // Plant a valid coordinate block (bytes 2,3 zero) at a safe offset.
            uint blockOff = 0x300000;
            uint gbaPtr = U.toPointer(blockOff);
            // u16(off+2) must be 0 for a non-broken coordinate.
            rom.write_u8(blockOff + 2, 0);
            rom.write_u8(blockOff + 3, 0);

            bool ok = AIScriptPointerJumpCore.AllocIfNeed(
                rom, AiPointerKind.Coordinate, gbaPtr, null,
                out uint newPtr, out bool allocated);

            Assert.True(ok);
            Assert.False(allocated);
            Assert.Equal(gbaPtr, newPtr);
        }

        [Fact]
        public void AllocIfNeed_UnitsAndTiles_NeverAllocate_EvenOnNull()
        {
            using var env = new AiDisasmEnv();
            ROM rom = env.Rom;

            foreach (AiPointerKind kind in new[] { AiPointerKind.Units, AiPointerKind.Tiles })
            {
                bool ok = AIScriptPointerJumpCore.AllocIfNeed(
                    rom, kind, 0, null, out uint newPtr, out bool allocated);
                Assert.True(ok);
                Assert.False(allocated);
                Assert.Equal(0u, newPtr);
            }
        }

        // ----------------------------------------------------------------
        // 3. WritePointerIntoBytes mutates only the 4 arg bytes.
        // ----------------------------------------------------------------

        [Fact]
        public void WritePointerIntoBytes_WritesOnlyArgBytes()
        {
            var bytes = new byte[16];
            for (int i = 0; i < 16; i++) bytes[i] = 0x11;
            var arg = new EventScript.Arg { Type = EventScript.ArgType.POINTER_AICOORDINATE, Position = 12, Size = 4 };

            AIScriptPointerJumpCore.WritePointerIntoBytes(bytes, arg, 0x08ABCDEFu);

            Assert.Equal(0x08ABCDEFu, ReadLe(bytes, 12));
            for (int i = 0; i < 12; i++)
                Assert.Equal(0x11, bytes[i]); // untouched
        }

        // ----------------------------------------------------------------
        // 4. VM ApplyPointerJump resolves the right kind for each AI pointer.
        // ----------------------------------------------------------------

        [Fact]
        public void ApplyPointerJump_ResolvesEachKind_WithExistingPointer()
        {
            using var env = new AiDisasmEnv();
            // A valid, safe block well away from the script body so it is NOT
            // treated as broken for the ASM types.
            uint blockGbaPtr = U.toPointer(0x300000);
            env.Rom.write_u8(0x300000 + 2, 0); // coordinate non-broken guard

            // Attack05's param 1 is PROBABILITY, param 2 is the POINTER_AIUNIT.
            AssertKind(env, Attack05WithUnitPtr(blockGbaPtr), 2, AiPointerKind.Units);
            // The remaining opcodes expose exactly one non-FIXED pointer arg (param 1).
            AssertKind(env, Tile1AWithTilePtr(blockGbaPtr), 1, AiPointerKind.Tiles);
            AssertKind(env, CoordinateFE8U(blockGbaPtr), 1, AiPointerKind.Coordinate);
            AssertKind(env, RangeFE8U(blockGbaPtr), 1, AiPointerKind.Range);
            AssertKind(env, CallTalkFE8U(blockGbaPtr), 1, AiPointerKind.CallTalk);
        }

        static void AssertKind(AiDisasmEnv env, byte[] body, int paramRow, AiPointerKind expected)
        {
            var vm = env.LoadVmAt(body);
            vm.DisassembleScript(); // populate _disassembled
            AiPointerKind kind = vm.ClassifyParam(0, paramRow);
            Assert.Equal(expected, kind);

            bool ok = vm.ApplyPointerJump(0, paramRow, null, out AiPointerKind resolved,
                out uint pointer, out bool allocated);
            Assert.True(ok, $"{expected}: ApplyPointerJump should succeed.");
            Assert.Equal(expected, resolved);
            Assert.False(allocated); // valid existing pointer -> no alloc
            Assert.NotEqual(0u, pointer);
        }

        // ----------------------------------------------------------------
        // 5. CONSISTENCY: a pointer jump is preserved by a later Serialize.
        // ----------------------------------------------------------------

        [Fact]
        public void ApplyPointerJump_BrokenCoordinate_AllocatedPointer_PreservedBySerialize()
        {
            using var env = new AiDisasmEnv();
            // A strict POINTER_AI* arg only DECODES as the coordinate opcode when
            // its pointer is a safe pointer (EventScript.DisAseemble rejects a 0 /
            // unsafe pointer and falls through to Unknown). So the realistic
            // alloc trigger is a pointer that decodes but whose target data is
            // BROKEN — the coordinate IsBrokenData check is u16(off+2) != 0.
            uint blockOff = 0x300000;
            env.Rom.write_u8(blockOff + 2, 0xFF); // make u16(off+2) != 0 -> broken
            uint brokenPtr = U.toPointer(blockOff);

            var vm = env.LoadVmAt(CoordinateFE8U(brokenPtr));
            vm.DisassembleScript();

            Assert.Equal(AiPointerKind.Coordinate, vm.ClassifyParam(0, 1));
            Assert.True(vm.ParamNeedsAlloc(0, 1), "Broken coordinate should require allocation.");

            bool ok = vm.ApplyPointerJump(0, 1, null, out AiPointerKind kind,
                out uint pointer, out bool allocated);
            Assert.True(ok);
            Assert.Equal(AiPointerKind.Coordinate, kind);
            Assert.True(allocated);
            Assert.NotEqual(0u, pointer);
            Assert.NotEqual(brokenPtr, pointer); // a fresh block, not the broken one

            // The allocated pointer must be present in the serialized model at the
            // coordinate arg position (byte 12) — i.e. a later WriteScript would
            // persist it, not the stale broken pointer.
            byte[] serialized = vm.SerializeScript();
            Assert.True(serialized.Length >= 16);
            Assert.Equal(pointer, ReadLe(serialized, 12));
        }

        // ----------------------------------------------------------------
        // 6. CONSISTENCY: a pre-existing unsaved row edit is NOT lost by a jump.
        // ----------------------------------------------------------------

        [Fact]
        public void ApplyPointerJump_DoesNotDiscardPendingRowEdit()
        {
            using var env = new AiDisasmEnv();

            // Row 0 = a plain opcode we will hand-edit; row 1 = the coordinate
            // opcode we will jump from.
            var body = new byte[32];
            byte[] plain = new byte[16];
            plain[0] = 0x06; plain[2] = 0xFF;            // DoNothing
            Array.Copy(plain, 0, body, 0, 16);
            Array.Copy(CoordinateFE8U(U.toPointer(0x300000)), 0, body, 16, 16);
            env.Rom.write_u8(0x300000 + 2, 0);

            var vm = env.LoadVmAt(body);
            vm.DisassembleScript();
            Assert.Equal(2, vm.RowCount);

            // Hand-edit row 0's bytes (DoNothing -> set byte[1] = 0x7F).
            var edited = new byte[16];
            edited[0] = 0x06; edited[1] = 0x7F; edited[2] = 0xFF;
            string hex = string.Join(" ", edited.Select(x => x.ToString("X2")));
            Assert.NotNull(vm.UpdateRow(0, hex));

            // Now jump from row 1's coordinate param.
            bool ok = vm.ApplyPointerJump(1, 1, null, out _, out _, out _);
            Assert.True(ok);

            // Row 0's pending edit must still be in the model.
            byte[] serialized = vm.SerializeScript();
            Assert.Equal(0x7F, serialized[1]);
        }

        // ----------------------------------------------------------------
        // 7. ApplyPointerJump is a no-op for a non-AI parameter.
        // ----------------------------------------------------------------

        [Fact]
        public void ApplyPointerJump_NonAiParam_IsNoOp()
        {
            using var env = new AiDisasmEnv();
            // Attack05's param 1 is PROBABILITY (a non-AI byte arg at offset 1),
            // param 2 is the POINTER_AIUNIT. Param 1 must classify as None.
            var vm = env.LoadVmAt(Attack05WithUnitPtr(U.toPointer(0x300000)));
            vm.DisassembleScript();

            Assert.Equal(AiPointerKind.None, vm.ClassifyParam(0, 1)); // PROBABILITY
            bool ok = vm.ApplyPointerJump(0, 1, null, out AiPointerKind kind, out _, out _);
            Assert.False(ok);
            Assert.Equal(AiPointerKind.None, kind);
        }
    }
}
