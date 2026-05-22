using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Core helpers for the LZ77 Tool — Move and Recompress tabs.
    /// Used by both WinForms <c>ToolLZ77Form</c> (move/recompress) and Avalonia
    /// <c>ToolLZ77ViewModel</c> (move/recompress). Closes issue #371's residual
    /// scope (Move + Recompress tabs) from PR #468.
    ///
    /// Undo model
    /// ----------
    /// Helpers DO NOT take an <c>Undo.UndoData</c> parameter. They rely
    /// exclusively on <c>ROM.BeginUndoScope(undoData)</c> (ambient undo).
    /// All writes use the no-undo <c>rom.write_*()</c> overloads — those
    /// automatically record to the ambient scope when active, or skip
    /// recording when not active. Callers wanting undo MUST set up a scope
    /// (e.g., Avalonia <c>UndoService.Begin</c>) before calling. This avoids
    /// the double-record bug where explicit + ambient undo both append.
    ///
    /// Pointer-search scope
    /// --------------------
    /// <see cref="SearchPointersForAddress"/> is explicitly scoped to LDR +
    /// raw-pointer search only. It does NOT mirror full WinForms
    /// <c>MoveToFreeSapceForm.SearchPointer</c> ordering. Specifically, it
    /// omits the <c>GrepPointerAllOnEvent</c> path (which depends on
    /// <c>U.MakeAllStructPointersList</c> — the Form-coupled aggregator that
    /// cannot be ported to Core without dragging every WinForms editor's
    /// <c>MakeAllDataLength</c> method along).
    ///
    /// Result flag <c>MissingEventAwareCoverage</c> is always true so the
    /// caller can surface a clear "event-aware search not included" notice.
    /// </summary>
    public static class LZ77ToolCore
    {
        /// <summary>Sanity cap mirroring WinForms InputFormRef.MoveBinaryData (2 MB).</summary>
        public const uint MOVE_LENGTH_LIMIT = 0x00200000;

        /// <summary>Result of <see cref="SearchPointersForAddress"/>.</summary>
        public class SearchPointerResult
        {
            /// <summary>Deduped list of pointer-write locations referring to the target address.</summary>
            public List<uint> Pointers { get; set; } = new List<uint>();

            /// <summary>
            /// True when LDR search returned zero hits and the result came from
            /// raw binary <c>GrepPointerAll</c>. Callers should surface an
            /// explicit confirmation prompt before rewriting these.
            /// </summary>
            public bool UsedRawFallback { get; set; }

            /// <summary>
            /// Always true. Documents that the Core helper does not include
            /// WinForms' event/struct-aware path (<c>GrepPointerAllOnEvent</c>
            /// → <c>MakeAllStructPointersList</c>). Users who need that
            /// coverage should perform Move/Recompress in WinForms.
            /// </summary>
            public bool MissingEventAwareCoverage { get; set; } = true;
        }

        /// <summary>Result of <see cref="MoveCompressedData"/>.</summary>
        public class MoveResult
        {
            /// <summary>True iff the move was applied successfully.</summary>
            public bool Ok { get; set; }

            /// <summary>Destination offset of moved data (resolved if auto-allocated).</summary>
            public uint NewAddress { get; set; }

            /// <summary>Pointer-write locations rewritten by this move.</summary>
            public List<uint> PointersRewritten { get; set; } = new List<uint>();

            /// <summary>True when raw-pointer fallback was used (no LDR hits).</summary>
            public bool UsedRawFallback { get; set; }

            /// <summary>True when destination was auto-allocated (<paramref name="toAddr"/> was 0).</summary>
            public bool AutoAllocated { get; set; }

            /// <summary>User-facing error message when <see cref="Ok"/> is false.</summary>
            public string ErrorMessage { get; set; } = string.Empty;
        }

        /// <summary>Result of <see cref="RecompressAt"/>.</summary>
        public class RecompressResult
        {
            /// <summary>True iff a write occurred (or the validator confirmed no benefit and skipped cleanly).</summary>
            public bool Ok { get; set; }

            /// <summary>Bytes saved by the recompression (zero when no benefit).</summary>
            public uint SavedBytes { get; set; }

            /// <summary>Reason a write was skipped, or error message on failure.</summary>
            public string ErrorMessage { get; set; } = string.Empty;
        }

        /// <summary>Search for pointer-write locations referring to <paramref name="addr"/>.</summary>
        /// <param name="romData">Raw ROM bytes.</param>
        /// <param name="addr">Target offset (NOT a pointer; will be converted internally).</param>
        /// <returns>
        /// <see cref="SearchPointerResult"/> with deduped pointer list. LDR hits
        /// take priority — when LDR returns >= 1 entry, raw fallback is NOT run.
        /// When LDR returns 0, raw <c>GrepPointerAll</c> is run and
        /// <c>UsedRawFallback = true</c>. Returns empty list for invalid input.
        /// </returns>
        public static SearchPointerResult SearchPointersForAddress(byte[] romData, uint addr)
        {
            var result = new SearchPointerResult();
            if (romData == null || addr == 0 || addr == U.NOT_FOUND)
            {
                return result;
            }

            // 1. LDR pointer search (Thumb code).
            List<uint> ldrHits = DisassemblerTrumb.GrepLDRData(romData, addr);
            if (ldrHits != null && ldrHits.Count > 0)
            {
                result.Pointers = Dedupe(ldrHits);
                return result;
            }

            // 2. Fallback: raw binary 4-byte aligned pointer search.
            List<uint> rawHits = U.GrepPointerAll(romData, addr);
            if (rawHits == null)
            {
                return result;
            }
            result.Pointers = Dedupe(rawHits);
            result.UsedRawFallback = true;
            return result;
        }

        /// <summary>Local dedupe — Core doesn't expose U.Uniq, so inline a simple set-based dedupe.</summary>
        static List<uint> Dedupe(List<uint> source)
        {
            var seen = new HashSet<uint>();
            var ret = new List<uint>(source.Count);
            foreach (uint v in source)
            {
                if (seen.Add(v)) ret.Add(v);
            }
            return ret;
        }

        /// <summary>
        /// Move arbitrary binary data from <paramref name="srcAddr"/> to
        /// <paramref name="toAddr"/> (or auto-allocate when <paramref name="toAddr"/>=0).
        /// Rewrites pointer references, zero-fills source. Wrap in
        /// <c>ROM.BeginUndoScope(undoData)</c> before calling to record undo.
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="srcAddr">Source offset (must be in ROM bounds, not 0).</param>
        /// <param name="toAddr">Destination offset, or 0 to auto-allocate.</param>
        /// <param name="length">Number of bytes to move (must be > 0 and &lt; 2 MB).</param>
        /// <returns>
        /// <see cref="MoveResult"/>. On failure, <c>Ok = false</c> and
        /// <c>ErrorMessage</c> describes the reason. No writes are issued on
        /// failure (caller-supplied undo scope will be empty).
        /// </returns>
        public static MoveResult MoveCompressedData(ROM rom, uint srcAddr, uint toAddr, uint length)
        {
            var result = new MoveResult();
            if (rom == null)
            {
                result.ErrorMessage = "MoveCompressedData: ROM is null.";
                return result;
            }

            // Normalize: caller may pass pointer or offset; downstream operates on offsets.
            uint src = U.toOffset(srcAddr);
            uint dest = toAddr == 0 ? 0u : U.toOffset(toAddr);

            // -- Input validation (mirrors WinForms safety checks) --
            if (src == 0)
            {
                result.ErrorMessage = "MoveCompressedData: source address is 0 (bad base address).";
                return result;
            }
            if (length == 0)
            {
                result.ErrorMessage = "MoveCompressedData: length is 0 (cannot auto-detect length).";
                return result;
            }
            if (length >= MOVE_LENGTH_LIMIT)
            {
                result.ErrorMessage = $"MoveCompressedData: length 0x{length:X} >= 0x{MOVE_LENGTH_LIMIT:X} (too large, likely corrupt).";
                return result;
            }
            if (src + length > (uint)rom.Data.Length)
            {
                result.ErrorMessage = $"MoveCompressedData: source range 0x{src:X8}+0x{length:X} exceeds ROM end 0x{rom.Data.Length:X}.";
                return result;
            }
            if (dest != 0 && (dest + length > (uint)rom.Data.Length))
            {
                result.ErrorMessage = $"MoveCompressedData: destination range 0x{dest:X8}+0x{length:X} exceeds ROM end 0x{rom.Data.Length:X}.";
                return result;
            }
            // -- Reject overlapping src/dest ranges (overlap-unsafe write order) --
            if (dest != 0 && RangesOverlap(src, length, dest, length))
            {
                result.ErrorMessage = $"MoveCompressedData: source 0x{src:X8}+0x{length:X} and destination 0x{dest:X8}+0x{length:X} overlap (not supported).";
                return result;
            }

            // -- "Already moved" guard: refuse to move all-zero data --
            byte[] srcBytes = rom.getBinaryData(src, length);
            if (U.IsEmptyRange(srcBytes))
            {
                result.ErrorMessage = $"MoveCompressedData: source range at 0x{src:X8} is all zeros (already moved?).";
                return result;
            }

            // -- Resolve / auto-allocate destination --
            if (dest == 0)
            {
                uint allocated = AutoAllocateDestination(rom, length);
                if (allocated == U.NOT_FOUND)
                {
                    result.ErrorMessage = "MoveCompressedData: no free space available (ROM is full).";
                    return result;
                }
                dest = allocated;
                result.AutoAllocated = true;
                // Auto-allocated dest will never overlap source (free space, not source range).
            }

            // -- Search for references to source --
            SearchPointerResult sp = SearchPointersForAddress(rom.Data, src);
            result.UsedRawFallback = sp.UsedRawFallback;

            // -- Apply: rewrite pointers, copy data, zero source --
            uint destPointer = U.toPointer(dest);
            foreach (uint ptrAddr in sp.Pointers)
            {
                if (ptrAddr + 4 > (uint)rom.Data.Length)
                {
                    continue; // Defensive: skip out-of-range pointer addresses
                }
                rom.write_u32(ptrAddr, destPointer);
                result.PointersRewritten.Add(ptrAddr);
            }

            // Write data to destination first, then zero source — order matters
            // when src and dest overlap (we do NOT support overlap; the bounds
            // checks above plus pointer-rewrite-first ordering keep us safe for
            // the common non-overlapping case).
            rom.write_range(dest, srcBytes);
            rom.write_fill(src, length, 0);

            result.NewAddress = dest;
            result.Ok = true;
            return result;
        }

        /// <summary>
        /// Re-compress LZ77 data at <paramref name="addr"/> and write it back
        /// in-place. Caller passes the original allocated length so writes
        /// remain bounded.
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="addr">Offset of LZ77 stream (must be 4-byte aligned).</param>
        /// <param name="allocatedLength">Bytes reserved for this entry in the ROM (4-aligned).</param>
        /// <returns>
        /// <see cref="RecompressResult"/>. <c>Ok = true, SavedBytes = 0</c> means
        /// "validated successfully but no benefit, no write". <c>Ok = false</c>
        /// means validation failed (bad LZ77 data) — no write was issued.
        /// </returns>
        public static RecompressResult RecompressAt(ROM rom, uint addr, uint allocatedLength)
        {
            var result = new RecompressResult();
            if (rom == null)
            {
                result.ErrorMessage = "RecompressAt: ROM is null.";
                return result;
            }
            if (!U.isPadding4(addr))
            {
                result.ErrorMessage = $"RecompressAt: 0x{addr:X8} not 4-byte aligned.";
                return result;
            }
            if (addr + 4 > (uint)rom.Data.Length)
            {
                result.ErrorMessage = $"RecompressAt: 0x{addr:X8} out of bounds.";
                return result;
            }
            if (rom.Data[addr] != 0x10)
            {
                result.ErrorMessage = $"RecompressAt: 0x{addr:X8} byte != 0x10 (not LZ77 magic).";
                return result;
            }
            if (allocatedLength == 0 || addr + allocatedLength > (uint)rom.Data.Length)
            {
                result.ErrorMessage = $"RecompressAt: allocatedLength 0x{allocatedLength:X} out of bounds at 0x{addr:X8}.";
                return result;
            }
            if (!U.isPadding4(allocatedLength))
            {
                result.ErrorMessage = $"RecompressAt: allocatedLength 0x{allocatedLength:X} is not 4-byte aligned.";
                return result;
            }

            uint expectedUncompSize = LZ77.getUncompressSize(rom.Data, addr);
            if (expectedUncompSize == 0)
            {
                result.ErrorMessage = $"RecompressAt: 0x{addr:X8} getUncompressSize=0 (bad header).";
                return result;
            }

            // Decompress and verify header consistency.
            byte[] uncompressed;
            try
            {
                uncompressed = LZ77.decompress(rom.Data, addr);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"RecompressAt: decompress failed at 0x{addr:X8}: {ex.Message}";
                return result;
            }
            if (uncompressed == null || uncompressed.Length != (int)expectedUncompSize)
            {
                result.ErrorMessage = $"RecompressAt: 0x{addr:X8} decompressed length {uncompressed?.Length ?? 0} != header {expectedUncompSize}.";
                return result;
            }

            // Re-compress.
            byte[] recompressed;
            try
            {
                recompressed = LZ77.compress(uncompressed);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"RecompressAt: compress failed at 0x{addr:X8}: {ex.Message}";
                return result;
            }
            if (recompressed == null || recompressed.Length == 0)
            {
                result.ErrorMessage = $"RecompressAt: compress produced empty output at 0x{addr:X8}.";
                return result;
            }

            // Skip if not beneficial.
            if ((uint)recompressed.Length >= allocatedLength)
            {
                result.Ok = true;
                result.SavedBytes = 0;
                result.ErrorMessage = "no benefit (new size >= allocated)";
                return result;
            }
            uint saved = allocatedLength - (uint)recompressed.Length;
            if (saved <= 3)
            {
                result.Ok = true;
                result.SavedBytes = 0;
                result.ErrorMessage = "no benefit (savings <= 3 bytes)";
                return result;
            }

            // Apply: zero the entire allocated range first, then write new compressed data.
            rom.write_fill(addr, allocatedLength, 0);
            rom.write_range(addr, recompressed);

            result.Ok = true;
            result.SavedBytes = saved;
            return result;
        }

        /// <summary>
        /// Heuristic scan for LZ77 data entries in ROM. Walks 4-byte aligned
        /// offsets, validates LZ77 magic + header + decompression consistency,
        /// and returns deduped sorted candidates with <c>Address.DataTypeEnum.LZ77IMG</c>.
        ///
        /// LIMITATION: this scanner does NOT use <c>MakeAllStructPointersList</c>
        /// (Form-coupled in WinForms). It may miss entries that WinForms catches
        /// (e.g., LZ77 referenced only from event scripts or struct pointer
        /// tables). For best coverage, run the Recompress tool in WinForms.
        /// </summary>
        public static List<Address> ScanForLZ77Candidates(ROM rom)
        {
            var ret = new List<Address>();
            if (rom == null || rom.Data == null) return ret;

            byte[] data = rom.Data;
            uint length = (uint)data.Length;
            if (length < 0x100) return ret;

            // Constrain scan to ROM (not RAM); start past header.
            uint scanLimit = length;

            var seen = new HashSet<uint>();
            for (uint addr = 0x100; addr + 4 < scanLimit; addr += 4)
            {
                if (data[addr] != 0x10) continue;

                uint uncompSize = LZ77.getUncompressSize(data, addr);
                if (uncompSize == 0) continue;

                uint compSize = LZ77.getCompressedSize(data, addr);
                if (compSize == 0) continue;

                // Verify decompression produces expected length.
                byte[] decompressed;
                try { decompressed = LZ77.decompress(data, addr); }
                catch { continue; }
                if (decompressed == null || decompressed.Length != (int)uncompSize) continue;

                // Allocated length is the next 4-byte aligned size of compressed stream.
                uint allocatedLen = U.Padding4(compSize);
                if (addr + allocatedLen > length) continue;

                if (!seen.Add(addr)) continue;

                ret.Add(new Address(addr, allocatedLen, U.NOT_FOUND,
                    $"LZ77 candidate at 0x{addr:X8}", Address.DataTypeEnum.LZ77IMG));
            }
            ret.Sort((a, b) => a.Addr.CompareTo(b.Addr));
            return ret;
        }

        /// <summary>16-byte forward slack added before payload to avoid collisions with the prior run.</summary>
        const uint AUTO_ALLOC_LTRIM_SLACK = 16;

        /// <summary>
        /// Auto-allocate a destination for moved data: find free space ahead of
        /// end-of-file, or append to ROM end (resizing if necessary). Mirrors
        /// the simple path of WinForms <c>InputFormRef.AppendBinaryData</c>
        /// without the magic-area / skill-reserve / event-unit-reserve guards
        /// (those are WinForms-form-coupled).
        ///
        /// IMPORTANT: When a free run is found, we return <c>freespace + 16</c>
        /// (mirroring WinForms' LTRIM slack to avoid touching the previous run's
        /// trailing bytes). The slack MUST be included in the requested free-run
        /// size, otherwise the payload would be written past the validated free
        /// area and clobber adjacent ROM data. PR #481 review fix.
        /// </summary>
        static uint AutoAllocateDestination(ROM rom, uint length)
        {
            uint payload = U.Padding4(length);
            // Include slack in the requested size so the discovered run is at
            // least slack + payload bytes — guarantees writing `length` bytes at
            // `freespace + slack` stays inside the validated free area.
            uint needSize = U.Padding4(AUTO_ALLOC_LTRIM_SLACK + payload);

            // Try to find a free run in extended area / existing ROM space.
            uint start = 0x100;
            if (rom.RomInfo != null)
            {
                start = U.toOffset(rom.RomInfo.extends_address);
                if (start == 0 || start == U.NOT_FOUND) start = 0x100;
            }
            uint freespace = rom.FindFreeSpace(start, needSize);
            if (freespace != U.NOT_FOUND)
            {
                return freespace + AUTO_ALLOC_LTRIM_SLACK;
            }

            // No free run found — append to end of file (resize ROM if needed).
            uint endAddr = U.Padding4((uint)rom.Data.Length);
            uint newEnd = endAddr + payload;
            if (newEnd >= 0x02000000)
            {
                return U.NOT_FOUND; // GBA 32 MB cap.
            }
            if (newEnd > (uint)rom.Data.Length)
            {
                if (!rom.write_resize_data(newEnd)) return U.NOT_FOUND;
            }
            return endAddr;
        }

        /// <summary>Returns true if [a, a+aLen) and [b, b+bLen) share any byte.</summary>
        static bool RangesOverlap(uint a, uint aLen, uint b, uint bLen)
        {
            if (aLen == 0 || bLen == 0) return false;
            uint aEnd = a + aLen;
            uint bEnd = b + bLen;
            return a < bEnd && b < aEnd;
        }
    }
}
