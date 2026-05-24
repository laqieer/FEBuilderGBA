// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform port of WinForms ToolAnimationCreator{Form,UserControl} (#500).
//
// The WinForms editor is a 2800-line UI for editing battle / magic / skill /
// map-action / TSA / ROM animations frame-by-frame, with OAM rendering and
// palette blending. Porting the rendering surface is out of scope for #500 —
// this Core file ports only the **data model + I/O surface** that the Avalonia
// view needs in order to surface an Init() flow on real animation context:
//
//   - AnimationTypeEnum mirrors the WF nested enum so callers can pass the
//     same kind discriminator.
//   - MapActionFrame is the per-row record shared by the file-based
//     (Import/Export) and ROM-based (ReadFromRom/WriteToRom) paths.
//   - ParseMapActionScript + FormatMapActionScript provide cross-platform
//     I/O against the same .txt format the WF Export emits.
//   - ReadFromRom + WriteToRom provide the direct-from-ROM path used by the
//     Map Action Animation entry point (no temp file required).
//
// Note: bitmap byte content (PNG → 4bpp + palette) is NOT touched by this Core
// surface. The Avalonia view is a frame list browser + metadata editor; full
// bitmap rewriting is deferred to a follow-up.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Animation kind discriminator — mirrors WinForms
    /// `ToolAnimationCreatorUserControl.AnimationTypeEnum` (file
    /// ToolAnimationCreatorUserControl.cs:254). The numeric values are pinned
    /// down by <c>ToolAnimationCreatorCoreTests.MakeUniqId_TypeValuesAreStable</c>
    /// so any future reorder fails at test time instead of at runtime.
    /// </summary>
    public enum AnimationTypeEnum
    {
        BattleAnime = 0,
        MagicAnime_FEEDitor = 1,
        MagicAnime_CSACreator = 2,
        Skill = 3,
        TSAAnime = 4,
        ROMAnime = 5,
        MapActionAnimation = 6,
    }

    /// <summary>
    /// One row of the Map-Action-Animation frame table. 12 bytes wide on ROM:
    /// <list type="bullet">
    ///   <item><c>byte Wait</c></item>
    ///   <item><c>byte 00</c> (padding)</item>
    ///   <item><c>ushort Sound</c></item>
    ///   <item><c>uint ImagePointer</c> (LZ77-compressed 4bpp tile data)</item>
    ///   <item><c>uint PalettePointer</c> (raw 0x20-byte 16-color palette)</item>
    /// </list>
    /// Both pointer fields are stored here as ROM **offsets** (not GBA
    /// pointers) so callers don't need to repeatedly call
    /// <c>U.toOffset</c>. Conversion to GBA pointer (+0x08000000) is done at
    /// write time inside <see cref="ToolAnimationCreatorCore.WriteToRom"/>.
    /// <br /><br />
    /// <see cref="ImageName"/> is the relative filename when the row was
    /// parsed from a .txt script (null when read from ROM).
    /// </summary>
    public sealed record MapActionFrame(
        uint Wait,
        uint ImagePointer,
        uint PalettePointer,
        uint Sound,
        string? ImageName);

    /// <summary>
    /// Helpers for the Avalonia ToolAnimationCreator entry-point. Surface is
    /// intentionally small — file Parse/Format + ROM Read/Write + bit-packed
    /// uniq-id. The WinForms 2800-line UI is NOT ported.
    /// </summary>
    public static class ToolAnimationCreatorCore
    {
        // 12 bytes per row, matches WF MapActionAnimation layout.
        const int ROW_SIZE = 12;
        // 1MB scan limit mirrors WF safety cap.
        const uint LOOKAHEAD_LIMIT = 1024u * 1024u;

        /// <summary>
        /// Bit-pack a (type, id) into the tab uniq-id the WF editor uses for
        /// tab de-dup (one open tab per kind+id). Layout: type in the high
        /// byte, id in the low 24 bits. Mirrors WF
        /// <c>ToolAnimationCreatorForm.MakeUniqID</c>.
        /// </summary>
        public static uint MakeUniqId(AnimationTypeEnum type, uint id)
        {
            return (((uint)type) << 24) | (id & 0x00FFFFFFu);
        }

        // ================================================================
        // File-based I/O — parity with WF ImageUtilMapActionAnimation.Export
        // ================================================================

        /// <summary>
        /// Parse a Map Action Animation script file (.txt) into a list of
        /// frames. The format is the same one WF
        /// <c>ImageUtilMapActionAnimation.Export</c> emits: one frame per
        /// non-comment line, fields separated by a tab or a space:
        /// <code>
        /// //NAME=optional human name
        /// 4\tframe_a.png
        /// 5\tframe_b.png\t0x1A   ← optional 3rd field = sound (decimal or 0x-hex)
        /// </code>
        /// Lines starting with <c>//</c> are comments; blank lines are
        /// skipped; the <c>//NAME=</c> directive (if present anywhere) is
        /// emitted via the <paramref name="name"/> out param. Lines that
        /// don't parse as a valid frame (no image field, non-numeric wait)
        /// are silently skipped — the importer cannot do anything useful
        /// with them.
        /// </summary>
        /// <exception cref="System.IO.FileNotFoundException">
        /// Thrown when the file does not exist. Missing files are NOT a
        /// silent-success case (Copilot CLI plan-review pt 5 on #500).
        /// </exception>
        public static List<MapActionFrame> ParseMapActionScript(
            string filename, out string? name)
        {
            if (!File.Exists(filename))
            {
                throw new FileNotFoundException(
                    "Map Action Animation script not found", filename);
            }
            name = null;
            var frames = new List<MapActionFrame>();
            foreach (string rawLine in File.ReadAllLines(filename))
            {
                string line = rawLine?.Trim() ?? "";
                if (line.Length == 0) continue;
                // Comment / directive line.
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    const string namePrefix = "//NAME=";
                    if (line.StartsWith(namePrefix, StringComparison.Ordinal))
                    {
                        name = line.Substring(namePrefix.Length).Trim();
                    }
                    continue;
                }

                // Frame line — split on tab OR space. WF accepts both.
                string[] parts = line.Split(
                    new[] { '\t', ' ' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (!uint.TryParse(parts[0], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out uint wait))
                {
                    continue;
                }
                string imageName = parts[1];
                uint sound = 0;
                if (parts.Length >= 3)
                {
                    string soundField = parts[2];
                    if (soundField.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!uint.TryParse(soundField.Substring(2),
                                NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out sound))
                        {
                            sound = 0;
                        }
                    }
                    else
                    {
                        if (!uint.TryParse(soundField, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out sound))
                        {
                            sound = 0;
                        }
                    }
                }
                frames.Add(new MapActionFrame(
                    Wait: wait,
                    ImagePointer: 0,
                    PalettePointer: 0,
                    Sound: sound,
                    ImageName: imageName));
            }
            return frames;
        }

        /// <summary>
        /// Inverse of <see cref="ParseMapActionScript"/> — emit the canonical
        /// tab-separated text format. When <paramref name="name"/> is non-null
        /// the output is prefixed with <c>//NAME=...</c>. Lines with a non-zero
        /// sound get a 3rd hex field; sound == 0 emits 2 fields only.
        /// </summary>
        public static string FormatMapActionScript(
            string? name, IReadOnlyList<MapActionFrame> frames)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(name))
            {
                sb.Append("//NAME=").Append(name).Append('\n');
            }
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                sb.Append(f.Wait.ToString(CultureInfo.InvariantCulture));
                sb.Append('\t');
                sb.Append(f.ImageName ?? "");
                if (f.Sound != 0)
                {
                    sb.Append('\t');
                    sb.Append("0x").Append(f.Sound.ToString("X",
                        CultureInfo.InvariantCulture));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Convenience: write <see cref="FormatMapActionScript"/> output to
        /// disk. Used by <c>ToolAnimationCreatorView.Create_Click</c> when the
        /// view was opened from a .txt source.
        /// </summary>
        public static void WriteMapActionScript(
            string filename,
            string? name,
            IReadOnlyList<MapActionFrame> frames)
        {
            File.WriteAllText(filename, FormatMapActionScript(name, frames));
        }

        // ================================================================
        // ROM-based I/O — direct path used by the Map Action entry point
        // ================================================================

        /// <summary>
        /// Walk the 12-byte-per-row frame table starting at
        /// <paramref name="animeAddress"/> and return one
        /// <see cref="MapActionFrame"/> per row. The scan stops on the first
        /// double-zero terminator (term1 == 0 &amp;&amp; term2 == 0) — matches WF
        /// <c>ImageUtilMapActionAnimation.Export</c> termination — and is
        /// hard-capped at <see cref="LOOKAHEAD_LIMIT"/> bytes (or
        /// <paramref name="frameLimit"/> frames, whichever is smaller).
        /// </summary>
        /// <param name="rom">
        /// ROM to read from. Marked as <c>ROM?</c> because the method
        /// explicitly tolerates a null ROM and returns an empty list
        /// (Copilot CLI inline review on PR #619).
        /// </param>
        /// <param name="animeAddress">
        /// ROM offset OR GBA pointer of the frame table. Converted via
        /// <c>U.toOffset</c> so callers can pass either form.
        /// </param>
        /// <param name="frameLimit">
        /// Optional explicit cap on the number of frames returned. Useful for
        /// unit tests; production callers leave this null.
        /// </param>
        public static List<MapActionFrame> ReadFromRom(
            ROM? rom, uint animeAddress, uint? frameLimit = null)
        {
            var frames = new List<MapActionFrame>();
            if (rom == null || rom.Data == null) return frames;

            uint offset = U.toOffset(animeAddress);
            // Use the ROM-aware safety check so unit tests using a small
            // synthetic ROM aren't bound to the global CoreState.ROM.
            if (!U.isSafetyOffset(offset, rom)) return frames;
            if (offset + ROW_SIZE > rom.Data.Length)
                return frames;

            uint limiter = offset + LOOKAHEAD_LIMIT;
            if (limiter > rom.Data.Length)
                limiter = (uint)rom.Data.Length;

            for (uint n = offset; n + ROW_SIZE <= limiter; n += ROW_SIZE)
            {
                // Use pointer-safe reads for the terminator check (matches
                // WF parity — invalid/out-of-range pointers become 0 via
                // p32, which prevents scanning far into garbage data when
                // the table is corrupted). Copilot CLI inline review on
                // PR #619.
                uint term1 = rom.u32(n);
                uint term2 = rom.p32(n + 4);
                if (term1 == 0 && term2 == 0)
                    break;

                uint wait = rom.u8(n + 0);
                uint sound = rom.u16(n + 2);
                // p32 returns ROM offset (after toOffset), 0 when out of range.
                uint imgOffset = rom.p32(n + 4);
                uint palOffset = rom.p32(n + 8);

                frames.Add(new MapActionFrame(
                    Wait: wait,
                    ImagePointer: imgOffset,
                    PalettePointer: palOffset,
                    Sound: sound,
                    ImageName: null));

                if (frameLimit.HasValue && frames.Count >= frameLimit.Value)
                    break;
            }
            return frames;
        }

        /// <summary>
        /// Write <paramref name="frames"/> back into the 12-byte rows starting
        /// at <paramref name="animeAddress"/>. Each row is written as
        /// <c>wait | 0 | sound | toPointer(image) | toPointer(palette)</c>.
        /// After the last frame, a zero terminator row is written so a
        /// subsequent <see cref="ReadFromRom"/> stops cleanly. The caller is
        /// responsible for ensuring the target ROM range is large enough.
        /// <br /><br />
        /// When <paramref name="undoData"/> is non-null, the writes record into
        /// it for undo. The standard pattern is to set ambient undo via
        /// <c>ROM.BeginUndoScope</c> at the call site, in which case
        /// <paramref name="undoData"/> can stay null (the ambient scope picks
        /// up the writes automatically).
        /// </summary>
        public static void WriteToRom(
            ROM rom,
            uint animeAddress,
            IReadOnlyList<MapActionFrame> frames,
            Undo.UndoData? undoData)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (frames == null) throw new ArgumentNullException(nameof(frames));

            uint offset = U.toOffset(animeAddress);
            // ROM-aware safety check matches ReadFromRom — keeps both ends
            // consistent for unit tests with synthetic small ROMs.
            if (!U.isSafetyOffset(offset, rom))
                throw new ArgumentOutOfRangeException(nameof(animeAddress));
            // Need room for the frames + one terminator row.
            long needed = (long)frames.Count * ROW_SIZE + ROW_SIZE;
            if (offset + needed > rom.Data.Length)
                throw new ArgumentOutOfRangeException(nameof(animeAddress),
                    "Not enough ROM space at the target address");

            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                uint rowAddr = offset + (uint)(i * ROW_SIZE);
                if (undoData != null)
                {
                    rom.write_u8(rowAddr + 0, f.Wait & 0xFF, undoData);
                    rom.write_u8(rowAddr + 1, 0, undoData);
                    rom.write_u16(rowAddr + 2, f.Sound & 0xFFFF, undoData);
                    rom.write_u32(rowAddr + 4,
                        f.ImagePointer == 0 ? 0 : U.toPointer(f.ImagePointer),
                        undoData);
                    rom.write_u32(rowAddr + 8,
                        f.PalettePointer == 0 ? 0 : U.toPointer(f.PalettePointer),
                        undoData);
                }
                else
                {
                    rom.write_u8(rowAddr + 0, f.Wait & 0xFF);
                    rom.write_u8(rowAddr + 1, 0);
                    rom.write_u16(rowAddr + 2, f.Sound & 0xFFFF);
                    rom.write_u32(rowAddr + 4,
                        f.ImagePointer == 0 ? 0 : U.toPointer(f.ImagePointer));
                    rom.write_u32(rowAddr + 8,
                        f.PalettePointer == 0 ? 0 : U.toPointer(f.PalettePointer));
                }
            }
            // Terminator row (8 zero bytes for term1+term2 is enough — the
            // remaining 4 bytes of the 12-byte slot are also zeroed for
            // hygiene so callers don't see stale palette pointers).
            uint termAddr = offset + (uint)(frames.Count * ROW_SIZE);
            if (undoData != null)
            {
                rom.write_u32(termAddr + 0, 0, undoData);
                rom.write_u32(termAddr + 4, 0, undoData);
                rom.write_u32(termAddr + 8, 0, undoData);
            }
            else
            {
                rom.write_u32(termAddr + 0, 0);
                rom.write_u32(termAddr + 4, 0);
                rom.write_u32(termAddr + 8, 0);
            }
        }
    }
}
