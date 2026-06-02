// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform Export helper for the FEditor magic-animation (.txt script
// + per-frame PNG byte arrays). Ported from WF
// `FEBuilderGBA/ImageUtilMagicFEditor.cs` — `Export()` (~lines 537-687),
// `ExportOBjFrameImage()` (~lines 63-162), `ExportBGFrameImage()`
// (~lines 164-231).
//
// This file is read-only (ZERO ROM mutation). The Avalonia view calls
// ExportMagicScript() to get the .txt lines, RenderObjFrameImage() /
// RenderBgFrameImage() to get per-frame PNG bytes, and writes them to disk
// itself.
//
// Import (frame table + per-frame LZ77 images + palettes + OAM) is a
// follow-up tracked by #878 PR2.
//
// Frame layout (mirrors WF comments in DrawFrameImage / ExportOBjFrameImage):
//   +0  frame header u32  (byte[3] == 0x86)
//   +4  objImagePointer   (GBA pointer, LZ77)
//   +8  OAMAbsoStart      (raw u32, relative to objRightToLeftOAM base)
//   +12 OAMBGAbsoStart    (raw u32, relative to objBGRightToLeftOAM base)
//   +16 bgImagePointer    (GBA pointer, LZ77)
//   +20 objPalettePointer (GBA pointer, RAW 0x20 bytes)
//   +24 bgPalettePointer  (GBA pointer, RAW 0x20 bytes)
//
// Frame stride = 28 bytes (4-byte header + 24-byte body); loop advances
// n+=24 inside (then the outer n+=4 makes 28 total) — mirrors WF exactly.
//
// Script commands:
//   0x86 → "O  p- <objPng>" / "B  p- <bgPng>" / "<wait>" (+ optional comments)
//   0x85 → "C<hex24>" (+ optional comment with song/command name)
//   0x80 with [n+1]==0x01 → "~~~" (miss terminator) + continue
//   0x80 plain → "~~~" (terminator) + break
//   unknown → break

