// SPDX-License-Identifier: GPL-3.0-or-later
// Decomp-project diff-to-source migration assistant (#1131 slice 3).
//
// ADVISORY + READ-ONLY. Given a decomp project's BUILT/baseline ROM and a
// FEBuilder-edited ROM, this diffs the two, coalesces the changed byte ranges,
// and classifies each range for a contributor migrating the edit back to source:
//   - nearest symbol (reusing the #1130 MergedAsmMapFile span-covering resolver),
//   - a category (struct table / graphics-palette / compressed / map / text /
//     music / unknown), derived from the symbol + section + known RomInfo table
//     ranges + a strict LZ77 signature probe,
//   - a source suggestion (object/source file when the .map carried it, else the
//     section, else honestly "manual"),
//   - an HONEST confidence (High only when a covering project symbol names the
//     WHOLE range and it maps to a known table/section; Medium for a nearby /
//     shipped-only symbol; Low for compressed / unknown / ambiguous spans).
//
// The built ROM is the canonical baseline; the edited ROM is read, never treated
// as final output, and NOTHING is written (source rewrite is #1132). Every public
// method is bounds-guarded and NEVER throws — a fault yields an empty report.
using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>Coarse classification of a changed range for source migration.</summary>
    public enum MigrationCategory
    {
        Unknown = 0,
        StructTable = 1,
        GraphicsPalette = 2,
        Compressed = 3,
        Map = 4,
        Text = 5,
        Music = 6,
    }

    /// <summary>Honesty level of a range's source suggestion.</summary>
    public enum MigrationConfidence
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>
    /// One classified changed range. <see cref="SpanLength"/> is the coalesced
    /// covered span (may include unchanged bytes after a small-gap merge);
    /// <see cref="ChangedBytes"/> is the count of bytes that ACTUALLY differ inside
    /// that span — the two are reported separately so a merged span never implies
    /// every byte changed (Copilot review finding 1).
    /// </summary>
    public sealed class MigrationRange
    {
        /// <summary>File offset of the range start.</summary>
        public uint Offset { get; set; }
        /// <summary>Coalesced covered span length (changed + small inter-gaps).</summary>
        public uint SpanLength { get; set; }
        /// <summary>Bytes that actually differ inside the span.</summary>
        public uint ChangedBytes { get; set; }

        /// <summary>Nearest symbol name, or "" when none resolved.</summary>
        public string Symbol { get; set; } = "";
        /// <summary>Offset of the range start past the symbol base (0 when exact / none).</summary>
        public uint SymbolOffset { get; set; }
        /// <summary>True when a single symbol's SPAN fully covers the whole range.</summary>
        public bool SymbolCovers { get; set; }

        /// <summary>Owning section (e.g. ".rodata"), "" when unknown.</summary>
        public string Section { get; set; } = "";
        /// <summary>Object/source file suggestion, or an honest "(...)" placeholder.</summary>
        public string SourceFile { get; set; } = "";

        public MigrationCategory Category { get; set; } = MigrationCategory.Unknown;
        /// <summary>Human source-migration suggestion (always actionable / honest).</summary>
        public string Suggestion { get; set; } = "";
        public MigrationConfidence Confidence { get; set; } = MigrationConfidence.Low;

        /// <summary>Which artifact named the symbol (map/elf/sym/json), or shipped/none.</summary>
        public DecompArtifactSource SymbolSource { get; set; } = DecompArtifactSource.None;

        /// <summary>True when this range requires manual migration (compressed/unknown/ambiguous).</summary>
        public bool Manual => Confidence == MigrationConfidence.Low;
    }

    /// <summary>The full advisory report for a built-vs-edited migration analysis.</summary>
    public sealed class MigrationReport
    {
        public List<MigrationRange> Ranges { get; } = new List<MigrationRange>();
        /// <summary>Sum of bytes that actually differ across all ranges.</summary>
        public uint TotalChangedBytes { get; set; }
        public int RangeCount => Ranges.Count;
        /// <summary>Count of ranges flagged manual / low-confidence.</summary>
        public int ManualCount
        {
            get
            {
                int n = 0;
                foreach (var r in Ranges) if (r != null && r.Manual) n++;
                return n;
            }
        }
        /// <summary>True when the built ROM was missing / unreadable (no analysis possible).</summary>
        public bool BuiltMissing { get; set; }
    }

    /// <summary>
    /// READ-ONLY diff-to-source migration analyzer. Pure aside from reading the two
    /// ROM byte arrays + the symbol resolver; never writes; never throws.
    /// </summary>
    public static class DecompDiffMigrationCore
    {
        /// <summary>Default small-gap merge distance (bytes) for range coalescing.</summary>
        public const int DefaultMaxGap = 16;

        // Bound the back-scan window when sniffing for a compressed header that
        // starts a few bytes before the diff (the edit may land mid-asset).
        const int CompressedBackScan = 4;

        /// <summary>
        /// Analyze the differences between a built ROM and an edited ROM and classify
        /// each coalesced changed range for source migration. NEVER throws.
        /// </summary>
        /// <param name="builtRom">The project's built/baseline ROM (canonical).</param>
        /// <param name="editedBytes">The FEBuilder-edited ROM bytes.</param>
        /// <param name="map">Merged symbol resolver (project over shipped); may be null.</param>
        /// <param name="resolver">Decomp symbol resolver for section/source hints; may be null.</param>
        /// <param name="maxGap">Small-gap merge distance for coalescing (default 16).</param>
        public static MigrationReport Analyze(
            ROM builtRom, byte[] editedBytes,
            MergedAsmMapFile map, DecompSymbolResolver resolver,
            int maxGap = DefaultMaxGap)
        {
            return AnalyzeInternal(builtRom, editedBytes, hasFill: false, fillByte: 0, map, resolver, maxGap);
        }

        /// <summary>
        /// Fill-aware variant of <see cref="Analyze"/>: classify each coalesced changed
        /// range between a shorter baseline ROM (virtually extended with
        /// <paramref name="fillByte"/>) and a longer edited buffer, reusing the exact
        /// same diff/coalesce/classify pipeline so the buildfile exporter's payload
        /// ownership can never drift from the classification. NEVER throws.
        /// </summary>
        public static MigrationReport AnalyzeWithFill(
            ROM builtRom, byte[] editedBytes, byte fillByte,
            MergedAsmMapFile map, DecompSymbolResolver resolver,
            int maxGap = DefaultMaxGap)
        {
            return AnalyzeInternal(builtRom, editedBytes, hasFill: true, fillByte, map, resolver, maxGap);
        }

        static MigrationReport AnalyzeInternal(
            ROM builtRom, byte[] editedBytes, bool hasFill, byte fillByte,
            MergedAsmMapFile map, DecompSymbolResolver resolver,
            int maxGap)
        {
            var report = new MigrationReport();
            try
            {
                byte[] builtBytes = builtRom?.Data;
                if (builtBytes == null || builtBytes.Length == 0)
                {
                    report.BuiltMissing = true;
                    return report;
                }
                if (editedBytes == null)
                    return report;
                if (maxGap < 0) maxGap = 0;

                RomDiffCore.DiffResult diff = hasFill
                    ? RomDiffCore.CompareWithFill(builtBytes, editedBytes, fillByte)
                    : RomDiffCore.Compare(builtBytes, editedBytes);
                if (diff == null || diff.Ranges.Count == 0)
                    return report;

                List<Coalesced> coalesced = Coalesce(diff.Ranges, maxGap);

                foreach (Coalesced c in coalesced)
                {
                    MigrationRange r = Classify(builtRom, builtBytes, c, map, resolver);
                    report.Ranges.Add(r);
                    report.TotalChangedBytes += r.ChangedBytes;
                }
            }
            catch
            {
                // never throw — return whatever we accumulated
            }
            return report;
        }

        // ---------------------------------------------------------------- coalesce

        /// <summary>A coalesced span carrying the true changed-byte count.</summary>
        internal struct Coalesced
        {
            public uint Offset;
            public uint SpanLength;
            public uint ChangedBytes;
        }

        /// <summary>
        /// Merge strictly-contiguous diff ranges that are separated by &lt;= maxGap
        /// unchanged bytes into one covered span, tracking the true changed-byte
        /// total separately from the span length. NEVER throws.
        /// </summary>
        internal static List<Coalesced> Coalesce(List<RomDiffCore.DiffRange> ranges, int maxGap)
        {
            var result = new List<Coalesced>();
            if (ranges == null) return result;

            Coalesced cur = default;
            bool open = false;

            foreach (RomDiffCore.DiffRange dr in ranges)
            {
                if (dr == null || dr.Length == 0) continue;
                if (!open)
                {
                    cur = new Coalesced { Offset = dr.Offset, SpanLength = dr.Length, ChangedBytes = dr.Length };
                    open = true;
                    continue;
                }

                ulong curEnd = (ulong)cur.Offset + cur.SpanLength;       // exclusive
                ulong gap = dr.Offset >= curEnd ? (ulong)dr.Offset - curEnd : 0UL;
                if (gap <= (ulong)maxGap && dr.Offset >= cur.Offset)
                {
                    ulong newEnd = (ulong)dr.Offset + dr.Length;
                    if (newEnd > curEnd)
                        cur.SpanLength = (uint)(newEnd - cur.Offset);
                    cur.ChangedBytes += dr.Length;
                }
                else
                {
                    result.Add(cur);
                    cur = new Coalesced { Offset = dr.Offset, SpanLength = dr.Length, ChangedBytes = dr.Length };
                }
            }
            if (open) result.Add(cur);
            return result;
        }

        // ---------------------------------------------------------------- classify

        static MigrationRange Classify(
            ROM rom, byte[] builtBytes, Coalesced c,
            MergedAsmMapFile map, DecompSymbolResolver resolver)
        {
            var r = new MigrationRange
            {
                Offset = c.Offset,
                SpanLength = c.SpanLength,
                ChangedBytes = c.ChangedBytes,
            };

            uint startPtr = U.toPointer(c.Offset);
            uint endOffsetExclusive = (uint)Math.Min((ulong)c.Offset + c.SpanLength, uint.MaxValue);

            // --- nearest symbol (span-covering resolver from #1130) ---
            uint symBaseOffset; // file offset of the covering symbol base (NOT_FOUND when none)
            ResolveSymbol(r, map, resolver, startPtr, c.SpanLength, out symBaseOffset);

            // A fully-covering project (map/elf/sym/json) symbol is the strongest
            // attribution we have: it names the whole changed span and points at a
            // source location. Drives High confidence (gated below by category).
            bool covering = r.SymbolCovers && IsProjectSource(r.SymbolSource);

            // --- classification precedence (highest wins): ---
            // 1. Compressed (strict LZ77 probe) — ALWAYS Low + manual. Sniff the
            //    range start (with a small back-scan) AND the covering symbol base,
            //    so an edit landing DEEP inside a compressed asset is still caught.
            if (SniffCompressed(builtBytes, c.Offset)
                || (covering && symBaseOffset != U.NOT_FOUND && SniffCompressedAt(builtBytes, symBaseOffset)))
            {
                r.Category = MigrationCategory.Compressed;
                r.Confidence = MigrationConfidence.Low;
                r.Suggestion = string.IsNullOrEmpty(r.Symbol)
                    ? "manual migration required (compressed asset — re-export from source)"
                    : $"manual migration required (compressed asset '{r.Symbol}' — re-export from source)";
                FinalizeSourceFile(r);
                return r;
            }

            // 1.5 Ambiguous: the coalesced span is NOT fully covered by a single
            // symbol, yet a symbol is nearby — it may cross symbol/table boundaries.
            // Mark manual + Low so High is never claimed for a span we cannot
            // attribute wholesale (Copilot finding 2).
            bool ambiguous = !r.SymbolCovers && c.SpanLength > 0 && HasNearbySymbol(r);
            if (ambiguous)
            {
                r.Category = ResolveCategory(rom, r, c.Offset, endOffsetExclusive);
                r.Confidence = MigrationConfidence.Low;
                r.Suggestion = $"manual migration required (range spans multiple symbols near '{r.Symbol}')";
                FinalizeSourceFile(r);
                return r;
            }

            // 2. Category from a known RomInfo range, else section/name heuristic.
            KnownRange kr = MatchKnownRange(rom, c.Offset, endOffsetExclusive);
            MigrationCategory cat = kr?.Category ?? HeuristicCategory(r.Section, r.Symbol);
            r.Category = cat;

            // High requires a KNOWN category (not Unknown). A covering project
            // symbol whose category we cannot determine — e.g. a neutral .text /
            // .rodata symbol with no data keyword — is still raw-bytes work, so it
            // must stay Low + manual (Copilot PR #1139 finding 1): never claim High
            // for unknown/raw content even when the symbol name is known.
            if (covering && cat != MigrationCategory.Unknown)
            {
                r.Confidence = MigrationConfidence.High;
                r.Suggestion = kr != null
                    ? $"edit the {kr.Label} source for symbol '{r.Symbol}'"
                    : $"edit source for symbol '{r.Symbol}' ({CategoryWord(cat)})";
                FinalizeSourceFile(r);
                return r;
            }

            if (kr != null)
            {
                // Known range but no covering project symbol (or covering+unknown
                // shouldn't reach here since kr implies a category) → Medium.
                r.Confidence = MigrationConfidence.Medium;
                r.Suggestion = $"likely the {kr.Label} (entry inferred — verify against source)";
                FinalizeSourceFile(r);
                return r;
            }

            if (cat != MigrationCategory.Unknown)
            {
                // Section/name heuristic only (no covering symbol) → Low (verify).
                r.Confidence = MigrationConfidence.Low;
                r.Suggestion = string.IsNullOrEmpty(r.Symbol)
                    ? $"likely {CategoryWord(cat)} (heuristic — verify against source)"
                    : $"likely {CategoryWord(cat)} near '{r.Symbol}' (verify against source)";
                FinalizeSourceFile(r);
                return r;
            }

            // 3. Unknown / raw — explicit manual. A covering project symbol still
            // names WHERE the change is, so surface it, but the content is raw.
            r.Category = MigrationCategory.Unknown;
            r.Confidence = MigrationConfidence.Low;
            r.Suggestion = string.IsNullOrEmpty(r.Symbol)
                ? "manual migration required (unknown/raw bytes — no symbol)"
                : (covering
                    ? $"manual migration required (raw bytes in symbol '{r.Symbol}' — unknown data type)"
                    : $"manual migration required (raw bytes near '{r.Symbol}')");
            FinalizeSourceFile(r);
            return r;
        }

        // Best-effort category for the ambiguous path (known range first, else heuristic).
        static MigrationCategory ResolveCategory(ROM rom, MigrationRange r, uint start, uint endExclusive)
        {
            KnownRange kr = MatchKnownRange(rom, start, endExclusive);
            if (kr != null) return kr.Category;
            return HeuristicCategory(r.Section, r.Symbol);
        }

        // Resolve the nearest symbol + whether its span fully covers the range, and
        // pull section / object-path hints. Outputs the covering symbol's FILE
        // offset (NOT_FOUND when none). Bounds-guarded; never throws.
        static void ResolveSymbol(
            MigrationRange r, MergedAsmMapFile map, DecompSymbolResolver resolver,
            uint startPtr, uint spanLength, out uint symBaseOffset)
        {
            symBaseOffset = U.NOT_FOUND;
            if (map == null) return;
            try
            {
                uint near = map.SearchNear(startPtr);
                if (near == U.NOT_FOUND) return;
                if (!map.TryGetValue(near, out AsmMapSt st) || st == null) return;

                r.Symbol = st.Name ?? "";
                r.SymbolOffset = startPtr >= near ? startPtr - near : 0;
                if (map.TryGetSource(near, out var src)) r.SymbolSource = src;
                symBaseOffset = U.toOffset(near);

                // Whole-range coverage: [startPtr, endPtr) inside [near, near+Length).
                // Compute the EXCLUSIVE end pointer by pointer arithmetic from
                // startPtr — NOT U.toPointer(endOffsetExclusive), which mis-handles a
                // span ending exactly at the EWRAM boundary (file off 0x02000000 on a
                // 32MB ROM: toPointer leaves it as 0x02000000, breaking coverage at
                // the ROM upper boundary). ulong avoids the 0x0A000000 wrap. (Copilot PR #1139.)
                ulong endPtr = (ulong)startPtr + spanLength;
                ulong symEnd = (ulong)near + st.Length;
                r.SymbolCovers = st.Length > 0
                    && startPtr >= near
                    && endPtr <= symEnd;

                if (resolver != null)
                {
                    if (resolver.TryGetSection(near, out string sec) && !string.IsNullOrEmpty(sec))
                        r.Section = sec;
                    if (resolver.TryGetObjectPath(near, out string op) && !string.IsNullOrEmpty(op))
                        r.SourceFile = op;
                }
            }
            catch { /* never throw */ }
        }

        static bool HasNearbySymbol(MigrationRange r) => !string.IsNullOrEmpty(r.Symbol);

        static bool IsProjectSource(DecompArtifactSource s)
            => s == DecompArtifactSource.Map || s == DecompArtifactSource.Elf
            || s == DecompArtifactSource.Sym || s == DecompArtifactSource.Json;

        // Pick the most honest SourceFile when ResolveSymbol did not already set an
        // object path: section name when known, else an explicit placeholder.
        static void FinalizeSourceFile(MigrationRange r)
        {
            if (!string.IsNullOrEmpty(r.SourceFile)) return;
            if (!string.IsNullOrEmpty(r.Section))
                r.SourceFile = "(section " + r.Section + ")";
            else
                r.SourceFile = "(unknown — manual)";
        }

        // ---------------------------------------------------------------- compressed

        // Strict LZ77 sniff: LZ77.getCompressedSize returns 0 for any malformed /
        // truncated stream, so we only flag a genuine, fully-decodable header at the
        // range start or within a small back-scan window. NEVER throws.
        static bool SniffCompressed(byte[] data, uint offset)
        {
            try
            {
                if (data == null) return false;
                for (int back = 0; back <= CompressedBackScan; back++)
                {
                    long o = (long)offset - back;
                    if (o < 0) break;
                    if (SniffCompressedAt(data, (uint)o)) return true;
                }
            }
            catch { }
            return false;
        }

        // Strict LZ77 sniff at one exact offset. NEVER throws.
        static bool SniffCompressedAt(byte[] data, uint offset)
        {
            try
            {
                if (data == null || offset >= (uint)data.Length) return false;
                if (data[offset] != 0x10) return false;          // LZ77 header byte
                return LZ77.getCompressedSize(data, offset) > 0;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- known ranges

        /// <summary>A reliable RomInfo-backed file-offset range and its category.</summary>
        sealed class KnownRange
        {
            public uint Start;        // inclusive file offset
            public uint End;          // exclusive file offset
            public MigrationCategory Category;
            public string Label;
            public bool Covers(uint start, uint endExclusive)
                => start >= Start && start < End && endExclusive <= End;
        }

        // Match the range to a conservative, reliable known region. Only ranges we
        // can bound with confidence are included (Copilot findings 5 + #1139.2):
        // each struct table uses the SAME stop rule as its WinForms reader, NOT a
        // generic "first u32 != 0xFFFFFFFF" guess that could over-claim 2048 rows of
        // unrelated ROM data. NEVER throws.
        static KnownRange MatchKnownRange(ROM rom, uint start, uint endExclusive)
        {
            try
            {
                if (rom?.RomInfo == null) return null;
                ROMFEINFO info = rom.RomInfo;

                // Text region — explicit start/end addresses (reliable).
                uint tStart = info.text_data_start_address;
                uint tEnd = info.text_data_end_address;
                if (tStart != 0 && tEnd > tStart)
                {
                    var kr = new KnownRange { Start = tStart, End = tEnd, Category = MigrationCategory.Text, Label = "text data region" };
                    if (kr.Covers(start, endExclusive)) return kr;
                }

                KnownRange t;

                // Unit table — stop at RomInfo.unit_maxcount (mirrors UnitForm).
                uint unitMax = info.unit_maxcount;
                if (unitMax > 0)
                {
                    t = TableRange(rom, info.unit_pointer, info.unit_datasize, MigrationCategory.StructTable,
                        "unit table", (int i, uint addr) => i < unitMax);
                    if (t != null && t.Covers(start, endExclusive)) return t;
                }

                // Class table — i==0 ok; stop at i>0xff OR u8(addr+4)==0 (mirrors ClassForm).
                t = TableRange(rom, info.class_pointer, info.class_datasize, MigrationCategory.StructTable,
                    "class table", (int i, uint addr) =>
                    {
                        if (i == 0) return true;
                        if (i > 0xff) return false;
                        if ((ulong)addr + 5 > (ulong)rom.Data.Length) return false;
                        return rom.u8(addr + 4) != 0;
                    });
                if (t != null && t.Covers(start, endExclusive)) return t;

                // Item table — stop at i>0xff; row valid while +12 (and +16 for
                // non-FE8U) is a pointer-or-NULL (mirrors ItemForm).
                bool fe8u = info.version == 8 && info.is_multibyte == false;
                t = TableRange(rom, info.item_pointer, info.item_datasize, MigrationCategory.StructTable,
                    "item table", (int i, uint addr) =>
                    {
                        if (i > 0xff) return false;
                        if ((ulong)addr + 20 > (ulong)rom.Data.Length) return false;
                        bool ok = U.isPointerOrNULL(rom.u32(addr + 12));
                        if (!fe8u) ok = ok && U.isPointerOrNULL(rom.u32(addr + 16));
                        return ok;
                    });
                if (t != null && t.Covers(start, endExclusive)) return t;
            }
            catch { }
            return null;
        }

        // Build a conservative struct-table file-offset range using the supplied
        // table-specific stop predicate (the SAME rule as the WF reader). datasize
        // must be sane non-zero; count is hard-capped so a corrupt table can't run
        // away. Returns null on any fault. NEVER throws.
        static KnownRange TableRange(ROM rom, uint pointerLoc, uint dataSize,
            MigrationCategory cat, string label, Func<int, uint, bool> isDataExists)
        {
            try
            {
                if (pointerLoc == 0 || dataSize == 0 || dataSize > 0x400) return null;
                uint baseAddr = rom.p32(pointerLoc);     // already toOffset'd
                if (baseAddr == 0 || baseAddr >= (uint)rom.Data.Length) return null;

                const int CAP = 0x1000;   // hard ceiling regardless of the predicate
                uint count = rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                {
                    if (i >= CAP) return false;
                    return isDataExists(i, addr);
                });
                if (count == 0) return null;
                ulong end = (ulong)baseAddr + (ulong)count * dataSize;
                if (end > (ulong)rom.Data.Length) end = (ulong)rom.Data.Length;
                return new KnownRange { Start = baseAddr, End = (uint)end, Category = cat, Label = label };
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- heuristics

        // Section / symbol-name keyword heuristic. Conservative — Unknown by default.
        // The CODE section name (.text) is intentionally EXCLUDED from the Text
        // (string-data) keyword so a code symbol in .text is never mislabelled as
        // text strings — only the SYMBOL name is searched for text-string hints.
        static MigrationCategory HeuristicCategory(string section, string symbol)
        {
            string sec = (section ?? "").ToLowerInvariant();
            string sym = (symbol ?? "").ToLowerInvariant();
            string s = (sec + " " + sym).Trim();
            if (s.Length == 0) return MigrationCategory.Unknown;

            if (Has(s, "pal") || Has(s, "palette") || Has(s, "gfx") || Has(s, "img")
                || Has(s, "image") || Has(s, "sprite") || Has(s, "tile") || Has(s, "icon"))
                return MigrationCategory.GraphicsPalette;
            if (Has(s, "song") || Has(s, "sound") || Has(s, "music") || Has(s, "bgm")
                || Has(s, "instrument") || Has(s, "sound_table"))
                return MigrationCategory.Music;
            if (Has(s, "map") || Has(s, "chapter") || Has(s, "tileset"))
                return MigrationCategory.Map;
            // "text" only from the SYMBOL name (a .text code section must NOT match).
            if (Has(sym, "text") || Has(s, "msg") || Has(s, "string") || Has(s, "dialog"))
                return MigrationCategory.Text;
            // Generic struct/table-shaped data — checked last so a more specific
            // category (gfx/music/map/text) wins first.
            if (Has(s, "table") || Has(s, "data") || Has(s, "array")
                || Has(s, "struct") || Has(s, "list"))
                return MigrationCategory.StructTable;
            return MigrationCategory.Unknown;
        }

        static bool Has(string hay, string needle)
            => hay.IndexOf(needle, StringComparison.Ordinal) >= 0;

        static string CategoryWord(MigrationCategory c)
        {
            switch (c)
            {
                case MigrationCategory.StructTable: return "struct table data";
                case MigrationCategory.GraphicsPalette: return "graphics/palette";
                case MigrationCategory.Compressed: return "compressed asset";
                case MigrationCategory.Map: return "map data";
                case MigrationCategory.Text: return "text data";
                case MigrationCategory.Music: return "music/song data";
                default: return "unknown/raw bytes";
            }
        }

        // ---------------------------------------------------------------- formatting

        /// <summary>Format the report as a TSV (one row per range). NEVER throws.</summary>
        public static string FormatTSV(MigrationReport report)
        {
            var sb = new StringBuilder();
            sb.Append("StartAddr\tSpanLength\tChangedBytes\tSymbol\t+Offset\tCovers\tCategory\tSection\tSourceFile\tConfidence\tSymbolSource\tSuggestion\n");
            if (report?.Ranges == null) return sb.ToString().TrimEnd('\n');
            foreach (MigrationRange r in report.Ranges)
            {
                if (r == null) continue;   // list is publicly mutable — stay non-throwing
                sb.Append("0x").Append(r.Offset.ToString("X06")).Append('\t');
                sb.Append(r.SpanLength).Append('\t');
                sb.Append(r.ChangedBytes).Append('\t');
                sb.Append(Clean(r.Symbol)).Append('\t');
                sb.Append("+0x").Append(r.SymbolOffset.ToString("X")).Append('\t');
                sb.Append(r.SymbolCovers ? "yes" : "no").Append('\t');
                sb.Append(CategoryWord(r.Category)).Append('\t');
                sb.Append(Clean(r.Section)).Append('\t');
                sb.Append(Clean(r.SourceFile)).Append('\t');
                sb.Append(r.Confidence).Append('\t');
                sb.Append(r.SymbolSource).Append('\t');
                sb.Append(Clean(r.Suggestion)).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>Human-readable summary of the report. NEVER throws.</summary>
        public static string FormatSummary(MigrationReport report)
        {
            if (report == null) return "No report.";
            if (report.BuiltMissing) return "Built ROM missing/unreadable — cannot analyze.";
            if (report.RangeCount == 0) return "ROMs are identical — no source migration needed.";

            var sb = new StringBuilder();
            foreach (MigrationRange r in report.Ranges)
            {
                if (r == null) continue;   // list is publicly mutable — stay non-throwing
                uint end = r.Offset + (r.SpanLength > 0 ? r.SpanLength - 1 : 0);
                string sym = string.IsNullOrEmpty(r.Symbol) ? "(no symbol)" : r.Symbol + "+0x" + r.SymbolOffset.ToString("X");
                sb.Append("0x").Append(r.Offset.ToString("X06")).Append("-0x").Append(end.ToString("X06"))
                  .Append("  [").Append(CategoryWord(r.Category)).Append(", ").Append(r.Confidence).Append("]  ")
                  .Append(sym).Append("  ").Append(r.ChangedBytes).Append(" byte(s) changed")
                  .Append("  => ").Append(r.Suggestion).Append('\n');
            }
            sb.Append("Total: ").Append(report.TotalChangedBytes).Append(" byte(s) changed across ")
              .Append(report.RangeCount).Append(" range(s); ")
              .Append(report.ManualCount).Append(" need manual migration (low-confidence).");
            return sb.ToString();
        }

        // Replace tabs / newlines so a value never breaks the TSV grid.
        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
