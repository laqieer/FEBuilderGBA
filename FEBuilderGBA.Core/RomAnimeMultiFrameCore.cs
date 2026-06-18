// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform multi-frame animation-script (.txt) + animated-GIF subsystem for
// the In-ROM Magic Animation editor (#1230 — follow-up to #1176). Ports the
// DEFERRED multi-frame workflow of WinForms ImageRomAnimeForm.cs:
//   * Export (L879)    -> ExportTxt   : a `wait png` script that dumps every frame
//   * ExportGif (L805) -> ExportGif   : the same frame walk emitted as an animated GIF
//   * Import (L989)    -> ImportTxt   : parse the `wait png` script and rebuild the
//                                        WHOLE animation (frame table + ALL pointer
//                                        arrays) as ONE atomic transaction
//   * MakeRecycleList (L457)          : a Core port that pools the OLD regions for reuse
//
// The single-frame slice (RomAnimeCore: list / preview / per-frame PNG import &
// export) is unchanged; this file adds the whole-animation rewrite on top of it.
//
// === Storage shapes (ported from RomAnimeCore.Resolve) ===
//   * FRAME-ptr is a real ROM offset (IsFrameTable) -> a {u16 id, u16 wait} frame
//     table terminated by 0xFFFF; TSA/IMAGE/PAL each point to a per-id pointer-LIST.
//   * FRAME-ptr < 0x100 (FIXEDCOUNT)               -> a fixed frame count; no frame
//     table; TSA/IMAGE/PAL are per-frame pointer-lists (or a single COMMONPALETTE).
//
// === The four WF import modes (faithfully reproduced) ===
//   FIXEDCOUNT  + multi-palette  : per-frame image/tsa/pal lists; frame count must match.
//   FIXEDCOUNT  + palette-anime  : ONE image+tsa, a per-frame PALETTE list (TSAList==1
//                                  && PaletteList>=2). Only frame 0's image is written.
//   frame-table + COMMONPALETTE  : per-frame image/tsa lists, a SINGLE shared palette
//                                  (TSAList>=2 && PaletteList==1). Frame 0's palette wins.
//   frame-table + multi-palette  : per-frame image/tsa/pal lists + the {id,wait} table.
//
// === Atomicity (the #1230 correctness bar) ===
// ImportTxt validates EVERYTHING first (parse every line, load+quantize+encode+LZ77
// every unique image, verify the FIXEDCOUNT count) with ZERO ROM mutation. It then
// takes a `byte[] snap = (byte[])rom.Data.Clone()` immediately before the first write,
// pools the OLD regions via MakeRecycleList, and performs ALL writes through the
// RecycleAddress AMBIENT overloads so they land in the View's `ROM.BeginUndoScope`
// ambient undo (the same mechanism RomAnimeCore.ImportFrame and the sibling cores use).
// On ANY failure (a write returns U.NOT_FOUND, or an exception) it RestoreSnapshot()s
// the ROM byte-identical and returns an error — a FAILED import mutates ZERO bytes.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform multi-frame export/import + animated-GIF export for the
    /// In-ROM Magic Animation editor (#1230). GUI-free; every ROM read is bounds-
    /// guarded, the export methods are read-only, and <see cref="ImportTxt"/> is an
    /// atomic transaction with a byte-identical fault restore.
    /// </summary>
    public static class RomAnimeMultiFrameCore
    {
        // WF renders/encodes every frame at a fixed 16-tile (128 px) tall canvas.
        const int FRAME_HEIGHT = 8 * 16;
        // RAW 16-color GBA palette = 16 * 2 bytes.
        const int PALETTE_BYTES = 16 * 2;
        // WF limiter for the uncompressed frame table (1 MB search ceiling).
        const uint FRAME_TABLE_LIMIT = 1024 * 1024;

        // ----------------------------------------------------------------
        // ExportTxt — dump every frame as PNGs + a `wait png` script.
        // ----------------------------------------------------------------

        /// <summary>
        /// Export the whole animation as a <c>wait png</c> script plus per-unique-frame
        /// PNGs alongside it (WF <c>Export</c>, L879). For a frame-table entry each
        /// line is <c>&lt;wait&gt; &lt;png&gt;</c> walking the <c>{id,wait}</c> table
        /// (a repeated id reuses its PNG); for a FIXEDCOUNT entry each of the
        /// <c>FramePointer</c> frames is dumped with <c>wait == 1</c>. Read-only.
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="e">The resolved entry (from <see cref="RomAnimeCore.Resolve"/>).</param>
        /// <param name="scriptPath">Output .txt path (PNGs land in the same directory).</param>
        /// <returns>Empty string on success; a user-facing error otherwise. Never throws.</returns>
        public static string ExportTxt(ROM rom, RomAnimeCore.RomAnimeEntry e, string scriptPath)
        {
            if (rom == null || rom.Data == null) return R.Error("ROM not loaded.");
            if (e == null) return R.Error("No animation entry selected.");
            if (string.IsNullOrEmpty(scriptPath)) return R.Error("Output path is empty.");
            if (CoreState.ImageService == null) return R.Error("Image service is not configured.");

            string baseName = Path.GetFileNameWithoutExtension(scriptPath) + "_";
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? "";
            try
            {
                if (baseDir.Length > 0 && !Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
            }
            catch (Exception ex) { return R.Error("Failed to create output directory: {0}", ex.Message); }

            var lines = new List<string>();

            // WF advisory comments (L886/L892): warn when the storage shape forces a
            // shared palette / palette-animation interpretation on re-import.
            if (e.TSAList.Count >= 2 && e.PaletteList.Count == 1)
                lines.Add("//" + R._("A common palette is required. Only the first image's palette is used; later frames reuse it."));

            try
            {
                if (!e.IsFrameTable)
                {
                    if (e.TSAList.Count == 1 && e.PaletteList.Count >= 2)
                        lines.Add("//" + R._("This is a palette animation. Only the first image is used; later frames change only the palette."));
                    lines.Add("//" + R._("This animation has a fixed frame count. The frame count must be {0} ({1}).",
                        U.To0xHexString(e.FramePointer), e.FramePointer));

                    int count = (int)e.FramePointer;
                    for (int i = 0; i < count; i++)
                    {
                        string png = baseName.Replace(" ", "_") + "g" + i.ToString("000", CultureInfo.InvariantCulture) + ".png";
                        string werr = RenderAndSave(rom, e, i, Path.Combine(baseDir, png));
                        if (werr.Length > 0) return werr;
                        lines.Add("1 " + png);
                    }
                }
                else
                {
                    // Frame-table walk: dedup PNG output per frame id (WF animeHash).
                    var saved = new HashSet<uint>();
                    uint addr = rom.p32(e.FramePointer);
                    if (!U.isSafetyOffset(addr, rom)) return R.Error("The animation frame table pointer is invalid.");

                    uint limit = (uint)Math.Min((ulong)addr + FRAME_TABLE_LIMIT, (ulong)rom.Data.Length);
                    for (; addr + 4 <= rom.Data.Length && addr < limit; addr += 4)
                    {
                        uint id = rom.u16(addr);
                        uint wait = rom.u16(addr + 2);
                        if (id == 0xFFFF) break;

                        string png = baseName.Replace(" ", "_") + "g" + id.ToString("000", CultureInfo.InvariantCulture) + ".png";
                        if (saved.Add(id))
                        {
                            string werr = RenderAndSaveById(rom, e, (int)id, Path.Combine(baseDir, png));
                            if (werr.Length > 0) return werr;
                        }
                        lines.Add(wait.ToString(CultureInfo.InvariantCulture) + " " + png);
                    }
                }

                File.WriteAllLines(scriptPath, lines);
            }
            catch (Exception ex)
            {
                return R.Error("Failed to export animation script: {0}", ex.Message);
            }
            return "";
        }

        // ----------------------------------------------------------------
        // ExportGif — walk the frame table and emit an animated GIF.
        // ----------------------------------------------------------------

        /// <summary>
        /// Export the whole animation as an animated GIF (WF <c>ExportGif</c>, L805):
        /// walk the frame table (or the fixed count), render each frame, and encode via
        /// <see cref="GifEncoderCore"/>. The per-frame GIF delay uses
        /// <see cref="U.GameFrameSecToGifFrameSec"/> so the rounding matches WF. Read-only.
        /// </summary>
        public static string ExportGif(ROM rom, RomAnimeCore.RomAnimeEntry e, string gifPath)
        {
            if (rom == null || rom.Data == null) return R.Error("ROM not loaded.");
            if (e == null) return R.Error("No animation entry selected.");
            if (string.IsNullOrEmpty(gifPath)) return R.Error("Output path is empty.");
            if (CoreState.ImageService == null) return R.Error("Image service is not configured.");

            var frames = new List<GifEncoderCore.GifFrame>();
            try
            {
                if (!e.IsFrameTable)
                {
                    int count = (int)e.FramePointer;
                    for (int i = 0; i < count; i++)
                    {
                        GifEncoderCore.GifFrame f = RenderGifFrame(rom, e, i, 1);
                        if (f != null) frames.Add(f);
                    }
                }
                else
                {
                    var cache = new Dictionary<uint, GifEncoderCore.GifFrame>();
                    uint addr = rom.p32(e.FramePointer);
                    if (!U.isSafetyOffset(addr, rom)) return R.Error("The animation frame table pointer is invalid.");

                    uint limit = (uint)Math.Min((ulong)addr + FRAME_TABLE_LIMIT, (ulong)rom.Data.Length);
                    for (; addr + 4 <= rom.Data.Length && addr < limit; addr += 4)
                    {
                        uint id = rom.u16(addr);
                        uint wait = rom.u16(addr + 2);
                        if (id == 0xFFFF) break;

                        if (!cache.TryGetValue(id, out GifEncoderCore.GifFrame f))
                        {
                            f = RenderGifFrameById(rom, e, (int)id, wait);
                            cache[id] = f;
                        }
                        if (f != null)
                        {
                            // Re-emit with THIS frame's wait (cache holds the rendered pixels).
                            frames.Add(new GifEncoderCore.GifFrame
                            {
                                Width = f.Width,
                                Height = f.Height,
                                RgbaPixels = f.RgbaPixels,
                                DelayCs = U.GameFrameSecToGifFrameSec(wait),
                            });
                        }
                    }
                }

                if (frames.Count == 0) return R.Error("No renderable frames in animation.");

                string outDir = Path.GetDirectoryName(Path.GetFullPath(gifPath)) ?? "";
                if (outDir.Length > 0 && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                GifEncoderCore.Encode(frames, gifPath);
            }
            catch (Exception ex)
            {
                return R.Error("Failed to export animation GIF: {0}", ex.Message);
            }
            return "";
        }

        // ----------------------------------------------------------------
        // ImportTxt — parse the script and rebuild the WHOLE animation atomically.
        // ----------------------------------------------------------------

        /// <summary>One parsed unique frame image (the WF <c>anime</c> record).</summary>
        public sealed class FrameImage
        {
            /// <summary>LZ77-compressed 4bpp tile data.</summary>
            public byte[] Image;
            /// <summary>LZ77-compressed TSA map.</summary>
            public byte[] TSA;
            /// <summary>RAW 16-color GBA palette (32 bytes).</summary>
            public byte[] Palette;
            /// <summary>The source PNG filename (dedup key, mirrors WF <c>filename</c>).</summary>
            public string Name;
        }

        /// <summary>
        /// Import a multi-frame <c>wait png</c> script and rebuild the WHOLE animation
        /// (WF <c>Import</c>, L989) as ONE atomic transaction. Parses every line, loads
        /// + quantizes + encodes + LZ77-compresses every UNIQUE image (ZERO ROM mutation),
        /// builds the <c>{id,wait}</c> frame table, then — only after every region is
        /// ready — pools the old regions (<see cref="MakeRecycleList"/>) and rewrites the
        /// frame table + ALL pointer arrays through the RecycleAddress AMBIENT overloads
        /// (so the active <see cref="ROM.BeginUndoScope"/> captures each write once). On
        /// ANY failure the ROM is restored byte-identical (snapshot + length-aware copy)
        /// and an error is returned — a FAILED import mutates ZERO bytes.
        ///
        /// <para>The <paramref name="frameLoader"/> turns a resolved PNG path into a
        /// quantized <c>(indexedPixels, gbaPalette16, width, height)</c> tuple (the View
        /// wires it to <c>ImageImportService.LoadAndQuantizeFromFile</c>, exactly like the
        /// per-frame import) or returns null when the file is missing / unreadable. Core
        /// crops/pads the indexed pixels to <c>(ImageWidthTiles*8, 128)</c> — the WF
        /// <c>ImageUtil.Copy(bitmap, 0, 0, imageWidth, imageHeight)</c> step.</para>
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="e">The resolved entry (from <see cref="RomAnimeCore.Resolve"/>).</param>
        /// <param name="scriptPath">Input <c>wait png</c> script.</param>
        /// <param name="frameLoader">PNG path -&gt; <c>(indexed, gbaPalette16, w, h)</c> or null.</param>
        /// <param name="error">Empty on success; a user-facing message on failure.</param>
        /// <returns>true on success; false (with <paramref name="error"/> set and ZERO ROM
        /// mutation) on any validation or write failure. Never throws.</returns>
        public static bool ImportTxt(ROM rom, RomAnimeCore.RomAnimeEntry e, string scriptPath,
            Func<string, (byte[] indexedPixels, byte[] gbaPalette16, int width, int height)?> frameLoader,
            out string error)
        {
            error = "";
            if (rom == null || rom.RomInfo == null || rom.Data == null)
            { error = R.Error("ROM not loaded."); return false; }
            if (e == null)
            { error = R.Error("No animation entry selected."); return false; }
            if (frameLoader == null)
            { error = R.Error("Invalid image data."); return false; }
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            { error = R.Error("Script file not found: {0}", scriptPath ?? ""); return false; }

            int imageWidth = e.ImageWidthTiles * 8;
            if (imageWidth <= 0) { error = R.Error("The animation frame width is invalid."); return false; }

            string baseDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? "";
            string[] lines;
            try { lines = File.ReadAllLines(scriptPath); }
            catch (Exception ex) { error = R.Error("Failed to read script file: {0}", ex.Message); return false; }

            // ---- PHASE 1: parse + encode EVERY unique image. ZERO ROM mutation. ----
            var animeList = new List<FrameImage>();
            var frames = new List<byte>(); // {u16 id, u16 wait} table being built
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                if (line == null) continue;
                if (U.IsComment(line) || (rom.RomInfo != null && U.OtherLangLine(line, rom))) continue;
                line = U.ClipComment(line);
                if (line.Length <= 0) continue;

                line = line.Replace("\t", " ");
                string[] sp = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length < 2) continue;
                string command = sp[0];
                if (command.Length <= 0 || !U.isNumString(command)) continue;

                uint wait = U.atoi(command);
                string imageName = sp[1];

                uint id = FindImage(animeList, imageName);
                if (id == U.NOT_FOUND)
                {
                    id = (uint)animeList.Count;
                    string werr = EncodeFrame(baseDir, imageName, imageWidth, frameLoader, li + 1, out FrameImage a);
                    if (werr.Length > 0) { error = werr; return false; }
                    animeList.Add(a);
                }

                U.append_u16(frames, (ushort)id);
                U.append_u16(frames, (ushort)wait);
            }
            // Terminator (WF L1065): {0xFFFF, 0x0000}.
            U.append_u16(frames, 0xFFFF);
            U.append_u16(frames, 0x0000);

            if (animeList.Count <= 0)
            { error = R.Error("There is no animation to write."); return false; }

            // FIXEDCOUNT shape: the parsed frame count MUST equal the fixed count.
            if (!e.IsFrameTable && e.FramePointer != animeList.Count)
            {
                error = R.Error(
                    "This is a fixed-count animation requiring {0} frames, but the script defines {1}.",
                    e.FramePointer, animeList.Count);
                return false;
            }

            // ---- PHASE 2: snapshot, pool old regions, rewrite atomically. ----
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                var recycle = new List<Address>();
                MakeRecycleList(rom, e, recycle);
                var ra = new RecycleAddress(recycle);

                bool ok;
                if (!e.IsFrameTable)
                {
                    if (e.TSAList.Count == 1 && e.PaletteList.Count >= 2)
                        ok = WriteFixedPaletteAnime(rom, e, ra, animeList);
                    else
                        ok = WriteFixedMultiPalette(rom, e, ra, animeList);
                }
                else
                {
                    if (e.TSAList.Count >= 2 && e.PaletteList.Count == 1)
                        ok = WriteFrameTableCommonPalette(rom, e, ra, animeList, frames.ToArray());
                    else
                        ok = WriteFrameTableMultiPalette(rom, e, ra, animeList, frames.ToArray());
                }

                if (!ok)
                {
                    RestoreSnapshot(rom, snap);
                    error = R._("Failed to write animation. Check ROM free space.");
                    return false;
                }

                // Free-list any leftover recycle ranges (WF ra.BlackOut).
                ra.BlackOutAmbient();
                return true;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                error = R.Error("Animation import failed: {0}", ex.Message);
                return false;
            }
        }

        // -- The four WF write modes (each returns false on the first NOT_FOUND). --

        // FIXEDCOUNT, palette-animation: ONE image+tsa (frame 0), a per-frame PAL list.
        static bool WriteFixedPaletteAnime(ROM rom, RomAnimeCore.RomAnimeEntry e,
            RecycleAddress ra, List<FrameImage> list)
        {
            var palList = new List<byte>();
            foreach (FrameImage a in list)
            {
                uint p = ra.WriteAmbient(a.Palette);
                if (p == U.NOT_FOUND) return false;
                U.append_u32(palList, U.toPointer(p));
            }
            if (ra.WriteAndWritePointerAmbient(e.TSAPointer, list[0].TSA) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.ImagePointer, list[0].Image) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.PalettePointer, palList.ToArray()) == U.NOT_FOUND) return false;
            return true;
        }

        // FIXEDCOUNT, multi-palette: per-frame image/tsa/pal lists.
        static bool WriteFixedMultiPalette(ROM rom, RomAnimeCore.RomAnimeEntry e,
            RecycleAddress ra, List<FrameImage> list)
        {
            var imageList = new List<byte>();
            var tsaList = new List<byte>();
            var palList = new List<byte>();
            foreach (FrameImage a in list)
            {
                uint pi = ra.WriteAmbient(a.Image);
                if (pi == U.NOT_FOUND) return false;
                U.append_u32(imageList, U.toPointer(pi));

                uint pt = ra.WriteAmbient(a.TSA);
                if (pt == U.NOT_FOUND) return false;
                U.append_u32(tsaList, U.toPointer(pt));

                uint pp = ra.WriteAmbient(a.Palette);
                if (pp == U.NOT_FOUND) return false;
                U.append_u32(palList, U.toPointer(pp));
            }
            if (ra.WriteAndWritePointerAmbient(e.TSAPointer, tsaList.ToArray()) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.ImagePointer, imageList.ToArray()) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.PalettePointer, palList.ToArray()) == U.NOT_FOUND) return false;
            return true;
        }

        // frame-table, COMMONPALETTE: per-frame image/tsa lists, ONE shared palette.
        static bool WriteFrameTableCommonPalette(ROM rom, RomAnimeCore.RomAnimeEntry e,
            RecycleAddress ra, List<FrameImage> list, byte[] frameTable)
        {
            var imageList = new List<byte>();
            var tsaList = new List<byte>();
            foreach (FrameImage a in list)
            {
                uint pi = ra.WriteAmbient(a.Image);
                if (pi == U.NOT_FOUND) return false;
                U.append_u32(imageList, U.toPointer(pi));

                uint pt = ra.WriteAmbient(a.TSA);
                if (pt == U.NOT_FOUND) return false;
                U.append_u32(tsaList, U.toPointer(pt));
            }
            if (ra.WriteAndWritePointerAmbient(e.FramePointer, frameTable) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.TSAPointer, tsaList.ToArray()) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.ImagePointer, imageList.ToArray()) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.PalettePointer, list[0].Palette) == U.NOT_FOUND) return false;
            return true;
        }

        // frame-table, multi-palette: per-frame image/tsa/pal lists + the {id,wait} table.
        static bool WriteFrameTableMultiPalette(ROM rom, RomAnimeCore.RomAnimeEntry e,
            RecycleAddress ra, List<FrameImage> list, byte[] frameTable)
        {
            var imageList = new List<byte>();
            var tsaList = new List<byte>();
            var palList = new List<byte>();
            foreach (FrameImage a in list)
            {
                uint pi = ra.WriteAmbient(a.Image);
                if (pi == U.NOT_FOUND) return false;
                U.append_u32(imageList, U.toPointer(pi));

                uint pt = ra.WriteAmbient(a.TSA);
                if (pt == U.NOT_FOUND) return false;
                U.append_u32(tsaList, U.toPointer(pt));

                uint pp = ra.WriteAmbient(a.Palette);
                if (pp == U.NOT_FOUND) return false;
                U.append_u32(palList, U.toPointer(pp));
            }
            if (ra.WriteAndWritePointerAmbient(e.FramePointer, frameTable) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.TSAPointer, tsaList.ToArray()) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.ImagePointer, imageList.ToArray()) == U.NOT_FOUND) return false;
            if (ra.WriteAndWritePointerAmbient(e.PalettePointer, palList.ToArray()) == U.NOT_FOUND) return false;
            return true;
        }

        // ----------------------------------------------------------------
        // MakeRecycleList — pool the OLD regions for reuse (WF L457).
        // ----------------------------------------------------------------

        /// <summary>
        /// Pool the entry's CURRENT IMAGE / TSA / PALETTE (and the frame table, for a
        /// frame-table entry) regions into <paramref name="recycle"/> so the rewrite can
        /// reuse the freed bytes (WF <c>MakeRecycleList(ref recycle, "", false)</c>, L457).
        /// FIXEDCOUNT walks 0..FramePointer; a frame-table entry walks the <c>{id,wait}</c>
        /// table (clamping list indexes like WF) and finally pools the table pointer itself.
        /// </summary>
        public static void MakeRecycleList(ROM rom, RomAnimeCore.RomAnimeEntry e, List<Address> recycle)
        {
            if (rom == null || e == null || recycle == null) return;

            if (!e.IsFrameTable)
            {
                int count = (int)e.FramePointer;
                for (int i = 0; i < count; i++)
                    AddFrameRegions(rom, e, i, recycle);
                return;
            }

            uint addr = rom.p32(e.FramePointer);
            if (!U.isSafetyOffset(addr, rom)) return;
            uint startAddr = addr;
            uint limit = (uint)Math.Min((ulong)addr + FRAME_TABLE_LIMIT, (ulong)rom.Data.Length);
            for (; addr + 4 <= rom.Data.Length && addr < limit; addr += 4)
            {
                uint id = rom.u16(addr);
                if (id == 0xFFFF) break;
                AddFrameRegions(rom, e, (int)id, recycle);
            }
            // Pool the frame table itself (WF AddPointer ROMANIMEFRAME).
            Address.AddPointer(recycle, e.FramePointer, addr - startAddr,
                "FRAME", Address.DataTypeEnum.ROMANIMEFRAME);
        }

        // Pool the IMAGE+TSA (LZ77) + PALETTE (32-byte) regions for one frame index.
        static void AddFrameRegions(ROM rom, RomAnimeCore.RomAnimeEntry e, int i, List<Address> recycle)
        {
            if (e.TSAList.Count > 0)
            {
                uint tsa = ClampPick(e.TSAList, i);
                Address.AddAddress(recycle, tsa, LZ77.getCompressedSize(rom.Data, tsa),
                    U.NOT_FOUND, "TSA", Address.DataTypeEnum.LZ77TSA);
            }
            if (e.ImageList.Count > 0)
            {
                uint image = ClampPick(e.ImageList, i);
                Address.AddAddress(recycle, image, LZ77.getCompressedSize(rom.Data, image),
                    U.NOT_FOUND, "IMAGE", Address.DataTypeEnum.LZ77IMG);
            }
            if (e.PaletteList.Count > 0)
            {
                uint pal = ClampPick(e.PaletteList, i);
                Address.AddAddress(recycle, pal, PALETTE_BYTES,
                    U.NOT_FOUND, "PAL", Address.DataTypeEnum.PAL);
            }
        }

        static uint ClampPick(List<uint> list, int i)
            => (i >= list.Count) ? list[list.Count - 1] : list[i];

        // ----------------------------------------------------------------
        // Per-frame encode / render helpers.
        // ----------------------------------------------------------------

        // Load + quantize a PNG (via the caller's loader), crop/pad the indexed pixels
        // to (imageWidth, 128), EncodeTSA, then LZ77-compress image + tsa. ZERO ROM
        // mutation — every byte produced here is buffered until PHASE 2.
        static string EncodeFrame(string baseDir, string imageName, int imageWidth,
            Func<string, (byte[] indexedPixels, byte[] gbaPalette16, int width, int height)?> loader,
            int lineNo, out FrameImage a)
        {
            a = null;
            string fullPath = Path.Combine(baseDir, imageName);
            (byte[] indexedPixels, byte[] gbaPalette16, int width, int height)? loaded;
            try { loaded = loader(fullPath); }
            catch (Exception ex) { return R.Error("Failed to load image: {0}\r\nFile: {1} line:{2}", ex.Message, imageName, lineNo); }
            if (loaded == null)
                return R.Error("The file was not found.\r\nFile: {0} line:{1}", imageName, lineNo);

            var (indexed, gbaPal, srcW, srcH) = loaded.Value;
            if (indexed == null || gbaPal == null)
                return R.Error("Could not import the image.\r\nFile: {0} line:{1}", imageName, lineNo);

            // Crop/pad the indexed pixels to the canvas (WF ImageUtil.Copy).
            byte[] canvas = CropOrPadIndexed(indexed, srcW, srcH, imageWidth, FRAME_HEIGHT);

            ImageImportCore.TSAEncodeResult enc;
            try { enc = ImageImportCore.EncodeTSA(canvas, imageWidth, FRAME_HEIGHT); }
            catch (Exception ex) { return R.Error("Could not import the image.\r\nFile: {0} line:{1}\r\n\r\n{2}", imageName, lineNo, ex.Message); }
            if (enc == null || enc.TileData == null || enc.TSAData == null)
                return R.Error("Could not import the image.\r\nFile: {0} line:{1}", imageName, lineNo);

            a = new FrameImage
            {
                Image = LZ77.compress(enc.TileData),
                TSA = LZ77.compress(enc.TSAData),
                Palette = NormalizePalette(gbaPal),
                Name = imageName,
            };
            return "";
        }

        // Render UI frame `frameIndex` and persist as a PNG (FIXEDCOUNT export path).
        static string RenderAndSave(ROM rom, RomAnimeCore.RomAnimeEntry e, int frameIndex, string pngPath)
        {
            using IImage img = RomAnimeCore.TryRenderFrame(rom, e, frameIndex);
            return SaveOrBlank(img, e, pngPath);
        }

        // Render stored-id `id` (frame-table export path) and persist as a PNG.
        static string RenderAndSaveById(ROM rom, RomAnimeCore.RomAnimeEntry e, int id, string pngPath)
        {
            using IImage img = RenderById(rom, e, id);
            return SaveOrBlank(img, e, pngPath);
        }

        static string SaveOrBlank(IImage img, RomAnimeCore.RomAnimeEntry e, string pngPath)
        {
            try
            {
                IImage save = img ?? CoreState.ImageService.CreateImage(e.ImageWidthTiles * 8, FRAME_HEIGHT);
                using (save == img ? null : save) { save.Save(pngPath); }
                return "";
            }
            catch (Exception ex) { return R.Error("Failed to write PNG: {0}", ex.Message); }
        }

        // Render a GIF frame from a UI frame index (FIXEDCOUNT path).
        static GifEncoderCore.GifFrame RenderGifFrame(ROM rom, RomAnimeCore.RomAnimeEntry e, int frameIndex, uint wait)
        {
            using IImage img = RomAnimeCore.TryRenderFrame(rom, e, frameIndex);
            return ToGifFrame(img, e, wait);
        }

        // Render a GIF frame from a stored frame id (frame-table path).
        static GifEncoderCore.GifFrame RenderGifFrameById(ROM rom, RomAnimeCore.RomAnimeEntry e, int id, uint wait)
        {
            using IImage img = RenderById(rom, e, id);
            return ToGifFrame(img, e, wait);
        }

        static GifEncoderCore.GifFrame ToGifFrame(IImage img, RomAnimeCore.RomAnimeEntry e, uint wait)
        {
            int w = e.ImageWidthTiles * 8, h = FRAME_HEIGHT;
            byte[] rgba;
            if (img != null)
            {
                w = img.Width; h = img.Height;
                rgba = GifEncoderCore.IndexedToRgba(img.GetPixelData(), img.GetPaletteRGBA(), w, h);
            }
            else
            {
                rgba = new byte[w * h * 4]; // a fully transparent blank frame
            }
            return new GifEncoderCore.GifFrame
            {
                Width = w,
                Height = h,
                RgbaPixels = rgba,
                DelayCs = U.GameFrameSecToGifFrameSec(wait),
            };
        }

        // Render a specific STORED frame id (the WF DrawDirect((int)id) path) — used by
        // both the frame-table export and GIF walks where id != UI frame index.
        static IImage RenderById(ROM rom, RomAnimeCore.RomAnimeEntry e, int id)
        {
            if (rom == null || e == null || id < 0) return null;
            if (CoreState.ImageService == null) return null;
            if (!TryPick(e.TSAList, id, out uint tsa)) return null;
            if (!TryPick(e.ImageList, id, out uint image)) return null;
            if (!TryPick(e.PaletteList, id, out uint palette)) return null;
            return RomAnimeCore.RenderFromOffsets(rom, e.ImageWidthTiles, tsa, image, palette);
        }

        static bool TryPick(List<uint> list, int index, out uint offset)
        {
            offset = 0;
            if (list == null || list.Count <= 0) return false;
            offset = (index >= list.Count) ? list[list.Count - 1] : list[index];
            return true;
        }

        static uint FindImage(List<FrameImage> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Name == name) return (uint)i;
            return U.NOT_FOUND;
        }

        // Crop/pad indexed pixels top-left to (dstW, dstH) (WF ImageUtil.Copy(_,0,0,w,h)).
        static byte[] CropOrPadIndexed(byte[] src, int srcW, int srcH, int dstW, int dstH)
        {
            byte[] dst = new byte[dstW * dstH];
            if (src == null || srcW <= 0 || srcH <= 0) return dst;
            int copyW = Math.Min(srcW, dstW);
            int copyH = Math.Min(srcH, dstH);
            for (int y = 0; y < copyH; y++)
            {
                int srcOff = y * srcW;
                int dstOff = y * dstW;
                int n = Math.Min(copyW, Math.Min(src.Length - srcOff, dst.Length - dstOff));
                if (n > 0) Array.Copy(src, srcOff, dst, dstOff, n);
            }
            return dst;
        }

        // Normalize any quantize palette to exactly 32 bytes (16 colors x 2).
        static byte[] NormalizePalette(byte[] input)
        {
            byte[] result = new byte[PALETTE_BYTES];
            if (input == null) return result;
            Array.Copy(input, result, Math.Min(input.Length, PALETTE_BYTES));
            return result;
        }

        // Length-aware byte-identical restore for the fault path (#885/#923), identical
        // to RomAnimeCore.RestoreSnapshot.
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom == null || snap == null) return;
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }
    }
}
