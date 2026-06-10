// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform recursive instrument-set (voicegroup) EXPORT seam (#1057, PR1).
//
// READ-ONLY port of the WinForms SongInstrumentForm.ExportAllLow / ExportOneLow
// (FEBuilderGBA/SongInstrumentForm.cs ~L933-1135). Walks a voicegroup (a table of
// 12-byte voice entries, max 128, stopping at the first invalid type/pointer just
// like the WF read-loop), emits a TSV index of 4 hex header bytes + a filename or
// @SELF/@BROKENDATA token per voice, and writes the per-voice side files
// (.DirectSound.bin / .Wave.bin / .Multi.keys.bin) and the recursive nested index
// files (.Drum.instrument / .Multi.instrument) through caller-supplied delegates.
//
// Core stays free of System.IO.File / System.Windows.Forms / InputFormRef: the
// host passes `writeFile(name, bytes)` and `writeLines(name, lines)` delegates,
// and Core only ever passes RELATIVE filename tokens (never absolute paths) — the
// host resolves them against the chosen output directory (Copilot plan review pt
// 3). The recursive index files route through the SAME delegates so nested side
// files land in the same directory.
//
// DIVERGENCE FROM WF (documented, intentional):
//   * The WF decorative trailing "//{name}" comment column (ifr.LoopCallback) is
//     DROPPED — it is GUI fingerprint-config dependent and not portable to Core.
//   * Everything else (column layout, self-ref tokens, side-file lengths, the
//     0x40 trailing-column omission) is verbatim WF.
using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform recursive instrument-set (voicegroup) EXPORT (#1057, PR1).
    /// Pure read-only port of the WinForms <c>SongInstrumentForm.ExportAllLow</c> /
    /// <c>ExportOneLow</c>. Emits a TSV index + per-voice side files through
    /// caller-supplied delegates so Core stays free of <c>System.IO.File</c> /
    /// WinForms. Every address read is bounds-guarded; never throws.
    /// </summary>
    public static class SongInstrumentSetCore
    {
        const int BlockSize = 12;       // 12-byte voice entry (WF InputFormRef BlockSize)
        const int MaxVoices = 128;      // WF read-loop cap (SongInstrumentForm.cs L65)

        /// <summary>
        /// Explicit-ROM range check: is <c>[addr, addr+length)</c> fully inside
        /// <paramref name="rom"/>? Used instead of <c>U.isSafetyLength</c> because
        /// the latter reads the AMBIENT <c>CoreState.ROM</c> length — the export
        /// must validate against the PASSED ROM only (Copilot review pt 3). Mirrors
        /// the same `addr+length` overflow-safe long-math guard.
        /// </summary>
        static bool SafeLen(ROM rom, uint addr, uint length)
        {
            if (rom == null) return false;
            long end = (long)addr + length;
            return addr >= 0x00000100 && addr < 0x02000000
                && end <= rom.Data.Length && length < 0x00200000;
        }

        /// <summary>
        /// Strip the LAST file extension from a name (Core has no
        /// <c>System.IO.Path</c> dependency by policy). Mirrors WF
        /// <c>Path.GetFileNameWithoutExtension</c> applied to a bare filename, so a
        /// nested index <c>vg0x00.Drum.instrument</c> yields the recurse basename
        /// <c>vg0x00.Drum</c> (Copilot review pt 1 — child side files become
        /// <c>vg0x00.Drum0xNN.Wave.bin</c>, never colliding with another group's
        /// <c>vg0x00.Wave.bin</c>).
        /// </summary>
        static string StripLastExtension(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        /// <summary>
        /// Export the voicegroup (instrument set) at <paramref name="vocaBaseAddress"/>
        /// (a GBA pointer or offset) to a TSV index plus per-voice side files.
        /// </summary>
        /// <param name="rom">The loaded ROM (read-only).</param>
        /// <param name="vocaBaseAddress">The voicegroup base (pointer or offset).</param>
        /// <param name="baseName">The index file's bare name (no extension, no
        /// directory) — used as the filename PREFIX for the side files, exactly like
        /// WF <c>Path.GetFileNameWithoutExtension(filename)</c>. e.g. "voicegroup".</param>
        /// <param name="writeFile">Delegate writing a RELATIVE-named binary side file
        /// (<c>name</c> is a bare filename; the host resolves it against the output
        /// directory).</param>
        /// <param name="writeLines">Delegate writing a RELATIVE-named text index file
        /// (the top-level index AND any nested <c>.Drum.instrument</c> /
        /// <c>.Multi.instrument</c> recurse through here).</param>
        public static void ExportAll(
            ROM rom,
            uint vocaBaseAddress,
            string baseName,
            Action<string, byte[]> writeFile,
            Action<string, IEnumerable<string>> writeLines)
        {
            if (rom == null || writeFile == null || writeLines == null) return;
            // baseName is part of every emitted filename; a null/empty name would
            // throw on the `baseName + ".instrument"` concatenation below, breaking
            // the "never throws" contract (Copilot bot inline finding). Guard it.
            if (string.IsNullOrEmpty(baseName)) return;
            // The top-level index file's own name = baseName + ".instrument".
            ExportAllLow(rom, vocaBaseAddress, baseName, baseName + ".instrument",
                         writeFile, writeLines, isNest: false);
        }

        // Port of WF ExportAllLow(string filename, uint voca_baseaddress, bool isNest).
        // `indexFileName` is the relative name of THIS index file (the top-level one,
        // or a nested .Drum.instrument / .Multi.instrument).
        static void ExportAllLow(
            ROM rom,
            uint vocaBaseAddress,
            string baseName,
            string indexFileName,
            Action<string, byte[]> writeFile,
            Action<string, IEnumerable<string>> writeLines,
            bool isNest)
        {
            uint vocaBase = U.toOffset(vocaBaseAddress);
            if (!U.isSafetyOffset(vocaBase, rom))
            {
                return;
            }

            int dataCount = CountVoices(rom, vocaBase);

            List<string> lines = new List<string>();
            uint addr = vocaBase;
            for (int i = 0; i < dataCount; i++, addr += BlockSize)
            {
                string str = ExportOneLow(rom, addr, i, vocaBase, dataCount, baseName,
                                          writeFile, writeLines, isNest);
                if (str == "")
                {
                    continue;
                }
                lines.Add(str);
            }
            writeLines(indexFileName, lines);
        }

        // Port of WF ExportOneLow. Returns the TSV row, or "" to SKIP this voice
        // (broken/unsafe pointer — exact WF behavior).
        static string ExportOneLow(
            ROM rom,
            uint addr,
            int index,
            uint vocaBaseAddress,
            int dataCount,
            string baseName,
            Action<string, byte[]> writeFile,
            Action<string, IEnumerable<string>> writeLines,
            bool isNest)
        {
            // The 12-byte voice entry must be fully in range before any indexed read.
            if (!SafeLen(rom, addr, BlockSize)) return "";

            StringBuilder sb = new StringBuilder();
            sb.Append(U.ToHexString(rom.u8(addr + 0))); sb.Append("\t");
            sb.Append(U.ToHexString(rom.u8(addr + 1))); sb.Append("\t");
            sb.Append(U.ToHexString(rom.u8(addr + 2))); sb.Append("\t");
            sb.Append(U.ToHexString(rom.u8(addr + 3))); sb.Append("\t");

            uint type = rom.u8(addr);
            if (type == 0x00
                || type == 0x08
                || type == 0x10
                || type == 0x18)
            {//directsound waveデータ.
                uint songdataAddr = rom.p32(addr + 4);
                if (!U.isSafetyOffset(songdataAddr, rom))
                {
                    return "";
                }
                uint sampleLength = SongDirectSoundWavCore.GetDirectSoundWaveDataLength(rom, songdataAddr);
                // Full-range guard against the PASSED rom (not the ambient ROM).
                if (!SafeLen(rom, songdataAddr + 12 + 4, sampleLength))
                {
                    return "";
                }
                if (!SongDirectSoundWavCore.IsDirectSoundData(rom, songdataAddr))
                {//壊れたデータ (broken data — skip the row)
                    return "";
                }

                string waveFilename = baseName + U.To0xHexString(index) + ".DirectSound.bin";
                byte[] bin = rom.getBinaryData(songdataAddr, 12 + 4 + sampleLength);
                writeFile(waveFilename, bin);

                sb.Append(waveFilename); sb.Append("\t");
            }
            else if (type == 0x03
                || type == 0x0B)
            {//波形データ (Wave Memory — fixed 16 bytes)
                uint songdataAddr = rom.p32(addr + 4);
                if (!U.isSafetyOffset(songdataAddr, rom))
                {
                    return "";
                }
                // Require the FULL 16 bytes in range so getBinaryData can't truncate
                // at EOF into a short .Wave.bin (Copilot review pt 2).
                if (!SafeLen(rom, songdataAddr, 16))
                {
                    return "";
                }

                byte[] bin = rom.getBinaryData(songdataAddr, 16);
                string waveFilename = baseName + U.To0xHexString(index) + ".Wave.bin";
                writeFile(waveFilename, bin);

                sb.Append(waveFilename); sb.Append("\t");
            }
            else if (type == 0x80)
            {//ドラム (Drum — recurse into the sub-voicegroup)
                uint drumVoices = rom.p32(addr + 4);
                if (!U.isSafetyOffset(drumVoices, rom))
                {
                    return "";
                }

                string token = ResolveSelfRefOrRecurse(
                    rom, drumVoices, vocaBaseAddress, dataCount, isNest,
                    baseName, index, ".Drum.instrument", writeFile, writeLines);
                sb.Append(token); sb.Append("\t");
            }
            else if (type == 0x40)
            {//マルチサンプル (Multisample — recurse + write the 128-byte keymap)
                uint multisampleVoices = rom.p32(addr + 4);
                uint sampleLocation = rom.p32(addr + 8);
                if (!U.isSafetyOffset(multisampleVoices, rom))
                {
                    return "";
                }
                if (!U.isSafetyOffset(sampleLocation, rom))
                {
                    return "";
                }
                // Require the FULL 128 bytes of the keymap in range so getBinaryData
                // can't truncate at EOF into a short .Multi.keys.bin (Copilot review
                // pt 2). Checked BEFORE recursing so a short keymap skips the row
                // entirely (no nested index written for an unwritable keymap).
                if (!SafeLen(rom, sampleLocation, 128))
                {
                    return "";
                }

                string token = ResolveSelfRefOrRecurse(
                    rom, multisampleVoices, vocaBaseAddress, dataCount, isNest,
                    baseName, index, ".Multi.instrument", writeFile, writeLines);
                sb.Append(token); sb.Append("\t");

                byte[] bin = rom.getBinaryData(sampleLocation, 128);
                string keysFilename = baseName + U.To0xHexString(index) + ".Multi.keys.bin";
                writeFile(keysFilename, bin);

                sb.Append(keysFilename); sb.Append("\t");
            }
            else
            {//その他 (no pointer — emit the raw +4..+7 bytes)
                sb.Append(U.ToHexString(rom.u8(addr + 4))); sb.Append("\t");
                sb.Append(U.ToHexString(rom.u8(addr + 5))); sb.Append("\t");
                sb.Append(U.ToHexString(rom.u8(addr + 6))); sb.Append("\t");
                sb.Append(U.ToHexString(rom.u8(addr + 7))); sb.Append("\t");
            }

            if (type != 0x40)
            {//マルチサンプル以外は、最後の4バイトはデータです (every type but 0x40 trails +8..+11)
                sb.Append(U.ToHexString(rom.u8(addr + 8))); sb.Append("\t");
                sb.Append(U.ToHexString(rom.u8(addr + 9))); sb.Append("\t");
                sb.Append(U.ToHexString(rom.u8(addr + 10))); sb.Append("\t");
                sb.Append(U.ToHexString(rom.u8(addr + 11)));
            }

            // NOTE: the WF decorative trailing "//{name}" comment column
            // (ifr.LoopCallback) is intentionally DROPPED here — it is GUI
            // fingerprint-config dependent and not portable to Core.
            return sb.ToString();
        }

        /// <summary>
        /// Port of the WF 0x80/0x40 self-reference / recurse branch. Returns the
        /// TSV token for a nested voicegroup pointer:
        /// <list type="bullet">
        ///   <item><c>@SELF+0</c> when the sub-voicegroup equals THIS voicegroup base.</item>
        ///   <item>(only while nested) <c>@SELF+{hex}</c> when the sub-voicegroup is
        ///         an in-range offset INTO this voicegroup (nonzero offset included);
        ///         <c>@BROKENDATA</c> when nested but out of range.</item>
        ///   <item>otherwise (top level): recurse via <see cref="ExportAllLow"/> into
        ///         <c>{baseName}{0xINDEX}{suffix}</c> and return that filename.</item>
        /// </list>
        /// </summary>
        static string ResolveSelfRefOrRecurse(
            ROM rom,
            uint subVoices,           // offset (already isSafetyOffset-checked)
            uint vocaBaseAddress,     // offset
            int dataCount,
            bool isNest,
            string baseName,
            int index,
            string suffix,            // ".Drum.instrument" / ".Multi.instrument"
            Action<string, byte[]> writeFile,
            Action<string, IEnumerable<string>> writeLines)
        {
            uint vocaBase = vocaBaseAddress;
            if (subVoices == vocaBase)
            {
                return "@SELF+0";
            }
            if (isNest)
            {
                // WF: voca_endaddress = voca_base + ((DataCount + 1) * BlockSize).
                uint vocaEnd = vocaBase + (uint)((dataCount + 1) * BlockSize);
                if (subVoices >= vocaBase && subVoices < vocaEnd)
                {
                    return "@SELF+" + U.ToHexString(subVoices - vocaBase);
                }
                return "@BROKENDATA";
            }

            // Top level: recurse and reference the nested index file by name. The
            // nested voicegroup's side files use the nested-index basename (the WF
            // `Path.GetFileNameWithoutExtension(filename)` step) — e.g. children of
            // `vg0x00.Drum.instrument` are `vg0x00.Drum0xNN.Wave.bin`, NOT
            // `vg0x00.Wave.bin` — so two top-level groups whose child voice index is
            // 0 never collide (Copilot review pt 1).
            string nestedFilename = baseName + U.To0xHexString(index) + suffix;
            string nestedBaseName = StripLastExtension(nestedFilename);
            ExportAllLow(rom, subVoices, nestedBaseName, nestedFilename,
                         writeFile, writeLines, isNest: true);
            return nestedFilename;
        }

        /// <summary>
        /// Count the voicegroup's defined-prefix voices: walk 12-byte blocks (max
        /// 128) and stop at the first one that fails the WF read-loop validity
        /// predicate (<see cref="IsValidVoice"/>). Mirrors WF
        /// <c>SongInstrumentForm.Init</c>'s read-loop / the Avalonia
        /// <c>SongInstrumentViewModel.CountDefinedPrefix</c>.
        /// </summary>
        static int CountVoices(ROM rom, uint vocaBase)
        {
            int count = 0;
            for (int i = 0; i < MaxVoices; i++)
            {
                uint addr = vocaBase + (uint)(i * BlockSize);
                if (!SafeLen(rom, addr, BlockSize)) break;
                if (!IsValidVoice(rom, addr)) break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// WF read-loop validity predicate (SongInstrumentForm.cs L70-114): a
        /// pointer-bearing type (DirectSound 0x00/0x08/0x10/0x18, Wave 0x03/0x0B,
        /// Drum 0x80) requires a safe +4 pointer; Multisample 0x40 requires safe +4
        /// AND +8 pointers; the data-less square/noise types (0x01/0x02/0x04/0x09/
        /// 0x0A/0x0C) are always valid; everything else stops the scan.
        /// </summary>
        static bool IsValidVoice(ROM rom, uint addr)
        {
            uint type = rom.u8(addr);
            if (type == 0x00 || type == 0x08 || type == 0x10 || type == 0x18
                || type == 0x03 || type == 0x0B || type == 0x80)
            {
                uint p = rom.u32(addr + 4);
                return U.isSafetyPointer(p, rom);
            }
            if (type == 0x40)
            {
                uint p1 = rom.u32(addr + 4);
                if (!U.isSafetyPointer(p1, rom)) return false;
                uint p2 = rom.u32(addr + 8);
                return U.isSafetyPointer(p2, rom);
            }
            if (type == 0x01 || type == 0x02 || type == 0x04
                || type == 0x09 || type == 0x0A || type == 0x0C)
            {
                return true;
            }
            return false;
        }
    }
}
