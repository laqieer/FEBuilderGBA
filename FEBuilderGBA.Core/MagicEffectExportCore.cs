// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform Export helper for the FEditor magic-animation (.txt script
// + per-frame PNG byte arrays). Ported from WF
// `FEBuilderGBA/ImageUtilMagicFEditor.cs` — `Export()` (~lines 537-687),
// `ExportOBjFrameImage()` (~lines 63-162), `ExportBGFrameImage()`
// (~lines 164-231).
//
// CSA Creator extension (#886): CSA_Creator frames have an EXTRA +28 TSA
// pointer beyond the standard 28-byte FEditor layout (total 32 bytes per
// frame record). `ExportMagicScriptLines` reads +28 when `isCsa=true`;
// `RenderCsaBgFrameSlot` uses the TSA to composite the BG via plain
// `ByteToImage16Tile` (not headerTSA). BG hash for CSA = rawBgPtr+rawTsaPtr.
// OBJ render and .txt format are IDENTICAL to FEditor → shared API is reused.
//
// This file is read-only (ZERO ROM mutation). The Avalonia view calls
// ExportMagicScriptLines() to get the .txt lines + ordered PNG slot list,
// RenderObjFrameSlot() / RenderCsaBgFrameSlot() / RenderBgFrameSlot() to
// get per-frame PNG bytes, and writes them to disk itself.
//
// Import (frame table + per-frame LZ77 images + palettes + OAM) is a
// follow-up tracked by #878 PR2 / #886.
//
// Frame layout (mirrors WF comments in DrawFrameImage / ExportOBjFrameImage):
//   +0  frame header u32  (byte[3] == 0x86)
//   +4  objImagePointer   (GBA pointer, LZ77)
//   +8  OAMAbsoStart      (raw u32, relative to objRightToLeftOAM base)
//   +12 OAMBGAbsoStart    (raw u32, relative to objBGRightToLeftOAM base)
//   +16 bgImagePointer    (GBA pointer, LZ77)
//   +20 objPalettePointer (GBA pointer, RAW 0x20 bytes)
//   +24 bgPalettePointer  (GBA pointer, RAW 0x20 bytes)
//   +28 bgTSAPointer      (GBA pointer, LZ77 — CSA Creator ONLY)
//
// Frame stride:
//   FEditor: 28 bytes (4-byte header + 24-byte body)
//   CSA Creator: 32 bytes (4-byte header + 28-byte body, includes +28 TSA)
//   Loop advances n+=24 (FEditor) or n+=28 (CSA) inside (outer n+=4 = 28/32).
//
// WF Export() loop ordering (the single walk this file mirrors):
//   0x86 → ExportOBjFrameImage(animeHash) → "O  p- <file>"
//        → ExportBGFrameImage(animeHash)  → "B  p- <file>"
//        → "<wait>"
//   0x85 → "C<hex24>"
//   0x00 0x01 0x00 0x80 → "~~~  #miss terminator", continue
//   second 0x80 → "~~~  #terminator", break
//   plain 0x80 (first) → break (no ~~~; WF L679 commented out)
//   unknown → break
//
// Single shared anime-hash (FIX 1): WF passes ONE List<uint> animeHash
// to BOTH ExportOBjFrameImage and ExportBGFrameImage. OBJ hash =
// (rawObjImagePtr<<8)|(OAMAbsoStart&0xFF);
// BG hash (FEditor) = rawBgImagePtr;
// BG hash (CSA)     = rawBgImagePtr + rawBgTsaPtr (mirrors WF CSA ExportBGFrameImage).
// Both call animeHash.IndexOf()/.Count on the SAME list so a BG slot is
// numbered AFTER any OBJ slots (e.g. frame 0 new OBJ → slot 0, new BG
// → slot 1).

