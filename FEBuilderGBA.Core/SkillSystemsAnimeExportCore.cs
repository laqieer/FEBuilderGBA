// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform, READ-ONLY Export helper for the SkillSystems skill
// animation (.txt script + per-frame PNGs + optional animated GIF). Ported
// from WF `FEBuilderGBA/ImageUtilSkillSystemsAnimeCreator.cs`:
//   * Export()        (~lines 310-421)
//   * SkipCode()      (~lines 24-69)
//   * DrawFrameImage()(~lines 164-232)
//
// IMPORT is intentionally OUT OF SCOPE — it stays in the WinForms file and is
// tracked as a separate PR (it needs RecycleAddress / Undo plumbing). This
// file performs ZERO ROM mutation.
//
// Anime data layout (mirrors the WF comments):
//   anime_address ──(SkipCode)──► anime_config_address
//     For FE8J (is_multibyte)  : config == anime_address (no embedded program).
//     For FE8U                 : a per-skill program template (skillanimtemplate
//                                *.dmp) is prepended; config = anime_address +
//                                template.Length. The "defender" template marks
//                                a defence-skill anime (the leading "D" line).
//   anime_config:
//     +0  POIN frames        (u16 id, u16 wait) pairs, 0xFFFF id = terminator
//     +4  POIN tsalist       p32(tsalist + id*4) → LZ77 TSA
//     +8  POIN graphiclist   p32(graphiclist + id*4) → LZ77 OBJ tiles
//     +12 POIN palettelist   p32(palettelist + id*4) → 0x20 raw palette
//     +16 WORD sound_id      (u32; the leading "S{id:X04}" line)
//
// Per-frame render (mirrors WF DrawFrameImage):
//   obj  = LZ77.decompress(graphic); tsa = LZ77.decompress(tsa)
//   width  = 240 (SCREEN_WIDTH)
//   height = CalcHeightbyTSA(240, tsa.Length); clamp to >= 160
//   bitmap = ByteToImage16Tile(width, height, obj, palette, tsa)  [WF]
//          ≈ ImageUtilCore.DecodeTSA(obj, tsa, palette, 240/8, height/8, opaqueIndex0:true)
//
// CORRECTION 1: ImageUtilCore.DecodeTSA takes TILE COUNTS (it multiplies ×8
// internally), so we pass screenWidthTiles = 240/8 = 30 and
// screenHeightTiles = height/8, NOT pixel dimensions.
//
// CORRECTION 5 (index-0 transparency): WF renders the frame via
// ByteToImage16Tile which emits palette index 0 as the actual ROM palette
// colour (it is NOT treated as transparent). The WF Export then saves that
// bitmap verbatim. To reproduce the WF output we therefore pass
// opaqueIndex0: true to DecodeTSA.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// One rendered skill-animation frame: its OBJ id (used for the per-frame
    /// PNG filename, matching WF <c>"g" + id.ToString("000")</c>), the wait
    /// value (60-fps game frames), and the rendered image.
    /// </summary>
    public sealed class SkillAnimeFrame
    {
        public uint Id;
        public uint Wait;
        public IImage Image;
    }

    /// <summary>
    /// Result of <see cref="SkillSystemsAnimeExportCore.ExportSkillAnimation"/>.
    /// </summary>
    public sealed class SkillAnimeExportResult
    {
        public List<SkillAnimeFrame> Frames = new List<SkillAnimeFrame>();
        public bool IsDefender;
        public uint SoundId;
        public string Error = "";
    }

    /// <summary>
    /// READ-ONLY cross-platform export seam for SkillSystems skill animations.
    /// Mirrors WF <c>ImageUtilSkillSystemsAnimeCreator.Export</c>.
    /// </summary>
    public static class SkillSystemsAnimeExportCore
    {
        public const int SCREEN_WIDTH  = 240;
        public const int SCREEN_HEIGHT = 160;

        // The two FE8U per-skill program templates. Order matters only for the
        // defender-flag check (we test the longer/non-defender first, like WF).
        static readonly string[] TemplateFiles = new string[]
        {
            "skillanimtemplate_2016_11_04.dmp",
            "skillanimtemplate_defender_2017_01_24.dmp",
        };

        /// <summary>
        /// Resolve the anime-config address from an anime address (mirrors WF
        /// <c>SkipCode</c>). For FE8J (<c>is_multibyte</c>) the config IS the
        /// anime address (no embedded program). For FE8U a per-skill program
        /// template is prepended; we read 0x150 bytes and memcmp against each
        /// template — on a match the config is <c>animeAddress + dmp.Length</c>
        /// and the "defender" template sets <paramref name="isDefender"/>.
        /// Returns <c>U.NOT_FOUND</c> when no template matches (FE8U) or the
        /// offset is unsafe.
        /// </summary>
        public static uint SkipCode(ROM rom, uint animeAddress, out bool isDefender)
        {
            isDefender = false;
            if (rom == null || rom.RomInfo == null) return U.NOT_FOUND;
            if (!U.isSafetyOffset(animeAddress, rom)) return U.NOT_FOUND;

            if (rom.RomInfo.is_multibyte)
            {
                // FE8J: no program prefix; config == anime address.
                return animeAddress;
            }

            // FE8U: a program template is embedded at the head of the anime.
            // We need 0x150 bytes to compare against the longer template. Guard
            // the LAST byte of that window (animeAddress + 0x150 - 1), not the
            // byte AFTER it — otherwise a valid window ending exactly at EOF
            // (start == len - 0x150) is wrongly rejected (codebase multi-byte
            // read-guard pattern).
            if (!U.isSafetyOffset(animeAddress + 0x150 - 1, rom)) return U.NOT_FOUND;
            byte[] bin = rom.getBinaryData(animeAddress, 0x150);

            foreach (string name in TemplateFiles)
            {
                string path = Path.Combine(
                    CoreState.BaseDirectory, "config", "patch2", "FE8U", "skill", name);
                if (!File.Exists(path)) continue;

                byte[] template = File.ReadAllBytes(path);
                if (template.Length == 0 || template.Length > bin.Length) continue;

                if (!PrefixEquals(bin, template)) continue;

                isDefender = name.IndexOf("defender", StringComparison.Ordinal) >= 0;
                return animeAddress + (uint)template.Length;
            }

            return U.NOT_FOUND;
        }

        // Compare the first template.Length bytes of bin against template.
        // (Core U.memcmp requires equal lengths; the defender template is
        // shorter than the 0x150 read window, so we do a prefix compare.)
        static bool PrefixEquals(byte[] bin, byte[] template)
        {
            for (int i = 0; i < template.Length; i++)
            {
                if (bin[i] != template[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Render every frame of the skill animation pointed to by
        /// <paramref name="animePointer"/> (a ROM OFFSET; <c>U.toOffset</c> is
        /// idempotent so passing an already-offset value is correct). Returns
        /// the ordered frame list, the defender flag, and the sound id. On any
        /// structural error the result carries a non-empty <c>Error</c> and an
        /// empty frame list (never throws).
        /// </summary>
        public static SkillAnimeExportResult ExportSkillAnimation(ROM rom, uint animePointer)
        {
            var result = new SkillAnimeExportResult();

            if (rom == null || rom.Data == null)
            {
                result.Error = "ROM is null.";
                return result;
            }
            if (CoreState.ImageService == null)
            {
                result.Error = "Image service is not initialized.";
                return result;
            }
            // Rom-identity guard: SkipCode/template paths and isSafetyOffset
            // resolve against CoreState (BaseDirectory + the active ROM). Refuse
            // foreign ROM instances so we never mix template data across ROMs.
            if (!ReferenceEquals(rom, CoreState.ROM))
            {
                result.Error = "ExportSkillAnimation requires the active CoreState.ROM.";
                return result;
            }

            uint addr = U.toOffset(animePointer);
            if (!U.isSafetyOffset(addr, rom))
            {
                result.Error = "BAD ANIME ADDRESS";
                return result;
            }

            uint cfg = SkipCode(rom, addr, out bool isDefender);
            if (cfg == U.NOT_FOUND)
            {
                result.Error = "BAD ANIME CONFIG ADDRESS (SkipCode failed)";
                return result;
            }
            result.IsDefender = isDefender;

            if (cfg + (4 * 5) > (uint)rom.Data.Length)
            {
                result.Error = "BAD ANIME CONFIG ADDRESS (out of range)";
                return result;
            }

            uint frames      = rom.p32(cfg + (4 * 0));
            uint tsalist     = rom.p32(cfg + (4 * 1));
            uint graphiclist = rom.p32(cfg + (4 * 2));
            uint palettelist = rom.p32(cfg + (4 * 3));
            uint soundId     = rom.u32(cfg + (4 * 4));
            result.SoundId = soundId;

            if (!U.isSafetyOffset(frames, rom))      { result.Error = "BAD ANIME_FRAMES"; return result; }
            if (!U.isSafetyOffset(tsalist, rom))     { result.Error = "BAD TSALIST"; return result; }
            if (!U.isSafetyOffset(graphiclist, rom)) { result.Error = "BAD GRAPHICS"; return result; }
            if (!U.isSafetyOffset(palettelist, rom)) { result.Error = "BAD PALETTELIST"; return result; }

            // Re-use a rendered IImage per OBJ id so duplicate frames share the
            // same bitmap (matches WF animeHash). Filename is derived from id.
            var cache = new Dictionary<uint, IImage>();

            // The frame stream is uncompressed; cap the walk to 1 MB like WF.
            uint limitter = frames + 1024 * 1024;
            if (limitter > (uint)rom.Data.Length) limitter = (uint)rom.Data.Length;

            for (uint n = frames; n + 4 <= limitter; n += 4)
            {
                if (!U.isSafetyOffset(n + 4, rom)) break;
                uint id = rom.u16(n + 0);
                uint wait = rom.u16(n + 2);
                if (id == 0xFFFF) break; // terminator

                IImage img;
                if (!cache.TryGetValue(id, out img))
                {
                    img = RenderFrame(rom, id, graphiclist, tsalist, palettelist, out string err);
                    if (img == null)
                    {
                        result.Error = err;
                        result.Frames.Clear();
                        return result;
                    }
                    cache[id] = img;
                }

                result.Frames.Add(new SkillAnimeFrame { Id = id, Wait = wait, Image = img });
            }

            return result;
        }

        /// <summary>
        /// Render a single frame image (mirrors WF <c>DrawFrameImage</c>):
        /// deref obj/tsa/pal via id, LZ77-decompress obj+tsa, render via
        /// <see cref="ImageUtilCore.DecodeTSA"/> with the WF index-0-opaque
        /// convention. Returns null + error on any structural fault.
        /// </summary>
        static IImage RenderFrame(ROM rom, uint id,
            uint graphiclist, uint tsalist, uint palettelist, out string error)
        {
            error = "";

            uint objPointer = graphiclist + (id * 4);
            uint tsaPointer = tsalist + (id * 4);
            uint palPointer = palettelist + (id * 4);

            if (!U.isSafetyOffset(objPointer + 4, rom)) { error = "BAD OBJ_POINTER"; return null; }
            if (!U.isSafetyOffset(tsaPointer + 4, rom)) { error = "BAD TSA_POINTER"; return null; }
            if (!U.isSafetyOffset(palPointer + 4, rom)) { error = "BAD PAL_POINTER"; return null; }

            uint objOffset = rom.p32(objPointer);
            uint tsaOffset = rom.p32(tsaPointer);
            uint palOffset = rom.p32(palPointer);

            if (!U.isSafetyOffset(objOffset, rom))       { error = "BAD OBJ_OFFSET"; return null; }
            if (!U.isSafetyOffset(tsaOffset, rom))       { error = "BAD TSA_OFFSET"; return null; }
            if (!U.isSafetyOffset(palOffset + 0x20, rom)) { error = "BAD PAL_OFFSET"; return null; }

            byte[] obj = LZ77.decompress(rom.Data, objOffset);
            byte[] tsa = LZ77.decompress(rom.Data, tsaOffset);
            if (obj == null || tsa == null) { error = "LZ77 decompress failed"; return null; }

            byte[] palette = rom.getBinaryData(palOffset, 0x20);

            int width = SCREEN_WIDTH;
            int height = CalcHeightByTsa(width, tsa.Length);
            if (height < 160) height = 160; // WF clamp

            // CORRECTION 1: DecodeTSA expects TILE COUNTS (multiplies ×8).
            // CORRECTION 5: opaqueIndex0:true so index 0 = ROM palette colour
            // (matches WF ByteToImage16Tile output).
            IImage img = ImageUtilCore.DecodeTSA(
                obj, tsa, palette,
                width / 8, height / 8,
                is4bpp: true, tsaOffset: 0, opaqueIndex0: true);
            if (img == null) { error = "DecodeTSA returned null"; return null; }
            return img;
        }

        /// <summary>
        /// Mirror of WF <c>ImageUtil.CalcHeightbyTSA(width, tsa_size, align=8)</c>:
        /// width/8 columns, tsa_size/2 entries; round up rows, then round the
        /// row count up to the alignment, return pixel height.
        /// </summary>
        public static int CalcHeightByTsa(int width, int tsaByteLen, int align = 8)
        {
            int cols = width / 8;
            if (cols <= 0) return 8;
            int entries = tsaByteLen / 2;
            int height = entries / cols;
            if (entries % cols != 0) height++;
            if (height % align != 0) height += align;
            return height * 8 / align * align;
        }

        /// <summary>
        /// Build the .txt script lines (mirrors WF <c>Export</c> line writing):
        /// optional leading <c>D</c> (defender), optional <c>S{soundId:X04}</c>,
        /// then one <c>{wait} {basename}g{id:000}.png</c> line per frame.
        /// <paramref name="songName"/> is appended as a comment on the S-line
        /// when supplied (matches WF's "#play sound &lt;name&gt;").
        /// </summary>
        public static List<string> BuildScriptLines(
            SkillAnimeExportResult result, string basename, string songName = null)
        {
            var lines = new List<string>();
            if (result == null) return lines;

            if (result.IsDefender)
            {
                lines.Add("D #is defender anim");
            }
            if (result.SoundId > 0)
            {
                string s = "S" + result.SoundId.ToString("X04", CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(songName))
                {
                    s += " #play sound " + songName;
                }
                lines.Add(s);
            }

            string safeBase = (basename ?? "").Replace(" ", "_");
            foreach (var f in result.Frames)
            {
                string imagefilename = safeBase + "g" + f.Id.ToString("000", CultureInfo.InvariantCulture) + ".png";
                lines.Add(f.Wait.ToString(CultureInfo.InvariantCulture) + " " + imagefilename);
            }
            return lines;
        }
    }
}
