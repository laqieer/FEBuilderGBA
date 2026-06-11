// SPDX-License-Identifier: GPL-3.0-or-later
// #1027 — Minimal AsmMap symbol reader for the Text Editor free-area union.
//
// The WinForms free-area scan (TextForm.SearcFreeArea_Click ->
// AsmMapFileAsmCache.GetVarsIDArray -> U.MakeVarsIDArray -> asmmap.MakeVarsIDArray)
// folds the loaded ASM/MAP symbol table's text references into the "used text id"
// union. AsmMapFile.MakeVarsIDArray (AsmMapFile.cs:1179) walks every loaded record
// and, for the THREE text-relevant record types, feeds the union:
//   - &TEXTBATCH        -> UseValsID.AppendASMDATATextID(list, p, base, sizeof=4)
//   - &TEXTBATCHSHORT   -> UseValsID.AppendASMDATATextID(list, p, base, sizeof=2)
//   - &EVENT            -> EventCondForm.MakeVarsIDArrayByEventAddress(base) (event scan)
//
// The full AsmMapFile loader (AsmMapFile.cs) is WinForms-bound (it is driven by the
// background AsmMapFileAsmCache thread, parses @STRUCT field definitions, OAM/LZ77/
// palette geometry, etc.). For the free-area union we only need the three text record
// types, which in every shipped asmmap_*.txt appear as either:
//   (a) plain line:   "08XXXXXX\t&TYPE\tName"          (length computed from ROM)
//   (b) range line:   "08XXXXXX\t:08YYYYYY\t&TYPE\tName" (length = end - start)
// This reader ports JUST those two line forms + the three record types — nothing else.
//
// READ-ONLY: never mutates the ROM. Every read is bounds-guarded; a missing config
// file or a malformed line is skipped (never throws).
//
// DOCUMENTED RESIDUAL GAP: struct-field-typed text records (a &TEXTBATCH/&EVENT inside
// an @STRUCT definition referenced via "08XXXX\t@STRUCT@T") are NOT covered. A grep of
// every shipped asmmap_FE{6,7,8}*.txt + asmmap_gba_ALL.txt confirms ZERO such records
// exist (all TEXTBATCH/TEXTBATCHSHORT/EVENT entries use the plain/range line forms), so
// this gap is theoretical for the shipped configs.

