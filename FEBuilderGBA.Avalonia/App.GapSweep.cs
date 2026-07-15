// Desktop-only GapSweep (Roslyn static-analysis dev-tooling) partial.
// Excluded from the net10.0-android TFM (#1121); GapSweep is not shippable
// Android UI. All methods here reference FEBuilderGBA.Avalonia.GapSweep.*
// types (Roslyn-backed scanners / report writers), which in turn pull in the
// Microsoft.CodeAnalysis.CSharp package — both are desktop-only. The android
// head removes this file plus Program.cs (the desktop entry point that calls
// RunGapSweepStandalone) and the Avalonia.Desktop / app.manifest references,
// so the shared UI compiles cleanly for net10.0-android.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using FEBuilderGBA.Avalonia.GapSweep;

namespace FEBuilderGBA.Avalonia
{
    public partial class App : Application
    {
        /// <summary>
        /// Pre-Avalonia entry point for the gap-sweep flags (called by
        /// <see cref="Program.Main"/>). Returns the exit code to use, or
        /// <c>null</c> when no `--gap-sweep-*` flag is present (in which case
        /// the caller continues with the normal Avalonia boot).
        ///
        /// This runs BEFORE Skia / Avalonia initialise so headless CI
        /// runners (where libSkiaSharp's version can mismatch the managed
        /// NuGet) can still publish reports without crashing in the Skia
        /// font-manager static constructor. Gap-sweep is pure static
        /// analysis — Roslyn syntax-trees + XDocument XML scans — and needs
        /// nothing from the windowing stack.
        ///
        /// Wires only the minimum CoreState the scanners require:
        /// <see cref="CoreState.BaseDirectory"/> (used to walk up to the
        /// repo root) and nothing else. The headless caches and Skia
        /// services that <see cref="OnFrameworkInitializationCompleted"/>
        /// installs are not touched by the scanners.
        /// </summary>
        internal static int? RunGapSweepStandalone(string[] args)
        {
            ParseArgs(args);
            if (GapSweepMode == null)
                return null;
            // Encoding.RegisterProvider for Shift-JIS isn't required by the
            // scanners (Roslyn handles source encoding via BOM detection,
            // and Phase 6 reads UTF-8 translate/*.txt). We register anyway
            // to keep behavioural parity with the main-form boot path in
            // case a future scanner does need it.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            CoreState.BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return RunGapSweep();
        }

