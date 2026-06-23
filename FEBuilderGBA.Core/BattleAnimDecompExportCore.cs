// SPDX-License-Identifier: GPL-3.0-or-later
// Decomp battle animations: export a FEBuilder/FEditor-decoded battle animation
// as reviewable decomp SOURCE macro assembly for `data/banim/banim_<TAG>_motion.s`
// using the macros from fireemblem8u's banim headers (#1363).
//
// READ-ONLY: reads the preview ROM to produce a source artifact; NEVER mutates
// the ROM (the source-of-truth), never allocates ROM free space, never throws.
// This is an EXPORT / migration aid (a reviewable `.s` + PNG/palette/manifest
// sidecars), NOT a byte-pinned ROM round-trip, NOT a full FEditor re-assembler,
// and NOT automatic `banim_data[]` / `BattleAnimDef` / linker-script registration
// (those remain documented MANUAL steps — we emit a manifest hint, never a
// guessed table edit).
//
// MACRO MAPPING — verified VERBATIM against FireEmblemUniverse/fireemblem8u:
//   include/banim_code.inc:
//     banim_code_frame duration, sheet_addr, frame_number, oam_offset
//         -> .word 0x86000000 + (frame_number << 16) + duration, sheet_addr, oam_offset
//     banim_code_end_mode -> .word 0x80000000
//     banim_code_85 number -> .word 0x85000000 + number   (GENERIC 0x85 control/sound;
//                              the named sound macros all expand to a constant 0x85xxxxxx,
//                              so 0x85 commands are emitted with the generic macro + the
//                              exact 24-bit payload — NEVER a guessed named macro)
//     banim_code_nop -> .word 0x85000000
//   include/banim_code_frame.inc:
//     banim_frame_oam attr0, attr1, attr2, dx, dy -> .hword attr0, attr1, attr2, dx, dy, 0
//     banim_frame_affine pa, pb, pcc, pd, total=0
//     banim_frame_end -> .word 1, 0, 0
//   data/banim/banim_<tag>_motion.s layout: a 12-entry mode pointer table of
//     `.word mode_label - script`, an OAM `.section .data.oam_*`, a script
//     `.section .data.script`, and the 3 `.include`s above.
//
// CONSERVATIVE / HONEST SCOPE (the maintainer reopens over-claims):
//   * FULLY SUPPORTED: the per-mode SCRIPT stream — 0x86 frame commands map 1:1
//     to banim_code_frame; ALL 0x85 control/sound commands map to banim_code_85
//     with the exact 24-bit payload; 0x80000000 ends each mode.
//   * The 12-byte OAM entries are emitted as banim_frame_oam (a clean normal
//     sprite = 6 hwords). Affine OAM entries (bytes [2..3] == 0xFFFF) are emitted
//     as raw `.hword` + a diagnostic, because the banim_frame_affine layout is
//     NOT a 1:1 of the raw 12-byte affine entry — no guessing.
//   * The sheet (graphics) pointer is a raw 0x08XXXXXX ROM address. We emit it as
//     a valid macro argument + an unresolved-pointer diagnostic; the actual
//     decomp sheet symbol is a documented manual step (no guessed symbol).
//   * UNKNOWN command bytes are emitted as a COMMENTED placeholder + an actionable
//     diagnostic — NEVER a guessed / wrong macro word.
using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// READ-ONLY decomp-source exporter (#1363): reads a battle animation from the
    /// preview ROM and emits a reviewable <c>banim_&lt;TAG&gt;_motion.s</c> using the
    /// fireemblem8u banim macros. Never mutates the ROM, never throws. The pure
    /// <see cref="FormatBanimSource"/> formatter is split out so it can be unit-tested
    /// ROM-free (mirrors <c>VoicegroupAsmExportCore.FormatVoicegroup</c>).
    /// </summary>
    public static class BattleAnimDecompExportCore
    {
        const int SECTION_COUNT = 0xC;   // 12 modes (BattleAnimeExportCore.SECTION_COUNT)

        /// <summary>The decoded kind of one frame-stream command.</summary>
        public enum CommandKind
        {
            Frame,      // 0x86 (12 bytes): banim_code_frame
            Control,    // 0x85 (4 bytes): banim_code_85
            EndMode,    // 0x80000000 (4 bytes): banim_code_end_mode
            Unknown,    // anything else (emitted as a commented placeholder)
        }

        /// <summary>
        /// A single decoded frame-stream command. PURE data (no ROM reference) so the
        /// formatter is ROM-free testable.
        /// </summary>
        public sealed class CommandRecord
        {
            public CommandKind Kind;

            // Control (0x85):
            public uint Payload24;      // the 24-bit payload (word == 0x85000000 | Payload24)

            // Frame (0x86):
            public ushort Duration;     // bytes [0..1]
            public byte FrameNumber;    // byte [2]
            public uint SheetPointer;   // bytes [4..7] (a GBA pointer, raw)
            public bool SheetPointerValid;
            public uint OamOffset;      // bytes [8..11]

            // Unknown:
            public uint RawWord;        // the raw first word, for the placeholder comment
        }

        /// <summary>One decoded mode (section) = an ordered list of commands.</summary>
        public sealed class ModeRecord
        {
            public int Index;                       // 0..11
            public bool HasData;                    // false if start==end (empty mode)
            public List<CommandRecord> Commands = new List<CommandRecord>();
        }

        /// <summary>A decoded 12-byte OAM entry (normal sprite or affine matrix).</summary>
        public sealed class OamEntry
        {
            public ushort Attr0;    // hword [0]
            public ushort Attr1;    // hword [1]
            public ushort Attr2;    // hword [2]
            public ushort Dx;       // hword [3]
            public ushort Dy;       // hword [4]
            public ushort Pad;      // hword [5]
            public bool IsAffine;   // bytes [2..3] == 0xFFFF (raw matrix entry)
            public bool IsTerminator;
        }

        /// <summary>The full decoded animation (script modes + OAM lists + provenance).</summary>
        public sealed class AnimeRecord
        {
            public string Tag;                  // sanitized label tag
            public uint RecordAddr;             // the 32-byte record ROM offset
            public List<ModeRecord> Modes = new List<ModeRecord>();
            // OAM has TWO sides: +20 = right-facing (oam_r), +24 = left-facing (oam_l).
            // They are frequently identical (AutoGenLeftOAM), but can differ — we never
            // silently drop the +24 side (Copilot plan review).
            public List<OamEntry> OamRight = new List<OamEntry>();   // +20
            public List<OamEntry> OamLeft = new List<OamEntry>();    // +24
            public bool OamSidesShared;         // true when +20 and +24 decode identically
            public byte[] PaletteRaw;           // decompressed palette bytes (4 team sub-palettes × 16 colors)
            // The raw sheet (graphics) pointers referenced by frame commands, in
            // first-seen order (for the manifest's sheet→symbol checklist).
            public List<uint> SheetPointers = new List<uint>();
        }

        /// <summary>The result of an export: the emitted <c>.s</c> text + diagnostics.</summary>
        public sealed class ExportResult
        {
            public bool Ok;
            public string Text;                                 // the banim_<TAG>_motion.s source
            public AnimeRecord Anime;                           // the decoded record (for sidecars/manifest)
            public byte[] PaletteRaw;                           // raw decompressed palette bytes (team sub-palettes)
            public List<string> Diagnostics = new List<string>();
            public int ModeCount;
            public int FrameCount;
            public int OamEntryCount;
        }

        /// <summary>
        /// Read + format the battle animation whose 32-byte record is at
        /// <paramref name="animRecordAddr"/> into a reviewable
        /// <c>banim_&lt;TAG&gt;_motion.s</c>. READ-ONLY; never throws; on a guarded /
        /// out-of-range address returns <c>Ok=false</c> with a diagnostic and empty text.
        /// </summary>
        /// <param name="rom">The preview ROM (read-only).</param>
        /// <param name="animRecordAddr">The 32-byte animation record base (offset).</param>
        /// <param name="tag">The label tag used in symbol names / sections.</param>
        public static ExportResult Export(ROM rom, uint animRecordAddr, string tag)
        {
            var result = new ExportResult { Ok = false, Text = "" };
            try
            {
                if (rom == null)
                {
                    result.Diagnostics.Add(R._("ROM is not loaded."));
                    return result;
                }

                tag = SanitizeTag(tag);

                uint baseOffset = U.toOffset(animRecordAddr);
                if (!U.isSafetyOffset(baseOffset + 31, rom))
                {
                    result.Diagnostics.Add(R._("The animation record address 0x{0:X} is out of range.", baseOffset));
                    return result;
                }

                var diags = new List<string>();
                AnimeRecord anime = ReadAnime(rom, baseOffset, tag, diags);
                if (anime == null)
                {
                    if (diags.Count == 0)
                        diags.Add(R._("No valid battle animation was found at 0x{0:X}.", baseOffset));
                    result.Diagnostics.AddRange(diags);
                    return result;
                }

                result.Anime = anime;
                result.PaletteRaw = anime.PaletteRaw;
                result.ModeCount = anime.Modes.Count;
                result.OamEntryCount = anime.OamRight.Count + anime.OamLeft.Count;
                foreach (var m in anime.Modes)
                    foreach (var c in m.Commands)
                        if (c.Kind == CommandKind.Frame) result.FrameCount++;

                List<string> fmtDiags;
                result.Text = FormatBanimSource(anime, out fmtDiags);
                diags.AddRange(fmtDiags);

                // Dedup the combined diagnostics (first-seen order) so the CLI never
                // re-prints hundreds of identical per-frame notes.
                var seen = new HashSet<string>();
                foreach (var d in diags)
                    if (seen.Add(d)) result.Diagnostics.Add(d);

                result.Ok = true;
                return result;
            }
            catch (Exception ex)
            {
                // never-throws contract: surface as a diagnostic, no text.
                result.Ok = false;
                result.Text = "";
                result.Diagnostics.Add(R._("Battle animation decomp export failed: {0}", ex.Message));
                return result;
            }
        }

        // --------------------------------------------------------------------
        // Decoding (ROM-reading). Reuses the verbatim BattleAnimeExportCore /
        // BattleAnimeRendererCore decode (32-byte record + LZ77 sections).
        // Every read is bounds-guarded; never throws.
        // --------------------------------------------------------------------

        /// <summary>
        /// Decode the 32-byte record at <paramref name="baseOffset"/> into an
        /// <see cref="AnimeRecord"/> (script modes + OAM entries). Returns null with
        /// a diagnostic on an undecodable record.
        /// </summary>
        public static AnimeRecord ReadAnime(ROM rom, uint baseOffset, string tag, List<string> diagnostics)
        {
            if (rom == null) return null;
            if (diagnostics == null) diagnostics = new List<string>();

            uint sectionDataPtr = rom.u32(baseOffset + 12);
            uint frameDataPtr = rom.u32(baseOffset + 16);
            uint oamRightPtr = rom.u32(baseOffset + 20);  // right-facing OAM (oam_r)
            uint oamLeftPtr = rom.u32(baseOffset + 24);   // left-facing OAM (oam_l)
            uint palettePtr = rom.u32(baseOffset + 28);

            if (!U.isPointer(sectionDataPtr) || !U.isPointer(oamRightPtr)
                || !U.isPointer(oamLeftPtr) || !U.isPointer(palettePtr))
            {
                diagnostics.Add(R._("The animation record at 0x{0:X} contains invalid section/OAM/palette pointers.", baseOffset));
                return null;
            }

            uint sectionDataOff = U.toOffset(sectionDataPtr);
            if (!U.isSafetyOffset(sectionDataOff + (SECTION_COUNT * 4) - 1, rom))
            {
                diagnostics.Add(R._("The section table pointer 0x{0:X8} is out of range.", sectionDataPtr));
                return null;
            }

            byte[] sectionData = rom.getBinaryData(sectionDataOff, SECTION_COUNT * 4);

            // frameDataPtr may be an UnHuffman patch pointer; DecompressFrameData handles both.
            byte[] frameData = BattleAnimeRendererCore.DecompressFrameData(rom, frameDataPtr);
            if (frameData == null || frameData.Length == 0)
            {
                diagnostics.Add(R._("Failed to decompress the frame data at 0x{0:X8}.", frameDataPtr));
                return null;
            }
            if (FETextEncode.IsUnHuffmanPatchPointer(frameDataPtr))
                diagnostics.Add(R._("The frame data pointer is a version-specific UnHuffman patch pointer; the emitted script is decoded but the original frame-pointer form is NOT preserved (manual review)."));

            byte[] oamRightData = LZ77.decompress(rom.Data, U.toOffset(oamRightPtr));
            if (oamRightData == null || oamRightData.Length == 0)
            {
                diagnostics.Add(R._("Failed to decompress the right-facing OAM data at 0x{0:X8}.", oamRightPtr));
                return null;
            }
            byte[] oamLeftData = LZ77.decompress(rom.Data, U.toOffset(oamLeftPtr));
            if (oamLeftData == null || oamLeftData.Length == 0)
            {
                diagnostics.Add(R._("Failed to decompress the left-facing OAM data at 0x{0:X8}.", oamLeftPtr));
                return null;
            }

            var anime = new AnimeRecord { Tag = tag, RecordAddr = baseOffset };

            // Palette (LZ77; 4 team sub-palettes × 16 colors). Non-fatal if missing.
            byte[] paletteData = LZ77.decompress(rom.Data, U.toOffset(palettePtr));
            if (paletteData == null || paletteData.Length == 0)
                diagnostics.Add(R._("Failed to decompress the palette data at 0x{0:X8}; palette sidecars are skipped.", palettePtr));
            else
                anime.PaletteRaw = paletteData;

            // ---- Script modes ----
            for (int section = 0; section < SECTION_COUNT; section++)
            {
                uint start = U.u32(sectionData, (uint)(section * 4));
                uint end = (section + 1 < SECTION_COUNT)
                    ? U.u32(sectionData, (uint)((section + 1) * 4))
                    : (uint)frameData.Length;

                if (start > frameData.Length) start = (uint)frameData.Length;
                if (end > frameData.Length) end = (uint)frameData.Length;

                var mode = new ModeRecord { Index = section, HasData = end > start };
                if (mode.HasData)
                    ParseModeCommands(frameData, start, end, mode, anime.Tag, section, diagnostics);
                anime.Modes.Add(mode);

                // Collect unique sheet pointers (first-seen order) for the manifest.
                foreach (var c in mode.Commands)
                {
                    if (c.Kind == CommandKind.Frame && c.SheetPointerValid && c.SheetPointer != 0
                        && !anime.SheetPointers.Contains(c.SheetPointer))
                        anime.SheetPointers.Add(c.SheetPointer);
                }
            }

            // ---- OAM entries (12-byte) for BOTH sides ----
            bool sharedBytes = ByteArrayEquals(oamRightData, oamLeftData);
            anime.OamSidesShared = sharedBytes;
            ParseOamEntries(oamRightData, anime.OamRight, diagnostics);
            if (sharedBytes)
            {
                // identical bytes (AutoGenLeftOAM) — emit one side + a note, no duplicate parse.
                diagnostics.Add(R._("The left-facing OAM (+24) is byte-identical to the right-facing OAM (+20) (AutoGenLeftOAM); a single shared oam_r is emitted and oam_l aliases it."));
            }
            else
            {
                ParseOamEntries(oamLeftData, anime.OamLeft, diagnostics);
                diagnostics.Add(R._("The left-facing OAM (+24) differs from the right-facing OAM (+20); both oam_r and oam_l are emitted."));
            }

            return anime;
        }

        static void ParseModeCommands(byte[] frameData, uint start, uint end, ModeRecord mode,
            string tag, int section, List<string> diagnostics)
        {
            for (uint n = start; n + 3 < end; )
            {
                byte cmdType = frameData[n + 3];

                if (cmdType == 0x86)
                {
                    if (n + 12 > end || n + 12 > (uint)frameData.Length)
                    {
                        // truncated frame command — stop this mode safely
                        diagnostics.Add(R._("Mode {0}: a truncated 0x86 frame command was skipped at frame offset 0x{1:X}.", section, n));
                        break;
                    }
                    uint sheet = U.u32(frameData, n + 4);
                    var rec = new CommandRecord
                    {
                        Kind = CommandKind.Frame,
                        Duration = (ushort)(frameData[n] | (frameData[n + 1] << 8)),
                        FrameNumber = frameData[n + 2],
                        SheetPointer = sheet,
                        SheetPointerValid = U.isPointer(sheet),
                        OamOffset = U.u32(frameData, n + 8),
                    };
                    mode.Commands.Add(rec);
                    n += 12;
                }
                else if (cmdType == 0x85)
                {
                    uint payload24 = (uint)(frameData[n] | (frameData[n + 1] << 8) | (frameData[n + 2] << 16));
                    mode.Commands.Add(new CommandRecord { Kind = CommandKind.Control, Payload24 = payload24 });
                    n += 4;
                }
                else if (U.u32(frameData, n) == 0x80000000)
                {
                    // ONLY the exact 0x80000000 word ends a mode (the documented
                    // terminator). A 0x80xxxxxx word with a nonzero payload is NOT a
                    // terminator — it falls through to the Unknown placeholder below
                    // rather than silently truncating the mode (Copilot review).
                    mode.Commands.Add(new CommandRecord { Kind = CommandKind.EndMode });
                    n += 4;
                    break;
                }
                else
                {
                    uint raw = U.u32(frameData, n);
                    mode.Commands.Add(new CommandRecord { Kind = CommandKind.Unknown, RawWord = raw });
                    diagnostics.Add(R._("Mode {0}: an unknown command word 0x{1:X8} was emitted as a commented placeholder (manual conversion required).", section, raw));
                    n += 4;
                }
            }
        }

        static void ParseOamEntries(byte[] oamData, List<OamEntry> oam, List<string> diagnostics)
        {
            if (oamData == null) return;
            bool affineNoted = false;
            for (uint pos = 0; pos + 12 <= (uint)oamData.Length; pos += 12)
            {
                byte b0 = oamData[pos];

                // FEditor serialized alternate terminator (00 FF FF FF) or normal 0x01.
                if ((b0 == 0 && oamData[pos + 1] == 0xFF && oamData[pos + 2] == 0xFF && oamData[pos + 3] == 0xFF)
                    || b0 == 0x01)
                {
                    oam.Add(new OamEntry { IsTerminator = true });
                    break;
                }

                bool isAffine = oamData[pos + 2] == 0xFF && oamData[pos + 3] == 0xFF;
                var e = new OamEntry
                {
                    Attr0 = (ushort)(oamData[pos + 0] | (oamData[pos + 1] << 8)),
                    Attr1 = (ushort)(oamData[pos + 2] | (oamData[pos + 3] << 8)),
                    Attr2 = (ushort)(oamData[pos + 4] | (oamData[pos + 5] << 8)),
                    Dx = (ushort)(oamData[pos + 6] | (oamData[pos + 7] << 8)),
                    Dy = (ushort)(oamData[pos + 8] | (oamData[pos + 9] << 8)),
                    Pad = (ushort)(oamData[pos + 10] | (oamData[pos + 11] << 8)),
                    IsAffine = isAffine,
                };
                oam.Add(e);
                if (isAffine && !affineNoted)
                {
                    diagnostics.Add(R._("The OAM list contains affine matrix entries; they are emitted as raw .hword (the banim_frame_affine layout is not a 1:1 of the raw 12-byte entry) — review manually."));
                    affineNoted = true;
                }

                if (oam.Count > 4096)
                {
                    diagnostics.Add(R._("The OAM list exceeded 4096 entries without a terminator; truncated (manual review)."));
                    break;
                }
            }
        }

        // --------------------------------------------------------------------
        // PURE formatter: turn a decoded AnimeRecord into banim_<TAG>_motion.s
        // macro-assembly source. ROM-FREE so it is unit-tested with hand-built
        // records. Unresolved-pointer + unsupported diagnostics go in a TRAILING
        // comment block (never inline between macro args).
        // --------------------------------------------------------------------

        /// <summary>
        /// PURE formatter (ROM-free, unit-testable): emit the
        /// <c>banim_&lt;TAG&gt;_motion.s</c> source from a decoded
        /// <see cref="AnimeRecord"/>.
        /// </summary>
        public static string FormatBanimSource(AnimeRecord anime, out List<string> diagnostics)
        {
            diagnostics = new List<string>();
            var sb = new StringBuilder();
            if (anime == null)
            {
                sb.Append("@ (no animation)\n");
                return sb.ToString();
            }

            string tag = SanitizeTag(anime.Tag);
            string script = "banim_" + tag + "_script";
            string oamRightSym = "banim_" + tag + "_oam_r";
            string oamLeftSym = "banim_" + tag + "_oam_l";

            sb.Append("@ Exported by FEBuilderGBA (#1363) -- review before building.\n");
            sb.Append("@ Source battle-animation record ROM offset: 0x" + anime.RecordAddr.ToString("X") + "\n");
            sb.Append("@ Macros verified verbatim against FireEmblemUniverse/fireemblem8u:\n");
            sb.Append("@   include/banim_code.inc, include/banim_code_frame.inc.\n");
            sb.Append("@ This is an EXPORT / migration aid: banim_data[] / BattleAnimDef / linker\n");
            sb.Append("@ registration remain MANUAL (see the .json manifest).\n");
            sb.Append("\t.include \"banim_code.inc\"\n");
            sb.Append("\t.include \"banim_code_frame.inc\"\n\n");
            sb.Append("\t.global " + script + "\n");
            sb.Append("\t.global " + oamRightSym + "\n");
            sb.Append("\t.global " + oamLeftSym + "\n\n");

            // ---- OAM data sections (BOTH sides: +20 oam_r, +24 oam_l) ----
            sb.Append("\t.section .data.oam_r\n");
            sb.Append("\t.align 2\n");
            sb.Append(oamRightSym + ":\n");
            if (anime.OamRight == null || anime.OamRight.Count == 0)
                sb.Append("\t@ (no OAM entries)\n");
            else
                FormatOam(sb, anime.OamRight, diagnostics);
            sb.Append("\n");

            sb.Append("\t.section .data.oam_l\n");
            sb.Append("\t.align 2\n");
            if (anime.OamSidesShared)
            {
                // The left-facing OAM (+24) is byte-identical to the right (+20) — alias it.
                sb.Append(oamLeftSym + " = " + oamRightSym + "  @ +24 OAM is shared with +20 (AutoGenLeftOAM)\n");
            }
            else
            {
                sb.Append(oamLeftSym + ":\n");
                if (anime.OamLeft == null || anime.OamLeft.Count == 0)
                    sb.Append("\t@ (no OAM entries)\n");
                else
                    FormatOam(sb, anime.OamLeft, diagnostics);
            }
            sb.Append("\n");

            // ---- Script section: each mode is a label, body, end_mode ----
            sb.Append("\t.section .data.script\n");
            sb.Append("\t.align 2\n");
            sb.Append(script + ":\n");

            string[] modeLabels = new string[SECTION_COUNT];
            int modeCount = anime.Modes != null ? anime.Modes.Count : 0;
            for (int i = 0; i < SECTION_COUNT; i++)
            {
                ModeRecord m = (i < modeCount) ? anime.Modes[i] : null;
                if (m == null || !m.HasData)
                {
                    modeLabels[i] = null;   // empty mode -> table entry 0
                    continue;
                }
                string lbl = "banim_" + tag + "_mode_" + i.ToString();
                modeLabels[i] = lbl;
                sb.Append(lbl + ":\n");
                FormatModeBody(sb, m, diagnostics);
            }

            // ---- mode pointer table: 12 mode-offset words PLUS 12 trailing zero
            // words (24 total), matching the upstream banim_<tag>_motion.s layout. ----
            sb.Append("\n\t.section .data.modes\n");
            sb.Append("\t.align 2\n");
            sb.Append("banim_" + tag + "_modes:\n");
            for (int i = 0; i < SECTION_COUNT; i++)
            {
                if (modeLabels[i] != null)
                    sb.Append("\t.word " + modeLabels[i] + " - " + script + "\n");
                else
                    sb.Append("\t.word 0\n");
            }
            sb.Append("\t@ trailing zero padding (upstream banim mode table is 24 words)\n");
            for (int i = 0; i < SECTION_COUNT; i++)
                sb.Append("\t.word 0\n");

            // ---- Trailing diagnostics (dedup, first-seen order; repeated per-frame
            // sheet-pointer notes collapse to one line) ----
            if (diagnostics.Count > 0)
            {
                var seen = new HashSet<string>();
                var unique = new List<string>();
                foreach (var d in diagnostics)
                    if (seen.Add(d)) unique.Add(d);

                sb.Append("\n@ ===== Export diagnostics (manual review required) =====\n");
                for (int i = 0; i < unique.Count; i++)
                    sb.Append("@   " + unique[i] + "\n");

                // Replace the formatter's own diagnostics list with the deduped set so
                // the CLI does not re-print hundreds of identical lines.
                diagnostics.Clear();
                diagnostics.AddRange(unique);
            }

            return sb.ToString();
        }

        static void FormatModeBody(StringBuilder sb, ModeRecord m, List<string> diagnostics)
        {
            bool sawEndMode = false;
            foreach (var c in m.Commands)
            {
                switch (c.Kind)
                {
                    case CommandKind.Frame:
                    {
                        string sheet = c.SheetPointerValid && c.SheetPointer != 0
                            ? "0x" + c.SheetPointer.ToString("X8")
                            : "0";
                        if (!c.SheetPointerValid || c.SheetPointer == 0)
                            diagnostics.Add(R._("Mode {0}: a frame sheet pointer was missing/out of range; emitted as 0.", m.Index));
                        else
                            diagnostics.Add(R._("Mode {0}: the frame sheet pointer 0x{1:X8} is an unresolved raw ROM address (no decomp symbol inferred).", m.Index, c.SheetPointer));
                        // banim_code_frame duration, sheet_addr, frame_number, oam_offset
                        sb.Append("\tbanim_code_frame " + c.Duration + ", " + sheet + ", " + c.FrameNumber
                            + ", " + c.OamOffset + "\n");
                        break;
                    }
                    case CommandKind.Control:
                    {
                        if (c.Payload24 == 0)
                            sb.Append("\tbanim_code_nop\n");
                        else
                            sb.Append("\tbanim_code_85 0x" + c.Payload24.ToString("X") + "\n");
                        break;
                    }
                    case CommandKind.EndMode:
                    {
                        sb.Append("\tbanim_code_end_mode\n");
                        sawEndMode = true;
                        break;
                    }
                    default:
                    {
                        sb.Append("\t@ UNKNOWN command word 0x" + c.RawWord.ToString("X8") + " -- emit manually\n");
                        break;
                    }
                }
            }
            if (!sawEndMode)
                sb.Append("\tbanim_code_end_mode\n");
        }

        static void FormatOam(StringBuilder sb, List<OamEntry> oam, List<string> diagnostics)
        {
            bool affineNoted = false;
            foreach (var e in oam)
            {
                if (e.IsTerminator)
                {
                    sb.Append("\tbanim_frame_end\n");
                    continue;
                }
                if (e.IsAffine)
                {
                    // Raw affine matrix entry (bytes [2..3] == 0xFFFF). Emit raw .hword
                    // (the banim_frame_affine layout is NOT a 1:1 of the raw entry).
                    sb.Append("\t.hword 0x" + e.Attr0.ToString("X4") + ", 0x" + e.Attr1.ToString("X4")
                        + ", 0x" + e.Attr2.ToString("X4") + ", 0x" + e.Dx.ToString("X4")
                        + ", 0x" + e.Dy.ToString("X4") + ", 0x" + e.Pad.ToString("X4") + " @ affine matrix\n");
                    if (!affineNoted)
                    {
                        diagnostics.Add(R._("An OAM section contains affine matrix entries; they are emitted as raw .hword (the banim_frame_affine layout is not a 1:1 of the raw 12-byte entry) — review manually."));
                        affineNoted = true;
                    }
                    continue;
                }
                // banim_frame_oam attr0, attr1, attr2, dx, dy  (the 6th hword pad is implicit in the macro)
                sb.Append("\tbanim_frame_oam 0x" + e.Attr0.ToString("X4") + ", 0x" + e.Attr1.ToString("X4")
                    + ", 0x" + e.Attr2.ToString("X4") + ", 0x" + e.Dx.ToString("X4")
                    + ", 0x" + e.Dy.ToString("X4") + "\n");
                if (e.Pad != 0)
                    diagnostics.Add(R._("An OAM entry had a non-zero 6th hword (0x{0:X4}); banim_frame_oam emits 0 there — review manually.", e.Pad));
            }
        }

        /// <summary>
        /// PURE: build a JSON manifest documenting the decoded record + the MANUAL
        /// registration checklist (banim_data[] / BattleAnimDef / linker), the
        /// per-side OAM symbols, the mode table, and the unresolved sheet pointers.
        /// ROM-free (uses only the decoded <see cref="AnimeRecord"/>) so it is unit-tested.
        /// </summary>
        public static string BuildManifestJson(AnimeRecord anime)
        {
            var sb = new StringBuilder();
            string tag = SanitizeTag(anime != null ? anime.Tag : null);
            sb.Append("{\n");
            sb.Append("  \"tool\": \"FEBuilderGBA --export-battle-anim-decomp (#1363)\",\n");
            sb.Append("  \"kind\": \"battle-animation\",\n");
            sb.Append("  \"tag\": \"" + JsonEscape(tag) + "\",\n");
            if (anime == null)
            {
                sb.Append("  \"error\": \"no animation decoded\"\n}\n");
                return sb.ToString();
            }
            sb.Append("  \"recordOffset\": \"0x" + anime.RecordAddr.ToString("X") + "\",\n");
            sb.Append("  \"motionSource\": \"banim_" + tag + "_motion.s\",\n");
            sb.Append("  \"script\": \"banim_" + tag + "_script\",\n");
            sb.Append("  \"oam\": { \"right\": \"banim_" + tag + "_oam_r\", \"left\": \"banim_" + tag
                + "_oam_l\", \"sidesShared\": " + (anime.OamSidesShared ? "true" : "false") + " },\n");

            // mode table (the 12 meaningful entries; 1 if the mode has data).
            sb.Append("  \"modes\": [");
            for (int i = 0; i < anime.Modes.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(anime.Modes[i].HasData ? "1" : "0");
            }
            sb.Append("],\n");
            sb.Append("  \"modeTableWords\": 24,\n");

            // unresolved sheet pointers — these need a decomp sheet symbol manually.
            sb.Append("  \"unresolvedSheetPointers\": [");
            for (int i = 0; i < anime.SheetPointers.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("\"0x" + anime.SheetPointers[i].ToString("X8") + "\"");
            }
            sb.Append("],\n");

            sb.Append("  \"manualRegistrationChecklist\": [\n");
            sb.Append("    \"Add a banim_data[] entry pointing at banim_" + tag + "_script / _oam_r / _oam_l.\",\n");
            sb.Append("    \"Wire the class BattleAnimDef (or skill/magic anime table) to the new banim_data[] index.\",\n");
            sb.Append("    \"Register banim_" + tag + "_motion.s in the linker script / .ld and the build (e.g. data/banim).\",\n");
            sb.Append("    \"Resolve each unresolvedSheetPointers entry to a real decomp sheet symbol (the raw ROM pointer is a placeholder).\",\n");
            sb.Append("    \"Import the referenced graphics sheets + TSA as decomp assets (TSA shared tilesets cannot be regenerated from a single PNG).\"\n");
            sb.Append("  ],\n");
            sb.Append("  \"note\": \"EXPORT / migration aid -- NOT a byte-pinned round-trip; the preview ROM is read-only.\"\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// PURE: convert the raw decompressed palette bytes into up to 4 JASC .pal
        /// files (one per 16-color team sub-palette). Returns a list of
        /// (suffix, palBytes) pairs; the CALLER writes them (root-confined). Never
        /// throws; returns an empty list when the palette is missing/short.
        /// </summary>
        public static List<(string Suffix, byte[] PalBytes)> BuildPaletteSidecars(byte[] paletteRaw)
        {
            var list = new List<(string, byte[])>();
            if (paletteRaw == null || paletteRaw.Length < 2) return list;
            const int SUB = 16 * 2; // 16 colors × 2 bytes
            int subCount = paletteRaw.Length / SUB;
            if (subCount <= 0) subCount = 1;     // emit at least the available colors
            for (int s = 0; s < subCount && s < 4; s++)
            {
                int off = s * SUB;
                int len = Math.Min(SUB, paletteRaw.Length - off);
                if (len <= 0) break;
                byte[] sub = new byte[len];
                Array.Copy(paletteRaw, off, sub, 0, len);
                byte[] pal;
                try { pal = PaletteFormatConverter.ExportToFormat(sub, PaletteFormat.JascPal); }
                catch { continue; }
                if (pal != null) list.Add(("_pal" + s + ".pal", pal));
            }
            return list;
        }

        static bool ByteArrayEquals(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// Sanitize a tag into a valid asm label fragment: lowercase, [a-z0-9_],
        /// non-empty. PURE.
        /// </summary>
        public static string SanitizeTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "anim";
            var sb = new StringBuilder();
            foreach (char ch in tag)
            {
                char c = char.ToLowerInvariant(ch);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            string s = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(s) ? "anim" : s;
        }
    }
}
