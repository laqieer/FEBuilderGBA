// SPDX-License-Identifier: GPL-3.0-or-later
// AIScriptPointerJumpCore (#1600) — headless logic behind the Avalonia AIScript
// editor's per-parameter POINTER_AI* jump to the 5 AI sub-editors
// (AIUnits / AITiles / AIASMCoordinate / AIASMRange / AIASMCALLTALK).
//
// WinForms parity: AIScriptForm.cs:718-760 dispatches the five POINTER_AI*
// ArgTypes — open the matching sub-editor seeded at the opcode-arg pointer,
// AllocIfNeed (allocate a 4-byte block when the pointer is null/broken for the
// 3 ASM types: coordinate/range/calltalk), then write the resulting base
// pointer back into the opcode parameter (U.ForceUpdate).
//
// This class is intentionally GUI-free and ROM-aware (every method takes the
// ROM explicitly), so the Avalonia VM, future WinForms parity, and headless
// tests share the same logic. It NEVER throws and guards every offset.
//
// Crucial consistency note (Copilot plan-review #1600): the opcode parameter
// pointer is written into the editor's IN-MEMORY OneCode.ByteData
// (WritePointerIntoBytes), NOT directly to the ROM. The Avalonia AIScript
// editor's _disassembled list is the canonical pending-edit model that
// AIScriptViewModel.WriteScript serializes on Write; writing the pointer into
// the byte model keeps a later WriteScript consistent and preserves any pending
// Update/New/Remove row edits. The ONLY ROM mutation here is AllocIfNeed's
// append of a fresh ASM data block (it needs a real address), which is
// undo-tracked.
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Which AI sub-editor a POINTER_AI* opcode argument routes to.
    /// </summary>
    public enum AiPointerKind
    {
        None,
        Units,       // POINTER_AIUNIT  -> AIUnitsView      (ushort unit list, never null in WF)
        Tiles,       // POINTER_AITILE  -> AITilesView      (byte tile list, never null in WF)
        Coordinate,  // POINTER_AICOORDINATE -> AIASMCoordinateView (4-byte block, AllocIfNeed)
        Range,       // POINTER_AIRANGE -> AIASMRangeView    (4-byte block, AllocIfNeed)
        CallTalk,    // POINTER_AICALLTALK -> AIASMCALLTALKView (4-byte block, AllocIfNeed)
    }

    public static class AIScriptPointerJumpCore
    {
        /// <summary>Initial allocation size for the 3 ASM data blocks (4 zero bytes), mirroring WF NewAlloc.</summary>
        const uint ASM_ALLOC_SIZE = 4;

        /// <summary>
        /// Map an opcode argument's ArgType to the AI sub-editor it routes to.
        /// Returns <see cref="AiPointerKind.None"/> for any non-AI-pointer arg.
        /// </summary>
        public static AiPointerKind ClassifyArg(EventScript.Arg arg)
        {
            if (arg == null) return AiPointerKind.None;
            switch (arg.Type)
            {
                case EventScript.ArgType.POINTER_AIUNIT: return AiPointerKind.Units;
                case EventScript.ArgType.POINTER_AITILE: return AiPointerKind.Tiles;
                case EventScript.ArgType.POINTER_AICOORDINATE: return AiPointerKind.Coordinate;
                case EventScript.ArgType.POINTER_AIRANGE: return AiPointerKind.Range;
                case EventScript.ArgType.POINTER_AICALLTALK: return AiPointerKind.CallTalk;
                default: return AiPointerKind.None;
            }
        }

        /// <summary>True when the kind is one of the 3 ASM data blocks that support AllocIfNeed.</summary>
        public static bool IsAllocKind(AiPointerKind kind)
            => kind == AiPointerKind.Coordinate || kind == AiPointerKind.Range || kind == AiPointerKind.CallTalk;

        /// <summary>
        /// Find the first POINTER_AI* argument in an opcode. Most AI opcodes that
        /// carry an AI pointer carry exactly one. Returns false (kind=None) when
        /// the opcode has no AI-pointer argument.
        /// </summary>
        public static bool TryGetPointerArg(EventScript.OneCode code, out int argIndex, out AiPointerKind kind)
        {
            argIndex = -1;
            kind = AiPointerKind.None;
            if (code == null || code.Script == null || code.Script.Args == null) return false;
            for (int i = 0; i < code.Script.Args.Length; i++)
            {
                AiPointerKind k = ClassifyArg(code.Script.Args[i]);
                if (k != AiPointerKind.None)
                {
                    argIndex = i;
                    kind = k;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Port of the WinForms AIASMCoordinateForm.IsBrokenData / Range / CallTalk
        /// validation: an existing pointer is "broken" when its target is not a
        /// safe 4-byte ROM offset, or (for Coordinate) the 3rd/4th bytes are
        /// non-zero (the WF u16(off+2)!=0 check — those bytes must be zero for a
        /// valid coordinate block). Range / CallTalk only require a safe offset.
        /// </summary>
        public static bool IsBrokenData(ROM rom, AiPointerKind kind, uint pointerValue)
        {
            if (rom == null) return true;
            uint off = U.toOffset(pointerValue);
            if (!U.isSafetyOffset(off + 4, rom)) return true;
            if (kind == AiPointerKind.Coordinate)
            {
                // WF: Program.ROM.u16(addr + 2) == 0 => OK; otherwise broken.
                if (rom.u16(off + 2) != 0) return true;
            }
            return false;
        }

        /// <summary>
        /// WinForms AllocIfNeed parity for the 3 ASM data blocks. When
        /// <paramref name="currentPointerValue"/> is null (0 / NOT_FOUND) or broken,
        /// allocate a fresh zeroed 4-byte block in free space (via the
        /// CoreState.AppendBinaryData seam when wired, else a direct
        /// FindFreeSpace + write_range fallback — same dispatch as
        /// MapExitPointCore.NewAlloc) and return its GBA pointer with
        /// <paramref name="allocated"/> = true. For Units/Tiles (which point into
        /// the script body and are never null) and for an already-valid value, the
        /// pointer passes through unchanged (allocated=false).
        ///
        /// The append is undo-tracked through the ambient scope (or the supplied
        /// undodata). Returns false ONLY on an allocation failure (out of free
        /// space) for an alloc kind; the caller should abort the jump in that case.
        /// </summary>
        public static bool AllocIfNeed(ROM rom, AiPointerKind kind, uint currentPointerValue,
            Undo.UndoData? undodata, out uint newPointerValue, out bool allocated)
        {
            newPointerValue = currentPointerValue;
            allocated = false;
            if (rom == null || rom.RomInfo == null) return false;

            if (!IsAllocKind(kind))
            {
                // Units / Tiles: never allocate; the pointer addresses script-body data.
                return true;
            }

            bool isNull = currentPointerValue == 0 || currentPointerValue == U.NOT_FOUND;
            if (!isNull && !IsBrokenData(rom, kind, currentPointerValue))
            {
                // Existing, valid block — keep it.
                return true;
            }

            uint newaddr = AppendZeroBlock(rom, undodata);
            if (newaddr == U.NOT_FOUND || newaddr == 0) return false;

            newPointerValue = U.toPointer(newaddr);
            allocated = true;
            return true;
        }

        /// <summary>
        /// Append a fresh zeroed <see cref="ASM_ALLOC_SIZE"/>-byte block to free
        /// space and return its ROM offset (U.NOT_FOUND on failure). Mirrors the
        /// WF NewAlloc payload (4 zero bytes) and the MapExitPointCore.NewAlloc
        /// allocation dispatch (AppendBinaryData seam when wired, else
        /// FindFreeSpace + write_range).
        /// </summary>
        static uint AppendZeroBlock(ROM rom, Undo.UndoData? undodata)
        {
            byte[] data = new byte[(int)ASM_ALLOC_SIZE]; // all zero

            uint newaddr;
            if (CoreState.AppendBinaryData != null && undodata != null)
            {
                newaddr = CoreState.AppendBinaryData(data, undodata);
            }
            else
            {
                uint searchStart = (uint)(rom.Data.Length / 2);
                newaddr = rom.FindFreeSpace(searchStart, ASM_ALLOC_SIZE);
                if (newaddr == U.NOT_FOUND)
                {
                    newaddr = rom.FindFreeSpace(0x100u, ASM_ALLOC_SIZE);
                }
                if (newaddr == U.NOT_FOUND) return U.NOT_FOUND;
                rom.write_range(newaddr, data);
            }
            return newaddr;
        }

        /// <summary>
        /// PURE: write a 4-byte GBA pointer into an opcode's in-memory ByteData at
        /// the argument's position (little-endian), returning the modified array.
        /// The caller mutates the editor's pending-edit model row with the result
        /// (WriteScript serializes ByteData later). No ROM access. Returns the
        /// array unchanged when the arg is not a 4-byte slot or would run off the
        /// end of ByteData.
        /// </summary>
        public static void WritePointerIntoBytes(byte[] byteData, EventScript.Arg arg, uint pointerValue)
        {
            if (byteData == null || arg == null) return;
            if (arg.Size != 4) return;
            int pos = arg.Position;
            if (pos < 0 || pos + 4 > byteData.Length) return;
            byteData[pos + 0] = (byte)(pointerValue & 0xFF);
            byteData[pos + 1] = (byte)((pointerValue >> 8) & 0xFF);
            byteData[pos + 2] = (byte)((pointerValue >> 16) & 0xFF);
            byteData[pos + 3] = (byte)((pointerValue >> 24) & 0xFF);
        }
    }
}
