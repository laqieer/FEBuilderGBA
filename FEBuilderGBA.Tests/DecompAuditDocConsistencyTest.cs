using System;
using System.IO;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Tests
{
    /// <summary>
    /// Cross-project guard (#1150): the README "Round-trip coverage matrix" block —
    /// delimited by <c>&lt;!-- decomp-audit-matrix:start --&gt;</c> /
    /// <c>&lt;!-- decomp-audit-matrix:end --&gt;</c> — MUST stay byte-identical (modulo
    /// line endings + trailing whitespace) to
    /// <c>DecompRoundTripAuditCore.FormatMatrix(DecompRoundTripAuditCore.BuildMatrix(), "md")</c>.
    ///
    /// This makes the maintained matrix the single source of truth: a code change to the
    /// matrix that isn't mirrored into the README fails CI here.
    /// </summary>
    public class DecompAuditDocConsistencyTest
    {
        const string StartMarker = "<!-- decomp-audit-matrix:start -->";
        const string EndMarker = "<!-- decomp-audit-matrix:end -->";

        [Fact]
        public void ReadmeMatrixBlock_MatchesGeneratedMatrix()
        {
            string root = FindRepoRoot();
            Assert.NotNull(root);
            string readmePath = Path.Combine(root, "README.md");
            Assert.True(File.Exists(readmePath), $"README.md not found at {readmePath}");

            string readme = File.ReadAllText(readmePath);

            int start = readme.IndexOf(StartMarker, StringComparison.Ordinal);
            int end = readme.IndexOf(EndMarker, StringComparison.Ordinal);
            Assert.True(start >= 0, "README is missing the decomp-audit-matrix:start marker");
            Assert.True(end > start, "README is missing the decomp-audit-matrix:end marker (or markers are out of order)");

            // Block is the text BETWEEN the markers (exclusive).
            int blockStart = start + StartMarker.Length;
            string readmeBlock = readme.Substring(blockStart, end - blockStart);

            string generated = DecompRoundTripAuditCore.FormatMatrix(
                DecompRoundTripAuditCore.BuildMatrix(), "md");

            Assert.Equal(Normalize(generated), Normalize(readmeBlock));
        }

        /// <summary>
        /// Normalize line endings (CRLF/CR → LF) and strip leading/trailing whitespace per
        /// line + overall, so the comparison is robust to platform line endings and the
        /// stray blank lines that surround the README block between its markers.
        /// </summary>
        static string Normalize(string s)
        {
            if (s == null) return "";
            string lf = s.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = lf.Split('\n');
            var keep = new System.Collections.Generic.List<string>();
            foreach (string line in lines)
            {
                string trimmed = line.TrimEnd();
                keep.Add(trimmed);
            }
            // Drop leading/trailing empty lines, keep interior structure.
            int first = 0, last = keep.Count - 1;
            while (first <= last && keep[first].Length == 0) first++;
            while (last >= first && keep[last].Length == 0) last--;
            var sb = new System.Text.StringBuilder();
            for (int i = first; i <= last; i++)
            {
                sb.Append(keep[i]);
                if (i < last) sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Walk up from the test base directory to the dir containing FEBuilderGBA.sln.</summary>
        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