        /// <summary>
        /// Execute the gap-sweep flow chosen by <see cref="GapSweepMode"/>. Returns
        /// the process exit code (0 success, 2 missing required argument, 1 fatal).
        /// Wraps each scanner in an outer try/catch so a fatal exception terminates
        /// the run with code 1 rather than crashing the entire app. NOTE: the per-
        /// file scanners themselves currently swallow individual file-level parse
        /// / I/O failures and record 0 for the affected count; Phase 7 will add
        /// explicit error rows to the report so silent zero-counts don't masquerade
        /// as real migration gaps.
        /// </summary>
        static int RunGapSweep()
        {
            if (string.IsNullOrEmpty(GapSweepOut))
            {
                Console.Error.WriteLine("--gap-sweep-* requires --out=<path>");
                return 2;
            }

            string repoRoot = GapSweepRepoRoot ?? FindRepoRoot();
            Console.WriteLine($"GAPSWEEP[{GapSweepMode}]: repo-root={repoRoot} out={GapSweepOut} dry-run={GapSweepDryRun}");

            try
            {
                switch (GapSweepMode)
                {
                    case "density":
                        return RunDensitySweep(repoRoot, GapSweepOut!, GapSweepDryRun);

                    case "labels":
                        return RunLabelsSweep(repoRoot, GapSweepOut!, GapSweepDryRun);

                    case "gallery":
                        return RunGalleryBuild(
                            repoRoot,
                            GapSweepWfDir,
                            GapSweepAvDir,
                            GapSweepRomTag,
                            GapSweepOut!,
                            GapSweepDryRun);

                    case "jumps":
                        return RunJumpsSweep(repoRoot, GapSweepOut!, GapSweepDryRun);

                    case "undo":
                        return RunUndoSweep(repoRoot, GapSweepOut!, GapSweepDryRun);

                    case "l10n":
                        return RunL10nSweep(repoRoot, GapSweepOut!, GapSweepDryRun, GapSweepLanguages);

                    case "all":
                        return RunAllSweep(repoRoot, GapSweepOut!, GapSweepDryRun);

                    default:
                        Console.Error.WriteLine($"Unknown gap-sweep mode: {GapSweepMode}");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"GAPSWEEP[{GapSweepMode}]: fatal: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        /// <summary>
        /// Phase 1: control-density delta sweep. Returns 0 on success.
        /// In dry-run mode we still discover pairs (cheap) so we can log a count,
        /// but we write a truly header-only file (no markdown body, just the YAML
        /// front-matter) — callers use this to verify file-system permissions and
        /// the CLI plumbing without paying the Roslyn / XML scan cost.
        /// </summary>
        static int RunDensitySweep(string repoRoot, string outPath, bool dryRun)
        {
            var pairs = PairMatcher.DiscoverAll(repoRoot);
            Console.WriteLine($"GAPSWEEP[density]: discovered {pairs.Count} editor pairs.");

            if (dryRun)
            {
                // Header-only: no body sections. The front-matter alone proves
                // the writer can reach the path and emits valid YAML; that is the
                // entirety of what dry-run is meant to test.
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, ReportWriter.BuildFrontMatter("density", gitWorkingDir: repoRoot));
                Console.WriteLine("GAPSWEEP[density]: dry-run header written.");
                return 0;
            }

            var rows = ControlDensityScanner.Scan(pairs, repoRoot);
            Console.WriteLine($"GAPSWEEP[density]: scanned {rows.Count} non-empty rows.");

            string body = ControlDensityScanner.FormatReport(rows);
            ReportWriter.WriteReport(outPath, "density", new[] { body }, gitWorkingDir: repoRoot);
            Console.WriteLine($"GAPSWEEP[density]: report written to {outPath}");
            return 0;
        }

        /// <summary>
        /// Phase 2: field-label diff sweep. Returns 0 on success.
        /// In dry-run mode we still discover pairs so we can log a count, but we
        /// write a header-only file (no markdown body, just YAML front-matter) —
        /// callers use this to verify file-system permissions and the CLI plumbing
        /// without paying the Roslyn / XML scan cost. Mirrors RunDensitySweep's
        /// shape exactly so the two flag handlers stay symmetrical.
        /// </summary>
        static int RunLabelsSweep(string repoRoot, string outPath, bool dryRun)
        {
            var pairs = PairMatcher.DiscoverAll(repoRoot);
            Console.WriteLine($"GAPSWEEP[labels]: discovered {pairs.Count} editor pairs.");

            if (dryRun)
            {
                // Header-only: identical pattern to RunDensitySweep's dry-run.
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, ReportWriter.BuildFrontMatter("labels", gitWorkingDir: repoRoot));
                Console.WriteLine("GAPSWEEP[labels]: dry-run header written.");
                return 0;
            }

            var rows = LabelDiffScanner.Scan(pairs);
            int pairsWithGap = rows.Count(r => r.WfOnlyLabels.Count > 0);
            Console.WriteLine($"GAPSWEEP[labels]: scanned {rows.Count} pairs with both files; {pairsWithGap} have >=1 WF-only label.");

            // Also run the density scan in-memory so the labels report can
            // cross-link each per-pair section to its density verdict. The
            // density scan is cheap (~1-2 s with the parallel scanner) and
            // gives reviewers the quantitative context for each qualitative
            // gap row.
            var densityRows = ControlDensityScanner.Scan(pairs, repoRoot);
            // Pick the latest density baseline (file name only, no directory
            // prefix) sitting in the same docs folder so the top-of-report
            // cross-link works regardless of the date of THIS report.
            string? densityLink = LabelDiffScanner.FindLatestDensityReport(outPath);
            Console.WriteLine($"GAPSWEEP[labels]: density cross-link → {densityLink ?? "(none found)"}");

            string body = LabelDiffScanner.FormatReport(rows, densityRows, densityLink);
            ReportWriter.WriteReport(outPath, "labels", new[] { body }, gitWorkingDir: repoRoot);
            Console.WriteLine($"GAPSWEEP[labels]: report written to {outPath}");
            return 0;
        }

        /// <summary>
        /// Phase 4: headless jump/navigation parity sweep. Returns 0 on success.
        /// In dry-run mode we write only the YAML front-matter header (mirrors
        /// RunDensitySweep / RunLabelsSweep dry-run shape) so callers can verify
        /// the CLI plumbing and write permissions without paying the Roslyn /
        /// reflection scan cost.
        /// </summary>
        static int RunJumpsSweep(string repoRoot, string outPath, bool dryRun)
        {
            if (dryRun)
            {
                // Header-only — identical pattern to other dry-run paths.
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, ReportWriter.BuildFrontMatter("jumps", gitWorkingDir: repoRoot));
                Console.WriteLine("GAPSWEEP[jumps]: dry-run header written.");
                return 0;
            }

            var rows = JumpParityScanner.Scan(repoRoot);
            int countMatch = rows.Count(r => r.Status == JumpRowStatus.Match);
            int countMissing = rows.Count(r => r.Status == JumpRowStatus.MissingAvManifest);
            int countNoWf = rows.Count(r => r.Status == JumpRowStatus.NoWfCallsite);
            int countKnown = rows.Count(r => r.Status == JumpRowStatus.KnownGap);
            Console.WriteLine($"GAPSWEEP[jumps]: scanned {rows.Count} rows " +
                $"(match={countMatch} missing={countMissing} no-wf={countNoWf} known-gap={countKnown}).");

            string body = JumpParityScanner.FormatReport(rows);
            ReportWriter.WriteReport(outPath, "jumps", new[] { body }, gitWorkingDir: repoRoot);
            Console.WriteLine($"GAPSWEEP[jumps]: report written to {outPath}");
            return 0;
        }

