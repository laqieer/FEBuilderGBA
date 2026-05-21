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
using System.Collections.Generic;
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
/// - One sub-sweep failure does not abort the others (per-sweep try/catch)
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
        // Always restore the SubSweepsOverride to null so a test that sets
        // it doesn't leak the override into the next test. Belt-and-braces
        // because each test is supposed to clean up its own override.
        App.SubSweepsOverride = null;
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

    /// <summary>
    /// Build a minimal sub-sweep set that writes a one-line header-only
    /// file per sweep and returns 0. Used by tests that don't want to pay
    /// the full Roslyn / XML scan cost just to exercise orchestration.
    /// </summary>
    static IReadOnlyList<(string Name, Func<string, int> Run)> NoOpSubSweeps()
    {
        Func<string, string, int> writer = (sweep, path) =>
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            // Minimal "header-only" — caller assertions only look for the
            // YAML front-matter markers, not real scanner output.
            File.WriteAllText(path, $"---\nsweep-type: {sweep}\n---\n");
            return 0;
        };
        return new (string, Func<string, int>)[]
        {
            ("density", path => writer("density", path)),
            ("labels",  path => writer("labels",  path)),
            ("jumps",   path => writer("jumps",   path)),
            ("undo",    path => writer("undo",    path)),
            ("l10n",    path => writer("l10n",    path)),
        };
    }

    // ---------------- Argument validation ----------------

    [Fact]
    public void RunAllSweep_OutPathWithExtension_RejectedAsFilePath()
    {
        // `--out=foo.md` is the standard single-sweep form — when used with
        // --gap-sweep-all it would silently write five reports beside the
        // intended single file. The composite explicitly rejects that to
        // catch CLI typos at the boundary. Use the no-op overrides so this
        // doesn't accidentally drive the real scanners.
        App.SubSweepsOverride = NoOpSubSweeps();
        string filePath = Path.Combine(_tempDir, "report.md");
        int code = App.RunAllSweep(FindRepoRoot(), filePath, dryRun: true);
        Assert.Equal(2, code);
        Assert.False(File.Exists(filePath), "must not write to a rejected file path");
    }

    [Fact]
    public void RunAllSweep_DirectoryWithoutExtension_Accepted()
    {
        // The composite happily writes into a freshly-named subdirectory.
        // No-op overrides keep the test fast (no Roslyn / XML scans).
        App.SubSweepsOverride = NoOpSubSweeps();
        string outDir = Path.Combine(_tempDir, "2026-05-22");
        int code = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: true);
        Assert.Equal(0, code);
        Assert.True(Directory.Exists(outDir), "must create the output directory");
    }

    // ---------------- File emission and naming ----------------

    [Fact]
    public void RunAllSweep_DryRun_ProducesFiveDatePrefixedReports()
    {
        App.SubSweepsOverride = NoOpSubSweeps();
        string outDir = Path.Combine(_tempDir, "drysweep");
        // Snapshot today's UTC date BEFORE the call so we can compare
        // afterwards against an exact set (today or, in case the test
        // straddles midnight UTC, yesterday — both must be accepted).
        DateTime utcBefore = DateTime.UtcNow;
        int code = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: true);
        DateTime utcAfter = DateTime.UtcNow;
        Assert.Equal(0, code);

        var files = Directory.GetFiles(outDir, "*.md").Select(Path.GetFileName).OrderBy(s => s).ToList();
        // The composite must produce one report per sweep — no more, no less.
        Assert.Equal(5, files.Count);

        // Build the set of acceptable date prefixes — every date the
        // composite could have observed between utcBefore and utcAfter
        // (inclusive). Almost always just one date; the two-date set only
        // matters across a midnight-UTC rollover. We do NOT accept
        // "tomorrow" — that would mask a real bug where the composite
        // picked up the local-time date instead of UTC.
        var acceptableDates = new HashSet<string>();
        for (DateTime d = utcBefore.Date; d <= utcAfter.Date; d = d.AddDays(1))
            acceptableDates.Add(d.ToString("yyyy-MM-dd"));

        foreach (string? f in files)
        {
            Assert.NotNull(f);
            // Each filename follows the YYYY-MM-DD-<sweep>-sweep.md template.
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}-(density|labels|jumps|undo|l10n)-sweep\.md$", f!);
            // The date prefix must be in the allowed window.
            string prefix = f!.Substring(0, 10);
            Assert.Contains(prefix, acceptableDates);
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
        // Use the real (default) sub-sweep set in dry-run mode — the
        // real handlers are the ones that have to honour the no-body
        // promise, so this test exercises them directly. Dry-run keeps
        // it cheap because no scanner code path actually runs.
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

    // ---------------- Per-sweep isolation: one failure doesn't abort the others ----------------

    [Fact]
    public void RunAllSweep_OneSubSweepFailure_OtherSweepsStillProduceReports()
    {
        // Inject a sub-sweep set where the middle entry ("jumps") throws.
        // The per-sweep try/catch in RunAllSweep must record that failure
        // and continue with undo + l10n, AND the composite must still
        // return 0 because four other sub-sweeps succeeded.
        var attempted = new List<string>();
        Func<string, string, int> ok = (sweep, path) =>
        {
            attempted.Add(sweep);
            string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, $"---\nsweep-type: {sweep}\n---\n");
            return 0;
        };
        Func<string, int> boom = path =>
        {
            attempted.Add("jumps");
            throw new InvalidOperationException("simulated transient scanner failure");
        };
        App.SubSweepsOverride = new (string, Func<string, int>)[]
        {
            ("density", path => ok("density", path)),
            ("labels",  path => ok("labels",  path)),
            ("jumps",   boom),
            ("undo",    path => ok("undo",    path)),
            ("l10n",    path => ok("l10n",    path)),
        };

        string outDir = Path.Combine(_tempDir, "isolation");
        int code = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: false);

        // Per the composite's contract: 0 when ≥1 sub-sweep succeeded,
        // even if some failed. 4 of 5 succeeded → expect 0.
        Assert.Equal(0, code);

        // Every sub-sweep was attempted (proves the loop didn't abort on
        // the jumps failure). Order matches the override list.
        Assert.Equal(new[] { "density", "labels", "jumps", "undo", "l10n" }, attempted);

        // The four successful sub-sweeps wrote their files; the failing
        // one did not.
        var files = Directory.GetFiles(outDir, "*.md").Select(Path.GetFileName).ToList();
        Assert.Equal(4, files.Count);
        Assert.Contains(files, f => f!.EndsWith("-density-sweep.md", StringComparison.Ordinal));
        Assert.Contains(files, f => f!.EndsWith("-labels-sweep.md",  StringComparison.Ordinal));
        Assert.Contains(files, f => f!.EndsWith("-undo-sweep.md",    StringComparison.Ordinal));
        Assert.Contains(files, f => f!.EndsWith("-l10n-sweep.md",    StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f!.EndsWith("-jumps-sweep.md", StringComparison.Ordinal));
    }

    [Fact]
    public void RunAllSweep_AllSubSweepsFail_ReturnsExitCodeOne()
    {
        // Companion to the isolation test: when EVERY sub-sweep fails, the
        // composite produced no useful artifacts and must surface that as
        // exit code 1 so CI / callers can distinguish "advisory yielded
        // nothing" from "all five reports published".
        Func<string, int> boom = path =>
            throw new InvalidOperationException("simulated total failure");
        App.SubSweepsOverride = new (string, Func<string, int>)[]
        {
            ("density", boom), ("labels", boom), ("jumps", boom), ("undo", boom), ("l10n", boom),
        };
        string outDir = Path.Combine(_tempDir, "allfail");
        int code = App.RunAllSweep(FindRepoRoot(), outDir, dryRun: false);
        Assert.Equal(1, code);
        // No reports were produced because every sub-sweep threw.
        Assert.Empty(Directory.GetFiles(outDir, "*.md"));
    }

    // ---------------- Output directory creation ----------------

    [Fact]
    public void RunAllSweep_CreatesNestedOutputDirectory()
    {
        // Composite invocations point at a fresh date-stamped subdirectory
        // most of the time; we must auto-create it (and any missing
        // intermediate dirs) rather than failing with "directory not
        // found".
        App.SubSweepsOverride = NoOpSubSweeps();
        string nestedDir = Path.Combine(_tempDir, "a", "b", "2026-05-22");
        int code = App.RunAllSweep(FindRepoRoot(), nestedDir, dryRun: true);
        Assert.Equal(0, code);
        Assert.True(Directory.Exists(nestedDir));
        Assert.Equal(5, Directory.GetFiles(nestedDir, "*.md").Length);
    }
}
