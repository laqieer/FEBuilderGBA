using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Core helpers for the C-String editor. Ports the read + write
    /// path of WinForms <c>CStringForm</c> (FEBuilderGBA/CStringForm.cs) — a
    /// pointer-bound field helper that edits a NUL-terminated, system-encoded
    /// C-string somewhere in the ROM.
    ///
    /// <para>READ (<see cref="ReadCString"/>) mirrors <c>CStringForm.Init</c> →
    /// <c>TextForm.Direct(pointer)</c>: the safety-pointer branch of
    /// <c>FETextDecode.Direct</c> (<c>ROM.getString</c> + <c>RevConvertSPMoji</c>),
    /// then the <c>@001F</c> strip + <c>ConvertEscapeText</c> that
    /// <c>TextForm.Direct</c> layers on top.</para>
    ///
    /// <para>WRITE (<see cref="WriteCString"/>) mirrors
    /// <c>CStringForm.WriteCString</c> → <c>InputFormRef.WriteBinaryData</c>:
    /// <c>SystemTextEncoder.Encode(text)</c> + a trailing <c>0x00</c> (RAW — NOT
    /// the symmetric reverse of the read path; this is a deliberate WinForms
    /// quirk), in-place when the new bytes fit the old region (zero-filling the
    /// surplus) else append to free space + repoint every discoverable reference
    /// (raw + LDR) + the explicit parent slot + zero the old region.</para>
    ///
    /// <para>The write skeleton intentionally reuses the proven, fault-safe
    /// <c>AoeRangeCore.Move</c> sequence (early orphan guard BEFORE any allocation,
    /// active-ROM guard, defensive cleanup) so a grow can never leave an orphaned
    /// C-string or un-undoable ROM growth behind.</para>
    /// </summary>
    public static class CStringCore
    {
        /// <summary>Result kind of a <see cref="WriteCString"/> call.</summary>
        public enum WriteStatus
        {
            /// <summary>Wrote in place; the string stayed at the same address.</summary>
            InPlace = 0,
            /// <summary>Grew/appended the string; it moved and references were repointed.</summary>
            Moved = 1,
            /// <summary>No mutation was performed (refused) — see the message.</summary>
            Refused = 2,
        }

        /// <summary>Outcome of a <see cref="WriteCString"/> call.</summary>
        public sealed class WriteResult
        {
            public WriteStatus Status;
            /// <summary>The OFFSET the string now lives at (== old offset for
            /// <see cref="WriteStatus.InPlace"/>; the new offset for
            /// <see cref="WriteStatus.Moved"/>; <see cref="U.NOT_FOUND"/> for
            /// <see cref="WriteStatus.Refused"/>). Always an offset — callers that
            /// surface it to the UI convert to GBA pointer form themselves.</summary>
            public uint Address = U.NOT_FOUND;
            /// <summary>Number of references repointed on a move (raw + LDR + parent slot).</summary>
            public int RepointedSlots;
            /// <summary>Human-readable reason when <see cref="Status"/> is
            /// <see cref="WriteStatus.Refused"/>; empty otherwise.</summary>
            public string Message = string.Empty;
        }

        // ------------------------------------------------------------------
        // READ-ONLY
        // ------------------------------------------------------------------

        /// <summary>
        /// Decode the C-string at <paramref name="pointer"/> (GBA pointer OR raw
        /// offset — the Avalonia manual-address box supplies a decimal OFFSET, the
        /// WinForms pointer field supplies a GBA pointer). A raw safety offset is
        /// normalized to a GBA pointer before decoding so both call paths work.
        /// Returns <c>""</c> for an address that is neither a safety pointer nor a
        /// safety offset (matches <c>CStringForm.Init</c>'s early return). Faithfully
        /// ports <c>TextForm.Direct(pointer)</c>. READ-ONLY, never throws.
        /// </summary>
        public static string ReadCString(ROM rom, uint pointer)
        {
            if (rom == null) return string.Empty;

            // Accept a raw OFFSET too: FETextDecode.Decode treats small values as
            // text IDs, so a manual offset like 0x1000 must be promoted to its GBA
            // pointer form (0x08001000) to take the CString branch.
            if (!U.isSafetyPointer(pointer, rom))
            {
                uint offset = U.toOffset(pointer);
                if (U.isSafetyOffset(offset, rom))
                {
                    pointer = U.toPointer(offset);
                }
                else
                {
                    return string.Empty;
                }
            }

            try
            {
                // FETextDecode.Direct(pointer): the safety-pointer branch decodes a
                // plain C-string (ROM.getString + RevConvertSPMoji). We use the
                // rom-aware FETextDecode instance so this never touches the ambient
                // ROM when a non-active ROM is passed.
                FETextDecode decoder = new FETextDecode(rom, CoreState.SystemTextEncoder);
                string str = decoder.Decode(pointer);

                // The extra steps TextForm.Direct layers on top of FETextDecode.Direct.
                str = str.Replace("@001F", "");
                str = ToolTranslateROMCore.ConvertEscapeText(str);
                return str;
            }
            catch (Exception ex)
            {
                Log.Error("CStringCore.ReadCString(0x" + pointer.ToString("X8") + ") failed: " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Compute the reusable length of the OLD C-string region at
        /// <paramref name="offset"/>. Ports <c>get_cstring_data_pos_callback</c>
        /// (<c>Padding2(strlen + 1)</c>) AND the <c>WriteBinaryData</c> clamping
        /// rules: a length ≥ 0x200000 is treated as corrupt (⇒ 0, force relocate);
        /// a length that runs past EOF is clamped to the remaining tail
        /// (<c>Data.Length - addr</c>) so the in-place path can still reuse the
        /// tail — exactly like WF. READ-ONLY, never throws.
        /// </summary>
        public static uint OldRegionLength(ROM rom, uint offset)
        {
            if (rom == null) return 0;
            offset = U.toOffset(offset);
            if (!U.isSafetyOffset(offset, rom)) return 0;

            // strlen up to the NUL (getString stops at the first 0x00). +1 for the
            // NUL, padded to 2 — verbatim get_cstring_data_pos_callback.
            int length;
            rom.getString(offset, out length);
            uint original = U.Padding2((uint)length + 1);

            if (original >= 0x00200000)
            {
                // Too long => corrupt => not reusable (WF logs + sets original_size=0).
                return 0;
            }
            if ((long)offset + original > rom.Data.Length)
            {
                // Runs past EOF => clamp to the real tail (WF: original_size =
                // Data.Length - addr), NOT 0 — lets the in-place tail reuse happen.
                return (uint)rom.Data.Length - offset;
            }
            return original;
        }

        // ------------------------------------------------------------------
        // ROM-MUTATING
        // ------------------------------------------------------------------

        /// <summary>
        /// Encode <paramref name="text"/> as a C-string (<c>SystemTextEncoder.Encode</c>
        /// + trailing <c>0x00</c> — verbatim WF <c>WriteCString</c>) and write it at
        /// <paramref name="pointer"/>. In-place when the new bytes fit the old
        /// region; otherwise append + repoint all discoverable references and the
        /// explicit <paramref name="parentPointerSlot"/> (when non-zero), then zero
        /// the old region. A grow/move is REFUSED (no mutation) when it would
        /// orphan the data (no parent slot AND no discoverable reference) — the
        /// safe rule from <c>AoeRangeCore.Move</c>.
        ///
        /// <para>Must be called inside an active ambient <c>ROM.BeginUndoScope</c>;
        /// every write is ambient-recorded.</para>
        /// </summary>
        /// <param name="rom">Target ROM (must be <c>CoreState.ROM</c> for a move).</param>
        /// <param name="parentPointerSlot">ROM offset of the pointer slot that
        /// references this string (0 = standalone / manual path).</param>
        /// <param name="pointer">GBA pointer OR offset of the existing string
        /// (0 = fresh append, needs a parent slot to be reachable).</param>
        /// <param name="text">The new string (escape tokens kept verbatim).</param>
        public static WriteResult WriteCString(ROM rom, uint parentPointerSlot, uint pointer, string text)
        {
            var result = new WriteResult();
            if (rom == null)
            {
                result.Status = WriteStatus.Refused;
                result.Message = "Refused: ROM not loaded.";
                return result;
            }

            // --- Encode (validate-before-mutate) ------------------------------
            byte[] bin;
            try
            {
                byte[] enc = CoreState.SystemTextEncoder.Encode(text ?? string.Empty);
                bin = U.ArrayAppend(enc, new byte[] { 0x00 });
            }
            catch (Exception ex)
            {
                result.Status = WriteStatus.Refused;
                result.Message = "Refused: could not encode the string (" + ex.Message + ").";
                return result;
            }

            uint slot = U.toOffset(parentPointerSlot);
            uint writeAddr = U.toOffset(pointer);

            // A zero (or unset) data address means "create new" — only reachable
            // through a parent slot (a NULL CSTRING pointer field).
            if (writeAddr == 0)
            {
                if (slot == 0)
                {
                    result.Status = WriteStatus.Refused;
                    result.Message = "Refused: no address to write to (enter a valid pointer, or arrive from a CSTRING field).";
                    return result;
                }
                return Move(rom, result, bin, slot, oldAddr: 0, oldLen: 0);
            }

            // A non-zero but UNSAFE address is a corruption guard, not a request.
            if (!U.isSafetyOffset(writeAddr, rom))
            {
                result.Status = WriteStatus.Refused;
                result.Message = "Refused: unsafe string address (0x" + writeAddr.ToString("X8") + ").";
                return result;
            }

            // --- In-place write (new fits the old region) ---------------------
            uint originalSize = OldRegionLength(rom, writeAddr);
            if (bin.Length <= originalSize)
            {
                rom.write_range(writeAddr, bin); // ambient-recorded.
                int surplus = (int)originalSize - bin.Length;
                if (surplus > 0)
                {
                    // Zero the surplus tail (WF:余剰領域は0x00クリア).
                    rom.write_range(writeAddr + (uint)bin.Length, new byte[surplus]);
                }
                result.Status = WriteStatus.InPlace;
                result.Address = writeAddr;
                return result;
            }

            // --- Grow: append + repoint all references + zero the old region --
            return Move(rom, result, bin, slot, oldAddr: writeAddr, oldLen: originalSize);
        }

        /// <summary>
        /// Append <paramref name="bin"/> to free space and wire up pointers. Used
        /// by both the fresh-append and grow paths. Refuses (no mutation) when the
        /// move would orphan the data or when <c>CoreState.ROM</c> is not the
        /// target ROM. Ported from <c>AoeRangeCore.Move</c> step-for-step.
        /// </summary>
        static WriteResult Move(ROM rom, WriteResult result, byte[] bin, uint slot,
            uint oldAddr, uint oldLen)
        {
            // RecycleAddress writes through CoreState.ROM; a move is unsafe unless
            // it IS the target ROM (matches the other ROM-mutating Core ports).
            if (!ReferenceEquals(CoreState.ROM, rom))
            {
                result.Status = WriteStatus.Refused;
                result.Message = "Refused: the C-string must move, which requires the active ROM.";
                return result;
            }

            // EARLY orphan guard — refuse BEFORE any allocation when the move would
            // leave the data unreachable. RecycleAddress.WriteAmbient can grow the
            // ROM (a resize that is NOT ambient-undo-tracked); refusing here keeps a
            // doomed move from leaving that growth behind. A move is reachable only
            // if there is a USABLE explicit parent slot OR a discoverable reference.
            // slotUsable validates the WHOLE 4-byte slot (start AND last byte) so a
            // danger-zone / near-EOF slot can never reach the p32/write_p32 below.
            bool slotUsable = slot != 0
                && U.isSafetyOffset(slot, rom)
                && U.isSafetyOffset(slot + 3, rom);
            if (!slotUsable && !HasAnyReference(rom, oldAddr))
            {
                result.Status = WriteStatus.Refused;
                result.Message = "Refused: no reference to the old C-string was found, so the moved string would be orphaned.";
                return result;
            }

            // Append to free space (ambient undo). Returns the OFFSET written.
            var recycle = new RecycleAddress();
            uint newAddr = recycle.WriteAmbient(bin);
            if (newAddr == U.NOT_FOUND)
            {
                result.Status = WriteStatus.Refused;
                result.Message = "Refused: ran out of ROM free space while writing the C-string.";
                return result;
            }

            int repointed = 0;
            bool repointedExplicitSlot = false;

            // Repoint EVERY discoverable reference to the old data (raw + LDR),
            // mirroring WF MoveToFreeSapceForm.SearchPointer. (No old data on a
            // fresh append — oldAddr == 0.) Pass null undo so the already-active
            // ambient scope records each repoint exactly once.
            if (oldAddr != 0)
            {
                repointed = DataExpansionCore.RepointAllReferences(rom, oldAddr, newAddr, null);
            }

            // Also repoint the explicit parent slot when it is USABLE and the rescan
            // did not already cover it (a slot inside ASM the scanner skips, or a
            // fresh append with no old data to scan). Gated on slotUsable (NOT bare
            // slot != 0) so an invalid/danger-zone slot can never make p32/write_p32
            // read or write out of bounds or corrupt the ROM header.
            if (slotUsable)
            {
                uint cur = rom.p32(slot);
                if (cur != newAddr)
                {
                    rom.write_p32(slot, newAddr);
                    repointed++;
                }
                repointedExplicitSlot = true;
            }

            // Defensive net: the EARLY orphan guard already refuses an unreachable
            // move before allocation, so this should be unreachable. If a race
            // somehow leaves the new data unreferenced, zero the bytes we wrote
            // (ambient-recorded) so a caller that commits does not leak it.
            if (repointed == 0 && !repointedExplicitSlot)
            {
                rom.write_range(newAddr, new byte[bin.Length]);
                result.Status = WriteStatus.Refused;
                result.Message = "Refused: no reference to the old C-string was found, so the moved string would be orphaned.";
                return result;
            }

            // Zero the old region (under ambient undo) — WF WriteBinaryData zeroes
            // the vacated source. Skip on a fresh append (no old data).
            if (oldAddr != 0 && oldLen >= 1
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
        /// mutates, never throws. Returns false for <c>oldAddr == 0</c>.
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
