using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Descriptor for a ROM table that should be scanned for occurrences of a given
    /// text ID. Each table is described by:
    ///   - either a ROMFEINFO pointer FIELD address (the ROM offset of a pointer),
    ///     which is dereferenced via rom.p32() to obtain the actual data base, OR
    ///   - a DirectBase address (already the table's actual ROM offset, e.g. for
    ///     ROMFEINFO fields like <c>ed_3c_pointer</c> that store a base directly
    ///     rather than a pointer to a pointer — see <c>ROMFE7*.cs</c> comments
    ///     "ポインタ指定できない").
    ///   - an entry size and a maximum entry count.
    ///   - the byte offsets within each entry that hold text IDs (u16 each).
    ///
    /// Exactly one of <see cref="PointerField"/> or <see cref="DirectBase"/> should
    /// be non-zero. If both are zero, the table is skipped (e.g. FE6 doesn't have
    /// FE7's ed_3c so the registry sets neither).
    /// </summary>
    public sealed class TextRefTableDescriptor
    {
        /// <summary>Human-readable kind label, e.g. "Unit", "Class", "Item".</summary>
        public string Kind { get; init; } = "";

        /// <summary>
        /// ROMFEINFO pointer FIELD address (the offset of a pointer, not the data
        /// base). Use this for fields like <c>unit_pointer</c>, <c>class_pointer</c>,
        /// <c>map_setting_pointer</c>, etc. that store the ROM offset of a pointer
        /// that must be dereferenced via <c>rom.p32()</c>.
        /// </summary>
        public uint PointerField { get; init; }

        /// <summary>
        /// Direct base address (already the actual ROM offset of the first entry).
        /// Use this for ROMFEINFO fields like <c>ed_3c_pointer</c> which the WinForms
        /// <c>EDFE7Form.N3_Init</c> assigns via <c>ifr.ReInit(...)</c> (= direct
        /// base) instead of <c>ReInitPointer(...)</c> (= pointer field).
        /// </summary>
        public uint DirectBase { get; init; }

        /// <summary>Size in bytes of each entry in the table.</summary>
        public uint EntrySize { get; init; }

        /// <summary>Maximum number of entries to scan.</summary>
        public uint MaxCount { get; init; }

        /// <summary>Byte offsets within each entry that hold a u16 text ID.</summary>
        public uint[] TextIdOffsets { get; init; } = Array.Empty<uint>();

        /// <summary>
        /// Optional terminator predicate. When supplied, the scanner stops walking
        /// the table at the first entry where this returns true. Mirrors the
        /// per-form WinForms <c>InputFormRef</c> stop callbacks (e.g. "stop at
        /// u16 == 0xFFFF", "stop at u8 == 0x00", "stop at empty run of N
        /// entries"). When null, the scanner walks <see cref="MaxCount"/>
        /// entries (clamped to ROM end). The predicate is invoked with the
        /// per-entry absolute ROM offset.
        /// </summary>
        public Func<ROM, uint, bool>? Terminator { get; init; }

        /// <summary>Resolver that maps an entry index to a display name.</summary>
        public Func<uint, string> NameResolver { get; init; } = _ => "";
    }

    /// <summary>
    /// Generic, ROM-version-agnostic scanner that finds references to a text ID
    /// across a list of ROM tables.
    ///
    /// Each table descriptor identifies a ROMFEINFO pointer FIELD (not the data base
    /// directly). This class dereferences the pointer field via
    /// <see cref="NameResolver.DerefPointer(ROM, uint)"/> and then walks the entries
    /// looking for a u16 text ID match at any of the configured offsets.
    ///
    /// Bounds and safety checks:
    ///   - Uses overflow-safe arithmetic (ulong) when validating the entire table fits in ROM.
    ///   - Uses <see cref="U.isSafetyOffset(uint, ROM)"/> per-entry to reject obviously bogus
    ///     addresses (below 0x200 or past ROM end).
    /// </summary>
    public static class TextReferenceFinder
    {
        /// <summary>
        /// Find references to <paramref name="textId"/> across all given tables.
        /// Returns a list of human-readable strings like "Unit 0x05 (Eirika)".
        /// </summary>
        public static List<string> Find(ROM rom, uint textId, IEnumerable<TextRefTableDescriptor> tables)
        {
            var refs = new List<string>();
            if (rom?.Data == null || textId == 0 || tables == null) return refs;
            foreach (var t in tables)
            {
                if (t == null) continue;
                ScanOne(rom, textId, t, refs);
            }
            return refs;
        }

        static void ScanOne(ROM rom, uint textId, TextRefTableDescriptor t, List<string> refs)
        {
            // Resolve the base address: prefer DirectBase if set (covers
            // FE7 ed_3c_pointer which is a direct address, not a pointer
            // field), otherwise dereference PointerField via p32.
            uint baseAddr;
            if (t.DirectBase != 0)
            {
                baseAddr = U.toOffset(t.DirectBase);
            }
            else
            {
                baseAddr = NameResolver.DerefPointer(rom, t.PointerField);
            }
            if (baseAddr == 0 || t.EntrySize == 0 || t.MaxCount == 0) return;

            // Defensive base validation: the pointer FIELD passed
            // U.isSafetyOffset inside DerefPointer, but its dereferenced
            // value may still be a malformed/unmapped offset (e.g. below
            // 0x200 header floor, or past ROM end). Reject those — without
            // this guard the loop below could match arbitrary later safe
            // offsets as `i` advances and reintroduce false positives.
            if (!U.isSafetyOffset(baseAddr, rom)) return;

            // Clamp MaxCount to the number of entries that physically fit
            // inside the loaded ROM. MaxCount is an upper bound (e.g. 0x100
            // for class/item) and the table might be relocated/expanded with
            // fewer entries than the bound — bailing on the whole table
            // would hide valid early matches. Compute the fitting count
            // in ulong to avoid 32-bit overflow.
            uint fittingCount = t.MaxCount;
            ulong end = (ulong)baseAddr + (ulong)t.MaxCount * (ulong)t.EntrySize;
            if (end > (ulong)rom.Data.Length)
            {
                ulong available = (ulong)rom.Data.Length - (ulong)baseAddr;
                fittingCount = (uint)(available / t.EntrySize);
                if (fittingCount == 0) return;
            }

            // Normalize null TextIdOffsets so the inner foreach is safe for
            // external callers that didn't initialize the array.
            uint[] offsets = t.TextIdOffsets ?? Array.Empty<uint>();
            if (offsets.Length == 0) return;

            var terminator = t.Terminator;
            for (uint i = 0; i < fittingCount; i++)
            {
                uint entry = baseAddr + i * t.EntrySize;
                // Stop if the descriptor's terminator predicate signals the
                // end-of-table sentinel (mirrors WinForms InputFormRef stop
                // callbacks). This guards against false positives when MaxCount
                // is an upper bound — the real table may terminate earlier
                // (e.g. on 0xFFFF or 0x00 sentinel) and the bytes past the
                // sentinel are unrelated data that happen to match.
                if (terminator != null)
                {
                    bool stop;
                    try { stop = terminator(rom, entry); }
                    catch { stop = true; } // defensive: bad predicate aborts table
                    if (stop) break;
                }
                foreach (uint off in offsets)
                {
                    uint addr = entry + off;
                    if (addr + 2 > (uint)rom.Data.Length) break;
                    if (!U.isSafetyOffset(addr + 1, rom)) break;
                    if (rom.u16(addr) == textId)
                    {
                        string name = "";
                        try { name = t.NameResolver != null ? t.NameResolver(i) : ""; }
                        catch { name = ""; }
                        refs.Add($"{t.Kind} 0x{i:X02} ({name})");
                        break; // one match per entry is enough
                    }
                }
            }
        }
    }
}