        /// <summary>
        /// Phase 5: undo coverage sweep. Returns 0 on success.
        /// In dry-run mode we write only the YAML front-matter header (mirrors
        /// RunDensitySweep / RunLabelsSweep / RunJumpsSweep dry-run shape) so
        /// callers can verify the CLI plumbing and write permissions without
        /// paying the Roslyn scan cost.
        /// </summary>
        static int RunUndoSweep(string repoRoot, string outPath, bool dryRun)
        {
            if (dryRun)
            {
                // Header-only — identical pattern to other dry-run paths.
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, ReportWriter.BuildFrontMatter("undo", gitWorkingDir: repoRoot));
                Console.WriteLine("GAPSWEEP[undo]: dry-run header written.");
                return 0;
            }

            var rows = UndoCoverageScanner.Scan(repoRoot);
            int noField = rows.Count(r => r.Coverage == UndoCoverage.NoUndoServiceField);
            int missing = rows.Count(r => r.Coverage == UndoCoverage.MissingScope);
            int ambiguous = rows.Count(r => r.Coverage == UndoCoverage.AmbiguousScope);
            int covered = rows.Count(r => r.Coverage == UndoCoverage.Covered);
            Console.WriteLine($"GAPSWEEP[undo]: scanned {rows.Count} write callsites " +
                $"(no-field={noField} missing-scope={missing} ambiguous={ambiguous} covered={covered}).");

