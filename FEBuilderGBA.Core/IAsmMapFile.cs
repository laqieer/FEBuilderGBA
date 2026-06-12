using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Data class for ASM map entries (function/symbol information).
    /// Shared between Core (DisassemblerTrumb) and WinForms (AsmMapFile).
    /// </summary>
    public class AsmMapSt
    {
        public string Name = "";
        public string ResultAndArgs = "";
        public string TypeName = "";
        public uint Length = 0;
        public bool IsPointer = false;
        public bool IsFreeArea = false;

        public string ToStringInfo()
        {
            if (ResultAndArgs != "")
            {
                return Name + " " + ResultAndArgs;
            }
            return Name;
        }
    }

    /// <summary>
    /// Interface for ASM map lookup, used by DisassemblerTrumb.
    /// WinForms' <c>AsmMapFile</c> and Core's <see cref="AsmMapSymbolFile"/>
    /// implement this.
    /// </summary>
    public interface IAsmMapFile
    {
        bool TryGetValue(uint pointer, out AsmMapSt out_p);

        /// <summary>
        /// Return the nearest loaded symbol key at or below
        /// <paramref name="pointer"/> (WF <c>AsmMapFile.SearchNear</c> semantics),
        /// or <see cref="U.NOT_FOUND"/> when <paramref name="pointer"/> sits below
        /// the lowest loaded key. Used by the Pointer Tool "What is this address?"
        /// lookup (#1026) so an address inside a known symbol's span resolves to
        /// "name + offset".
        ///
        /// Default body returns <see cref="U.NOT_FOUND"/> so existing
        /// implementors (which only need exact <see cref="TryGetValue"/>) keep
        /// compiling without change.
        /// </summary>
        uint SearchNear(uint pointer) => U.NOT_FOUND;
    }

    /// <summary>
    /// Cross-platform ASM/MAP symbol table reader for the Pointer Tool
    /// "What is this address?" lookup (#1026). Parses the same
    /// <c>config/data/asmmap_*.txt</c> files WinForms <c>AsmMapFile</c> loads
    /// (in order: <c>asmmap_addition_</c>, <c>asmmap_</c>, <c>asmmap_gba_</c>),
    /// keyed by GBA pointer (0x08xxxxxx), and exposes the two queries the
    /// Pointer Tool needs:
    /// <list type="bullet">
    ///   <item><see cref="TryGetValue"/> — exact-pointer symbol lookup.</item>
    ///   <item><see cref="SearchNear"/> — nearest symbol at/below a pointer,
    ///   so an address inside a symbol's span resolves to "name + offset".</item>
    /// </list>
    ///
    /// <para>Scope: ports just the plain (untyped &amp; <c>&amp;TYPE</c>) and
    /// range (untyped &amp; <c>&amp;TYPE</c>) line forms, with the WF
    /// <c>ParseFuncNamePlus</c> / <c>ParseFuncNamePlus2</c> name+args parsers and
    /// the cheap typed-length subset of <c>TypeToLengthAndName</c> (palettes,
    /// fixed-size records, TEXTBATCH/TEXTBATCHSHORT/EVENT via the shared
    /// <see cref="AsmMapSymbolLength"/> helpers). <c>@STRUCT</c> definitions and
    /// <c>@STRUCT</c>-referencing records are out of scope (skipped). Geometric /
    /// struct length types (LZ77, OAMREGS, HEADERTSA, PROC, …) load with
    /// <c>Length = 0</c>, so they still resolve on an EXACT pointer match but
    /// never via <see cref="SearchNear"/>'s span check — documented PARTIAL WF
    /// length parity.</para>
    ///
    /// <para>Strictly READ-ONLY: never mutates the ROM. The ctor and both query
    /// methods never throw — a missing config file or a malformed line is
    /// skipped, and a <c>version == 0</c> ROM produces an empty map.</para>
    /// </summary>
    public class AsmMapSymbolFile : IAsmMapFile
    {
        readonly Dictionary<uint, AsmMapSt> _map = new Dictionary<uint, AsmMapSt>();
        // GBA-pointer keys, ascending. Built once after load for SearchNear.
        List<uint> _sortedKeys = new List<uint>();

        /// <summary>
        /// Build the symbol map for <paramref name="rom"/> by parsing the loaded
        /// ROM's <c>asmmap_*.txt</c> config files. Never throws.
        /// </summary>
        public AsmMapSymbolFile(ROM rom)
        {
            if (rom?.RomInfo == null || rom.RomInfo.version == 0)
            {
                // version == 0 (no ROM detected) -> empty map (WF AsmMapFile ctor
                // returns early for version 0).
                BuildSortedKeys();
                return;
            }

            foreach (string type in new[] { "asmmap_addition_", "asmmap_", "asmmap_gba_" })
            {
                string file;
                try { file = U.ConfigDataFilename(type, rom); }
                catch { continue; }
                ReadFile(rom, file);
            }

            BuildSortedKeys();
        }

        /// <summary>
        /// Test seam (no config-dir touching): build the map from explicit
        /// asmmap text <paramref name="lines"/>. Used by the Core unit tests to
        /// inject synthetic symbol records without writing real config files.
        /// Same per-line parse as the production <see cref="ReadFile"/> path.
        /// </summary>
        internal void LoadFromLines(ROM rom, IEnumerable<string> lines)
        {
            if (lines == null) { BuildSortedKeys(); return; }
            foreach (string raw in lines)
            {
                ParseLine(rom, raw);
            }
            BuildSortedKeys();
        }

        void ReadFile(ROM rom, string fullfilename)
        {
            if (string.IsNullOrEmpty(fullfilename) || !File.Exists(fullfilename)) return;

            string[] lines;
            try { lines = File.ReadAllLines(fullfilename); }
            catch { return; }

            foreach (string raw in lines)
            {
                ParseLine(rom, raw);
            }
        }

        // Port of AsmMapFile.ASMMapLoadResource per-line dispatch, narrowed to the
        // plain (untyped & &TYPE) and range (untyped & &TYPE) line forms.
        void ParseLine(ROM rom, string raw)
        {
            try
            {
                if (raw == null) return;
                if (U.IsComment(raw) || U.OtherLangLine(raw, rom)) return;
                string line = U.ClipComment(raw);

                string[] sp = line.Split('\t');
                if (sp.Length <= 1) return;

                string op = sp[0];
                if (op.Length <= 0) return;
                if (op[0] == '@') return;                 // @STRUCT definition: skip

                uint pointer = U.toPointer(U.atoh(op));
                if (pointer < 0x02000000) return;

                if (sp[1].Length <= 0) return;
                if (sp[1][0] == '@') return;              // @STRUCT-referencing: out of scope

                if (sp[1][0] == ':')
                {
                    // Range line: "start\t:end\t[&TYPE\t]Name [args]". Length = end - start.
                    if (sp.Length <= 2) return;
                    uint endpointer = U.toPointer(U.atoh(sp[1].Substring(1)));
                    if (endpointer == 0) return;
                    if (pointer > endpointer) U.Swap(ref pointer, ref endpointer);

                    var p = new AsmMapSt();
                    AsmMapSymbolLength.ParseFuncNamePlus(sp, out p.Name, out p.ResultAndArgs, out p.TypeName);
                    p.Length = endpointer - pointer;
                    _map[pointer] = p;
                }
                else
                {
                    // Plain line: "start\t[&TYPE\t]Name [args]". Length from the
                    // cheap typed-length subset (0 for unknown / deferred types).
                    var p = new AsmMapSt();
                    AsmMapSymbolLength.ParseFuncNamePlus2(sp, out p.Name, out p.ResultAndArgs, out p.TypeName);
                    if (!AsmMapSymbolLength.TypeToLength(p, pointer, rom))
                    {
                        // WF returns false (and skips the record) only when a typed
                        // length computation yields 0; mirror that so a bogus
                        // TEXTBATCH/EVENT record is not added.
                        return;
                    }
                    _map[pointer] = p;
                }
            }
            catch
            {
                // Malformed line — skip it (never throw).
            }
        }

        void BuildSortedKeys()
        {
            _sortedKeys = new List<uint>(_map.Keys);
            _sortedKeys.Sort();
        }

        /// <summary>Exact-pointer symbol lookup.</summary>
        public bool TryGetValue(uint pointer, out AsmMapSt out_p)
        {
            return _map.TryGetValue(pointer, out out_p);
        }

        /// <summary>
        /// VERBATIM port of WF <c>AsmMapFile.SearchNear</c>: walk the ascending
        /// key list; for each key <c>p</c>, stop once <c>pointer &lt; p</c>; else
        /// advance <c>p</c> by its symbol length and, if <c>pointer</c> now falls
        /// inside the span, take the NEXT index. After the loop, index 0 means
        /// <paramref name="pointer"/> is below the lowest key (<see cref="U.NOT_FOUND"/>);
        /// otherwise the previous key is the nearest at/below.
        /// </summary>
        public uint SearchNear(uint pointer)
        {
            int length = _sortedKeys.Count;
            int i;
            for (i = 0; i < length; i++)
            {
                uint p = _sortedKeys[i];
                if (pointer < p)
                {
                    break;
                }
                // Guard against uint wraparound when length is large near 0xFFFFFFFF.
                ulong span = (ulong)p + (_map.TryGetValue(p, out var st) ? st.Length : 0u);
                if (pointer < span)
                {
                    i++;
                    break;
                }
            }

            if (i == 0)
            {
                return U.NOT_FOUND;
            }
            return _sortedKeys[i - 1];
        }

        /// <summary>Smallest symbol pointer in the map, or U.NOT_FOUND when empty.
        /// Used only by the Pointer Tool screenshot seed (#1026).</summary>
        public uint FirstKeyForScreenshot()
            => _sortedKeys.Count > 0 ? _sortedKeys[0] : U.NOT_FOUND;
    }

    /// <summary>
    /// Shared helpers ported from WinForms <c>AsmMapFile</c> for the symbol
    /// reader (#1026): the two function-name parsers and the cheap typed-length
    /// subset of <c>TypeToLengthAndName</c>. Internal — Core-only.
    /// </summary>
    internal static class AsmMapSymbolLength
    {
        // Port of AsmMapFile.ParseFuncNamePlus (range/&TYPE form, starts at i=2).
        public static void ParseFuncNamePlus(string[] sp, out string out_FuncName, out string out_FuncArgs, out string out_FuncType)
        {
            int i = 2;
            if (sp[i].Length > 0 && sp[i][0] == '&')
            {
                out_FuncType = sp[i].Substring(1);
                i++;
            }
            else
            {
                out_FuncType = "";
            }

            out_FuncName = sp[i];
            i++;

            for (; i < sp.Length; i++)
            {
                string a = sp[i];
                if (a.IndexOf("=") > 0)
                {
                    break;
                }
                out_FuncName += " " + a;
            }

            var comment = new System.Text.StringBuilder();
            for (; i < sp.Length; i++)
            {
                comment.Append(" ");
                comment.Append(sp[i]);
            }
            out_FuncArgs = U.substr(comment.ToString(), 1);
        }

        // Port of AsmMapFile.ParseFuncNamePlus2 (plain form, starts at i=1; also
        // extracts the type from a trailing "RET=" field when no &TYPE present).
        public static void ParseFuncNamePlus2(string[] sp, out string out_FuncName, out string out_FuncArgs, out string out_FuncType)
        {
            int i = 1;
            out_FuncType = "";
            if (sp[i].Length > 0 && sp[i][0] == '&')
            {
                out_FuncType = sp[i].Substring(1);
                i++;
            }

            out_FuncName = sp[i];
            i++;

            for (; i < sp.Length; i++)
            {
                string a = sp[i];
                if (a.IndexOf("=") > 0)
                {
                    break;
                }
                out_FuncName += " " + a;
            }

            var comment = new System.Text.StringBuilder();
            for (; i < sp.Length; i++)
            {
                comment.Append(" ");
                comment.Append(sp[i]);

                int start = sp[i].IndexOf("RET=");
                if (start >= 0 && out_FuncType == "")
                {
                    start += 4;
                    int term = sp[i].IndexOf(" ", start);
                    if (term < 0)
                    {
                        out_FuncType = U.substr(sp[i], start);
                    }
                    else
                    {
                        out_FuncType = U.substr(sp[i], start, term - start);
                    }
                }
            }

            out_FuncArgs = U.substr(comment.ToString(), 1);
        }

        // Port of AsmMapFile.TypeToLengthAndName — cheap / core-safe types only.
        // Returns false (skip record) only when a length-bearing type computes 0
        // (mirrors WF). All other / deferred types resolve to true with Length 0
        // (still exact-matchable; documented PARTIAL WF length parity).
        public static bool TypeToLength(AsmMapSt p, uint pointer, ROM rom)
        {
            string type = p.TypeName;
            switch (type)
            {
                case "PALETTE": p.Length = 0x20; return true;
                case "PALETTE2": p.Length = 0x20 * 2; return true;
                case "PALETTE3": p.Length = 0x20 * 3; return true;
                case "PALETTE4": p.Length = 0x20 * 4; return true;
                case "PALETTE7": p.Length = 0x20 * 7; return true;
                case "PALETTE8": p.Length = 0x20 * 8; return true;
                case "PALETTE16": p.Length = 0x20 * 16; return true;
                case "NAZO60": p.Length = 60; return true;
                case "FONTCOLOR0x200": p.Length = 0x200; return true;
                case "SECONDARYOAM": p.Length = 14; return true;
                case "BGCONFIG": p.Length = 10 * 2; return true;
                case "ASM":
                case "ARM": p.Length = 0; return true;
            }

            uint offset = U.toOffset(pointer);
            if (type == "TEXTBATCH")
            {
                uint size = TextBatchLength(offset, rom);
                if (size <= 0 || size >= 1000) return false;
                p.Length = size;
                return true;
            }
            if (type == "TEXTBATCHSHORT")
            {
                uint size = TextBatchShortLength(offset, rom);
                if (size <= 0 || size >= 1000) return false;
                p.Length = size;
                return true;
            }
            if (type == "EVENT")
            {
                uint size = EventScript.SearchEveneLength(rom.Data, offset);
                if (size <= 0 || size >= 2000) return false;
                p.Length = size;
                return true;
            }

            // Unknown / deferred geometric / struct length type: keep the record
            // with Length 0 (resolves on EXACT match). Matches WF's "不明 -> return
            // true" fall-through for untyped records.
            p.Length = 0;
            return true;
        }

        // Port of U.TextBatchLength: stride 8, stop on u16==0 (consume) or >= 0x3000.
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

        // Port of U.TextBatchShortLength: stride 2, stop on u16==0 (consume) or >= 0x3000.
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

    /// <summary>
    /// GBA BIOS SWI call name lookup table.
    /// Extracted from AsmMapFile for use in Core's DisassemblerTrumb.
    /// </summary>
    public static class GbaBiosCall
    {
        public static string GetSWI_GBA_BIOS_CALL(uint swicode)
        {
            switch (swicode)
            {
                case 0x00: return "SoftReset";
                case 0x01: return "RegisterRamReset";
                case 0x02: return "Halt";
                case 0x03: return "Stop";
                case 0x04: return "IntrWait";
                case 0x05: return "VBlankIntrWait";
                case 0x06: return "Div";
                case 0x07: return "DivArm";
                case 0x08: return "Sqrt";
                case 0x09: return "ArcTan";
                case 0x0A: return "ArcTan2";
                case 0x0B: return "CpuSet";
                case 0x0C: return "CpuFastSet";
                case 0x0D: return "GetBiosCheckSum";
                case 0x0E: return "BgAffineSet";
                case 0x0F: return "ObjAffineSet";
                case 0x10: return "BitUnPack";
                case 0x11: return "LZ77UnCompNormalWrite8bit";
                case 0x12: return "LZ77UnCompNormalWrite8bit";
                case 0x13: return "HuffUnCompReadNormal";
                case 0x14: return "RLUnCompReadNormalWrite8bit";
                case 0x15: return "RLUnCompReadNormalWrite16bit";
                case 0x16: return "Diff8bitUnFilterNormalWrite8bit";
                case 0x17: return "Diff8bitUnFilterNormalWrite8bit";
                case 0x18: return "Diff16bitUnFilter";
                case 0x19: return "SoundBias";
                case 0x1A: return "SoundDriverInit";
                case 0x1B: return "SoundDriverMode";
                case 0x1C: return "SoundDriverMain";
                case 0x1D: return "SoundDriverVSync";
                case 0x1E: return "SoundChannelClear";
                case 0x1F: return "MidiKey2Freq";
                case 0x20: return "SoundWHatever0";
                case 0x21: return "SoundWHatever1";
                case 0x22: return "SoundWHatever2";
                case 0x23: return "SoundWHatever3";
                case 0x24: return "SoundWHatever4";
                case 0x25: return "MultiBoot";
                case 0x26: return "HardReset";
                case 0x27: return "CustomHalt";
                case 0x28: return "SoundDriverVSyncOff";
                case 0x29: return "SoundDriverVSyncOn";
                case 0x2A: return "SoundGetJumpList";
            }
            return "";
        }
    }
}
