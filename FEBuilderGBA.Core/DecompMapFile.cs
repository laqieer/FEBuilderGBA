// SPDX-License-Identifier: GPL-3.0-or-later
// Decomp-project artifact symbol parsers (#1130 slice 2).
//
// Three tolerant, never-throws parsers that read the symbol artifacts a GBA
// decompilation build emits and turn each into a flat list of DecompSymbol
// records (name + addr + best-effort size + section):
//   - DecompMapParser:        GNU ld `.map` linker map.
//   - DecompSymParser:        no$gba `.sym` (HEXADDR<space>name) symbol file.
//   - DecompSymbolJsonParser: a JSON symbol dump (array or {symbols:[...]}).
//
// These are READ-ONLY: they never touch the ROM and never throw — any null /
// malformed / faulting input yields an empty list so an early, headless, or
// hostile call can never crash the host. They are consumed by
// DecompSymbolResolver, which layers them over the shipped asmmap symbol table
// at the CoreAsmMapCache.GetAsmMapFile() chokepoint.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// One decomp symbol record: a name, its GBA virtual address (EWRAM / IWRAM /
    /// ROM), a best-effort byte size (0 when unknown), and the owning section
    /// name (possibly empty).
    /// </summary>
    public sealed class DecompSymbol
    {
        public string Name;
        public uint Addr;
        public uint Size;
        public string Section = "";
        /// <summary>
        /// Best-effort owning object-file / source path the linker map carried on
        /// the section line (e.g. <c>build/src/unit.o</c>), or empty when the map
        /// did not record one. Read-only hint for the #1131 diff-to-source
        /// migration assistant's SourceFile suggestion; never fabricated.
        /// </summary>
        public string ObjectPath = "";
    }

    /// <summary>
    /// Tolerant GNU-ld <c>.map</c> linker-map parser. NEVER throws; returns an
    /// empty list on null / fault. Best-effort Size from section ranges.
    /// </summary>
    public static class DecompMapParser
    {
        // A symbol line: leading whitespace, then `0xADDR` then a single identifier
        // token, with NOTHING after it (GNU ld `<ws>0xADDR<ws>name` symbol form).
        // The negative trailing-anchor rules out the `0xADDR 0xSIZE` section form
        // and any line carrying extra columns (e.g. an object-file path).
        static readonly Regex SymbolLineRegex = new Regex(
            @"^\s*0x([0-9a-fA-F]{1,16})\s+([A-Za-z_.$][\w.$]*)\s*$",
            RegexOptions.Compiled);

        // A section line: `.section  0xADDR  0xSIZE [objpath]`.
        static readonly Regex SectionLineRegex = new Regex(
            @"^\s*(\.[A-Za-z0-9_.$*]+)\s+0x([0-9a-fA-F]+)\s+0x([0-9a-fA-F]+)(\s+\S.*)?$",
            RegexOptions.Compiled);

        // A "wrapped" section name that owns the address/size on the NEXT line:
        // a line that is JUST a long `.section_name`.
        static readonly Regex WrappedSectionNameRegex = new Regex(
            @"^\s*(\.[A-Za-z0-9_.$*]+)\s*$",
            RegexOptions.Compiled);

        // The continuation of a wrapped section: `<ws>0xADDR 0xSIZE`.
        static readonly Regex WrappedSectionContinuationRegex = new Regex(
            @"^\s+0x([0-9a-fA-F]+)\s+0x([0-9a-fA-F]+)(\s+\S.*)?$",
            RegexOptions.Compiled);

        /// <summary>
        /// Parse a GNU-ld <c>.map</c> file text into a flat, deduped symbol list.
        /// NEVER throws; empty on null / fault.
        /// </summary>
        public static List<DecompSymbol> Parse(string mapText)
        {
            var result = new List<DecompSymbol>();
            try
            {
                if (string.IsNullOrEmpty(mapText))
                    return result;

                string[] lines = mapText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

                // Section boundaries (sorted later) used to bound a symbol's Size.
                var boundaries = new List<uint>();
                // (addr,name,section,objectPath) raw hits, in file order.
                var raw = new List<(uint Addr, string Name, string Section, string ObjectPath)>();

                string curSection = "";
                // Best-effort object-file / source path the section line carried,
                // reset whenever we cross into a new section. Empty when absent.
                string curObjectPath = "";

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line == null) continue;

                    // Skip the obvious noise / linker bookkeeping lines first.
                    if (ShouldSkipLine(line)) continue;

                    // Section line (single-line form).
                    Match secm = SectionLineRegex.Match(line);
                    if (secm.Success)
                    {
                        curSection = secm.Groups[1].Value;
                        // Group 4 is the optional trailing column (an object-file
                        // path); keep its trimmed value as a SourceFile hint.
                        curObjectPath = ExtractObjectPath(secm.Groups[4].Value);
                        uint secAddr = ParseHex(secm.Groups[2].Value);
                        uint secSize = ParseHex(secm.Groups[3].Value);
                        AddSectionBoundaries(boundaries, secAddr, secSize);
                        continue;
                    }

                    // Wrapped section: a lone `.section` then a continuation line.
                    Match wrapName = WrappedSectionNameRegex.Match(line);
                    if (wrapName.Success && i + 1 < lines.Length)
                    {
                        Match cont = WrappedSectionContinuationRegex.Match(lines[i + 1] ?? "");
                        if (cont.Success)
                        {
                            curSection = wrapName.Groups[1].Value;
                            // Group 3 is the wrapped continuation's optional object path.
                            curObjectPath = ExtractObjectPath(cont.Groups[3].Value);
                            uint secAddr = ParseHex(cont.Groups[1].Value);
                            uint secSize = ParseHex(cont.Groups[2].Value);
                            AddSectionBoundaries(boundaries, secAddr, secSize);
                            i++; // consume the continuation line
                            continue;
                        }
                        // A lone `.section` with no continuation is just section
                        // bookkeeping — never a symbol; fall through (it can't
                        // match the symbol regex anyway since it has no 0xADDR).
                    }

                    // Symbol line.
                    Match symm = SymbolLineRegex.Match(line);
                    if (symm.Success)
                    {
                        uint addr = ParseHex(symm.Groups[1].Value);
                        string name = symm.Groups[2].Value;

                        if (addr < 0x02000000) continue;     // RAM-mirror / absolute bookkeeping
                        if (addr == 0) continue;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name[0] == '$') continue;          // mapping symbols ($t/$a/$d)
                        if (name == ".") continue;             // hidden `.`-only bookkeeping

                        raw.Add((addr, name, curSection, curObjectPath));
                        boundaries.Add(addr);
                    }
                }

                FillSizesAndDedup(raw, boundaries, result);
            }
            catch
            {
                // never throw — return whatever we have (possibly empty)
                return result;
            }
            return result;
        }

        // Compute best-effort Size = next boundary above addr - addr, then dedup by
        // addr keeping the FIRST concrete symbol at each address (first wins).
        static void FillSizesAndDedup(
            List<(uint Addr, string Name, string Section, string ObjectPath)> raw,
            List<uint> boundaries,
            List<DecompSymbol> result)
        {
            boundaries.Sort();

            var seen = new HashSet<uint>();
            foreach (var r in raw)
            {
                if (seen.Contains(r.Addr))
                    continue;                 // same-address alias: first wins
                seen.Add(r.Addr);

                uint size = 0;
                // Find the smallest boundary strictly greater than this addr.
                int lo = 0, hi = boundaries.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (boundaries[mid] <= r.Addr) lo = mid + 1; else hi = mid;
                }
                if (lo < boundaries.Count)
                {
                    uint next = boundaries[lo];
                    if (next > r.Addr)
                        size = next - r.Addr;
                }

                result.Add(new DecompSymbol
                {
                    Name = r.Name,
                    Addr = r.Addr,
                    Size = size,
                    Section = r.Section ?? "",
                    ObjectPath = r.ObjectPath ?? "",
                });
            }
        }

        // Clean an optional trailing section-line column into an object-file path
        // hint. Only accepts a single token that looks like a path with an object
        // extension (.o/.a/.obj) — never a fill/expression or a make variable —
        // so we never fabricate a bogus SourceFile. Empty when none qualifies.
        static string ExtractObjectPath(string trailing)
        {
            if (string.IsNullOrEmpty(trailing)) return "";
            string t = trailing.Trim();
            if (t.Length == 0) return "";
            // Take the first whitespace-delimited token (the linker writes the
            // archive(member.o) / path.o as the leading token of the column).
            int sp = t.IndexOfAny(new[] { ' ', '\t' });
            string tok = sp >= 0 ? t.Substring(0, sp) : t;
            if (tok.IndexOf('$') >= 0) return "";          // make var — not a file
            if (tok.IndexOf("0x", StringComparison.Ordinal) == 0) return "";
            // Must look like an object / archive member.
            if (tok.EndsWith(".o", StringComparison.OrdinalIgnoreCase)
                || tok.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
                || tok.IndexOf(".o)", StringComparison.OrdinalIgnoreCase) >= 0
                || tok.EndsWith(".a", StringComparison.OrdinalIgnoreCase))
            {
                return tok;
            }
            return "";
        }

        // Add a section's START and END addresses as Size boundaries. The END
        // boundary (secAddr + secSize) is what gives the LAST symbol in a section a
        // correct Size and, crucially, stops a RAM (IWRAM/EWRAM) section's last
        // symbol from spanning all the way into the next ROM section. Overflow- and
        // range-guarded: an END past ROM space (> 0x0A000000) or that wraps uint is
        // skipped (Copilot PR #1138).
        static void AddSectionBoundaries(List<uint> boundaries, uint secAddr, uint secSize)
        {
            if (secAddr >= 0x02000000)
                boundaries.Add(secAddr);

            if (secSize > 0)
            {
                ulong end = (ulong)secAddr + secSize;
                if (end > secAddr && end <= 0x0A000000UL)
                    boundaries.Add((uint)end);
            }
        }

        static bool ShouldSkipLine(string line)
        {
            // Cheap substring checks for linker-script / assignment / fill noise.
            if (line.IndexOf("*fill*", StringComparison.Ordinal) >= 0) return true;
            if (line.IndexOf("*(", StringComparison.Ordinal) >= 0) return true;
            if (line.IndexOf("PROVIDE", StringComparison.Ordinal) >= 0) return true;
            if (line.IndexOf("ABSOLUTE", StringComparison.Ordinal) >= 0) return true;
            if (line.IndexOf("= .", StringComparison.Ordinal) >= 0) return true;
            if (line.IndexOf("= ALIGN", StringComparison.Ordinal) >= 0) return true;

            string t = line.TrimStart();
            if (t.StartsWith("LOAD ", StringComparison.Ordinal)) return true;
            if (t.StartsWith("/DISCARD/", StringComparison.Ordinal)) return true;
            return false;
        }

        static uint ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            // Clamp to 32-bit (GBA addrs fit; a 64-bit map value -> low 32 is fine
            // because we filter the result by GBA ranges downstream).
            if (ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong v))
                return unchecked((uint)v);
            return 0;
        }
    }

    /// <summary>
    /// Tolerant symbol-table parser. Handles BOTH:
    /// <list type="bullet">
    /// <item>no$gba <c>.sym</c>: <c>HEXADDR&lt;space&gt;name</c> (mirrors
    /// <see cref="SymbolUtil"/>'s RegistSymbolByNoDoll split).</item>
    /// <item>linker-assignment (#1773, FE8J <c>sym_jp.txt</c>):
    /// <c>Name = 0x08XXXXXX;</c> — tolerant of a trailing <c>;</c> and
    /// <c>/* … */</c> block comments.</item>
    /// </list>
    /// Format is auto-detected per line. Addresses &lt;= 0x100 are skipped. NEVER throws.
    /// </summary>
    public static class DecompSymParser
    {
        // #1773: FE8J sym_jp.txt linker assignment, e.g. "ColorFadeTick = 0x08000234;".
        // Name must start with a letter/underscore (so the linker location counter "."
        // and no$gba "HEXADDR name" lines never match and fall through to the no$gba path).
        static readonly Regex LinkerAssignRx = new Regex(
            @"^\s*([A-Za-z_][A-Za-z0-9_.$]*)\s*=\s*0[xX]([0-9A-Fa-f]+)\s*;?\s*$",
            RegexOptions.Compiled);

        static readonly Regex BlockCommentRx = new Regex(@"/\*.*?\*/",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public static List<DecompSymbol> Parse(string symText)
        {
            var result = new List<DecompSymbol>();
            try
            {
                if (string.IsNullOrEmpty(symText))
                    return result;

                // Strip /* ... */ block comments (sym_jp.txt annotates some assignments).
                // Preserve any newlines the comment spanned so two logical lines can't be
                // merged (which would silently drop the symbols on the merged line, #1789).
                symText = BlockCommentRx.Replace(symText, m =>
                {
                    int nl = 0;
                    for (int i = 0; i < m.Value.Length; i++)
                        if (m.Value[i] == '\n') nl++;
                    return nl > 0 ? new string('\n', nl) : " ";
                });

                string[] lines = symText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var seen = new HashSet<uint>();
                foreach (string line in lines)
                {
                    if (line == null) continue;
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;

                    string name;
                    uint addr;

                    Match la = LinkerAssignRx.Match(trimmed);
                    if (la.Success)
                    {
                        // FE8J sym_jp.txt: "Name = 0xADDR;"
                        name = la.Groups[1].Value;
                        addr = ParseHexU(la.Groups[2].Value);
                    }
                    else
                    {
                        // no$gba line: "100109C MMBDrawInventoryObjs". Column-aligned dumps
                        // use repeated spaces/tabs between addr and name, so split on any
                        // run of whitespace and take the first two tokens (Copilot PR #1138).
                        string[] sp = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (sp.Length < 2) continue;
                        addr = U.atoh(sp[0]);
                        name = sp[1];
                    }

                    if (string.IsNullOrEmpty(name)) continue;
                    if (name[0] == '$') continue;
                    if (addr <= 0x100) continue;
                    if (seen.Contains(addr)) continue;   // first wins
                    seen.Add(addr);

                    result.Add(new DecompSymbol { Name = name, Addr = addr, Size = 0, Section = "" });
                }
            }
            catch
            {
                return result;
            }
            return result;
        }

        // Parse a bare hex string (no 0x prefix) to u32; clamps a 64-bit value to low 32.
        static uint ParseHexU(string hex)
        {
            if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
                return v;
            if (ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong lv))
                return unchecked((uint)lv);
            return 0;
        }
    }

    /// <summary>
    /// Tolerant JSON symbol-dump parser. Accepts a top-level array of objects, or
    /// a top-level object with a <c>symbols</c> array. Each entry:
    /// <c>{ "name": str, "addr": ("0x.." | number), "size"?: num, "section"?: str }</c>.
    /// NEVER throws; empty on fault.
    /// </summary>
    public static class DecompSymbolJsonParser
    {
        public static List<DecompSymbol> Parse(string jsonText)
        {
            var result = new List<DecompSymbol>();
            try
            {
                if (string.IsNullOrWhiteSpace(jsonText))
                    return result;

                using var doc = JsonDocument.Parse(jsonText);
                JsonElement root = doc.RootElement;

                JsonElement arr;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    arr = root;
                }
                else if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("symbols", out var symbolsEl)
                    && symbolsEl.ValueKind == JsonValueKind.Array)
                {
                    arr = symbolsEl;
                }
                else
                {
                    return result;
                }

                var seen = new HashSet<uint>();
                foreach (JsonElement el in arr.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;

                    string name = null;
                    if (el.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        name = nameEl.GetString();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name[0] == '$') continue;

                    if (!el.TryGetProperty("addr", out var addrEl)) continue;
                    if (!TryReadAddr(addrEl, out uint addr)) continue;
                    if (addr < 0x02000000 || addr == 0) continue;
                    if (seen.Contains(addr)) continue;
                    seen.Add(addr);

                    uint size = 0;
                    if (el.TryGetProperty("size", out var sizeEl)
                        && sizeEl.ValueKind == JsonValueKind.Number
                        && sizeEl.TryGetUInt32(out uint s))
                        size = s;

                    string section = "";
                    if (el.TryGetProperty("section", out var secEl) && secEl.ValueKind == JsonValueKind.String)
                        section = secEl.GetString() ?? "";

                    string objectPath = "";
                    if (el.TryGetProperty("objectPath", out var opEl) && opEl.ValueKind == JsonValueKind.String)
                        objectPath = opEl.GetString() ?? "";

                    result.Add(new DecompSymbol { Name = name, Addr = addr, Size = size, Section = section, ObjectPath = objectPath });
                }
            }
            catch
            {
                return result;
            }
            return result;
        }

        // addr may be a hex string ("0x..."/"$...") or a JSON number.
        static bool TryReadAddr(JsonElement el, out uint addr)
        {
            addr = 0;
            if (el.ValueKind == JsonValueKind.String)
            {
                string s = el.GetString();
                if (string.IsNullOrEmpty(s)) return false;
                addr = U.atoi0x(s.Trim());
                return addr != 0;
            }
            if (el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetUInt32(out uint v)) { addr = v; return true; }
                if (el.TryGetUInt64(out ulong v64)) { addr = unchecked((uint)v64); return true; }
            }
            return false;
        }
    }
}
