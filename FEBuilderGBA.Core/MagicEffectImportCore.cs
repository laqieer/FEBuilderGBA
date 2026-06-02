// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform Import helper for the FEditor magic-animation (.txt script +
// per-frame PNG byte arrays). Implements #881 (Import ROM-mutation counterpart to
// #880 Export).
//
// Design:
//   - ParseMagicScript: stateless text parser, no ROM access.
//   - ImportMagicScript: ROM-mutating, validate-before-mutate, ambient-undo, no file I/O.
//
// The Avalonia view (ImageMagicFEditorView) calls these two methods and handles
// file I/O + undo scope externally. The injectable imageProvider delegate keeps
// all file access outside Core so headless tests can drive the full pipeline.
//
// Frame layout (mirrors MagicEffectExportCore header + WF ImportBGImageToData):
//   [+0]  frame header u32: 0x86_BGIDX_WAIT (byte[3]=0x86, byte[2]=bgIdx, [1:0]=wait)
//   [+4]  objImagePointer   (GBA pointer, LZ77-compressed 4bpp tiles)
//   [+8]  OAMAbsoStart      (raw u32: relative offset into obj RightToLeft OAM array)
//   [+12] OAMBGAbsoStart    (raw u32: relative offset into bg RightToLeft OAM array)
//   [+16] bgImagePointer    (GBA pointer, LZ77-compressed 4bpp tiles)
//   [+20] objPalettePointer (GBA pointer, RAW 0x20 bytes)
//   [+24] bgPalettePointer  (GBA pointer, RAW 0x20 bytes)
//
// Write sequence (mirrors WF ImageUtilMagicFEditor.Import ~L1133-1178):
//   1. Parse script → command list.
//   2. For each O/B frame, call imageProvider → AssembleOAM (isMagic=true).
//   3. Build intermediate frame-data array (placeholder indices for pointers).
//   4. Gather old recycle pool from existing magic-base slot.
//   5. Allocate + write: OBJ OAM (R→L + L→R), BG OAM (R→L + L→R), per-image data.
//   6. Patch real addresses into frame-data, write frame table.
//   7. BlackOut leftover old space.
//
// Parity gaps vs WF (documented, intentional):
//   - SubConfilctArea: WF removes shared-image regions from the recycle pool.
//     Core skips this because it requires InputFormRef to enumerate all CSA entries.
//     Impact: minor — may write slightly into free space instead of recycling shared
//     frames. Correctness is unaffected.
//   - No progress callbacks: WF calls InputFormRef.DoEvents every parse step.
//     Not needed for Core headless operation.