            string body = UndoCoverageScanner.FormatReport(rows);
            ReportWriter.WriteReport(outPath, "undo", new[] { body }, gitWorkingDir: repoRoot);
            Console.WriteLine($"GAPSWEEP[undo]: report written to {outPath}");
            return 0;
        }

        /// <summary>
        /// Phase 6: localisation sweep. Inventories every English-looking AXAML
        /// literal under `FEBuilderGBA.Avalonia/Views/` and joins it against the
        /// translation tables in `config/translate/<lang>.txt`. Returns 0 on
        /// success.
        ///
        /// In dry-run mode we write only the YAML front-matter header (mirrors
        /// every other sweep's dry-run shape) so callers can verify the CLI
        /// plumbing without paying the XML-scan cost. The `languages` arg is a
        /// comma-separated list (e.g. "ja,zh,ko"); when null we use
        /// <see cref="L10nScanner.DefaultLanguages"/>.
        /// </summary>
        static int RunL10nSweep(string repoRoot, string outPath, bool dryRun, string? languagesArg)
        {
            // Parse the languages list once. Empty / null falls back to the
            // default; we filter blank entries so "ja,,zh" still works.
            IReadOnlyList<string> languages = string.IsNullOrEmpty(languagesArg)
                ? L10nScanner.DefaultLanguages
                : languagesArg.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();
            if (languages.Count == 0)
                languages = L10nScanner.DefaultLanguages;

            // Front-matter extras: surface the languages set in the metadata so
            // downstream tooling (Phase 7 CI) can introspect which targets the
            // report covered without re-parsing the markdown body.
            var extras = new Dictionary<string, string>
            {
                ["languages"] = string.Join(",", languages),
            };

            if (dryRun)
            {
                // Header-only — identical pattern to other dry-run paths.
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, ReportWriter.BuildFrontMatter("l10n", extras, gitWorkingDir: repoRoot));
                Console.WriteLine($"GAPSWEEP[l10n]: dry-run header written (langs={string.Join(",", languages)}).");
                return 0;
            }

            var findings = L10nScanner.Scan(repoRoot, languages);
            int translated = findings.Count(f => f.Verdict == L10nVerdict.Translated);
            int partial = findings.Count(f => f.Verdict == L10nVerdict.PartiallyTranslated);
            int untranslated = findings.Count(f => f.Verdict == L10nVerdict.Untranslated);
            int nonEnglish = findings.Count(f => f.Verdict == L10nVerdict.NonEnglish);
            Console.WriteLine($"GAPSWEEP[l10n]: scanned {findings.Count} literals " +
                $"(translated={translated} partial={partial} untranslated={untranslated} non-english={nonEnglish}; " +
                $"langs={string.Join(",", languages)}).");

