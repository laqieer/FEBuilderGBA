// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform, READ-ONLY bulk-export helper for the SkillSystems skill
// config (SLICE 1 of #920). Ported from WinForms
// `SkillConfigSkillSystemForm.ExportAllData` (:703-763):
//   * find the text/anime pointer LOCATIONS (resolved by the caller, e.g. the
//     Avalonia VM's TextPointerLocation / AnimePointerLocation),
//   * deref BOTH locations to their bases (MUST-FIX 1),
//   * walk `count` (capped at 255 like WF, MUST-FIX 2) skill rows,
//   * emit one TSV line per row: `textID<TAB>animePtr` (both hex),
//   * for each EXTENDED-AREA anime pointer, hand the rendered animation off to
//     the caller via a `writeAnime` delegate so this Core file stays free of
//     any GUI image-save dependency. The caller writes `anime{i:hex}/anime.txt`
//     + per-frame PNGs (MUST-FIX 4: HEX dir suffix, matching WF `"anime" +
//     U.ToHexString(i)`).
//
// IMPORT is SLICE 2 (separate PR). This file performs ZERO ROM mutation —
// it only reads + writes the TSV file on disk.

using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// One extended-area skill animation surfaced to the bulk-export caller via
    /// the <c>writeAnime</c> delegate: the rendered animation result plus the
    /// per-skill output directory name (<c>anime{i:hex}</c>, mirroring WF).
    /// </summary>
    public sealed class SkillConfigBulkAnimeEntry
    {
        /// <summary>The skill row index (0-based) this animation belongs to.</summary>
        public uint Index;
        /// <summary>The rendered animation (frames + defender flag + sound id).</summary>
        public SkillAnimeExportResult Result;
        /// <summary>The per-skill output directory NAME (NOT a full path):
        /// <c>"anime" + U.ToHexString(Index)</c>, matching WF.</summary>
        public string AnimeDirName;
    }

    /// <summary>
    /// READ-ONLY cross-platform bulk-export seam for the SkillSystems skill
    /// config. Mirrors WF <c>SkillConfigSkillSystemForm.ExportAllData</c>.
    /// SLICE 1 of #920 (import = slice 2).
    /// </summary>
    public static class SkillConfigSkillSystemBulkExportCore
    {
        // WF predicate cap (SkillConfigSkillSystemForm.Init uses `i < 255`).
        public const int MAX_COUNT = 255;

        /// <summary>
        /// Bulk-export every skill row to a TSV file at <paramref name="tsvPath"/>.
        /// For each EXTENDED-AREA anime pointer, render the animation via the
        /// merged <see cref="SkillSystemsAnimeExportCore.ExportSkillAnimation"/>
        /// seam (#912) and hand it to <paramref name="writeAnime"/> so the
        /// platform layer can save PNGs + the script (keeping this Core file
        /// free of GUI image-save). Returns an empty string on success or a
        /// human-readable error message on failure. NEVER throws for the
        /// expected guard paths; NEVER mutates the ROM.
        /// </summary>
        /// <param name="rom">The active ROM (must be <see cref="CoreState.ROM"/>).</param>
        /// <param name="textPointerLocation">Address of the u32 that holds the
        /// text-base GBA pointer (a pointer LOCATION, not the base itself —
        /// MUST-FIX 1 derefs it).</param>
        /// <param name="animePointerLocation">Address of the u32 that holds the
        /// anime-base GBA pointer (also a pointer LOCATION).</param>
        /// <param name="tsvPath">Output TSV file path.</param>
        /// <param name="writeAnime">Per-extended-anime callback invoked with the
        /// rendered result + the <c>anime{i:hex}</c> dir name. May be null (then
        /// only the TSV is written).</param>
        public static string ExportAll(
            ROM rom,
            uint textPointerLocation,
            uint animePointerLocation,
            string tsvPath,
            Action<SkillConfigBulkAnimeEntry> writeAnime)
        {
            if (rom == null || rom.Data == null) return "ROM is null.";
            // Rom-identity guard: the merged ExportSkillAnimation seam resolves
            // template/SkipCode data against CoreState.ROM, so we refuse foreign
            // instances to keep the per-anime render consistent.
            if (!ReferenceEquals(rom, CoreState.ROM))
                return "ExportAll requires the active CoreState.ROM.";
            if (string.IsNullOrEmpty(tsvPath)) return "Output path is empty.";

            // MUST-FIX 3 (guards): a NOT_FOUND location means the SkillSystems
            // patch isn't installed (the caller's dynamic scan failed). Bail
            // with a clean, no-mutation message.
            if (textPointerLocation == U.NOT_FOUND || animePointerLocation == U.NOT_FOUND)
                return "SkillSystems patch is not installed (text/anime pointer not found).";

            // MUST-FIX 1: deref BOTH pointer locations to their bases.
            uint textBase = rom.p32(textPointerLocation);
            uint animeBase = rom.p32(animePointerLocation);

            if (!U.isSafetyOffset(textBase, rom)) return "Bad text table base address.";
            if (!U.isSafetyOffset(animeBase, rom)) return "Bad anime table base address.";

            // MUST-FIX 2: WF DataCount via getBlockDataCount with the `i < 255`
            // predicate (callback arity is (int i, uint addr) => bool).
            uint count = rom.getBlockDataCount(textBase, 2, (i, addr) => i < MAX_COUNT);

            uint extendsOffset = rom.RomInfo != null
                ? U.toOffset(rom.RomInfo.extends_address)
                : U.NOT_FOUND;

            var lines = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                uint textAddr = textBase + 2 * i;
                uint animeSlot = animeBase + 4 * i;

                // Stop if either slot would read past the safety floor/ceiling
                // (WF breaks the loop on an unsafe anime offset).
                if (!U.isSafetyOffset(textAddr + 1, rom)) break;
                if (!U.isSafetyOffset(animeSlot + 3, rom)) break;

                uint textID = rom.u16(textAddr);
                uint animePtr = rom.p32(animeSlot);

                lines.Add(U.ToHexString(textID) + "\t" + U.ToHexString(animePtr));

                // WF: export iff isExtrendsROMArea(animePtr) && animePtr != 0.
                // isExtrendsROMArea(a) == a >= U.toOffset(extends_address).
                bool isExtended = animePtr != 0
                    && extendsOffset != U.NOT_FOUND
                    && animePtr >= extendsOffset;
                if (!isExtended) continue;

                if (writeAnime == null) continue;

                var res = SkillSystemsAnimeExportCore.ExportSkillAnimation(rom, animePtr);
                if (!string.IsNullOrEmpty(res.Error))
                {
                    // WF tolerates a single bad extended anime by skipping it;
                    // we mirror that (the TSV line is already written so the
                    // pointer survives a re-import).
                    continue;
                }

                // MUST-FIX 4: HEX dir suffix (`"anime" + U.ToHexString(i)`).
                writeAnime(new SkillConfigBulkAnimeEntry
                {
                    Index = i,
                    Result = res,
                    AnimeDirName = "anime" + U.ToHexString(i),
                });
            }

            try
            {
                string fullDir = Path.GetDirectoryName(Path.GetFullPath(tsvPath));
                if (!string.IsNullOrEmpty(fullDir))
                    Directory.CreateDirectory(fullDir);
                File.WriteAllLines(tsvPath, lines);
            }
            catch (Exception ex)
            {
                return "Failed to write TSV: " + ex.Message;
            }

            return "";
        }
    }
}