using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Per-frame metadata from the magic-animation frame-data stream.
    /// </summary>
    public sealed class MagicFrameMeta
    {
        /// <summary>ROM offset of the 4-byte frame header (byte[3]==0x86).</summary>
        public uint RecordOffset;
        /// <summary>Display wait duration (u16 at record+0).</summary>
        public uint Wait;
        /// <summary>ROM offset of the LZ77-compressed OBJ tile image.</summary>
        public uint ObjImageOffset;
        /// <summary>OAM absolute-start value (RELATIVE to the obj OAM base — WF +8).</summary>
        public uint OamAbsoStart;
        /// <summary>OAM-BG absolute-start value (RELATIVE to the bg OAM base — WF +12).</summary>
        public uint OamBGAbsoStart;
        /// <summary>ROM offset of the LZ77-compressed BG tile image.</summary>
        public uint BgImageOffset;
        /// <summary>ROM offset of the RAW (not LZ77) OBJ palette (0x20 bytes).</summary>
        public uint ObjPaletteOffset;
        /// <summary>ROM offset of the RAW (not LZ77) BG palette (0x20 bytes).</summary>
        public uint BgPaletteOffset;
        // ---- GBA pointer values (for hash + log) ----
        public uint RawObjImagePtr;
        public uint RawBgImagePtr;
        public uint RawObjPalPtr;
        public uint RawBgPalPtr;
        /// <summary>GBA pointer to the LZ77-compressed BG TSA arrangement
        /// (CSA Creator frame layout +28 only; 0 for FEditor frames).</summary>
        public uint RawBgTsaPtr;
        /// <summary>ROM offset of the LZ77-compressed BG TSA arrangement
        /// (CSA Creator frames only; 0 for FEditor frames).</summary>
        public uint BgTsaOffset;
    }

    /// <summary>
    /// Command record for a 0x85 script command.
    /// </summary>
    public sealed class MagicCommandMeta
    {
        public uint RecordOffset;
        public uint Command24;
        public bool IsSound => (Command24 & 0xFF) == 0x48;
        public uint MusicId => (Command24 >> 8) & 0xFFFF;
    }

    /// <summary>
    /// Script line types emitted by
    /// <see cref="MagicEffectExportCore.ExportMagicScriptLines"/>.
    /// </summary>
    public enum MagicScriptLineKind
    {
        HeaderComment,
        StartMarker,
        ObjImage,
        BgImage,
        Wait,
        Command,
        Terminator,
        EndMarker,
    }

    /// <summary>
    /// One emitted script line (text + metadata).
    /// </summary>
    public sealed class MagicScriptLine
    {
        public MagicScriptLineKind Kind;
        public string Text;
        /// <summary>For ObjImage / BgImage lines: shared slot index (matches WF
        /// animeHash index — single shared list for both OBJ and BG).</summary>
        public int ImageIndex = -1;
    }

    /// <summary>
    /// Cross-platform, read-only Export helper for the FEditor magic animation
    /// script and per-frame PNG images. Zero ROM mutation.
    ///
    /// <para><b>Primary entry point: <see cref="ExportMagicScriptLines"/></b> —
    /// a SINGLE ordered walk mirroring WF <c>Export()</c> exactly (FIX 1+2+3).</para>
    ///
    /// <para>Legacy two-step API (<see cref="ScanMagicFrames"/> +
    /// <see cref="ExportMagicScript"/>) is kept for backward-compatibility.</para>
    /// </summary>
    public static class MagicEffectExportCore
    {
        public const int OBJ_EXPORT_WIDTH  = 480;
        public const int OBJ_EXPORT_HEIGHT = 160;
        public const int BG_EXPORT_WIDTH   = 264;
        public const int BG_EXPORT_HEIGHT  = 64;

        const int OBJ_SHEET_ROM_WIDTH    = 256;
        const int BG_SHEET_ROM_WIDTH     = 256;
        const int BG_SHEET_ROM_HEIGHT    = 64;
        const int TILE_SIZE              = 8;
        const int BYTES_PER_TILE_4BPP    = 32;
        const uint SCAN_LIMITER          = 1024 * 1024;

        // -----------------------------------------------------------------------
        // ExportMagicScriptLines — PRIMARY single-walk API (FIX 1+2+3)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Walk the FEditor magic-animation frame-data stream and produce the
        /// ordered .txt script lines in a SINGLE pass that mirrors WF
        /// <c>ImageUtilMagicFEditor.Export()</c>'s loop exactly.
        ///
        /// <para><b>FIX 1 — Single shared anime-hash:</b> WF passes ONE
        /// <c>List&lt;uint&gt; animeHash</c> to both
        /// <c>ExportOBjFrameImage</c> and <c>ExportBGFrameImage</c>, so a BG
        /// slot is numbered AFTER any OBJ slots already added. Example: frame 0
        /// new OBJ → <c>o_000.png</c> (slot 0); new BG → <c>b_001.png</c>
        /// (slot 1).</para>
        ///
        /// <para><b>FIX 2 — Inline ~~~:</b> The <c>~~~</c> line is emitted
        /// INLINE at the stream position where the <c>0x00 0x01 0x00 0x80</c>
        /// miss-continuation record is found — interleaved with preceding
        /// OBJ/BG/wait lines. On a second 0x80 (any kind) the method emits
        /// <c>~~~  #terminator</c> then breaks. A plain first 0x80 stops
        /// without emitting <c>~~~</c> (WF L679 is commented out).</para>
        ///
        /// <para><b>FIX 3 — No separate detector:</b> The ~~~ emission is
        /// handled entirely by this single walk; the view's buggy
        /// <c>DetectMissContinuation</c> is removed.</para>
        ///
        /// <para>EOF-safe: guards n+4 and n+28 (or n+32 for CSA) before every read.</para>
        /// </summary>
        /// <param name="isCsa">When <c>true</c> reads the +28 TSA pointer (CSA Creator
        /// frame layout, 32-byte record). When <c>false</c> uses the standard 28-byte
        /// FEditor record with no TSA.</param>
        public static List<MagicScriptLine> ExportMagicScriptLines(
            ROM rom,
            uint frameDataAddr,
            string basename,
            bool enableComment,
            out List<int> sharedObjSlots,
            out List<int> sharedBgSlots,
            out List<MagicFrameMeta> frames,
            bool isCsa = false)
        {
            sharedObjSlots = new List<int>();
            sharedBgSlots  = new List<int>();
            frames         = new List<MagicFrameMeta>();
            var lines = new List<MagicScriptLine>();

            if (rom == null || rom.Data == null)
                return lines;

            if (enableComment)
            {
                lines.Add(L(MagicScriptLineKind.HeaderComment,
                    "#######################################################"));
                lines.Add(L(MagicScriptLineKind.HeaderComment, "#"));
                lines.Add(L(MagicScriptLineKind.HeaderComment,
                    "#" + R._("FEditorAdvにインポートする時には各行の#以降を削除してください。")));
                lines.Add(L(MagicScriptLineKind.HeaderComment,
                    "#######################################################"));
            }
            lines.Add(L(MagicScriptLineKind.StartMarker, "/// - Start Animation"));

            // ONE shared anime-hash for OBJ+BG (FIX 1 — mirrors WF List<uint> animeHash).
            var animeHash = new List<uint>();

            uint frameDataOffset = U.isSafetyPointer(frameDataAddr)
                ? U.toOffset(frameDataAddr) : frameDataAddr;
            if (!U.isSafetyOffset(frameDataOffset, rom))
            {
                lines.Add(L(MagicScriptLineKind.EndMarker, "/// - End of animation"));
                return lines;
            }

            uint dataLen = (uint)rom.Data.Length;
            uint limiter = frameDataOffset + SCAN_LIMITER;
            if (limiter > dataLen) limiter = dataLen;

            byte[] data = rom.Data;
            int termCount = 0;
            string objBaseName = basename + "o_";
            string bgBaseName  = basename + "b_";

            for (uint n = frameDataOffset; n < limiter; n += 4)
            {
                if (n + 4 > dataLen) break;
                byte cmd = data[n + 3];

                if (cmd == 0x80)
                {
                    bool isCont = (n + 2 <= dataLen) && (data[n + 1] == 0x01);
                    termCount++;
                    if (isCont)
                {
                    if (termCount == 1)
                    {
                        // FIX 2: miss-continuation → emit ~~~ INLINE at this stream
                        // position (interleaved with preceding frames). WF Export()
                        // lines 584-593: termCount==1 branch emits #miss terminator
                        // then continues.
                        string missLine = "~~~";
                        if (enableComment)
                            missLine += "                               #miss terminator";
                        lines.Add(L(MagicScriptLineKind.Terminator, missLine));
                        continue;
                    }
                    else
                    {
                        // WF: second 0x01 0x80 → emit ~~~ #terminator, break.
                        string termLine = "~~~";
                        if (enableComment)
                            termLine += "                               #terminator";
                        lines.Add(L(MagicScriptLineKind.Terminator, termLine));
                        break;
                    }
                }
                else
                {
                    // Plain 0x80 (first, no continuation byte) → break without
                    // emitting ~~~. WF L679 is commented out; we mirror that.
                    break;
                }
                }

                if (cmd == 0x85)
                {
                    uint id24 = U.u24(data, n);
                    bool isSound = (data[n] == 0x48);
                    string cmdLine = "C" + U.ToHexString(id24);
                    if (enableComment)
                    {
                        if (isSound)
                            cmdLine += "                               #Sound " + ((id24 >> 8) & 0xFFFF);
                        else
                            cmdLine += "                               #cmd 0x" + (id24 & 0xFF).ToString("X2");
                    }
                    lines.Add(L(MagicScriptLineKind.Command, cmdLine));
                    continue;
                }

                if (cmd != 0x86) break;
                // CSA frames are 32 bytes (adds +28 TSA); FEditor frames are 28 bytes.
                uint minFrameBytes = isCsa ? 32u : 28u;
                if (n + minFrameBytes > dataLen) break;

                uint wait      = U.u16(data, n);
                uint rawObjImg = U.u32(data, n + 4);
                uint oamAbso   = U.u32(data, n + 8);
                uint oamBGAbso = U.u32(data, n + 12);
                uint rawBgImg  = U.u32(data, n + 16);
                uint rawObjPal = U.u32(data, n + 20);
                uint rawBgPal  = U.u32(data, n + 24);
                // +28: CSA only — LZ77-compressed BG TSA arrangement.
                uint rawBgTsa  = isCsa ? U.u32(data, n + 28) : 0u;

                frames.Add(new MagicFrameMeta
                {
                    RecordOffset     = n,
                    Wait             = wait,
                    ObjImageOffset   = U.isPointer(rawObjImg) ? U.toOffset(rawObjImg) : rawObjImg,
                    OamAbsoStart     = oamAbso,
                    OamBGAbsoStart   = oamBGAbso,
                    BgImageOffset    = U.isPointer(rawBgImg)  ? U.toOffset(rawBgImg)  : rawBgImg,
                    ObjPaletteOffset = U.isPointer(rawObjPal) ? U.toOffset(rawObjPal) : rawObjPal,
                    BgPaletteOffset  = U.isPointer(rawBgPal)  ? U.toOffset(rawBgPal)  : rawBgPal,
                    RawObjImagePtr   = rawObjImg,
                    RawBgImagePtr    = rawBgImg,
                    RawObjPalPtr     = rawObjPal,
                    RawBgPalPtr      = rawBgPal,
                    RawBgTsaPtr      = rawBgTsa,
                    BgTsaOffset      = (rawBgTsa != 0 && U.isPointer(rawBgTsa))
                                           ? U.toOffset(rawBgTsa) : rawBgTsa,
                });

                // OBJ — shared hash (FIX 1)
                uint objHash = (rawObjImg << 8) | (oamAbso & 0xFF);
                int objSlot = animeHash.IndexOf(objHash);
                if (objSlot < 0) { objSlot = animeHash.Count; animeHash.Add(objHash); }
                sharedObjSlots.Add(objSlot);
                lines.Add(new MagicScriptLine
                {
                    Kind = MagicScriptLineKind.ObjImage,
                    Text = "O  p- " + objBaseName + objSlot.ToString("000") + ".png",
                    ImageIndex = objSlot,
                });

                // BG — shared hash (FIX 1)
                // CSA: hash = rawBgImg + rawBgTsa (mirrors WF ExportBGFrameImage CSA path).
                // FEditor: hash = rawBgImg only.
                uint bgHash = isCsa ? (rawBgImg + rawBgTsa) : rawBgImg;
                int bgSlot = animeHash.IndexOf(bgHash);
                if (bgSlot < 0) { bgSlot = animeHash.Count; animeHash.Add(bgHash); }
                sharedBgSlots.Add(bgSlot);
                lines.Add(new MagicScriptLine
                {
                    Kind = MagicScriptLineKind.BgImage,
                    Text = "B  p- " + bgBaseName + bgSlot.ToString("000") + ".png",
                    ImageIndex = bgSlot,
                });

                lines.Add(L(MagicScriptLineKind.Wait, wait.ToString()));
                // CSA: advance 28 (inside) + 4 (outer) = 32; FEditor: 24+4=28.
                n += isCsa ? 28u : 24u; // + 4 from outer loop
            }

            // WF L679: trailing ~~~ commented out — not emitted.
            lines.Add(L(MagicScriptLineKind.EndMarker, "/// - End of animation"));
            return lines;
        }

        // -----------------------------------------------------------------------
        // ScanMagicFrames (legacy — kept for existing tests)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Walk the frame-data stream and return 0x86 frame records + 0x85
        /// command records. Legacy API; use <see cref="ExportMagicScriptLines"/>
        /// for WF-exact output.
        /// </summary>
        public static bool ScanMagicFrames(
            ROM rom,
            uint frameDataAddr,
            uint objRightToLeftOAM,
            uint objBGRightToLeftOAM,
            out List<MagicFrameMeta> frames,
            out List<MagicCommandMeta> commands)
        {
            frames = new List<MagicFrameMeta>();
            commands = new List<MagicCommandMeta>();

            if (rom == null || rom.Data == null) return false;

            uint frameDataOffset = U.isSafetyPointer(frameDataAddr)
                ? U.toOffset(frameDataAddr) : frameDataAddr;
            if (!U.isSafetyOffset(frameDataOffset, rom)) return false;

            uint dataLen = (uint)rom.Data.Length;
            uint limiter = frameDataOffset + SCAN_LIMITER;
            if (limiter > dataLen) limiter = dataLen;

            byte[] data = rom.Data;
            int termCount = 0;

            for (uint n = frameDataOffset; n < limiter; n += 4)
            {
                if (n + 4 > dataLen) break;
                byte cmd = data[n + 3];

                if (cmd == 0x80)
                {
                    bool isCont = (n + 2 <= dataLen) && (data[n + 1] == 0x01);
                    termCount++;
                    if (isCont && termCount == 1) continue;
                    break;
                }

                if (cmd == 0x85)
                {
                    commands.Add(new MagicCommandMeta
                    {
                        RecordOffset = n,
                        Command24 = U.u24(data, n),
                    });
                    continue;
                }

                if (cmd != 0x86) break;
                if (n + 28 > dataLen) break;

                uint wait      = U.u16(data, n);
                uint rawObjImg = U.u32(data, n + 4);
                uint oamAbso   = U.u32(data, n + 8);
                uint oamBGAbso = U.u32(data, n + 12);
                uint rawBgImg  = U.u32(data, n + 16);
                uint rawObjPal = U.u32(data, n + 20);
                uint rawBgPal  = U.u32(data, n + 24);

                frames.Add(new MagicFrameMeta
                {
                    RecordOffset     = n,
                    Wait             = wait,
                    ObjImageOffset   = U.isPointer(rawObjImg) ? U.toOffset(rawObjImg) : rawObjImg,
                    OamAbsoStart     = oamAbso,
                    OamBGAbsoStart   = oamBGAbso,
                    BgImageOffset    = U.isPointer(rawBgImg)  ? U.toOffset(rawBgImg)  : rawBgImg,
                    ObjPaletteOffset = U.isPointer(rawObjPal) ? U.toOffset(rawObjPal) : rawObjPal,
                    BgPaletteOffset  = U.isPointer(rawBgPal)  ? U.toOffset(rawBgPal)  : rawBgPal,
                    RawObjImagePtr   = rawObjImg,
                    RawBgImagePtr    = rawBgImg,
                    RawObjPalPtr     = rawObjPal,
                    RawBgPalPtr      = rawBgPal,
                });

                n += 24;
            }

            return true;
        }

        // -----------------------------------------------------------------------
        // ExportMagicScript (legacy — kept for existing tests)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Legacy two-step script builder. Uses SEPARATE objAnimeHash / bgAnimeHash
        /// lists (does NOT match WF shared hash). Kept for backward-compatibility.
        /// Use <see cref="ExportMagicScriptLines"/> for WF-exact output.
        /// </summary>
        public static List<MagicScriptLine> ExportMagicScript(
            ROM rom,
            List<MagicFrameMeta> frames,
            List<MagicCommandMeta> commands,
            string basename,
            bool enableComment,
            bool scanHadContinuation,
            out List<int> objImageIndices,
            out List<int> bgImageIndices)
        {
            objImageIndices = new List<int>();
            bgImageIndices = new List<int>();
            var lines = new List<MagicScriptLine>();

            if (enableComment)
            {
                lines.Add(L(MagicScriptLineKind.HeaderComment,
                    "#######################################################"));
                lines.Add(L(MagicScriptLineKind.HeaderComment, "#"));
                lines.Add(L(MagicScriptLineKind.HeaderComment,
                    "#" + R._("FEditorAdvにインポートする時には各行の#以降を削除してください。")));
                lines.Add(L(MagicScriptLineKind.HeaderComment,
                    "#######################################################"));
            }
            lines.Add(L(MagicScriptLineKind.StartMarker, "/// - Start Animation"));

            var objAnimeHash = new List<uint>();
            var bgAnimeHash  = new List<uint>();

            int fi = 0, ci = 0;

            if (scanHadContinuation)
            {
                string line = "~~~";
                if (enableComment)
                    line += "                               #miss terminator";
                lines.Add(L(MagicScriptLineKind.Terminator, line));
            }

            while (fi < frames.Count || ci < commands.Count)
            {
                bool pickFrame;
                if (fi >= frames.Count) pickFrame = false;
                else if (ci >= commands.Count) pickFrame = true;
                else pickFrame = frames[fi].RecordOffset < commands[ci].RecordOffset;

                if (!pickFrame)
                {
                    var cmd = commands[ci++];
                    string cmdLine = "C" + U.ToHexString(cmd.Command24);
                    if (enableComment)
                    {
                        if (cmd.IsSound)
                            cmdLine += "                               #Sound " + cmd.MusicId;
                        else
                            cmdLine += "                               #cmd 0x" + (cmd.Command24 & 0xFF).ToString("X2");
                    }
                    lines.Add(L(MagicScriptLineKind.Command, cmdLine));
                }
                else
                {
                    var frame = frames[fi++];

                    uint objHash = (frame.RawObjImagePtr << 8) | (frame.OamAbsoStart & 0xFF);
                    int objSlot = objAnimeHash.IndexOf(objHash);
                    if (objSlot < 0) { objSlot = objAnimeHash.Count; objAnimeHash.Add(objHash); }
                    objImageIndices.Add(objSlot);
                    lines.Add(new MagicScriptLine
                    {
                        Kind = MagicScriptLineKind.ObjImage,
                        Text = "O  p- " + basename + "o_" + objSlot.ToString("000") + ".png",
                        ImageIndex = objSlot,
                    });

                    uint bgHash = frame.RawBgImagePtr;
                    int bgSlot = bgAnimeHash.IndexOf(bgHash);
                    if (bgSlot < 0) { bgSlot = bgAnimeHash.Count; bgAnimeHash.Add(bgHash); }
                    bgImageIndices.Add(bgSlot);
                    lines.Add(new MagicScriptLine
                    {
                        Kind = MagicScriptLineKind.BgImage,
                        Text = "B  p- " + basename + "b_" + bgSlot.ToString("000") + ".png",
                        ImageIndex = bgSlot,
                    });

                    lines.Add(L(MagicScriptLineKind.Wait, frame.Wait.ToString()));
                }
            }

            lines.Add(L(MagicScriptLineKind.EndMarker, "/// - End of animation"));
            return lines;
        }

        // -----------------------------------------------------------------------
        // RenderObjFrameSlot (FIX 5 — dead code removed)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Render the OBJ frame image for the given shared slot index (480x160).
        /// Slot search rebuilds the shared anime-hash (OBJ then BG per frame)
        /// to find the frame whose OBJ hash first occupies
        /// <paramref name="slotIndex"/>.
        ///
        /// <para><b>FIX 5:</b> Removed the dead-code
        /// <c>else if (s == slotIndex) break;</c> branch.</para>
        /// </summary>
        public static IImage RenderObjFrameSlot(
            ROM rom,
            List<MagicFrameMeta> frames,
            int slotIndex,
            uint objRightToLeftOAM,
            uint objBGRightToLeftOAM)
        {
            if (rom == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (frames == null || slotIndex < 0) return null;

            var sharedHash = new List<uint>();
            MagicFrameMeta target = null;
            foreach (var f in frames)
            {
                uint objHash = (f.RawObjImagePtr << 8) | (f.OamAbsoStart & 0xFF);
                int s = sharedHash.IndexOf(objHash);
                if (s < 0)
                {
                    s = sharedHash.Count;
                    sharedHash.Add(objHash);
                    if (s == slotIndex) { target = f; break; }
                }
                // FIX 5: dead "else if (s == slotIndex) break;" removed.
                uint bgHash = f.RawBgImagePtr;
                if (sharedHash.IndexOf(bgHash) < 0) sharedHash.Add(bgHash);
            }
            if (target == null) return null;
            return RenderObjFrameInternal(rom, target, objRightToLeftOAM, objBGRightToLeftOAM);
        }

        /// <summary>
        /// Render the BG frame image for the given shared slot index (264x64).
        /// Slot search rebuilds the shared anime-hash (OBJ first, then BG per
        /// frame) to find the frame whose BG hash first occupies
        /// <paramref name="slotIndex"/>.
        /// </summary>
        public static IImage RenderBgFrameSlot(
            ROM rom,
            List<MagicFrameMeta> frames,
            int slotIndex)
        {
            if (rom == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (frames == null || slotIndex < 0) return null;

            var sharedHash = new List<uint>();
            MagicFrameMeta target = null;
            foreach (var f in frames)
            {
                uint objHash = (f.RawObjImagePtr << 8) | (f.OamAbsoStart & 0xFF);
                if (sharedHash.IndexOf(objHash) < 0) sharedHash.Add(objHash);

                uint bgHash = f.RawBgImagePtr;
                int s = sharedHash.IndexOf(bgHash);
                if (s < 0)
                {
                    s = sharedHash.Count;
                    sharedHash.Add(bgHash);
                    if (s == slotIndex) { target = f; break; }
                }
            }
            if (target == null) return null;
            return RenderBgFrameInternal(rom, target);
        }

        // -----------------------------------------------------------------------
        // RenderCsaBgFrameSlot — CSA Creator BG with TSA-composite render (#886)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Render the CSA Creator BG frame image for the given shared slot index.
        /// Unlike <see cref="RenderBgFrameSlot"/> (which renders a plain 264×64
        /// FEditor BG tilesheet), this method composites the BG via the +28
        /// LZ77-compressed TSA arrangement, mirroring WF
        /// <c>ImageUtilMagicCSACreator.ExportBGFrameImage</c>.
        ///
        /// <para>BG hash for CSA = <c>rawBgImagePtr + rawBgTsaPtr</c> (mirrors WF
        /// <c>imageHash = bgPointer + bgTSAPointer</c>).</para>
        ///
        /// <para>Output dimensions:
        ///   <c>width = 240</c> (256 − 8 − 8);
        ///   <c>height = CalcHeightbyTSA(240, tsa.Length)</c> capped at 160 if
        ///   ≥ 160, otherwise 64 (mirrors WF ExportBGFrameImage height logic).</para>
        /// </summary>
        public static IImage RenderCsaBgFrameSlot(
            ROM rom,
            List<MagicFrameMeta> frames,
            int slotIndex)
        {
            if (rom == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (frames == null || slotIndex < 0) return null;

            var sharedHash = new List<uint>();
            MagicFrameMeta target = null;
            foreach (var f in frames)
            {
                uint objHash = (f.RawObjImagePtr << 8) | (f.OamAbsoStart & 0xFF);
                if (sharedHash.IndexOf(objHash) < 0) sharedHash.Add(objHash);

                // CSA BG hash = rawBgImg + rawBgTsa (mirrors WF ExportBGFrameImage).
                uint bgHash = f.RawBgImagePtr + f.RawBgTsaPtr;
                int s = sharedHash.IndexOf(bgHash);
                if (s < 0)
                {
                    s = sharedHash.Count;
                    sharedHash.Add(bgHash);
                    if (s == slotIndex) { target = f; break; }
                }
            }
            if (target == null) return null;
            return RenderCsaBgFrameInternal(rom, target);
        }

        // -----------------------------------------------------------------------
        // RenderCsaFramePreview — live 240×128 CSA frame composite (#1021)
        // -----------------------------------------------------------------------

        /// <summary>Live-preview canvas width — mirrors WF
        /// <c>ImageUtilMagicCSACreator.SCREEN_TILE_WIDTH*8 = 240</c>.</summary>
        public const int CSA_PREVIEW_WIDTH  = 240; // 240/8 tiles * 8
        /// <summary>Live-preview canvas height — mirrors WF
        /// <c>ImageUtilMagicCSACreator.SCREEN_TILE_HEIGHT*8 = 128</c>.</summary>
        public const int CSA_PREVIEW_HEIGHT = 128; // 64*2/8 tiles * 8

        /// <summary>
        /// Render a single CSA-Creator frame as a 240×128 live-preview image
        /// (READ-ONLY: zero ROM mutation). Mirrors WF
        /// <c>ImageUtilMagicCSACreator.Draw</c> →
        /// <c>DrawFrameImage</c> composite order:
        /// <list type="number">
        ///   <item>BG (honoring the <c>IsExpandsBG</c> top-64 vertical scale driven by
        ///     the preceding 0x85 <c>0x53</c> command);</item>
        ///   <item>OBJ <b>back</b> layer (the RIGHT 240px of the 480×160
        ///     <see cref="RenderObjFrameSlot"/> output);</item>
        ///   <item>OBJ <b>front</b> layer (the LEFT 240px), each alpha-over.</item>
        /// </list>
        ///
        /// <para><b>slotIndex ≠ frameIndex.</b> The per-frame OBJ/BG <em>shared</em>
        /// slot indices are obtained from
        /// <see cref="ExportMagicScriptLines"/> with <c>isCsa:true</c> (one entry per
        /// frame in <c>sharedObjSlots</c>/<c>sharedBgSlots</c>). One CSA frame can map
        /// to OBJ slot 0 + BG slot 1, so <paramref name="frameIndex"/> is NEVER passed
        /// directly as a slot index.</para>
        ///
        /// <para>Returns <c>null</c> (never throws) on any guard failure: null ROM,
        /// no ImageService, a failed/empty CSA scan, an out-of-range
        /// <paramref name="frameIndex"/>, or a failed slot render.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="frameDataAddr">P0 — GBA pointer (or raw offset) to the CSA
        ///   frame-data script.</param>
        /// <param name="frameIndex">0-based index of the 0x86 frame to render.</param>
        /// <param name="objRightToLeftOAM">P4 — front OAM table base.</param>
        /// <param name="objBGRightToLeftOAM">P12 — back (BG) OAM table base.</param>
        /// <returns>A 240×128 <see cref="IImage"/>, or <c>null</c>.</returns>
        public static IImage RenderCsaFramePreview(
            ROM rom,
            uint frameDataAddr,
            uint frameIndex,
            uint objRightToLeftOAM,
            uint objBGRightToLeftOAM)
        {
            if (rom == null || rom.Data == null) return null;
            IImageService svc = CoreState.ImageService;
            if (svc == null) return null;

            // 1. CSA-aware scan + per-frame shared-slot mapping (slotIndex ≠ frameIndex).
            List<int> sharedObjSlots, sharedBgSlots;
            List<MagicFrameMeta> frames;
            ExportMagicScriptLines(
                rom, frameDataAddr, string.Empty, false,
                out sharedObjSlots, out sharedBgSlots, out frames,
                isCsa: true);

            if (frames.Count == 0) return null;
            if (frameIndex >= (uint)frames.Count) return null;

            int objSlot = sharedObjSlots[(int)frameIndex];
            int bgSlot  = sharedBgSlots[(int)frameIndex];

            // 2. Render the per-frame slots.
            //    BG: 240×(160|64) TSA-composited (CSA +28).
            //    OBJ: 480×160, front in the LEFT 240px, back in the RIGHT 240px.
            IImage bg  = RenderCsaBgFrameSlot(rom, frames, bgSlot);
            IImage obj = RenderObjFrameSlot(
                rom, frames, objSlot, objRightToLeftOAM, objBGRightToLeftOAM);

            byte[] bgPx  = bg?.GetPixelData();
            byte[] objPx = obj?.GetPixelData();
            int bgW = bg?.Width ?? 0, bgH = bg?.Height ?? 0;

            try
            {
                // 3. Composite into a 240×128 canvas (mirror WF DrawFrameImage).
                byte[] canvas = new byte[CSA_PREVIEW_WIDTH * CSA_PREVIEW_HEIGHT * 4];

                // 3a. BG first. WF fills with bgPalette[0] then draws BG; honour
                //     IsExpandsBG (the preceding 0x85/0x53 command scales the top
                //     64px of the BG into the full 240×128). When not expanding,
                //     the BG is blitted 1:1.
                if (bgPx != null && bgW == CSA_PREVIEW_WIDTH && bgH > 0)
                {
                    bool bgExpands = IsCsaExpandsBG(rom, frameDataAddr, frameIndex);
                    if (bgExpands)
                    {
                        // Scale the BG's top 64px up to fill the 240×128 canvas
                        // (mirrors WF ImageUtil.Scale(retImage,0,0,W,H, bg,0,0,W,64)).
                        int srcScaleH = Math.Min(64, bgH);
                        ScaleBgIntoCanvas(bgPx, bgW, bgH, srcScaleH,
                            canvas, CSA_PREVIEW_WIDTH, CSA_PREVIEW_HEIGHT);
                    }
                    else
                    {
                        // 1:1 blit of the top 128px (mirrors WF BitBlt opaque copy).
                        BlitOpaque(bgPx, bgW, bgH, canvas,
                            CSA_PREVIEW_WIDTH, CSA_PREVIEW_HEIGHT);
                    }
                }

                // 3b. OBJ back layer = the RIGHT 240px of the 480×160 OBJ image.
                // 3c. OBJ front layer = the LEFT 240px. Both alpha-over.
                if (objPx != null
                    && obj.Width == OBJ_EXPORT_WIDTH && obj.Height == OBJ_EXPORT_HEIGHT)
                {
                    AlphaOverObjHalf(objPx, OBJ_EXPORT_WIDTH, OBJ_EXPORT_HEIGHT,
                        srcX0: 240, canvas, CSA_PREVIEW_WIDTH, CSA_PREVIEW_HEIGHT); // back
                    AlphaOverObjHalf(objPx, OBJ_EXPORT_WIDTH, OBJ_EXPORT_HEIGHT,
                        srcX0: 0,   canvas, CSA_PREVIEW_WIDTH, CSA_PREVIEW_HEIGHT); // front
                }

                var image = svc.CreateImage(CSA_PREVIEW_WIDTH, CSA_PREVIEW_HEIGHT);
                image.SetPixelData(canvas);
                return image;
            }
            finally
            {
                bg?.Dispose();
                obj?.Dispose();
            }
        }

        /// <summary>
        /// Determine whether the BG of the <paramref name="frameIndex"/>-th 0x86 frame
        /// should be vertically expanded (mirrors WF
        /// <c>ImageUtilMagicCSACreator.IsExpandsBG</c>): a preceding 0x85 command
        /// whose first byte is <c>0x53</c> with <c>byte[1] &gt;= 0x01</c> sets the
        /// expand flag, which persists until overridden. EOF-safe; read-only.
        /// </summary>
        static bool IsCsaExpandsBG(ROM rom, uint frameDataAddr, uint frameIndex)
        {
            if (rom == null || rom.Data == null) return false;
            uint frameDataOffset = U.isSafetyPointer(frameDataAddr)
                ? U.toOffset(frameDataAddr) : frameDataAddr;
            if (!U.isSafetyOffset(frameDataOffset, rom)) return false;

            byte[] data = rom.Data;
            uint dataLen = (uint)data.Length;
            uint limiter = frameDataOffset + SCAN_LIMITER;
            if (limiter > dataLen) limiter = dataLen;

            uint frameI = 0;
            bool bgExpands = false;
            for (uint n = frameDataOffset; n < limiter; n += 4)
            {
                if (n + 4 > dataLen) break;
                byte cmd = data[n + 3];

                if (cmd == 0x80) // terminator (with 0x01 continuation tolerance)
                {
                    if (n + 2 <= dataLen && data[n + 1] == 0x01) continue;
                    break;
                }
                if (cmd != 0x86)
                {
                    if (cmd == 0x85)
                    {
                        // WF: a 0x53 command sets/clears the expand flag.
                        if (data[n + 0] == 0x53)
                            bgExpands = (data[n + 1] >= 0x01);
                        continue;
                    }
                    break; // unknown command
                }

                if (frameI == frameIndex) return bgExpands;
                if (n + 32 > dataLen) break; // CSA stride = 32
                frameI++;
                n += 24 + 4; // 28 (inside) + 4 (outer) = 32
            }
            return bgExpands;
        }

        /// <summary>Scale the top <paramref name="srcScaleH"/> rows of a 240-wide BG
        /// into the full <paramref name="dstW"/>×<paramref name="dstH"/> canvas
        /// (nearest-neighbour, opaque). Mirrors WF
        /// <c>ImageUtil.Scale(retImage,0,0,W,H, bg,0,0,W,64)</c>.</summary>
        static void ScaleBgIntoCanvas(
            byte[] src, int srcW, int srcTotalH, int srcScaleH,
            byte[] dst, int dstW, int dstH)
        {
            if (src == null || dst == null || srcScaleH <= 0) return;
            for (int dy = 0; dy < dstH; dy++)
            {
                int sy = dy * srcScaleH / dstH;
                if (sy >= srcTotalH) sy = srcTotalH - 1;
                if (sy < 0) continue;
                for (int dx = 0; dx < dstW; dx++)
                {
                    int sx = dx; // 1:1 horizontally (both 240 wide)
                    if (sx >= srcW) sx = srcW - 1;
                    int si = (sy * srcW + sx) * 4;
                    int di = (dy * dstW + dx) * 4;
                    if (si + 3 >= src.Length || di + 3 >= dst.Length) continue;
                    dst[di]     = src[si];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
        }

        /// <summary>Opaque 1:1 blit of the top-left <paramref name="dstW"/>×<paramref name="dstH"/>
        /// of a BG image into the canvas (mirrors WF non-expand BitBlt).</summary>
        static void BlitOpaque(
            byte[] src, int srcW, int srcH,
            byte[] dst, int dstW, int dstH)
        {
            if (src == null || dst == null) return;
            int h = Math.Min(srcH, dstH);
            int w = Math.Min(srcW, dstW);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int si = (y * srcW + x) * 4;
                int di = (y * dstW + x) * 4;
                if (si + 3 >= src.Length || di + 3 >= dst.Length) continue;
                dst[di]     = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        /// <summary>Alpha-over (source-over) the 240-wide half of the 480×160 OBJ image
        /// starting at <paramref name="srcX0"/> onto the canvas. A source pixel with
        /// alpha==0 is skipped (mirrors WF BitBlt transparency flag 1).</summary>
        static void AlphaOverObjHalf(
            byte[] src, int srcW, int srcH, int srcX0,
            byte[] dst, int dstW, int dstH)
        {
            if (src == null || dst == null) return;
            int h = Math.Min(srcH, dstH);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < dstW; x++)
            {
                int sx = srcX0 + x;
                if (sx >= srcW) break;
                int si = (y * srcW + sx) * 4;
                int di = (y * dstW + x) * 4;
                if (si + 3 >= src.Length || di + 3 >= dst.Length) continue;
                if (src[si + 3] == 0) continue; // transparent → keep BG
                dst[di]     = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        // -----------------------------------------------------------------------
        // Convenience slot counters (shared-hash aware — FIX 1)
        // -----------------------------------------------------------------------

        /// <summary>Count unique OBJ slots (shared-hash ordering).</summary>
        public static int CountUniqueObjSlots(List<MagicFrameMeta> frames)
        {
            if (frames == null) return 0;
            var sharedHash = new List<uint>();
            int count = 0;
            foreach (var f in frames)
            {
                uint h = (f.RawObjImagePtr << 8) | (f.OamAbsoStart & 0xFF);
                if (sharedHash.IndexOf(h) < 0) { sharedHash.Add(h); count++; }
                uint bg = f.RawBgImagePtr;
                if (sharedHash.IndexOf(bg) < 0) sharedHash.Add(bg);
            }
            return count;
        }

        /// <summary>Count unique BG slots (shared-hash ordering, offset after OBJ slots).
        /// For FEditor frames: hash = rawBgImagePtr.
        /// For CSA frames: hash = rawBgImagePtr + rawBgTsaPtr (mirrors WF).</summary>
        public static int CountUniqueBgSlots(List<MagicFrameMeta> frames, bool isCsa = false)
        {
            if (frames == null) return 0;
            var sharedHash = new List<uint>();
            int count = 0;
            foreach (var f in frames)
            {
                uint obj = (f.RawObjImagePtr << 8) | (f.OamAbsoStart & 0xFF);
                if (sharedHash.IndexOf(obj) < 0) sharedHash.Add(obj);
                uint bg = isCsa ? (f.RawBgImagePtr + f.RawBgTsaPtr) : f.RawBgImagePtr;
                if (sharedHash.IndexOf(bg) < 0) { sharedHash.Add(bg); count++; }
            }
            return count;
        }

        // -----------------------------------------------------------------------
        // Internal renderers
        // -----------------------------------------------------------------------

        static IImage RenderObjFrameInternal(
            ROM rom,
            MagicFrameMeta frame,
            uint objRightToLeftOAM,
            uint objBGRightToLeftOAM)
        {
            IImageService svc = CoreState.ImageService;

            uint objOAMBase = U.isSafetyPointer(objRightToLeftOAM)
                ? U.toOffset(objRightToLeftOAM) : objRightToLeftOAM;
            uint bgOAMBase = U.isSafetyPointer(objBGRightToLeftOAM)
                ? U.toOffset(objBGRightToLeftOAM) : objBGRightToLeftOAM;

            if (frame.ObjPaletteOffset + 0x20 > (uint)rom.Data.Length) return null;
            byte[] objPalette = rom.getBinaryData(frame.ObjPaletteOffset, 0x20);

            if (!U.isSafetyOffset(frame.ObjImageOffset, rom)) return null;
            byte[] objDecomp = LZ77.decompress(rom.Data, frame.ObjImageOffset);
            if (objDecomp == null || objDecomp.Length == 0)
            {
                Log.Error("MagicEffectExportCore: obj LZ77 decompress failed at",
                    "0x" + frame.ObjImageOffset.ToString("X08"));
                objDecomp = new byte[0x1000];
            }

            int objHeight = MagicEffectRendererCore.CalcHeight(OBJ_SHEET_ROM_WIDTH, objDecomp.Length);
            if (objHeight < 64) objHeight = 64;
            byte[] objSheet = DecodeSheet(objDecomp, objPalette, OBJ_SHEET_ROM_WIDTH, objHeight);

            byte[] canvas = new byte[OBJ_EXPORT_WIDTH * OBJ_EXPORT_HEIGHT * 4];

            {
                uint oamStart = objOAMBase + frame.OamAbsoStart;
                byte[] slice = new byte[240 * OBJ_EXPORT_HEIGHT * 4];
                BattleAnimeRendererCore.DrawOAMSprites(
                    rom.Data, oamStart,
                    objSheet, OBJ_SHEET_ROM_WIDTH, objHeight,
                    slice, 240, OBJ_EXPORT_HEIGHT, isMagicOAM: true);
                BlitSlice(slice, 240, OBJ_EXPORT_HEIGHT, canvas, OBJ_EXPORT_WIDTH, 0, 0);
            }

            {
                uint oamStart = bgOAMBase + frame.OamBGAbsoStart;
                byte[] slice = new byte[240 * OBJ_EXPORT_HEIGHT * 4];
                BattleAnimeRendererCore.DrawOAMSprites(
                    rom.Data, oamStart,
                    objSheet, OBJ_SHEET_ROM_WIDTH, objHeight,
                    slice, 240, OBJ_EXPORT_HEIGHT, isMagicOAM: true);
                BlitSlice(slice, 240, OBJ_EXPORT_HEIGHT, canvas, OBJ_EXPORT_WIDTH, 240, 0);
            }

            var image = svc.CreateImage(OBJ_EXPORT_WIDTH, OBJ_EXPORT_HEIGHT);
            image.SetPixelData(canvas);
            return image;
        }

        static IImage RenderBgFrameInternal(ROM rom, MagicFrameMeta frame)
        {
            IImageService svc = CoreState.ImageService;

            if (frame.BgPaletteOffset + 0x20 > (uint)rom.Data.Length) return null;
            byte[] bgPalette = rom.getBinaryData(frame.BgPaletteOffset, 0x20);

            if (!U.isSafetyOffset(frame.BgImageOffset, rom)) return null;
            byte[] bgDecomp = LZ77.decompress(rom.Data, frame.BgImageOffset);
            if (bgDecomp == null || bgDecomp.Length == 0)
            {
                Log.Error("MagicEffectExportCore: bg LZ77 decompress failed at",
                    "0x" + frame.BgImageOffset.ToString("X08"));
                return null;
            }

            byte[] bgSheet = DecodeSheet(bgDecomp, bgPalette, BG_SHEET_ROM_WIDTH, BG_SHEET_ROM_HEIGHT);
            byte[] canvas = new byte[BG_EXPORT_WIDTH * BG_EXPORT_HEIGHT * 4];

            byte[] bgColor = GBAPaletteColor0(bgPalette, svc);
            FillBackground(canvas, BG_EXPORT_WIDTH, BG_EXPORT_HEIGHT, bgColor);
            BlitSlice(bgSheet, BG_SHEET_ROM_WIDTH, BG_SHEET_ROM_HEIGHT,
                canvas, BG_EXPORT_WIDTH, 0, 0);

            var image = svc.CreateImage(BG_EXPORT_WIDTH, BG_EXPORT_HEIGHT);
            image.SetPixelData(canvas);
            return image;
        }

        // CSA_Creator BG export dimensions (mirrors WF ExportBGFrameImage).
        // width = 256 - 8 - 8 = 240; height capped at 160 (or 64 if < 160).
        public const int CSA_BG_EXPORT_WIDTH  = 240; // 256 - 8 - 8
        public const int CSA_BG_EXPORT_HEIGHT_FULL = 160;
        public const int CSA_BG_EXPORT_HEIGHT_SMALL = 64;

        /// <summary>
        /// CSA Creator BG render (mirrors WF
        /// <c>ImageUtilMagicCSACreator.ExportBGFrameImage</c>):
        /// decompress bg tilesheet + TSA, composite via plain
        /// <c>ImageUtilCore.DecodeTSA</c>-equivalent, output 240×160 (or 240×64).
        /// </summary>
        static IImage RenderCsaBgFrameInternal(ROM rom, MagicFrameMeta frame)
        {
            IImageService svc = CoreState.ImageService;

            if (frame.BgPaletteOffset + 0x20 > (uint)rom.Data.Length) return null;
            byte[] bgPalette = rom.getBinaryData(frame.BgPaletteOffset, 0x20);

            if (!U.isSafetyOffset(frame.BgImageOffset, rom)) return null;
            byte[] bgDecomp = LZ77.decompress(rom.Data, frame.BgImageOffset);
            if (bgDecomp == null || bgDecomp.Length == 0)
            {
                Log.Error("MagicEffectExportCore: CSA bg LZ77 decompress failed at",
                    "0x" + frame.BgImageOffset.ToString("X08"));
                return null;
            }

            if (frame.BgTsaOffset == 0 || !U.isSafetyOffset(frame.BgTsaOffset, rom))
                return null;
            byte[] tsaDecomp = LZ77.decompress(rom.Data, frame.BgTsaOffset);
            if (tsaDecomp == null || tsaDecomp.Length == 0)
            {
                Log.Error("MagicEffectExportCore: CSA TSA LZ77 decompress failed at",
                    "0x" + frame.BgTsaOffset.ToString("X08"));
                return null;
            }

            // Height logic mirrors WF ExportBGFrameImage:
            //   CalcHeightbyTSA(240, tsaLen) → if >= 160: height=160, else height=64.
            int width = CSA_BG_EXPORT_WIDTH;
            int height = CalcHeightByTsa(width, tsaDecomp.Length);
            if (height >= CSA_BG_EXPORT_HEIGHT_FULL)
                height = CSA_BG_EXPORT_HEIGHT_FULL;
            else
                height = CSA_BG_EXPORT_HEIGHT_SMALL;

            // Decode the tilesheet to RGBA pixels (same as DecodeSheet but only
            // covers the tiles actually needed by the TSA arrangement).
            int bgSheetHeight = CalcTileSheetHeight(BG_SHEET_ROM_WIDTH, bgDecomp.Length);
            if (bgSheetHeight < height) bgSheetHeight = height;
            byte[] bgSheet = DecodeSheet(bgDecomp, bgPalette, BG_SHEET_ROM_WIDTH, bgSheetHeight);

            // Composite via TSA: each TSA entry = u16, bits 0-9 = tile index,
            // bit 10 = hflip, bit 11 = vflip (4bpp GBA tile format).
            byte[] canvas = new byte[width * height * 4];
            byte[] bgColor = GBAPaletteColor0(bgPalette, svc);
            FillBackground(canvas, width, height, bgColor);
            DecodeTSAIntoCanvas(bgSheet, BG_SHEET_ROM_WIDTH, bgSheetHeight,
                tsaDecomp, bgPalette, canvas, width, height);

            var image = svc.CreateImage(width, height);
            image.SetPixelData(canvas);
            return image;
        }

        /// <summary>
        /// Calculate the output height given a tilesheet width and tile-data
        /// byte count (mirrors WF <c>ImageUtil.CalcHeightbyTSA</c>).
        /// Each tile = 32 bytes (4bpp, 8×8); tiles arranged in <paramref name="width"/>÷8
        /// columns.
        /// </summary>
        static int CalcHeightByTsa(int width, int tsaByteLen)
        {
            if (width <= 0) return 8;
            int tilesPerRow = width / TILE_SIZE;
            if (tilesPerRow <= 0) return TILE_SIZE;
            // TSA entry = 2 bytes; number of tile references = tsaByteLen / 2.
            int tileCount = tsaByteLen / 2;
            int rows = (tileCount + tilesPerRow - 1) / tilesPerRow;
            return rows * TILE_SIZE;
        }

        /// <summary>
        /// Calculate the pixel height of a tilesheet from raw tile bytes
        /// (mirrors WF <c>ImageUtil.CalcHeight</c>).
        /// </summary>
        static int CalcTileSheetHeight(int width, int tileDataLen)
        {
            if (width <= 0) return TILE_SIZE;
            int tilesPerRow = width / TILE_SIZE;
            if (tilesPerRow <= 0) return TILE_SIZE;
            int tileCount = tileDataLen / BYTES_PER_TILE_4BPP;
            int rows = (tileCount + tilesPerRow - 1) / tilesPerRow;
            return Math.Max(rows * TILE_SIZE, TILE_SIZE);
        }

        /// <summary>
        /// Composite the BG tilesheet via a plain (non-header) TSA arrangement
        /// into <paramref name="canvas"/>. Mirrors WF
        /// <c>ImageUtil.ByteToImage16Tile(w, h, bgData, 0, pal, 0, tsaData, 0, 0)</c>.
        /// Each TSA entry is a u16: bits 0-9 = tile index (0-based in the
        /// decoded tilesheet), bit 10 = horizontal flip, bit 11 = vertical flip.
        /// </summary>
        static void DecodeTSAIntoCanvas(
            byte[] sheet, int sheetW, int sheetH,
            byte[] tsa, byte[] gbaPalette,
            byte[] canvas, int canvasW, int canvasH)
        {
            int tilesPerRow = canvasW / TILE_SIZE;
            int sheetTilesPerRow = sheetW / TILE_SIZE;
            int tsaCount = tsa.Length / 2;

            for (int t = 0; t < tsaCount; t++)
            {
                int destCol = t % tilesPerRow;
                int destRow = t / tilesPerRow;
                int destX = destCol * TILE_SIZE;
                int destY = destRow * TILE_SIZE;
                if (destX + TILE_SIZE > canvasW) continue;
                if (destY + TILE_SIZE > canvasH) continue;

                ushort entry = (ushort)(tsa[t * 2] | (tsa[t * 2 + 1] << 8));
                int tileIdx = entry & 0x3FF;
                bool hflip  = (entry & 0x0400) != 0;
                bool vflip  = (entry & 0x0800) != 0;

                int srcCol = tileIdx % sheetTilesPerRow;
                int srcRow = tileIdx / sheetTilesPerRow;
                int srcX   = srcCol * TILE_SIZE;
                int srcY   = srcRow * TILE_SIZE;
                if (srcX + TILE_SIZE > sheetW) continue;
                if (srcY + TILE_SIZE > sheetH) continue;

                // Copy one 8×8 tile from sheet RGBA → canvas RGBA, applying flips.
                for (int py = 0; py < TILE_SIZE; py++)
                for (int px = 0; px < TILE_SIZE; px++)
                {
                    int sx = srcX + (hflip ? (TILE_SIZE - 1 - px) : px);
                    int sy = srcY + (vflip ? (TILE_SIZE - 1 - py) : py);
                    int si = (sy * sheetW + sx) * 4;
                    int di = ((destY + py) * canvasW + (destX + px)) * 4;
                    if (si + 3 >= sheet.Length)  continue;
                    if (di + 3 >= canvas.Length) continue;
                    canvas[di]     = sheet[si];
                    canvas[di + 1] = sheet[si + 1];
                    canvas[di + 2] = sheet[si + 2];
                    canvas[di + 3] = sheet[si + 3];
                }
            }
        }

        // -----------------------------------------------------------------------
        // 4bpp tile decode
        // -----------------------------------------------------------------------

        static byte[] DecodeSheet(byte[] tileData, byte[] gbaPalette, int width, int height)
        {
            IImageService svc = CoreState.ImageService;
            byte[] pixels = new byte[width * height * 4];
            int tilesPerRow = width / TILE_SIZE;
            int totalDataTiles = tileData.Length / BYTES_PER_TILE_4BPP;
            int totalSheetTiles = tilesPerRow * (height / TILE_SIZE);
            int tileCount = Math.Min(totalDataTiles, totalSheetTiles);

            for (int t = 0; t < tileCount; t++)
            {
                int tileOff = t * BYTES_PER_TILE_4BPP;
                int tileCol = t % tilesPerRow;
                int tileRow = t / tilesPerRow;

                for (int py = 0; py < TILE_SIZE; py++)
                for (int px = 0; px < TILE_SIZE; px++)
                {
                    int bytePos = tileOff + py * 4 + px / 2;
                    if (bytePos >= tileData.Length) continue;
                    byte b = tileData[bytePos];
                    int ci = (px % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);
                    int palOff = ci * 2;
                    if (palOff + 2 > gbaPalette.Length) continue;
                    ushort gbaColor = (ushort)(gbaPalette[palOff] | (gbaPalette[palOff + 1] << 8));
                    byte r, g, bl;
                    if (svc != null)
                        svc.GBAColorToRGBA(gbaColor, out r, out g, out bl);
                    else
                    {
                        r  = (byte)(((gbaColor >>  0) & 0x1F) * 255 / 31);
                        g  = (byte)(((gbaColor >>  5) & 0x1F) * 255 / 31);
                        bl = (byte)(((gbaColor >> 10) & 0x1F) * 255 / 31);
                    }
                    int sx = tileCol * TILE_SIZE + px;
                    int sy = tileRow * TILE_SIZE + py;
                    int si = (sy * width + sx) * 4;
                    if (si + 3 >= pixels.Length) continue;
                    pixels[si]     = r;
                    pixels[si + 1] = g;
                    pixels[si + 2] = bl;
                    pixels[si + 3] = (byte)(ci == 0 ? 0 : 255);
                }
            }
            return pixels;
        }

        static void BlitSlice(byte[] src, int sliceW, int sliceH,
            byte[] dst, int dstW, int dstX, int dstY)
        {
            for (int y = 0; y < sliceH; y++)
            for (int x = 0; x < sliceW; x++)
            {
                int si = (y * sliceW + x) * 4;
                int di = ((dstY + y) * dstW + (dstX + x)) * 4;
                if (si + 3 >= src.Length) continue;
                if (di + 3 >= dst.Length) continue;
                dst[di]     = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        static byte[] GBAPaletteColor0(byte[] gbaPalette, IImageService svc)
        {
            if (gbaPalette == null || gbaPalette.Length < 2) return new byte[4];
            ushort gbaColor = (ushort)(gbaPalette[0] | (gbaPalette[1] << 8));
            byte r, g, b;
            if (svc != null)
                svc.GBAColorToRGBA(gbaColor, out r, out g, out b);
            else
            {
                r = (byte)(((gbaColor >>  0) & 0x1F) * 255 / 31);
                g = (byte)(((gbaColor >>  5) & 0x1F) * 255 / 31);
                b = (byte)(((gbaColor >> 10) & 0x1F) * 255 / 31);
            }
            return new byte[] { r, g, b, 255 };
        }

        static void FillBackground(byte[] canvas, int w, int h, byte[] color)
        {
            if (color == null || color.Length < 4) return;
            for (int i = 0; i < w * h; i++)
            {
                canvas[i * 4]     = color[0];
                canvas[i * 4 + 1] = color[1];
                canvas[i * 4 + 2] = color[2];
                canvas[i * 4 + 3] = color[3];
            }
        }

        static MagicScriptLine L(MagicScriptLineKind kind, string text)
            => new MagicScriptLine { Kind = kind, Text = text };
    }
}
