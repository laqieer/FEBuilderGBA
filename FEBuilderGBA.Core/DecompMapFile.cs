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
                // (addr,name,section) raw hits, in file order.
                var raw = new List<(uint Addr, string Name, string Section)>();

                string curSection = "";

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
                        uint secAddr = ParseHex(secm.Groups[2].Value);
                        if (secAddr >= 0x02000000)
                            boundaries.Add(secAddr);
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
                            uint secAddr = ParseHex(cont.Groups[1].Value);
                            if (secAddr >= 0x02000000)
                                boundaries.Add(secAddr);
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

                        raw.Add((addr, name, curSection));
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
            List<(uint Addr, string Name, string Section)> raw,
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
                });
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
    /// Tolerant no$gba <c>.sym</c> parser: each line is <c>HEXADDR&lt;space&gt;name</c>
    /// (mirrors <see cref="SymbolUtil"/>'s RegistSymbolByNoDoll split). Addresses
    /// &lt;= 0x100 are skipped. NEVER throws.
    /// </summary>
    public static class DecompSymParser
    {
        public static List<DecompSymbol> Parse(string symText)
        {
            var result = new List<DecompSymbol>();
            try
            {
                if (string.IsNullOrEmpty(symText))
                    return result;

                string[] lines = symText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var seen = new HashSet<uint>();
                foreach (string line in lines)
                {
                    if (line == null) continue;
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    // no$gba line: "100109C MMBDrawInventoryObjs" (split on a single space).
                    string[] sp = trimmed.Split(' ');
                    if (sp.Length != 2) continue;

                    uint addr = U.atoh(sp[0]);
                    string name = sp[1];
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

                    result.Add(new DecompSymbol { Name = name, Addr = addr, Size = size, Section = section });
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
