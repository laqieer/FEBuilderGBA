// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform, ROM-MUTATING Import helper for SkillSystems skill animations
// (.txt script + per-frame PNGs → ROM write). SLICE 1 of issue #913 — ported
// from WF `FEBuilderGBA/ImageUtilSkillSystemsAnimeCreator.cs` Import (:460-635).
//
// SCOPE — FE8J path ONLY:
//   * FE8J (is_multibyte): no per-skill program prefix; mainData begins at the
//     5-word config block (frames/tsalist/imagelist/pallist/sound_id).
//   * FE8U (NOT is_multibyte): returns a clean not-supported error with ZERO
//     mutation. FE8U prepends a `skillanimtemplate*.dmp` program code block
//     (WF :589-598) — re-emitting that is Slice 2.
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
// PARITY GAP (intentional, documented): WF calls RecycleOldAnime (:560) to reuse
//   the overwritten anime region. This port does NOT — it always fresh-allocates
//   from free space, which is strictly SAFER (never overwrites live data that
//   another anime might share) but LEAKS the old anime region per import. A
//   follow-up issue should add an event-aware recycle pass once the
//   InputFormRef-free recycle enumeration is available in Core.
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
        /// <para><b>FE8J only.</b> FE8U returns a clean not-supported error with
        /// ZERO mutation.</para>
        ///
        /// <para><b>Validate-before-mutate:</b> the script parse, every PNG load
        /// + encode, and the slot-safety check all run BEFORE any ROM write. Any
        /// failure returns a non-empty error and leaves the ROM byte-identical.</para>
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
        /// <returns>Empty string on success; a non-empty error on any rejection.</returns>
        public static string ImportSkillAnimation(
            ROM rom,
            string[] scriptLines,
            uint animePointerSlot,
            ImageProvider imageProvider,
            Action faultInjector = null)
        {
            // ---- Pre-flight guards (no mutation) ----
            if (rom == null || rom.Data == null) return "ROM is not loaded.";
            if (rom.RomInfo == null) return "ROM info is not available.";
            if (imageProvider == null) return "Image provider is null.";
            if (!ReferenceEquals(rom, CoreState.ROM))
                return "Internal error: ROM is not the active CoreState.ROM.";

            // FE8U deferral — clean, ZERO mutation (Slice 2 re-emits the program code).
            if (!rom.RomInfo.is_multibyte)
                return "FE8U skill-anime import is not yet supported (Slice 2: program-code re-emit).";

            // ---- VALIDATE script ----
            SkillAnimeScript script = ParseScript(scriptLines);
            if (!string.IsNullOrEmpty(script.Error)) return script.Error;

            // ---- VALIDATE slot ----
            uint slot = U.toOffset(animePointerSlot);
            if (!IsRegionSafe(rom, slot, 4))
                return "Animation pointer slot is out of range.";

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
            //  MUTATION begins. Snapshot first so any fault restores byte-identity.
            // ====================================================================
            byte[] snapshot = (byte[])rom.Data.Clone();
            var undoData = CoreState.Undo != null
                ? CoreState.Undo.NewUndoData("SkillSystemsAnimeImportCore.Import")
                : new Undo.UndoData();

            try
            {
                using (ROM.BeginUndoScope(undoData))
                {
                    // No recycle pool (intentional parity gap — always fresh-allocate).
                    var ra = new RecycleAddress(new List<Address>());

                    int uniqueCount = uniqueNames.Count;
                    var imagePtrs = new uint[uniqueCount];
                    var tsaPtrs   = new uint[uniqueCount];
                    var palPtrs   = new uint[uniqueCount];

                    // Per-frame regions: image_lz77, tsa_lz77, pal_raw.
                    for (int i = 0; i < uniqueCount; i++)
                    {
                        imagePtrs[i] = ra.WriteAmbient(encImage[i]);
                        if (imagePtrs[i] == U.NOT_FOUND)
                            throw new InvalidOperationException("Cannot allocate space for frame image " + i + ".");

                        tsaPtrs[i] = ra.WriteAmbient(encTsa[i]);
                        if (tsaPtrs[i] == U.NOT_FOUND)
                            throw new InvalidOperationException("Cannot allocate space for frame TSA " + i + ".");

                        palPtrs[i] = ra.WriteAmbient(encPal[i]);
                        if (palPtrs[i] == U.NOT_FOUND)
                            throw new InvalidOperationException("Cannot allocate space for frame palette " + i + ".");

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
                    if (framesOff == U.NOT_FOUND) throw new InvalidOperationException("Cannot allocate space for frames table.");
                    uint tsaListOff  = ra.WriteAmbient(tsaListBytes.ToArray());
                    if (tsaListOff == U.NOT_FOUND) throw new InvalidOperationException("Cannot allocate space for TSA list.");
                    uint imageListOff = ra.WriteAmbient(imageListBytes.ToArray());
                    if (imageListOff == U.NOT_FOUND) throw new InvalidOperationException("Cannot allocate space for image list.");
                    uint palListOff  = ra.WriteAmbient(palListBytes.ToArray());
                    if (palListOff == U.NOT_FOUND) throw new InvalidOperationException("Cannot allocate space for palette list.");

                    // mainData: frames, tsalist, imagelist, pallist (toPointer),
                    // then sound_id (RAW u32, NOT toPointer — WF :616).
                    var mainData = new List<byte>();
                    U.append_u32(mainData, U.toPointer(framesOff));
                    U.append_u32(mainData, U.toPointer(tsaListOff));
                    U.append_u32(mainData, U.toPointer(imageListOff));
                    U.append_u32(mainData, U.toPointer(palListOff));
                    U.append_u32(mainData, script.SoundId);

                    // LAST op: allocate mainData + repoint the slot (WF :620).
                    uint mainOff = ra.WriteAndWritePointerAmbient(slot, mainData.ToArray());
                    if (mainOff == U.NOT_FOUND)
                        throw new InvalidOperationException("Cannot allocate space for the animation config block.");
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
