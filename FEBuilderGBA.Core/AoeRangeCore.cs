using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Core helpers for the Area-of-Effect (AOE) Range editor.
    /// Ports the data model + write path of WinForms <c>AOERANGEForm</c>
    /// (FEBuilderGBA/AOERANGEForm.cs), which edits a variable-length
    /// <c>byte[4 + w*h]</c> structure:
    /// <list type="bullet">
    ///   <item><c>u8 @ +0</c> — grid width  (W)</item>
    ///   <item><c>u8 @ +1</c> — grid height (H)</item>
    ///   <item><c>u8 @ +2</c> — center X</item>
    ///   <item><c>u8 @ +3</c> — center Y</item>
    ///   <item><c>byte[W*H] @ +4</c> — the AoE mask cells (row-major; 1 = attacked tile, 0 = not)</item>
    /// </list>
    /// The length model is the same one already exposed by
    /// <c>RebuildProducerCore.EmitAoeRangePointer</c> (4 + w*h, BIN).
    /// READ-ONLY (<see cref="ReadAoeRange"/>, <see cref="BuildBinary"/>) and
    /// ROM-MUTATING (<see cref="WriteAoeRange"/>) members are clearly separated.
    /// </summary>
    public static class AoeRangeCore
    {
        /// <summary>Result of a <see cref="WriteAoeRange"/> call.</summary>
        public enum WriteStatus
        {
            /// <summary>Wrote in place; the record stayed at the same address.</summary>
            InPlace = 0,
            /// <summary>Grew/appended the record; it moved and references were repointed.</summary>
            Moved = 1,
            /// <summary>No mutation was performed (refused) — see the message.</summary>
            Refused = 2,
        }

        /// <summary>Outcome of a <see cref="WriteAoeRange"/> call.</summary>
        public sealed class WriteResult
        {
            public WriteStatus Status;
            /// <summary>The address the record now lives at (== old addr for
            /// <see cref="WriteStatus.InPlace"/>; the new offset for
            /// <see cref="WriteStatus.Moved"/>; <see cref="U.NOT_FOUND"/> for
            /// <see cref="WriteStatus.Refused"/>).</summary>
            public uint Address = U.NOT_FOUND;
            /// <summary>Number of references repointed on a move (raw + LDR).</summary>
            public int RepointedSlots;
            /// <summary>Human-readable reason when <see cref="Status"/> is
            /// <see cref="WriteStatus.Refused"/>; empty otherwise.</summary>
            public string Message = string.Empty;
        }

        /// <summary>
        /// Decoded AOE-range record. <see cref="Cells"/> is always
        /// <c>Width*Height</c> bytes (row-major).
        /// </summary>
        public sealed class AoeRangeData
        {
            public uint Width;
            public uint Height;
            public uint CenterX;
            public uint CenterY;
            public byte[] Cells = Array.Empty<byte>();

            /// <summary>
            /// Linear index of the highlighted center cell
            /// (<c>CenterX + CenterY*Width</c>), or -1 when out of range.
            /// Mirrors WinForms <c>UpdateCenterMark</c>.
            /// </summary>
            public int CenterIndex
            {
                get
                {
                    if (Width == 0) return -1;
                    long idx = (long)CenterX + (long)CenterY * Width;
                    if (idx < 0 || idx >= Cells.Length) return -1;
                    return (int)idx;
                }
            }
        }

        /// <summary>
        /// Linear index of the highlighted center cell for a header
        /// (<c>cx + cy*w</c>), or -1 when out of range. Mirrors WinForms
        /// <c>UpdateCenterMark</c>; usable without a decoded record.
        /// </summary>
        public static int CenterIndex(uint w, uint h, uint cx, uint cy)
        {
            if (w == 0) return -1;
            long idx = (long)cx + (long)cy * w;
            if (idx < 0 || idx >= (long)w * h) return -1;
            return (int)idx;
        }

        // ------------------------------------------------------------------
        // READ-ONLY
        // ------------------------------------------------------------------

        /// <summary>
        /// Read an AOE-range record from <paramref name="addr"/>. Ports WinForms
        /// <c>AOERANGEForm.JumpTo</c> (FEBuilderGBA/AOERANGEForm.cs:182-223):
        /// normalise the address, read <c>w/h/cx/cy</c>, bounds-check the whole
        /// <c>4 + w*h</c> extent, then read the <c>w*h</c> grid cells at <c>+4</c>.
        /// </summary>
        /// <returns>The decoded record, or <c>null</c> when the address (or the
        /// w*h extent) is unsafe / out of bounds — never throws.</returns>
        public static AoeRangeData ReadAoeRange(ROM rom, uint addr)
        {
            if (rom == null) return null;
            addr = U.toOffset(addr);
            // Guard the FULL 4-byte header (w/h/cx/cy) before reading. WF checks
            // addr+2 (its NumericUpDowns tolerate a short tail), but we read cx@+2
            // and cy@+3, so guard addr+3 to honour the never-throw contract at EOF.
            if (!U.isSafetyOffset(addr + 3, rom)) return null;

            uint w = rom.u8(addr + 0);
            uint h = rom.u8(addr + 1);
            uint cx = rom.u8(addr + 2);
            uint cy = rom.u8(addr + 3);

            uint length = w * h;
            // WF: if (!isSafetyOffset(addr + 4 + length - 1)) return;
            // (length==0 still yields an empty grid — header already bounds-checked.)
            if (length > 0 && !U.isSafetyOffset(addr + 4 + length - 1, rom)) return null;

            var data = new AoeRangeData
            {
                Width = w,
                Height = h,
                CenterX = cx,
                CenterY = cy,
                Cells = new byte[length],
            };
            for (uint n = 0; n < length; n++)
            {
                data.Cells[n] = (byte)rom.u8(addr + 4 + n);
            }
            return data;
        }

        /// <summary>
        /// Build the raw <c>byte[4 + w*h]</c> payload from a header + grid cells.
        /// Ports the buffer construction in WinForms
        /// <c>AOERANGEForm.WriteButton_Click</c> (lines 249-262).
        /// </summary>
        /// <param name="cells">Exactly <c>w*h</c> grid bytes (row-major). A
        /// <c>null</c> or short array is zero-padded; extra cells are ignored.</param>
        /// <remarks>Never throws: <paramref name="w"/>/<paramref name="h"/> are
        /// clamped to the 0..255 the on-ROM <c>u8</c> header can hold, so the cell
        /// count can never exceed 255*255 (no overflow / OOM on absurd inputs).</remarks>
        public static byte[] BuildBinary(uint w, uint h, uint cx, uint cy, byte[] cells)
        {
            w &= 0xFF;
            h &= 0xFF;
            int count = (int)(w * h); // <= 255*255 = 65025; fits int, never overflows.
            byte[] bin = new byte[4 + count];
            U.write_u8(bin, 0, w & 0xFF);
            U.write_u8(bin, 1, h & 0xFF);
            U.write_u8(bin, 2, cx & 0xFF);
            U.write_u8(bin, 3, cy & 0xFF);

            if (cells != null)
            {
                int n = Math.Min(count, cells.Length);
                for (int i = 0; i < n; i++)
                {
                    bin[4 + i] = cells[i];
                }
            }
            return bin;
        }

        // ------------------------------------------------------------------
        // ROM-MUTATING
        // ------------------------------------------------------------------

        /// <summary>
        /// Write an AOE-range record, porting WinForms
        /// <c>AOERANGEForm.WriteButton_Click</c> + <c>get_data_pos_callback</c> +
        /// <c>InputFormRef.WriteBinaryData</c>:
        /// <list type="number">
        ///   <item>build <c>byte[4 + w*h]</c> from the header + grid cells;</item>
        ///   <item>compute the OLD record length (<c>4 + oldW*oldH</c>) at
        ///         <paramref name="addr"/>;</item>
        ///   <item>if the new payload fits the old region — overwrite IN PLACE and
        ///         zero-fill the surplus (no move, no repoint);</item>
        ///   <item>otherwise APPEND the new payload to free space, repoint EVERY
        ///         reference to the old data (raw 32-bit pointers AND ARM LDR
        ///         literal-pool loads, via
        ///         <see cref="DataExpansionCore.RepointAllReferences"/> — the same
        ///         all-reference rescan WinForms <c>MoveToFreeSapceForm.SearchPointer</c>
        ///         performs), ALSO repoint an explicit <paramref name="parentPointerSlot"/>
        ///         when supplied, then ZERO the old region.</item>
        /// </list>
        ///
        /// <para><b>Orphan guard:</b> a move (grow / fresh append) that would leave
        /// the new data with NO pointer to it is REFUSED with no mutation. Concretely
        /// a move requires at least one of: an explicit <paramref name="parentPointerSlot"/>,
        /// or at least one discoverable reference to the old data. (An in-place write
        /// never moves, so it never needs a pointer.)</para>
        ///
        /// <para><b>Active-ROM requirement (move only):</b> the append routes through
        /// <see cref="RecycleAddress"/>, which writes via <c>CoreState.ROM</c>; a move
        /// therefore requires <c>ReferenceEquals(CoreState.ROM, rom)</c> and is refused
        /// otherwise. An in-place write uses the explicit <paramref name="rom"/> and has
        /// no such requirement.</para>
        ///
        /// <para>Validate-all-before-mutate. The caller is expected to have an ambient
        /// <see cref="ROM.BeginUndoScope"/> open; every write here routes through the
        /// no-undo overloads so the ambient scope records each write exactly once.</para>
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="parentPointerSlot">ROM offset (or GBA pointer) of a 32-bit
        /// pointer slot that references this record (the WinForms <c>ParentNumnic</c>
        /// slot). <c>0</c> means "no known parent" (the standalone manual-address path);
        /// a move then succeeds only if other references are discoverable.</param>
        /// <param name="addr">Current record address (raw offset or GBA pointer).
        /// <c>0</c> requests a fresh append (which requires a parent slot).</param>
        /// <param name="w">Width (0..255).</param>
        /// <param name="h">Height (0..255).</param>
        /// <param name="cx">Center X.</param>
        /// <param name="cy">Center Y.</param>
        /// <param name="cells">Exactly <c>w*h</c> grid bytes (row-major).</param>
        /// <returns>A <see cref="WriteResult"/> describing the outcome; never throws.</returns>
        public static WriteResult WriteAoeRange(ROM rom, uint parentPointerSlot, uint addr,
            uint w, uint h, uint cx, uint cy, byte[] cells)
        {
            var result = new WriteResult { Status = WriteStatus.Refused, Address = U.NOT_FOUND };
            if (rom == null)
            {
                result.Message = "ROM not loaded.";
                return result;
            }
            if (w > 0xFF || h > 0xFF)
            {
                result.Message = "Width/Height out of range (0..255).";
                return result;
            }

            byte[] bin = BuildBinary(w, h, cx, cy, cells);

            uint writeAddr = U.toOffset(addr);
            uint slot = parentPointerSlot != 0 ? U.toOffset(parentPointerSlot) : 0;
            bool haveSlot = slot != 0 && U.isSafetyOffset(slot + 3, rom);

            // --- Fresh append (addr == 0) -------------------------------------
            if (writeAddr == 0)
            {
                if (!haveSlot)
                {
                    result.Message = "Cannot create a new AOE record without a parent pointer slot (it would be orphaned).";
                    return result;
                }
                return Move(rom, result, bin, slot, oldAddr: 0, oldLen: 0);
            }

            // A non-zero but UNSAFE address is a corruption guard, not a request.
            if (!U.isSafetyOffset(writeAddr, rom))
            {
                result.Message = "Refused: unsafe record address (0x" + writeAddr.ToString("X8") + ").";
                return result;
            }

            // Compute the OLD region length (4 + oldW*oldH) — ports
            // get_data_pos_callback + the WriteBinaryData clamping rules.
            uint originalSize = 0;
            if (U.isSafetyOffset(writeAddr + 1, rom))
            {
                uint oldW = rom.u8(writeAddr + 0);
                uint oldH = rom.u8(writeAddr + 1);
                originalSize = 4 + (oldW * oldH);
            }
            if (originalSize >= 0x00200000)
            {
                originalSize = 0; // too long => no reusable region.
            }
            else if ((long)writeAddr + originalSize > rom.Data.Length)
            {
                // The existing header claims a record that runs PAST EOF — it is
                // malformed. Do NOT treat the ROM tail as reusable "surplus" (that
                // would let the in-place path zero-fill the rest of the ROM). Force
                // the safer move/append path by declaring no reusable region.
                originalSize = 0;
            }

            // --- In-place write (new fits the old region) ---------------------
            if (bin.Length <= originalSize)
            {
                rom.write_range(writeAddr, bin); // explicit rom, ambient-recorded.
                int surplus = (int)originalSize - bin.Length;
                if (surplus > 0)
                {
                    rom.write_range(writeAddr + (uint)bin.Length, new byte[surplus]);
                }
                result.Status = WriteStatus.InPlace;
                result.Address = writeAddr;
                return result;
            }

            // --- Grow: append + repoint all references + zero the old region --
            return Move(rom, result, bin, haveSlot ? slot : 0, oldAddr: writeAddr, oldLen: originalSize);
        }

        /// <summary>
        /// Append <paramref name="bin"/> to free space and wire up pointers. Used by
        /// both the fresh-append and grow paths. Refuses (no mutation) when the move
        /// would orphan the data or when <c>CoreState.ROM</c> is not the target ROM.
        /// </summary>
        static WriteResult Move(ROM rom, WriteResult result, byte[] bin, uint slot,
            uint oldAddr, uint oldLen)
        {
            // RecycleAddress writes through CoreState.ROM; a move is unsafe unless it
            // IS the target ROM (matches the other ROM-mutating Core ports).
            if (!ReferenceEquals(CoreState.ROM, rom))
            {
                result.Message = "Refused: the AOE record must move, which requires the active ROM.";
                return result;
            }

            // EARLY orphan guard — refuse BEFORE any allocation when the move would
            // leave the data unreachable. Without this, RecycleAddress.WriteAmbient
            // could grow the ROM (a resize that is NOT ambient-undo-tracked) and a
            // later refusal would leave that size change behind. A move is reachable
            // only if there is an explicit parent slot OR at least one discoverable
            // reference to the old data.
            bool slotUsable = slot != 0 && U.isSafetyOffset(slot + 3, rom);
            if (!slotUsable && !HasAnyReference(rom, oldAddr))
            {
                result.Message = "Refused: no reference to the old AOE data was found, so the moved data would be orphaned.";
                return result;
            }

            // Append to free space (ambient undo). RecycleAddress.WriteAmbient
            // returns the OFFSET of the freshly written data.
            var recycle = new RecycleAddress();
            uint newAddr = recycle.WriteAmbient(bin);
            if (newAddr == U.NOT_FOUND)
            {
                result.Message = "Refused: ran out of ROM free space while writing the AOE record.";
                return result;
            }

            int repointed = 0;
            bool repointedExplicitSlot = false;

            // Repoint EVERY discoverable reference to the old data (raw + LDR),
            // mirroring WF MoveToFreeSapceForm.SearchPointer. (No old data on a
            // fresh append — oldAddr == 0 — so there is nothing to rescan/zero.)
            if (oldAddr != 0)
            {
                // Pass null undo: RepointAllReferences then opens NO scope of its
                // own and lets the already-active ambient BeginUndoScope (opened by
                // the View's UndoService) record each repoint exactly once. Passing
                // the ambient UndoData instead would make it open a NESTED scope and
                // null out the ambient on dispose, breaking our later writes.
                repointed = DataExpansionCore.RepointAllReferences(rom, oldAddr, newAddr, null);
            }

            // Also repoint the explicit parent slot (if any) when the rescan did not
            // already cover it (e.g. a slot inside ASM the scanner skipped, or a
            // fresh append with no old data to scan).
            if (slot != 0)
            {
                uint cur = rom.p32(slot);
                if (cur != newAddr)
                {
                    rom.write_p32(slot, newAddr);
                    repointed++;
                }
                repointedExplicitSlot = true;
            }

            // Defensive net: the EARLY orphan guard above already refuses an
            // unreachable move before allocation, so this should be unreachable. If
            // a race somehow leaves the new data unreferenced, zero the bytes we
            // wrote (ambient-recorded) so a caller that commits does not leak it.
            if (repointed == 0 && !repointedExplicitSlot)
            {
                rom.write_range(newAddr, new byte[bin.Length]);
                result.Message = "Refused: no reference to the old AOE data was found, so the moved data would be orphaned.";
                return result;
            }

            // Zero the old region (under ambient undo) — WF WriteBinaryData zeroes
            // the vacated source. Skip on a fresh append (no old data).
            if (oldAddr != 0 && oldLen >= 4
                && U.isSafetyOffset(oldAddr, rom)
                && (long)oldAddr + oldLen <= rom.Data.Length)
            {
                rom.write_range(oldAddr, new byte[oldLen]);
            }

            result.Status = WriteStatus.Moved;
            result.Address = newAddr;
            result.RepointedSlots = repointed;
            return result;
        }

        /// <summary>
        /// READ-ONLY: true when at least one safe, repointable reference to
        /// <paramref name="oldAddr"/> exists (raw 32-bit pointer OR ARM LDR
        /// literal-pool load), using the same scanners as
        /// <see cref="DataExpansionCore.RepointAllReferences"/>. A danger-zone /
        /// out-of-ROM slot is NOT counted (it would be skipped on repoint). Never
        /// mutates, never throws. Returns false for <c>oldAddr == 0</c> (a fresh
        /// append has no old data to reference).
        /// </summary>
        static bool HasAnyReference(ROM rom, uint oldAddr)
        {
            if (rom == null || oldAddr == 0) return false;
            uint oldOffset = U.toOffset(oldAddr);
            if (!U.isSafetyOffset(oldOffset, rom)) return false;

            uint oldPtr = U.toPointer(oldAddr);
            foreach (uint slot in U.GrepPointerAll(rom.Data, oldPtr))
            {
                if (U.isSafetyOffset(slot, rom) && slot + 4 <= (uint)rom.Data.Length)
                    return true;
            }
            foreach (uint slot in U.GrepPointerAllOnLDR(rom.Data, oldPtr))
            {
                if (U.isSafetyOffset(slot, rom) && slot + 4 <= (uint)rom.Data.Length)
                    return true;
            }
            return false;
        }
    }
}
