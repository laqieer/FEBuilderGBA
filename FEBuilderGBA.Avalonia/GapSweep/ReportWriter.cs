// SPDX-License-Identifier: GPL-3.0-or-later
// FEBuilderGBA gap-sweep tooling (#374).
//
// ReportWriter emits the markdown reports under docs/avalonia-gaps/.
// Every report gets a YAML front-matter block so downstream tooling
// (e.g. Phase 7 CI summaries) can introspect the metadata without parsing
// the body. The front-matter always carries:
//
//   generated:  ISO-8601 UTC timestamp of report generation
//   git-sha:    short SHA of HEAD (or "unknown" when git is not reachable)
//   sweep-type: e.g. "density", "labels", "jumps", "undo", "l10n"
//
// plus any extra key/value pairs the caller supplies (e.g. "rom: FE8U").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FEBuilderGBA.Avalonia.GapSweep
{
    /// <summary>Writes gap-sweep reports with a uniform YAML front-matter header.</summary>
    public static class ReportWriter
    {
        /// <summary>
        /// Write a report to <paramref name="path"/>. The body is the concatenation
        /// of <paramref name="sections"/> separated by blank lines. The directory
        /// is created if it does not already exist.
        ///
        /// <paramref name="gitWorkingDir"/> is passed through to the git-sha probe so
        /// the SHA reflects the worktree under analysis rather than the caller's CWD.
        /// </summary>
        public static void WriteReport(
            string path,
            string sweepType,
            IEnumerable<string> sections,
            IDictionary<string, string>? extraFrontMatter = null,
            string? gitWorkingDir = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path must be non-empty", nameof(path));
            if (string.IsNullOrEmpty(sweepType))
                throw new ArgumentException("sweepType must be non-empty", nameof(sweepType));

            string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string header = BuildFrontMatter(sweepType, extraFrontMatter, gitWorkingDir);
            var sb = new StringBuilder();
            sb.Append(header);

            bool first = true;
            foreach (string section in sections)
            {
                if (string.IsNullOrEmpty(section))
                    continue;
                if (!first)
                    sb.Append("\n\n");
                sb.Append(section);
                first = false;
            }
            // Final newline so editors that strip trailing-newlines don't churn the file.
            if (sb.Length == 0 || sb[sb.Length - 1] != '\n')
                sb.Append('\n');

            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// Build just the front-matter header — the dry-run code path uses this
        /// (writes header only, no body) so the report file is still a valid
        /// markdown skeleton.
        /// </summary>
        public static string BuildFrontMatter(
            string sweepType,
            IDictionary<string, string>? extras = null,
            string? gitWorkingDir = null)
        {
            // Use LF consistently (no Environment.NewLine, no AppendLine) so the
            // emitted YAML block has identical line endings across Windows/Linux/macOS.
            // Mixing CRLF and LF would dirty up committed reports on Windows checkouts.
            var sb = new StringBuilder();
            sb.Append("---\n");
            sb.Append("generated: ").Append(EscapeYamlScalar(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))).Append('\n');
            sb.Append("git-sha: ").Append(EscapeYamlScalar(TryGetGitSha(gitWorkingDir))).Append('\n');
            sb.Append("sweep-type: ").Append(EscapeYamlScalar(sweepType)).Append('\n');
            if (extras != null)
            {
                foreach (var kv in extras)
                {
                    // YAML keys must be plain scalars too; we escape both sides so
                    // a caller-supplied value with `:` / `#` / newline / quotes
                    // produces a parseable document instead of breaking the front-
                    // matter block.
                    sb.Append(EscapeYamlKey(kv.Key)).Append(": ").Append(EscapeYamlScalar(kv.Value)).Append('\n');
                }
            }
            sb.Append("---\n\n");
            return sb.ToString();
        }

        /// <summary>
        /// YAML 1.2 plain-scalar safety net. We err on the side of quoting any
        /// value that contains a YAML metacharacter (or looks like a YAML keyword)
        /// so downstream parsers always see a string. Inside double-quoted scalars
        /// only `"` and `\` need escaping (other YAML escapes are optional).
        /// </summary>
        public static string EscapeYamlScalar(string? value)
        {
            value ??= "";
            if (value.Length == 0)
                return "\"\"";

            // Triggers that force double-quoting: anything that could change YAML
            // parsing or yield a non-string scalar.
            bool needsQuote =
                value.IndexOfAny(new[] { ':', '#', '\n', '\r', '\t', '"', '\\', '\'', ' ' }) >= 0
                || value.StartsWith(" ") || value.EndsWith(" ")
                || value.StartsWith("-") || value.StartsWith("?") || value.StartsWith("[") || value.StartsWith("{")
                || value.StartsWith("|") || value.StartsWith(">") || value.StartsWith("@") || value.StartsWith("`")
                || value.StartsWith("*") || value.StartsWith("&") || value.StartsWith("!") || value.StartsWith("%")
                || IsYamlReservedWord(value);

            if (!needsQuote)
                return value;

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// Apply the same escaping to keys. Plain-scalar YAML keys allow letters,
        /// digits, dash and underscore; anything else gets quoted to keep parsers
        /// happy.
        /// </summary>
        public static string EscapeYamlKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "\"\"";
            foreach (char c in key)
            {
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                    return EscapeYamlScalar(key);
            }
            return key;
        }

        /// <summary>
        /// YAML 1.1 boolean / null lookalikes that must be quoted. We accept any
        /// case fold (`True`, `FALSE`, `On`, etc.) because most YAML 1.1 parsers
        /// treat the keywords case-insensitively — `True` would still be coerced
        /// to a boolean, defeating the safety net.
        /// </summary>
        static readonly HashSet<string> YamlReservedWords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "true", "false",
                "yes", "no",
                "on", "off",
                "y", "n",
                "null", "~",
            };

        static bool IsYamlReservedWord(string value) => YamlReservedWords.Contains(value);

        /// <summary>
        /// Best-effort `git rev-parse --short HEAD`. CI sometimes runs without a
        /// .git context (e.g. tarballed source) — in that case we return "unknown"
        /// rather than failing the report generation.
        ///
        /// Implementation notes:
        ///   - Reads stdout asynchronously into a StringBuilder via OutputDataReceived
        ///     and uses WaitForExit(timeout) BEFORE consuming the captured output.
        ///     A synchronous ReadToEnd would block until the child exits, defeating
        ///     the timeout entirely if git stalls or prompts.
        ///   - Runs with WorkingDirectory set to the gap-sweep repo root so the
        ///     SHA comes from the worktree under analysis rather than the caller's
        ///     CWD (which may be unrelated when invoked from CI or another tool).
        /// </summary>
        static string TryGetGitSha(string? workingDir = null)
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
                    psi.WorkingDirectory = workingDir;

                using var p = Process.Start(psi);
                if (p == null)
                    return "unknown";

                // Async stdout capture so WaitForExit's timeout is honoured even
                // if git itself hangs (e.g. credential prompt, pack-objects stall).
                var sb = new System.Text.StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                // Drain stderr to /dev/null so the pipe doesn't fill up — we don't
                // surface it but we mustn't block git on a full stderr buffer.
                p.ErrorDataReceived += (_, _) => { };
                p.BeginErrorReadLine();

                if (!p.WaitForExit(3000))
                {
                    try { p.Kill(entireProcessTree: true); }
                    catch { /* best effort */ }
                    return "unknown";
                }
                // Force a final flush of any buffered output after the process exits.
                p.WaitForExit();

                if (p.ExitCode != 0)
                    return "unknown";
                string output = sb.ToString().Trim();
                return string.IsNullOrEmpty(output) ? "unknown" : output;
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
