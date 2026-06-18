// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform render + per-frame import/export for the In-ROM Magic Animation
// editor (#1176). Ports the list / preview / single-frame import & export of
// WinForms ImageRomAnimeForm.cs (1295 lines) over the `g_ROMAnime` table, so the
// Avalonia ImageRomAnimeView can show every in-ROM spell/magic animation frame
// and round-trip a single frame to/from PNG — no System.Drawing / WinForms
// dependency.
//
// Config (config/data/romanime_FE{6,7,8}.txt, TSV) per row:
//   ID  ImageWidth(tiles)  OPTION  FRAME-ptr  TSA-ptr  IMAGE-ptr  PAL-ptr  NAME
// The IMAGE + TSA blocks are LZ77-compressed; the PAL block is a RAW 16-color
// (32-byte) GBA palette. Two storage shapes (ported from the WF resolver):
//   * FRAME-ptr is a real ROM offset -> it points to a {u16 id, u16 wait} frame
//     table terminated by 0xFFFF; TSA/IMAGE/PAL each point to a pointer-LIST.
//   * FRAME-ptr < 0x100 (FIXEDCOUNT) -> a fixed frame count; TSA/IMAGE/PAL are
//     per-frame pointer-lists (or COMMONPALETTE = a single shared palette).
//
// Render path (WF Draw/DrawDirect/DrawImageLow): LZ77-decompress TSA + IMAGE,
// height = CalcHeightbyTSA(width, tsaLen) capped at 128, then the plain (non-
// header) TSA primitive ImageUtilCore.DecodeTSA — the Core equivalent of WF
// ByteToImage16Tile(...tsa...) -> ByteToImage16TileInner (which indexes the
// image bytes directly by the TSA tile number, skipping only 0xFFFF). The big
// magic BG is an opaque background, so index 0 renders OPAQUE (WF blits the
// indexed bitmap with no transparent key) -> DecodeTSA(opaqueIndex0:true).
//
// Per-frame import (the inverse, #1176 slice): resolve UI frame -> stored frame
// id (walk the frame table when FRAME-ptr is real, else id == UI index), then
// repoint THAT id's IMAGE + TSA list slots and (unless COMMONPALETTE) its PAL
// list slot. Encodes already-quantized indexed pixels + a RAW 16-color GBA
// palette (the View quantizes via ImageImportService.LoadAndQuantize, exactly
// like BigCGViewerView) — NEVER raw RGBA. validate-all-before-mutate, ambient
// undo, lazy rom.Data.Clone() snapshot (after encode succeeds, before the first
// write) -> byte-identical fault restore (#885/#923).
//
// DEFERRED to a tracked follow-up (large separate subsystem): the multi-frame
// `.txt` animation-script export/import (frame-table rewrites, palette-animation
// / COMMONPALETTE multi-frame modes, FIXEDCOUNT pointer-list rewrites, recycle-
// list reuse) and the animated-GIF export.

