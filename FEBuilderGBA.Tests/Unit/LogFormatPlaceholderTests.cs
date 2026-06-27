using System.Text;
using System.Text.RegularExpressions;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Regression guard for GitHub issue #1637.
    ///
    /// <para>The plain <c>Log.Error/Notify/Debug(params string[])</c> sink only does
    /// <c>string.Join(" ", args)</c> — it has NO <c>string.Format</c>. So a call like
    /// <c>Log.Error("... failed: {0}", ex.Message)</c> recorded the LITERAL <c>{0}</c>
    /// token in the log instead of substituting it. The fix added real
    /// <c>Log.ErrorF/NotifyF/DebugF(string fmt, params object[])</c> overloads that do an
    /// actual <c>string.Format</c>, and every <c>{N}</c>-bearing call site was migrated to them.</para>
    ///
    /// <para>This test scans the four production source projects (Core, Avalonia, CLI, WinForms)
    /// and FAILS the build if:</para>
    /// <list type="number">
    /// <item>A NON-<c>F</c> <c>Log.Error/Notify/Debug(...)</c> invocation contains a <c>{N}</c>
    /// indexed placeholder in a string-literal argument (the original bug — would be silently
    /// joined, not substituted), OR</item>
    /// <item>A <c>Log.ErrorF/NotifyF/DebugF(...)</c> invocation's literal format string references
    /// a placeholder index that exceeds the number of arguments supplied (a malformed format that
    /// would land in <c>FormatSafe</c>'s fallback path).</item>
    /// </list>
    ///
    /// <para>The scanner strips line/block comments and walks balanced parentheses so it catches
    /// multi-line invocations and never trips on commented-out examples or string content outside
    /// the call.</para>
    /// </summary>
    public class LogFormatPlaceholderTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private static readonly string[] ScannedProjects =
        {
            "FEBuilderGBA.Core",
            "FEBuilderGBA.Avalonia",
            "FEBuilderGBA.CLI",
            "FEBuilderGBA",
        };

        // {0}, {1,5}, {2:X4}, {3,-10:N2} — any indexed composite-format placeholder.
        private static readonly Regex PlaceholderRx =
            new(@"\{(\d+)(?:[,:][^{}]*)?\}", RegexOptions.Compiled);

        // A Log.Error / Log.Notify / Log.Debug (+ optional trailing F) method call start.
        private static readonly Regex LogCallStartRx =
            new(@"\bLog\.(Error|Notify|Debug)(F?)\s*\(", RegexOptions.Compiled);

        [Fact]
        public void NoLiteralPlaceholderInPlainLogCalls()
        {
            var violations = new List<string>();

            foreach (var proj in ScannedProjects)
            {
                var projDir = Path.Combine(SolutionDir, proj);
                if (!Directory.Exists(projDir))
                    continue;

                foreach (var file in Directory.GetFiles(projDir, "*.cs", SearchOption.AllDirectories))
                {
                    // Skip generated designer files and the test project's own sources.
                    if (file.Contains(".Tests", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var raw = File.ReadAllText(file);
                    var stripped = StripComments(raw);
                    var relPath = Path.GetRelativePath(SolutionDir, file);

                    foreach (var call in EnumerateLogCalls(stripped))
                    {
                        int lineNum = LineOf(raw, stripped, call.StartIndex);

                        if (!call.IsFormatOverload)
                        {
                            // Plain Log.Error/Notify/Debug: ANY {N} placeholder in a string
                            // literal is the bug — it is joined, never substituted.
                            foreach (var lit in call.StringLiterals)
                            {
                                var m = PlaceholderRx.Match(lit);
                                if (m.Success)
                                {
                                    violations.Add(
                                        $"{relPath}:{lineNum} => plain Log.{call.Method}(...) has literal placeholder " +
                                        $"\"{m.Value}\"; use Log.{call.Method}F(...) so it is substituted.");
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Log.*F: the format string's highest placeholder index must be
                            // satisfiable by the number of supplied arguments, else FormatSafe
                            // would hit its fallback (a malformed-format smell).
                            if (call.StringLiterals.Count == 0)
                                continue; // non-literal format (a variable) — cannot statically check
                            int maxIndex = -1;
                            foreach (Match m in PlaceholderRx.Matches(call.StringLiterals[0]))
                                maxIndex = Math.Max(maxIndex, int.Parse(m.Groups[1].Value));
                            // argCount counts all top-level arguments; the format string is arg 0,
                            // so substitution args available = ArgCount - 1.
                            int substitutionArgs = call.ArgCount - 1;
                            if (maxIndex >= 0 && maxIndex >= substitutionArgs)
                            {
                                violations.Add(
                                    $"{relPath}:{lineNum} => Log.{call.Method}F(...) references placeholder " +
                                    $"{{{maxIndex}}} but only {Math.Max(0, substitutionArgs)} argument(s) supplied.");
                            }
                        }
                    }
                }
            }

            Assert.True(violations.Count == 0,
                $"Found {violations.Count} Log.* placeholder violation(s) (see issue #1637). " +
                $"Plain Log.Error/Notify/Debug must NOT carry {{N}} placeholders — use the *F overloads. " +
                $"Violations:\n" + string.Join("\n", violations));
        }

        /// <summary>Sanity check: the *F overloads exist in Core so the guidance above is actionable.</summary>
        [Fact]
        public void FormatOverloadsExist()
        {
            var logCs = File.ReadAllText(Path.Combine(SolutionDir, "FEBuilderGBA.Core", "Log.cs"));
            Assert.Contains("void ErrorF(", logCs);
            Assert.Contains("void NotifyF(", logCs);
            Assert.Contains("void DebugF(", logCs);
            Assert.Contains("string.Format(", logCs);
        }

        // ------------------------------------------------------------------
        // Lightweight C# comment stripper. Replaces comment characters with
        // spaces (preserving offsets so line numbers stay accurate) but keeps
        // string/char literal contents intact.
        // ------------------------------------------------------------------
        private static string StripComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];

                // Line comment
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') { sb.Append(' '); i++; }
                    continue;
                }
                // Block comment
                if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    while (i < n && !(src[i] == '*' && i + 1 < n && src[i + 1] == '/'))
                    {
                        sb.Append(src[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < n) { sb.Append(' '); i++; }       // '*'
                    if (i < n) { sb.Append(' '); i++; }       // '/'
                    continue;
                }
                // Verbatim string @"..."  ("" is an escaped quote)
                if (c == '@' && i + 1 < n && src[i + 1] == '"')
                {
                    sb.Append(c); i++;
                    sb.Append(src[i]); i++; // opening quote
                    while (i < n)
                    {
                        if (src[i] == '"' && i + 1 < n && src[i + 1] == '"')
                        {
                            sb.Append(src[i]); sb.Append(src[i + 1]); i += 2; continue;
                        }
                        if (src[i] == '"') { sb.Append(src[i]); i++; break; }
                        sb.Append(src[i]); i++;
                    }
                    continue;
                }
                // Regular string "..."  (\" escapes a quote)
                if (c == '"')
                {
                    sb.Append(c); i++;
                    while (i < n)
                    {
                        if (src[i] == '\\' && i + 1 < n) { sb.Append(src[i]); sb.Append(src[i + 1]); i += 2; continue; }
                        if (src[i] == '"') { sb.Append(src[i]); i++; break; }
                        sb.Append(src[i]); i++;
                    }
                    continue;
                }
                // Char literal '...'
                if (c == '\'')
                {
                    sb.Append(c); i++;
                    while (i < n)
                    {
                        if (src[i] == '\\' && i + 1 < n) { sb.Append(src[i]); sb.Append(src[i + 1]); i += 2; continue; }
                        if (src[i] == '\'') { sb.Append(src[i]); i++; break; }
                        sb.Append(src[i]); i++;
                    }
                    continue;
                }

                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private sealed class LogCall
        {
            public string Method = "";          // Error / Notify / Debug
            public bool IsFormatOverload;        // true => *F
            public int StartIndex;
            public int ArgCount;                 // count of top-level arguments
            public List<string> StringLiterals = new();  // contents of any top-level string-literal args
        }

        // Enumerate every Log.(Error|Notify|Debug)(...) call in comment-stripped source,
        // walking balanced parentheses so multi-line calls are captured whole.
        private static IEnumerable<LogCall> EnumerateLogCalls(string src)
        {
            foreach (Match start in LogCallStartRx.Matches(src))
            {
                int open = start.Index + start.Length - 1; // index of '('
                var call = new LogCall
                {
                    Method = start.Groups[1].Value,
                    IsFormatOverload = start.Groups[2].Value == "F",
                    StartIndex = start.Index,
                };

                int depth = 0;
                int i = open;
                int argStart = open + 1;
                bool sawAnyToken = false;
                var literalBuf = new StringBuilder();
                bool argIsPureLiteral = true;
                bool argHasNonWhitespace = false;

                while (i < src.Length)
                {
                    char c = src[i];
                    if (c == '"')
                    {
                        // capture string literal content (handles \" )
                        int j = i + 1;
                        var lit = new StringBuilder();
                        while (j < src.Length)
                        {
                            if (src[j] == '\\' && j + 1 < src.Length) { lit.Append(src[j + 1]); j += 2; continue; }
                            if (src[j] == '"') break;
                            lit.Append(src[j]); j++;
                        }
                        if (depth == 1)
                        {
                            literalBuf.Append(lit);
                            argHasNonWhitespace = true;
                        }
                        sawAnyToken = true;
                        i = j + 1;
                        continue;
                    }
                    if (c == '(') { depth++; i++; continue; }
                    if (c == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            // close last argument
                            FlushArg(call, literalBuf, ref argIsPureLiteral, ref argHasNonWhitespace, sawAnyToken && argStart < i);
                            break;
                        }
                        i++; continue;
                    }
                    if (c == ',' && depth == 1)
                    {
                        FlushArg(call, literalBuf, ref argIsPureLiteral, ref argHasNonWhitespace, true);
                        argStart = i + 1;
                        i++; continue;
                    }
                    if (depth == 1 && !char.IsWhiteSpace(c) && c != '+')
                    {
                        // token that isn't a string literal => arg is not a pure literal
                        argIsPureLiteral = false;
                        argHasNonWhitespace = true;
                    }
                    sawAnyToken = true;
                    i++;
                }

                yield return call;
            }
        }

        private static void FlushArg(LogCall call, StringBuilder literalBuf,
            ref bool argIsPureLiteral, ref bool argHasNonWhitespace, bool argExists)
        {
            if (argExists)
            {
                call.ArgCount++;
                if (argIsPureLiteral && argHasNonWhitespace)
                    call.StringLiterals.Add(literalBuf.ToString());
            }
            literalBuf.Clear();
            argIsPureLiteral = true;
            argHasNonWhitespace = false;
        }

        // Line number of an index in the comment-stripped string maps 1:1 to the raw
        // string because StripComments preserves '\n' positions.
        private static int LineOf(string raw, string stripped, int index)
        {
            int count = 1;
            int max = Math.Min(index, stripped.Length);
            for (int k = 0; k < max; k++)
                if (stripped[k] == '\n') count++;
            return count;
        }
    }
}
