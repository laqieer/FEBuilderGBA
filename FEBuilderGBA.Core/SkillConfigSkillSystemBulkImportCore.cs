// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform, BULK-ATOMIC import helper for the SkillSystems skill config
// (SLICE 2 of #923 / #885). Ported from WinForms
// `SkillConfigSkillSystemForm.ImportAllData` (:764-839) with the #923 plan's
// 3 HIGH + M/L corruption fixes baked in:
//
//   H1 — length-aware restore: a per-skill anime import can GROW rom.Data (via
//        RecycleAddress -> write_resize_data). A naive Array.Copy(snap, Data)
//        would leave the trailing grown bytes intact. The fault restore therefore
//        down-resizes rom.Data back to snap.Length BEFORE the in-place copy.
//   H2 — return-value fault detection: SkillSystemsAnimeImportCore.ImportSkill-
//        Animation signals failure by RETURNING a non-empty string (not only by
//        throwing). The bulk treats a non-empty returned error OR a thrown
//        exception OR a NOT_FOUND as a FAULT -> restore.
//   H3 — ONE ambient BeginUndoScope wraps the WHOLE loop with manageSnapshot:false
//        on every per-skill import (so the inner import composes into the bulk
//        scope instead of opening its own). The scope is asserted alive across
//        all skills (Rom.IsAmbientUndoScopeActive). Exactly ONE undo record is
//        pushed on success; ZERO on a fault.
//   M1 — textID written ONLY when textID != 0 (WF :813); the 0 case preserves
//        the existing slot.
//   M2 — applyRecycle toggle (default true): bulk-export -> bulk-import round-trip
//        tests pass applyRecycle:false so the recycled IDs don't break the
//        comparison.
//   M3 — VALIDATE-ALL-BEFORE-MUTATE: every per-skill anime script + all its PNGs
//        (+ FE8U .dmp template) are pre-loaded and validated FIRST. Any failure
//        returns an error with ZERO mutation.
//   L1 — a TSV row with < 2 tab-separated fields is skipped (WF :808).
//   L2 — textID write uses the ambient write_u16 overload (no explicit undodata),
//        so it composes into the bulk scope.

