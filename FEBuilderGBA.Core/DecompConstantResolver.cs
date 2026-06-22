// SPDX-License-Identifier: GPL-3.0-or-later
// Decomp constants-header resolver (#1354): a bidirectional id<->macro map for
// ITEM_* constants, parsed from a decomp project's constants header (typically
// include/constants/items.h). It lets the in-place shop-list source writer
// (DecompSourceWriterCore.RewriteListBody) serialize SYMBOLIC item-id-only lists
// (e.g. { ITEM_SWORD_IRON, ITEM_NONE, }) instead of raw-hex.
//
// READ-ONLY and NEVER throws: header discovery, the (tolerant) C parser, and every
// lookup are fully guarded. A malformed / missing / out-of-universe header simply
// yields an "unavailable" resolver (empty map) — the writer then keeps refusing
// symbolic rewrites rather than fabricating wrong names.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FEBuilderGBA
{
    /// <summary>
    /// Bidirectional id&lt;-&gt;macro map for <c>ITEM_*</c> constants parsed from a decomp
    /// project's constants header (#1354). Built via
    /// <see cref="BuildForProject"/>; consumed by
    /// <see cref="DecompSourceWriterCore.RewriteListBody(string,string,System.Collections.Generic.IReadOnlyList{ushort},DecompConstantResolver,out string)"/>.
    ///
    /// <para>The class is READ-ONLY and NEVER throws. When an EXPLICIT header path was
    /// declared (via the owner's <c>constantsHeader</c> or the manifest top-level
    /// <c>artifacts.itemConstants</c>) but it is absolute / escapes the project root /
    /// missing / unparseable, the resolver is <see cref="IsUnavailable"/> and does NOT
    /// fall back to the conventional default — a wrong-universe header would map list
    /// entries to the wrong names. The default
    /// (<c>include/constants/items.h</c>) is only tried when no explicit path was
    /// declared at all.</para>
    /// </summary>
    public sealed class DecompConstantResolver
    {
        /// <summary>Conventional default constants header, relative to the project root.</summary>
        public const string DefaultHeaderRelative = "include/constants/items.h";

        // macro -> id (only UNAMBIGUOUS macros are kept here).
        readonly Dictionary<string, ushort> _macroToId =
            new Dictionary<string, ushort>(StringComparer.Ordinal);
        // id -> preferred macro (FIRST macro that resolved to that id wins for display).
        readonly Dictionary<ushort, string> _idToMacro = new Dictionary<ushort, string>();
        // Macros known to be ambiguous (non-literal expression, unknown auto-increment
        // base, or a conflicting redefinition) — explicitly NOT in _macroToId so a list
        // entry that uses one is refused rather than silently mis-resolved.
        readonly HashSet<string> _ambiguous = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The resolved absolute header path, or "" when none was loaded.</summary>
        public string HeaderPath { get; private set; } = "";

        /// <summary>
        /// True when an EXPLICIT header path was declared (owner.constantsHeader or
        /// manifest artifacts.itemConstants) — whether or not it resolved.
        /// </summary>
        public bool ExplicitPathDeclared { get; private set; }

        /// <summary>
        /// True when the resolver could not load a usable header. Distinguishes:
        /// an EXPLICIT path that was absolute / escaping / missing / unparseable
        /// (Unavailable; default NOT consulted), OR no header found at all. The
        /// <see cref="Reason"/> string carries the honest cause.
        /// </summary>
        public bool IsUnavailable { get; private set; } = true;

        /// <summary>Honest, human-readable cause for an unavailable / refused resolver.</summary>
        public string Reason { get; private set; } = "no constants header loaded";

        /// <summary>
        /// True when <c>ITEM_NONE</c> resolves to 0 (parsed from the header, or injected
        /// when the header omits it). False ⇒ symbolic rewrites must be refused (the
        /// terminator can't be expressed). The resolved <c>ITEM_NONE</c> macro NAME for
        /// id 0 is <see cref="ItemNoneMacro"/>.
        /// </summary>
        public bool ItemNoneIsZero { get; private set; }

        /// <summary>Preferred macro name for id 0 (e.g. <c>ITEM_NONE</c>), or "ITEM_NONE" when injected.</summary>
        public string ItemNoneMacro { get; private set; } = "ITEM_NONE";

        DecompConstantResolver() { }

        // -------------------------------------------------------------- public lookups

        /// <summary>
        /// Resolve an UNAMBIGUOUS macro (e.g. <c>ITEM_SWORD_IRON</c>) to its id. Returns
        /// false for an unknown macro, an ambiguous macro, or any non-macro token. NEVER throws.
        /// </summary>
        public bool TryResolveMacroToId(string macro, out ushort id)
        {
            id = 0;
            if (string.IsNullOrEmpty(macro))
                return false;
            return _macroToId.TryGetValue(macro, out id);
        }

        /// <summary>
        /// Resolve an id to its preferred (first-declared) macro name. Returns false when
        /// no macro maps to that id. NEVER throws.
        /// </summary>
        public bool TryResolveIdToMacro(ushort id, out string macro)
        {
            return _idToMacro.TryGetValue(id, out macro);
        }

        // -------------------------------------------------------------- discovery + build

        // Per-project cache. Keyed by the project instance (so a fresh project load —
        // a new instance — naturally invalidates) PLUS the resolved header path (so a
        // manifest that changed which header it points at re-parses). A
        // ConditionalWeakTable keeps the entry alive only as long as the project is.
        static readonly ConditionalWeakTable<DecompProject, CacheBox> _cache =
            new ConditionalWeakTable<DecompProject, CacheBox>();

        sealed class CacheBox
        {
            public string Key;          // discovered header path (or a sentinel)
            public DecompConstantResolver Resolver;
        }

        /// <summary>
        /// Discover, parse and return the constants resolver for a project/owner (#1354).
        /// ALWAYS returns a non-null resolver (an unavailable one when no usable header).
        /// NEVER throws. Result is cached per-project (keyed by the resolved header path).
        ///
        /// <para>Header discovery precedence: (a) the owner's project-relative
        /// <c>constantsHeader</c>; (b) the manifest top-level
        /// <c>artifacts.itemConstants</c>; (c) the conventional default
        /// <c>include/constants/items.h</c>. (a) and (b) are EXPLICIT — if declared but
        /// not resolvable (absolute / escapes root / missing / unparseable) the resolver
        /// is <see cref="IsUnavailable"/> and (c) is NOT consulted.</para>
        /// </summary>
        public static DecompConstantResolver BuildForProject(DecompProject project, DecompTableEntry owner)
        {
            try
            {
                if (project == null || string.IsNullOrEmpty(project.ProjectRoot))
                    return Unavailable(false, "no project root for constants-header discovery");

                // ---- Discover the header path + whether it was an EXPLICIT declaration ----
                bool explicitDeclared = false;
                string explicitRaw = null;

                // (a) owner.constantsHeader
                if (owner != null && !string.IsNullOrEmpty(owner.ConstantsHeader))
                {
                    explicitDeclared = true;
                    explicitRaw = owner.ConstantsHeader;
                }
                // (b) manifest artifacts.itemConstants
                else
                {
                    string fromArtifacts = ReadArtifactsString(project, "itemConstants");
                    if (!string.IsNullOrEmpty(fromArtifacts))
                    {
                        explicitDeclared = true;
                        explicitRaw = fromArtifacts;
                    }
                }

                string headerAbs;
                if (explicitDeclared)
                {
                    // An EXPLICIT path that is absolute / escapes root resolves to null ⇒
                    // Unavailable, do NOT fall through to the default.
                    headerAbs = DecompProjectDetector.ResolveArtifact(project.ProjectRoot, explicitRaw);
                    if (string.IsNullOrEmpty(headerAbs) || !File.Exists(headerAbs))
                    {
                        return Unavailable(true,
                            "explicit constants header '" + explicitRaw +
                            "' is absolute/escapes-root/missing — refusing to fall back to the default (wrong-universe danger)");
                    }
                }
                else
                {
                    // (c) conventional default — only when nothing explicit was declared.
                    headerAbs = DecompProjectDetector.ResolveArtifact(project.ProjectRoot, DefaultHeaderRelative);
                    if (string.IsNullOrEmpty(headerAbs) || !File.Exists(headerAbs))
                    {
                        return Unavailable(false,
                            "no constants header (owner.constantsHeader / artifacts.itemConstants / " +
                            DefaultHeaderRelative + " all absent)");
                    }
                }

                // ---- Per-project cache (keyed by the resolved header path) ----
                if (_cache.TryGetValue(project, out CacheBox box)
                    && box != null
                    && string.Equals(box.Key, headerAbs, StringComparison.Ordinal)
                    && box.Resolver != null)
                {
                    return box.Resolver;
                }

                DecompConstantResolver built = BuildFromFile(headerAbs, explicitDeclared);

                // (Re)seat the cache entry for this project.
                try
                {
                    _cache.Remove(project);
                    _cache.Add(project, new CacheBox { Key = headerAbs, Resolver = built });
                }
                catch { /* caching is best-effort */ }

                return built;
            }
            catch
            {
                return Unavailable(false, "unexpected fault during constants-header discovery");
            }
        }

        // Build a resolver from a concrete header file (read + parse). Never throws.
        static DecompConstantResolver BuildFromFile(string headerAbs, bool explicitDeclared)
        {
            var r = new DecompConstantResolver
            {
                ExplicitPathDeclared = explicitDeclared,
                HeaderPath = headerAbs,
            };
            try
            {
                string text;
                try { text = File.ReadAllText(headerAbs); }
                catch
                {
                    r.IsUnavailable = true;
                    r.Reason = "could not read constants header: " + headerAbs;
                    return r;
                }

                r.Parse(text);

                if (r._macroToId.Count == 0 && !r._idToMacro.ContainsKey(0))
                {
                    // Nothing usable parsed at all.
                    r.IsUnavailable = true;
                    r.Reason = "constants header parsed no ITEM_* macros: " + headerAbs;
                    // still resolve the terminator (injection below would have handled id 0,
                    // but if Parse found nothing we mark unavailable yet keep ITEM_NONE).
                }
                else
                {
                    r.IsUnavailable = false;
                    r.Reason = "loaded " + r._macroToId.Count + " macro(s) from " + headerAbs;
                }
            }
            catch
            {
                r.IsUnavailable = true;
                r.Reason = "unexpected fault parsing constants header";
            }
            return r;
        }

        static DecompConstantResolver Unavailable(bool explicitDeclared, string reason)
        {
            return new DecompConstantResolver
            {
                ExplicitPathDeclared = explicitDeclared,
                IsUnavailable = true,
                Reason = reason ?? "constants header unavailable",
                ItemNoneIsZero = false,
            };
        }

        // Read manifest.Artifacts.<key> as a string when present, else null. Mirrors
        // DecompSymbolResolver.ReadArtifactsString.
        static string ReadArtifactsString(DecompProject project, string key)
        {
            try
            {
                if (project?.Manifest?.Artifacts is JsonElement art
                    && art.ValueKind == JsonValueKind.Object
                    && art.TryGetProperty(key, out var v)
                    && v.ValueKind == JsonValueKind.String)
                {
                    return v.GetString();
                }
            }
            catch { }
            return null;
        }

        // -------------------------------------------------------------- parser

        /// <summary>
        /// Tolerant, never-throws parse of a C constants header: enum blocks
        /// (auto-incrementing, with explicit-literal anchors) AND <c>#define IDENT
        /// &lt;intliteral&gt;</c> lines. Then finalize the <c>ITEM_NONE</c> state
        /// (inject id 0 when absent; flag a non-zero ITEM_NONE).
        /// </summary>
        void Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                FinalizeItemNone();
                return;
            }

            ParseEnums(text);
            ParseDefines(text);
            FinalizeItemNone();
        }

        // Assign macro NAME -> id with the collision + redefinition rules:
        //  - first macro for an id wins for id->macro (preferred display);
        //  - both macros still map name->id;
        //  - a macro REDEFINED to a DIFFERENT id becomes ambiguous (name->id removed).
        void Assign(string name, ushort id)
        {
            if (string.IsNullOrEmpty(name))
                return;

            if (_ambiguous.Contains(name))
                return;   // already poisoned — never resurrect

            if (_macroToId.TryGetValue(name, out ushort existing))
            {
                if (existing == id)
                    return;   // identical redefinition — harmless
                // Conflicting redefinition ⇒ ambiguous; drop the name->id mapping.
                _macroToId.Remove(name);
                _ambiguous.Add(name);
                // Leave id->macro entries intact (another name may still own those ids).
                return;
            }

            _macroToId[name] = id;
            if (!_idToMacro.ContainsKey(id))
                _idToMacro[id] = name;   // first macro wins for display
        }

        // Mark a member NAME ambiguous (no name->id mapping).
        void MarkAmbiguous(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;
            _macroToId.Remove(name);
            _ambiguous.Add(name);
        }

        // ---- enum blocks ----
        void ParseEnums(string text)
        {
            int i = 0;
            int n = text.Length;
            while (i < n)
            {
                int kw = IndexOfWord(text, "enum", i);
                if (kw < 0)
                    break;
                // Find the next top-level '{' after the keyword (skipping an optional
                // tag name); the body ends at the matching '}'.
                int brace = text.IndexOf('{', kw + 4);
                if (brace < 0)
                    break;
                int close = MatchBrace(text, brace);
                if (close < 0)
                {
                    i = brace + 1;
                    continue;
                }
                ParseEnumBody(text, brace + 1, close);
                i = close + 1;
            }
        }

        void ParseEnumBody(string text, int start, int end)
        {
            // Split by top-level commas; each member is NAME or NAME = <value>.
            // Running counter for C auto-increment. knownBase tracks whether the counter
            // is currently TRUSTWORTHY (an ambiguous explicit value makes subsequent
            // implicit members ambiguous until the next explicit-literal member).
            long counter = 0;
            bool knownBase = true;

            foreach (var (ms, me) in SplitTopLevelCommas(text, start, end))
            {
                string member = StripLineComments(text.Substring(ms, me - ms)).Trim();
                if (member.Length == 0)
                    continue;

                int eq = member.IndexOf('=');
                if (eq < 0)
                {
                    // Bare NAME — C auto-increment from the running counter.
                    string name = member.Trim();
                    if (!IsIdent(name))
                        continue;
                    if (knownBase && counter >= 0 && counter <= ushort.MaxValue)
                    {
                        Assign(name, (ushort)counter);
                        counter++;
                    }
                    else
                    {
                        // Unknown base ⇒ this implicit member is ambiguous; counter
                        // stays unknown until an explicit-literal member re-anchors it.
                        MarkAmbiguous(name);
                    }
                }
                else
                {
                    string name = member.Substring(0, eq).Trim();
                    string valueExpr = member.Substring(eq + 1).Trim();
                    if (!IsIdent(name))
                        continue;

                    if (TryParseUShortLiteral(valueExpr, out ushort lit))
                    {
                        Assign(name, lit);
                        // Re-anchor the running counter to a KNOWN base.
                        counter = (long)lit + 1;
                        knownBase = true;
                    }
                    else
                    {
                        // Non-literal expression (A | B, FOO+1, > 0xFFFF, …) ⇒ ambiguous.
                        // The counter is now UNKNOWN — subsequent implicit members stay
                        // ambiguous until the next explicit literal.
                        MarkAmbiguous(name);
                        knownBase = false;
                    }
                }
            }
        }

        // ---- #define lines ----
        void ParseDefines(string text)
        {
            int i = 0;
            int n = text.Length;
            while (i < n)
            {
                int hash = text.IndexOf("#define", i, StringComparison.Ordinal);
                if (hash < 0)
                    break;
                // Must start the line (allow leading whitespace).
                int lineStart = text.LastIndexOf('\n', hash) + 1;
                bool onlyWs = true;
                for (int k = lineStart; k < hash; k++)
                {
                    if (!char.IsWhiteSpace(text[k])) { onlyWs = false; break; }
                }
                int lineEnd = text.IndexOf('\n', hash);
                if (lineEnd < 0) lineEnd = n;
                if (onlyWs)
                {
                    string line = StripLineComments(text.Substring(hash, lineEnd - hash));
                    // #define IDENT VALUE
                    string body = line.Substring("#define".Length).Trim();
                    int sp = IndexOfWhitespace(body);
                    if (sp > 0)
                    {
                        string name = body.Substring(0, sp).Trim();
                        string val = body.Substring(sp).Trim();
                        // A function-like macro (IDENT(...)) is not a constant.
                        if (IsIdent(name) && TryParseUShortLiteral(val, out ushort lit))
                            Assign(name, lit);
                    }
                }
                i = lineEnd + 1 > i ? lineEnd + 1 : i + 1;
            }
        }

        // Finalize ITEM_NONE: it must resolve to 0 for symbolic rewrites. Inject id 0 ->
        // ITEM_NONE when no macro maps to 0; flag a non-zero ITEM_NONE as unusable.
        void FinalizeItemNone()
        {
            // Did some macro claim id 0?
            if (_idToMacro.TryGetValue(0, out string zeroName))
            {
                ItemNoneMacro = zeroName;
                ItemNoneIsZero = true;
            }
            else
            {
                // No macro maps to 0. If a header DECLARED an ITEM_NONE at a non-zero id
                // (or marked it ambiguous), the terminator can't be expressed.
                bool itemNoneDeclaredNonZero =
                    _macroToId.ContainsKey("ITEM_NONE") || _ambiguous.Contains("ITEM_NONE");
                if (itemNoneDeclaredNonZero)
                {
                    ItemNoneIsZero = false;
                    ItemNoneMacro = "ITEM_NONE";
                }
                else
                {
                    // Inject ITEM_NONE = 0 (terminator must resolve).
                    _macroToId["ITEM_NONE"] = 0;
                    _idToMacro[0] = "ITEM_NONE";
                    ItemNoneMacro = "ITEM_NONE";
                    ItemNoneIsZero = true;
                }
            }
        }

        // -------------------------------------------------------------- low-level scanners

        // Parse a plain ushort literal (dec / 0xHEX, optional u/U/l/L suffix). Reject
        // anything else (expression, identifier, out-of-range > 0xFFFF, signed). Reuses
        // the SAME literal grammar as DecompSourceWriterCore.TryParseIntLiteral, then
        // gates to the ushort range.
        static bool TryParseUShortLiteral(string s, out ushort value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s))
                return false;

            s = s.Trim();
            int i = 0;
            int n = s.Length;
            if (n == 0)
                return false;
            if (s[0] == '-' || s[0] == '+')
                return false;

            bool isHex = false;
            int digitsStart;
            if (n >= 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
            {
                isHex = true;
                i = 2;
                digitsStart = i;
                while (i < n && Uri.IsHexDigit(s[i])) i++;
                if (i == digitsStart) return false;   // "0x" with no digits
            }
            else
            {
                digitsStart = i;
                while (i < n && s[i] >= '0' && s[i] <= '9') i++;
                if (i == digitsStart) return false;
            }
            int digitsEnd = i;

            // Remaining must be only u/U/l/L suffix chars.
            while (i < n)
            {
                char c = s[i];
                if (c == 'u' || c == 'U' || c == 'l' || c == 'L') { i++; continue; }
                return false;   // trailing non-suffix char ⇒ not a plain literal
            }

            string digits = s.Substring(digitsStart, digitsEnd - digitsStart);
            uint u;
            bool parsed = isHex
                ? uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out u)
                : uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out u);
            if (!parsed)
                return false;
            if (u > ushort.MaxValue)
                return false;   // out of ushort range ⇒ skipped
            value = (ushort)u;
            return true;
        }

        // Find a whole-word occurrence of `word` (keyword scan; not comment/string aware,
        // which is fine for `enum` — a stray `enum` inside a comment is harmless because
        // its body still gets parsed by member rules, and ITEM_* macros only appear in
        // real enums in practice). NEVER throws.
        static int IndexOfWord(string text, string word, int from)
        {
            int i = from;
            while (true)
            {
                int idx = text.IndexOf(word, i, StringComparison.Ordinal);
                if (idx < 0) return -1;
                bool leftOk = idx == 0 || !IsIdentChar(text[idx - 1]);
                int after = idx + word.Length;
                bool rightOk = after >= text.Length || !IsIdentChar(text[after]);
                if (leftOk && rightOk)
                    return idx;
                i = idx + word.Length;
                if (i >= text.Length) return -1;
            }
        }

        // Match the brace at `open` ('{') to its closing '}', comment/string aware. -1
        // when unbalanced. NEVER throws.
        static int MatchBrace(string text, int open)
        {
            int depth = 0;
            int i = open;
            int n = text.Length;
            while (i < n)
            {
                int skip = SkipTriviaAndLiterals(text, i, n);
                if (skip != i) { i = skip; continue; }
                char c = text[i];
                if (c == '{') { depth++; i++; continue; }
                if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                    i++;
                    continue;
                }
                i++;
            }
            return -1;
        }

        // Split [start, end) by top-level commas (comment/string/nesting aware). Each
        // span is [s, e) of the raw member (untrimmed). NEVER throws.
        static List<(int start, int end)> SplitTopLevelCommas(string text, int start, int end)
        {
            var spans = new List<(int, int)>();
            int depth = 0;
            int tokenStart = start;
            int i = start;
            while (i < end)
            {
                int skip = SkipTriviaAndLiterals(text, i, end);
                if (skip != i) { i = skip; continue; }
                char c = text[i];
                if (c == '{' || c == '[' || c == '(') { depth++; i++; continue; }
                if (c == '}' || c == ']' || c == ')') { depth--; i++; continue; }
                if (depth == 0 && c == ',')
                {
                    spans.Add((tokenStart, i));
                    tokenStart = i + 1;
                    i++;
                    continue;
                }
                i++;
            }
            // Final member (only if non-whitespace remains).
            int ss = tokenStart;
            while (ss < end && char.IsWhiteSpace(text[ss])) ss++;
            if (ss < end)
                spans.Add((tokenStart, end));
            return spans;
        }

        // If `i` begins whitespace, a // or /* */ comment, or a string/char literal,
        // return the index just past it; else return `i`. Bounded by `limit`. NEVER throws.
        static int SkipTriviaAndLiterals(string text, int i, int limit)
        {
            int n = Math.Min(limit, text.Length);
            if (i >= n) return i;
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                int j = i;
                while (j < n && char.IsWhiteSpace(text[j])) j++;
                return j;
            }
            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                int j = i + 2;
                while (j < n && text[j] != '\n') j++;
                return j;
            }
            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                int j = i + 2;
                while (j + 1 < n && !(text[j] == '*' && text[j + 1] == '/')) j++;
                return Math.Min(n, j + 2);
            }
            if (c == '"')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (text[j] == '\\') { j += 2; continue; }
                    if (text[j] == '"') { j++; break; }
                    j++;
                }
                return j;
            }
            if (c == '\'')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (text[j] == '\\') { j += 2; continue; }
                    if (text[j] == '\'') { j++; break; }
                    j++;
                }
                return j;
            }
            return i;
        }

        // Strip C // line and /* */ block comments from a short token span. NEVER throws.
        static string StripLineComments(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('/') < 0)
                return s ?? "";
            var sb = new System.Text.StringBuilder(s.Length);
            int i = 0, n = s.Length;
            while (i < n)
            {
                if (s[i] == '/' && i + 1 < n && s[i + 1] == '/')
                {
                    int j = i;
                    while (j < n && s[j] != '\n') j++;
                    i = j;
                    continue;
                }
                if (s[i] == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    int j = i + 2;
                    while (j + 1 < n && !(s[j] == '*' && s[j + 1] == '/')) j++;
                    i = Math.Min(n, j + 2);
                    continue;
                }
                sb.Append(s[i]);
                i++;
            }
            return sb.ToString();
        }

        static int IndexOfWhitespace(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsWhiteSpace(s[i])) return i;
            return -1;
        }

        static bool IsIdent(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
            for (int i = 1; i < s.Length; i++)
                if (!IsIdentChar(s[i])) return false;
            return true;
        }

        static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
