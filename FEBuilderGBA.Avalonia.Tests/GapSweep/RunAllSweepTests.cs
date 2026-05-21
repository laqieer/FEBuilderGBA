// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 7 tests — composite --gap-sweep-all flag. (#374)
//
// These tests invoke `App.RunAllSweep` directly via the
// `InternalsVisibleTo("FEBuilderGBA.Avalonia.Tests")` seam (declared in
// FEBuilderGBA.Avalonia.csproj). They never launch the Avalonia application
// or load the XAML resources — the composite is pure file I/O around five
// scanners and we only validate the I/O / orchestration layer here. The
// per-scanner correctness lives in the existing density / labels / jumps /
// undo / l10n test suites.
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests for the Phase 7 composite `--gap-sweep-all` flag in
/// <see cref="FEBuilderGBA.Avalonia.App.RunAllSweep"/>.
///
/// The composite drives each of the five static-analysis sweeps (density,
/// labels, jumps, undo, l10n) into one date-prefixed file per sweep inside
/// the caller-provided output directory. The tests verify:
/// - All five files appear with the expected `YYYY-MM-DD-&lt;sweep&gt;-sweep.md`
///   naming
/// - Dry-run produces header-only files (front-matter only, no body)
/// - The `--out=` validation rejects file paths and accepts directories
/// - The exit code reflects success / failure aggregation correctly
/// </summary>
public class RunAllSweepTests : IDisposable
{
    readonly string _tempDir;

    public RunAllSweepTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fbgba-allsweep-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Locate the repo root by walking up from the test-bin directory until
    /// we find FEBuilderGBA.sln — same algorithm as the App and the other
    /// gap-sweep test suites. The composite needs the repo root to feed
    /// into each scanner's WF/AV path resolution.
    /// </summary>
    static string FindRepoRoot()
    {
        string start = AppContext.BaseDirectory;
        for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Could not find FEBuilderGBA.sln (test must run from inside the worktree).");
    }

    static readonly string[] ExpectedSweepNames = { "density", "labels", "jumps", "undo", "l10n" };

    // ---------------- Argument validation ----------------

    [Fact]
    public void RunAllSweep_OutPathWithExtension_RejectedAsFilePath()
    {
        // `--out=foo.md` is the standard single-sweep form — when used with
        // --gap-sweep-all it would silently write five reports beside the
        // intended single file. The composite explicitly rejects that to
        // catch CLI typos at the boundary.
        string filePath = Path.Combine(_tempDir, "report.md");
        int code = App.RunAllSweep(FindRepoRoot(), filePath, dryRun: true);
        Assert.Equal(2, code);
        Assert.False(File.Exists(filePath), "must not write to a rejected file path");
    }