using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// BULK-ATOMIC cross-platform import seam for the SkillSystems skill config.
    /// Mirrors WF <c>SkillConfigSkillSystemForm.ImportAllData</c> but performs the
    /// whole multi-skill import as a SINGLE atomic transaction: either every
    /// skill commits (one undo record) or the ROM is left byte-identical to the
    /// pre-bulk snapshot (zero undo records). SLICE 2 of #923 / #885.
    /// </summary>
    public static class SkillConfigSkillSystemBulkImportCore
    {
        // WF predicate cap (SkillConfigSkillSystemForm.Init uses `i < 255`).
        public const int MAX_COUNT = 255;

        /// <summary>
        /// Import every skill row from <paramref name="tsvPath"/> (a
        /// <c>*.SkillConfig.tsv</c> of <c>textID&lt;TAB&gt;animePtr</c> hex rows,
        /// as written by <see cref="SkillConfigSkillSystemBulkExportCore"/>). For
        /// each skill with an <c>anime{i:hex}/anime.txt</c> script the per-skill
        /// animation is re-imported via the merged
        /// <see cref="SkillSystemsAnimeImportCore.ImportSkillAnimation"/> seam
        /// (<c>manageSnapshot:false</c>, composing into THIS bulk's undo scope).
        /// </summary>
        /// <param name="rom">The active ROM (must be <see cref="CoreState.ROM"/>).</param>
        /// <param name="textPointerLocation">Address of the u32 that holds the
        /// text-base GBA pointer (a pointer LOCATION, not the base).</param>
        /// <param name="animePointerLocation">Address of the u32 that holds the
        /// anime-base GBA pointer (also a pointer LOCATION).</param>
        /// <param name="tsvPath">Input TSV file path.</param>
        /// <param name="animeScriptDirResolver">Given the skill index <c>i</c>,
        /// returns the directory that holds that skill's <c>anime.txt</c> + PNGs
        /// (the platform layer builds <c>Path.Combine(basedir, "anime"+i:hex)</c>).
        /// May resolve a non-existent dir — the bulk just skips a skill whose
        /// <c>anime.txt</c> is absent.</param>
        /// <param name="imageProvider">Per-filename PNG loader: resolves a PNG name
        /// (relative to the skill's anime dir, supplied via the resolver) to
        /// indexed pixels + dims + GBA palette. Reused from the single-import
        /// helper.</param>
        /// <param name="applyRecycle">When <c>true</c> (default) a non-zero textID
        /// is mapped through <see cref="SkillConfigSkillTextIDRecycle.Convert"/>
        /// (WF recycle). Round-trip tests pass <c>false</c>.</param>
        /// <param name="faultInjector">Test-only hook threaded into EACH per-skill
        /// <see cref="SkillSystemsAnimeImportCore.ImportSkillAnimation"/> call so a
        /// forced mid-bulk fault (after a resize-growing skill) can prove the
        /// length-aware restore. Production callers pass <c>null</c>.</param>
        /// <returns>Empty string on success; a non-empty, human-readable error on
        /// any rejection or fault (with the ROM left byte-identical).</returns>
        public static string ImportAll(
            ROM rom,
            uint textPointerLocation,
            uint animePointerLocation,
            string tsvPath,
            Func<uint, string> animeScriptDirResolver,
            SkillSystemsAnimeImportCore.ImageProvider imageProvider,
            bool applyRecycle = true,
            Action faultInjector = null)
        {
            if (rom == null || rom.Data == null) return "ROM is null.";
            // Rom-identity guard: the merged ImportSkillAnimation seam resolves the
            // FE8U template + RecycleAddress writes against CoreState.ROM, so we
            // refuse foreign instances.
            if (!ReferenceEquals(rom, CoreState.ROM))
                return "ImportAll requires the active CoreState.ROM.";
            if (string.IsNullOrEmpty(tsvPath)) return "Input path is empty.";
            if (imageProvider == null) return "Image provider is null.";
            if (animeScriptDirResolver == null) return "Anime directory resolver is null.";

            // A NOT_FOUND location means the SkillSystems patch isn't installed.
            if (textPointerLocation == U.NOT_FOUND || animePointerLocation == U.NOT_FOUND)
                return "SkillSystems patch is not installed (text/anime pointer not found).";

            // #922 lesson: validate the pointer LOCATIONS themselves are safe
            // BEFORE dereferencing, so an invalid (or zero) location can't make us
            // p32-read garbage. The `+3` covers the full 4-byte read.
            if (!U.isSafetyOffset(textPointerLocation + 3, rom)
                || !U.isSafetyOffset(animePointerLocation + 3, rom))
                return "Bad text/anime pointer location.";

            uint textBase = rom.p32(textPointerLocation);
            uint animeBase = rom.p32(animePointerLocation);
            if (!U.isSafetyOffset(textBase, rom)) return "Bad text table base address.";
            if (!U.isSafetyOffset(animeBase, rom)) return "Bad anime table base address.";

            string[] lines;
            try
            {
                lines = File.ReadAllLines(tsvPath);
            }
            catch (Exception ex)
            {
                return "Failed to read TSV: " + ex.Message;
            }

            uint count = rom.getBlockDataCount(textBase, 2, (i, addr) => i < MAX_COUNT);

            // --------------------------------------------------------------
            // Parse the TSV into per-skill rows up-front, bounded by `count`,
            // the line count, and the per-slot safety floor/ceiling.
            // --------------------------------------------------------------
            var rows = new List<Row>();
            for (uint i = 0; i < count; i++)
            {
                if (i >= lines.Length) break;

                uint textAddr = textBase + 2 * i;
                uint animeSlot = animeBase + 4 * i;
                if (!U.isSafetyOffset(textAddr + 1, rom)) break;
                if (!U.isSafetyOffset(animeSlot + 3, rom)) break;

                string[] sp = lines[i].Split('\t');
                if (sp.Length < 2) continue; // L1: malformed row -> skip (WF :808).

                rows.Add(new Row
                {
                    Index = i,
                    TextAddr = textAddr,
                    AnimeSlot = animeSlot,
                    TextID = U.atoh(sp[0]),
                    AnimePtr = U.atoh(sp[1]),
                });
            }

            // ==============================================================
            // M3: VALIDATE-ALL-BEFORE-MUTATE. Pre-load + validate every skill's
            // anime script + ALL its PNGs (and, on FE8U, that the .dmp template
            // exists). ANY failure -> return error, ZERO mutation. We cache the
            // resolved script lines per skill so the mutate phase re-reads
            // nothing (a missing file between validate and mutate can't sneak a
            // partial write past us).
            // ==============================================================
            var scriptCache = new Dictionary<uint, string[]>();
            foreach (var row in rows)
            {
                if (row.AnimePtr == 0) continue; // 0 -> write_p32(slot,0); no script.

                string dir = animeScriptDirResolver(row.Index);
                string animeTxt = Path.Combine(dir ?? ".", "anime.txt");
                if (!File.Exists(animeTxt))
                    continue; // No script for this slot -> keep the existing anime.

                string[] scriptLines;
                try
                {
                    scriptLines = File.ReadAllLines(animeTxt);
                }
                catch (Exception ex)
                {
                    return "Skill " + U.ToHexString(row.Index) + ": cannot read anime.txt: " + ex.Message;
                }

                // Validate the script parse.
                var parsed = SkillSystemsAnimeImportCore.ParseScript(scriptLines);
                if (!string.IsNullOrEmpty(parsed.Error))
                    return "Skill " + U.ToHexString(row.Index) + ": " + parsed.Error;

                // FE8U: pre-validate the .dmp template exists (the single-import
                // seam reads it in its own validate phase, but pre-checking here
                // keeps the whole-bulk no-mutation contract honest).
                if (rom.RomInfo != null && !rom.RomInfo.is_multibyte)
                {
                    string templatePath = FE8USkillTemplate.PathFor(parsed.IsDefender);
                    if (!File.Exists(templatePath))
                        return "Skill " + U.ToHexString(row.Index)
                            + ": FE8U skill-anime program template not found: "
                            + FE8USkillTemplate.FileFor(parsed.IsDefender);
                }

                // Validate EVERY referenced PNG (load, dims, <=16 colours).
                foreach (var f in parsed.Frames)
                {
                    var loaded = imageProvider(f.PngName);
                    if (loaded == null)
                        return "Skill " + U.ToHexString(row.Index) + ": cannot load frame image: " + f.PngName;
                    var (idx, w, h, _) = loaded.Value;
                    if (idx == null)
                        return "Skill " + U.ToHexString(row.Index) + ": null pixels for: " + f.PngName;
                    if (w <= 0 || h <= 0)
                        return "Skill " + U.ToHexString(row.Index) + ": invalid dimensions for: " + f.PngName;
                    if ((long)idx.Length != (long)w * h)
                        return "Skill " + U.ToHexString(row.Index) + ": pixel count mismatch for: " + f.PngName;
                    for (int p = 0; p < idx.Length; p++)
                    {
                        if (idx[p] > 15)
                            return "Skill " + U.ToHexString(row.Index) + ": frame " + f.PngName
                                + " uses more than 16 colours (index " + idx[p] + ").";
                    }
                }

                scriptCache[row.Index] = scriptLines;
            }

            // ==============================================================
            // ONE snapshot + ONE ambient BeginUndoScope wrapping the whole loop.
            // ==============================================================
            byte[] snap = (byte[])rom.Data.Clone();
            var bulkUndoData = CoreState.Undo != null
                ? CoreState.Undo.NewUndoData("SkillConfigSkillSystemBulkImport")
                : new Undo.UndoData();

            string fault = null;
            try
            {
                using (ROM.BeginUndoScope(bulkUndoData))
                {
                    foreach (var row in rows)
                    {
                        // H3: the bulk ambient scope MUST stay alive across every
                        // skill (manageSnapshot:false relies on it). If it ever
                        // went null a per-skill import would silently open + close
                        // its own scope and leave this bulk record incomplete.
                        System.Diagnostics.Debug.Assert(ROM.IsAmbientUndoScopeActive,
                            "Bulk undo scope must stay active across every skill.");
                        if (!ROM.IsAmbientUndoScopeActive)
                        {
                            fault = "Internal error: bulk undo scope closed mid-loop.";
                            break;
                        }

                        // M1: write textID ONLY when non-zero (WF :813). The 0 case
                        // preserves the existing slot. L2: ambient write_u16 (no
                        // explicit undodata) composes into the bulk scope.
                        if (row.TextID != 0)
                        {
                            uint tid = applyRecycle
                                ? SkillConfigSkillTextIDRecycle.Convert(row.Index, row.TextID)
                                : row.TextID;
                            rom.write_u16(row.TextAddr, tid);
                        }

                        if (scriptCache.TryGetValue(row.Index, out var scriptLines))
                        {
                            // H2: ImportSkillAnimation returns a non-empty error on
                            // a per-skill fault (it does NOT only throw). Treat a
                            // returned error string as a FAULT and stop.
                            string err;
                            try
                            {
                                err = SkillSystemsAnimeImportCore.ImportSkillAnimation(
                                    rom, scriptLines, row.AnimeSlot, imageProvider,
                                    faultInjector: faultInjector,
                                    manageSnapshot: false);
                            }
                            catch (Exception ex)
                            {
                                err = "Skill " + U.ToHexString(row.Index) + " import threw: " + ex.Message;
                            }

                            if (!string.IsNullOrEmpty(err))
                            {
                                fault = err;
                                break;
                            }
                        }
                        else if (row.AnimePtr == 0)
                        {
                            // 0 -> explicitly clear the anime slot (WF :824-828).
                            rom.write_p32(row.AnimeSlot, 0);
                        }
                        // else: AnimePtr != 0 but no anime.txt -> keep current
                        // anime (WF "default" fall-through :834).
                    }
                }
            }
            catch (Exception ex)
            {
                fault = "Bulk import failed: " + ex.Message;
            }

            // ==============================================================
            // H1: length-aware restore on ANY fault. A per-skill import can have
            // grown rom.Data; a naive Array.Copy would leave the trailing grown
            // bytes. Down-resize FIRST, then in-place copy -> byte-identical to
            // the pre-bulk snapshot. ZERO undo records pushed.
            // ==============================================================
            if (!string.IsNullOrEmpty(fault))
            {
                if (rom.Data.Length != snap.Length)
                    rom.write_resize_data((uint)snap.Length);
                Array.Copy(snap, rom.Data, snap.Length);
                return fault;
            }

            // Success: exactly ONE undo record for the whole bulk.
            if (CoreState.Undo != null)
                CoreState.Undo.Push(bulkUndoData);
            return "";
        }

        sealed class Row
        {
            public uint Index;
            public uint TextAddr;
            public uint AnimeSlot;
            public uint TextID;
            public uint AnimePtr;
        }
    }
}
