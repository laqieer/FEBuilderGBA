using FEBuilderGBA;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Issue #364 — central predicate for the FE7/FE8 Item Editor list count.
    ///
    /// Mirrors the WinForms behaviour of <c>ItemForm.Init()</c> in
    /// <c>FEBuilderGBA/ItemForm.cs</c>:
    ///
    /// <list type="bullet">
    ///   <item>Stop at index &gt; 0xFF (hard cap of 256 entries).</item>
    ///   <item>For FE8U non-multibyte, accept the entry iff
    ///         <c>u32(addr + 12)</c> is a ROM pointer or NULL.</item>
    ///   <item>For every other version (FE6 / FE7J / FE7U / FE8J),
    ///         additionally require <c>u32(addr + 16)</c> to be a ROM pointer or NULL.</item>
    /// </list>
    ///
    /// Counting stops at the first invalid entry. This kept dummy/garbage tail items
    /// out of the legacy list view, but was missing from the Avalonia rewrite.
    /// </summary>
    public static class ItemListPredicate
    {
        /// <summary>
        /// Returns true iff the entry at <paramref name="entryAddr"/> is a valid
        /// item record per the WinForms predicate. Stops also when <paramref name="i"/>
        /// exceeds 0xFF.
        /// </summary>
        /// <param name="rom">Source ROM. Must be non-null.</param>
        /// <param name="i">Zero-based entry index.</param>
        /// <param name="entryAddr">Absolute ROM-relative offset of the entry start.</param>
        /// <param name="fe8uSingleByte">
        /// true for FE8U non-multibyte (loosened predicate); false for every other
        /// version (strict +12 AND +16 predicate, matching WinForms <c>ItemForm</c>).
        /// </param>
        public static bool IsValidEntry(ROM rom, int i, uint entryAddr, bool fe8uSingleByte)
        {
            if (i > 0xFF) return false;

            // Need at least 20 bytes from entryAddr to read u32 @ +12 (and +16 when non-FE8U).
            if (entryAddr + 20 > (uint)rom.Data.Length) return false;

            if (!U.isPointerOrNULL(rom.u32(entryAddr + 12))) return false;
            if (fe8uSingleByte) return true;
            return U.isPointerOrNULL(rom.u32(entryAddr + 16));
        }

        /// <summary>
        /// Walks the item table from <paramref name="baseAddr"/> applying
        /// <see cref="IsValidEntry"/> and returns the number of valid entries.
        /// </summary>
        public static int CountValidEntries(ROM rom, uint baseAddr, uint dataSize, bool fe8uSingleByte)
        {
            if (rom == null) return 0;
            if (dataSize == 0) return 0;
            // Note: we do not call U.isSafetyOffset here because callers may already
            // have resolved a ROM-relative offset that is below the header range; the
            // per-iteration `addr + dataSize > Data.Length` check is sufficient and
            // keeps the helper testable with arbitrary base addresses.
            if (baseAddr >= (uint)rom.Data.Length) return 0;

            int count = 0;
            for (int i = 0; i <= 0xFF; i++)
            {
                uint addr = baseAddr + (uint)i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                if (!IsValidEntry(rom, i, addr, fe8uSingleByte)) break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Returns the FE8U-single-byte flag for the supplied ROM
        /// (FE8 + non-multibyte). All other configurations get the strict +12 AND +16 predicate.
        /// </summary>
        public static bool IsFE8USingleByte(ROM rom)
        {
            if (rom?.RomInfo == null) return false;
            return rom.RomInfo.version == 8 && rom.RomInfo.is_multibyte == false;
        }
    }
}