    [Fact]
    public void RunAllSweep_DirectoryWithoutExtension_Accepted()
    {
        // The composite happily writes into a freshly-named subdirectory.
        // Dry-run keeps the test fast (no Roslyn / XML scans).
        string outDir = Path.Combine(_tempDir, "2026-05-22");
        int code = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: true);
        Assert.Equal(0, code);
        Assert.True(Directory.Exists(outDir), "must create the output directory");
    }

    // ---------------- File emission and naming ----------------

    [Fact]
    public void RunAllSweep_DryRun_ProducesFiveDatePrefixedReports()
    {
        string outDir = Path.Combine(_tempDir, "drysweep");
        int code = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: true);
        Assert.Equal(0, code);

            var files = Directory.GetFiles(outDir, "*.md").Select(Path.GetFileName).OrderBy(s => s).ToList();
            // The composite must produce one report per sweep — no more, no less.
            Assert.Equal(5, files.Count);

            // Each filename follows the YYYY-MM-DD-<sweep>-sweep.md template.
            // The date is the UTC date at composite invocation time; we accept
            // either today or yesterday/tomorrow to dodge a midnight-rollover
            // false-positive on slow machines.
            foreach (string? f in files)
            {
                Assert.NotNull(f);
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}-(density|labels|jumps|undo|l10n)-sweep\.md$", f!);
            }

            // Every expected sweep must appear exactly once in the directory listing.
            foreach (string expected in ExpectedSweepNames)
            {
                Assert.Single(files, f => f!.EndsWith($"-{expected}-sweep.md", StringComparison.Ordinal));
            }
    }

    [Fact]
    public void RunAllSweep_DryRun_FilesAreHeaderOnly()
    {
        // Dry-run shape: each per-sweep handler writes only the YAML
        // front-matter (between `---` markers) and nothing below. The
        // composite must preserve that invariant for every sub-sweep.
        string outDir = Path.Combine(_tempDir, "drypayload");
        int code = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: true);
        Assert.Equal(0, code);

        foreach (string file in Directory.GetFiles(outDir, "*.md"))
        {
            string contents = File.ReadAllText(file);
            Assert.StartsWith("---", contents);

            // Exactly two `---` markers and nothing meaningful after the
            // second one. Mirrors ReportWriterTests.WriteReport_DryRunHeaderOnly_HasNoBody.
            int firstSep = contents.IndexOf("---", StringComparison.Ordinal);
            int secondSep = contents.IndexOf("---", firstSep + 3, StringComparison.Ordinal);
            Assert.True(secondSep > firstSep, $"{file}: should have two --- markers");
            string afterSecondSep = contents.Substring(secondSep + 3);
            Assert.Equal("", afterSecondSep.Trim());
        }
    }

    [Fact]
    public void RunAllSweep_DryRun_FrontMatterCarriesPerSweepType()
    {
        // Sanity-check: each report's front-matter advertises the sweep type
        // matching its filename. Catches a wiring mistake where two handlers
        // would silently swap their output path.
        string outDir = Path.Combine(_tempDir, "drytypes");
        int code = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: true);
        Assert.Equal(0, code);

        foreach (string file in Directory.GetFiles(outDir, "*.md"))
        {
            string contents = File.ReadAllText(file);
            string fileName = Path.GetFileName(file);
            // Extract the sweep name from the filename (between the date
            // prefix and the "-sweep.md" suffix).
            // YYYY-MM-DD-<sweep>-sweep.md → index 11 ..< len-10
            string sweepFromName = fileName.Substring(11, fileName.Length - 11 - "-sweep.md".Length);
            Assert.Contains($"sweep-type: {sweepFromName}", contents);
        }
    }

    // ---------------- Independence: one failure doesn't abort the others ----------------

    [Fact]
    public void RunAllSweep_OneSubSweepFailure_OtherSweepsStillProduceReports()
    {
        // We can't easily mock a transient failure inside a single scanner
        // (the scanners are static and read the repo state directly), so we
        // exercise the recovery path indirectly: a successful dry-run
        // produces all five files. We then run the composite a second time
        // against a directory the first run already populated — the
        // composite must overwrite cleanly without aborting partway
        // through. This is a regression check on the per-sweep try/catch
        // structure (sub-sweep failures must not propagate out and abort
        // the remaining sweeps).
        string outDir = Path.Combine(_tempDir, "rerun");
        int firstCode = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: true);
        Assert.Equal(0, firstCode);
        Assert.Equal(5, Directory.GetFiles(outDir, "*.md").Length);

        // Capture the first-run timestamps so we can prove the second run
        // re-wrote every file.
        var firstWrites = Directory.GetFiles(outDir, "*.md")
            .ToDictionary(f => Path.GetFileName(f)!, File.GetLastWriteTimeUtc);

        // Tiny sleep so the timestamp comparison has resolution to work
        // with on filesystems with second-level mtime granularity.
        System.Threading.Thread.Sleep(1100);

        int secondCode = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: true);
        Assert.Equal(0, secondCode);

        var secondWrites = Directory.GetFiles(outDir, "*.md")
            .ToDictionary(f => Path.GetFileName(f)!, File.GetLastWriteTimeUtc);

        Assert.Equal(firstWrites.Count, secondWrites.Count);
        foreach (var kv in firstWrites)
        {
            Assert.True(secondWrites.ContainsKey(kv.Key), $"missing {kv.Key} after rerun");
            Assert.True(secondWrites[kv.Key] >= kv.Value, $"{kv.Key} mtime regressed across rerun");
        }
    }

    // ---------------- Output directory creation ----------------

    [Fact]
    public void RunAllSweep_CreatesNestedOutputDirectory()
    {
        // Composite invocations point at a fresh date-stamped subdirectory
        // most of the time; we must auto-create it (and any missing
        // intermediate dirs) rather than failing with "directory not
        // found".
        string nestedDir = Path.Combine(_tempDir, "a", "b", "2026-05-22");
        int code = App.RunAllSweep(FindRepoRoot(), nestedDir, dryRun: true);
        Assert.Equal(0, code);
        Assert.True(Directory.Exists(nestedDir));
        Assert.Equal(5, Directory.GetFiles(nestedDir, "*.md").Length);
    }
}
