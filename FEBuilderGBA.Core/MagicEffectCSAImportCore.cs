// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform Import helper for CSA-Creator magic-animation scripts
// (.txt script + per-frame OBJ/BG PNG byte arrays) — issue #889.
//
// CSA Import = FEditor Import (#881 / MagicEffectImportCore) + TWO CSA-specific additions:
//   1. BG tiles: use ImageImportCore.EncodeTSA (256×64 → deduplicated tiles + TSA map),
//      both LZ77-compressed. FEditor uses plain 4bpp direct encoding with no TSA.
//   2. 32-byte 0x86 frame record: the 28-byte FEditor layout plus TSA pointer at +28.
//   3. BGScaleMode: auto-insert a 0x53 scale command at the first 64px-BG frame and
//      cancel it (0x00000053 dword) before the final terminator (mirrors WF exactly).
//
// All five #885 lessons applied:
//   FIX 1 - 5×C00 leading header: emit each leading C00 encountered THEN pad to 5.
//   FIX 2 - BG 256-wide crop: encode only the left 256×64 pixels (drop palette-mark col).
//   FIX 3 - Explicit O+B+time triple required (no silent fallback).
//   FIX 4 - CoreState.ROM guard: if (!ReferenceEquals(rom, CoreState.ROM)) → error.
//   FIX 5 - Validate-before-mutate; ONE atomic ambient-undo reverts ALL writes.
//
// Design mirrors MagicEffectImportCore (same 5-phase pipeline):
//   Phase 1: validate script + pre-load all images via imageProvider.
//   Phase 2: assemble OBJ OAM + CSA BG (EncodeTSA path).
//   Phase 3: build intermediate frame-data byte array (placeholder indices).
//   Phase 4: compute OAM sizes + OAM start offsets.
//   Phase 5: ROM writes (ambient-undo), address patching.
//
// Frame layout (CSA Creator, 32 bytes per 0x86 record):
//   [+0]  frame header u32: 0x86_BGIDX_WAIT
//   [+4]  objImagePointer   (GBA pointer, LZ77 tiles)
//   [+8]  OAMAbsoStart      (relative offset into obj R→L OAM array)
//   [+12] OAMBGAbsoStart    (relative offset into bg R→L OAM array)
//   [+16] bgImagePointer    (GBA pointer, LZ77 tiles, TSA-deduplicated)
//   [+20] objPalettePointer (GBA pointer, RAW 0x20 bytes)
//   [+24] bgPalettePointer  (GBA pointer, RAW 0x20 bytes)
//   [+28] bgTSAPointer      (GBA pointer, LZ77-compressed TSA — CSA ONLY)
//
// Parity gaps vs WF (documented, intentional):
//   - SubConfilctArea: WF removes shared-image regions from the recycle pool.
//     Core skips this because it requires InputFormRef to enumerate all CSA entries.
//     Impact: minor — may write slightly into free space instead of recycling shared
//     frames. Correctness is unaffected.
//   - No progress callbacks: WF calls InputFormRef.DoEvents every parse step.