using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Reads <c>config/data/asmmap_*.txt</c> (the same files WinForms
    /// <c>AsmMapFile</c> loads) and folds their <c>&amp;TEXTBATCH</c> /
    /// <c>&amp;TEXTBATCHSHORT</c> text ids + <c>&amp;EVENT</c> event-script text
    /// references into the free-area "used text id" union. Cross-platform,
    /// strictly READ-ONLY.
    /// </summary>
    public static class AsmMapTextSymbolReader
    {
        /// <summary>
        /// Walk the loaded ROM's asmmap config files and add every TEXTBATCH /
        /// TEXTBATCHSHORT text id + every &amp;EVENT script's referenced text ids
        /// into <paramref name="ids"/>. Mirrors WinForms
        /// <c>AsmMapFile.MakeVarsIDArray</c> (text-id path only).
        ///
        /// The &amp;EVENT scan dereferences <see cref="CoreState.EventScript"/> /
        /// <see cref="CoreState.CommentCache"/>; if those prerequisites aren't met
        /// the event records are skipped (the TEXTBATCH records still contribute).
        /// </summary>
        public static void CollectUsedTextIds(ROM rom, HashSet<uint> ids)
        {
            if (rom?.Data == null || ids == null) return;

            // WinForms AsmMapFile loads, in order: asmmap_addition_, asmmap_, asmmap_gba_.
            // Each via U.ConfigDataFilename(type, rom) (version/lang resolution).
            foreach (string type in new[] { "asmmap_addition_", "asmmap_", "asmmap_gba_" })
            {
                string file;
                try { file = U.ConfigDataFilename(type, rom); }
                catch { continue; }
                ReadFile(rom, file, ids);
            }
        }

        static void ReadFile(ROM rom, string fullfilename, HashSet<uint> ids)
        {
            if (string.IsNullOrEmpty(fullfilename) || !File.Exists(fullfilename)) return;

            // EventScript scan prerequisites — mirror EventScriptReferenceScanner's
            // gating so an &EVENT record only runs the disasm path when the active
            // ROM / EventScript / CommentCache are wired.
            bool eventScanReady =
                CoreState.EventScript != null
                && CoreState.ROM != null
                && ReferenceEquals(CoreState.ROM, rom)
                && CoreState.CommentCache != null;
            var tracelist = new List<uint>();

            string[] lines;
            try { lines = File.ReadAllLines(fullfilename); }
            catch { return; }

            foreach (string raw in lines)
            {
                if (raw == null) continue;
                if (U.IsComment(raw) || U.OtherLangLine(raw, rom)) continue;
                string line = U.ClipComment(raw);

                string[] sp = line.Split('\t');
                if (sp.Length <= 1) continue;

                string op = sp[0];
                if (op.Length <= 0 || op[0] == '@') continue; // @STRUCT defs: skip

                uint pointer = U.toPointer(U.atoh(op));
                if (pointer < 0x02000000) continue;

                if (sp[1].Length <= 0) continue;
                if (sp[1][0] == '@') continue; // @STRUCT-referencing entries: out of scope

                uint startOffset = U.toOffset(pointer);

                if (sp[1][0] == ':')
                {
                    // Range line: "start\t:end\t&TYPE\tName". TypeName is the &TYPE
                    // token (sp[2] with a leading '&'). Length = end - start.
                    if (sp.Length <= 2) continue;
                    uint endPointer = U.toPointer(U.atoh(sp[1].Substring(1)));
                    if (endPointer == 0 || endPointer <= pointer) continue;
                    string typeName = ParseAmpType(sp, 2);
                    if (typeName == "") continue;
                    uint length = endPointer - pointer;
                    DispatchRecord(rom, typeName, startOffset, length, eventScanReady, tracelist, ids);
                }
                else
                {
                    // Plain line: "start\t&TYPE\tName". TypeName is the &TYPE token
                    // (sp[1] with a leading '&'). Length is computed from the ROM,
                    // exactly as AsmMapFile.TypeToLengthAndName does.
                    string typeName = ParseAmpType(sp, 1);
                    if (typeName == "") continue;
                    uint length = ComputeLength(rom, typeName, startOffset);
                    if (length == 0) continue;
                    DispatchRecord(rom, typeName, startOffset, length, eventScanReady, tracelist, ids);
                }
            }
        }

        // Extract the &TYPE token at sp[index] (must start with '&'). The plain-line
        // form (ParseFuncNamePlus2) and the range-line form (ParseFuncNamePlus) both
        // recognise the type only when the token at that index begins with '&'.
        static string ParseAmpType(string[] sp, int index)
        {
            if (index >= sp.Length) return "";
            string token = sp[index];
            if (token.Length > 0 && token[0] == '&')
                return token.Substring(1);
            return "";
        }

        // Port of AsmMapFile.TypeToLengthAndName (text record types only).
        static uint ComputeLength(ROM rom, string type, uint offset)
        {
            try
            {
                if (type == "TEXTBATCH")
                {
                    uint size = TextBatchLength(offset, rom);
                    if (size <= 0 || size >= 1000) return 0;
                    return size;
                }
                if (type == "TEXTBATCHSHORT")
                {
                    uint size = TextBatchShortLength(offset, rom);
                    if (size <= 0 || size >= 1000) return 0;
                    return size;
                }
                if (type == "EVENT")
                {
                    uint size = EventScript.SearchEveneLength(rom.Data, offset);
                    if (size <= 0 || size >= 2000) return 0;
                    return size;
                }
            }
            catch { return 0; }
            return 0;
        }

        // Mirror of AsmMapFile.MakeVarsIDArray per-record dispatch (text path).
        static void DispatchRecord(ROM rom, string type, uint startOffset, uint length,
            bool eventScanReady, List<uint> tracelist, HashSet<uint> ids)
        {
            if (length == 0) return;
            if (type == "TEXTBATCH")
            {
                AppendAsmDataTextIds(rom, startOffset, length, 4, ids);
            }
            else if (type == "TEXTBATCHSHORT")
            {
                AppendAsmDataTextIds(rom, startOffset, length, 2, ids);
            }
            else if (type == "EVENT")
            {
                if (!eventScanReady) return; // EventCondForm.MakeVarsIDArrayByEventAddress
                if (!U.isSafetyOffset(startOffset, rom)) return;
                EventScriptReferenceScanner.ScanScriptForTextIds(
                    rom, CoreState.EventScript, startOffset, tracelist, ids);
            }
        }

        // Port of UseValsID.AppendASMDATATextID: walk [start, start+length) in steps
        // of `sizeof`, reading the u16 text id at each step. WF adds every id verbatim
        // (no 0/0x7FFF filter in AppendASMDATATextID), but the free-area union later
        // re-applies ConvertMaps (which keys by id) so the guard here matches the
        // overall AppendTextID semantics used by the union (id != 0 && id < 0x7FFF).
        static void AppendAsmDataTextIds(ROM rom, uint start, uint length, uint sizeof_, HashSet<uint> ids)
        {
            if (sizeof_ == 0) return;
            ulong end = (ulong)start + length;
            uint romLen = (uint)rom.Data.Length;
            for (uint addr = start; addr < end; addr += sizeof_)
            {
                if (addr + 2 > romLen) break;
                uint id = rom.u16(addr);
                if (id == 0 || id >= 0x7FFF) continue;
                ids.Add(id);
            }
        }

        // Port of U.TextBatchLength (WF U.cs:6075): stride 8, stop on u16==0 (consume)
        // or u16 >= 0x3000.
        static uint TextBatchLength(uint addr, ROM rom)
        {
            uint first = addr;
            uint length = (uint)rom.Data.Length - 8;
            for (; addr < length; addr += 8)
            {
                uint v = rom.u16(addr);
                if (v == 0) { addr += 8; break; }
                if (v >= 0x3000) break;
            }
            return addr - first;
        }

        // Port of U.TextBatchShortLength (WF U.cs:6099): stride 2, stop on u16==0
        // (consume) or u16 >= 0x3000.
        static uint TextBatchShortLength(uint addr, ROM rom)
        {
            uint first = addr;
            uint length = (uint)rom.Data.Length - 2;
            for (; addr < length; addr += 2)
            {
                uint v = rom.u16(addr);
                if (v == 0) { addr += 2; break; }
                if (v >= 0x3000) break;
            }
            return addr - first;
        }
    }
}