using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    // -----------------------------------------------------------------------
    // Command token types
    // -----------------------------------------------------------------------

    /// <summary>
    /// Kind of command parsed from an FEditor magic-animation .txt script.
    /// </summary>
    public enum MagicImportCmdKind
    {
        /// <summary>O p- filename.png — OBJ image frame.</summary>
        ObjImage,
        /// <summary>B p- filename.png — BG image frame.</summary>
        BgImage,
        /// <summary>Decimal wait duration (e.g. "4").</summary>
        Wait,
        /// <summary>C&lt;hex24&gt; or S&lt;hex16&gt; → 0x85 command dword.</summary>
        Command85,
        /// <summary>~~~ — miss terminator or final terminator.</summary>
        Terminator,
    }

    /// <summary>
    /// One parsed command from a magic-animation .txt script.
    /// </summary>
    public sealed class MagicFrameCommand
    {
        public MagicImportCmdKind Kind;

        /// <summary>
        /// For ObjImage / BgImage: the filename token (may be relative or absolute).
        /// </summary>
        public string Filename;

        /// <summary>
        /// For Wait: the decoded wait value.
        /// </summary>
        public uint WaitValue;

        /// <summary>
        /// For Command85: the assembled 0x85_xxxxxx dword.
        /// </summary>
        public uint Command85Dword;
    }

    /// <summary>
    /// Cross-platform, ROM-mutating Import helper for FEditor magic-animation scripts.
    ///
    /// <para><b>Entry points:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="ParseMagicScript"/> — stateless text parser, no ROM access.</item>
    ///   <item><see cref="ImportMagicScript"/> — ROM-mutating write with ambient-undo.</item>
    /// </list>
    ///
    /// <para>All I/O (reading .txt + loading PNGs) is performed by the caller; this
    /// class operates only on byte arrays and <see cref="ROM"/>.</para>
    /// </summary>
    public static class MagicEffectImportCore
    {
        // OBJ image dimensions accepted as input (must be >= these values)
        public const int OBJ_MIN_WIDTH  = 480;
        public const int OBJ_MIN_HEIGHT = 160;
        // BG image dimensions (required)
        public const int BG_MIN_WIDTH  = 240;
        public const int BG_MIN_HEIGHT =  64;

        // Seat dimensions used in AssembleOAM for magic mode
        public const int OBJ_SEAT_WIDTH  = 256;
        public const int OBJ_SEAT_HEIGHT =  32;  // magic OAM uses 32-px height
        public const int BG_SEAT_WIDTH   = 256;
        public const int BG_SEAT_HEIGHT  =  64;

        // ================================================================
        // ParseMagicScript
        // ================================================================

        /// <summary>
        /// Parse FEditor magic-animation script lines into a command list.
        ///
        /// <para>Tokens recognized (mirrors <see cref="MagicEffectExportCore"/> output exactly):</para>
        /// <list type="bullet">
        ///   <item><c>O p- &lt;filename&gt;</c> → ObjImage</item>
        ///   <item><c>B p- &lt;filename&gt;</c> → BgImage</item>
        ///   <item><c>&lt;decimal&gt;</c>        → Wait</item>
        ///   <item><c>C&lt;hex&gt;</c>           → Command85 (0x85 command dword)</item>
        ///   <item><c>S&lt;hex&gt;</c>           → Command85 (sound: maps to 0x85_xx_NN_48)</item>
        ///   <item><c>~~~</c>                    → Terminator (miss or final)</item>
        /// </list>
        ///
        /// <para>Comment stripping: anything from <c>#</c> or <c>@</c> onwards is discarded,
        /// as in WF <c>U.ClipCommentWithCharpAndAtmark</c>.</para>
        ///
        /// <para>Empty lines and unrecognized tokens are silently skipped (mirrors WF).</para>
        ///
        /// <para>Does NOT access the ROM or file system.</para>
        /// </summary>
        /// <param name="lines">Lines from the .txt script file.</param>
        /// <returns>Ordered list of <see cref="MagicFrameCommand"/> tokens.</returns>
        public static List<MagicFrameCommand> ParseMagicScript(IEnumerable<string> lines)
        {
            var cmds = new List<MagicFrameCommand>();
            if (lines == null) return cmds;

            foreach (string rawLine in lines)
            {
                // Strip comments (#...) and alternative-language markers (@...).
                string line = ClipComment(rawLine);
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Skip header/end markers.
                if (line.StartsWith("///", StringComparison.Ordinal)) continue;

                // ~~~ terminator
                if (line.StartsWith("~~~", StringComparison.Ordinal))
                {
                    cmds.Add(new MagicFrameCommand { Kind = MagicImportCmdKind.Terminator });
                    continue;
                }

                // O p- <filename>
                if (line.Length >= 2 && line[0] == 'O' && line[1] != 'B')
                {
                    string fn = ParsePFilename(line);
                    if (!string.IsNullOrEmpty(fn))
                        cmds.Add(new MagicFrameCommand { Kind = MagicImportCmdKind.ObjImage, Filename = fn });
                    continue;
                }

                // B p- <filename>
                if (line.Length >= 2 && line[0] == 'B')
                {
                    string fn = ParsePFilename(line);
                    if (!string.IsNullOrEmpty(fn))
                        cmds.Add(new MagicFrameCommand { Kind = MagicImportCmdKind.BgImage, Filename = fn });
                    continue;
                }

                // S<hex> — sound command → 0x85 dword with sound flag
                if (line[0] == 'S' && line.Length >= 2)
                {
                    uint musicId = ParseHex(line.Substring(1));
                    // Mirror WF: uint a = ((music & 0xFFFF) << 8) | 0x85000048;
                    uint dword = ((musicId & 0xFFFF) << 8) | 0x85000048u;
                    cmds.Add(new MagicFrameCommand
                    {
                        Kind           = MagicImportCmdKind.Command85,
                        Command85Dword = dword,
                    });
                    continue;
                }

                // C<hex> — 0x85 command
                if (line[0] == 'C' && line.Length >= 2)
                {
                    uint id24 = ParseHex(line.Substring(1));
                    // Mirror WF: uint a = (command & 0x00FFFFFF) | 0x85000000;
                    uint dword = (id24 & 0x00FFFFFFu) | 0x85000000u;
                    cmds.Add(new MagicFrameCommand
                    {
                        Kind           = MagicImportCmdKind.Command85,
                        Command85Dword = dword,
                    });
                    continue;
                }

                // Decimal wait value
                if (char.IsDigit(line[0]))
                {
                    if (uint.TryParse(line.Trim(), out uint wait))
                        cmds.Add(new MagicFrameCommand { Kind = MagicImportCmdKind.Wait, WaitValue = wait });
                    continue;
                }

                // Unknown token — silently skip (mirrors WF).
            }

            return cmds;
        }

        // ================================================================
        // ImportMagicScript
        // ================================================================

        /// <summary>
        /// ROM-mutating import: write a complete magic-animation dataset into the
        /// slot at <paramref name="magicBaseAddr"/>.
        ///
        /// <para><b>Validate-before-mutate contract:</b> all validation (script parsing,
        /// image loading, OAM assembly) is performed BEFORE any ROM write. On any
        /// validation failure the method returns a non-empty error string and leaves
        /// the ROM UNTOUCHED.</para>
        ///
        /// <para><b>Undo:</b> the caller MUST open an ambient undo scope with
        /// <c>CoreState.ROM.BeginUndoScope()</c> before calling this method and
        /// commit/rollback that scope based on the return value. Every write inside
        /// this method is routed through no-undo overloads so the scope captures each
        /// write exactly once.</para>
        ///
        /// <para><b>FE-gate:</b> if no magic system is detected, returns an error
        /// immediately.</para>
        ///
        /// <para><b>Parity gap vs WF:</b> <c>SubConfilctArea</c> (shared-image
        /// de-duplication from the recycle pool) is intentionally omitted because it
        /// requires <c>InputFormRef</c> to enumerate CSA entries. The gap is
        /// documented; correctness is unaffected (writes may land in free space rather
        /// than recycled shared frames).</para>
        /// </summary>
        /// <param name="rom">The ROM to write into.</param>
        /// <param name="magicBaseAddr">
        ///   The 20-byte CSA-entry address (ROM offset, NOT a GBA pointer).
        ///   Slots: +0 frameData ptr, +4 objRtoL OAM ptr, +8 objLtoR OAM ptr,
        ///   +12 bgRtoL OAM ptr, +16 bgLtoR OAM ptr.
        /// </param>
        /// <param name="cmds">
        ///   Parsed script commands from <see cref="ParseMagicScript"/>.
        /// </param>
        /// <param name="imageProvider">
        ///   Called for each unique image filename encountered. Receives the filename
        ///   as-is from the script (may be relative — resolve against script directory
        ///   in the caller). Returns (indexedPixels, width, height, gbaPalette) on
        ///   success, or null on failure.
        /// </param>
        /// <returns>
        ///   Empty string on success. Non-empty error description if validation
        ///   fails or the ROM cannot accommodate the data.
        /// </returns>
        public static string ImportMagicScript(
            ROM rom,
            uint magicBaseAddr,
            List<MagicFrameCommand> cmds,
            Func<string, (byte[] indexedPixels, int w, int h, byte[] gbaPalette)?> imageProvider)
        {
            if (rom == null)   return "ROM is null";
            if (cmds == null)  return "Command list is null";
            if (imageProvider == null) return "imageProvider is null";

            // FIX 2: CoreState.ROM consistency guard.
            // All ambient writes (write_p32, RecycleAddress.WriteAmbient, BlackOutAmbient)
            // target CoreState.ROM internally.  If the caller passed a different ROM
            // instance the writes would go to the WRONG ROM and the Data.Length range
            // checks inside RecycleAddress would be applied to the wrong buffer.
            // Pattern mirrors other Core mutators that reject rom != CoreState.ROM before any write.
            if (CoreState.ROM == null)
                return "CoreState.ROM is null";
            if (!ReferenceEquals(rom, CoreState.ROM))
                return "rom argument must be CoreState.ROM (ambient-undo writes always target CoreState.ROM)";

            // FE-gate: magic system must be present.
            var ms = ImageUtilMagicCore.SearchMagicSystem(rom, out _, out _, out _);
            if (ms == ImageUtilMagicCore.MagicSystem.No)
                return "FEditor / SCA_Creator magic-system patch not detected.";

            // magicBaseAddr safety check.
            if (magicBaseAddr < 0x200u || magicBaseAddr + 20u > (uint)rom.Data.Length)
                return $"magicBaseAddr 0x{magicBaseAddr:X8} is out of ROM range";

            // ---- PHASE 1: Validate script structure + load all images ----

            // We need at least one O/B/wait triplet.
            // First pass: collect unique filenames so we can fail fast before any writes.

            // Image cache key → (indexedPixels, w, h, gbaPalette) — deduplicated by filename.
            var imageCache = new Dictionary<string, (byte[] idx, int w, int h, byte[] pal)>(StringComparer.Ordinal);

            string validateErr = ValidateScript(cmds, imageProvider, imageCache);
            if (!string.IsNullOrEmpty(validateErr))
                return validateErr;

            // ---- PHASE 2: Assemble all OAM data (OBJ images, BG images) ----

            // OBJ: unique filename → assembled result
            var objAssembled = new Dictionary<string, OAMAssembleResult2>(StringComparer.Ordinal);
            // BG: unique filename → (lz77tiles, palette32)
            var bgAssembled = new Dictionary<string, (byte[] lz77Tiles, byte[] palette)>(StringComparer.Ordinal);

            string assembleErr = AssembleAllImages(cmds, imageCache, objAssembled, bgAssembled);
            if (!string.IsNullOrEmpty(assembleErr))
                return assembleErr;

            // ---- PHASE 3: Build intermediate frame-data list ----
            // Pointers in frames use placeholder indices at this stage (patched in Phase 5).

            var frameBytes = new List<byte>();

            // FIX 1: WF C00 header (L917-944 of ImageUtilMagicFEditor.cs).
            // WF appends EVERY leading C00 dword encountered in the script, THEN pads
            // to reach 5 total.  The old code only padded the missing count (emitting
            // zero dwords when the script already had >= 5 C00s).
            // Mirror WF exactly: walk and emit each leading C<hex==0> entry, then pad.
            int c00Count = 0;
            foreach (var cmd in cmds)
            {
                if (cmd.Kind != MagicImportCmdKind.Command85) break;
                if (cmd.Command85Dword == 0x85000000u)
                {
                    AppendU32(frameBytes, 0x85000000u);  // emit the C00 we encountered
                    c00Count++;
                }
                else break;
            }
            // Pad up to 5 total (mirrors WF L940-943).
            for (int i = c00Count; i < 5; i++)
                AppendU32(frameBytes, 0x85000000u);

            // Ordered lists of (placeholderIndex, isObj) for frames so Phase 5 can patch.
            var frameRecordStarts = new List<int>(); // byte offsets in frameBytes of each 0x86 record

            // Per-image allocation slots (built during frame generation).
            // OBJ slot list: ordered unique-filenames for OBJ images.
            var objSlotFilenames = new List<string>();
            var objFilenameToSlot = new Dictionary<string, int>(StringComparer.Ordinal);
            // BG slot list: ordered unique-filenames for BG images.
            var bgSlotFilenames = new List<string>();
            var bgFilenameToSlot = new Dictionary<string, int>(StringComparer.Ordinal);

            int bgNumber = 0; // image_bg_number — used in frame header byte[2]

            int cmdIdx = 0;
            while (cmdIdx < cmds.Count)
            {
                var cmd = cmds[cmdIdx];

                if (cmd.Kind == MagicImportCmdKind.Terminator)
                {
                    // ~~~ miss terminator → 0x00010080
                    AppendU32(frameBytes, 0x80000100u);
                    cmdIdx++;
                    continue;
                }

                if (cmd.Kind == MagicImportCmdKind.Command85)
                {
                    // C00 at head already handled above; append rest.
                    if (cmdIdx >= c00Count)
                        AppendU32(frameBytes, cmd.Command85Dword);
                    // Special case: if this is 0x0D command, WF appends an extra 0x80000000.
                    if ((cmd.Command85Dword & 0x00FFFFFFu) == 0x0Du)
                        AppendU32(frameBytes, 0x80000000u);
                    cmdIdx++;
                    continue;
                }

                if (cmd.Kind != MagicImportCmdKind.ObjImage)
                {
                    cmdIdx++;
                    continue;
                }

                // Collect O / B / wait triplet.
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
                        break; // Unexpected; skip
                    }
                }

                // FIX 4: WF (L1099-1110) REQUIRES an explicit O + B + time triple.
                // The old code silently fell back to bgFn ?? objFn which could crash with
                // KeyNotFoundException (bgFn not in bgAssembled) and contradicts the WF error.
                // Validate-before-mutate: return error if any part of the triple is missing.
                if (bgFn == null)
                    return "BG image (B p- ...) is missing for frame after O: " + objFn;
                if (n < 2 || waitVal == 0)
                    return "Wait time is missing for frame (need O + B + time triple): O: " + objFn;

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

                // 0x86 record (28 bytes):
                // [+0]  0x86_BGNUM_WAIT
                // [+4]  objImageSlot    (placeholder — patched in Phase 5)
                // [+8]  objOamStart     (placeholder — computed in Phase 4)
                // [+12] bgOamStart      (placeholder — computed in Phase 4)
                // [+16] bgImageSlot     (placeholder — patched in Phase 5)
                // [+20] objPalSlot      (placeholder — patched in Phase 5)
                // [+24] bgPalSlot       (placeholder — patched in Phase 5)

                int recordStart = frameBytes.Count;
                frameRecordStarts.Add(recordStart);

                uint header = (waitVal & 0xFFFF) | ((uint)(bgNumber & 0xFF) << 16) | 0x86000000u;
                AppendU32(frameBytes, header);
                AppendU32(frameBytes, (uint)objSlot);       // +4  obj image slot
                AppendU32(frameBytes, 0u);                  // +8  obj OAM start (Phase 4)
                AppendU32(frameBytes, 0u);                  // +12 bg OAM start  (Phase 4)
                AppendU32(frameBytes, (uint)bgSlot);        // +16 bg image slot
                AppendU32(frameBytes, (uint)objSlot);       // +20 obj pal slot
                AppendU32(frameBytes, (uint)bgSlot);        // +24 bg pal slot

                bgNumber++;
            }

            // Final terminator.
            AppendU32(frameBytes, 0x80000000u);

            // ---- PHASE 4: Compute OAM sizes + layout ----
            // OBJ OAM: concatenate all assembled OAM records (RtoL), compute per-slot start offsets.
            var objOamRtoLAllBytes = new List<byte>();
            // Per objSlot: start offset into combined OAM byte array.
            var objOamStarts = new uint[objSlotFilenames.Count];
            for (int i = 0; i < objSlotFilenames.Count; i++)
            {
                objOamStarts[i] = (uint)objOamRtoLAllBytes.Count;
                var res = objAssembled[objSlotFilenames[i]];
                objOamRtoLAllBytes.AddRange(res.OamRtoL);
            }
            // Terminator for combined OAM.
            AppendTermOAM(objOamRtoLAllBytes);

            // BG OAM: simple dummy terminator-only OAM per slot (WF: MakeDummyOAM was commented out,
            // WriteAndWritePointer writes oam.GetRightToLeftOAMBG() which in the FEditor case is
            // a dummy OAM per BG image count). We produce a minimal 1-entry dummy.
            var bgOamRtoLAllBytes = new List<byte>();
            var bgOamStarts = new uint[bgSlotFilenames.Count];
            for (int i = 0; i < bgSlotFilenames.Count; i++)
            {
                bgOamStarts[i] = (uint)bgOamRtoLAllBytes.Count;
                // One terminator entry per BG slot (matches WF MakeDummyOAM structure).
                AppendTermOAM(bgOamRtoLAllBytes);
            }
            // Combined terminator.
            AppendTermOAM(bgOamRtoLAllBytes);

            // LtoR OAM: flip all RtoL OAM records.
            byte[] objOamRtoLData = objOamRtoLAllBytes.ToArray();
            byte[] objOamLtoRData = BattleAnimeOAMImportCore.ConvertToLeftToRightOAM(objOamRtoLData);
            byte[] bgOamRtoLData  = bgOamRtoLAllBytes.ToArray();
            byte[] bgOamLtoRData  = BattleAnimeOAMImportCore.ConvertToLeftToRightOAM(bgOamRtoLData);

            // Patch OAM start offsets into frame records.
            foreach (int recStart in frameRecordStarts)
            {
                uint objSlot = ReadU32(frameBytes, recStart + 4);
                uint bgSlot  = ReadU32(frameBytes, recStart + 16);
                uint objOamStart = objSlot < (uint)objOamStarts.Length ? objOamStarts[objSlot] : 0u;
                uint bgOamStart  = bgSlot  < (uint)bgOamStarts.Length  ? bgOamStarts[bgSlot]   : 0u;
                WriteU32(frameBytes, recStart + 8,  objOamStart);
                WriteU32(frameBytes, recStart + 12, bgOamStart);
            }

            // ---- PHASE 5: ROM writes (ambient-undo) ----
            // Gather recycle pool from existing slot data.
            var recycle = new List<Address>();
            RecycleOldMagicSlot(rom, magicBaseAddr, recycle);
            var ra = new RecycleAddress(recycle);

            // Write OAM arrays.
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

            // Write OBJ images (LZ77 tiles + raw palettes) — ordered by slot.
            var objImageAddrs  = new uint[objSlotFilenames.Count];
            var objPalAddrs    = new uint[objSlotFilenames.Count];
            for (int i = 0; i < objSlotFilenames.Count; i++)
            {
                var res = objAssembled[objSlotFilenames[i]];
                byte[] lz77Tiles = LZ77.compress(res.TileData4bpp);
                objImageAddrs[i] = ra.WriteAmbient(lz77Tiles);
                if (objImageAddrs[i] == U.NOT_FOUND) return $"Cannot allocate space for OBJ image slot {i}";
                // Palette: exact 0x20 bytes
                byte[] pal = EnsureExactLength(res.Palette, 0x20);
                objPalAddrs[i] = ra.WriteAmbient(pal);
                if (objPalAddrs[i] == U.NOT_FOUND) return $"Cannot allocate space for OBJ palette slot {i}";
            }

            // Write BG images (LZ77 tiles + raw palettes) — ordered by slot.
            var bgImageAddrs = new uint[bgSlotFilenames.Count];
            var bgPalAddrs   = new uint[bgSlotFilenames.Count];
            for (int i = 0; i < bgSlotFilenames.Count; i++)
            {
                var (lz77, pal) = bgAssembled[bgSlotFilenames[i]];
                bgImageAddrs[i] = ra.WriteAmbient(lz77);
                if (bgImageAddrs[i] == U.NOT_FOUND) return $"Cannot allocate space for BG image slot {i}";
                byte[] palExact = EnsureExactLength(pal, 0x20);
                bgPalAddrs[i] = ra.WriteAmbient(palExact);
                if (bgPalAddrs[i] == U.NOT_FOUND) return $"Cannot allocate space for BG palette slot {i}";
            }

            // Patch real addresses into frame-data (replace placeholder slot indices).
            byte[] frameData = frameBytes.ToArray();
            PatchFrameDataAddresses(
                frameData,
                objImageAddrs, objPalAddrs,
                bgImageAddrs,  bgPalAddrs);

            // Write frame-data array.
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
        /// Validate script structure and pre-load all images via imageProvider.
        /// Returns non-empty error if anything is wrong; does NOT mutate ROM.
        /// </summary>
        static string ValidateScript(
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
                    if (w < OBJ_MIN_WIDTH || h < OBJ_MIN_HEIGHT)
                        return $"OBJ image too small: {w}x{h} (need >= {OBJ_MIN_WIDTH}x{OBJ_MIN_HEIGHT}) for: {objFn}";
                    if (w % 8 != 0 || h % 8 != 0)
                        return $"OBJ image dimensions not multiples of 8: {w}x{h} for: {objFn}";
                    imageCache["OBJ:" + objFn] = (idx, w, h, pal);
                }

                // FIX 4 (validate phase): require explicit O + B + wait triple.
                // Scan ahead for the B and wait lines (up to next O or Terminator).
                string bgFn = null;
                bool hasWait = false;
                int scanIdx = cmdIdx;
                while (scanIdx < cmds.Count)
                {
                    var s = cmds[scanIdx];
                    if (s.Kind == MagicImportCmdKind.ObjImage) break;    // next O — stop
                    if (s.Kind == MagicImportCmdKind.Terminator) break;  // miss-term — stop
                    if (s.Kind == MagicImportCmdKind.BgImage && bgFn == null)
                        bgFn = s.Filename;
                    if (s.Kind == MagicImportCmdKind.Wait)
                        hasWait = true;
                    scanIdx++;
                    if (bgFn != null && hasWait) break; // got both
                }

                if (bgFn == null)
                    return $"BG image (B p- ...) is missing for frame after O: {objFn}";
                if (!hasWait)
                    return $"Wait time is missing for frame (need O + B + time triple): O: {objFn}";

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
                    if (w < BG_MIN_WIDTH || h < BG_MIN_HEIGHT)
                        return $"BG image too small: {w}x{h} (need >= {BG_MIN_WIDTH}x{BG_MIN_HEIGHT}) for: {bgFn}";
                    imageCache["BG:" + bgFn] = (idx, w, h, pal);
                }

                hasFrame = true;
            }

            if (!hasFrame)
                return "Script contains no image frames (O / B / wait triplets).";

            return string.Empty;
        }

        /// <summary>
        /// Run AssembleOAM for each unique OBJ image; for each BG image, encode tiles + compress.
        /// All data is pre-assembled before any ROM write.
        /// </summary>
        static string AssembleAllImages(
            List<MagicFrameCommand> cmds,
            Dictionary<string, (byte[] idx, int w, int h, byte[] pal)> imageCache,
            Dictionary<string, OAMAssembleResult2> objAssembled,
            Dictionary<string, (byte[] lz77Tiles, byte[] palette)> bgAssembled)
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

                // Assemble OBJ OAM (isMagic=true, single palette bank).
                if (!objAssembled.ContainsKey(objFn))
                {
                    var (idx, w, h, pal) = imageCache["OBJ:" + objFn];
                    var oamResult = BattleAnimeOAMImportCore.AssembleOAM(
                        idx, w, h, pal,
                        isMagic: true,
                        isMultiPalette: false);
                    if (!oamResult.Success)
                        return $"OAM assembly failed for {objFn}: {oamResult.Error}";

                    // Build LtoR OAM variant.
                    byte[] rtoL = oamResult.OamRecords;
                    byte[] ltoR = BattleAnimeOAMImportCore.ConvertToLeftToRightOAM(rtoL);

                    objAssembled[objFn] = new OAMAssembleResult2
                    {
                        TileData4bpp = oamResult.TileData4bpp,
                        Palette      = EnsureExactLength(oamResult.PaletteBytes, 0x20),
                        OamRtoL      = rtoL,
                        OamLtoR      = ltoR,
                    };
                }

                // Find and process BG image.
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
                        // BG: encode 4bpp tiles, LZ77 compress.
                        byte[] tiles4bpp  = EncodeBg4bpp(idx, w, h);
                        byte[] lz77Tiles  = LZ77.compress(tiles4bpp);
                        byte[] palExact   = EnsureExactLength(pal, 0x20);
                        bgAssembled[bgFn] = (lz77Tiles, palExact);
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Encode indexed-pixel BG image (row-major, 1 byte/pixel) into 4bpp GBA tile format.
        /// Mirrors WF <c>ImageUtil.ImageToByte16Tile</c> called with the cropped bitmap.
        ///
        /// <para><b>FIX 3:</b> WF <c>ImportBGImageToData</c> (L1269-1292) copies ONLY the
        /// left <c>BG_SEAT_TILE_WIDTH*8 = 256</c> pixels wide and <c>BG_SEAT_TILE_HEIGHT*8 = 64</c>
        /// pixels tall into a clean bitmap before encoding.  A 264-wide Export PNG has an
        /// 8-pixel palette-mark column on the right; the old Core code encoded all 264 columns,
        /// producing 33 tile columns (extra tiles + shifted BG indices).  We crop to exactly
        /// 256×64 here, discarding anything beyond column 255.
        /// Row stride in <paramref name="indexed"/> is still <paramref name="w"/> (may be 264+),
        /// but we iterate only over the leftmost 256 columns and topmost 64 rows.</para>
        /// </summary>
        static byte[] EncodeBg4bpp(byte[] indexed, int w, int h)
        {
            // FIX 3: always encode exactly BG_SEAT_WIDTH × BG_SEAT_HEIGHT pixels (256×64).
            // This drops the 8-px palette-mark column that the Export PNG appends (264 wide).
            int encW = Math.Min(w, BG_SEAT_WIDTH);   // crop to 256 columns
            int encH = Math.Min(h, BG_SEAT_HEIGHT);  // crop to 64 rows
            int tileW = encW / 8;
            int tileH = encH / 8;
            int totalTiles = tileW * tileH;
            byte[] data = new byte[totalTiles * 32];
            int outIdx = 0;

            for (int ty = 0; ty < tileH; ty++)
            for (int tx = 0; tx < tileW; tx++)
            {
                int baseX = tx * 8;
                int baseY = ty * 8;
                for (int py = 0; py < 8; py++)
                {
                    int rowBase = (baseY + py) * w + baseX;  // stride = w (original width)
                    for (int px = 0; px < 8; px += 2)
                    {
                        byte lo = (byte)(indexed[rowBase + px]     & 0x0F);
                        byte hi = (byte)(indexed[rowBase + px + 1] & 0x0F);
                        data[outIdx++] = (byte)(lo | (hi << 4));
                    }
                }
            }
            return data;
        }

        /// <summary>
        /// Collect the existing magic-slot data regions into a recycle pool.
        /// Mirrors WF <c>ImageUtilMagicFEditor.RecycleOldAnime</c> adapted for Core.
        /// </summary>
        static void RecycleOldMagicSlot(ROM rom, uint magicBaseAddr, List<Address> recycle)
        {
            if (rom == null || rom.Data == null) return;
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
                if (i + 28 > dataLen) break;

                // OBJ image (LZ77)
                uint objImgPtrSlot = i + 4;
                if (objImgPtrSlot + 4 <= dataLen)
                    Address.AddLZ77Pointer(recycle, objImgPtrSlot, "OLD_OBJ", false, Address.DataTypeEnum.LZ77IMG);

                // BG image (LZ77)
                uint bgImgPtrSlot = i + 16;
                if (bgImgPtrSlot + 4 <= dataLen)
                    Address.AddLZ77Pointer(recycle, bgImgPtrSlot, "OLD_BG", false, Address.DataTypeEnum.LZ77IMG);

                // OBJ palette (0x20 raw bytes)
                uint objPalPtrSlot = i + 20;
                if (objPalPtrSlot + 4 <= dataLen)
                    Address.AddPointer(recycle, objPalPtrSlot, 0x20u, "OLD_OBJPAL", Address.DataTypeEnum.PAL);

                // BG palette (0x20 raw bytes)
                uint bgPalPtrSlot = i + 24;
                if (bgPalPtrSlot + 4 <= dataLen)
                    Address.AddPointer(recycle, bgPalPtrSlot, 0x20u, "OLD_BGPAL", Address.DataTypeEnum.PAL);

                uint objOAM = U.u32(d, i + 8);
                uint bgOAM  = U.u32(d, i + 12);
                if (objOAM > maxObjOAM) maxObjOAM = objOAM;
                if (bgOAM  > maxBGOAM)  maxBGOAM  = bgOAM;

                i += 24; // +4 from loop = 28 total
            }

            if (i <= limiter)
            {
                // Frame data block.
                uint frameLen = i - frameDataOff;
                Address.AddPointer(recycle, magicBaseAddr + 0, frameLen,
                    "OLD_FRAME", Address.DataTypeEnum.MAGICFRAME_FEITORADV);

                // OAM blocks — use calcOAMLength equivalent.
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

        /// <summary>
        /// Compute the byte length of an OAM block starting at <paramref name="oamPtr"/>
        /// (a raw u32 from the magic entry — NOT a GBA pointer; it IS the OAM base offset).
        /// Mirrors WF <c>ImageUtilMagicFEditor.calcOAMLength</c>.
        /// </summary>
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
                if (a == 0x01) break; // terminator
            }
            return oam - oamOff;
        }

        /// <summary>
        /// Patch real ROM addresses into the frame-data array (replaces slot-index placeholders).
        /// Mirrors WF <c>updateFrameDataAddress</c>.
        /// </summary>
        static void PatchFrameDataAddresses(
            byte[] frameData,
            uint[] objImageAddrs, uint[] objPalAddrs,
            uint[] bgImageAddrs,  uint[] bgPalAddrs)
        {
            uint length = (uint)frameData.Length;
            for (uint n = 0; n + 28 <= length; n += 4)
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

                n += 4 * 6; // skip rest of 28-byte record (outer loop adds 4)
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
        {
            return (uint)(buf[offset] | (buf[offset + 1] << 8) |
                          (buf[offset + 2] << 16) | (buf[offset + 3] << 24));
        }

        static void WriteU32(List<byte> buf, int offset, uint val)
        {
            buf[offset]     = (byte)( val        & 0xFF);
            buf[offset + 1] = (byte)((val >>  8) & 0xFF);
            buf[offset + 2] = (byte)((val >> 16) & 0xFF);
            buf[offset + 3] = (byte)((val >> 24) & 0xFF);
        }

        static uint ReadU32Arr(byte[] buf, uint offset)
        {
            return (uint)(buf[offset] | (buf[offset + 1] << 8) |
                          (buf[offset + 2] << 16) | (buf[offset + 3] << 24));
        }

        /// <summary>Write a ROM GBA pointer (addr | 0x08000000) into <paramref name="buf"/>.</summary>
        static void WriteP32(byte[] buf, uint offset, uint addr)
        {
            uint gba = addr | 0x08000000u;
            buf[offset]     = (byte)( gba        & 0xFF);
            buf[offset + 1] = (byte)((gba >>  8) & 0xFF);
            buf[offset + 2] = (byte)((gba >> 16) & 0xFF);
            buf[offset + 3] = (byte)((gba >> 24) & 0xFF);
        }

        /// <summary>Ensure <paramref name="data"/> is exactly <paramref name="len"/> bytes.</summary>
        static byte[] EnsureExactLength(byte[] data, int len)
        {
            if (data == null) return new byte[len];
            if (data.Length == len) return data;
            var result = new byte[len];
            Array.Copy(data, result, Math.Min(data.Length, len));
            return result;
        }

        // ================================================================
        // Script text helpers
        // ================================================================

        /// <summary>
        /// Strip comments (# and @ onwards) from a script line.
        /// Mirrors WF <c>U.ClipCommentWithCharpAndAtmark</c>.
        /// </summary>
        static string ClipComment(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            int sharp = line.IndexOf('#');
            int at    = line.IndexOf('@');
            int cut   = -1;
            if (sharp >= 0) cut = sharp;
            if (at >= 0 && (cut < 0 || at < cut)) cut = at;
            string result = cut >= 0 ? line.Substring(0, cut) : line;
            return result.Trim();
        }

        /// <summary>
        /// Extract the filename from an "O p- filename" or "B p- filename" line.
        /// Mirrors WF <c>ImageUtilOAM.parsePFilename</c>.
        /// Returns the filename (may include path), or empty string on parse failure.
        /// </summary>
        static string ParsePFilename(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            // Format: "O  p- filename.png" or "B  p- filename.png"
            int dashIdx = line.IndexOf("p-", StringComparison.Ordinal);
            if (dashIdx < 0) return string.Empty;
            string rest = line.Substring(dashIdx + 2).Trim();
            return rest;
        }

        /// <summary>
        /// Parse a hex string (without leading 0x) to uint.
        /// Returns 0 on parse failure.
        /// </summary>
        static uint ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            hex = hex.Trim();
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                null, out uint val))
                return val;
            return 0;
        }

        // ================================================================
        // Internal result type
        // ================================================================

        /// <summary>Assembled OAM result for one OBJ image slot.</summary>
        sealed class OAMAssembleResult2
        {
            public byte[] TileData4bpp;
            public byte[] Palette;     // trimmed / padded to 0x20 bytes
            public byte[] OamRtoL;     // right-to-left OAM records
            public byte[] OamLtoR;     // left-to-right OAM records (mirrored)
        }
    }
}
