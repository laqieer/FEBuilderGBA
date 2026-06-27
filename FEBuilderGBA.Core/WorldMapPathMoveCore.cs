// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    // ======================================================================
    // World Map Path MOVEMENT editor — Core (#1598). FE8-ONLY.
    //
    // CORRUPTION FIX. The Avalonia "World Map Path Movement" editor used to
    // open with NO base address, fall back to p32(worldmap_road_pointer) — the
    // base of the 12-byte PATH-RECORD table — walk THAT at an 8-byte movement
    // stride reading garbage, and (worse) write back onto the record table,
    // CORRUPTING the road-record pointers/ids. There was no path selector.
    //
    // Ground truth (verified against FE8U.gba):
    //   worldmap_road_pointer (FE8U 0xC2C0) -> 12-byte path-record table.
    //     record +0  path-data pointer
    //     record +4  base-point id #1
    //     record +5  base-point id #2
    //     record +8  MOVEMENT-data pointer  <-- the actual movement sub-table
    //   The movement sub-table at p32(record+8) is 8-byte stride, terminated
    //   by a u32 == 0xFFFFFFFF sentinel. Real fields:
    //     ElapsedTime = u16 @ +0   (values 0x547/0x800/0xA8F observed, > 255 so
    //                               genuinely 16-bit; +2/+3 are 0 padding)
    //     X           = u16 @ +4
    //     Y           = u16 @ +6
    //
    // WF parity source (FEBuilderGBA/WorldMapPathMoveEditorForm.cs): a PathType
    // combo (one entry per 12-byte path record via WorldMapPathForm.MakeList),
    // PathType_SelectedIndexChanged -> pathmove = p32(addr+8) ->
    // InputFormRef.ReInit(pathmove) (loads the movement data only AFTER a path
    // is picked). Movement record fields D0(time)/W4(X)/W6(Y) at +0/+4/+6,
    // 8-byte stride, u32 != 0xFFFFFFFF terminator.
    //
    // The ElapsedTime field was read as a DWord (4 bytes) by the old VM — that
    // is the format bug fixed here (use u16).
    // ======================================================================

    /// <summary>
    /// READ-ONLY decode + ROM-MUTATING node write helpers for the FE8 World Map
    /// Path MOVEMENT editor (#1598). All methods never throw and guard every
    /// ROM access. FE8-only (FE6/FE7 reject / return empty).
    ///
    /// <para>The movement base is resolved as <c>p32(record+8)</c> — the
    /// 8-byte-stride movement sub-table — NOT the 12-byte path-record table.
    /// <see cref="WriteNode"/> validates that the target address is a real node
    /// inside a resolved movement sub-table (and not the <c>0xFFFFFFFF</c>
    /// terminator slot), so a write can NEVER corrupt the record table.</para>
    /// </summary>
    public static class WorldMapPathMoveCore
    {
        // FE8 = ROMFEINFO.version 8 (matches WorldMapPathCore's FE8-only gate).
        public const int VERSION_FE8 = 8;
        // 12-byte path-record table entry.
        public const int ENTRY_SIZE = 12;
        // 8-byte movement node.
        public const int NODE_SIZE = 8;
        // Max movement nodes scanned per path (bounded scan headroom).
        public const int MAX_NODES = 256;

        // The u32 0xFFFFFFFF sentinel terminating a movement sub-table.
        const uint TERMINATOR = 0xFFFFFFFF;

        // ==================================================================
        // Path list
        // ==================================================================

        /// <summary>
        /// Build the 12-byte path-record selector list (reuses
        /// <see cref="WorldMapPathCore.MakePathList(ROM)"/>; the path id is
        /// carried in <see cref="AddrResult.tag"/>). Empty on null / non-FE8.
        /// Never throws.
        /// </summary>
        public static List<AddrResult> MakePathList(ROM rom)
        {
            return WorldMapPathCore.MakePathList(rom);
        }

        // ==================================================================
        // Movement-base resolve
        // ==================================================================

        /// <summary>
        /// Resolve the movement sub-table base for path <paramref name="pathId"/>:
        /// <c>moveBase = p32(roadBase + pathId*12 + 8)</c>. Returns false on
        /// null / non-FE8 / pathId&lt;0 / unsafe region / a null or unsafe
        /// movement pointer. READ-ONLY; never throws.
        /// </summary>
        public static bool ResolveMovementBase(ROM rom, int pathId, out uint moveBase)
        {
            moveBase = 0;
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;
            if (rom.RomInfo.version != VERSION_FE8) return false;
            if (pathId < 0) return false;

            uint ptr = rom.RomInfo.worldmap_road_pointer;
            if (!IsRegionSafe(rom, ptr, 4)) return false;
            uint roadBase = rom.p32(ptr);
            if (!U.isSafetyOffset(roadBase, rom)) return false;

            uint entry = roadBase + (uint)pathId * ENTRY_SIZE;
            if (!IsRegionSafe(rom, entry, ENTRY_SIZE)) return false;

            uint mb = rom.p32(entry + 8);
            if (mb == 0) return false;
            if (!U.isSafetyOffset(mb, rom)) return false;
            moveBase = mb;
            return true;
        }

        // ==================================================================
        // Movement-node list
        // ==================================================================

        /// <summary>
        /// Walk the 8-byte-stride movement nodes from <paramref name="moveBase"/>
        /// (the resolved <c>p32(record+8)</c>) until the <c>u32 == 0xFFFFFFFF</c>
        /// terminator — which is NOT emitted. Each node row label shows the
        /// <c>u16</c> ElapsedTime (T) and the (X,Y) <c>u16</c> coords. The node
        /// index is carried in <see cref="AddrResult.tag"/>.
        ///
        /// <para><b>All-or-nothing</b> (matches <see cref="WorldMapPathCore"/>):
        /// any out-of-bounds node yields an EMPTY list, so a partial/corrupt
        /// table cannot reach the editor and be written back truncated.</para>
        /// READ-ONLY; never throws.
        /// </summary>
        public static List<AddrResult> LoadMovementList(ROM rom, uint moveBase)
        {
            var list = new List<AddrResult>();
            if (rom == null || rom.RomInfo == null || rom.Data == null) return list;
            if (rom.RomInfo.version != VERSION_FE8) return list;
            if (!U.isSafetyOffset(moveBase, rom)) return list;

            for (int i = 0; i < MAX_NODES; i++)
            {
                uint addr = moveBase + (uint)(i * NODE_SIZE);
                if (!IsRegionSafe(rom, addr, NODE_SIZE)) return new List<AddrResult>(); // OOB -> empty
                if (rom.u32(addr) == TERMINATOR) break;                                 // terminator -> stop (not emitted)

                uint t = rom.u16(addr + 0);
                uint x = rom.u16(addr + 4);
                uint y = rom.u16(addr + 6);
                list.Add(new AddrResult(addr, $"Node {i}: T={t} ({x},{y})", (uint)i));
            }
            return list;
        }

        // ==================================================================
        // Record / node accessors
        // ==================================================================

        /// <summary>ElapsedTime (u16 @ +0). 0 on any guard failure. READ-ONLY.</summary>
        public static uint ReadElapsedTime(ROM rom, uint nodeAddr)
            => IsRegionSafe(rom, nodeAddr, NODE_SIZE) ? rom.u16(nodeAddr + 0) : 0;

        /// <summary>X (u16 @ +4). 0 on any guard failure. READ-ONLY.</summary>
        public static uint ReadX(ROM rom, uint nodeAddr)
            => IsRegionSafe(rom, nodeAddr, NODE_SIZE) ? rom.u16(nodeAddr + 4) : 0;

        /// <summary>Y (u16 @ +6). 0 on any guard failure. READ-ONLY.</summary>
        public static uint ReadY(ROM rom, uint nodeAddr)
            => IsRegionSafe(rom, nodeAddr, NODE_SIZE) ? rom.u16(nodeAddr + 6) : 0;

        // ==================================================================
        // Node-membership guard
        // ==================================================================

        /// <summary>
        /// True only when <paramref name="nodeAddr"/> is one of the EMITTED node
        /// addresses across every path's movement sub-table. Because
        /// <see cref="LoadMovementList"/> excludes the <c>0xFFFFFFFF</c>
        /// terminator, this naturally rejects the terminator slot, the
        /// record-table base, and any garbage address. READ-ONLY; never throws.
        /// </summary>
        public static bool IsNodeInAnyMovementList(ROM rom, uint nodeAddr)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return false;
            if (rom.RomInfo.version != VERSION_FE8) return false;

            var paths = MakePathList(rom);
            foreach (var p in paths)
            {
                if (!ResolveMovementBase(rom, (int)p.tag, out uint moveBase)) continue;
                var nodes = LoadMovementList(rom, moveBase);
                foreach (var n in nodes)
                    if (n.addr == nodeAddr) return true;
            }
            return false;
        }

        // ==================================================================
        // Write (ROM-mutating)
        // ==================================================================

        /// <summary>
        /// Write a movement node's <c>u16</c> ElapsedTime/X/Y at +0/+4/+6.
        /// FE8-only. The node MUST be a real node inside a resolved movement
        /// sub-table (validated via <see cref="IsNodeInAnyMovementList"/>) and
        /// NOT the <c>0xFFFFFFFF</c> terminator slot — so a write can never
        /// corrupt the 12-byte record table or a terminator.
        ///
        /// <para><b>Validate-before-mutate</b> + a <c>rom.Data.Clone()</c>
        /// snapshot taken AFTER validation but BEFORE the first write, with a
        /// length-aware byte-identical fault restore (#885/#923): a FAILED write
        /// mutates ZERO bytes. Values are clamped to <c>0xFFFF</c>. All writes
        /// use the ambient-undo overloads so they land in the caller's ambient
        /// <c>ROM.BeginUndoScope</c>.</para>
        /// </summary>
        /// <returns>Empty string on success; a non-empty user-facing error
        /// (with ZERO ROM mutation) on any failure. Never throws.</returns>
        public static string WriteNode(ROM rom, uint nodeAddr, uint time, uint x, uint y)
        {
            if (rom == null || rom.RomInfo == null || rom.Data == null) return R.Error("ROM not loaded.");
            if (rom.RomInfo.version != VERSION_FE8) return R.Error("World map path movement is FE8-only.");

            // Reject the record-table base, the terminator slot, and any
            // address that is not a real movement node (validate before mutate).
            if (!IsRegionSafe(rom, nodeAddr, NODE_SIZE)) return R.Error("Movement node address is out of range.");
            if (!IsNodeInAnyMovementList(rom, nodeAddr))
                return R.Error("Refusing to write: the target address is not a world map path movement node.");

            // Clamp to the u16 field width.
            uint t = time > 0xFFFF ? 0xFFFF : time;
            uint cx = x > 0xFFFF ? 0xFFFF : x;
            uint cy = y > 0xFFFF ? 0xFFFF : y;

            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                rom.write_u16(nodeAddr + 0, t);
                rom.write_u16(nodeAddr + 4, cx);
                rom.write_u16(nodeAddr + 6, cy);
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R.Error("World map path movement write failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Length-aware byte-identical restore (#885/#923): down-resize to the
        /// snapshot length before the in-place copy (a movement-node write does
        /// not grow rom.Data, but keep parity with the WorldMapPathCore idiom).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }

        // ==================================================================
        // Local guards
        // ==================================================================

        /// <summary>
        /// True when <c>[addr, addr+bytes)</c> is a valid in-ROM region
        /// (isSafetyOffset domain + an explicit ulong end-of-range check so the
        /// addition cannot overflow). Mirrors WorldMapPathCore.IsRegionSafe.
        /// </summary>
        static bool IsRegionSafe(ROM rom, uint addr, int bytes)
        {
            if (rom == null || rom.Data == null) return false;
            if (!U.isSafetyOffset(addr, rom)) return false;
            if (bytes <= 0) return false;
            ulong lastByte = (ulong)addr + (ulong)bytes - 1UL;
            return lastByte < (ulong)rom.Data.Length;
        }
    }
}
