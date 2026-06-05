using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform port of the two safe, high-value pieces of the WinForms
    /// <c>TranslateTextUtil</c> translate path (#967, follow-up to #949):
    /// <list type="number">
    ///   <item><b>Control-code protection</b> — split the text into alternating
    ///     literal-text and <c>@XXXX</c>-escape segments (bundling a
    ///     <c>@0003</c> immediately followed by a <c>\r\n</c>), translate ONLY
    ///     the literal segments, and re-insert the codes verbatim so escape
    ///     sequences survive translation. Port of
    ///     <c>TranslateTextUtil.SplitEscapeString</c>.</item>
    ///   <item><b>Dictionary-first lookup</b> — load the shipped fixed glossary
    ///     <c>config/translate/dic_&lt;from&gt;_&lt;to&gt;.txt</c> (tab-separated
    ///     <c>source\ttarget</c>, case-insensitive) and, if a whole literal
    ///     segment matches, use the glossary value instead of calling the
    ///     online translator. Port of
    ///     <c>TranslateTextUtil.AppendFixedDic</c> + <c>TranslateTextDic</c>.</item>
    /// </list>
    ///
    /// The orchestrator <see cref="TranslateText"/> takes an injectable
    /// <c>translator</c> delegate so unit tests run fully offline (the real
    /// Avalonia call defaults to <c>new TranslateManage().Trans</c>).
    ///
    /// DEFERRED (documented, out of scope — see #967):
    /// <list type="bullet">
    ///   <item><c>InsertSerifnl</c> line-breaking — needs WinForms
    ///     <c>FontForm.MeasureTextWidth</c> (System.Drawing font metrics);
    ///     not cleanly portable.</item>
    ///   <item><c>FE8SkipFace48</c> — game-specific JA↔EN face-index remap.</item>
    ///   <item>The full WF <c>LoadTranslateDic</c> ROM-pair text-id glossary —
    ///     only the static file-based <c>dic_*.txt</c> is ported here.</item>
    /// </list>
    /// </summary>
    public static class TranslateTextUtilCore
    {
        // ----------------------------------------------------------------
        // Control-code protection (split / classify)
        // ----------------------------------------------------------------

        /// <summary>
        /// Split <paramref name="text"/> into alternating literal-text and
        /// <c>@XXXX</c>-escape-code segments. A <c>@0003</c> immediately followed
        /// by a <c>\r\n</c> is bundled into ONE segment (mirroring WF, which keeps
        /// the line-break that follows a paragraph code attached to it).
        ///
        /// CORRECTED port of WF <c>SplitEscapeString</c>: the WinForms version
        /// drops any literal text that trails the LAST escape code (e.g.
        /// <c>abc@0001def</c> loses <c>def</c>). This version flushes that
        /// trailing literal so the segments always round-trip exactly:
        /// <c>string.Concat(SplitEscapeSegments(x)) == x</c> for ALL <paramref name="text"/>.
        ///
        /// A <c>@</c> is only treated as the start of an escape code when it is
        /// followed by EXACTLY four hex digits (the same <c>@</c>+4-hex rule
        /// <see cref="IsEscapeSegment"/> enforces — see <see cref="IsCodeAt"/>).
        /// A literal at-sign in ordinary text — <c>email@example.com</c>,
        /// <c>hello@catworld</c>, a trailing <c>@</c>, or <c>@</c> followed by
        /// fewer than four hex / non-hex chars — stays attached to its
        /// surrounding literal run instead of being fragmented (#971).
        /// </summary>
        public static List<string> SplitEscapeSegments(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return list;
            }

            int textstart = 0;
            int i = 0;
            while (i < text.Length)
            {
                // Only a real "@XXXX" control code (4 hex digits) is an escape
                // boundary; a literal '@' falls through and stays in the literal
                // run (#971).
                if (text[i] == '@' && IsCodeAt(text, i))
                {
                    // Flush the pending literal run before this code.
                    if (i - textstart > 0)
                    {
                        list.Add(U.substr(text, textstart, i - textstart));
                    }

                    // Read the 5-char "@XXXX" code (4 hex digits after '@').
                    string code = U.substr(text, i, 5);
                    uint codeint = U.atoh(code.Substring(1));
                    if (codeint == 3 && U.substr(text, i + 5, 2) == "\r\n")
                    {
                        // @0003 is a paragraph break; the WF text keeps the
                        // following CRLF bundled with it so it never gets
                        // translated as part of a literal segment.
                        code = U.substr(text, i, 5 + 2);
                        list.Add(code);
                        i += 5 + 2;
                    }
                    else
                    {
                        list.Add(code);
                        i += 5;
                    }

                    textstart = i;
                }
                else
                {
                    i++;
                }
            }

            // Flush the trailing literal (WF parity FIX — never drop tail text).
            if (textstart < text.Length)
            {
                list.Add(U.substr(text, textstart, text.Length - textstart));
            }

            return list;
        }

        /// <summary>
        /// True when an escape code begins AT position <paramref name="i"/> in
        /// <paramref name="text"/>: a <c>@</c> immediately followed by exactly
        /// four hex digits (so the full 5-char <c>@XXXX</c> token fits). This is
        /// the position-based twin of <see cref="IsEscapeSegment"/> — both enforce
        /// the identical <c>@</c>+4-hex rule so the splitter and the classifier
        /// never disagree about what counts as a control code (#971).
        /// </summary>
        static bool IsCodeAt(string text, int i)
        {
            // Need '@' plus 4 trailing hex digits → indices i+1..i+4 must exist.
            if (i + 4 >= text.Length || text[i] != '@')
            {
                return false;
            }
            for (int k = 1; k <= 4; k++)
            {
                if (!U.ishex(text[i + k]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// True when <paramref name="segment"/> is an escape-code segment, i.e.
        /// it begins with <c>@</c> followed by exactly four hex digits (the real
        /// <c>@XXXX</c> form). A bundled <c>@0003\r\n</c> still qualifies because
        /// it starts with <c>@0003</c>. A bare <c>@</c> or <c>@</c> followed by
        /// non-hex text is treated as a literal (so it still gets translated /
        /// glossary-checked), matching the intent that only real control codes
        /// are protected.
        /// </summary>
        public static bool IsEscapeSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment) || segment[0] != '@' || segment.Length < 5)
            {
                return false;
            }
            for (int i = 1; i <= 4; i++)
            {
                if (!U.ishex(segment[i]))
                {
                    return false;
                }
            }
            return true;
        }

        // ----------------------------------------------------------------
        // Fixed glossary load (config/translate/dic_<from>_<to>.txt)
        // ----------------------------------------------------------------

        static readonly object _dicLock = new object();
        static string _dicKey;
        static Dictionary<string, string> _dicCache;

        /// <summary>Drop the cached glossary (call on language change / base-dir change).</summary>
        public static void ClearCache()
        {
            lock (_dicLock)
            {
                _dicKey = null;
                _dicCache = null;
            }
        }

        /// <summary>
        /// Load the fixed translation glossary for the
        /// <paramref name="from"/>→<paramref name="to"/> language pair from
        /// <c>config/translate/dic_&lt;from&gt;_&lt;to&gt;.txt</c> (tab-separated
        /// <c>source\ttarget</c>). The <c>ja</c>↔<c>en</c> pair shares the single
        /// shipped <c>dic_ja_en.txt</c> (reversed for <c>en</c>→<c>ja</c>),
        /// mirroring WF <c>AppendFixedDic</c>. Keys are upper-cased for the
        /// case-insensitive lookup; <c>\r\n</c> escapes in the file are
        /// un-escaped; the first occurrence of a duplicate key wins.
        ///
        /// Missing file, unsupported pair, or a null
        /// <see cref="CoreState.BaseDirectory"/> all return an EMPTY dictionary
        /// and NEVER throw. Following the W2a <c>SongNameResolverCore</c> lesson,
        /// only a STABLE result is cached: a glossary file that was actually read,
        /// OR an unsupported/same-language pair (which has no file to load now or
        /// ever). A resolvable-but-MISSING file and a null/empty BaseDirectory are
        /// deliberately NOT cached, so a later call re-reads the glossary if the
        /// file appears (or BaseDirectory is set) instead of being permanently
        /// poisoned with an empty dict. The cache is keyed by
        /// <c>(BaseDirectory, from, to)</c> and guarded by a lock for the
        /// multi-window / off-UI-thread case.
        /// </summary>
        public static Dictionary<string, string> LoadFixedDic(string from, string to)
        {
            string baseDir = CoreState.BaseDirectory;
            string cacheKey = (baseDir ?? "") + " " + (from ?? "") + " " + (to ?? "");

            lock (_dicLock)
            {
                if (_dicCache != null && _dicKey == cacheKey)
                {
                    return _dicCache;
                }
            }

            var dic = new Dictionary<string, string>();
            // Cacheable only on a genuinely-stable result:
            //   * a glossary file that actually EXISTED and was read (a real load), or
            //   * an UNSUPPORTED / same-language pair (no file path → there is nothing
            //     to load now or later, so an empty dict is the permanent answer).
            // A resolvable-but-MISSING file (the pair is supported but the file is not
            // present yet) is deliberately NOT cached — so if the file later appears
            // (e.g. after a config update with BaseDirectory unchanged) a subsequent
            // call re-reads it instead of being stuck with an empty dict. Likewise a
            // null/empty BaseDirectory is never cached. (W2a SongNameResolverCore lesson.)
            bool cacheable = false;
            try
            {
                if (!string.IsNullOrEmpty(baseDir))
                {
                    bool isRev;
                    string fullfilename = ResolveDicFile(baseDir, from, to, out isRev);
                    if (fullfilename == null)
                    {
                        // Unsupported / same-language pair — nothing to load, ever.
                        cacheable = true;
                    }
                    else if (File.Exists(fullfilename))
                    {
                        foreach (string line in File.ReadAllLines(fullfilename))
                        {
                            string[] sp = line.Split('\t');
                            if (sp.Length < 2)
                            {
                                continue;
                            }

                            string key = isRev ? sp[1] : sp[0];
                            string value = isRev ? sp[0] : sp[1];
                            key = key.Replace("\\r\\n", "\r\n").ToUpper();
                            value = value.Replace("\\r\\n", "\r\n");

                            if (dic.ContainsKey(key))
                            {
                                continue; // first-wins on duplicate keys (WF parity)
                            }
                            dic[key] = value;
                        }
                        cacheable = true; // a real, successful file read
                    }
                    // else: file path resolved but the file is absent → NOT cacheable.
                }
            }
            catch (Exception ex)
            {
                // Do NOT poison-cache on failure — leave the cache untouched so a
                // later call retries instead of being stuck with an empty dict.
                Log.Error("TranslateTextUtilCore.LoadFixedDic failed: " + ex.Message);
                return dic;
            }

            if (cacheable)
            {
                lock (_dicLock)
                {
                    _dicCache = dic;
                    _dicKey = cacheKey;
                }
            }
            return dic;
        }

        /// <summary>
        /// Resolve the glossary file path for a language pair. The single shipped
        /// <c>dic_ja_en.txt</c> serves both <c>ja</c>→<c>en</c> (forward) and
        /// <c>en</c>→<c>ja</c> (<paramref name="isRev"/>=true). Other pairs map
        /// directly to <c>dic_&lt;from&gt;_&lt;to&gt;.txt</c> if present.
        /// Returns null for an unsupported / same-language pair.
        /// </summary>
        static string ResolveDicFile(string baseDir, string from, string to, out bool isRev)
        {
            isRev = false;
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to)
            {
                return null;
            }

            string transDir = Path.Combine(baseDir, "config", "translate");
            if (from == "ja" && to == "en")
            {
                return Path.Combine(transDir, "dic_ja_en.txt");
            }
            if (from == "en" && to == "ja")
            {
                isRev = true;
                return Path.Combine(transDir, "dic_ja_en.txt");
            }
            // Generic forward file (e.g. dic_ja_zh.txt) when shipped.
            return Path.Combine(transDir, "dic_" + from + "_" + to + ".txt");
        }

        // ----------------------------------------------------------------
        // Orchestrator
        // ----------------------------------------------------------------

        /// <summary>
        /// Translate <paramref name="text"/> from <paramref name="from"/> to
        /// <paramref name="to"/> while PROTECTING control codes and preferring the
        /// fixed glossary. The text is split via <see cref="SplitEscapeSegments"/>;
        /// each segment is handled in order:
        /// <list type="bullet">
        ///   <item>an escape-code segment (<see cref="IsEscapeSegment"/>) is kept
        ///     verbatim and is NEVER passed to <paramref name="translator"/>;</item>
        ///   <item>a literal segment that matches the glossary
        ///     (case-insensitive on the trimmed value) uses the glossary value;</item>
        ///   <item>otherwise, when <paramref name="useGoogle"/> is set, the literal
        ///     segment is translated via <paramref name="translator"/> (defaulting
        ///     to <c>new TranslateManage().Trans</c>); when not set it is left
        ///     unchanged.</item>
        /// </list>
        /// Segments are reassembled in order. Empty <paramref name="text"/> or
        /// <c>from == to</c> returns <paramref name="text"/> unchanged.
        /// </summary>
        /// <param name="dic">Glossary from <see cref="LoadFixedDic"/> (may be null/empty).</param>
        /// <param name="translator">
        /// Injectable <c>(text, from, to) =&gt; translated</c> delegate; defaults to
        /// the online <c>TranslateManage.Trans</c>. Lets tests run offline.
        /// </param>
        public static string TranslateText(
            string text, string from, string to,
            Dictionary<string, string> dic, bool useGoogle,
            Func<string, string, string, string> translator = null)
        {
            if (string.IsNullOrEmpty(text) || from == to)
            {
                return text;
            }
            if (translator == null)
            {
                translator = (s, f, t) => new TranslateManage().Trans(s, f, t);
            }

            List<string> segments = SplitEscapeSegments(text);
            for (int i = 0; i < segments.Count; i++)
            {
                string seg = segments[i];
                if (seg.Length == 0 || IsEscapeSegment(seg))
                {
                    continue; // control codes survive verbatim
                }

                // Dictionary-first: try the whole segment (and its trimmed form)
                // case-insensitively before spending a network call.
                string hit = LookupDic(seg, dic);
                if (hit != null)
                {
                    segments[i] = hit;
                    continue;
                }

                if (useGoogle)
                {
                    segments[i] = translator(seg, from, to);
                }
                // else: leave the literal segment untranslated.
            }

            return string.Concat(segments);
        }

        /// <summary>
        /// Case-insensitive glossary lookup for a literal segment. Tries the
        /// upper-cased segment, then its trimmed form (mirroring WF, which retries
        /// with <c>text.Trim()</c>). Returns null when there is no glossary hit.
        /// </summary>
        static string LookupDic(string seg, Dictionary<string, string> dic)
        {
            if (dic == null || dic.Count == 0)
            {
                return null;
            }
            string upper = seg.ToUpper();
            if (dic.TryGetValue(upper, out string v))
            {
                return v;
            }
            string trimmed = seg.Trim().ToUpper();
            if (trimmed != upper && dic.TryGetValue(trimmed, out v))
            {
                return v;
            }
            return null;
        }
    }
}
