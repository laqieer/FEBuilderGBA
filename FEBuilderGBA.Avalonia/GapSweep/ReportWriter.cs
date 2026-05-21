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
        /// </summary>
        public static void WriteReport(
            string path,
            string sweepType,
            IEnumerable<string> sections,
            IDictionary<string, string>? extraFrontMatter = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path must be non-empty", nameof(path));
            if (string.IsNullOrEmpty(sweepType))
                throw new ArgumentException("sweepType must be non-empty", nameof(sweepType));

            string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string header = BuildFrontMatter(sweepType, extraFrontMatter);
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
        public static string BuildFrontMatter(string sweepType, IDictionary<string, string>? extras = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.Append("generated: ").Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")).Append('\n');
            sb.Append("git-sha: ").Append(TryGetGitSha()).Append('\n');
            sb.Append("sweep-type: ").Append(sweepType).Append('\n');
            if (extras != null)
            {
                foreach (var kv in extras)
                {
                    sb.Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
                }
            }
            sb.AppendLine("---");
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>
        /// Best-effort `git rev-parse --short HEAD`. CI sometimes runs without a
        /// .git context (e.g. tarballed source) — in that case we return "unknown"
        /// rather than failing the report generation.
        /// </summary>
        static string TryGetGitSha()
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
                using var p = Process.Start(psi);
                if (p == null)
                    return "unknown";
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(3000);
                if (p.ExitCode != 0 || string.IsNullOrEmpty(output))
                    return "unknown";
                return output;
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
