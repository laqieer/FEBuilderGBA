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

        // =================================================================
        // IMPORT (#1057, PR2) — recursive ROM-MUTATING instrument-set import.
        //
        // Inverse of ExportAll: re-import a TSV index (+ its per-voice side
        // files + recursive nested .Drum.instrument / .Multi.instrument indexes)
        // back into the ROM as a freshly-appended voicegroup blob, and return the
        // new voicegroup base OFFSET. Port of the WinForms
        // SongInstrumentForm.ImportAllLow / ImportOneLow / WriteBackData
        // (FEBuilderGBA/SongInstrumentForm.cs ~L1137-1414), with the established
        // ROM-mutating safety patterns applied rigorously:
        //
        //   * WHOLE-GRAPH VALIDATE-BEFORE-MUTATE (Copilot finding 6): the entire
        //     import graph (root + all nested voicegroups + all side files) is
        //     parsed + validated in-memory FIRST. If ANY file is missing, any
        //     length is insane, or any @SELF / nesting target fails to resolve,
        //     ImportAll returns U.NOT_FOUND with NO ROM mutation.
        //   * SINGLE TRANSACTION (finding 6): every append + every fixup runs under
        //     the caller's ONE ambient undo scope. Nested voicegroups do NOT own
        //     independent commits/snapshots — they are part of the same transaction.
        //   * BYTE-IDENTICAL FAULT RESTORE (finding 6, #885/#923/#1090): a defensive
        //     snapshot is taken before the mutation phase; on ANY fault during
        //     mutation the ROM is restored byte-identically (length-aware) and
        //     U.NOT_FOUND is returned. A failed import changes ZERO bytes.
        //   * TWO-PHASE DEFERRED WRITE-BACK (finding 7): phase (a) allocates the
        //     base offset of EVERY voicegroup (root + nested) and EVERY side-data
        //     blob first; phase (b) resolves all fixups via rom.write_p32 (which
        //     takes OFFSETS and applies U.toPointer internally). Every append is
        //     4-byte aligned.
        //   * @SELF+offset PARSE TRAP (finding 4): @SELF+... is parsed from the RAW
        //     token (Core never Path.Combines), the hex is validated, the offset is
        //     required to be 12-byte aligned, and the resolved target (the voicegroup
        //     base + offset) is required to stay inside the imported voicegroup blob
        //     + terminator range. Misaligned / out-of-range / unparseable @SELF is
        //     REJECTED with NO mutation. A nonzero @SELF+0C-style offset is handled
        //     correctly (ExportAll emits these).
        //   * @BROKENDATA (finding 5): preserved exactly as WF — mapped to @SELF+0
        //     (the in-range root base), so a re-import of a @BROKENDATA row round-trips
        //     to a self-reference at the voicegroup base.
        // =================================================================

        /// <summary>The fixed 0xC-byte terminator (three u32 0s) appended after the
        /// last voice — WF ImportAllLow's "終端データ".</summary>
        const int TerminatorSize = 12;

        /// <summary>The kind of a deferred pointer-slot fixup (the in-memory model
        /// of WF DataWriteHelper, refined per Copilot finding 7).</summary>
        enum FixupKind
        {
            /// <summary>@SELF / @BROKENDATA: write THIS voicegroup's base + offset.</summary>
            SelfRelative,
            /// <summary>Nested voicegroup pointer: write the child voicegroup's allocated base.</summary>
            NestedVoicegroup,
            /// <summary>Side-data blob (a sample / keymap byte[]): append it, write its base.</summary>
            SideData,
        }

        /// <summary>A deferred pointer-slot fixup inside a single voicegroup blob.
        /// The slot lives at <see cref="SlotInBlob"/> bytes from this voicegroup's
        /// own base; the target depends on <see cref="Kind"/>.</summary>
        sealed class Fixup
        {
            public FixupKind Kind;
            public int SlotInBlob;          // pointer-slot offset WITHIN this voicegroup's blob
            public uint SelfOffset;         // SelfRelative: offset added to this voicegroup base
            public ParsedVoicegroup Child;  // NestedVoicegroup: the child voicegroup
            public byte[] Data;             // SideData: the raw blob to append
            public uint SideDataBase;       // SideData: the allocated base (filled in phase a)
        }

        /// <summary>An in-memory parsed voicegroup: the blob (with placeholder 0s in
        /// every pointer slot) + the deferred fixups + the allocated base (filled in
        /// during the mutation phase). The tree is built ENTIRELY before any ROM
        /// write (validate-before-mutate).</summary>
        sealed class ParsedVoicegroup
        {
            public byte[] Blob;                 // voice rows + terminator (pointers = 0)
            public List<Fixup> Fixups = new List<Fixup>();
            public uint AllocatedBase;          // filled in phase (a) of WriteGraph
        }

        /// <summary>
        /// Recursively import a TSV instrument-set index (the inverse of
        /// <see cref="ExportAll"/>) into the ROM and return the new voicegroup base
        /// OFFSET, or <see cref="U.NOT_FOUND"/> on any failure (with the ROM
        /// restored byte-identical — zero bytes changed).
        /// </summary>
        /// <param name="rom">The loaded ROM (mutated under the caller's ambient undo
        /// scope on success).</param>
        /// <param name="indexName">The relative name of the root index file (as the
        /// host's <paramref name="readLines"/> understands it).</param>
        /// <param name="readLines">Reads a RELATIVE-named text index file into its
        /// lines, or returns <c>null</c> when it does not exist. The host resolves
        /// the relative name against the chosen index directory.</param>
        /// <param name="readFile">Reads a RELATIVE-named binary side file into its
        /// bytes, or returns <c>null</c> when it does not exist.</param>
        /// <param name="appendBinaryData">Allocator that appends a 4-byte-aligned
        /// blob to free space under the ambient undo scope and returns its base
        /// OFFSET (or <see cref="U.NOT_FOUND"/> on failure). The Avalonia host wires
        /// <c>CoreState.AppendBinaryData</c>; pass <c>null</c> to use the built-in
        /// ROM-end appender.</param>
        /// <param name="error">Human-readable failure reason on
        /// <see cref="U.NOT_FOUND"/>; <c>null</c> on success.</param>
        public static uint ImportAll(
            ROM rom,
            string indexName,
            Func<string, string[]> readLines,
            Func<string, byte[]> readFile,
            Func<byte[], uint> appendBinaryData,
            out string error)
        {
            error = null;
            if (rom == null)
            {
                error = R._("ROM is not loaded.");
                return U.NOT_FOUND;
            }
            if (string.IsNullOrEmpty(indexName) || readLines == null || readFile == null)
            {
                error = R._("Instrument set import requires an index file and read delegates.");
                return U.NOT_FOUND;
            }

            // ---- PHASE 1: parse + validate the WHOLE graph in memory (no ROM write).
            // Guard against a self-referential nesting cycle (a malformed file set
            // whose nested index transitively re-references itself) blowing the stack.
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ParsedVoicegroup root;
            try
            {
                root = ParseVoicegroup(indexName, readLines, readFile, visiting, out error);
            }
            catch (Exception ex)
            {
                error = R._("Instrument set import failed while reading: {0}", ex.Message);
                return U.NOT_FOUND;
            }
            if (root == null)
            {
                // error already set by ParseVoicegroup
                if (string.IsNullOrEmpty(error))
                    error = R._("Could not parse the instrument set index.");
                return U.NOT_FOUND;
            }

            // ---- PHASE 2: mutate under ONE transaction with a byte-identical restore.
            // The caller has already opened the ambient undo scope (the View's
            // UndoService.Begin). The snapshot guarantees a FAILED import mutates
            // ZERO bytes even though some appends/fixups may have already run.
            byte[] snapshot = (byte[])rom.Data.Clone();
            try
            {
                Func<byte[], uint> appender = appendBinaryData ?? (buf => AppendToRomEnd(rom, buf));

                // Phase (a): allocate the base of EVERY voicegroup (root + nested,
                // depth-first) and EVERY side-data blob. After this every
                // ParsedVoicegroup.AllocatedBase and every SideData fixup target is
                // known, so the SELF / nested-base fixups resolve to real offsets.
                if (!AllocateGraph(root, appender, out error))
                {
                    RestoreSnapshot(rom, snapshot);
                    if (string.IsNullOrEmpty(error))
                        error = R._("Failed to allocate ROM space for the instrument set.");
                    return U.NOT_FOUND;
                }

                // Phase (b): resolve every deferred pointer-slot fixup. write_p32
                // takes an OFFSET and applies U.toPointer internally — pass target
                // OFFSETS, never pre-converted GBA pointers.
                ResolveFixups(rom, root);

                return root.AllocatedBase;
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snapshot);
                error = R._("Instrument set import failed: {0}", ex.Message);
                return U.NOT_FOUND;
            }
        }

        // ---- PHASE 1 helpers (read-only, in-memory) -------------------------

        /// <summary>
        /// Parse one index file (+ its side files + nested indexes) into a
        /// <see cref="ParsedVoicegroup"/>. Validates EVERYTHING (file existence,
        /// lengths, @SELF alignment/range) before returning. Returns <c>null</c>
        /// (with <paramref name="error"/> set) on the first validation failure —
        /// NO ROM is touched here. Recurses through nested .Drum.instrument /
        /// .Multi.instrument indexes via the same delegates (same transaction).
        /// </summary>
        static ParsedVoicegroup ParseVoicegroup(
            string indexName,
            Func<string, string[]> readLines,
            Func<string, byte[]> readFile,
            HashSet<string> visiting,
            out string error)
        {
            error = null;

            if (!visiting.Add(indexName))
            {
                error = R._("The instrument set index references itself in a cycle: {0}", indexName);
                return null;
            }
            try
            {
                string[] lines = readLines(indexName);
                if (lines == null)
                {
                    error = R._("The instrument set index file is missing: {0}", indexName);
                    return null;
                }

                var bin = new List<byte>();
                var fixups = new List<Fixup>();
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!ParseVoiceRow(lines[i], i, bin, fixups, readLines, readFile, visiting, out error))
                        return null;
                }

                // Terminator: three u32 0s (WF "終端データ"). Self-ref targets are
                // validated against the blob INCLUDING this terminator (so a child
                // record may legitimately point one record past the last voice).
                U.append_u32(bin, 0);
                U.append_u32(bin, 0);
                U.append_u32(bin, 0);

                byte[] blob = bin.ToArray();

                // Validate every @SELF target against the final blob length now that
                // the terminator is appended (finding 4 — in-range requirement). The
                // SlotInBlob itself must also be inside the voice-row region.
                foreach (var f in fixups)
                {
                    if (f.Kind != FixupKind.SelfRelative) continue;
                    // 12-byte alignment (finding 4).
                    if ((f.SelfOffset % (uint)BlockSize) != 0)
                    {
                        error = R._("A @SELF offset (0x{0:X}) is not 12-byte aligned.", f.SelfOffset);
                        return null;
                    }
                    // In-range: base + offset must land on a record boundary inside
                    // the blob (voice rows + terminator). The last legal target is the
                    // terminator record start (== blob length - TerminatorSize).
                    if ((long)f.SelfOffset + BlockSize > blob.Length)
                    {
                        error = R._("A @SELF offset (0x{0:X}) points outside the instrument set.", f.SelfOffset);
                        return null;
                    }
                }

                return new ParsedVoicegroup { Blob = blob, Fixups = fixups };
            }
            finally
            {
                visiting.Remove(indexName);
            }
        }

        /// <summary>
        /// Port of WF <c>ImportOneLow</c>: parse one TSV row, append its 12 (or
        /// terminator-bound) blob bytes with placeholder 0s in pointer slots, and
        /// record the deferred fixups. Validates row arity + side-file existence +
        /// @SELF tokens; returns <c>false</c> (with <paramref name="error"/> set) on
        /// the first failure. NO ROM write.
        /// </summary>
        static bool ParseVoiceRow(
            string line,
            int index,
            List<byte> bin,
            List<Fixup> fixups,
            Func<string, string[]> readLines,
            Func<string, byte[]> readFile,
            HashSet<string> visiting,
            out string error)
        {
            error = null;

            // Skip comment / other-language lines (WF ImportOneLow).
            if (U.IsComment(line) || U.OtherLangLine(line))
                return true;
            line = U.ClipComment(line);
            if (line == "")
                return true;

            string[] sp = line.Split(new char[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (sp.Length < 4 + 2)
            {
                error = R._("Instrument row {0} is malformed: at least {1} columns are required.", index + 1, 6);
                return false;
            }

            uint type = U.atoh(sp[0]);
            U.append_u8(bin, type);
            U.append_u8(bin, U.atoh(sp[1]));
            U.append_u8(bin, U.atoh(sp[2]));
            U.append_u8(bin, U.atoh(sp[3]));

            if (type == 0x00 || type == 0x08 || type == 0x10 || type == 0x18
                || type == 0x03 || type == 0x0B)
            {//directsound wave / wave-memory — a side .bin file, repoint to it.
                if (sp.Length < 4 + 1 + 4)
                {
                    error = R._("Instrument row {0} is malformed: at least {1} columns are required.", index + 1, 9);
                    return false;
                }
                byte[] wav = ReadSideFile(sp[4], readFile, out error);
                if (wav == null)
                {
                    if (string.IsNullOrEmpty(error))
                        error = R._("Instrument sample data ({1}) is missing on row {0}.", index + 1, sp[4]);
                    return false;
                }
                fixups.Add(new Fixup { Kind = FixupKind.SideData, SlotInBlob = bin.Count, Data = wav });
                U.append_u32(bin, 0); // placeholder pointer slot (written in phase b)
                U.append_u8(bin, U.atoh(sp[5]));
                U.append_u8(bin, U.atoh(sp[6]));
                U.append_u8(bin, U.atoh(sp[7]));
                U.append_u8(bin, U.atoh(sp[8]));
            }
            else if (type == 0x80)
            {//ドラム — @SELF / @BROKENDATA / nested .Drum.instrument.
                if (sp.Length < 4 + 1 + 4)
                {
                    error = R._("Instrument row {0} is malformed: at least {1} columns are required.", index + 1, 9);
                    return false;
                }
                if (!ParseNestedPointer(sp[4], index, bin, fixups, readLines, readFile, visiting, out error))
                    return false;
                U.append_u8(bin, U.atoh(sp[5]));
                U.append_u8(bin, U.atoh(sp[6]));
                U.append_u8(bin, U.atoh(sp[7]));
                U.append_u8(bin, U.atoh(sp[8]));
            }
            else if (type == 0x40)
            {//マルチサンプル — P4 nested/@SELF + P8 keymap .bin (NO trailing +8..+11).
                if (!ParseNestedPointer(sp[4], index, bin, fixups, readLines, readFile, visiting, out error))
                    return false;

                // P8 keymap side file (sp[5]). The WF tolerates a missing keymap with
                // a non-fatal warning then proceeds to ReadAllBytes (which throws) —
                // we reject cleanly (validate-before-mutate) so no half-graph forms.
                if (sp.Length < 4 + 2)
                {
                    error = R._("Multisample row {0} is missing the keymap column.", index + 1);
                    return false;
                }
                byte[] multi = ReadSideFile(sp[5], readFile, out error);
                if (multi == null)
                {
                    if (string.IsNullOrEmpty(error))
                        error = R._("Multisample keymap data ({1}) is missing on row {0}.", index + 1, sp[5]);
                    return false;
                }
                fixups.Add(new Fixup { Kind = FixupKind.SideData, SlotInBlob = bin.Count, Data = multi });
                U.append_u32(bin, 0); // placeholder keymap pointer slot
            }
            else
            {//その他 — 8 raw bytes, no pointer.
                if (sp.Length < 4 + 4 + 4)
                {
                    error = R._("Instrument row {0} is malformed: at least {1} columns are required.", index + 1, 12);
                    return false;
                }
                U.append_u8(bin, U.atoh(sp[4]));
                U.append_u8(bin, U.atoh(sp[5]));
                U.append_u8(bin, U.atoh(sp[6]));
                U.append_u8(bin, U.atoh(sp[7]));
                U.append_u8(bin, U.atoh(sp[8]));
                U.append_u8(bin, U.atoh(sp[9]));
                U.append_u8(bin, U.atoh(sp[10]));
                U.append_u8(bin, U.atoh(sp[11]));
            }
            return true;
        }

        /// <summary>
        /// Parse a 0x80/0x40 P4 token: a nested .Drum/.Multi index (recurse), a
        /// <c>@SELF+{hex}</c> self-reference, or <c>@BROKENDATA</c> (== @SELF+0).
        /// Appends the placeholder pointer slot + records the fixup. Parses the
        /// @SELF offset from the RAW token (Core never Path.Combines — finding 4);
        /// alignment + range are validated once the blob is finalized (in
        /// <see cref="ParseVoicegroup"/>). NO ROM write.
        /// </summary>
        static bool ParseNestedPointer(
            string token,
            int index,
            List<byte> bin,
            List<Fixup> fixups,
            Func<string, string[]> readLines,
            Func<string, byte[]> readFile,
            HashSet<string> visiting,
            out string error)
        {
            error = null;
            if (token == null) token = "";

            int selfIdx = token.IndexOf("@SELF+", StringComparison.Ordinal);
            if (selfIdx >= 0)
            {
                // Parse the hex AFTER "@SELF+" from the RAW token. U.atoh stops at the
                // first non-hex char; require at least one hex digit so a stray
                // "@SELF+" with no offset is rejected rather than silently == 0.
                string hex = token.Substring(selfIdx + "@SELF+".Length);
                if (hex.Length == 0 || !U.ishex(hex[0]))
                {
                    error = R._("A @SELF token on row {0} has no valid hex offset: {1}", index + 1, token);
                    return false;
                }
                uint relativeOffset = U.atoh(hex);
                fixups.Add(new Fixup { Kind = FixupKind.SelfRelative, SlotInBlob = bin.Count, SelfOffset = relativeOffset });
                U.append_u32(bin, 0);
                return true;
            }
            if (token.IndexOf("@BROKENDATA", StringComparison.Ordinal) >= 0)
            {//ドラム内でドラムがあるような変なデータ — WF maps to @SELF+0.
                fixups.Add(new Fixup { Kind = FixupKind.SelfRelative, SlotInBlob = bin.Count, SelfOffset = 0 });
                U.append_u32(bin, 0);
                return true;
            }

            // Otherwise it is a nested index filename — recurse (same transaction).
            ParsedVoicegroup child = ParseVoicegroup(token, readLines, readFile, visiting, out error);
            if (child == null)
            {
                if (string.IsNullOrEmpty(error))
                    error = R._("Could not import the nested instrument set ({1}) on row {0}.", index + 1, token);
                return false;
            }
            fixups.Add(new Fixup { Kind = FixupKind.NestedVoicegroup, SlotInBlob = bin.Count, Child = child });
            U.append_u32(bin, 0);
            return true;
        }

        /// <summary>Read a side file, rejecting an empty / oversized blob (sanity —
        /// validate-before-mutate). Returns <c>null</c> (with <paramref name="error"/>
        /// set when the cause is a bad length) when the file is missing or insane.</summary>
        static byte[] ReadSideFile(string name, Func<string, byte[]> readFile, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(name)) return null;
            byte[] data = readFile(name);
            if (data == null) return null;       // missing — caller reports
            if (data.Length == 0)
            {
                error = R._("The instrument data file is empty: {0}", name);
                return null;
            }
            if (data.Length >= 0x00200000)
            {
                error = R._("The instrument data file is too large: {0}", name);
                return null;
            }
            return data;
        }

        // ---- PHASE 2 helpers (ROM-mutating, single transaction) -------------

        /// <summary>
        /// Phase (a): allocate the base offset of this voicegroup AND every nested
        /// voicegroup AND every side-data blob, depth-first. After this returns true,
        /// every <see cref="ParsedVoicegroup.AllocatedBase"/> and every
        /// <see cref="FixupKind.SideData"/> blob has a real ROM offset, so the fixups
        /// resolve. Child voicegroups are appended BEFORE their parent's pointer
        /// slots are resolved (the two-phase guarantee). On any alloc failure returns
        /// false; the caller restores the snapshot.
        /// </summary>
        static bool AllocateGraph(ParsedVoicegroup vg, Func<byte[], uint> appender, out string error)
        {
            error = null;

            // Allocate the children + side data first so their bases are known, then
            // this voicegroup's own blob. (Order between siblings is irrelevant —
            // every fixup target is resolved from a recorded base, not a write order.)
            foreach (var f in vg.Fixups)
            {
                if (f.Kind == FixupKind.NestedVoicegroup)
                {
                    if (!AllocateGraph(f.Child, appender, out error))
                        return false;
                }
                else if (f.Kind == FixupKind.SideData)
                {
                    uint dataBase = appender(f.Data);
                    if (dataBase == U.NOT_FOUND) return false;
                    f.SideDataBase = dataBase;
                }
            }

            uint vgBase = appender(vg.Blob);
            if (vgBase == U.NOT_FOUND) return false;
            vg.AllocatedBase = vgBase;
            return true;
        }

        /// <summary>
        /// Phase (b): resolve every deferred pointer-slot fixup, depth-first. Each
        /// slot lives at <c>vg.AllocatedBase + f.SlotInBlob</c>; write_p32 takes the
        /// TARGET OFFSET and applies U.toPointer internally (finding 7 — pass offsets,
        /// never pre-converted GBA pointers).
        /// </summary>
        static void ResolveFixups(ROM rom, ParsedVoicegroup vg)
        {
            foreach (var f in vg.Fixups)
            {
                uint slot = vg.AllocatedBase + (uint)f.SlotInBlob;
                switch (f.Kind)
                {
                    case FixupKind.SelfRelative:
                        rom.write_p32(slot, vg.AllocatedBase + f.SelfOffset);
                        break;
                    case FixupKind.SideData:
                        rom.write_p32(slot, f.SideDataBase);
                        break;
                    case FixupKind.NestedVoicegroup:
                        rom.write_p32(slot, f.Child.AllocatedBase);
                        ResolveFixups(rom, f.Child);
                        break;
                }
            }
        }

        /// <summary>
        /// Built-in 4-byte-aligned ROM-end appender (used when the host passes no
        /// <c>appendBinaryData</c> allocator). Word-aligns the append offset (so the
        /// repointed pointer lands on a 4-byte boundary), resizes, and writes the
        /// blob under the ambient undo scope. Returns the append OFFSET or
        /// <see cref="U.NOT_FOUND"/> on resize failure.
        /// </summary>
        static uint AppendToRomEnd(ROM rom, byte[] blob)
        {
            uint appendOffset = U.Padding4((uint)rom.Data.Length);
            if (!rom.write_resize_data(appendOffset + (uint)blob.Length))
                return U.NOT_FOUND;
            rom.write_range(appendOffset, blob);
            return appendOffset;
        }

        /// <summary>
        /// Length-aware byte-identical restore (the #885/#923 pattern): an append can
        /// GROW rom.Data, so down-resize back to the snapshot length BEFORE the
        /// in-place copy (a naive Array.Copy would leave the grown tail alive).
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snapshot)
        {
            if (rom.Data.Length != snapshot.Length)
                rom.write_resize_data((uint)snapshot.Length);
            Array.Copy(snapshot, rom.Data, snapshot.Length);
        }
    }
}
