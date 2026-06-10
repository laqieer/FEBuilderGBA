// SPDX-License-Identifier: GPL-3.0-or-later
// Cross-platform .s / SondFont voicegroup-assembler IMPORT seam (#1001 PR2).
//
// ROM-MUTATING port of the WinForms SongUtil.ImportS
// (FEBuilderGBA/SongUtil.cs ~L2496-2817). Assembles a GBA `.s` SondFont source
// (the `mid2agb` / `m4a` text format: `.equ` / `.global` / labels / `.byte` /
// `.word` / `.end`, with `.include` / `.section` / `.align` ignored) into one or
// more "global" data blobs, appends them to ROM free space, resolves the
// label-relative `.word` pointers, and repoints the song-table entry at the new
// song header plus the song header's instrument pointer (`+4`) at the
// user-selected instrument set.
//
// Core stays free of System.IO.File / System.Windows.Forms / InputFormRef:
//   * file I/O is supplied via the `readLines` delegate;
//   * free-space allocation is supplied via the `appendBinaryData` delegate
//     (the Avalonia host wires CoreState.AppendBinaryData; a built-in ROM-end
//     appender is used when null);
//   * the WF `InputFormRef.DoEvents` progress pumps are dropped.
//
// The established ROM-mutating safety patterns (#885 / #923 / #1057) are applied:
//   * VOICEGROUP000 / MUSICVOICES SENTINEL (Copilot plan finding): WF seeds
//     `equ["voicegroup000"] = equ["MusicVoices"] = -1` and then blindly indexes
//     `global[(rewrite>>24)&0xFF]` — for the -1 sentinel that is `global[0xFF]`,
//     an out-of-bounds index. Core SPECIAL-CASES the sentinel: a `.word` whose
//     resolved value is the -1 sentinel resolves to `selectedInstrumentAddr`
//     (the song-header instrument pointer / the user-picked voicegroup), NOT
//     `global[255]`. Every NON-sentinel encoded label id is BOUNDS-CHECKED
//     (`globalID < global.Count`) before indexing.
//   * VALIDATE-ALL-BEFORE-MUTATE: the entire `.s` is parsed + resolved into an
//     in-memory image and EVERYTHING is validated (song-table global exists,
//     every `.word` label id is in range or the known sentinel) BEFORE a single
//     ROM byte is written. Any unresolved / out-of-range `.word`, or any parse
//     error, fails with `File:... Line:...` context and ZERO ROM mutation.
//   * SINGLE TRANSACTION + BYTE-IDENTICAL FAULT RESTORE: a defensive snapshot is
//     taken before the mutation phase; the whole import (append every blob +
//     repoint) runs under the caller's ONE ambient undo scope; on ANY fault the
//     ROM is restored byte-AND-record identically (length-aware, plus ambient
//     undo-record truncation) and U.NOT_FOUND is returned.
//   * OFFSETS NOT POINTERS: rom.write_p32(slotOffset, targetOffset) takes
//     OFFSETS (it applies U.toPointer internally).
//
// WF DIVERGENCE (documented, intentional): WF recycles the OLD song's region via
// RecycleAddress / SongTableForm.MakeRecycleSong; this Core port is APPEND-ONLY
// (same as the merged SongInstrumentSetCore.ImportAll / SongTrackWaveImportCore)
// — old-region recycle is a documented follow-up.
using System;
using System.Collections.Generic;
using System.Data;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Cross-platform `.s` / SondFont voicegroup-assembler IMPORT (#1001 PR2).
    /// ROM-mutating port of the WinForms <c>SongUtil.ImportS</c>. Validates the
    /// entire assembled image before mutating; the whole import is one undo
    /// transaction with a byte-identical fault restore.
    /// </summary>
    public static class SongTrackSImportCore
    {
        // -----------------------------------------------------------------
        // m4a / mid2agb constants (verbatim from WF SongUtil).
        // -----------------------------------------------------------------
        const uint WAIT_START = 0x80;       // W00 ..
        const uint WAIT_END = 0x80 + 48;
        const uint TIE = 0xCF;
        const uint NOTE_END = 0xFF;

        // Wait/note byte table (WF SongUtil.WaitCode).
        static readonly int[] WaitCode = new int[]{
            0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,
            28,30,32,36,40,42,44,48,52,54,56,60,64,66,68,72,76,78,80,84,88,90,92,96,
        };

        static readonly string[] KEYCODE = new string[]{
            "Cn","Cs","Dn","Ds","En","Fn","Fs","Gn","Gs","An","As","Bn",
        };

        static readonly string[] MEMACC = new string[]{
            "mem_set","mem_add","mem_sub","mem_mem_set","mem_mem_add","mem_mem_sub",
            "mem_beq","mem_bne","mem_bhi","mem_bhs","mem_bls","mem_blo",
            "mem_mem_beq","mem_mem_bne","mem_mem_bhi","mem_mem_bhs","mem_mem_bls","mem_mem_blo",
        };

        /// <summary>The -1 sentinel seeded for <c>voicegroup000</c> / <c>MusicVoices</c>.
        /// As a u32 it is 0xFFFFFFFF, whose top byte (0xFF) would index global[255]
        /// in the naive WF rewrite — Core treats it as "the song-header instrument
        /// pointer" instead (Copilot plan finding).</summary>
        const uint VOICEGROUP_SENTINEL = 0xFFFFFFFF; // (uint)(-1)

        static uint byteToWait(uint b)
        {
            if (b < WAIT_START) return 0;
            if (b - WAIT_START >= WaitCode.Length) return 0;
            return (uint)WaitCode[(int)b - WAIT_START];
        }
        static uint byteToNote(uint b)
        {
            const uint NOTE_START = 0xD0;
            if (b + 1 < NOTE_START) return 0;
            if (b + 1 - NOTE_START >= WaitCode.Length) return 0;
            return (uint)WaitCode[(int)b + 1 - NOTE_START];
        }
        static string getKeyCode(uint code)
        {
            int key = (int)code % 12;
            int keyN = (int)code / 12;
            if (keyN == 0) return KEYCODE[key] + "M2";
            if (keyN == 1) return KEYCODE[key] + "M1";
            if (keyN >= 2) return KEYCODE[key] + (keyN - 2);
            return "";
        }

        /// <summary>One deferred <c>.word</c> pointer-slot fixup. WF stores ONLY the
        /// computed u32, which makes a literal numeric <c>.word</c> (e.g.
        /// <c>.word 0x01000000</c>) indistinguishable from a label-relative
        /// <c>(globalID&lt;&lt;24)+offset</c> encoding — so WF blindly rewrites it as a
        /// pointer and corrupts the ROM (Copilot PR review). Core preserves the
        /// per-<c>.word</c> ORIGIN so the validate phase can reject an absolute /
        /// numeric word BEFORE mutating: only a word that resolved through a real
        /// LABEL name (the <c>equ[name]</c> set by a label definition) or the
        /// <c>voicegroup000</c>/<c>MusicVoices</c> SENTINEL is a legal pointer slot.</summary>
        sealed class WordRef
        {
            public uint SlotInBlob;     // pointer-slot offset within this global's blob
            public bool IsSentinel;     // resolved from voicegroup000 / MusicVoices
            public bool IsLabel;        // resolved from a registered label name
        }

        /// <summary>One assembled "global" data blob (WF SongInnerDataSt): its
        /// label name, the byte list, the per-<c>.word</c> pointer-slot fixups
        /// (with origin metadata), and the ROM offset allocated during the mutation
        /// phase.</summary>
        sealed class SongGlobal
        {
            public int GlobalID;            // == index in the global list
            public string Name;
            public List<WordRef> WordRefs = new List<WordRef>(); // .word fixups + origin
            public List<byte> List = new List<byte>();
            public uint RomAllocOffset;     // filled in the mutation phase
        }

        /// <summary>
        /// Assemble a `.s` SondFont source and import it into the ROM, repointing
        /// the song-table slot at the new song header and the song header's
        /// instrument pointer (<c>+4</c>) at <paramref name="selectedInstrumentAddr"/>.
        /// Returns the new song-header base OFFSET, or <see cref="U.NOT_FOUND"/> on
        /// ANY failure (with the ROM restored byte-identical — zero bytes changed).
        /// </summary>
        /// <param name="rom">The loaded ROM (mutated under the caller's ambient
        /// undo scope on success).</param>
        /// <param name="sFilePath">The `.s` source path (used only for file:line
        /// error context — the bytes are read via <paramref name="readLines"/>).</param>
        /// <param name="songTableSlotAddr">The song-table entry OFFSET to repoint
        /// (the 8-byte entry's 4-byte header-pointer slot).</param>
        /// <param name="selectedInstrumentAddr">The user-selected instrument-set
        /// OFFSET (the song-header <c>+4</c> target, and the resolution of every
        /// <c>voicegroup000</c> / <c>MusicVoices</c> sentinel reference). The caller
        /// MUST have normalized this to an OFFSET (e.g. <c>U.toOffset(pick.Address)</c>)
        /// and validated it; this method re-validates it.</param>
        /// <param name="readLines">Reads the `.s` source into its lines (the host
        /// supplies <c>File.ReadAllLines</c>); returns <c>null</c> when missing.</param>
        /// <param name="appendBinaryData">Allocator appending a blob to free space
        /// under the ambient undo scope and returning its base OFFSET (or
        /// <see cref="U.NOT_FOUND"/>). Pass <c>null</c> for the built-in ROM-end
        /// appender.</param>
        /// <param name="error">Human-readable failure reason on
        /// <see cref="U.NOT_FOUND"/>; <c>null</c> on success.</param>
        public static uint ImportS(
            ROM rom,
            string sFilePath,
            uint songTableSlotAddr,
            uint selectedInstrumentAddr,
            Func<string, string[]> readLines,
            Func<byte[], uint> appendBinaryData,
            out string error)
        {
            error = null;
            if (rom == null)
            {
                error = R._("ROM is not loaded.");
                return U.NOT_FOUND;
            }
            if (readLines == null)
            {
                error = R._("The .s import requires a read delegate.");
                return U.NOT_FOUND;
            }

            // The song-table slot and song header must be valid BEFORE any append
            // (Copilot plan finding 3): a null/invalid header would otherwise have
            // us append data then fail (or write to offset 4). Validate the slot,
            // the dereferenced header, and the +4 instrument-pointer slot.
            uint slotOffset = U.toOffset(songTableSlotAddr);
            if (!U.isSafetyOffset(slotOffset, rom))
            {
                error = R._("The song-table entry address is invalid.");
                return U.NOT_FOUND;
            }
            uint songHeaderOffset = rom.p32(slotOffset);
            if (!U.isSafetyOffset(songHeaderOffset, rom)
                || !U.isSafetyOffset(songHeaderOffset + 4, rom))
            {
                error = R._("The song table has no song header.");
                return U.NOT_FOUND;
            }

            // The selected instrument set (the song-header +4 target and the
            // resolution of every voicegroup000/MusicVoices sentinel) must be a
            // valid OFFSET (Copilot plan finding 1).
            uint instrumentOffset = U.toOffset(selectedInstrumentAddr);
            if (!U.isSafetyOffset(instrumentOffset, rom))
            {
                error = R._("The selected instrument set address is invalid.");
                return U.NOT_FOUND;
            }

            // ---- PHASE 1: parse + resolve the WHOLE .s in memory (no ROM write).
            string globalName;
            List<SongGlobal> global;
            try
            {
                global = ParseScript(sFilePath, readLines, out globalName, out error);
            }
            catch (Exception ex)
            {
                error = R._("The .s import failed while reading: {0}", ex.Message);
                return U.NOT_FOUND;
            }
            if (global == null)
            {
                if (string.IsNullOrEmpty(error))
                    error = R._("Could not parse the .s source.");
                return U.NOT_FOUND;
            }

            // The song-table global must exist (WF: findGlobal(global, globalName)).
            if (globalName == null || FindGlobal(global, globalName) < 0)
            {
                error = R._("The song table is missing.\r\n\r\nglobal sss\r\nsss:\r\nThis kind of data is required.");
                return U.NOT_FOUND;
            }

            // VALIDATE-ALL-BEFORE-MUTATE (Copilot plan finding): every .word slot's
            // encoded label id must be the known sentinel OR an in-range global id.
            // Decode each placeholder u32 from the in-memory blob (NOT the ROM) so a
            // bad id is rejected with file:line and ZERO ROM mutation.
            if (!ValidateLabelReferences(global, sFilePath, out error))
                return U.NOT_FOUND;

            // ---- PHASE 2: mutate under ONE transaction with a byte-identical
            // restore (the SongInstrumentSetCore.RestoreFault pattern). The caller
            // has already opened the ambient undo scope (the View's UndoService).
            byte[] snapshot = (byte[])rom.Data.Clone();
            var ambient = ROM.GetAmbientUndoData();
            int undoStart = ambient?.list.Count ?? 0;
            try
            {
                Func<byte[], uint> appender = appendBinaryData ?? (buf => AppendToRomEnd(rom, buf));

                // (a) Append every global's blob; record its base offset. The
                // song-table global is repointed at its allocated base.
                for (int i = 0; i < global.Count; i++)
                {
                    uint writeOffset = appender(global[i].List.ToArray());
                    if (writeOffset == U.NOT_FOUND)
                    {
                        RestoreFault(rom, snapshot, ambient, undoStart);
                        error = R._("Failed to allocate ROM space for the .s import.");
                        return U.NOT_FOUND;
                    }
                    global[i].RomAllocOffset = writeOffset;

                    if (global[i].Name == globalName)
                    {
                        rom.write_p32(slotOffset, global[i].RomAllocOffset);
                    }
                }

                // (b) Resolve every label-relative .word pointer using the recorded
                // per-.word ORIGIN (not a re-decode of the bytes — Copilot PR review).
                // A SENTINEL word -> the user-selected instrument set; a LABEL word's
                // encoded (globalID<<24)+offset -> global[id].base + offset. Both the
                // id range and the offset range were already validated in phase 1.
                // write_p32 takes the TARGET OFFSET and applies U.toPointer internally.
                for (int i = 0; i < global.Count; i++)
                {
                    SongGlobal g = global[i];
                    foreach (WordRef wr in g.WordRefs)
                    {
                        uint rewriteAddr = g.RomAllocOffset + wr.SlotInBlob;

                        if (wr.IsSentinel)
                        {//voicegroup000 / MusicVoices — the user-selected instrument set.
                            rom.write_p32(rewriteAddr, instrumentOffset);
                            continue;
                        }

                        // LABEL word (validated): decode (globalID<<24)+offset.
                        uint rewriteInfo = rom.u32(rewriteAddr);
                        int globalID = (int)((rewriteInfo >> 24) & 0xFF);
                        uint offset = (rewriteInfo & 0xFFFFFF);
                        if (globalID < 0 || globalID >= global.Count)
                        {
                            RestoreFault(rom, snapshot, ambient, undoStart);
                            error = R._("A .word reference points to an undefined label (global id {0}).", globalID);
                            return U.NOT_FOUND;
                        }
                        uint newPointer = global[globalID].RomAllocOffset + offset;
                        rom.write_p32(rewriteAddr, newPointer);
                    }
                }

                // (c) Repoint the song header's instrument pointer (+4) at the
                // user-selected instrument set (WF "楽器テーブルの更新"). WF re-reads
                // the song header from the (now-repointed) slot, so this targets the
                // NEW header's +4 — the freshly-assembled song-table global, whose +4
                // is the .word voicegroup000 instrument slot already resolved in (b).
                uint newSongHeaderOffset = rom.p32(slotOffset);
                if (!U.isSafetyOffset(newSongHeaderOffset, rom)
                    || !U.isSafetyOffset(newSongHeaderOffset + 4, rom))
                {
                    RestoreFault(rom, snapshot, ambient, undoStart);
                    error = R._("The song table has no song header.");
                    return U.NOT_FOUND;
                }
                rom.write_p32(newSongHeaderOffset + 4, instrumentOffset);

                // The new song-header base offset (the repointed global).
                return newSongHeaderOffset;
            }
            catch (Exception ex)
            {
                RestoreFault(rom, snapshot, ambient, undoStart);
                error = R._("The .s import failed: {0}", ex.Message);
                return U.NOT_FOUND;
            }
        }

        // -----------------------------------------------------------------
        // PHASE 1 — parse + resolve (read-only, in-memory).
        // -----------------------------------------------------------------

        /// <summary>
        /// Parse the `.s` source into the list of assembled globals. Mirrors the WF
        /// <c>ImportS</c> line loop verbatim (`.equ` / `.global` / labels / `.byte`
        /// / `.word` / `.end`; `.include` / `.section` / `.align` ignored). Returns
        /// <c>null</c> (with <paramref name="error"/> set, including
        /// <c>File:... Line:...</c>) on the first parse error. NO ROM write.
        /// </summary>
        static List<SongGlobal> ParseScript(
            string filename,
            Func<string, string[]> readLines,
            out string globalName,
            out string error)
        {
            error = null;
            globalName = null;

            string[] lines = readLines(filename);
            if (lines == null)
            {
                error = R._("The .s source file is missing: {0}", filename);
                return null;
            }

            Dictionary<string, int> equ = BuildEquDictionary();
            List<KeyValuePair<string, int>> equSorted = SortedEQU(equ);

            List<SongGlobal> global = new List<SongGlobal>();
            SongGlobal current = null;
            // Every label name registered into `equ` (so a .word can tell a real
            // label pointer from a numeric/absolute expression — Copilot PR review).
            var labelNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                // @ / # / // (and {J}/{U}) onwards is a comment — strip it.
                line = ClipCommentWithCharpAndAtmark(line);

                if (line.Length <= 1)
                    continue;

                string[] token = line.Split(
                    new string[] { " ", "\t", "," }, StringSplitOptions.RemoveEmptyEntries);
                if (token.Length <= 0)
                    continue;

                if (token[0] == ".equ")
                {
                    if (token.Length <= 2)
                    {
                        error = R._(".equ requires two arguments.\r\ne.g. .equ name, value\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                        return null;
                    }
                    string v = "";
                    for (int n = 2; n < token.Length; n++)
                        v += token[n];
                    if (!TryExpr(v, equ, equSorted, out int val, out string exErr))
                    {
                        error = R._("{2}\r\n\r\nFile:{0} Line:{1}", filename, i + 1, exErr);
                        return null;
                    }
                    equ[token[1]] = val;
                    equSorted = SortedEQU(equ);
                    continue;
                }
                if (token[0] == ".global")
                {
                    if (token.Length <= 1)
                    {
                        error = R._(".global requires a name.\r\ne.g. global aaa\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                        return null;
                    }
                    if (globalName != null)
                    {
                        error = R._("global was defined twice.\r\ne.g. global aaa\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                        return null;
                    }
                    globalName = token[1];
                    continue;
                }

                if (token[0].IndexOf(":") == token[0].Length - 1)
                {//label
                    if (globalName == null)
                    {
                        error = R._("Define global info before writing a label.\r\ne.g. global aaa\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                        return null;
                    }

                    string name = token[0].Substring(0, token[0].Length - 1);
                    if (IsGlobalLabel(globalName, token[0]))
                    {
                        if (FindGlobal(global, name) >= 0)
                        {
                            error = R._("Global label {2} is already used.\r\n\r\nFile:{0} Line:{1}", filename, i + 1, name);
                            return null;
                        }
                        current = new SongGlobal
                        {
                            Name = name,
                            GlobalID = global.Count,
                        };
                        global.Add(current);
                    }
                    else
                    {
                        if (current == null)
                        {
                            error = R._("A local label was used with no global label.\r\nDefine a global label first.\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                            return null;
                        }
                    }

                    // Relative coordinate: (globalID<<24) + listCount.
                    equ[name] = (current.GlobalID << 24) + current.List.Count;
                    labelNames.Add(name);          // mark as a real label name
                    equSorted = SortedEQU(equ);
                    continue;
                }

                if (token[0] == ".byte")
                {
                    if (current == null)
                    {
                        error = R._(".byte was used with no global label.\r\nDefine a global label first.\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                        return null;
                    }
                    if (token.Length <= 1)
                    {
                        error = R._(".byte requires at least one argument.\r\ne.g. .byte arg1....\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                        return null;
                    }
                    for (int n = 1; n < token.Length; n++)
                    {
                        if (!TryExpr(token[n], equ, equSorted, out int v, out string exErr))
                        {
                            error = R._(".byte parse error {2}\r\n{3}\r\n\r\nFile:{0} Line:{1}", filename, i + 1, token[n], exErr);
                            return null;
                        }
                        current.List.Add((byte)v);
                    }
                    continue;
                }
                if (token[0] == ".word")
                {//word but really a 4-byte pointer (label-relative).
                    if (current == null)
                    {
                        error = R._(".word was used with no global label.\r\nDefine a global label first.\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                        return null;
                    }
                    if (token.Length <= 1)
                    {
                        error = R._(".word requires at least one label argument.\r\ne.g. .word arg1....\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                        return null;
                    }
                    for (int n = 1; n < token.Length; n++)
                    {
                        if (!TryExpr(token[n], equ, equSorted, out int vi, out string exErr))
                        {
                            error = R._(".word parse error {2}\r\n{3}\r\n\r\nFile:{0} Line:{1}", filename, i + 1, token[n], exErr);
                            return null;
                        }
                        uint v = (uint)vi;

                        // ORIGIN (Copilot PR review): a legal .word pointer is exactly
                        // a registered LABEL name or the voicegroup000/MusicVoices
                        // SENTINEL. A numeric / absolute / arithmetic expression
                        // (e.g. `.word 0x01000000`, `.word 65535`) is REJECTED in the
                        // validate phase — WF would blindly rewrite it as a pointer and
                        // corrupt the ROM. The raw token (whitespace-trimmed) carries
                        // the origin; an arithmetic word like `lbl+4` is NOT a bare
                        // label name, so it is (correctly) treated as absolute too.
                        string wtok = token[n];
                        bool isSentinel = wtok == "voicegroup000" || wtok == "MusicVoices";
                        bool isLabel = labelNames.Contains(wtok);

                        current.WordRefs.Add(new WordRef
                        {
                            SlotInBlob = (uint)current.List.Count,
                            IsSentinel = isSentinel,
                            IsLabel = isLabel,
                        });
                        U.append_u32(current.List, v);
                    }
                    continue;
                }
                if (token[0] == ".end")
                {
                    if (current == null)
                    {
                        error = R._(".end was used with no global label.\r\nDefine a global label first.\r\n\r\nFile:{0} Line:{1}", filename, i + 1);
                        return null;
                    }
                    U.append_u32(current.List, 0);
                    break;
                }
                // .include / .section / .align / anything else: ignored (WF parity).
            }

            return global;
        }

        /// <summary>
        /// VALIDATE-ALL-BEFORE-MUTATE: every <c>.word</c> must have resolved through
        /// a real LABEL name or the <c>voicegroup000</c>/<c>MusicVoices</c> SENTINEL,
        /// and a label word's encoded global id must be in range AND its offset must
        /// land inside that global's blob. A numeric / absolute / arithmetic
        /// <c>.word</c> (no label origin) is REJECTED here with file:line and ZERO ROM
        /// mutation — WF would blindly rewrite it as a pointer and corrupt the ROM
        /// (Copilot PR review). The sentinel resolves to the selected instrument set.
        /// </summary>
        static bool ValidateLabelReferences(List<SongGlobal> global, string filename, out string error)
        {
            error = null;
            for (int i = 0; i < global.Count; i++)
            {
                SongGlobal g = global[i];
                foreach (WordRef wr in g.WordRefs)
                {
                    // The slot itself must be inside this global's byte list.
                    uint slot = wr.SlotInBlob;
                    if (slot + 4 > g.List.Count)
                    {
                        error = R._("A .word reference is out of range in global {0}.", g.Name);
                        return false;
                    }

                    // The sentinel resolves to the selected instrument set (validated
                    // separately as a real ROM offset by ImportS).
                    if (wr.IsSentinel)
                        continue;

                    // ORIGIN GATE (Copilot PR review): a .word that did NOT resolve
                    // from a label name is an absolute / numeric expression — reject
                    // it (e.g. `.word 0x01000000`, `.word 65535`, `.word lbl+4`).
                    if (!wr.IsLabel)
                    {
                        uint absval = (uint)(g.List[(int)slot]
                            | (g.List[(int)slot + 1] << 8)
                            | (g.List[(int)slot + 2] << 16)
                            | (g.List[(int)slot + 3] << 24));
                        error = R._("A .word value (0x{2:X}) in {3}:{4} is not a label or instrument reference. Only labels and voicegroup000/MusicVoices may appear in a .word.", 0, 0, absval, filename, g.Name);
                        return false;
                    }

                    // A label word encodes (globalID<<24)+offset. Bounds-check the id
                    // AND require the offset to land inside the target global's blob.
                    uint info = (uint)(g.List[(int)slot]
                        | (g.List[(int)slot + 1] << 8)
                        | (g.List[(int)slot + 2] << 16)
                        | (g.List[(int)slot + 3] << 24));
                    int globalID = (int)((info >> 24) & 0xFF);
                    uint offset = info & 0xFFFFFF;
                    if (globalID < 0 || globalID >= global.Count)
                    {
                        error = R._("A .word reference points to an undefined label (global id {0}) in {1}:{2}.", globalID, filename, g.Name);
                        return false;
                    }
                    if (offset > (uint)global[globalID].List.Count)
                    {
                        error = R._("A .word label offset (0x{0:X}) is outside its target in {1}:{2}.", offset, filename, g.Name);
                        return false;
                    }
                }
            }
            return true;
        }

        // -----------------------------------------------------------------
        // equ dictionary + expression evaluator (verbatim WF behavior).
        // -----------------------------------------------------------------

        static Dictionary<string, int> BuildEquDictionary()
        {
            var equ = new Dictionary<string, int>();
            equ["voicegroup000"] = -1; // instrument table — special-cased.
            equ["MusicVoices"] = -1;   // instrument table — special-cased.
            equ["mxv"] = 0x7F;
            equ["c_v"] = 0x40;
            equ["EOT"] = 0xCE;
            equ["FINE"] = 0xB1;
            equ["GOTO"] = 0xb2;
            equ["PATT"] = 0xb3;
            equ["PEND"] = 0xb4;
            equ["MEMACC"] = 0xb9;
            equ["PRIO"] = 0xba;
            equ["TEMPO"] = 0xbb;
            equ["KEYSH"] = 0xbc;
            equ["VOICE"] = 0xbd;
            equ["VOL"] = 0xbe;
            equ["PAN"] = 0xbf;
            equ["BEND"] = 0xc0;
            equ["BENDR"] = 0xc1;
            equ["LFOS"] = 0xc2;
            equ["LFODL"] = 0xc3;
            equ["MOD"] = 0xc4;
            equ["MODT"] = 0xc5;
            equ["TUNE"] = 0xc8;
            equ["gtp1"] = 0x01;
            equ["gtp2"] = 0x02;
            equ["gtp3"] = 0x03;
            equ["mod_vib"] = 0x00;

            for (uint i = WAIT_START; i <= WAIT_END; i++)
                equ["W" + byteToWait(i).ToString("00")] = (int)i;
            equ["TIE"] = (int)TIE;
            for (uint i = TIE + 1; i <= NOTE_END; i++)
                equ["N" + byteToNote(i).ToString("00")] = (int)i;
            for (uint i = 0; i <= 127; i++)
                equ[getKeyCode(i)] = (int)i;
            for (uint i = 0; i <= 127; i++)
                equ["v" + i.ToString("000")] = (int)i;
            for (uint i = 0; i < MEMACC.Length; i++)
                equ[MEMACC[i]] = (int)i;
            // hex lookups. NOTE: WF stops at `< 0xf` / `< 0xff`, which leaves the
            // common `0xF` and `0xFF` (= NOTE_END) tokens UN-substituted — Expr then
            // rejects them as non-numeric and a valid `.s` fails to import (Copilot PR
            // review). Use `<= 0xF` / `<= 0xFF` so the full single- and double-digit
            // hex range resolves (a strict superset of WF — no valid token regresses).
            for (uint i = 0; i <= 0xf; i++)
                equ["0x" + i.ToString("X")] = (int)i;
            for (uint i = 0; i <= 0xff; i++)
                equ[U.To0xHexString(i)] = (int)i;

            return equ;
        }

        /// <summary>Sort the equ entries by DESCENDING key length so longer names
        /// match first during the substring-replace in <see cref="TryExpr"/> (WF
        /// <c>SortedEQU</c>).</summary>
        static List<KeyValuePair<string, int>> SortedEQU(Dictionary<string, int> equ)
        {
            return U.OrderBy<string, int>(equ, (x) => { return -(x.Key.Length); });
        }

        /// <summary>
        /// Resolve an expression: a direct equ lookup, else substring-replace every
        /// equ name (longest-first) by its value then evaluate via
        /// <see cref="DataTable.Compute"/> (WF <c>Expr</c>). Returns <c>false</c>
        /// (with <paramref name="error"/> set) on a syntax / evaluate fault — the
        /// caller wraps it with file:line context.
        /// </summary>
        static bool TryExpr(
            string exprValue,
            Dictionary<string, int> equ,
            List<KeyValuePair<string, int>> equSorted,
            out int result,
            out string error)
        {
            error = null;
            result = 0;

            if (equ.TryGetValue(exprValue, out int direct))
            {
                result = direct;
                return true;
            }

            string expr = exprValue;
            foreach (var pair in equSorted)
                expr = expr.Replace(pair.Key, pair.Value.ToString());

            if (!IsExprString(expr))
            {
                error = R._("Non-numeric characters are present. expr:{0}", expr);
                return false;
            }

            try
            {
                using var dt = new DataTable();
                object computed = dt.Compute(expr, "");
                string str = computed.ToString();
                if (!int.TryParse(str, out result))
                    result = (int)U.atoi(str);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        static bool IsExprString(string str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (!((c >= '0' && c <= '9')
                    || c == '+' || c == '-' || c == '*' || c == '/' || c == '(' || c == ')' || c == '.'
                    || c == '\0'))
                    return false;
            }
            return true;
        }

        // -----------------------------------------------------------------
        // Label helpers (verbatim WF behavior).
        // -----------------------------------------------------------------

        static int FindGlobal(List<SongGlobal> global, string name)
        {
            for (int i = 0; i < global.Count; i++)
                if (global[i].Name == name)
                    return i;
            return -1;
        }

        /// <summary>
        /// WF <c>isGlobalLabel</c>: a label is global when it is exactly the global
        /// name optionally followed by <c>_</c> + digits then <c>:</c>
        /// (e.g. <c>aaa:</c>, <c>aaa1:</c>, <c>aaa_1:</c> are global; <c>aaa_1_1:</c>,
        /// <c>foo:</c> are local).
        /// </summary>
        static bool IsGlobalLabel(string globalName, string token)
        {
            if (token.IndexOf(globalName) != 0)
                return false;
            int i = globalName.Length;
            // Bounds-guard the index walk (WF assumes the trailing ':' terminates).
            while (i < token.Length && token[i] == '_')
                i++;
            while (i < token.Length && U.isnum(token[i]))
                i++;
            if (i < token.Length && token[i] == ':')
                return true;
            return false;
        }

        /// <summary>
        /// Strip an inline comment: <c>{J}</c> / <c>{U}</c> language markers and
        /// <c>//</c> / <c>#</c> / <c>@</c> comment leaders (ports WF
        /// <c>U.ClipCommentWithCharpAndAtmark</c>; <c>U.ClipCommentIndexOf</c> is
        /// already in Core).
        /// </summary>
        static string ClipCommentWithCharpAndAtmark(string str)
        {
            int term = U.ClipCommentIndexOf(str, "{J}");
            if (term >= 0) str = str.Substring(0, term);
            term = U.ClipCommentIndexOf(str, "{U}");
            if (term >= 0) str = str.Substring(0, term);
            term = U.ClipCommentIndexOf(str, "//");
            if (term >= 0) str = str.Substring(0, term);
            term = U.ClipCommentIndexOf(str, "#");
            if (term >= 0) str = str.Substring(0, term);
            term = U.ClipCommentIndexOf(str, "@");
            if (term >= 0) str = str.Substring(0, term);
            return str;
        }

        // -----------------------------------------------------------------
        // PHASE 2 helpers (ROM-mutating, single transaction).
        // -----------------------------------------------------------------

        /// <summary>Built-in 4-byte-aligned ROM-end appender (used when the host
        /// passes no allocator). Mirrors <c>SongInstrumentSetCore.AppendToRomEnd</c>.</summary>
        static uint AppendToRomEnd(ROM rom, byte[] blob)
        {
            uint appendOffset = U.Padding4((uint)rom.Data.Length);
            if (!rom.write_resize_data(appendOffset + (uint)blob.Length))
                return U.NOT_FOUND;
            rom.write_range(appendOffset, blob);
            return appendOffset;
        }

        /// <summary>Byte-AND-record identical fault restore (the
        /// <c>SongInstrumentSetCore.RestoreFault</c> pattern): restore the ROM
        /// bytes/length from <paramref name="snapshot"/> AND truncate any ambient
        /// undo records added since <paramref name="undoStart"/> so a subsequent
        /// caller Rollback cannot replay a stale record into the shrunk region.</summary>
        static void RestoreFault(ROM rom, byte[] snapshot, Undo.UndoData ambient, int undoStart)
        {
            if (rom.Data.Length != snapshot.Length)
                rom.write_resize_data((uint)snapshot.Length);
            Array.Copy(snapshot, rom.Data, snapshot.Length);
            if (ambient != null && ambient.list.Count > undoStart)
                ambient.list.RemoveRange(undoStart, ambient.list.Count - undoStart);
        }
    }
}