            string body = L10nScanner.FormatReport(findings, languages);
            ReportWriter.WriteReport(outPath, "l10n", new[] { body }, extras, gitWorkingDir: repoRoot);
            Console.WriteLine($"GAPSWEEP[l10n]: report written to {outPath}");
            return 0;
        }

        /// <summary>
        /// Phase 3: side-by-side screenshot gallery. Pairs PNGs emitted by the
        /// two `--screenshot-all` runners (WinForms in
        /// `FEBuilderGBA/ScreenshotAllRunner.cs`, Avalonia in
        /// `FEBuilderGBA.Avalonia/Views/MainWindow.axaml.cs:RunScreenshotAll`)
        /// and emits a `| Editor | WinForms | Avalonia |` Markdown table.
        ///
        /// Dry-run is intentionally relaxed: writes a header-only file (just
        /// the YAML front-matter) without requiring the wf/av dirs or the ROM
        /// tag, so callers can verify the CLI plumbing and write permissions
        /// before doing a full capture run. The non-dry-run path requires all
        /// four of <paramref name="wfDir"/>, <paramref name="avDir"/>,
        /// <paramref name="romTag"/>, and <paramref name="outPath"/>.
        /// </summary>
        static int RunGalleryBuild(
            string repoRoot,
            string? wfDir,
            string? avDir,
            string? romTag,
            string outPath,
            bool dryRun)
        {
            if (dryRun)
            {
                // Header-only — identical pattern to the other dry-run paths.
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, ReportWriter.BuildFrontMatter("gallery", gitWorkingDir: repoRoot));
                Console.WriteLine("GAPSWEEP[gallery]: dry-run header written.");
                return 0;
            }

            // Full run requires the three capture-driven args.
            var missing = new List<string>();
            if (string.IsNullOrEmpty(wfDir)) missing.Add("--wf-dir");
            if (string.IsNullOrEmpty(avDir)) missing.Add("--av-dir");
            if (string.IsNullOrEmpty(romTag)) missing.Add("--rom-tag");
            if (missing.Count > 0)
            {
                Console.Error.WriteLine($"--gap-sweep-gallery requires: {string.Join(", ", missing)}");
                return 2;
            }

            // Capture-summary metadata (Copilot Phase 3 plan v2 concern #2 / PR review).
            // Computing total PNG count per side BEFORE pairing lets the
            // front-matter distinguish "fully-paired full capture" from
            // "non-Windows host: AV-only" from "WinForms captured nothing".
            // The runners mask exit codes (WinForms always exits 0 even when
            // the inner ScreenshotAllRunner sets ExitCode); these counts
            // surface the discrepancy in the committed manifest.
            int wfCaptured = SafePngCount(wfDir);
            int avCaptured = SafePngCount(avDir);
            string status = ComputeGalleryStatus(wfCaptured, avCaptured);

            // Cross-check expected editors against the project's coverage doc so
            // the gallery's MissingFromExpected section surfaces docs/runtime drift.
            var expected = GalleryBuilder.LoadExpectedEditorsFromDoc(repoRoot);
            Console.WriteLine($"GAPSWEEP[gallery]: loaded {expected.Count} expected editor names from docs/avalonia-gui-forms.md.");
            Console.WriteLine($"GAPSWEEP[gallery]: PNG counts — wf={wfCaptured} av={avCaptured} status={status}");

            var report = GalleryBuilder.BuildGallery(wfDir!, avDir!, romTag!, expected);
            Console.WriteLine($"GAPSWEEP[gallery]: paired={report.Pairs.Count} av-only={report.AvOnly.Count} wf-only={report.WfOnly.Count} missing={report.MissingFromExpected.Count}");

            // Image links use "wf"/"av" relative dirs — same names the
            // make-screenshots.ps1 wrapper creates beside the index.md.
            string body = GalleryBuilder.FormatIndexMarkdown(report, "wf", "av");
            var extras = new Dictionary<string, string>
            {
                ["rom"] = romTag!,
                ["status"] = status,
                ["wf-captured"] = wfCaptured.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["av-captured"] = avCaptured.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["paired"] = report.Pairs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["av-only"] = report.AvOnly.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["wf-only"] = report.WfOnly.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["missing-from-expected"] = report.MissingFromExpected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            ReportWriter.WriteReport(outPath, "gallery", new[] { body }, extras, gitWorkingDir: repoRoot);
            Console.WriteLine($"GAPSWEEP[gallery]: report written to {outPath}");
            return 0;
        }

        /// <summary>
        /// Count `*.png` files in <paramref name="dir"/>. Returns 0 (not -1)
        /// for missing or unreadable directories so the JSON-like front-matter
        /// stays clean. The companion `status` field encodes WHY the count
        /// might be zero (no-windows, capture-fail, etc.).
        /// </summary>
        static int SafePngCount(string? dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return 0;
            try
            {
                return Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly).Count();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Classify the gallery run for the front-matter `status:` field. The
        /// values are machine-readable so downstream tooling (Phase 7 CI) can
        /// gate on `status: complete` and surface partial / failed runs as
        /// warnings rather than treating them as successful baselines.
        /// </summary>
        static string ComputeGalleryStatus(int wfCount, int avCount)
        {
            if (wfCount == 0 && avCount == 0)
                return "empty";
            if (wfCount == 0)
                return "av-only";
            if (avCount == 0)
                return "wf-only";
            return "complete";
        }

        /// <summary>
        /// Phase 7: composite sweep — runs every static-analysis sub-sweep
        /// (density, labels, jumps, undo, l10n) and writes each report into
        /// <paramref name="outDir"/> with a date-prefixed file name. Skipped:
        /// the Phase 3 `gallery` sweep, which requires both `--screenshot-all`
        /// runners to have produced PNGs first (Windows + ROM dependent — not
        /// something the composite flag drives on its own).
        ///
        /// `outDir` MUST be a directory (no file extension). The current
        /// `--out=` parser unpacks a single path string; we apply a heuristic
        /// here: anything with an extension is treated as a file-path mistake
        /// and rejected with a clear error so callers can correct the CLI
        /// invocation instead of silently writing five reports beside their
        /// intended single-file path.
        ///
        /// Sub-sweep failures do NOT abort the composite. Each scanner runs
        /// inside its own try/catch and its outcome is recorded in the
        /// summary table; the composite returns 0 as long as ANY sub-sweep
        /// succeeded, and 1 only when all five failed. Advisory CI uses this
        /// directly — a single transient scanner failure should not block the
        /// other four sweeps' artifacts from publishing.
        ///
        /// Returns 0 on overall success (≥1 sub-sweep succeeded), 1 when all
        /// sub-sweeps failed, 2 on argument validation errors.
        /// </summary>
        internal static int RunAllSweep(string repoRoot, string outDir, bool dryRun)
        {
            // Reject `--out=foo.md`-style file paths — the composite needs a
            // directory because it emits five reports. We use HasExtension as
            // the heuristic because directory paths without an extension are
            // the dominant idiom in the existing CLI examples
            // (`docs/avalonia-gaps/2026-05-22-...`).
            if (Path.HasExtension(outDir))
            {
                Console.Error.WriteLine(
                    $"--gap-sweep-all --out=<dir> requires a directory path; got '{outDir}' (looks like a file). " +
                    "Pass a directory without an extension (e.g. --out=docs/avalonia-gaps/2026-05-22).");
                return 2;
            }

            // Materialise the output directory up-front so per-sweep handlers
            // don't all race to create it. CreateDirectory is idempotent.
            try
            {
                Directory.CreateDirectory(outDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"--gap-sweep-all: cannot create output directory '{outDir}': {ex.Message}");
                return 2;
            }

            // Allow tests to override which sub-sweeps run (and how) so they
            // can inject a deliberately-failing sweep to exercise the
            // per-sweep try/catch isolation. Production path uses the
            // default set built by BuildDefaultSubSweeps below.
            var subs = SubSweepsOverride ?? BuildDefaultSubSweeps(repoRoot, dryRun);
            string datePrefix = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            // Run each sub-sweep and track success/failure for the summary.
            // We deliberately swallow exceptions per-sweep so one transient
            // scanner failure (e.g. a corrupt AXAML during a flaky CI build)
            // doesn't suppress the other four sweeps' artifacts.
            var results = new List<(string Name, string Path, int ExitCode, string? Error)>();
            foreach (var (name, run) in subs)
            {
                string fileName = $"{datePrefix}-{name}-sweep.md";
                string fullPath = Path.Combine(outDir, fileName);
                Console.WriteLine($"GAPSWEEP[all]: running {name} -> {fullPath}");
                try
                {
                    int code = run(fullPath);
                    results.Add((name, fullPath, code, null));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"GAPSWEEP[all]: {name} sweep failed: {ex.Message}");
                    results.Add((name, fullPath, 1, ex.Message));
                }
            }

            // Emit a console summary table so CI logs surface the per-sweep
            // outcomes at a glance. Format mirrors the per-sweep `scanned N
            // rows` lines so the same grep on the workflow log keeps working.
            Console.WriteLine();
            Console.WriteLine("GAPSWEEP[all]: summary");
            Console.WriteLine("| sweep    | exit | wrote                                |");
            Console.WriteLine("|----------|-----:|--------------------------------------|");
            foreach (var (name, path, code, _) in results)
            {
                bool wrote = File.Exists(path);
                string wroteCell = wrote ? Path.GetFileName(path) : "(missing)";
                Console.WriteLine(
                    $"| {name,-8} | {code,4} | {wroteCell,-36} |");
            }

            int successes = results.Count(r => r.ExitCode == 0);
            int failures = results.Count - successes;
            Console.WriteLine();
            Console.WriteLine($"GAPSWEEP[all]: {successes}/{results.Count} sub-sweeps succeeded ({failures} failed).");

            // Overall exit code: 0 if ANY sub-sweep succeeded (advisory mode);
            // 1 only when every sub-sweep failed (the composite produced no
            // useful artifacts).
            return successes > 0 ? 0 : 1;
        }

        /// <summary>
        /// Build the default sub-sweep set for the production composite run.
        /// Density is computed ONCE here and passed in to the labels sweep
        /// (which would otherwise re-run its own in-memory density scan to
        /// annotate label gaps — wasteful when we're about to compute it
        /// for the explicit density sub-sweep anyway). The labels sub-sweep
        /// then takes the precomputed density rows and skips the redundant
        /// scan. All other sub-sweeps stay as direct calls to their Run*
        /// handlers because they don't share work with each other.
        ///
        /// Each tuple is (sweep-name, run-function); the run-function takes
        /// the report file path and returns the per-sweep exit code.
        /// </summary>
        static IReadOnlyList<(string Name, Func<string, int> Run)> BuildDefaultSubSweeps(
            string repoRoot, bool dryRun)
        {
            // Cache the pairs and density rows so labels and density don't
            // each re-discover them. Lazy so dry-run (which doesn't need the
            // scanner output) stays fast.
            IReadOnlyList<GapSweep.EditorPair>? cachedPairs = null;
            IReadOnlyList<GapSweep.DensityRow>? cachedDensityRows = null;

            IReadOnlyList<GapSweep.EditorPair> EnsurePairs()
            {
                cachedPairs ??= GapSweep.PairMatcher.DiscoverAll(repoRoot);
                return cachedPairs;
            }
            IReadOnlyList<GapSweep.DensityRow> EnsureDensityRows()
            {
                cachedDensityRows ??= GapSweep.ControlDensityScanner.Scan(EnsurePairs(), repoRoot);
                return cachedDensityRows;
            }

            return new (string, Func<string, int>)[]
            {
                ("density", path => RunDensitySweepShared(repoRoot, path, dryRun, EnsurePairs, EnsureDensityRows)),
                ("labels",  path => RunLabelsSweepShared (repoRoot, path, dryRun, EnsurePairs, EnsureDensityRows)),
                ("jumps",   path => RunJumpsSweep        (repoRoot, path, dryRun)),
                ("undo",    path => RunUndoSweep         (repoRoot, path, dryRun)),
                ("l10n",    path => RunL10nSweep         (repoRoot, path, dryRun, GapSweepLanguages)),
            };
        }

        /// <summary>
        /// Density sub-sweep variant that consumes the shared
        /// pairs / rows caches built by <see cref="BuildDefaultSubSweeps"/>.
        /// Behaviour is identical to <see cref="RunDensitySweep"/> for the
        /// single-flag CLI path; only the caching pattern differs.
        /// </summary>
        static int RunDensitySweepShared(
            string repoRoot,
            string outPath,
            bool dryRun,
            Func<IReadOnlyList<GapSweep.EditorPair>> ensurePairs,
            Func<IReadOnlyList<GapSweep.DensityRow>> ensureDensityRows)
        {
            if (dryRun)
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, ReportWriter.BuildFrontMatter("density", gitWorkingDir: repoRoot));
                Console.WriteLine("GAPSWEEP[density]: dry-run header written.");
                return 0;
            }

            var pairs = ensurePairs();
            Console.WriteLine($"GAPSWEEP[density]: discovered {pairs.Count} editor pairs.");
            var rows = ensureDensityRows();
            Console.WriteLine($"GAPSWEEP[density]: scanned {rows.Count} non-empty rows.");

            string body = GapSweep.ControlDensityScanner.FormatReport(rows);
            ReportWriter.WriteReport(outPath, "density", new[] { body }, gitWorkingDir: repoRoot);
            Console.WriteLine($"GAPSWEEP[density]: report written to {outPath}");
            return 0;
        }

        /// <summary>
        /// Labels sub-sweep variant that consumes the shared pairs / density
        /// rows caches so the composite path doesn't run the density scan
        /// twice. Behaviour is identical to <see cref="RunLabelsSweep"/> for
        /// the single-flag CLI path; only the caching pattern differs.
        /// </summary>
        static int RunLabelsSweepShared(
            string repoRoot,
            string outPath,
            bool dryRun,
            Func<IReadOnlyList<GapSweep.EditorPair>> ensurePairs,
            Func<IReadOnlyList<GapSweep.DensityRow>> ensureDensityRows)
        {
            if (dryRun)
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, ReportWriter.BuildFrontMatter("labels", gitWorkingDir: repoRoot));
                Console.WriteLine("GAPSWEEP[labels]: dry-run header written.");
                return 0;
            }

            var pairs = ensurePairs();
            Console.WriteLine($"GAPSWEEP[labels]: discovered {pairs.Count} editor pairs.");

            var rows = GapSweep.LabelDiffScanner.Scan(pairs);
            int pairsWithGap = rows.Count(r => r.WfOnlyLabels.Count > 0);
            Console.WriteLine($"GAPSWEEP[labels]: scanned {rows.Count} pairs with both files; {pairsWithGap} have >=1 WF-only label.");

            // Reuse the cached density rows that the density sub-sweep
            // already computed (or compute them now if labels runs first,
            // e.g. through SubSweepsOverride). The Func indirection keeps
            // the work lazy in the dry-run path above.
            var densityRows = ensureDensityRows();
            string? densityLink = GapSweep.LabelDiffScanner.FindLatestDensityReport(outPath);
            Console.WriteLine($"GAPSWEEP[labels]: density cross-link -> {densityLink ?? "(none found)"}");

            string body = GapSweep.LabelDiffScanner.FormatReport(rows, densityRows, densityLink);
            ReportWriter.WriteReport(outPath, "labels", new[] { body }, gitWorkingDir: repoRoot);
            Console.WriteLine($"GAPSWEEP[labels]: report written to {outPath}");
            return 0;
        }

        /// <summary>
        /// Test hook: when non-null, <see cref="RunAllSweep"/> uses this
        /// list of sub-sweeps instead of <see cref="BuildDefaultSubSweeps"/>.
        /// Tests use this to inject a deliberately-failing sweep and verify
        /// per-sweep try/catch isolation (one sweep throws → other sweeps
        /// still run and the composite still returns 0).
        ///
        /// Production code MUST NOT set this — the field is internal so
        /// only the same-assembly tests reach it via
        /// `InternalsVisibleTo("FEBuilderGBA.Avalonia.Tests")`.
        /// </summary>
        internal static IReadOnlyList<(string Name, Func<string, int> Run)>? SubSweepsOverride { get; set; }

        /// <summary>
        /// Walk up from the executable directory looking for `FEBuilderGBA.sln`.
        /// Falls back to the current working directory if no solution is found
        /// (e.g. running a published binary outside the source tree).
        /// </summary>
        static string FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
