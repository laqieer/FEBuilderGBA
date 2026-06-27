// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform image I/O seam for the Map Tile Animation Type 1 editor (#1602).
//
// Ports the image preview + single-PNG Import/Export + batch (.mapanime1.txt)
// I/O of WinForms MapTileAnimation1Form into Core so the Avalonia
// MapTileAnimation1View can render, export and import the animated tile sheet
// without WinForms / System.Drawing dependencies.
//
// CRITICAL anime1 schema (the INVERSE of anime2):
//   8-byte entry: wait u16@+0, length u16@+2, imagePointer p32@+4.
//   The graphics block reached by +4 is RAW UNCOMPRESSED 4bpp (NOT LZ77),
//   width = 256px (32 tiles * 8), height = CalcHeight(256, length), sized by
//   the entry's +2 u16 length. WF reads it via getBinaryData (no decompress)
//   and writes it via WriteImageData (raw) — so this seam uses
//   ImageImportCore.WriteRawToROM, NEVER WriteCompressedToROM.
//
// +2 LENGTH AUTHORITY (per the #1602 Copilot plan review — WF semantics):
//   * Single import keeps the entry's EXISTING +2 length authoritative: WF
//     derives the expected PNG height from CalcHeight(256, currentLength),
//     REJECTS a mismatched size with NO mutation, writes only the raw bytes to
//     +4, and leaves +2 unchanged. ImportEntryImage mirrors this exactly.
//   * Batch import honours the manifest's explicit 3rd "length" column when
//     present (truncate the encoded bytes to it) else the full encoded length,
//     and writes THAT length to +2 (always <= 0xFFFF guarded).
//
// Atomicity: ROM-mutating methods run under the CALLER's ambient undo scope and
// keep a defensive rom.Data snapshot so any fault — including a free-space
// resize-append — is restored byte-identical (length-aware; the #885/#923
// pattern, same as WaitIconImportCore).
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA.Core;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform image preview + Import/Export seam for the Map Tile
    /// Animation Type 1 editor (#1602). The render/export halves are READ-ONLY;
    /// the import halves are ROM-MUTATING and run inside the caller's ambient
    /// undo scope with a byte-identical fault restore.
    /// </summary>
    public static class MapTileAnimation1ImageCore
    {
        /// <summary>Fixed tile-sheet width (32 tiles * 8 px) for anime1.</summary>
        public const int IMAGE_WIDTH = 32 * 8;

        /// <summary>
        /// Port of <c>ImageUtil.CalcHeight(256, length)</c> — the row count of
        /// the 256-wide RAW 4bpp tile sheet for a given byte length. Returns 0
        /// when <paramref name="length"/> &lt;= 0.
        /// </summary>
        public static int CalcEntryHeight(int length)
        {
            if (length <= 0) return 0;
            const int width = IMAGE_WIDTH;
            const int align = 8;
            int height = length / (width / 2);   // length / 128
            if (length % (width / 2) != 0) height++;
            if (height % align != 0) height += align;
            return height / align * align;
        }

        /// <summary>
        /// Resolve the parent map's palette offset for an anime1 PLIST. WF preview
        /// uses the palette of the FIRST map whose <c>palette_plist</c> shares the
        /// chapter that references this <paramref name="anime1Plist"/>. Walks the
        /// resolve cache, finds the map whose <c>anime1_plist</c> equals
        /// <paramref name="anime1Plist"/>, reads its <c>palette_plist</c> and
        /// resolves the PALETTE table offset. READ-ONLY, never throws; returns
        /// false (palOffset=0) when nothing resolves.
        /// </summary>
        public static bool ResolvePaletteOffset(ROM rom, uint anime1Plist, out uint palOffset)
        {
            palOffset = 0;
            try
            {
                if (rom?.RomInfo == null || anime1Plist == 0) return false;
                var cache = MapPListResolverCore.BuildCache(rom);
                foreach (AddrResult map in cache.Maps)
                {
                    var plists = cache.PListsAt(map.addr);
                    if (plists.anime1_plist != anime1Plist) continue;
                    if (plists.palette_plist == 0) continue;
                    uint addr = MapChangeCore.PlistToOffsetAddr(
                        rom, MapChangeCore.PlistType.PALETTE, plists.palette_plist, out uint _);
                    if (addr == U.NOT_FOUND) continue;
                    if (!U.isSafetyOffset(addr, rom)) continue;
                    palOffset = addr;
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Render the RAW 4bpp graphics block reached by an entry's <c>+4</c>
        /// image pointer to an <see cref="IImage"/> at 256 × <c>CalcEntryHeight(length)</c>,
        /// using the 16-color sub-palette at <paramref name="paletteIndex"/> from
        /// the palette region at <paramref name="palOffset"/>. READ-ONLY, never
        /// throws; returns null on any bad pointer / out-of-bounds / no image
        /// service.
        /// </summary>
        public static IImage RenderEntryImage(ROM rom, uint p4Pointer, uint length, uint palOffset, int paletteIndex)
        {
            try
            {
                if (rom?.Data == null) return null;
                if (CoreState.ImageService == null) return null;
                uint imgOffset = U.toOffset(p4Pointer);
                if (!U.isSafetyOffset(imgOffset, rom)) return null;
                if (!U.isSafetyOffset(palOffset, rom)) return null;

                int height = CalcEntryHeight((int)length);
                if (height <= 0) return null;

                int palByteOffset = (int)palOffset + paletteIndex * 16 * 2;
                return ImageUtilCore.ByteToImage16Tile(
                    rom.Data, (int)imgOffset,
                    rom.Data, palByteOffset,
                    IMAGE_WIDTH, height);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Single-PNG import (WF <c>ImportButton_Click</c> parity): the entry's
        /// EXISTING <c>+2</c> length stays authoritative. Validates
        /// <paramref name="width"/> == 256 and
        /// <paramref name="height"/> == <c>CalcEntryHeight(currentLength)</c>
        /// (REJECT with NO mutation otherwise), remaps the RGBA pixels onto the
        /// 16-color sub-palette at <paramref name="paletteIndex"/>, encodes RAW
        /// 4bpp, writes the raw bytes to free space and repoints <c>entryAddr+4</c>
        /// — <c>+2</c> is left UNCHANGED. Runs in the caller's ambient undo scope;
        /// any fault restores the ROM byte-identical. Returns "" on success or a
        /// localized error string.
        /// </summary>
        public static string ImportEntryImage(ROM rom, uint entryAddr, byte[] rgbaPixels,
            int width, int height, uint palOffset, int paletteIndex)
        {
            if (rom?.Data == null) return R._("ROM is not loaded.");
            if (rgbaPixels == null) return R._("No image data.");
            if (entryAddr + 8 > (uint)rom.Data.Length)
                return R._("The tile animation entry address is out of range.");

            uint currentLength = rom.u16(entryAddr + 2);
            int expectedHeight = CalcEntryHeight((int)currentLength);
            if (width != IMAGE_WIDTH || height != expectedHeight)
            {
                return R._(
                    "The image size is not correct.\r\nWidth:{2} Height:{3} is required.\r\n\r\nSelected image size Width:{0} Height:{1}",
                    width, height, IMAGE_WIDTH, expectedHeight);
            }
            if (rgbaPixels.Length < (long)width * height * 4)
                return R._("Image pixel data is missing or too short.");
            if (!U.isSafetyOffset(palOffset, rom))
                return R._("The tile animation palette is invalid.");

            // Read the 16-color sub-palette to remap onto.
            byte[] palette = ImageUtilCore.GetPalette(rom, palOffset + (uint)(paletteIndex * 16 * 2), 16);
            byte[] indexed = ImageImportCore.RemapToExistingPalette(rgbaPixels, width, height, palette, 16);
            if (indexed == null)
                return R._("Failed to remap the image onto the palette.");

            byte[] raw = ImageImportCore.EncodeDirectTiles4bpp(indexed, width, height);
            if (raw == null || raw.Length == 0)
                return R._("Failed to encode tile animation image.");

            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                uint writeAddr = ImageImportCore.WriteRawToROM(rom, raw, entryAddr + 4);
                if (writeAddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("Failed to write tile animation image. Check ROM free space.");
                }
                // +2 length is NOT changed (WF single-import parity).
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("Tile animation import failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Export the current PLIST's entries to a <c>.mapanime1.txt</c> manifest
        /// plus one per-entry PNG (<c>wait\tfilename\tlength</c> lines, mirroring
        /// WF <c>ExportAll</c>). READ-ONLY (never mutates the ROM). The
        /// <paramref name="savePng"/> delegate writes each rendered IImage to a
        /// PNG file (keeps Core free of an image-encoder dependency). Returns ""
        /// on success or a localized error string.
        /// </summary>
        public static string ExportBatchTxt(ROM rom, uint baseAddr, int dataCount,
            uint palOffset, int paletteIndex, string txtPath,
            Action<IImage, string> savePng)
        {
            try
            {
                if (rom?.Data == null) return R._("ROM is not loaded.");
                if (savePng == null) return R._("No PNG writer.");
                if (!U.isSafetyOffset(baseAddr, rom)) return R._("Invalid entry table address.");

                string dir = Path.GetDirectoryName(txtPath) ?? ".";
                string file = Path.GetFileNameWithoutExtension(txtPath);
                // Strip a trailing ".mapanime1" so the PNG stem is clean.
                if (file.EndsWith(".mapanime1", StringComparison.OrdinalIgnoreCase))
                    file = file.Substring(0, file.Length - ".mapanime1".Length);

                var lines = new List<string> { "//wait\tfilename\tlength" };
                uint addr = baseAddr;
                for (int i = 0; i < dataCount; i++, addr += 8)
                {
                    if (addr + 8 > (uint)rom.Data.Length) break;
                    uint wait = rom.u16(addr + 0);
                    uint length = rom.u16(addr + 2);
                    uint p4 = rom.p32(addr + 4);
                    if (!U.isSafetyOffset(U.toOffset(p4), rom)) continue;

                    IImage img = RenderEntryImage(rom, p4, length, palOffset, paletteIndex);
                    if (img == null) continue;

                    string pngName = file + "_" + i.ToString("000") + ".png";
                    savePng(img, Path.Combine(dir, pngName));
                    lines.Add(wait + "\t" + pngName + "\t" + length);
                }
                File.WriteAllLines(txtPath, lines);
                return "";
            }
            catch (Exception ex)
            {
                return R._("Tile animation export failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Resolved chapter-map render offsets + the anime1 entry table base for a
        /// given anime1 PLIST, mirroring how WF <c>MapTileAnimation1Form.ExportGif</c>
        /// loads the FIRST map that references the PLIST and reads its OBJ / palette
        /// / config / map-pointer plists.
        /// </summary>
        public sealed class GifRenderContext
        {
            /// <summary>ROM offset of the LZ77 OBJ tile stream (primary tileset).</summary>
            public uint ObjOffset;
            /// <summary>ROM offset of the FE7 secondary OBJ tileset, or 0 (none).</summary>
            public uint Obj2Offset;
            /// <summary>ROM offset of the RAW 512-byte palette block.</summary>
            public uint PaletteOffset;
            /// <summary>ROM offset of the LZ77 config (chipset) stream.</summary>
            public uint ConfigOffset;
            /// <summary>ROM offset of the LZ77 map arrangement stream.</summary>
            public uint MapOffset;
            /// <summary>ROM offset of the anime1 8-byte entry table.</summary>
            public uint EntryBaseAddr;
        }

        /// <summary>
        /// Resolve every offset needed to composite the chapter map that uses an
        /// anime1 PLIST, mirroring WF <c>MapTileAnimation1Form.ExportGif</c>
        /// (lines 416-426: pick the FIRST map whose <c>anime1_plist</c> equals the
        /// selected PLIST, then read its OBJ/palette/config/map-pointer plists and
        /// resolve them through the standard PLIST tables). READ-ONLY, never
        /// throws; returns null when no map references the PLIST or any required
        /// plist fails to resolve.
        /// </summary>
        public static GifRenderContext ResolveGifContext(ROM rom, uint anime1Plist)
        {
            try
            {
                if (rom?.RomInfo == null || anime1Plist == 0) return null;

                var cache = MapPListResolverCore.BuildCache(rom);
                foreach (AddrResult map in cache.Maps)
                {
                    var plists = cache.PListsAt(map.addr);
                    if (plists.anime1_plist != anime1Plist) continue;

                    // OBJ: low byte = primary tileset, high byte = FE7 secondary.
                    uint objLow = plists.obj_plist & 0xFF;
                    uint objHigh = (plists.obj_plist >> 8) & 0xFF;
                    uint objOffset = MapChangeCore.PlistToOffsetAddr(
                        rom, MapChangeCore.PlistType.OBJECT, objLow, out uint _);
                    if (objOffset == U.NOT_FOUND || !U.isSafetyOffset(objOffset, rom)) continue;

                    uint obj2Offset = 0;
                    if (objHigh > 0)
                    {
                        obj2Offset = MapChangeCore.PlistToOffsetAddr(
                            rom, MapChangeCore.PlistType.OBJECT, objHigh, out uint _);
                        // A broken obj2 plist makes WF DrawMapChipOnly bail; skip
                        // this map and keep searching for a renderable one.
                        if (obj2Offset == U.NOT_FOUND || !U.isSafetyOffset(obj2Offset, rom)) continue;
                    }

                    uint palOffset = MapChangeCore.PlistToOffsetAddr(
                        rom, MapChangeCore.PlistType.PALETTE, plists.palette_plist, out uint _);
                    if (palOffset == U.NOT_FOUND || !U.isSafetyOffset(palOffset, rom)) continue;

                    uint cfgOffset = MapChangeCore.PlistToOffsetAddr(
                        rom, MapChangeCore.PlistType.CONFIG, plists.config_plist, out uint _);
                    if (cfgOffset == U.NOT_FOUND || !U.isSafetyOffset(cfgOffset, rom)) continue;

                    uint mapOffset = MapChangeCore.PlistToOffsetAddr(
                        rom, MapChangeCore.PlistType.MAP, plists.mappointer_plist, out uint _);
                    if (mapOffset == U.NOT_FOUND || !U.isSafetyOffset(mapOffset, rom)) continue;

                    uint entryBase = MapChangeCore.PlistToOffsetAddr(
                        rom, MapChangeCore.PlistType.ANIMATION, anime1Plist, out uint _);
                    if (entryBase == U.NOT_FOUND || !U.isSafetyOffset(entryBase, rom)) continue;

                    return new GifRenderContext
                    {
                        ObjOffset = objOffset,
                        Obj2Offset = obj2Offset,
                        PaletteOffset = palOffset,
                        ConfigOffset = cfgOffset,
                        MapOffset = mapOffset,
                        EntryBaseAddr = entryBase,
                    };
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Read the RAW (uncompressed) 4bpp graphics block for one anime1 entry —
        /// the WF <c>GetTileAnime1(p4, length)</c> byte path used as
        /// <c>MapAnimations.change_bitmap_bytes</c>: 256-wide, height =
        /// <see cref="CalcEntryHeight"/>, so <c>128 * height</c> bytes. Returns
        /// null on a bad pointer / out-of-bounds. READ-ONLY.
        /// </summary>
        public static byte[] ReadFrameBytes(ROM rom, uint p4Pointer, uint length)
        {
            if (rom?.Data == null) return null;
            uint imgOffset = U.toOffset(p4Pointer);
            if (!U.isSafetyOffset(imgOffset, rom)) return null;
            int height = CalcEntryHeight((int)length);
            if (height <= 0) return null;
            long need = (long)(IMAGE_WIDTH / 2) * height; // 128 * height
            if (need <= 0 || imgOffset + need > (uint)rom.Data.Length) return null;
            return rom.getBinaryData(imgOffset, (uint)need);
        }

        /// <summary>
        /// Export every anime1 frame for <paramref name="anime1Plist"/> as an
        /// animated GIF, compositing each frame onto the rendered chapter map that
        /// uses this PLIST — the deferred half of #1602 (WF
        /// <c>MapTileAnimation1Form.ExportGif</c>). For each 8-byte entry the
        /// frame's RAW 4bpp bytes are patched into the map's OBJ tiles (via
        /// <see cref="MapRenderCore.RenderMapImage"/>'s anime-overlay parameter at
        /// <see cref="MapRenderCore.ANIME1_OBJ_PATCH_OFFSET"/>) and the resulting
        /// composited map image becomes one GIF frame with a delay of
        /// <see cref="U.GameFrameSecToGifFrameSec"/>(wait). READ-ONLY (never mutates
        /// the ROM). Each rendered <see cref="IImage"/> is disposed after its RGBA
        /// pixels are copied out (the documented frame-leak trap). Returns "" on
        /// success or a localized error string.
        /// </summary>
        public static string ExportGif(ROM rom, uint anime1Plist, string gifPath)
        {
            if (rom?.Data == null) return R._("ROM is not loaded.");
            if (CoreState.ImageService == null) return R._("Image service is not available.");
            if (anime1Plist == 0) return R._("No PLIST selected.");

            GifRenderContext ctx = ResolveGifContext(rom, anime1Plist);
            if (ctx == null)
                return R._("Cannot resolve the chapter map for this tile animation PLIST.");

            var entries = MapTileAnimation1Core.ScanEntries(rom, ctx.EntryBaseAddr, maxRows: 256);
            if (entries.Count == 0)
                return R._("This tile animation PLIST has no frames to export.");

            var frames = new List<GifEncoderCore.GifFrame>();
            try
            {
                foreach (var e in entries)
                {
                    byte[] frameBytes = ReadFrameBytes(rom, e.P4, e.Length);
                    if (frameBytes == null) continue;

                    IImage img = MapRenderCore.RenderMapImage(
                        rom, ctx.ObjOffset, ctx.PaletteOffset, ctx.ConfigOffset, ctx.MapOffset,
                        ctx.Obj2Offset, frameBytes, MapRenderCore.ANIME1_OBJ_PATCH_OFFSET);
                    if (img == null) continue;

                    try
                    {
                        int w = img.Width, h = img.Height;
                        byte[] rgba = img.IsIndexed
                            ? GifEncoderCore.IndexedToRgba(img.GetPixelData(), img.GetPaletteRGBA(), w, h)
                            : img.GetPixelData();
                        frames.Add(new GifEncoderCore.GifFrame
                        {
                            Width = w,
                            Height = h,
                            RgbaPixels = rgba,
                            DelayCs = U.GameFrameSecToGifFrameSec(e.Wait),
                        });
                    }
                    finally { img.Dispose(); }
                }

                if (frames.Count == 0)
                    return R._("Failed to render any tile animation frame for the chapter map.");

                GifEncoderCore.Encode(frames, gifPath);
                return "";
            }
            catch (Exception ex)
            {
                return R._("Tile animation GIF export failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Re-import a <c>.mapanime1.txt</c> manifest (WF <c>ImportAll</c> parity).
        /// Each <c>wait\tfilename[\tlength]</c> line loads a PNG (via the injected
        /// <paramref name="loadRgba"/> delegate so Core stays decoder-free), remaps
        /// it onto the 16-color sub-palette at <paramref name="palOffset"/> /
        /// <paramref name="paletteIndex"/>, encodes RAW 4bpp, optionally truncates
        /// to the explicit 3rd-column length (the manifest length is authoritative
        /// when present), and rebuilds the 8-byte entry table (terminated by a
        /// <c>0,0,0</c> row). All image blocks PLUS the entry table are written as
        /// ONE contiguous free-space allocation (so an all-transparent / all-0x00
        /// frame can never be rediscovered as free and overwritten by a later
        /// block or the table — <c>ROM.FindFreeSpace</c> treats 0x00 runs as free),
        /// then the PLIST <paramref name="pointerAddr"/> is repointed to the rebuilt
        /// table — all under the caller's ambient undo scope with a byte-identical
        /// fault restore. The <c>wait</c> column and every <c>+2</c> length write
        /// are <c>&lt;= 0xFFFF</c> range-checked (rejected, not truncated). Returns
        /// "" on success or a localized error string.
        /// </summary>
        /// <param name="loadRgba">Loads a PNG path → (rgba, width, height). Returns
        /// false on failure.</param>
        public static string ImportBatchTxt(ROM rom, uint pointerAddr, string txtPath,
            uint palOffset, int paletteIndex,
            LoadRgbaDelegate loadRgba)
        {
            if (rom?.Data == null) return R._("ROM is not loaded.");
            if (loadRgba == null) return R._("No image loader.");
            if (pointerAddr + 4 > (uint)rom.Data.Length)
                return R._("The tile animation pointer address is out of range.");
            if (!U.isSafetyOffset(palOffset, rom))
                return R._("The tile animation palette is invalid.");

            string[] lines;
            try { lines = File.ReadAllLines(txtPath); }
            catch (Exception ex) { return R._("Cannot read manifest: {0}", ex.Message); }

            string dir = Path.GetDirectoryName(txtPath) ?? ".";
            byte[] palette = ImageUtilCore.GetPalette(rom, palOffset + (uint)(paletteIndex * 16 * 2), 16);

            // -------- Phase 1: validate + encode ALL blocks (NO ROM writes) --------
            // The snapshot is intentionally NOT taken yet: an early validation
            // failure here mutates nothing, so we avoid a full ~32MB ROM clone on
            // the no-op path (Copilot inline review).
            var blocks = new List<(uint wait, byte[] raw)>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (U.IsComment(line) || U.OtherLangLine(line)) continue;
                line = U.ClipComment(line);
                if (line == "") continue;
                string[] sp = line.Split('\t');
                if (sp.Length < 2) continue;

                uint wait = U.atoi(sp[0]);
                // anime1 wait is a u16 — reject (not silently truncate) > 0xFFFF
                // (Copilot inline review).
                if (wait > 0xFFFF)
                    return R._("Tile animation wait {0} is out of range (max 65535). Line: {1}", wait, i);

                string imgPath = Path.Combine(dir, sp[1].Trim());
                if (!loadRgba(imgPath, out byte[] rgba, out int w, out int h)
                    || rgba == null || w <= 0 || h <= 0)
                    return R._("Tile animation image not found:\r\n{0}\r\nLine: {1}", imgPath, i);
                if (w % 8 != 0 || h % 8 != 0)
                    return R._("Image {0} must be a multiple of 8 in both dimensions.", imgPath);

                byte[] indexed = ImageImportCore.RemapToExistingPalette(rgba, w, h, palette, 16);
                if (indexed == null)
                    return R._("Failed to remap the image onto the palette:\r\n{0}", imgPath);
                byte[] raw = ImageImportCore.EncodeDirectTiles4bpp(indexed, w, h);
                if (raw == null || raw.Length == 0)
                    return R._("Failed to encode tile animation image:\r\n{0}", imgPath);

                // Explicit length column (authoritative) truncates the bytes.
                if (sp.Length >= 3)
                {
                    uint explicitLen = U.atoi(sp[2]);
                    if (explicitLen > 0 && explicitLen < (uint)raw.Length)
                        raw = U.subrange(raw, 0, explicitLen);
                }
                if (raw.Length > 0xFFFF)
                    return R._("Tile animation image too large ({0} bytes; max 65535):\r\n{1}", raw.Length, imgPath);
                blocks.Add((wait, raw));
            }

            // -------- Phase 2: ONE contiguous allocation: [block0..blockN][table] --------
            // Writing every block PLUS the entry table as a single contiguous buffer
            // (rather than per-block FindAndWriteData calls) guarantees that an
            // all-transparent (all-0x00) frame can NEVER be rediscovered as free
            // space and overwritten by a later block / the table — ROM.FindFreeSpace
            // treats a run of 0x00 (and 0xFF) as free, so per-block allocation could
            // alias an earlier all-zero block (Copilot blocking review). One
            // allocation also means one self-consistent fault-restore point.
            int tableBytes = (blocks.Count + 1) * 8; // +1 terminator row
            // Lay out each block 4-byte aligned; record its offset within the buffer.
            var blockOffsets = new int[blocks.Count];
            int cursor = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                blockOffsets[i] = cursor;
                cursor += (int)U.Padding4((uint)blocks[i].raw.Length);
            }
            int tableOffset = cursor;
            byte[] buffer = new byte[tableOffset + tableBytes];

            // Take the byte-identical snapshot ONLY now, right before the first
            // (and only) ROM write — so the restore is exact on any fault.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // Write the whole buffer ONCE; pointer fix-ups happen after we know
                // the base address (so the table's per-row pointers are absolute).
                uint baseAddr = ImageImportCore.FindAndWriteData(rom, buffer);
                if (baseAddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("Failed to write tile animation data. Check ROM free space.");
                }

                // Now that baseAddr is known, write each block's bytes and the entry
                // table (with absolute GBA pointers) into the reserved region.
                for (int i = 0; i < blocks.Count; i++)
                {
                    uint blockAddr = baseAddr + (uint)blockOffsets[i];
                    ImageImportCore.WriteBytes(rom, blockAddr, blocks[i].raw);

                    uint rowAddr = baseAddr + (uint)tableOffset + (uint)(i * 8);
                    rom.write_u16(rowAddr + 0, (ushort)blocks[i].wait);
                    rom.write_u16(rowAddr + 2, (ushort)blocks[i].raw.Length);
                    rom.write_p32(rowAddr + 4, blockAddr);
                }
                // Terminator row (wait=0, length=0, pointer=0) — buffer is already
                // zero-filled there, but write explicitly for clarity / undo cover.
                uint termAddr = baseAddr + (uint)tableOffset + (uint)(blocks.Count * 8);
                rom.write_u16(termAddr + 0, 0);
                rom.write_u16(termAddr + 2, 0);
                rom.write_u32(termAddr + 4, 0);

                // Repoint the PLIST slot to the entry TABLE (not the first block).
                rom.write_p32(pointerAddr, baseAddr + (uint)tableOffset);
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("Tile animation import failed: {0}", ex.Message);
            }
        }

        /// <summary>Loads a PNG/BMP file into RGBA pixels + dims. Injected so Core
        /// stays free of an image-decoder dependency. Returns false on failure.</summary>
        public delegate bool LoadRgbaDelegate(string path, out byte[] rgba, out int width, out int height);

        /// <summary>
        /// Length-aware byte-identical restore (a free-space resize-append can
        /// GROW rom.Data, so shrink back to the snapshot length BEFORE the in-place
        /// copy). Mirrors WaitIconImportCore.RestoreSnapshot.
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }
    }
}
