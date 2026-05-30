using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform instrument-set (voicegroup) discovery, ported from the
    /// WinForms <c>PatchUtil.SearchInstrumentSet</c> / <c>SearchInstrumentSetLow</c>
    /// (FEBuilderGBA/PatchUtil.cs). No WinForms / System.Drawing dependency —
    /// takes a <see cref="ROM"/> parameter instead of reading <c>Program.ROM</c>.
    ///
    /// Scans <c>config/data/song_instrumentset_&lt;ver&gt;.txt</c> for known
    /// native-instrument-map / voicegroup byte signatures and returns the
    /// addresses found in the ROM, seeded with the song's "Current" instrument
    /// pointer. Used by the Avalonia SongTrack instrument-set browser; the
    /// WinForms <c>PatchUtil.SearchInstrumentSet</c> delegates here so there is
    /// a single source of truth. (#787)
    /// </summary>
    public static class InstrumentSetCore
    {
        // Mirrors WinForms PatchUtil.g_InstrumentSet — a single memoised result.
        // Keyed by (ROM identity, currentData) so a ROM swap or a different
        // song's instrument pointer recomputes. Reset by ClearCache(), which
        // WinForms PatchUtil.ClearCache() also calls on ROM change.
        static List<AddrResult> g_InstrumentSet = null;
        static ROM g_InstrumentSetRom = null;
        static uint g_InstrumentSetCurrentData = 0;

        /// <summary>
        /// Invalidate the memoised instrument-set result. Mirrors the
        /// <c>g_InstrumentSet = null</c> reset in WinForms
        /// <c>PatchUtil.ClearCache</c> (PatchUtil.cs:64).
        /// </summary>
        public static void ClearCache()
        {
            g_InstrumentSet = null;
            g_InstrumentSetRom = null;
            g_InstrumentSetCurrentData = 0;
        }

        /// <summary>
        /// Memoised wrapper. Returns the instrument-set list for
        /// <paramref name="rom"/>, seeded with <paramref name="currentData"/>
        /// as the "Current" entry. Mirrors WinForms
        /// <c>PatchUtil.SearchInstrumentSet(uint)</c>.
        /// </summary>
        public static List<AddrResult> SearchInstrumentSet(ROM rom, uint currentData)
        {
            if (rom == null)
            {
                return new List<AddrResult>();
            }
            if (g_InstrumentSet == null
                || !object.ReferenceEquals(g_InstrumentSetRom, rom)
                || g_InstrumentSetCurrentData != currentData)
            {
                g_InstrumentSet = SearchInstrumentSetLow(rom, U.ConfigDataFilename("song_instrumentset_", rom), currentData);
                g_InstrumentSetRom = rom;
                g_InstrumentSetCurrentData = currentData;
            }
            return g_InstrumentSet;
        }

        /// <summary>
        /// Faithful port of WinForms <c>PatchUtil.SearchInstrumentSetLow</c>.
        /// Reads the signature list from <paramref name="filename"/>, greps each
        /// hex pattern out of <paramref name="rom"/> below
        /// <c>compress_image_borderline_address</c>, and returns the matching
        /// instrument-set pointers. The list always begins with the
        /// "Current" seed (<paramref name="currentData"/>).
        /// </summary>
        public static List<AddrResult> SearchInstrumentSetLow(ROM rom, string filename, uint currentData)
        {
            List<AddrResult> iset = new List<AddrResult>();
            iset.Add(new AddrResult(currentData, U.ToHexString(U.toPointer(currentData)) + "=Current"));

            if (rom?.RomInfo == null || filename == null || !File.Exists(filename))
            {
                return iset;
            }

            bool hasNimap2 = false;

            string[] lines = File.ReadAllLines(filename);
            string version = rom.RomInfo.VersionToFilename;
            for (int i = 0; i < lines.Length; i++)
            {
                if (U.IsComment(lines[i]))
                {
                    continue;
                }
                string line = U.ClipComment(lines[i]);
                string[] sp = line.Split('\t');
                if (sp.Length < 3)
                {
                    continue;
                }
                if (sp[1] != version)
                {
                    if (sp[1] != "ALL")
                    {
                        continue;
                    }
                }
                string[] hexStrings = sp[2].Split(' ');
                byte[] need = new byte[hexStrings.Length];
                for (int n = 0; n < hexStrings.Length; n++)
                {
                    need[n] = (byte)U.atoh(hexStrings[n]);
                }

                //Grepして調べる 結構重い.
                uint v = U.Grep(rom.Data, need, rom.RomInfo.compress_image_borderline_address, 0, 4);
                if (v == U.NOT_FOUND)
                {
                    continue;
                }

                // NOTE (faithful WinForms port): sp[0] is the full first column
                // of song_instrumentset_*.txt. The "AllInstrument" row's first
                // column is the bare string "AllInstrument", so its deref branch
                // fires. The NIMAP rows' first columns are however
                // "NatveInstrumentMap2(NIMAP2)" / "NatveInstrumentMap(NIMAP)"
                // (with the parenthetical alias), so the equality checks below
                // against the bare names never match — meaning the
                // NIMAP2-supersedes-NIMAP suppression is dormant in WinForms as
                // well. Reproduced verbatim here so behaviour is identical.
                if (sp[0] == "AllInstrument")
                {//All Instrumentは、マルチトラックしかないので、データのポインタでサンプルを取ります。
                    v = U.GrepPointer(rom.Data, v, rom.RomInfo.compress_image_borderline_address);
                    if (v == U.NOT_FOUND)
                    {
                        continue;
                    }
                    v -= 8;
                }
                else if (sp[0] == "NatveInstrumentMap2")
                {
                    hasNimap2 = true;
                }
                else if (sp[0] == "NatveInstrumentMap")
                {
                    if (hasNimap2)
                    {
                        continue;
                    }
                }

                v = U.toPointer(v);
                iset.Add(new AddrResult(v, U.ToHexString(v) + "=" + sp[0]));
            }
            return iset;
        }
    }
}
