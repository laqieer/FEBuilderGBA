using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    //正規表現のキャッシュ
    //コンパイルして永続的にキャッシュします.
    public static class RegexCache
    {
        static ConcurrentDictionary<string,Regex> Cache = new ConcurrentDictionary<string,Regex>();
        public static Regex Regex(string pattern)
        {
            return Cache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.Compiled));
        }
        public static string Replace(string str, string pattern, string replace)
        {
            return Regex(pattern).Replace(str, replace);
        }
        public static string Replace(string str, string pattern, MatchEvaluator evaluator)
        {
            return Regex(pattern).Replace(str, evaluator);
        }
        public static bool IsMatch(string str, string pattern)
        {
            return Regex(pattern).IsMatch(str);
        }
        public static Match Match(string str, string pattern)
        {
            return Regex(pattern).Match(str);
        }
        public static MatchCollection Matches(string str, string pattern)
        {
            return Regex(pattern).Matches(str);
        }
        public static string[] Split(string str, string pattern)
        {
            return Regex(pattern).Split(str);
        }
        //最初にマッチしたものをテキストで返す
        public static string MatchSimple(string str, string pattern, string def = "")
        {
            Match m = Match(str, pattern);
            if (m.Groups.Count > 1)
            {
                return m.Groups[1].ToString();
            }
            return def;
        }
    }
}