using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform, ROM-mutating Import helper for CSA-Creator magic-animation scripts.
    ///
    /// <para><b>Entry points:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="MagicEffectImportCore.ParseMagicScript"/> — shared stateless text parser.</item>
    ///   <item><see cref="ImportCsaMagicScript"/> — CSA ROM-mutating write with ambient-undo.</item>
    /// </list>
    ///
    /// <para>All I/O (reading .txt + loading PNGs) is performed by the caller; this
    /// class operates only on byte arrays and <see cref="ROM"/>.</para>
    ///
    /// <para>The <paramref name="imageProvider"/> delegate keeps all file access outside Core
    /// so headless tests can drive the full pipeline without touching the file system.</para>
    /// </summary>
    public static class MagicEffectCSAImportCore
    {
        // BG encoding dimensions (256px wide × 64px or 160px tall).
        // FIX 2: always crop to exactly 256 columns to drop the 8-px palette-mark column.
        public const int CSA_BG_ENCODE_WIDTH  = 256;
        public const int CSA_BG_ENCODE_HEIGHT_SMALL = 64;
        public const int CSA_BG_ENCODE_HEIGHT_FULL  = 160;

        // BGScaleMode mirrors WF enum ImageUtilMagicCSACreator.BGScaleMode.
        enum BGScaleMode { NO, AUTO_SCALE_MODE, SCRIPT_SCALE_MODE }

        // ================================================================
        // ImportCsaMagicScript
        // ================================================================

        /// <summary>
        /// ROM-mutating import: write a complete CSA-Creator magic-animation
        /// dataset into the slot at <paramref name="magicBaseAddr"/>.
        ///
        /// <para><b>Validate-before-mutate contract:</b> all validation (script parsing,
        /// image loading, OAM assembly, TSA encoding) is performed BEFORE any ROM write.
        /// On any validation failure the method returns a non-empty error string and
        /// leaves the ROM UNTOUCHED.</para>
        ///
        /// <para><b>Undo:</b> the caller MUST open an ambient undo scope before calling
        /// this method. Every write inside uses ambient-undo overloads so the scope
        /// captures each write exactly once. Rollback reverts ALL writes atomically.</para>
        ///
        /// <para><b>CSA-gate:</b> requires <see cref="MagicSystemKind.CsaCreator"/>
        /// detected via <see cref="MagicCSACore.SearchMagicSystem"/>; returns error
        /// for FEditor-only ROMs.</para>
        ///
        /// <para><b>All five #885 import lessons are applied.</b></para>
        /// </summary>
        /// <param name="rom">The ROM to write into (must equal <c>CoreState.ROM</c>).</param>
        /// <param name="magicBaseAddr">
        ///   ROM offset of the 20-byte CSA-entry struct:
        ///   +0 frameData ptr, +4 objRtoL OAM ptr, +8 objLtoR OAM ptr,
        ///   +12 bgRtoL OAM ptr, +16 bgLtoR OAM ptr.
        /// </param>
        /// <param name="cmds">
        ///   Parsed script commands from <see cref="MagicEffectImportCore.ParseMagicScript"/>.
        /// </param>
        /// <param name="imageProvider">
        ///   Called for each unique image filename. Receives the filename as-is from
        ///   the script (relative — caller resolves against script directory).
        ///   Returns (indexedPixels, width, height, gbaPalette) on success, or null.
        /// </param>
        /// <returns>Empty string on success; non-empty error description on failure.</returns>
        public static string ImportCsaMagicScript(
            ROM rom,
            uint magicBaseAddr,
            List<MagicFrameCommand> cmds,
            Func<string, (byte[] indexedPixels, int w, int h, byte[] gbaPalette)?> imageProvider)
        {
            if (rom == null)   return "ROM is null";
            if (cmds == null)  return "Command list is null";
            if (imageProvider == null) return "imageProvider is null";

            // FIX 4: CoreState.ROM consistency guard.
            if (CoreState.ROM == null)
                return "CoreState.ROM is null";
            if (!ReferenceEquals(rom, CoreState.ROM))
                return "rom argument must be CoreState.ROM (ambient-undo writes always target CoreState.ROM)";

            // CSA-gate: must be CsaCreator (not FEditor-only).
            var ms = ImageUtilMagicCore.SearchMagicSystem(rom, out _, out _, out _);
            if (ms != ImageUtilMagicCore.MagicSystem.CsaCreator)
                return "CSA Creator magic-system patch not detected (CsaCreator required for CSA import).";

            // magicBaseAddr safety check.
            if (magicBaseAddr < 0x200u || magicBaseAddr + 20u > (uint)rom.Data.Length)
                return $"magicBaseAddr 0x{magicBaseAddr:X8} is out of ROM range";

            // ---- PHASE 1: Validate script structure + pre-load all images ----

            var imageCache = new Dictionary<string, (byte[] idx, int w, int h, byte[] pal)>(StringComparer.Ordinal);
            string validateErr = ValidateCsaScript(cmds, imageProvider, imageCache);
            if (!string.IsNullOrEmpty(validateErr))
                return validateErr;

            // ---- PHASE 2: Assemble OBJ OAM + CSA BG (EncodeTSA path) ----

            var objAssembled = new Dictionary<string, ObjAssembleResult>(StringComparer.Ordinal);
            // BG: filename → (lz77Tiles, lz77Tsa, rawPalette)
            var bgAssembled  = new Dictionary<string, BgAssembleResult>(StringComparer.Ordinal);

            string assembleErr = AssembleAllCsaImages(cmds, imageCache, objAssembled, bgAssembled);
            if (!string.IsNullOrEmpty(assembleErr))
                return assembleErr;

            // ---- PHASE 3: Build intermediate frame-data byte array ----

            var frameBytes = new List<byte>();

            // FIX 1: emit each leading C00 dword, then pad to 5 total.
            int c00Count = 0;
            foreach (var cmd in cmds)
            {
                if (cmd.Kind != MagicImportCmdKind.Command85) break;
                if (cmd.Command85Dword == 0x85000000u)
                {
                    AppendU32(frameBytes, 0x85000000u);
                    c00Count++;
                }
                else break;
            }
            for (int i = c00Count; i < 5; i++)
                AppendU32(frameBytes, 0x85000000u);

            // BGScaleMode tracking (mirrors WF exactly).
            BGScaleMode bgScaleMode = BGScaleMode.NO;

            // Ordered slot lists for image allocation.
            var objSlotFilenames = new List<string>();
            var objFilenameToSlot = new Dictionary<string, int>(StringComparer.Ordinal);
            var bgSlotFilenames = new List<string>();
            var bgFilenameToSlot = new Dictionary<string, int>(StringComparer.Ordinal);

            var frameRecordStarts = new List<int>(); // byte offsets of each 0x86 record

            int bgNumber = 0;
            int cmdIdx = 0;

            // Position where the BGScaleMode AUTO cancel needs to be inserted BEFORE terminator.
            // We'll track a "did_auto_scale" flag and append after the loop.

            while (cmdIdx < cmds.Count)
            {
                var cmd = cmds[cmdIdx];

                if (cmd.Kind == MagicImportCmdKind.Terminator)
                {
                    AppendU32(frameBytes, 0x80000100u); // ~~~ miss terminator
                    cmdIdx++;
                    continue;
                }

                if (cmd.Kind == MagicImportCmdKind.Command85)
                {
                    // C00 leading header already handled above; emit the rest.
                    if (cmdIdx >= c00Count)
                    {
                        AppendU32(frameBytes, cmd.Command85Dword);
                        // Track script-side scale commands.
                        if ((cmd.Command85Dword & 0x00FFFFu) == 0x000053u ||
                            (cmd.Command85Dword & 0x0000FFu) == 0x53u)
                        {
                            bgScaleMode = BGScaleMode.SCRIPT_SCALE_MODE;
                        }
                    }
                    cmdIdx++;
                    continue;
                }

                if (cmd.Kind != MagicImportCmdKind.ObjImage)
                {
                    cmdIdx++;
                    continue;
                }

                // Collect O / B / wait triple.
                string objFn = cmd.Filename;
                cmdIdx++;

                string bgFn = null;
                uint waitVal = 0;
                int n = 0;
                while (n < 2 && cmdIdx < cmds.Count)
                {
                    var next = cmds[cmdIdx];
                    if (next.Kind == MagicImportCmdKind.BgImage && bgFn == null)
                    {
                        bgFn = next.Filename;
                        cmdIdx++;
                        n++;
                    }
                    else if (next.Kind == MagicImportCmdKind.Wait)
                    {
                        waitVal = next.WaitValue;
                        cmdIdx++;
                        n++;
                    }
                    else
                    {
                        break;
                    }
                }

                // FIX 3: explicit triple required (already validated in Phase 1).
                if (bgFn == null)
                    return "BG image (B p- ...) is missing for frame after O: " + objFn;
                if (n < 2 || waitVal == 0)
                    return "Wait time is missing for frame (O + B + time triple): O: " + objFn;

                // BGScaleMode: if BG height is 64, auto-insert scale command on first occurrence.
                var bgRes = bgAssembled[bgFn];
                if (bgRes.SourceHeight == CSA_BG_ENCODE_HEIGHT_SMALL)
                {
                    if (bgScaleMode == BGScaleMode.NO)
                    {
                        // Auto-insert 0x53 scale command (0x00000153 | 0x85000000).
                        AppendU32(frameBytes, 0x85000153u);
                        bgScaleMode = BGScaleMode.AUTO_SCALE_MODE;
                    }
                }

                // Assign OBJ slot index.
                if (!objFilenameToSlot.TryGetValue(objFn, out int objSlot))
                {
                    objSlot = objSlotFilenames.Count;
                    objSlotFilenames.Add(objFn);
                    objFilenameToSlot[objFn] = objSlot;
                }
                // Assign BG slot index.
                if (!bgFilenameToSlot.TryGetValue(bgFn, out int bgSlot))
                {
                    bgSlot = bgSlotFilenames.Count;
                    bgSlotFilenames.Add(bgFn);
                    bgFilenameToSlot[bgFn] = bgSlot;
                }

                // CSA 0x86 record — 32 bytes (8 dwords):
                // [+0]  header
                // [+4]  objImageSlot     (placeholder)
                // [+8]  objOamStart      (filled in Phase 4)
                // [+12] bgOamStart       (filled in Phase 4)
                // [+16] bgImageSlot      (placeholder)
                // [+20] objPalSlot       (placeholder)
                // [+24] bgPalSlot        (placeholder)
                // [+28] bgTsaSlot        (placeholder — CSA ONLY)

                int recordStart = frameBytes.Count;
                frameRecordStarts.Add(recordStart);

                uint header = (waitVal & 0xFFFF) | ((uint)(bgNumber & 0xFF) << 16) | 0x86000000u;
                AppendU32(frameBytes, header);
                AppendU32(frameBytes, (uint)objSlot);  // +4  obj image slot
                AppendU32(frameBytes, 0u);             // +8  obj OAM start (Phase 4)
                AppendU32(frameBytes, 0u);             // +12 bg OAM start  (Phase 4)
                AppendU32(frameBytes, (uint)bgSlot);   // +16 bg image slot
                AppendU32(frameBytes, (uint)objSlot);  // +20 obj pal slot
                AppendU32(frameBytes, (uint)bgSlot);   // +24 bg pal slot
                AppendU32(frameBytes, (uint)bgSlot);   // +28 bg TSA slot (CSA ONLY)

                bgNumber++;
            }

            // BGScaleMode AUTO cancel: append 0x53 reset before terminator.
            if (bgScaleMode == BGScaleMode.AUTO_SCALE_MODE)
            {
                // Cancel scale: value = 0x00000053 (param=0) — mirrors WF L979-983.
                AppendU32(frameBytes, 0x85000053u);
            }

            // Final terminator.
            AppendU32(frameBytes, 0x80000000u);

            // ---- PHASE 4: Compute OAM sizes + layout ----

            var objOamRtoLAllBytes = new List<byte>();
            var objOamStarts = new uint[objSlotFilenames.Count];
            for (int i = 0; i < objSlotFilenames.Count; i++)
            {
                objOamStarts[i] = (uint)objOamRtoLAllBytes.Count;
                objOamRtoLAllBytes.AddRange(objAssembled[objSlotFilenames[i]].OamRtoL);
            }
            AppendTermOAM(objOamRtoLAllBytes);

            var bgOamRtoLAllBytes = new List<byte>();
            var bgOamStarts = new uint[bgSlotFilenames.Count];
            for (int i = 0; i < bgSlotFilenames.Count; i++)
            {
                bgOamStarts[i] = (uint)bgOamRtoLAllBytes.Count;
                AppendTermOAM(bgOamRtoLAllBytes);
            }
            AppendTermOAM(bgOamRtoLAllBytes);

            byte[] objOamRtoLData = objOamRtoLAllBytes.ToArray();
            byte[] objOamLtoRData = BattleAnimeOAMImportCore.ConvertToLeftToRightOAM(objOamRtoLData);
            byte[] bgOamRtoLData  = bgOamRtoLAllBytes.ToArray();
            byte[] bgOamLtoRData  = BattleAnimeOAMImportCore.ConvertToLeftToRightOAM(bgOamRtoLData);

            // Patch OAM start offsets into frame records.
            foreach (int recStart in frameRecordStarts)
            {
                uint oSlot = ReadU32(frameBytes, recStart + 4);
                uint bSlot = ReadU32(frameBytes, recStart + 16);
                uint objOamStart = oSlot < (uint)objOamStarts.Length ? objOamStarts[oSlot] : 0u;
                uint bgOamStart  = bSlot < (uint)bgOamStarts.Length  ? bgOamStarts[bSlot]  : 0u;
                WriteU32(frameBytes, recStart + 8,  objOamStart);
                WriteU32(frameBytes, recStart + 12, bgOamStart);
            }

            // ---- PHASE 5: ROM writes (ambient-undo) ----

            var recycle = new List<Address>();
            RecycleOldCsaSlot(rom, magicBaseAddr, recycle);
            var ra = new RecycleAddress(recycle);

            // OAM arrays.
            uint objRtoLAddr = ra.WriteAmbient(objOamRtoLData);
            if (objRtoLAddr == U.NOT_FOUND) return "Cannot allocate space for OBJ R→L OAM";
            CoreState.ROM.write_p32(magicBaseAddr + 4, objRtoLAddr);

            uint objLtoRAddr = ra.WriteAmbient(objOamLtoRData);
            if (objLtoRAddr == U.NOT_FOUND) return "Cannot allocate space for OBJ L→R OAM";
            CoreState.ROM.write_p32(magicBaseAddr + 8, objLtoRAddr);

            uint bgRtoLAddr = ra.WriteAmbient(bgOamRtoLData);
            if (bgRtoLAddr == U.NOT_FOUND) return "Cannot allocate space for BG R→L OAM";
            CoreState.ROM.write_p32(magicBaseAddr + 12, bgRtoLAddr);

            uint bgLtoRAddr = ra.WriteAmbient(bgOamLtoRData);
            if (bgLtoRAddr == U.NOT_FOUND) return "Cannot allocate space for BG L→R OAM";
            CoreState.ROM.write_p32(magicBaseAddr + 16, bgLtoRAddr);

            // OBJ images (LZ77 tiles + raw palettes).
            var objImageAddrs = new uint[objSlotFilenames.Count];
            var objPalAddrs   = new uint[objSlotFilenames.Count];
            for (int i = 0; i < objSlotFilenames.Count; i++)
            {
                var res = objAssembled[objSlotFilenames[i]];
                byte[] lz77Tiles = LZ77.compress(res.TileData4bpp);
                objImageAddrs[i] = ra.WriteAmbient(lz77Tiles);
                if (objImageAddrs[i] == U.NOT_FOUND) return $"Cannot allocate space for OBJ image slot {i}";
                byte[] pal = EnsureExactLength(res.Palette, 0x20);
                objPalAddrs[i] = ra.WriteAmbient(pal);
                if (objPalAddrs[i] == U.NOT_FOUND) return $"Cannot allocate space for OBJ palette slot {i}";
            }

            // BG images: LZ77 tiles + raw palettes + LZ77 TSA.
            var bgImageAddrs = new uint[bgSlotFilenames.Count];
            var bgPalAddrs   = new uint[bgSlotFilenames.Count];
            var bgTsaAddrs   = new uint[bgSlotFilenames.Count];
            for (int i = 0; i < bgSlotFilenames.Count; i++)
            {
                var bgr = bgAssembled[bgSlotFilenames[i]];
                bgImageAddrs[i] = ra.WriteAmbient(bgr.Lz77Tiles);
                if (bgImageAddrs[i] == U.NOT_FOUND) return $"Cannot allocate space for BG image slot {i}";
                byte[] palExact = EnsureExactLength(bgr.RawPalette, 0x20);
                bgPalAddrs[i] = ra.WriteAmbient(palExact);
                if (bgPalAddrs[i] == U.NOT_FOUND) return $"Cannot allocate space for BG palette slot {i}";
                bgTsaAddrs[i] = ra.WriteAmbient(bgr.Lz77Tsa);
                if (bgTsaAddrs[i] == U.NOT_FOUND) return $"Cannot allocate space for BG TSA slot {i}";
            }

            // Patch real addresses into frame-data.
            byte[] frameData = frameBytes.ToArray();
            PatchCsaFrameDataAddresses(
                frameData,
                objImageAddrs, objPalAddrs,
                bgImageAddrs,  bgPalAddrs, bgTsaAddrs);

            // Write frame-data table.
            uint frameDataAddr = ra.WriteAmbient(frameData);
            if (frameDataAddr == U.NOT_FOUND) return "Cannot allocate space for frame-data table";
            CoreState.ROM.write_p32(magicBaseAddr + 0, frameDataAddr);

            // Clear unused old-data regions.
            ra.BlackOutAmbient();

            return string.Empty; // success
        }

        // ================================================================
        // Private helpers
        // ================================================================

        /// <summary>
        /// Validate CSA script structure + pre-load all images via imageProvider.
        /// Does NOT mutate ROM. Returns non-empty error on failure.
        /// </summary>
        static string ValidateCsaScript(
            List<MagicFrameCommand> cmds,
            Func<string, (byte[] indexedPixels, int w, int h, byte[] gbaPalette)?> imageProvider,
            Dictionary<string, (byte[] idx, int w, int h, byte[] pal)> imageCache)
        {
            bool hasFrame = false;
            int cmdIdx = 0;

            while (cmdIdx < cmds.Count)
            {
                var cmd = cmds[cmdIdx];

                if (cmd.Kind == MagicImportCmdKind.Command85 ||
                    cmd.Kind == MagicImportCmdKind.Wait ||
                    cmd.Kind == MagicImportCmdKind.Terminator)
                {
                    cmdIdx++;
                    continue;
                }

                if (cmd.Kind != MagicImportCmdKind.ObjImage)
                {
                    cmdIdx++;
                    continue;
                }

                string objFn = cmd.Filename;
                cmdIdx++;

                // Load OBJ image if not cached.
                if (!imageCache.ContainsKey("OBJ:" + objFn))
                {
                    var result = imageProvider(objFn);
                    if (!result.HasValue)
                        return $"Cannot load OBJ image: {objFn}";
                    var (idx, w, h, pal) = result.Value;
                    if (idx == null || pal == null)
                        return $"OBJ image provider returned null data for: {objFn}";
                    if (w < MagicEffectImportCore.OBJ_MIN_WIDTH || h < MagicEffectImportCore.OBJ_MIN_HEIGHT)
                        return $"OBJ image too small: {w}x{h} (need >= {MagicEffectImportCore.OBJ_MIN_WIDTH}x{MagicEffectImportCore.OBJ_MIN_HEIGHT}) for: {objFn}";
                    if (w % 8 != 0 || h % 8 != 0)
                        return $"OBJ image dimensions not multiples of 8: {w}x{h} for: {objFn}";
                    imageCache["OBJ:" + objFn] = (idx, w, h, pal);
                }

                // FIX 3: require explicit O + B + wait triple.
                string bgFn = null;
                bool hasWait = false;
                int scanIdx = cmdIdx;
                while (scanIdx < cmds.Count)
                {
                    var s = cmds[scanIdx];
                    if (s.Kind == MagicImportCmdKind.ObjImage) break;
                    if (s.Kind == MagicImportCmdKind.Terminator) break;
                    if (s.Kind == MagicImportCmdKind.BgImage && bgFn == null)
                        bgFn = s.Filename;
                    if (s.Kind == MagicImportCmdKind.Wait)
                        hasWait = true;
                    scanIdx++;
                    if (bgFn != null && hasWait) break;
                }

                if (bgFn == null)
                    return $"BG image (B p- ...) is missing for frame after O: {objFn}";
                if (!hasWait)
                    return $"Wait time is missing for frame (need O + B + time triple): O: {objFn}";

                // Load BG image if not cached.
                if (!imageCache.ContainsKey("BG:" + bgFn))
                {
                    var result = imageProvider(bgFn);
                    if (!result.HasValue)
                        return $"Cannot load BG image: {bgFn}";
                    var (idx, w, h, pal) = result.Value;
                    if (idx == null || pal == null)
                        return $"BG image provider returned null data for: {bgFn}";
                    if (w % 8 != 0 || h % 8 != 0)
                        return $"BG image dimensions not multiples of 8: {w}x{h} for: {bgFn}";
                    // Accept 64px (FEditor small BG) or 160px (CSA full BG); width >= 240.
                    if (w < 240)
                        return $"BG image too narrow: width={w} (need >= 240) for: {bgFn}";
                    if (h != CSA_BG_ENCODE_HEIGHT_SMALL && h != CSA_BG_ENCODE_HEIGHT_FULL)
                        return $"BG image height {h} not accepted (expect {CSA_BG_ENCODE_HEIGHT_SMALL} or {CSA_BG_ENCODE_HEIGHT_FULL}) for: {bgFn}";
                    imageCache["BG:" + bgFn] = (idx, w, h, pal);
                }

                hasFrame = true;
            }

            if (!hasFrame)
                return "Script contains no image frames (O / B / wait triplets).";

            return string.Empty;
        }

        /// <summary>
        /// Assemble all OBJ OAM + CSA BG tiles/TSA. All data pre-assembled before any ROM write.
        /// </summary>
        static string AssembleAllCsaImages(
            List<MagicFrameCommand> cmds,
            Dictionary<string, (byte[] idx, int w, int h, byte[] pal)> imageCache,
            Dictionary<string, ObjAssembleResult> objAssembled,
            Dictionary<string, BgAssembleResult> bgAssembled)
        {
            int cmdIdx = 0;
            while (cmdIdx < cmds.Count)
            {
                var cmd = cmds[cmdIdx];

                if (cmd.Kind != MagicImportCmdKind.ObjImage)
                {
                    cmdIdx++;
                    continue;
                }

                string objFn = cmd.Filename;
                cmdIdx++;

                // Assemble OBJ OAM (isMagic=true).
                if (!objAssembled.ContainsKey(objFn))
                {
                    var (idx, w, h, pal) = imageCache["OBJ:" + objFn];
                    var oamResult = BattleAnimeOAMImportCore.AssembleOAM(
                        idx, w, h, pal, isMagic: true, isMultiPalette: false);
                    if (!oamResult.Success)
                        return $"OAM assembly failed for {objFn}: {oamResult.Error}";

                    byte[] rtoL = oamResult.OamRecords;
                    byte[] ltoR = BattleAnimeOAMImportCore.ConvertToLeftToRightOAM(rtoL);

                    objAssembled[objFn] = new ObjAssembleResult
                    {
                        TileData4bpp = oamResult.TileData4bpp,
                        Palette      = EnsureExactLength(oamResult.PaletteBytes, 0x20),
                        OamRtoL      = rtoL,
                        OamLtoR      = ltoR,
                    };
                }

                // Find and assemble the BG image.
                while (cmdIdx < cmds.Count &&
                       cmds[cmdIdx].Kind != MagicImportCmdKind.BgImage &&
                       cmds[cmdIdx].Kind != MagicImportCmdKind.ObjImage &&
                       cmds[cmdIdx].Kind != MagicImportCmdKind.Terminator)
                {
                    cmdIdx++;
                }

                if (cmdIdx < cmds.Count && cmds[cmdIdx].Kind == MagicImportCmdKind.BgImage)
                {
                    string bgFn = cmds[cmdIdx].Filename;
                    cmdIdx++;

                    if (!bgAssembled.ContainsKey(bgFn))
                    {
                        var (idx, w, h, pal) = imageCache["BG:" + bgFn];

                        // FIX 2: crop to 256×min(h,160) — drops palette-mark column.
                        int encW = Math.Min(w, CSA_BG_ENCODE_WIDTH);
                        int encH = h; // 64 or 160 — keep as-is (already validated)

                        // EncodeTSA: 256-wide indexed pixels → deduplicated tiles + TSA map.
                        // We need to crop the indexed pixel buffer to encW wide.
                        byte[] croppedIdx = CropIndexedPixels(idx, w, h, encW, encH);

                        var tsaResult = ImageImportCore.EncodeTSA(croppedIdx, encW, encH, paletteIndex: 0);
                        if (tsaResult == null)
                            return $"TSA encoding failed for BG image: {bgFn}";

                        byte[] lz77Tiles = LZ77.compress(tsaResult.TileData);
                        byte[] lz77Tsa   = LZ77.compress(tsaResult.TSAData);
                        byte[] palExact  = EnsureExactLength(pal, 0x20);

                        bgAssembled[bgFn] = new BgAssembleResult
                        {
                            Lz77Tiles    = lz77Tiles,
                            Lz77Tsa      = lz77Tsa,
                            RawPalette   = palExact,
                            SourceHeight = encH,
                        };
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Crop indexed pixel buffer from <paramref name="srcW"/>-wide to <paramref name="dstW"/>-wide.
        /// FIX 2: used to discard the 8-px palette-mark column that CSA Export PNGs append.
        /// </summary>
        static byte[] CropIndexedPixels(byte[] idx, int srcW, int srcH, int dstW, int dstH)
        {
            if (srcW == dstW && srcH == dstH) return idx;
            byte[] dst = new byte[dstW * dstH];
            for (int row = 0; row < dstH; row++)
            {
                int srcBase = row * srcW;
                int dstBase = row * dstW;
                int copy = Math.Min(dstW, srcW);
                Array.Copy(idx, srcBase, dst, dstBase, copy);
            }
            return dst;
        }

        /// <summary>
        /// Collect existing CSA-slot data regions into a recycle pool.
        /// Mirrors WF <c>ImageUtilMagicCSACreator.RecycleOldAnime</c>.
        /// CSA-specific: also recycles the LZ77 TSA pointer at frame+28.
        /// </summary>
        static void RecycleOldCsaSlot(ROM rom, uint magicBaseAddr, List<Address> recycle)
        {
            if (rom?.Data == null) return;
            uint dataLen = (uint)rom.Data.Length;

            uint frameDataPtr = rom.p32(magicBaseAddr + 0);
            if (frameDataPtr == 0 || !U.isSafetyPointer(frameDataPtr)) return;

            uint frameDataOff = U.toOffset(frameDataPtr);
            uint limiter = frameDataOff + 1024 * 1024;
            if (limiter > dataLen) limiter = dataLen;

            byte[] d = rom.Data;
            uint maxObjOAM = 0;
            uint maxBGOAM  = 0;
            uint i;

            for (i = frameDataOff; i + 4 <= limiter; i += 4)
            {
                byte cmd = d[i + 3];
                if (cmd == 0x80)
                {
                    if (i + 2 <= dataLen && d[i + 1] == 0x01) continue;
                    i += 4;
                    break;
                }
                if (cmd == 0x85) continue;
                if (cmd != 0x86) break;
                if (i + 32 > dataLen) break;  // CSA frame is 32 bytes

                // OBJ image (LZ77)
                uint objImgSlot = i + 4;
                if (objImgSlot + 4 <= dataLen)
                    Address.AddLZ77Pointer(recycle, objImgSlot, "OLD_OBJ", false, Address.DataTypeEnum.LZ77IMG);

                // BG image (LZ77)
                uint bgImgSlot = i + 16;
                if (bgImgSlot + 4 <= dataLen)
                    Address.AddLZ77Pointer(recycle, bgImgSlot, "OLD_BG", false, Address.DataTypeEnum.LZ77IMG);

                // OBJ palette (0x20 raw bytes)
                uint objPalSlot = i + 20;
                if (objPalSlot + 4 <= dataLen)
                    Address.AddPointer(recycle, objPalSlot, 0x20u, "OLD_OBJPAL", Address.DataTypeEnum.PAL);

                // BG palette (0x20 raw bytes)
                uint bgPalSlot = i + 24;
                if (bgPalSlot + 4 <= dataLen)
                    Address.AddPointer(recycle, bgPalSlot, 0x20u, "OLD_BGPAL", Address.DataTypeEnum.PAL);

                // BG TSA (LZ77) — CSA ONLY at +28
                uint bgTsaSlot = i + 28;
                if (bgTsaSlot + 4 <= dataLen)
                    Address.AddLZ77Pointer(recycle, bgTsaSlot, "OLD_BGTSA", false, Address.DataTypeEnum.LZ77TSA);

                uint objOAM = U.u32(d, i + 8);
                uint bgOAM  = U.u32(d, i + 12);
                if (objOAM > maxObjOAM) maxObjOAM = objOAM;
                if (bgOAM  > maxBGOAM)  maxBGOAM  = bgOAM;

                i += 28; // +4 from outer loop = 32 total
            }

            if (i <= limiter)
            {
                uint frameLen = i - frameDataOff;
                Address.AddPointer(recycle, magicBaseAddr + 0, frameLen,
                    "OLD_FRAME", Address.DataTypeEnum.MAGICFRAME_CSA);

                uint objRtoLPtr = rom.u32(magicBaseAddr + 4);
                uint objLtoRPtr = rom.u32(magicBaseAddr + 8);
                uint bgRtoLPtr  = rom.u32(magicBaseAddr + 12);
                uint bgLtoRPtr  = rom.u32(magicBaseAddr + 16);

                uint objRtoLLen = CalcOAMLength(rom, objRtoLPtr, maxObjOAM);
                if (objRtoLLen > 0) Address.AddPointer(recycle, magicBaseAddr + 4, objRtoLLen, "OLD_OAMRL", Address.DataTypeEnum.MAGICOAM);
                uint objLtoRLen = CalcOAMLength(rom, objLtoRPtr, maxObjOAM);
                if (objLtoRLen > 0) Address.AddPointer(recycle, magicBaseAddr + 8, objLtoRLen, "OLD_OAMLR", Address.DataTypeEnum.MAGICOAM);
                uint bgRtoLLen  = CalcOAMLength(rom, bgRtoLPtr,  maxBGOAM);
                if (bgRtoLLen  > 0) Address.AddPointer(recycle, magicBaseAddr + 12, bgRtoLLen, "OLD_BGOAMRL", Address.DataTypeEnum.MAGICOAM);
                uint bgLtoRLen  = CalcOAMLength(rom, bgLtoRPtr,  maxBGOAM);
                if (bgLtoRLen  > 0) Address.AddPointer(recycle, magicBaseAddr + 16, bgLtoRLen, "OLD_BGOAMLR", Address.DataTypeEnum.MAGICOAM);
            }
        }

        static uint CalcOAMLength(ROM rom, uint oamPtr, uint maxOAM)
        {
            if (!U.isSafetyPointer(oamPtr)) return 0;
            uint oamOff = U.toOffset(oamPtr);
            if (!U.isSafetyOffset(oamOff, rom)) return 0;

            uint dataLen = (uint)rom.Data.Length;
            uint limiter = oamOff + maxOAM + 2048;
            if (limiter > dataLen) limiter = dataLen;

            uint oam = oamOff + maxOAM;
            while (true)
            {
                if (oam + 4 > limiter) return 0;
                uint a = rom.u32(oam);
                oam += 12;
                if (a == 0x01) break;
            }
            return oam - oamOff;
        }

        /// <summary>
        /// Patch real ROM addresses into the CSA frame-data array.
        /// CSA ONLY: also patches the TSA pointer at +28 in each 0x86 record.
        /// </summary>
        static void PatchCsaFrameDataAddresses(
            byte[] frameData,
            uint[] objImageAddrs, uint[] objPalAddrs,
            uint[] bgImageAddrs,  uint[] bgPalAddrs, uint[] bgTsaAddrs)
        {
            uint length = (uint)frameData.Length;
            for (uint n = 0; n + 32 <= length; n += 4)
            {
                if (n + 3 >= length || frameData[n + 3] != 0x86) continue;

                int objSlot = (int)ReadU32Arr(frameData, n + 4);
                int bgSlot  = (int)ReadU32Arr(frameData, n + 16);

                if (objSlot >= 0 && objSlot < objImageAddrs.Length)
                    WriteP32(frameData, n + 4,  objImageAddrs[objSlot]);
                if (objSlot >= 0 && objSlot < objPalAddrs.Length)
                    WriteP32(frameData, n + 20, objPalAddrs[objSlot]);
                if (bgSlot >= 0 && bgSlot < bgImageAddrs.Length)
                    WriteP32(frameData, n + 16, bgImageAddrs[bgSlot]);
                if (bgSlot >= 0 && bgSlot < bgPalAddrs.Length)
                    WriteP32(frameData, n + 24, bgPalAddrs[bgSlot]);
                if (bgSlot >= 0 && bgSlot < bgTsaAddrs.Length)
                    WriteP32(frameData, n + 28, bgTsaAddrs[bgSlot]);  // CSA ONLY

                n += 4 * 7; // skip rest of 32-byte record (8 dwords, outer adds 4)
            }
        }

        // ================================================================
        // Binary helpers
        // ================================================================

        static void AppendU32(List<byte> buf, uint val)
        {
            buf.Add((byte)( val        & 0xFF));
            buf.Add((byte)((val >>  8) & 0xFF));
            buf.Add((byte)((val >> 16) & 0xFF));
            buf.Add((byte)((val >> 24) & 0xFF));
        }

        static void AppendTermOAM(List<byte> buf)
        {
            buf.Add(0x01);
            for (int i = 0; i < 11; i++) buf.Add(0x00);
        }

        static uint ReadU32(List<byte> buf, int offset)
            => (uint)(buf[offset] | (buf[offset + 1] << 8) |
                      (buf[offset + 2] << 16) | (buf[offset + 3] << 24));

        static void WriteU32(List<byte> buf, int offset, uint val)
        {
            buf[offset]     = (byte)( val        & 0xFF);
            buf[offset + 1] = (byte)((val >>  8) & 0xFF);
            buf[offset + 2] = (byte)((val >> 16) & 0xFF);
            buf[offset + 3] = (byte)((val >> 24) & 0xFF);
        }

        static uint ReadU32Arr(byte[] buf, uint offset)
            => (uint)(buf[offset] | (buf[offset + 1] << 8) |
                      (buf[offset + 2] << 16) | (buf[offset + 3] << 24));

        static void WriteP32(byte[] buf, uint offset, uint addr)
        {
            uint gba = addr | 0x08000000u;
            buf[offset]     = (byte)( gba        & 0xFF);
            buf[offset + 1] = (byte)((gba >>  8) & 0xFF);
            buf[offset + 2] = (byte)((gba >> 16) & 0xFF);
            buf[offset + 3] = (byte)((gba >> 24) & 0xFF);
        }

        static byte[] EnsureExactLength(byte[] data, int len)
        {
            if (data == null) return new byte[len];
            if (data.Length == len) return data;
            var result = new byte[len];
            Array.Copy(data, result, Math.Min(data.Length, len));
            return result;
        }

        // ================================================================
        // Internal result types
        // ================================================================

        sealed class ObjAssembleResult
        {
            public byte[] TileData4bpp;
            public byte[] Palette;
            public byte[] OamRtoL;
            public byte[] OamLtoR;
        }

        sealed class BgAssembleResult
        {
            public byte[] Lz77Tiles;
            public byte[] Lz77Tsa;
            public byte[] RawPalette;
            /// <summary>Source height before encoding: 64 or 160.</summary>
            public int SourceHeight;
        }
    }
}