using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helpers for the In-ROM Magic Animation editor (#1176):
    /// list, per-frame preview render, and single-frame PNG import/export over
    /// the <c>romanime_</c> table. GUI-free; every ROM read/write is bounds-
    /// guarded and the public methods never throw.
    /// </summary>
    public static class RomAnimeCore
    {
        // The GBA LZ77 stream header is 4 bytes (0x10 + a 3-byte uncompressed
        // size). Require the FULL header in-bounds before any LZ77 call (#818/#827).
        const int LZ77_HEADER_BYTES = 4;
        // RAW 16-color GBA palette = 16 * 2 bytes.
        const int PALETTE_COLORS = 16;
        const int PALETTE_BYTES = PALETTE_COLORS * 2;
        // WF caps the rendered height at 8*16 = 128 px (16 tiles tall).
        const int MAX_HEIGHT = 8 * 16;
        // WF limiter for the uncompressed frame table (1 MB search ceiling).
        const uint FRAME_TABLE_LIMIT = 1024 * 1024;

        /// <summary>One resolved <c>romanime_</c> entry (the WF per-selection state).</summary>
        public sealed class RomAnimeEntry
        {
            /// <summary>The hex ID key from the config row.</summary>
            public uint Id;
            /// <summary>Image width in 8-pixel tiles (config column 0).</summary>
            public int ImageWidthTiles;
            /// <summary>OPTION column (e.g. "COMMONPALETTE", "FIXEDCOUNT", or N's).</summary>
            public string Option = "";
            /// <summary>Display name (config column 6).</summary>
            public string Name = "";
            /// <summary>FRAME pointer slot (a real ROM offset OR a small fixed count).</summary>
            public uint FramePointer;
            /// <summary>TSA pointer slot (points to a pointer-list).</summary>
            public uint TSAPointer;
            /// <summary>IMAGE pointer slot (points to a pointer-list).</summary>
            public uint ImagePointer;
            /// <summary>PALETTE pointer slot (points to a pointer-list).</summary>
            public uint PalettePointer;
            /// <summary>Resolved per-frame TSA data offsets.</summary>
            public List<uint> TSAList = new List<uint>();
            /// <summary>Resolved per-frame IMAGE data offsets.</summary>
            public List<uint> ImageList = new List<uint>();
            /// <summary>Resolved per-frame PALETTE data offsets.</summary>
            public List<uint> PaletteList = new List<uint>();

            /// <summary>
            /// True when FRAME pointer is a real ROM offset (frame-table mode) vs.
            /// a small fixed count (FIXEDCOUNT). Precomputed at <see cref="Resolve"/>
            /// time via the explicit-ROM <c>U.isSafetyOffset(addr, rom)</c> overload
            /// so the property is self-contained — it does NOT read
            /// <see cref="CoreState.ROM"/> and never throws if that is unset
            /// (Copilot PR #1231 review #2).
            /// </summary>
            public bool IsFrameTable;
            /// <summary>True when the single shared palette is reused for every frame.</summary>
            public bool IsCommonPalette => Option == "COMMONPALETTE";
            /// <summary>
            /// True when the PALETTE pointer resolved to a single shared / in-place
            /// 32-byte block (the <see cref="GetPalettePointerListCount"/> no-pointer-
            /// list fallback), so a per-frame palette import writes that block raw
            /// in-place rather than repointing a list slot
            /// (Copilot PR #1231 review #1).
            /// </summary>
            public bool PaletteIsInPlaceBlock;
        }

        /// <summary>
        /// Load every <c>romanime_</c> config row for the loaded ROM as resolved
        /// entries (WF <c>g_ROMAnime</c> + the per-row pointer resolution). Returns
        /// an empty list (never throws) when the config file is missing or the ROM
        /// is null.
        /// </summary>
        public static List<RomAnimeEntry> LoadList(ROM rom)
        {
            var result = new List<RomAnimeEntry>();
            if (rom == null || rom.RomInfo == null || rom.Data == null) return result;

            Dictionary<uint, string[]> table;
            try
            {
                table = U.LoadTSVResource(U.ConfigDataFilename("romanime_", rom), false);
            }
            catch { return result; }
            if (table == null) return result;

            foreach (var pair in table)
            {
                RomAnimeEntry e = Resolve(rom, pair.Key, pair.Value);
                if (e != null) result.Add(e);
            }
            return result;
        }

        /// <summary>
        /// Resolve one config row (the WF <c>AddressList_SelectedIndexChanged</c>
        /// body): parse the width/option/4 pointers and build the per-frame
        /// TSA/IMAGE/PALETTE offset lists. Never throws; bad fields yield an entry
        /// with empty lists (the caller renders a blank frame).
        /// </summary>
        public static RomAnimeEntry Resolve(ROM rom, uint id, string[] fields)
        {
            if (rom == null || fields == null) return null;
            var e = new RomAnimeEntry
            {
                Id = id,
                ImageWidthTiles = (int)U.atoi(U.at(fields, 0)),
                Option = U.at(fields, 1),
                FramePointer = U.atoh(U.at(fields, 2)),
                TSAPointer = U.atoh(U.at(fields, 3)),
                ImagePointer = U.atoh(U.at(fields, 4)),
                PalettePointer = U.atoh(U.at(fields, 5)),
                Name = U.at(fields, 6),
            };
            if (e.ImageWidthTiles <= 0) e.ImageWidthTiles = 30; // WF default

            // Precompute IsFrameTable with the explicit-ROM overload so the entry's
            // IsFrameTable property is self-contained (no CoreState.ROM read).
            e.IsFrameTable = U.isSafetyOffset(e.FramePointer, rom);

            e.TSAList = GetPointerListCount(rom, e.TSAPointer);
            e.ImageList = GetPointerListCount(rom, e.ImagePointer);
            e.PaletteList = GetPalettePointerListCount(
                rom, e.PalettePointer, e.FramePointer, e.Option, out bool palInPlace);
            e.PaletteIsInPlaceBlock = palInPlace;
            return e;
        }

        // Ported from WF GetPointerListCount: p32 the slot, then walk consecutive
        // u32 pointers until the first non-pointer; if none, fall back to the single
        // dereferenced offset.
        static List<uint> GetPointerListCount(ROM rom, uint p)
        {
            var ret = new List<uint>();
            if (!U.isSafetyOffset(p, rom)) return ret;
            uint a = rom.p32(p);
            if (!U.isSafetyOffset(a, rom)) return ret;

            uint length = (uint)rom.Data.Length - 4;
            for (; a < length; a += 4)
            {
                uint p2 = rom.u32(a);
                if (!U.isSafetyPointer(p2, rom)) break;
                ret.Add(U.toOffset(p2));
            }
            if (ret.Count <= 0) ret.Add(U.toOffset(a));
            return ret;
        }

        // Ported from WF GetPalettePointerListCount: like GetPointerListCount, but
        // the no-pointer fallback expands per-frame (COMMONPALETTE = one; small
        // FRAME count = one 32-byte palette per frame; else one).
        // <paramref name="inPlaceBlock"/> reports whether the result came from the
        // no-pointer-LIST fallback (the palette is a single shared / in-place
        // 32-byte block, NOT a list of repointable slots) — the import then writes
        // that block raw in-place rather than repointing a slot (review #1).
        static List<uint> GetPalettePointerListCount(ROM rom, uint p, uint framePointer,
            string option, out bool inPlaceBlock)
        {
            inPlaceBlock = false;
            var ret = new List<uint>();
            if (!U.isSafetyOffset(p, rom)) return ret;
            uint a = rom.p32(p);
            if (!U.isSafetyOffset(a, rom)) return ret;

            uint length = (uint)rom.Data.Length - 4;
            for (; a < length; a += 4)
            {
                uint p2 = rom.u32(a);
                if (!U.isSafetyPointer(p2, rom)) break;
                ret.Add(U.toOffset(p2));
            }
            if (ret.Count <= 0)
            {
                // The PALETTE pointer did NOT resolve to a pointer-LIST: the palette
                // bytes live in-place at `a` (COMMONPALETTE / FIXEDCOUNT fallback).
                inPlaceBlock = true;
                if (option == "COMMONPALETTE")
                {
                    ret.Add(U.toOffset(a));
                }
                else if (framePointer < 0x100)
                {
                    for (uint i = 0; i < framePointer; i++)
                        ret.Add(U.toOffset(a + (i * (2 * 16))));
                }
                else
                {
                    ret.Add(U.toOffset(a));
                }
            }
            return ret;
        }

        /// <summary>
        /// Count the renderable frames for an entry (WF <c>GetFrameCountLow</c> for
        /// frame-table entries; the fixed <c>FramePointer</c> count otherwise).
        /// Always &gt;= 1 when there is any image data. Never throws.
        /// </summary>
        public static int GetFrameCount(ROM rom, RomAnimeEntry e)
        {
            if (rom == null || e == null) return 0;
            if (!e.IsFrameTable)
            {
                // Fixed-count entry: FramePointer IS the count (WF iterates 0..FramePointer).
                int n = (int)e.FramePointer;
                if (n <= 0) n = Math.Max(e.ImageList.Count, 1);
                return n;
            }

            uint addr = rom.p32(e.FramePointer);
            if (!U.isSafetyOffset(addr, rom)) return Math.Max(e.ImageList.Count, 1);

            uint limit = (uint)Math.Min((ulong)addr + FRAME_TABLE_LIMIT, (ulong)rom.Data.Length);
            int i;
            for (i = 0; addr + 2 <= rom.Data.Length && addr < limit; i++, addr += 4)
            {
                uint fid = rom.u16(addr);
                if (fid == 0xFFFF) break;
            }
            return Math.Max(i, 1);
        }

        /// <summary>
        /// Resolve a UI frame index to the stored frame ID used to index the
        /// TSA/IMAGE/PALETTE lists (WF <c>Draw</c>): for a frame-table entry, walk
        /// the <c>{id, wait}</c> table; otherwise the ID equals the UI index.
        /// Returns -1 when the frame is past the <c>0xFFFF</c> terminator / out of
        /// the fixed count. Never throws.
        /// </summary>
        public static int ResolveFrameId(ROM rom, RomAnimeEntry e, int frameIndex)
        {
            if (rom == null || e == null || frameIndex < 0) return -1;
            if (!e.IsFrameTable)
            {
                int count = GetFrameCount(rom, e);
                if (frameIndex >= count) return -1;
                return frameIndex;
            }

            uint addr = rom.p32(e.FramePointer);
            if (!U.isSafetyOffset(addr, rom)) return -1;

            uint id = 0;
            for (int i = 0; i < frameIndex + 1; i++, addr += 4)
            {
                if (addr + 2 > rom.Data.Length) return -1;
                id = rom.u16(addr);
                if (id == 0xFFFF) return -1;
            }
            return (int)id;
        }

        /// <summary>
        /// Render one frame of a <c>romanime_</c> entry to an RGBA image (WF
        /// <c>Draw</c> -&gt; <c>DrawDirect</c> -&gt; <c>DrawImageLow</c>):
        /// resolve the UI frame to a stored ID, pick that ID's TSA/IMAGE/PALETTE
        /// offsets (clamped to the last list entry like WF), LZ77-decompress the
        /// TSA + IMAGE, compute <c>height = CalcHeightbyTSA(width, tsaLen)</c>
        /// (capped at 128), and decode via the plain-TSA
        /// <see cref="ImageUtilCore.DecodeTSA"/> with an opaque index 0 (the magic
        /// BG is a full background). Returns <c>null</c> (never throws) on any
        /// null / out-of-bounds / corrupt / truncated input.
        /// </summary>
        public static IImage TryRenderFrame(ROM rom, RomAnimeEntry e, int frameIndex)
        {
            if (rom == null || rom.Data == null || e == null) return null;
            if (CoreState.ImageService == null) return null;

            int id = ResolveFrameId(rom, e, frameIndex);
            if (id < 0) return null;

            if (!TryPickFrameOffset(e.TSAList, id, out uint tsa)) return null;
            if (!TryPickFrameOffset(e.ImageList, id, out uint image)) return null;
            if (!TryPickFrameOffset(e.PaletteList, id, out uint palette)) return null;

            return RenderImageLow(rom, e.ImageWidthTiles, tsa, image, palette);
        }

        // WF DrawDirect's list pick: clamp the index to the last list entry.
        static bool TryPickFrameOffset(List<uint> list, int index, out uint offset)
        {
            offset = 0;
            if (list == null || list.Count <= 0) return false;
            offset = (index >= list.Count) ? list[list.Count - 1] : list[index];
            return true;
        }

        /// <summary>
        /// Render one frame straight from its resolved TSA / IMAGE / PALETTE ROM offsets
        /// (the WF <c>DrawImageLow</c> body), bypassing the UI-frame -&gt; stored-id
        /// resolution. Used by the multi-frame export/GIF walk (#1230) where the stored
        /// frame id differs from the UI frame index. Returns <c>null</c> (never throws)
        /// on any null / out-of-bounds / corrupt input.
        /// </summary>
        public static IImage RenderFromOffsets(ROM rom, int imageWidthTiles, uint tsa, uint image, uint palette)
        {
            if (rom == null || rom.Data == null) return null;
            if (CoreState.ImageService == null) return null;
            return RenderImageLow(rom, imageWidthTiles, tsa, image, palette);
        }

        // WF DrawImageLow: LZ77-decompress TSA + IMAGE, read the RAW palette, decode.
        static IImage RenderImageLow(ROM rom, int imageWidthTiles, uint tsa, uint image, uint palette)
        {
            if (imageWidthTiles <= 0) return null;

            byte[] tsaByte = DecompressGuarded(rom, tsa);
            if (tsaByte == null || tsaByte.Length <= 0) return null;
            byte[] imageByte = DecompressGuarded(rom, image);
            if (imageByte == null || imageByte.Length <= 0) return null;

            byte[] pal = ReadRawPalette(rom, palette, PALETTE_BYTES);
            if (pal == null) return null;

            int widthPx = imageWidthTiles * 8;
            int height = CalcHeightbyTSA(widthPx, tsaByte.Length);
            if (height > MAX_HEIGHT) height = MAX_HEIGHT;
            if (height <= 0) return null;

            // Plain-TSA decode (the WF ByteToImage16Tile(...tsa...) equivalent). The
            // magic BG is opaque, so index 0 renders OPAQUE (WF blits with no key).
            return ImageUtilCore.DecodeTSA(imageByte, tsaByte, pal,
                widthPx / 8, height / 8, true, 0, opaqueIndex0: true);
        }

        /// <summary>
        /// Import a single PNG frame (the inverse of <see cref="TryRenderFrame"/>):
        /// resolve the UI frame to its stored ID and repoint THAT ID's IMAGE + TSA
        /// list slots with the encoded image / tsa, then write the 16-color palette
        /// to the frame's palette target — REPOINTING the per-frame pointer-list slot
        /// when the palette is a list, or OVERWRITING the shared / per-frame 32-byte
        /// block RAW in-place when it is not (the new palette ALWAYS lands somewhere;
        /// a frame whose palette is not addressable is rejected with ZERO mutation —
        /// review #1). The pixels are already
        /// QUANTIZED indexed bytes (1 palette byte/pixel) + a RAW 16-color GBA
        /// palette (32 bytes) — the caller quantizes via
        /// <c>ImageImportService.LoadAndQuantize(owner, w, h, 16)</c>, exactly like
        /// <c>BigCGViewerView.ImportPng_Click</c>; raw RGBA is NEVER accepted.
        ///
        /// <para><b>Atomic.</b> validate-all-before-mutate; a lazy
        /// <c>rom.Data.Clone()</c> snapshot (after the encode succeeds, before the
        /// first write) + length-aware byte-identical fault restore (#885/#923)
        /// means a FAILED import mutates ZERO bytes. All writes go through the no-
        /// undoData <c>write_p32</c> / <c>write_range</c> path so they land in the
        /// caller's ambient <c>ROM.BeginUndoScope</c>.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM.</param>
        /// <param name="e">The resolved entry (from <see cref="Resolve"/>).</param>
        /// <param name="frameIndex">UI frame index being replaced.</param>
        /// <param name="indexedPixels">Quantized 4bpp indices, 1 byte/pixel.</param>
        /// <param name="gbaPalette16">RAW 16-color GBA palette (exactly 32 bytes).</param>
        /// <param name="width">Source width in pixels (must equal ImageWidthTiles*8).</param>
        /// <param name="height">Source height in pixels (multiple of 8, &lt;= 128).</param>
        /// <param name="error">Empty on success; a user-facing message on failure.</param>
        /// <returns>true on success; false (with <paramref name="error"/> set and
        /// ZERO ROM mutation) on any validation or write failure. Never throws.</returns>
        public static bool ImportFrame(ROM rom, RomAnimeEntry e, int frameIndex,
            byte[] indexedPixels, byte[] gbaPalette16, int width, int height, out string error)
        {
            error = "";
            if (rom == null || rom.RomInfo == null || rom.Data == null)
            { error = "ROM not loaded."; return false; }
            if (e == null)
            { error = R.Error("No animation entry selected."); return false; }
            if (indexedPixels == null || gbaPalette16 == null)
            { error = R.Error("Invalid image data."); return false; }

            int expectWidth = e.ImageWidthTiles * 8;
            if (width != expectWidth)
            {
                error = R.Error(
                    "The animation frame must be {0} pixels wide.\r\n\r\nSelected image width: {1}.",
                    expectWidth, width);
                return false;
            }
            if (height <= 0 || height % 8 != 0 || height > MAX_HEIGHT)
            {
                error = R.Error(
                    "The animation frame height must be a multiple of 8 and at most {0} pixels.\r\n\r\nSelected image height: {1}.",
                    MAX_HEIGHT, height);
                return false;
            }
            if ((long)indexedPixels.Length < (long)width * height)
            { error = R.Error("Image pixel data is missing or too short."); return false; }
            // >16-color sources are rejected upstream by LoadAndQuantize; re-check
            // the palette length here so a bad caller cannot write a wrong palette.
            if (gbaPalette16.Length != PALETTE_BYTES)
            { error = R.Error("The animation palette must be exactly {0} bytes (16 colors).", PALETTE_BYTES); return false; }

            // Resolve the stored frame ID + its IMAGE / TSA list slots.
            int id = ResolveFrameId(rom, e, frameIndex);
            if (id < 0)
            { error = R.Error("The selected frame cannot be imported (no addressable slot)."); return false; }

            if (!TryFrameListSlot(rom, e.ImagePointer, id, out uint imageSlot))
            { error = R.Error("The animation image pointer slot is not addressable for this frame."); return false; }
            if (!TryFrameListSlot(rom, e.TSAPointer, id, out uint tsaSlot))
            { error = R.Error("The animation TSA pointer slot is not addressable for this frame."); return false; }

            // Resolve the palette write target (validate-all-before-mutate). The new
            // 16-color palette MUST always land somewhere — silently skipping it
            // would pair the new indices with the OLD colors (review #1). Three cases:
            //   (a) per-frame pointer-LIST slot  -> REPOINT that slot,
            //   (b) in-place 32-byte block       -> overwrite RAW in-place,
            //   (c) neither addressable          -> FAIL (no mutation).
            uint paletteSlot = 0;   // (a) the list slot to repoint
            uint paletteInPlace = 0;// (b) the in-place offset to overwrite
            bool paletteRepoint = TryPaletteSlot(rom, e, id, out paletteSlot);
            bool paletteWriteRaw = false;
            if (!paletteRepoint)
            {
                // (b) in-place block: the palette bytes live at the resolved offset
                // (COMMONPALETTE id 0, else this frame's per-frame block). Confirm
                // the full 32-byte block is in-bounds.
                int palId = e.IsCommonPalette ? 0 : id;
                if (e.PaletteIsInPlaceBlock
                    && palId >= 0 && palId < e.PaletteList.Count
                    && IsRegionSafe(rom, e.PaletteList[palId], PALETTE_BYTES))
                {
                    paletteInPlace = e.PaletteList[palId];
                    paletteWriteRaw = true;
                }
                else
                {
                    // (c) no addressable palette target -> refuse (no mutation).
                    error = R.Error("The animation palette is not addressable for this frame.");
                    return false;
                }
            }

            // Encode (NO mutation yet). EncodeTSA wants indexed pixels (1 byte/px).
            ImageImportCore.TSAEncodeResult enc;
            try { enc = ImageImportCore.EncodeTSA(indexedPixels, width, height); }
            catch (Exception ex) { error = "Animation frame encode failed: " + ex.Message; return false; }
            if (enc == null || enc.TileData == null || enc.TSAData == null)
            { error = R.Error("Failed to encode the animation frame image."); return false; }

            // Lazy snapshot for the byte-identical restore — taken only after the
            // encode succeeds, right before the first write.
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                // IMAGE + TSA are LZ77-written + repointed; PALETTE is raw 32 bytes.
                uint imgAddr = ImageImportCore.WriteCompressedToROM(rom, enc.TileData, imageSlot);
                if (imgAddr == U.NOT_FOUND)
                { RestoreSnapshot(rom, snap); error = R._("Failed to write image. Check ROM free space."); return false; }

                uint tsaAddr = ImageImportCore.WriteCompressedToROM(rom, enc.TSAData, tsaSlot);
                if (tsaAddr == U.NOT_FOUND)
                { RestoreSnapshot(rom, snap); error = R._("Failed to write TSA. Check ROM free space."); return false; }

                // PALETTE always lands somewhere (review #1): (a) repoint a list slot
                // to a fresh 32-byte block, or (b) overwrite the in-place block RAW.
                if (paletteWriteRaw)
                {
                    rom.write_range(paletteInPlace, gbaPalette16); // ambient undo
                }
                else
                {
                    uint palAddr = ImageImportCore.WriteRawToROM(rom, gbaPalette16, paletteSlot);
                    if (palAddr == U.NOT_FOUND)
                    { RestoreSnapshot(rom, snap); error = R._("Failed to write palette. Check ROM free space."); return false; }
                }
                return true;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                error = "Animation frame import failed: " + ex.Message;
                return false;
            }
        }

        // Resolve the in-ROM pointer-list SLOT (the address holding the per-frame
        // pointer) for `id`. Requires the list pointer to be a real pointer-list AND
        // the slot to be in-bounds; refuses a single-entry shared-pointer list when
        // id > 0 (truly unaddressable — would corrupt a shared slot).
        static bool TryFrameListSlot(ROM rom, uint listPointer, int id, out uint slot)
        {
            slot = 0;
            if (rom == null || id < 0) return false;
            if (!U.isSafetyOffset(listPointer, rom)) return false;
            uint baseAddr = rom.p32(listPointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return false;

            // The slot must itself currently hold a pointer (it is a pointer-list).
            uint candidate = baseAddr + (uint)(id * 4);
            if ((ulong)candidate + 4 > (ulong)rom.Data.Length) return false;
            uint existing = rom.u32(candidate);
            if (!U.isSafetyPointer(existing, rom)) return false;

            slot = candidate;
            return true;
        }

        // Resolve the palette slot to write. COMMONPALETTE -> the single shared slot
        // (id 0). Otherwise the per-frame palette slot. Returns false (skip palette
        // write) when there is no addressable palette slot.
        static bool TryPaletteSlot(ROM rom, RomAnimeEntry e, int id, out uint slot)
        {
            int palId = e.IsCommonPalette ? 0 : id;
            return TryFrameListSlot(rom, e.PalettePointer, palId, out slot);
        }

        /// <summary>
        /// Read the IMAGE/TSA/PALETTE list offsets actually rendered for a given UI
        /// frame (used by export to dump the exact frame the user is viewing).
        /// Returns false when any of the three is unresolved. Never throws.
        /// </summary>
        public static bool TryGetFrameOffsets(ROM rom, RomAnimeEntry e, int frameIndex,
            out uint tsa, out uint image, out uint palette)
        {
            tsa = image = palette = 0;
            if (rom == null || e == null) return false;
            int id = ResolveFrameId(rom, e, frameIndex);
            if (id < 0) return false;
            return TryPickFrameOffset(e.TSAList, id, out tsa)
                && TryPickFrameOffset(e.ImageList, id, out image)
                && TryPickFrameOffset(e.PaletteList, id, out palette);
        }

        // ---- small local guards (each precedent image-core keeps its own) ----

        /// <summary>
        /// WF <c>ImageUtil.CalcHeightbyTSA(width, tsa_size, align=8)</c> ported
        /// verbatim: rows of (width/8) tiles, two bytes per TSA entry, aligned up to
        /// a multiple of 8 pixels. Public so tests can assert parity.
        /// </summary>
        public static int CalcHeightbyTSA(int width, int tsaSize, int align = 8)
        {
            if (width < 8 || align <= 0) return 0;
            width = width / 8;
            tsaSize = tsaSize / 2;
            if (width <= 0) return 0;

            int height = tsaSize / width;
            if (tsaSize % width != 0) height++;
            if (height % align != 0) height += align;
            return height * 8 / align * align;
        }

        // Full 4-byte LZ77 header guard + truncation guard, then decompress.
        static byte[] DecompressGuarded(ROM rom, uint addr)
        {
            if (!U.isSafetyOffset(addr, rom)) return null;
            if (!IsRegionSafe(rom, addr, LZ77_HEADER_BYTES)) return null;
            uint compressedSize = LZ77.getCompressedSize(rom.Data, addr);
            if (compressedSize == 0) return null;
            if ((ulong)addr + compressedSize > (ulong)rom.Data.Length) return null;
            byte[] data = LZ77.decompress(rom.Data, addr);
            return (data == null || data.Length == 0) ? null : data;
        }

        // Read a fixed-size RAW palette region; null when truncated/out of bounds.
        static byte[] ReadRawPalette(ROM rom, uint addr, int sizeBytes)
        {
            if (!U.isSafetyOffset(addr, rom)) return null;
            if (!IsRegionSafe(rom, addr, sizeBytes)) return null;
            byte[] pal = new byte[sizeBytes];
            Array.Copy(rom.Data, addr, pal, 0, sizeBytes);
            return pal;
        }

        static bool IsRegionSafe(ROM rom, uint addr, int bytes)
        {
            if (rom == null || rom.Data == null) return false;
            if (bytes <= 0) return false;
            return (ulong)addr + (ulong)bytes <= (ulong)rom.Data.Length;
        }

        // Length-aware byte-identical restore for the fault path (#885/#923).
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom == null || snap == null) return;
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }
    }
}