using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Per-frame metadata produced by <see cref="MagicEffectExportCore.ScanMagicFrames"/>.
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

        // ---- GBA pointer values (for comment logging + hash) ----
        /// <summary>Raw GBA pointer for objImagePointer (for hash + log).</summary>
        public uint RawObjImagePtr;
        /// <summary>Raw GBA pointer for bgImagePointer (for hash + log).</summary>
        public uint RawBgImagePtr;
        /// <summary>Raw GBA pointer for objPalettePointer (for log).</summary>
        public uint RawObjPalPtr;
        /// <summary>Raw GBA pointer for bgPalettePointer (for log).</summary>
        public uint RawBgPalPtr;
    }

    /// <summary>
    /// Command record for a 0x85 script command (non-frame, non-terminator).
    /// </summary>
    public sealed class MagicCommandMeta
    {
        /// <summary>ROM offset of the 4-byte command record.</summary>
        public uint RecordOffset;
        /// <summary>24-bit command value (bytes 0-2 of the record).</summary>
        public uint Command24;
        /// <summary>True when the low byte of Command24 is 0x48 (sound play).</summary>
        public bool IsSound => (Command24 & 0xFF) == 0x48;
        /// <summary>Music ID (valid only when IsSound==true).</summary>
        public uint MusicId => (Command24 >> 8) & 0xFFFF;
    }

    /// <summary>
    /// Script line types emitted by
    /// <see cref="MagicEffectExportCore.ExportMagicScript"/>.
    /// </summary>
    public enum MagicScriptLineKind
    {
        /// <summary>Header comment line (enableComment only).</summary>
        HeaderComment,
        /// <summary>/// - Start Animation</summary>
        StartMarker,
        /// <summary>O  p- &lt;file&gt;</summary>
        ObjImage,
        /// <summary>B  p- &lt;file&gt;</summary>
        BgImage,
        /// <summary>Wait count (decimal number).</summary>
        Wait,
        /// <summary>C command (0x85).</summary>
        Command,
        /// <summary>~~~ terminator line.</summary>
        Terminator,
        /// <summary>/// - End of animation</summary>
        EndMarker,
    }

    /// <summary>
    /// One emitted script line (text + metadata).
    /// </summary>
    public sealed class MagicScriptLine
    {
        public MagicScriptLineKind Kind;
        public string Text;
        /// <summary>For ObjImage / BgImage lines: index into the per-type image array.</summary>
        public int ImageIndex = -1;
    }

    /// <summary>
    /// Cross-platform, read-only Export helper for the FEditor magic animation
    /// script and per-frame PNG images. Zero ROM mutation.
    ///
    /// Call sequence:
    /// <list type="number">
    ///   <item><see cref="ScanMagicFrames"/> — walk the frame-data stream once.</item>
    ///   <item><see cref="ExportMagicScript"/> — build the .txt lines.</item>
    ///   <item>For each unique frame: <see cref="RenderObjFrameBytes"/> /
    ///     <see cref="RenderBgFrameBytes"/> — render to RGBA byte arrays for PNG
    ///     saving.</item>
    /// </list>
    ///
    /// The Avalonia view (<c>ImageMagicFEditorView</c>) drives these three steps
    /// and writes the .txt + PNG files to disk using
    /// <c>FileDialogHelper.SaveFile</c>.
    /// </summary>
    public static class MagicEffectExportCore
    {
        // ---- OBJ frame export dimensions (mirrors WF ExportOBjFrameImage) ----
        /// <summary>Export OBJ frame width in pixels (480). Mirrors WF SRC_OBJ_SEAT_TILE_WIDTH*8=488? No: 480.</summary>
        public const int OBJ_EXPORT_WIDTH = 480;
        /// <summary>Export OBJ frame height in pixels (160).</summary>
        public const int OBJ_EXPORT_HEIGHT = 160;

        // ---- BG frame export dimensions (mirrors WF ExportBGFrameImage) ----
        /// <summary>Export BG frame width in pixels (264). Mirrors WF SRC_BG_SEAT_TILE_WIDTH*8+8=264.</summary>
        public const int BG_EXPORT_WIDTH = 264;
        /// <summary>Export BG frame height in pixels (64).</summary>
        public const int BG_EXPORT_HEIGHT = 64;

        // OBJ tile-sheet decode width (ROM internal — WF "width = 256").
        const int OBJ_SHEET_ROM_WIDTH = 256;
        // BG tile-sheet decode width (ROM internal — WF "width = 256").
        const int BG_SHEET_ROM_WIDTH = 256;
        const int BG_SHEET_ROM_HEIGHT = 64;

        // Per-tile constants.
        const int TILE_SIZE = 8;
        const int BYTES_PER_TILE_4BPP = 32;

        // Safety limiter: 1 MB per WF.
        const uint SCAN_LIMITER = 1024 * 1024;

        // -----------------------------------------------------------------------
        // ScanMagicFrames
        // -----------------------------------------------------------------------

        /// <summary>
        /// Walk the FEditor magic-animation frame-data stream and return all
        /// 0x86 frame records in order, together with all 0x85 command records.
        /// Mirrors WF <c>ImageUtilMagicFEditor.Export()</c> scan loop
        /// (~lines 569-677) but is read-only and returns the parsed metadata
        /// instead of writing to disk.
        ///
        /// <para>Frame stride = 28 bytes (4-byte header + 24-byte body). The loop
        /// does <c>n += 24</c> INSIDE for each 0x86 record (the outer loop's
        /// <c>n += 4</c> makes the total 28). 0x85 records advance 4 bytes only.
        /// Mirrors WF exactly.</para>
        ///
        /// <para>EOF-safe: we guard <c>n + 4 &lt;= rom.Data.Length</c> before every
        /// command-byte read, and <c>n + 28 &lt;= rom.Data.Length</c> before parsing
        /// a 0x86 record. WF has a latent 3-byte overrun at EOF; we do NOT
        /// reproduce it.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="frameDataAddr">GBA pointer (or raw offset) to the frame-data
        ///   stream. Mirrors WF P0.</param>
        /// <param name="objRightToLeftOAM">GBA pointer to the front OAM table.
        ///   Mirrors WF P4. Only stored in frames; not used during scan.</param>
        /// <param name="objBGRightToLeftOAM">GBA pointer to the back OAM table.
        ///   Mirrors WF P12. Only stored in frames; not used during scan.</param>
        /// <param name="frames">Output: ordered list of 0x86 frame records.</param>
        /// <param name="commands">Output: ordered list of 0x85 command records.</param>
        /// <returns>True on clean termination (0x80 plain stop or limiter);
        ///   false when a mandatory pointer validation fails. The out-params are
        ///   populated regardless.</returns>
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
                // EOF guard: need at least 4 bytes at n.
                if (n + 4 > dataLen) break;

                byte cmd = data[n + 3];

                if (cmd == 0x80)
                {
                    // Guard: [n+1] read
                    bool isContinuation = (n + 2 <= dataLen) && (data[n + 1] == 0x01);
                    termCount++;
                    if (isContinuation && termCount == 1)
                    {
                        // 0x00 0x01 0x00 0x80 — miss terminator, continue
                        continue;
                    }
                    // Plain 0x80 terminator (or second 0x01 0x80 in WF GIF path)
                    break;
                }

                if (cmd == 0x85)
                {
                    // Command record — 4 bytes, no extra advance.
                    uint id24 = U.u24(data, n);
                    commands.Add(new MagicCommandMeta
                    {
                        RecordOffset = n,
                        Command24 = id24,
                    });
                    continue;
                }

                if (cmd != 0x86)
                {
                    // Unknown command → stop.
                    break;
                }

                // ---- 0x86 frame record ----
                if (n + 28 > dataLen) break; // EOF guard for full record

                uint wait = U.u16(data, n);

                // Read the raw pointer values without safety-checking them here.
                // WF Export() also reads raw values and only validates at render time.
                // This matches WF RecycleOldAnime / MakeCheckError scan patterns.
                uint rawObjImg = U.u32(data, n + 4);
                uint oamAbso   = U.u32(data, n + 8);
                uint oamBGAbso = U.u32(data, n + 12);
                uint rawBgImg  = U.u32(data, n + 16);
                uint rawObjPal = U.u32(data, n + 20);
                uint rawBgPal  = U.u32(data, n + 24);

                // Resolve offsets (pointer → offset; raw offset → raw offset).
                uint objImgOff = U.isPointer(rawObjImg) ? U.toOffset(rawObjImg) : rawObjImg;
                uint bgImgOff  = U.isPointer(rawBgImg)  ? U.toOffset(rawBgImg)  : rawBgImg;
                uint objPalOff = U.isPointer(rawObjPal) ? U.toOffset(rawObjPal) : rawObjPal;
                uint bgPalOff  = U.isPointer(rawBgPal)  ? U.toOffset(rawBgPal)  : rawBgPal;

                frames.Add(new MagicFrameMeta
                {
                    RecordOffset    = n,
                    Wait            = wait,
                    ObjImageOffset  = objImgOff,
                    OamAbsoStart    = oamAbso,
                    OamBGAbsoStart  = oamBGAbso,
                    BgImageOffset   = bgImgOff,
                    ObjPaletteOffset = objPalOff,
                    BgPaletteOffset  = bgPalOff,
                    RawObjImagePtr  = rawObjImg,
                    RawBgImagePtr   = rawBgImg,
                    RawObjPalPtr    = rawObjPal,
                    RawBgPalPtr     = rawBgPal,
                });

                n += 24; // advances by 24 here + 4 from loop = 28 total
            }

            return true;
        }

        // -----------------------------------------------------------------------
        // ExportMagicScript
        // -----------------------------------------------------------------------

        /// <summary>
        /// Build the .txt script lines for the FEditor magic animation.
        /// Mirrors WF <c>ImageUtilMagicFEditor.Export()</c> text output
        /// (~lines 555-683) but takes the pre-scanned frame/command lists and
        /// returns the lines as a list of <see cref="MagicScriptLine"/> objects
        /// (no file I/O in Core). The view writes them to disk.
        ///
        /// <para>Image filenames are generated as <c>{basename}o_{index:000}.png</c>
        /// (OBJ) and <c>{basename}b_{index:000}.png</c> (BG), matching WF exactly.
        /// Deduplication: a (rawObjImagePtr, oamAbsoStart) hash picks the existing
        /// slot if the same image was already emitted — exactly WF's
        /// <c>animeHash.IndexOf(imageHash)</c> behaviour.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM (used for comment-dict lookups).</param>
        /// <param name="frames">Ordered 0x86 frame records from
        ///   <see cref="ScanMagicFrames"/>.</param>
        /// <param name="commands">Ordered 0x85 command records from
        ///   <see cref="ScanMagicFrames"/>.</param>
        /// <param name="basename">Filename stem for the generated PNG names
        ///   (e.g. "magic_01_"). A trailing underscore is NOT added here;
        ///   the caller should include it if desired. The "o_" / "b_" prefixes
        ///   are added by this method.</param>
        /// <param name="enableComment">True to emit the WF comment header + inline
        ///   address comments (matching WF <paramref name="enableComment"/>).</param>
        /// <param name="termCount">Number of terminator (~~~) lines actually
        ///   emitted by the scan. Passed from ScanMagicFrames caller; set to the
        ///   number of 0x80 records seen in the stream.</param>
        /// <param name="scanHadContinuation">True when a miss-terminator
        ///   (0x00 0x01 0x00 0x80) was found — mirrors WF termCount==1 branch.</param>
        /// <param name="objImageIndices">Output: for each frame (index i), the
        ///   slot number of its OBJ image (for PNG rendering).</param>
        /// <param name="bgImageIndices">Output: for each frame (index i), the
        ///   slot number of its BG image (for PNG rendering).</param>
        /// <returns>Ordered list of script lines.</returns>
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

            // Deduplication hashes — mirrors WF List<uint> animeHash for OBJ
            // and BG separately.
            var objAnimeHash = new List<uint>();
            var bgAnimeHash = new List<uint>();

            // Build a combined timeline from frames + commands, ordered by ROM
            // record offset so they appear in script order. We reconstruct the
            // scan order: each frame record and command record came from the
            // original stream in order of RecordOffset.
            //
            // The simplest correct approach: merge frames and commands by their
            // RecordOffset, then emit lines in that order. (WF interleaves them
            // in a single loop.)
            int fi = 0;  // frame pointer
            int ci = 0;  // command pointer

            // Miss-terminator emitted BEFORE the frames (matches WF: termCount==1
            // branch inside the loop inserts ~~~ and continues before frames).
            if (scanHadContinuation)
            {
                string line = "~~~";
                if (enableComment)
                    line += "                               #miss terminator";
                lines.Add(L(MagicScriptLineKind.Terminator, line));
            }

            while (fi < frames.Count || ci < commands.Count)
            {
                // Pick the item with the smaller RecordOffset.
                bool pickFrame;
                if (fi >= frames.Count) pickFrame = false;
                else if (ci >= commands.Count) pickFrame = true;
                else pickFrame = frames[fi].RecordOffset < commands[ci].RecordOffset;

                if (!pickFrame)
                {
                    // Emit command line (0x85).
                    var cmd = commands[ci++];
                    string cmdLine;
                    if (cmd.IsSound)
                    {
                        cmdLine = "C" + U.ToHexString(cmd.Command24);
                        if (enableComment)
                            cmdLine += "                               #Sound " + cmd.MusicId;
                    }
                    else
                    {
                        cmdLine = "C" + U.ToHexString(cmd.Command24);
                        if (enableComment)
                        {
                            // WF: U.at(Comment_85command_Dic, frameData[n]) — the command dict
                            // is WF-side only; in Core we emit the hex as the comment.
                            cmdLine += "                               #cmd 0x"
                                + (cmd.Command24 & 0xFF).ToString("X2");
                        }
                    }
                    lines.Add(L(MagicScriptLineKind.Command, cmdLine));
                }
                else
                {
                    // Emit frame lines (0x86): OBJ, BG, wait.
                    var frame = frames[fi++];

                    // ---- OBJ image ----
                    string objBaseName = basename + "o_";
                    uint objHash = (frame.RawObjImagePtr << 8) | (frame.OamAbsoStart & 0xFF);
                    int objSlot = objAnimeHash.IndexOf(objHash);
                    if (objSlot < 0)
                    {
                        objSlot = objAnimeHash.Count;
                        objAnimeHash.Add(objHash);
                    }
                    string objFilename = objBaseName + objSlot.ToString("000") + ".png";
                    objImageIndices.Add(objSlot);
                    lines.Add(new MagicScriptLine
                    {
                        Kind = MagicScriptLineKind.ObjImage,
                        Text = "O  p- " + objFilename,
                        ImageIndex = objSlot,
                    });

                    // ---- BG image ----
                    string bgBaseName = basename + "b_";
                    uint bgHash = frame.RawBgImagePtr;
                    int bgSlot = bgAnimeHash.IndexOf(bgHash);
                    if (bgSlot < 0)
                    {
                        bgSlot = bgAnimeHash.Count;
                        bgAnimeHash.Add(bgHash);
                    }
                    string bgFilename = bgBaseName + bgSlot.ToString("000") + ".png";
                    bgImageIndices.Add(bgSlot);
                    lines.Add(new MagicScriptLine
                    {
                        Kind = MagicScriptLineKind.BgImage,
                        Text = "B  p- " + bgFilename,
                        ImageIndex = bgSlot,
                    });

                    // ---- Wait ----
                    lines.Add(L(MagicScriptLineKind.Wait, frame.Wait.ToString()));
                }
            }

            // WF does NOT emit a trailing ~~~ (the commented-out line at 679
            // confirms this). Only the inline ~~~ lines inside the loop are
            // emitted (see scanHadContinuation above + the second terminator
            // branch in the WF loop — WF GIF path only; Export path breaks
            // before the second 0x80).
            lines.Add(L(MagicScriptLineKind.EndMarker, "/// - End of animation"));

            return lines;
        }

        // -----------------------------------------------------------------------
        // RenderObjFrameBytes / RenderBgFrameBytes
        // -----------------------------------------------------------------------

        /// <summary>
        /// Render the OBJ frame image for slot <paramref name="slotIndex"/> and
        /// return it as an <see cref="IImage"/> (480×160). Returns null when:
        /// <list type="bullet">
        ///   <item>No magic system detected (FE-gate).</item>
        ///   <item>The frame slot index is out of range.</item>
        ///   <item>Any pointer / LZ77 read fails.</item>
        /// </list>
        /// Mirrors WF <c>ExportOBjFrameImage()</c> (480×160 canvas;
        /// OAM-draw into left and right halves).
        ///
        /// <para>The caller passes the full <paramref name="frames"/> list and
        /// deduplication arrays so the render is slot-indexed (multiple frames
        /// sharing the same image are only rendered once).</para>
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

            // Find first frame with this slot (same as WF's seatnumber).
            // OBJ hash = (rawObjImagePtr << 8) | (oamAbsoStart & 0xFF)
            // Slot 0 = first unique hash encountered in the frame list.
            var objAnimeHash = new List<uint>();
            MagicFrameMeta target = null;
            foreach (var f in frames)
            {
                uint hash = (f.RawObjImagePtr << 8) | (f.OamAbsoStart & 0xFF);
                int s = objAnimeHash.IndexOf(hash);
                if (s < 0)
                {
                    s = objAnimeHash.Count;
                    objAnimeHash.Add(hash);
                    if (s == slotIndex)
                    {
                        target = f;
                        break;
                    }
                }
                else if (s == slotIndex)
                {
                    // This hash was already registered as slot s; we need the frame
                    // that FIRST registered this slot.
                    break;
                }
            }
            if (target == null) return null;

            return RenderObjFrameInternal(rom, target, objRightToLeftOAM, objBGRightToLeftOAM);
        }

        /// <summary>
        /// Render the BG frame image for slot <paramref name="slotIndex"/> and
        /// return it as an <see cref="IImage"/> (264×64). Returns null on error.
        /// Mirrors WF <c>ExportBGFrameImage()</c>.
        /// </summary>
        public static IImage RenderBgFrameSlot(
            ROM rom,
            List<MagicFrameMeta> frames,
            int slotIndex)
        {
            if (rom == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            if (frames == null || slotIndex < 0) return null;

            var bgAnimeHash = new List<uint>();
            MagicFrameMeta target = null;
            foreach (var f in frames)
            {
                uint hash = f.RawBgImagePtr;
                int s = bgAnimeHash.IndexOf(hash);
                if (s < 0)
                {
                    s = bgAnimeHash.Count;
                    bgAnimeHash.Add(hash);
                    if (s == slotIndex)
                    {
                        target = f;
                        break;
                    }
                }
            }
            if (target == null) return null;

            return RenderBgFrameInternal(rom, target);
        }

        // -----------------------------------------------------------------------
        // Convenience: how many unique OBJ / BG image slots are in a frame list
        // -----------------------------------------------------------------------

        /// <summary>
        /// Count the distinct OBJ image slots in the frame list (same as the
        /// number of unique PNG files the exporter would write for OBJ images).
        /// </summary>
        public static int CountUniqueObjSlots(List<MagicFrameMeta> frames)
        {
            if (frames == null) return 0;
            var hash = new List<uint>();
            foreach (var f in frames)
            {
                uint h = (f.RawObjImagePtr << 8) | (f.OamAbsoStart & 0xFF);
                if (!hash.Contains(h)) hash.Add(h);
            }
            return hash.Count;
        }

        /// <summary>
        /// Count the distinct BG image slots in the frame list.
        /// </summary>
        public static int CountUniqueBgSlots(List<MagicFrameMeta> frames)
        {
            if (frames == null) return 0;
            var hash = new List<uint>();
            foreach (var f in frames)
            {
                uint h = f.RawBgImagePtr;
                if (!hash.Contains(h)) hash.Add(h);
            }
            return hash.Count;
        }

        // -----------------------------------------------------------------------
        // Internal OBJ frame renderer (480×160)
        // -----------------------------------------------------------------------

        // Mirrors WF ExportOBjFrameImage:
        //   Blank(480, 160), draw OAM at objRightToLeftOAM + OAMAbsoStart →
        //   BitBlt(retImage, 0, 0, ...), draw OAM at objBGRightToLeftOAM +
        //   OAMBGAbsoStart → BitBlt(retImage, 240, 0, ...).
        static IImage RenderObjFrameInternal(
            ROM rom,
            MagicFrameMeta frame,
            uint objRightToLeftOAM,
            uint objBGRightToLeftOAM)
        {
            IImageService svc = CoreState.ImageService;

            // Resolve OAM base offsets.
            uint objOAMBase = U.isSafetyPointer(objRightToLeftOAM)
                ? U.toOffset(objRightToLeftOAM) : objRightToLeftOAM;
            uint bgOAMBase = U.isSafetyPointer(objBGRightToLeftOAM)
                ? U.toOffset(objBGRightToLeftOAM) : objBGRightToLeftOAM;

            // OBJ palette — RAW 0x20 bytes at +20.
            if (frame.ObjPaletteOffset + 0x20 > (uint)rom.Data.Length) return null;
            byte[] objPalette = rom.getBinaryData(frame.ObjPaletteOffset, 0x20);

            // LZ77-decompress OBJ tiles (+4).
            if (!U.isSafetyOffset(frame.ObjImageOffset, rom)) return null;
            byte[] objDecomp = LZ77.decompress(rom.Data, frame.ObjImageOffset);
            if (objDecomp == null || objDecomp.Length == 0)
            {
                // WF creates a dummy 0x1000-byte array on decompress failure.
                Log.Error("MagicEffectExportCore: obj LZ77 decompress failed at",
                    "0x" + frame.ObjImageOffset.ToString("X08"));
                objDecomp = new byte[0x1000];
            }

            // OBJ sheet: 256 × CalcHeight(256, objLen).
            int objHeight = MagicEffectRendererCore.CalcHeight(OBJ_SHEET_ROM_WIDTH, objDecomp.Length);
            if (objHeight < 64) objHeight = 64;
            byte[] objSheet = DecodeSheet(objDecomp, objPalette, OBJ_SHEET_ROM_WIDTH, objHeight);

            // Canvas: 480×160, filled transparent.
            byte[] canvas = new byte[OBJ_EXPORT_WIDTH * OBJ_EXPORT_HEIGHT * 4];

            // WF uses SEAT_TILE_WIDTH/HEIGHT for the tempCanvas, but we render
            // directly. The WF structure:
            //   tempCanvas = Blank((SEAT_TILE_WIDTH-2)*8, SEAT_TILE_HEIGHT*8*2, obj)
            //   oam.Draw(tempCanvas, obj)         → draws into tempCanvas
            //   BitBlt(retImage, 0, 0, ...)       → front OAM at left half
            //   and again for BG at right half (offset 240).
            //
            // We use BattleAnimeRendererCore.DrawOAMSprites which draws directly
            // into the destination pixel buffer. We call it twice with different
            // dst X offsets by using two destination pixel arrays and compositing.
            // For simplicity: render into the full 480×160 canvas by splitting.

            // Front OAM — left half (0, 0) — uses objRightToLeftOAM + OAMAbsoStart.
            {
                uint oamStart = objOAMBase + frame.OamAbsoStart;
                // Render into a 240×160 slice.
                byte[] slice = new byte[240 * OBJ_EXPORT_HEIGHT * 4];
                BattleAnimeRendererCore.DrawOAMSprites(
                    rom.Data, oamStart,
                    objSheet, OBJ_SHEET_ROM_WIDTH, objHeight,
                    slice, 240, OBJ_EXPORT_HEIGHT,
                    isMagicOAM: true);
                BlitSlice(slice, 240, OBJ_EXPORT_HEIGHT, canvas, OBJ_EXPORT_WIDTH, 0, 0);
            }

            // Back OAM — right half (240, 0) — uses objBGRightToLeftOAM + OAMBGAbsoStart.
            {
                uint oamStart = bgOAMBase + frame.OamBGAbsoStart;
                byte[] slice = new byte[240 * OBJ_EXPORT_HEIGHT * 4];
                BattleAnimeRendererCore.DrawOAMSprites(
                    rom.Data, oamStart,
                    objSheet, OBJ_SHEET_ROM_WIDTH, objHeight,
                    slice, 240, OBJ_EXPORT_HEIGHT,
                    isMagicOAM: true);
                BlitSlice(slice, 240, OBJ_EXPORT_HEIGHT, canvas, OBJ_EXPORT_WIDTH, 240, 0);
            }

            var image = svc.CreateImage(OBJ_EXPORT_WIDTH, OBJ_EXPORT_HEIGHT);
            image.SetPixelData(canvas);
            return image;
        }

        // -----------------------------------------------------------------------
        // Internal BG frame renderer (264×64)
        // -----------------------------------------------------------------------

        // Mirrors WF ExportBGFrameImage:
        //   width=256, height=64, Bitmap bg = ByteToImage16Tile(...)
        //   Blank(264, 64, bgPalette, 0)
        //   BitBlt(retImage, 0, 0, bg.Width, bg.Height, bg, 0, 0)
        static IImage RenderBgFrameInternal(ROM rom, MagicFrameMeta frame)
        {
            IImageService svc = CoreState.ImageService;

            // BG palette — RAW 0x20 bytes at +24.
            if (frame.BgPaletteOffset + 0x20 > (uint)rom.Data.Length) return null;
            byte[] bgPalette = rom.getBinaryData(frame.BgPaletteOffset, 0x20);

            // LZ77-decompress BG tiles (+16).
            if (!U.isSafetyOffset(frame.BgImageOffset, rom)) return null;
            byte[] bgDecomp = LZ77.decompress(rom.Data, frame.BgImageOffset);
            if (bgDecomp == null || bgDecomp.Length == 0)
            {
                Log.Error("MagicEffectExportCore: bg LZ77 decompress failed at",
                    "0x" + frame.BgImageOffset.ToString("X08"));
                return null;
            }

            // BG sheet: 256×64 (WF: width=256, height=64).
            byte[] bgSheet = DecodeSheet(bgDecomp, bgPalette, BG_SHEET_ROM_WIDTH, BG_SHEET_ROM_HEIGHT);

            // Canvas: 264×64 — copy 256×64 BG into left portion, right 8px empty
            // (matches WF BitBlt(retImage, 0, 0, bg.Width, bg.Height, bg, 0, 0)).
            byte[] canvas = new byte[BG_EXPORT_WIDTH * BG_EXPORT_HEIGHT * 4];

            // Fill canvas background with palette color 0 (transparent/black).
            byte[] bgColor = GBAPaletteColor0(bgPalette, svc);
            FillBackground(canvas, BG_EXPORT_WIDTH, BG_EXPORT_HEIGHT, bgColor);

            // Blit the 256×64 sheet into the left portion of the 264×64 canvas.
            BlitSlice(bgSheet, BG_SHEET_ROM_WIDTH, BG_SHEET_ROM_HEIGHT,
                canvas, BG_EXPORT_WIDTH, 0, 0);

            var image = svc.CreateImage(BG_EXPORT_WIDTH, BG_EXPORT_HEIGHT);
            image.SetPixelData(canvas);
            return image;
        }

        // -----------------------------------------------------------------------
        // Shared 4bpp tile decode
        // -----------------------------------------------------------------------

        static byte[] DecodeSheet(byte[] tileData, byte[] gbaPalette, int width, int height)
        {
            IImageService svc = CoreState.ImageService;
            byte[] pixels = new byte[width * height * 4];

            int tilesPerRow = width / TILE_SIZE;
            int tilesPerCol = height / TILE_SIZE;
            int totalSheetTiles = tilesPerRow * tilesPerCol;
            int totalDataTiles = tileData.Length / BYTES_PER_TILE_4BPP;
            int tileCount = Math.Min(totalDataTiles, totalSheetTiles);

            for (int t = 0; t < tileCount; t++)
            {
                int tileOff = t * BYTES_PER_TILE_4BPP;
                int tileCol = t % tilesPerRow;
                int tileRow = t / tilesPerRow;

                for (int py = 0; py < TILE_SIZE; py++)
                {
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
                        {
                            svc.GBAColorToRGBA(gbaColor, out r, out g, out bl);
                        }
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

                        pixels[si + 0] = r;
                        pixels[si + 1] = g;
                        pixels[si + 2] = bl;
                        pixels[si + 3] = (byte)(ci == 0 ? 0 : 255);
                    }
                }
            }

            return pixels;
        }

        // Copy a (sliceW × sliceH) RGBA source into (dstX, dstY) of a wider canvas.
        static void BlitSlice(
            byte[] src, int sliceW, int sliceH,
            byte[] dst, int dstW,
            int dstX, int dstY)
        {
            for (int y = 0; y < sliceH; y++)
            {
                for (int x = 0; x < sliceW; x++)
                {
                    int si = (y * sliceW + x) * 4;
                    int di = ((dstY + y) * dstW + (dstX + x)) * 4;
                    if (si + 3 >= src.Length) continue;
                    if (di + 3 >= dst.Length) continue;
                    dst[di + 0] = src[si + 0];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
        }

        static byte[] GBAPaletteColor0(byte[] gbaPalette, IImageService svc)
        {
            if (gbaPalette == null || gbaPalette.Length < 2) return new byte[4];
            ushort gbaColor = (ushort)(gbaPalette[0] | (gbaPalette[1] << 8));
            byte r, g, b;
            if (svc != null)
            {
                svc.GBAColorToRGBA(gbaColor, out r, out g, out b);
            }
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
                canvas[i * 4 + 0] = color[0];
                canvas[i * 4 + 1] = color[1];
                canvas[i * 4 + 2] = color[2];
                canvas[i * 4 + 3] = color[3];
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        static MagicScriptLine L(MagicScriptLineKind kind, string text)
            => new MagicScriptLine { Kind = kind, Text = text };
    }
}
