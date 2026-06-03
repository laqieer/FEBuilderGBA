// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform, ROM-MUTATING Import helper for SkillSystems skill animations
// (.txt script + per-frame PNGs → ROM write). SLICE 1 (#916, FE8J) + SLICE 2
// (#917, FE8U program-code re-emit) of issue #913 — ported from WF
// `FEBuilderGBA/ImageUtilSkillSystemsAnimeCreator.cs` Import (:460-635).
//
// SCOPE — FE8J AND FE8U:
//   * FE8J (is_multibyte): no per-skill program prefix; mainData begins at the
//     5-word config block (frames/tsalist/imagelist/pallist/sound_id).
//   * FE8U (NOT is_multibyte): mainData is PREFIXED with the per-skill program
//     template (`config/patch2/FE8U/skill/skillanimtemplate*.dmp`, defender vs
//     attack selected by the leading `D` line; WF :589-598) BEFORE the 5-word
//     config block. The template is read ONCE in the validate-before-mutate
//     phase (GUARD A) and carried verbatim into the write (GUARD C). A
//     missing/unreadable .dmp returns a clean no-mutation error. The repointed
//     slot points to the template START, so the export `SkipCode` skips exactly
//     this prefix on re-read. The dir + filenames are the SINGLE shared
//     `FE8USkillTemplate` constants (GUARD E) used by BOTH this prepend and the
//     export skip — they can never drift.
//
// THREE CRITICAL WF-parity invariants (each a compile-clean corruption trap):
//   CRITICAL 1 — palette is RAW 0x20 bytes, NOT LZ77-compressed. WF :537-538
//     compress ONLY image+tsa; :539 `a.pal = ImageToPalette(bmp, 1)` is raw.
//   CRITICAL 2 — frame dedup is by PNG FILENAME string (WF FindImage :625-635
//     `anime_list[i].filename == image`), NOT by encoded bytes.
//   CRITICAL 3 — the frames table is terminated by TWO u16 0xFFFF (a 4-byte
//     0xFFFF,0xFFFF terminator). WF :549-550.
//
// Other pinned WF details:
//   * sound_id default = 0x3d1 when no `S` line (WF :470); `S{hex}` parsed via
//     U.atoh (WF :495); written as a RAW u32, NOT toPointer (WF :616). The four
//     config words (frames/tsalist/imagelist/pallist) ARE U.toPointer (:605-614).
//   * each frame image is forced to 240×160 before encode (WF :526-528).
//   * mainData word order: frames, tsalist, imagelist, pallist, sound_id.
//   * per-frame parallel pointer lists order: image, tsa, pal (WF :571-578).
//   * WriteAndWritePointer(anime_pointer, mainData) is the LAST op (WF :620),
//     and anime_pointer = U.toOffset(anime_pointer) first (WF :552).
//
// OLD-REGION RECYCLE (#914): WF calls RecycleOldAnime (:560) to reuse the
//   overwritten anime sub-regions (image/tsa/pal/frames/lists + config) before
//   allocating the new ones. This is now ported as the READ-ONLY Core
//   enumeration EnumerateOldAnimeRegions (a literal port of WF
//   RecycleOldAnime :637-784), threaded into WriteCore's RecycleAddress pool via
//   the recycleOldRegion parameter (default true). The SINGLE-IMPORT parity gap
//   is therefore CLOSED — re-importing the same skill reuses the freed region
//   instead of leaking it. BULK import (SkillConfigSkillSystemBulkImportCore)
//   still passes recycleOldRegion:false: it mutates many slots in one
//   transaction where cross-slot shared sub-regions are most likely, and WF
//   skill-anime has NO SubConfilctArea de-dup pass to catch them — so bulk
//   recycle stays deferred pending a shared-region safety test (#914 follow-up).
//   EnumerateOldAnimeRegions is STRICTLY READ-ONLY (no rom.write_*, no
//   RecycleAddress writes): it runs in the validate phase BEFORE the defensive
//   snapshot clone, so the in-place restore (#885) must never depend on it.
//
// WRITE discipline (#885): validate-EVERYTHING-before-mutate; ONE ambient
//   ROM.BeginUndoScope wraps all writes; a defensive byte[] snapshot is restored
//   in-place on ANY failure so cached offsets stay valid and the slot never
//   half-flips (the forced-failure corruption guard test depends on this).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace FEBuilderGBA
{
    /// <summary>One parsed script frame: a wait value and the referenced PNG filename.</summary>
    public sealed class SkillAnimeScriptFrame
    {
        public uint Wait;
        public string PngName;
    }

    /// <summary>Result of <see cref="SkillSystemsAnimeImportCore.ParseScript"/>.</summary>
    public sealed class SkillAnimeScript
    {
        public List<SkillAnimeScriptFrame> Frames = new List<SkillAnimeScriptFrame>();
        public bool IsDefender;
        public uint SoundId = SkillSystemsAnimeImportCore.DEFAULT_SOUND_ID;
        public string Error = "";
    }

    /// <summary>
    /// Cross-platform, ROM-mutating import seam for SkillSystems skill
    /// animations. Mirrors WF <c>ImageUtilSkillSystemsAnimeCreator.Import</c>
    /// (FE8J path). All file I/O is the caller's job: it supplies the parsed
    /// script lines and a per-filename image provider, so headless tests drive
    /// the whole pipeline with no filesystem access.
    /// </summary>
    public static class SkillSystemsAnimeImportCore
    {
        public const int SCREEN_WIDTH  = 240;
        public const int SCREEN_HEIGHT = 160;

        /// <summary>WF default sound id when no <c>S</c> line is present (WF :470).</summary>
        public const uint DEFAULT_SOUND_ID = 0x3d1;

        /// <summary>
        /// Image provider: receives a PNG filename as written in the script
        /// (relative — caller resolves against the script directory) and returns
        /// indexed pixels + image dimensions on success, or <c>null</c> on any
        /// load failure. The palette argument is the quantized GBA palette
        /// (0x20 bytes for 16 colours) — written RAW per CRITICAL 1.
        /// </summary>
        public delegate (byte[] indexedPixels, int width, int height, byte[] gbaPalette)? ImageProvider(string pngName);

        // ================================================================
        // ParseScript
        // ================================================================

        /// <summary>
        /// Parse a SkillSystems anime script (mirrors WF Import :472-547 line
        /// handling): <c>D</c> → defender flag; <c>S{hex}</c> → sound id via
        /// <c>U.atoh</c>; <c>{wait} {png}</c> → a frame. Comments / other-lang
        /// lines are skipped. Returns a populated <see cref="SkillAnimeScript"/>;
        /// a non-empty <c>Error</c> means the script is structurally invalid.
        /// </summary>
        public static SkillAnimeScript ParseScript(string[] lines)
        {
            var script = new SkillAnimeScript();
            if (lines == null)
            {
                script.Error = "Script lines are null.";
                return script;
            }

            for (int lineCount = 0; lineCount < lines.Length; lineCount++)
            {
                string line = lines[lineCount];
                if (line == null) continue;
                if (U.IsComment(line) || U.OtherLangLine(line)) continue;
                line = U.ClipComment(line);
                if (line.Length <= 0) continue;

                line = line.Replace("\t", " ");
                string[] sp = line.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length <= 0) continue;
                string command = sp[0];
                if (command.Length <= 0) continue;

                if (command[0] == 'S')
                {
                    script.SoundId = U.atoh(command.Substring(1));
                    continue;
                }
                if (command[0] == 'D')
                {
                    script.IsDefender = true;
                    continue;
                }
                if (!(U.isNumString(command) && sp.Length >= 2))
                {
                    // Not a frame line (and not S/D). WF silently skips these.
                    continue;
                }

                uint wait = U.atoi(command);
                string png = sp[1];
                script.Frames.Add(new SkillAnimeScriptFrame { Wait = wait, PngName = png });
            }

            if (script.Frames.Count == 0)
            {
                script.Error = "Script contains no animation frames ({wait} {png.png} lines).";
            }
            return script;
        }

        // ================================================================
        // ImportSkillAnimation
        // ================================================================

        /// <summary>
        /// ROM-mutating import: encode every frame PNG, build the frames table +
        /// three parallel pointer lists + the 5-word config block, and repoint
        /// the slot at <paramref name="animePointerSlot"/> — all under one
        /// ambient undo scope.
        ///
        /// <para><b>FE8J and FE8U.</b> FE8U prepends the per-skill program
        /// template (defender/attack by the leading <c>D</c> line) read ONCE in
        /// the validate phase; a missing template returns a clean no-mutation
        /// error. FE8J prepends nothing.</para>
        ///
        /// <para><b>Validate-before-mutate:</b> the script parse, the FE8U
        /// template read, every PNG load + encode, and the slot-safety check all
        /// run BEFORE any ROM write. Any failure returns a non-empty error and
        /// leaves the ROM byte-identical.</para>
        ///
        /// <para><b>Undo:</b> the caller MUST open an ambient undo scope (the
        /// Avalonia <c>UndoService.Begin</c>/<c>Commit</c>/<c>Rollback</c>) before
        /// calling. A defensive snapshot is restored in-place on any failure.</para>
        /// </summary>
        /// <param name="rom">The ROM to write into (must be the active CoreState.ROM).</param>
        /// <param name="scriptLines">Raw script lines (caller read them from the .txt).</param>
        /// <param name="animePointerSlot">
        ///   ROM address of the 4-byte anime pointer slot (animeBase + 4*id). The
        ///   slot is repointed to the freshly-allocated mainData block.
        /// </param>
        /// <param name="imageProvider">Per-filename PNG loader (see <see cref="ImageProvider"/>).</param>
        /// <param name="faultInjector">
        ///   Test-only hook: invoked once AFTER the first per-frame region has
        ///   been written but BEFORE the slot is repointed. Throwing from it
        ///   exercises the partial-write corruption guard. Production callers
        ///   pass <c>null</c>.
        /// </param>
        /// <param name="manageSnapshot">
        ///   <c>true</c> (default, single-import #916/#919): manage undo + a
        ///   defensive snapshot INTERNALLY — open a fresh <c>BeginUndoScope</c>,
        ///   clone <c>rom.Data</c>, restore byte-for-byte on any fault, and
        ///   <c>CoreState.Undo.Push</c> on success. This is the EXACT, unchanged
        ///   behaviour the four single-import views depend on.
        ///   <para><c>false</c> (BULK mode, #923/#885): the CALLER owns the undo
        ///   scope + snapshot. This method then runs its writes into the caller's
        ///   AMBIENT undo scope (it does NOT open its own), does NOT clone a
        ///   snapshot, does NOT restore on fault, and does NOT push. On any
        ///   per-skill failure it RETURNS the non-empty error string (validate-
        ///   before-mutate still runs first) — the bulk caller's single outer
        ///   restore handles the rollback. A thrown exception still propagates to
        ///   the bulk caller's try/catch.</para>
        /// </param>
        /// <param name="recycleOldRegion">
        ///   <c>true</c> (default, single-import #914): before the new writes,
        ///   build a recycle pool from the slot's CURRENT anime target via the
        ///   READ-ONLY <see cref="EnumerateOldAnimeRegions"/> so the new
        ///   allocations REUSE the old anime's freed sub-regions instead of
        ///   leaking them. A zero/garbage slot enumerates EMPTY (no-op), so this
        ///   is a true superset of the fresh-allocate path.
        ///   <para><c>false</c> (BULK #923): pass an EMPTY pool — always
        ///   fresh-allocate. Bulk mutates many slots in one transaction where
        ///   cross-slot shared sub-regions are most likely and WF skill-anime has
        ///   NO SubConfilctArea de-dup pass to catch them, so bulk recycle is
        ///   deferred pending a shared-region safety test (#914 follow-up).</para>
        /// </param>
        /// <returns>Empty string on success; a non-empty error on any rejection.</returns>
        public static string ImportSkillAnimation(
            ROM rom,
            string[] scriptLines,
            uint animePointerSlot,
            ImageProvider imageProvider,
            Action faultInjector = null,
            bool manageSnapshot = true,
            bool recycleOldRegion = true)
        {
            // ---- Pre-flight guards (no mutation) ----
            if (rom == null || rom.Data == null) return "ROM is not loaded.";
            if (rom.RomInfo == null) return "ROM info is not available.";
            if (imageProvider == null) return "Image provider is null.";
            if (!ReferenceEquals(rom, CoreState.ROM))
                return "Internal error: ROM is not the active CoreState.ROM.";

            // ---- VALIDATE script ----
            // GUARD B (#917): ParseScript runs FIRST so script.IsDefender is known
            // before the FE8U template is selected.
            SkillAnimeScript script = ParseScript(scriptLines);
            if (!string.IsNullOrEmpty(script.Error)) return script.Error;

            // ---- GUARD A (#917): read the FE8U program template ONCE, in the
            //   validate-before-mutate phase, BEFORE any ROM byte is touched. ----
            // FE8U (NOT is_multibyte) prepends a per-skill program code block (WF
            // :586-598). FE8J prepends nothing → empty prefix. We select the
            // template by script.IsDefender (GUARD B) and File.ReadAllBytes it
            // exactly once here; a missing/unreadable .dmp returns a clean
            // no-mutation error AT THIS POINT, so a template failure mutates ZERO
            // bytes. The bytes are carried VERBATIM into the mutate phase (GUARD C)
            // — never re-read — so the validated bytes == the written bytes and the
            // forced-failure byte-identity guarantee holds.
            byte[] programTemplate = Array.Empty<byte>();
            if (!rom.RomInfo.is_multibyte)
            {
                string templatePath = FE8USkillTemplate.PathFor(script.IsDefender);
                try
                {
                    programTemplate = System.IO.File.ReadAllBytes(templatePath);
                }
                catch (Exception ex)
                {
                    return "Cannot read FE8U skill-anime program template '"
                        + FE8USkillTemplate.FileFor(script.IsDefender) + "': " + ex.Message;
                }
                if (programTemplate == null || programTemplate.Length == 0)
                {
                    return "FE8U skill-anime program template is empty: "
                        + FE8USkillTemplate.FileFor(script.IsDefender);
                }
            }

            // ---- VALIDATE slot ----
            uint slot = U.toOffset(animePointerSlot);
            if (!IsRegionSafe(rom, slot, 4))
                return "Animation pointer slot is out of range.";

            // ---- OLD-REGION RECYCLE (#914): build the recycle pool from the
            //   slot's CURRENT anime target, READ-ONLY, in the validate phase
            //   BEFORE the snapshot clone / any mutation. A zero/garbage slot
            //   enumerates EMPTY (no-op), so recycleOldRegion:true is a strict
            //   superset of the fresh-allocate path. recycleOldRegion:false (bulk)
            //   keeps the empty pool — always fresh-allocate. ----
            List<Address> recyclePool = recycleOldRegion
                ? EnumerateOldAnimeRegions(rom, rom.p32(slot))
                : new List<Address>();

            // ---- VALIDATE + ENCODE every UNIQUE frame (dedup by FILENAME — CRITICAL 2) ----
            // uniqueNames preserves first-seen order = the id assignment order
            // (WF id = anime_list.Count at first sighting, FindImage :515-518).
            var idByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var uniqueNames = new List<string>();
            var encImage = new List<byte[]>(); // LZ77 tiles per id
            var encTsa   = new List<byte[]>(); // LZ77 tsa per id
            var encPal   = new List<byte[]>(); // RAW 0x20 palette per id (CRITICAL 1)

            // (id, wait) per script frame, in script order.
            var frameIds = new List<int>();
            var frameWaits = new List<uint>();

            foreach (var f in script.Frames)
            {
                int id;
                if (!idByName.TryGetValue(f.PngName, out id))
                {
                    id = uniqueNames.Count;

                    var loaded = imageProvider(f.PngName);
                    if (loaded == null)
                        return "Cannot load frame image: " + f.PngName;

                    var (idx, w, h, pal) = loaded.Value;
                    if (idx == null)
                        return "Frame image provider returned null pixels for: " + f.PngName;
                    if (w <= 0 || h <= 0)
                        return "Frame image has invalid dimensions for: " + f.PngName;
                    if ((long)idx.Length != (long)w * h)
                        return "Frame image pixel count mismatch for: " + f.PngName;

                    // 4bpp: every index must fit a nibble (WF ImageToBytePackedTSA
                    // assumes a <=16-colour image).
                    for (int p = 0; p < idx.Length; p++)
                    {
                        if (idx[p] > 15)
                            return "Frame image " + f.PngName + " uses more than 16 colours (index " + idx[p] + ").";
                    }

                    // Force 240×160 (crop/pad, top-left anchored) — WF :526-528.
                    byte[] normalized = NormalizeTo(idx, w, h, SCREEN_WIDTH, SCREEN_HEIGHT);

                    var tsaResult = ImageImportCore.EncodeTSA(normalized, SCREEN_WIDTH, SCREEN_HEIGHT, paletteIndex: 0);
                    if (tsaResult == null || tsaResult.TileData == null || tsaResult.TSAData == null)
                        return "Failed to encode frame image: " + f.PngName;

                    byte[] lz77Image = LZ77.compress(tsaResult.TileData);
                    byte[] lz77Tsa   = LZ77.compress(tsaResult.TSAData);
                    if (lz77Image == null || lz77Image.Length == 0 ||
                        lz77Tsa == null || lz77Tsa.Length == 0)
                        return "Failed to compress frame image: " + f.PngName;

                    // CRITICAL 1: palette is RAW 0x20 bytes, never compressed.
                    byte[] rawPal = EnsureExactLength(pal, 0x20);

                    idByName[f.PngName] = id;
                    uniqueNames.Add(f.PngName);
                    encImage.Add(lz77Image);
                    encTsa.Add(lz77Tsa);
                    encPal.Add(rawPal);
                }

                frameIds.Add(id);
                frameWaits.Add(f.Wait);
            }

            // ---- Build the frames table: [u16 id, u16 wait]* then 0xFFFF,0xFFFF (CRITICAL 3) ----
            var frames = new List<byte>();
            for (int i = 0; i < frameIds.Count; i++)
            {
                U.append_u16(frames, (uint)frameIds[i]);
                U.append_u16(frames, frameWaits[i]);
            }
            U.append_u16(frames, 0xFFFF);
            U.append_u16(frames, 0xFFFF);
            byte[] framesData = frames.ToArray();

            // ====================================================================
            //  MUTATION begins.
            //
            //  manageSnapshot == true  (single-import #916/#919): clone a
            //    defensive snapshot, open a FRESH BeginUndoScope, restore on any
            //    fault, and Push on success — EXACTLY the slice-1/2 behaviour.
            //  manageSnapshot == false (BULK #923/#885): the CALLER already owns
            //    the snapshot + the ambient BeginUndoScope. We DON'T clone, DON'T
            //    open a scope (writes compose into the caller's ambient one),
            //    DON'T restore (the bulk's single outer restore handles a fault),
            //    DON'T push. A per-skill fault is signalled by RETURNING the
            //    non-empty error; a thrown exception propagates to the caller.
            // ====================================================================
            if (!manageSnapshot)
            {
                // BULK mode: run the write directly under the caller's ambient
                // undo scope. WriteCore returns a non-empty error on any
                // allocation fault (NOT_FOUND); the bulk caller treats that
                // returned string as a FAULT and runs its single outer restore.
                // An exception still escapes to the bulk caller's try/catch.
                return WriteCore(rom, slot, uniqueNames.Count, encImage, encTsa, encPal,
                    framesData, programTemplate, script.SoundId, recyclePool, faultInjector);
            }

            byte[] snapshot = (byte[])rom.Data.Clone();
            var undoData = CoreState.Undo != null
                ? CoreState.Undo.NewUndoData("SkillSystemsAnimeImportCore.Import")
                : new Undo.UndoData();

            try
            {
                using (ROM.BeginUndoScope(undoData))
                {
                    string werr = WriteCore(rom, slot, uniqueNames.Count, encImage, encTsa, encPal,
                        framesData, programTemplate, script.SoundId, recyclePool, faultInjector);
                    if (!string.IsNullOrEmpty(werr))
                        throw new InvalidOperationException(werr);
                }
            }
            catch (Exception ex)
            {
                // Restore byte-for-byte: cached offsets stay valid (in-place copy),
                // the slot never half-flips, and no free-space bytes leak into a
                // live state. Forward-only undo per WF — the snapshot is the
                // authoritative revert; the undo scope is already closed.
                // In-place restore keeps cached offsets valid; the ROM is left
                // byte-identical to the pre-import snapshot (Modified flag was
                // already set by the partial writes — harmless and matches the
                // "ROM may need re-save" semantics other import rollbacks share).
                // (#923 H1 parity: a single-import that GREW rom.Data must also
                // shrink it back before the in-place copy, else the trailing
                // grown bytes survive. write_resize_data handles the down-resize.)
                if (rom.Data.Length != snapshot.Length)
                    rom.write_resize_data((uint)snapshot.Length);
                Array.Copy(snapshot, rom.Data, snapshot.Length);
                return "Skill animation import failed: " + ex.Message;
            }

            // Push the captured undo so the editor's redo/undo stack is correct.
            if (CoreState.Undo != null)
            {
                CoreState.Undo.Push(undoData);
            }
            return string.Empty; // success
        }

        // ================================================================
        // EnumerateOldAnimeRegions (#914) — read-only recycle enumeration
        // ================================================================

        /// <summary>
        /// Enumerate the recyclable sub-regions of the OLD skill animation the
        /// slot currently points at, so a re-import can REUSE them instead of
        /// leaking them. A literal, STRICTLY READ-ONLY port of WF
        /// <c>ImageUtilSkillSystemsAnimeCreator.RecycleOldAnime</c> (:637-784):
        /// the per-frame OBJ image (LZ77), TSA (LZ77) and palette (RAW 0x20)
        /// pointers, plus the program+config region and the three pointer-list
        /// blocks. Substitutes <paramref name="rom"/> for WF <c>Program.ROM</c>
        /// and the Core <see cref="SkillSystemsAnimeExportCore.SkipCode"/> for
        /// WF's <c>SkipCode</c>.
        ///
        /// <para><b>STRICTLY READ-ONLY:</b> performs ONLY reads
        /// (<c>rom.p32/u16</c>) and <c>list.Add</c> via the <c>Address.Add*</c>
        /// helpers — NO <c>rom.write_*</c>, NO RecycleAddress writes. It runs in
        /// the validate phase BEFORE the defensive snapshot clone, so the #885
        /// in-place restore must never depend on it.</para>
        ///
        /// <para><b>Two-tier WF semantics:</b> a PRE-walk guard failure (unsafe
        /// <paramref name="oldAnimeAddress"/>; <c>SkipCode == NOT_FOUND</c>;
        /// config block out of range; any of the 4 config pointers unsafe)
        /// returns the list EMPTY — nothing has accumulated, so a zero/garbage
        /// slot is a no-op. A MID-walk bail (an unsafe per-frame pointer or
        /// dereferenced offset, OR <c>n &gt;= limitter</c>) returns the
        /// PARTIALLY-populated list — NEVER cleared, exactly like WF and the
        /// sibling <c>RecycleOldMagicSlot</c>.</para>
        ///
        /// <para><b>Per-frame <c>count</c>:</b> incremented once PER frame-table
        /// entry (NOT per unique id); the freed pointer-list blocks are sized
        /// <c>(count+1)*4</c> (frames) and <c>count*4</c> (each list), and the
        /// per-frame <c>Add*</c> calls rely on the <c>RecycleAddress</c> pool's
        /// own dedup to collapse repeated-id pointers — so freed lengths match WF
        /// byte-for-byte.</para>
        /// </summary>
        /// <param name="rom">The ROM to read (must be the active CoreState.ROM —
        ///   the <c>Address.Add*</c> helpers dereference via CoreState.ROM).</param>
        /// <param name="oldAnimeAddress">The slot's current anime target
        ///   (<c>rom.p32(slot)</c>); a GBA pointer or a ROM offset (toOffset is
        ///   idempotent). 0 / garbage ⇒ empty list.</param>
        /// <returns>The recyclable regions (possibly empty / partial — see above).</returns>
        public static List<Address> EnumerateOldAnimeRegions(ROM rom, uint oldAnimeAddress)
        {
            const string basename = "OLDANIME";
            const bool isPointerOnly = false;
            var recycle = new List<Address>();

            if (rom == null || rom.Data == null) return recycle;

            // The Address.Add* helpers below dereference CoreState.ROM internally,
            // so a foreign rom would compute the recycle pool from mismatched data
            // (mirrors the ReferenceEquals guard in ImportSkillAnimation).
            if (!ReferenceEquals(rom, CoreState.ROM)) return new List<Address>();

            // WF :639-643 — guard the anime address.
            uint anime_address = U.toOffset(oldAnimeAddress);
            if (!U.isSafetyOffset(anime_address, rom))
            {
                return recycle;
            }

            // WF :648-652 — resolve the config address (FE8J direct, FE8U skips
            // the program template prefix). NOT_FOUND ⇒ empty list (pre-walk).
            bool isDefender;
            uint anime_config_address = SkillSystemsAnimeExportCore.SkipCode(rom, anime_address, out isDefender);
            if (anime_config_address == U.NOT_FOUND)
            {
                return recycle;
            }
            // WF :653-656 — config block (5 words) must be in range.
            if (anime_config_address + (4 * 5) > rom.Data.Length)
            {//範囲外
                return recycle;
            }

            // WF :663-667 — the 4 config pointers (frames/tsalist/graphiclist/
            // palettelist); the 5th word is the raw sound id (not a pointer).
            uint frames = rom.p32(anime_config_address + (4 * 0));
            uint tsalist = rom.p32(anime_config_address + (4 * 1));
            uint graphiclist = rom.p32(anime_config_address + (4 * 2));
            uint palettelist = rom.p32(anime_config_address + (4 * 3));

            // WF :669-684 — each of the 4 config pointers must be safe (pre-walk).
            if (!U.isSafetyOffset(frames, rom))
            {
                return recycle;
            }
            if (!U.isSafetyOffset(tsalist, rom))
            {
                return recycle;
            }
            if (!U.isSafetyOffset(graphiclist, rom))
            {
                return recycle;
            }
            if (!U.isSafetyOffset(palettelist, rom))
            {
                return recycle;
            }

            // WF :686-688 — the frames table is uncompressed, so cap the scan at
            // 1 MB (clamped to the ROM size) to avoid runaway on a corrupt table.
            uint limitter = frames + 1024 * 1024; //1MBサーチしたらもうあきらめる.
            limitter = (uint)Math.Min(limitter, rom.Data.Length);

            // WF :690-751 — walk the [u16 id, u16 wait]* frames table to the
            // 0xFFFF terminator. count is PER-FRAME (not per unique id).
            uint count = 0;
            uint n;
            for (n = frames; n < limitter; n += 4)
            {
                uint id = rom.u16(n + 0);
                //uint wait = rom.u16(n + 2);
                if (id == 0xFFFF)
                {
                    break;
                }

                uint objPointer = graphiclist + (id * 4);
                uint tsaPointer = tsalist + (id * 4);
                uint palPointer = palettelist + (id * 4);
                count++;

                // WF :706-717 — each per-frame pointer SLOT (and its trailing
                // word) must be safe; a mid-walk bail returns the PARTIAL list.
                if (!U.isSafetyOffset(objPointer + 4, rom))
                {
                    return recycle;
                }
                if (!U.isSafetyOffset(tsaPointer + 4, rom))
                {
                    return recycle;
                }
                if (!U.isSafetyOffset(palPointer + 4, rom))
                {
                    return recycle;
                }
                uint objOffset = rom.p32(objPointer);
                uint tsaOffset = rom.p32(tsaPointer);
                uint palOffset = rom.p32(palPointer);
                // WF :721-732 — each dereferenced offset must be safe.
                if (!U.isSafetyOffset(objOffset, rom))
                {
                    return recycle;
                }
                if (!U.isSafetyOffset(tsaOffset, rom))
                {
                    return recycle;
                }
                if (!U.isSafetyOffset(palOffset + 0x20, rom))
                {
                    return recycle;
                }

                // WF :733-750 — add the OBJ image (LZ77), TSA (LZ77) and palette
                // (RAW 0x20) for THIS frame. Per-frame Add* (no unique-id dedup
                // here — the RecycleAddress pool collapses repeated-id pointers).
                Address.AddLZ77Pointer(recycle
                    , objPointer
                    , basename + "OBJ"
                    , isPointerOnly
                    , Address.DataTypeEnum.LZ77IMG);

                Address.AddLZ77Pointer(recycle
                    , tsaPointer
                    , basename + "TSA"
                    , isPointerOnly
                    , Address.DataTypeEnum.LZ77TSA);

                Address.AddPointer(recycle
                    , palPointer
                    , 0x20   //16色*2バイト=0x20バイト
                    , basename + "PAL"
                    , Address.DataTypeEnum.PAL);
            }
            // WF :752-755 — limiter hit (no terminator found) ⇒ return the
            // PARTIAL list, WITHOUT adding the trailing config/list blocks.
            if (n >= limitter)
            {
                return recycle;
            }

            // WF :756-783 — the terminator was reached: add the program+config
            // region and the three pointer-list blocks. Block sizes mirror WF:
            // frames (count+1)*4, each list count*4.
            Address.AddAddress(recycle
                , anime_address
                , anime_config_address - anime_address + (4 * 5)
                , U.NOT_FOUND
                , basename + "POINTER"
                , Address.DataTypeEnum.POINTER);

            Address.AddPointer(recycle
                , anime_config_address + (4 * 0)
                , (count + 1) * 4
                , basename + "ROMANIMEFRAME"
                , Address.DataTypeEnum.ROMANIMEFRAME);

            Address.AddPointer(recycle
                , anime_config_address + (4 * 1)
                , (count) * 4
                , basename + "TSALIST"
                , Address.DataTypeEnum.POINTER);
            Address.AddPointer(recycle
                , anime_config_address + (4 * 2)
                , (count) * 4
                , basename + "IMAGELIST"
                , Address.DataTypeEnum.POINTER);
            Address.AddPointer(recycle
                , anime_config_address + (4 * 3)
                , (count) * 4
                , basename + "PALETTELIST"
                , Address.DataTypeEnum.POINTER);

            return recycle;
        }

        /// <summary>
        /// The raw ROM-write half of <see cref="ImportSkillAnimation"/>: allocate
        /// every per-frame region + the three parallel pointer lists + the 5-word
        /// config block (with the optional FE8U program prefix), then repoint the
        /// slot LAST. Runs under whatever ambient undo scope the caller has open
        /// (single-import opens its own; bulk reuses the bulk scope). Returns an
        /// empty string on success or a non-empty error on an allocation fault
        /// (so a NOT_FOUND surfaces as a returned string in BULK mode and as a
        /// thrown InvalidOperationException in single-import mode). The
        /// <paramref name="faultInjector"/> still fires once after the first
        /// frame's regions (test-only).
        /// </summary>
        static string WriteCore(
            ROM rom, uint slot, int uniqueCount,
            List<byte[]> encImage, List<byte[]> encTsa, List<byte[]> encPal,
            byte[] framesData, byte[] programTemplate, uint soundId,
            List<Address> recyclePool,
            Action faultInjector)
        {
            // Recycle pool from the slot's old anime region (#914). EMPTY ⇒
            // always fresh-allocate (zero/garbage slot, or recycleOldRegion:false).
            var ra = new RecycleAddress(recyclePool);

            var imagePtrs = new uint[uniqueCount];
            var tsaPtrs   = new uint[uniqueCount];
            var palPtrs   = new uint[uniqueCount];

            // Per-frame regions: image_lz77, tsa_lz77, pal_raw.
            for (int i = 0; i < uniqueCount; i++)
            {
                imagePtrs[i] = ra.WriteAmbient(encImage[i]);
                if (imagePtrs[i] == U.NOT_FOUND)
                    return "Cannot allocate space for frame image " + i + ".";

                tsaPtrs[i] = ra.WriteAmbient(encTsa[i]);
                if (tsaPtrs[i] == U.NOT_FOUND)
                    return "Cannot allocate space for frame TSA " + i + ".";

                palPtrs[i] = ra.WriteAmbient(encPal[i]);
                if (palPtrs[i] == U.NOT_FOUND)
                    return "Cannot allocate space for frame palette " + i + ".";

                // Test-only fault hook: fire once after the first frame's
                // regions are written but before the slot is repointed, so
                // the corruption guard can assert byte-identity after a
                // genuine partial write.
                if (i == 0 && faultInjector != null)
                    faultInjector();
            }

            // Three parallel pointer lists (image, tsa, pal) — each a
            // u32-pointer array (WF :565-578, order image/tsa/pal).
            var imageListBytes = new List<byte>();
            var tsaListBytes   = new List<byte>();
            var palListBytes   = new List<byte>();
            for (int i = 0; i < uniqueCount; i++)
            {
                U.append_u32(imageListBytes, U.toPointer(imagePtrs[i]));
                U.append_u32(tsaListBytes,   U.toPointer(tsaPtrs[i]));
                U.append_u32(palListBytes,   U.toPointer(palPtrs[i]));
            }

            uint framesOff   = ra.WriteAmbient(framesData);
            if (framesOff == U.NOT_FOUND) return "Cannot allocate space for frames table.";
            uint tsaListOff  = ra.WriteAmbient(tsaListBytes.ToArray());
            if (tsaListOff == U.NOT_FOUND) return "Cannot allocate space for TSA list.";
            uint imageListOff = ra.WriteAmbient(imageListBytes.ToArray());
            if (imageListOff == U.NOT_FOUND) return "Cannot allocate space for image list.";
            uint palListOff  = ra.WriteAmbient(palListBytes.ToArray());
            if (palListOff == U.NOT_FOUND) return "Cannot allocate space for palette list.";

            // mainData: [FE8U program template prefix], frames, tsalist,
            // imagelist, pallist (toPointer), then sound_id (RAW u32, NOT
            // toPointer — WF :616).
            var mainData = new List<byte>();

            // GUARD C (#917): prepend the FE8U program template VERBATIM —
            // no endian swap, no pad, no truncate (WF :592/:597
            // mainData.AddRange(File.ReadAllBytes(prog))). FE8J → empty
            // prefix (programTemplate.Length == 0), so the FE8J layout is
            // byte-identical to slice 1. The slot (repointed below) points
            // to the template START, so the export SkipCode skips exactly
            // this prefix on re-read.
            if (programTemplate.Length > 0)
                mainData.AddRange(programTemplate);

            U.append_u32(mainData, U.toPointer(framesOff));
            U.append_u32(mainData, U.toPointer(tsaListOff));
            U.append_u32(mainData, U.toPointer(imageListOff));
            U.append_u32(mainData, U.toPointer(palListOff));
            U.append_u32(mainData, soundId);

            // LAST op: allocate mainData + repoint the slot (WF :620).
            uint mainOff = ra.WriteAndWritePointerAmbient(slot, mainData.ToArray());
            if (mainOff == U.NOT_FOUND)
                return "Cannot allocate space for the animation config block.";

            return string.Empty;
        }

        // ================================================================
        // Helpers
        // ================================================================

        /// <summary>
        /// Crop/pad an indexed pixel buffer to <paramref name="dstW"/>×<paramref name="dstH"/>,
        /// top-left anchored, padding with index 0 (mirrors WF
        /// <c>ImageUtil.Copy(bmp, 0, 0, 240, 160)</c> :528). Returns the input
        /// unchanged when it is already the target size.
        /// </summary>
        static byte[] NormalizeTo(byte[] idx, int srcW, int srcH, int dstW, int dstH)
        {
            if (srcW == dstW && srcH == dstH) return idx;
            byte[] dst = new byte[dstW * dstH]; // zero-filled = index 0 pad
            int rows = Math.Min(srcH, dstH);
            int cols = Math.Min(srcW, dstW);
            for (int row = 0; row < rows; row++)
            {
                Array.Copy(idx, row * srcW, dst, row * dstW, cols);
            }
            return dst;
        }

        /// <summary>Force <paramref name="data"/> to exactly <paramref name="len"/> bytes (truncate/zero-pad).</summary>
        static byte[] EnsureExactLength(byte[] data, int len)
        {
            if (data != null && data.Length == len) return data;
            var result = new byte[len];
            if (data != null) Array.Copy(data, result, Math.Min(data.Length, len));
            return result;
        }

        /// <summary>
        /// Bounds-safe region check: <paramref name="addr"/> passes
        /// <c>isSafetyOffset</c> AND the whole <paramref name="bytes"/>-byte span
        /// stays inside rom.Data (ulong arithmetic guards overflow).
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
