using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;

namespace FEBuilderGBA
{
    //C#のリソースはなんだかいけていないので、
    //自前リソースを作る.
    //
    //:文字列
    //翻訳
    //
    //:文字列
    //翻訳
    //
    //
    public class MyTranslateResourceLow
    {
        Dictionary<string,string> Dic = new Dictionary<string,string>();
        Dictionary<string,string> ReverseEnglishMap = new Dictionary<string,string>();

        // Decode the XML character/entity references that an AXAML parser resolves
        // before a string ever reaches str(). Older en.txt extractions retained raw
        // entities in their keys (e.g. `Support &amp; Other`, `...&#10;...`,
        // `&lt;= 4095`) while ja.txt/zh.txt use the decoded form, so without this the
        // entity-keyed entries miss every lookup and fall through to English
        // identity-passthrough (issue #1636 — a residual of #356). Applied to keys
        // AND values at load time and as a final str() fallback so every locale file
        // resolves uniformly regardless of which form it ships with, and so a future
        // re-extraction cannot silently re-introduce the regression.
        //
        // `&amp;` MUST be decoded LAST so a doubly-escaped `&amp;lt;` decodes to the
        // literal `&lt;` (not `<`). Numeric refs are converted before named refs for
        // the same reason. Idempotent on entity-free strings.
        public static string DecodeXmlEntities(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0)
            {
                return s;
            }

            // Numeric character references: &#NN; (decimal) and &#xNN; (hex, any case).
            // We only resolve the small set that actually appears in the translation
            // files and that the AXAML parser would emit: a LINE BREAK (10/0x0a).
            //
            // The whole translate system standardises newlines on CRLF (the `\r\n`
            // escape that LoadResource decodes to "\r\n"). ja.txt/zh.txt — and the
            // normalized en.txt — store line breaks as that CRLF form, so a lone
            // LF entity (`&#10;`) MUST decode to "\r\n" here, otherwise the reverse
            // map's Japanese key (LF) would never match the target Dic key (CRLF).
            // A bare CR entity is decoded to "\r" (none currently ship, but kept for
            // completeness). str()'s #356 LF↔CRLF fallback covers the runtime input.
            s = s.Replace("&#10;", "\r\n");
            s = s.Replace("&#x0a;", "\r\n").Replace("&#x0A;", "\r\n");
            s = s.Replace("&#xa;", "\r\n").Replace("&#xA;", "\r\n");
            s = s.Replace("&#13;", "\r");
            s = s.Replace("&#x0d;", "\r").Replace("&#x0D;", "\r");
            s = s.Replace("&#xd;", "\r").Replace("&#xD;", "\r");

            // Named entities. `&amp;` decoded LAST.
            s = s.Replace("&lt;", "<");
            s = s.Replace("&gt;", ">");
            s = s.Replace("&quot;", "\"");
            s = s.Replace("&apos;", "'");
            s = s.Replace("&amp;", "&");
            return s;
        }

        //翻訳文字列の取得
        public string str(string src)
        {
            string dest;
            string japaneseKey;

            // 1. Direct lookup (Japanese keys — WinForms backward compat)
            if (Dic.TryGetValue(src, out dest))
            {
                return dest;
            }
            // 2. Reverse chain: English key → Japanese key → target translation
            if (ReverseEnglishMap.TryGetValue(src, out japaneseKey))
            {
                if (Dic.TryGetValue(japaneseKey, out dest))
                    return dest;
                // For Japanese mode (Dic cleared): return the Japanese key itself
                return japaneseKey;
            }
            // 2b. Retry with TrimEnd — en.txt values may have trailing whitespace
            //     that Avalonia keys won't have.
            string trimmed = src.TrimEnd();
            if (trimmed != src && ReverseEnglishMap.TryGetValue(trimmed, out japaneseKey))
            {
                if (Dic.TryGetValue(japaneseKey, out dest))
                    return dest;
                return japaneseKey;
            }

            // 2c. Newline normalisation (issue #356): Avalonia AXAML literals
            //     decoded from `&#x0a;` XML entities use bare LF, while the
            //     translation files use literal `\r\n` (which the parser
            //     decodes to CRLF). Without normalisation, the LF runtime
            //     key would never match the CRLF stored key. Try both LF→CRLF
            //     and CRLF→LF conversions before giving up.
            if (src.IndexOf('\n') >= 0 || src.IndexOf('\r') >= 0)
            {
                // Normalise to CRLF (LF → CRLF) for direct + reverse lookup
                string crlfForm = src.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                if (crlfForm != src)
                {
                    if (Dic.TryGetValue(crlfForm, out dest)) return dest;
                    if (ReverseEnglishMap.TryGetValue(crlfForm, out japaneseKey))
                    {
                        if (Dic.TryGetValue(japaneseKey, out dest)) return dest;
                        return japaneseKey;
                    }
                }
                // Normalise to LF (CRLF → LF) for direct + reverse lookup
                string lfForm = src.Replace("\r\n", "\n").Replace("\r", "\n");
                if (lfForm != src)
                {
                    if (Dic.TryGetValue(lfForm, out dest)) return dest;
                    if (ReverseEnglishMap.TryGetValue(lfForm, out japaneseKey))
                    {
                        if (Dic.TryGetValue(japaneseKey, out dest)) return dest;
                        return japaneseKey;
                    }
                }
            }

            // 2d. XML-entity normalisation (issue #1636): a caller may pass a string
            //     that still carries raw entities (`&#10;`, `&#x0a;`, `&amp;`, `&lt;`)
            //     even though the dictionaries are now keyed by the decoded form.
            //     Decode and retry the full direct + reverse chain (with newline
            //     normalisation) before giving up.
            if (src.IndexOf('&') >= 0)
            {
                string decoded = DecodeXmlEntities(src);
                if (decoded != src)
                {
                    if (Dic.TryGetValue(decoded, out dest)) return dest;
                    if (ReverseEnglishMap.TryGetValue(decoded, out japaneseKey))
                    {
                        if (Dic.TryGetValue(japaneseKey, out dest)) return dest;
                        return japaneseKey;
                    }
                    // The decoded form may itself differ from the stored key only in
                    // newline form (LF vs CRLF) — reuse str()'s normalisation chain.
                    string viaNewline = str(decoded);
                    if (viaNewline != decoded) return viaNewline;
                }
            }

            // 3. Pass-through
            return src;
        }
        /// <summary>
        /// Clear all translation entries so str() returns keys as-is (built-in Japanese).
        /// Keep ReverseEnglishMap — Japanese mode needs it to map English→Japanese.
        /// </summary>
        public void Clear()
        {
            Dic = new Dictionary<string,string>();
        }

