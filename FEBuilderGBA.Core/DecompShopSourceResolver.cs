// SPDX-License-Identifier: GPL-3.0-or-later
// Decomp shop-list source-owner resolver (#1347).
//
// Bridges a ROM shop-list address to its owning decomp source list declaration by
// resolving the address to a symbol NAME through the project's merged ASM/MAP symbol
// table, then looking that name up as a manifest u16-list owner. READ-ONLY and NEVER
// throws: every public entry is fully guarded and returns false on any fault.
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Resolver glue (#1347) that maps a ROM shop-list address to its owning decomp
    /// <c>u16-list</c> source declaration. The in-place shop-list writer
    /// (<see cref="DecompSourceWriterCore.WriteListEntries"/>) needs to know WHICH
    /// manifest list-owner owns the shop the user is editing; shop lists are reached via
    /// scattered pointers (hensei / worldmap / event-cond) and have no rectangular
    /// C-array table the fixed-row writer can index, so the only honest source binding is
    /// "the data symbol at this address, declared as a manifest list-owner."
    ///
    /// REQUIRES decomp open mode with a built .map/.elf/.sym carrying the list's DATA
    /// symbol AND a manifest <c>tables[]</c> list-owner declaration (format
    /// <c>u16-list</c>) for that symbol. With NEITHER, the caller degrades to
    /// <c>--export-asset --kind=shop</c> (the migration artifact). There is NO automatic
    /// source-file discovery from a symbol's object path — the owner's <c>sourceFile</c>
    /// is the single source of truth. The match is STRICTLY exact-or-span-covering so a
    /// neighbouring symbol can NEVER be mistaken for the shop's owning list.
    /// </summary>
    public static class DecompShopSourceResolver
    {
        /// <summary>
        /// Resolve the manifest list-owner of the shop at <paramref name="shopAddr"/>
        /// (a ROM OFFSET, as produced by <c>ItemShopCore.MakeShopList</c>). Resolves the
        /// address to a symbol NAME via <paramref name="asmMap"/> (exact match first, then
        /// a SPAN-COVERING nearest-symbol fallback) and looks that name up as a u16-list
        /// owner in the project manifest. NEVER throws.
        /// </summary>
        /// <param name="project">The active decomp project (its manifest declares the owner).</param>
        /// <param name="asmMap">
        /// The merged ASM/MAP symbol file (decomp mode layers the project's .map/.elf/.sym
        /// symbols over the shipped map). The CLI passes
        /// <c>CoreState.AsmMapFileAsmCache?.GetAsmMapFile()</c>; tests pass a
        /// <see cref="MergedAsmMapFile"/> built from a synthetic resolver.
        /// </param>
        /// <param name="shopAddr">The shop item-list ROM OFFSET.</param>
        /// <param name="owner">On success, the matched u16-list owner; else null.</param>
        /// <param name="symbolName">On success, the resolved symbol name; else "".</param>
        /// <returns>True only when a u16-list owner was resolved.</returns>
        public static bool TryResolveShopOwner(
            DecompProject project,
            IAsmMapFile asmMap,
            uint shopAddr,
            out DecompTableEntry owner,
            out string symbolName)
        {
            owner = null;
            symbolName = "";
            try
            {
                if (project == null || asmMap == null)
                    return false;

                // Shop addresses are ROM offsets; the symbol table is GBA-pointer-keyed.
                uint ptr = U.toPointer(shopAddr);

                // EXACT lookup first.
                string name = asmMap.GetName(ptr);
                if (string.IsNullOrEmpty(name))
                {
                    // Fall back to a SPAN-COVERING nearest symbol. Only accept it when the
                    // symbol at `near` actually covers `ptr` (key <= ptr < key + length);
                    // a zero-length / non-covering nearest symbol is REJECTED so a
                    // neighbouring list is never mistaken for the shop's owner.
                    uint near = asmMap.SearchNear(ptr);
                    if (near != U.NOT_FOUND
                        && asmMap.TryGetValue(near, out AsmMapSt st)
                        && st != null
                        && st.Length > 0
                        && ptr >= near
                        && (ulong)ptr < (ulong)near + st.Length)
                    {
                        name = st.Name;
                    }
                }

                if (string.IsNullOrEmpty(name))
                    return false;

                DecompTableEntry candidate = project.TryGetListOwner(name);
                if (candidate == null)
                    return false;

                owner = candidate;
                symbolName = name;
                return true;
            }
            catch
            {
                owner = null;
                symbolName = "";
                return false;
            }
        }
    }
}