        /// <summary>
        /// Load a reverse English→Japanese lookup map from an English translation file.
        /// This maps English translation values back to their Japanese keys,
        /// enabling Avalonia (which uses English keys in R._()) to find translations
        /// in non-English target language files.
        /// </summary>
        public void LoadReverseEnglishMap(string enFilePath)
        {
            ReverseEnglishMap = new Dictionary<string,string>();
            if (!File.Exists(enFilePath)) return;

            using (StreamReader reader = File.OpenText(enFilePath))
            {
                string src = null;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length <= 0) { src = null; continue; }
                    if (src == null)
                    {
                        if (line[0] != ':') continue;
                        src = line.Substring(1).Replace("\\r\\n", "\r\n");
                        src = DecodeXmlEntities(src);
                    }
                    else
                    {
                        string englishValue = DecodeXmlEntities(line.Replace("\\r\\n", "\r\n")).TrimEnd();
                        // Map English value → Japanese key (for reverse lookup)
                        if (!string.IsNullOrEmpty(englishValue))
                            ReverseEnglishMap[englishValue] = src;
                        src = null; // after reading translation line, reset src for next pair
                    }
                }
            }
        }

        //翻訳があるかどうか取得 開発用
        public bool Exist(string src)
        {
            return Dic.ContainsKey(src);
        }
        //翻訳の変更.開発用
        public void replaceTranslateString(string f,string t)
        {
            Dic[f] = t;
        }
        public void LoadResource(string fullfilename)
        {
            Dic = new Dictionary<string,string>();

            if (!File.Exists(fullfilename))
            {//リソースがない.
                if (fullfilename.IndexOf("ja.txt") > 0 )
                {
                    return;
                }
                CoreState.Services.ShowError(string.Format(
                    "Translation resource file {0} could not be found.\r\nWe recommend re-downloading the file.\r\n翻訳リソースファイル {0}が見つかりませんでした。\r\n再ダウンロードすることを推奨します。", fullfilename));
                return;
            }

            using (StreamReader reader = File.OpenText(fullfilename))
            {
                string src = null;

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length <= 0)
                    {
                        src = null;
                        continue;
                    }
                    if (src == null)
                    {
                        if (line[0] != ':' )
                        {
                            continue;
                        }
                        src = line.Substring(1);
                        src = src.Replace("\\r\\n", "\r\n");
                        src = DecodeXmlEntities(src);
                    }
                    else
                    {
                        line = line.Replace("\\r\\n", "\r\n");
                        line = DecodeXmlEntities(line);
                        Dic[src] = line;
                    }
                }
            }
        }

        public void WriteResource(string fullfilename)
        {
            List<string> lines = new List<string>();
            foreach (var pair in Dic)
            {
                string f = pair.Key;
                string f2 = f.Replace("\r\n","\\r\\n");
                string t = pair.Value;
                string t2 = t.Replace("\r\n", "\\r\\n");

                string line = ":" + f2;
                lines.Add(line);

                line = t2;
                lines.Add(line);

                //空改行
                lines.Add("");
            }

            File.WriteAllLines(fullfilename, lines.ToArray());
        }


        // DEBUG test methods (TESTFULL_RESOURCE, TESTSUB_OpenDialogResource) that
        // use WinForms OpenFileDialog remain in the WinForms project.

        public Dictionary<string, string> ConvertOnelineSplitWord()
        {
            Dictionary<string, string> dic = new Dictionary<string, string>(Dic);
            foreach (var pair in Dic)
            {
                string[] lines = pair.Key.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                string[] enlines = pair.Value.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length != pair.Key.Length || pair.Value.Length <= 1)
                {
                    continue;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    dic[lines[i]] = enlines[i];
                }
            }
            return dic;
        }
    }
}
