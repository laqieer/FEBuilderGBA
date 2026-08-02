// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FEBuilderGBA.Core;
using FEBuilderGBA.SkiaSharp;

namespace FEBuilderGBA.CLI
{
    internal sealed class FontLibraryPublicationTestHooks
    {
        internal Action<string> AfterStagingValidation { get; set; }
        internal Action<string> AfterFinalStagingValidation { get; set; }
        internal Action<string> AfterMoveBeforeVerification { get; set; }
        internal Action<string> AfterPublishedValidationBeforeRelease
        {
            get;
            set;
        }
        internal Action<string> BeforeStagingCleanupFinalIdentityCheck
        {
            get;
            set;
        }
        internal Action<string> AfterStagingCleanupFinalIdentityCheck
        {
            get;
            set;
        }
    }

    static partial class Program
    {
        static readonly HashSet<string> FontLibraryAllowedFlags =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "--build-font-library",
                "--manifest",
                "--out",
                "--mode",
                "--report",
                "--job",
            };

        const int FontLibraryPngBytes = 188;

        static int RunBuildFontLibrary(Dictionary<string, string> argsDic)
        {
            if (!ValidateFontLibraryArgv(out string argvError))
                return FontLibraryError(argvError);
            foreach (string key in argsDic.Keys)
                if (!FontLibraryAllowedFlags.Contains(key))
                    return FontLibraryError("unknown option '" + key + "'.");

            if (!RequireFontLibraryValue(
                argsDic, "--manifest", out string manifestArg))
                return 1;
            if (!RequireFontLibraryValue(
                argsDic, "--out", out string outArg))
                return 1;
            string mode = argsDic.TryGetValue("--mode", out string modeValue)
                && !string.IsNullOrEmpty(modeValue)
                ? modeValue : "generate";
            if (argsDic.ContainsKey("--mode")
                && string.IsNullOrEmpty(modeValue))
                return FontLibraryError(
                    "--mode requires a non-empty value.");
            if (mode != "dry-run" && mode != "generate"
                && mode != "validate" && mode != "roundtrip")
                return FontLibraryError(
                    "--mode must be dry-run, generate, validate, or roundtrip.");
            string selectedJob =
                argsDic.TryGetValue("--job", out string jobValue)
                    && !string.IsNullOrEmpty(jobValue)
                    ? jobValue : null;
            string reportArg =
                argsDic.TryGetValue("--report", out string reportValue)
                    && !string.IsNullOrEmpty(reportValue)
                    ? reportValue : null;
            if (argsDic.ContainsKey("--job") && selectedJob == null)
                return FontLibraryError("--job requires a non-empty value.");
            if (argsDic.ContainsKey("--report") && reportArg == null)
                return FontLibraryError("--report requires a non-empty value.");

            if (BuildfilePathSafety.ContainsParentTraversal(manifestArg)
                || BuildfilePathSafety.ContainsParentTraversal(outArg)
                || (reportArg != null
                    && BuildfilePathSafety.ContainsParentTraversal(reportArg)))
            {
                return FontLibraryError(
                    "manifest/out/report paths must not contain '..' segments.",
                    FontLibraryFailureKind.Path);
            }

            string manifestFull;
            string outFull;
            string reportFull = null;
            try
            {
                manifestFull =
                    BuildfilePathSafety.NormalizeFullPath(manifestArg);
                outFull = BuildfilePathSafety.NormalizeFullPath(outArg);
                if (reportArg != null)
                    reportFull =
                        BuildfilePathSafety.NormalizeFullPath(reportArg);
            }
            catch (Exception ex)
            {
                return FontLibraryError(
                    "invalid path: " + ex.Message,
                    FontLibraryFailureKind.Path);
            }

            if (!File.Exists(manifestFull))
                return FontLibraryError(
                    "manifest file not found: " + manifestArg,
                    FontLibraryFailureKind.Io);
            if (IsReparse(manifestFull))
                return FontLibraryError(
                    "manifest must not be a reparse point/symlink.",
                    FontLibraryFailureKind.Path);

            string manifestPhysical;
            byte[] manifestBytes;
            try
            {
                EnsureNoReparsePathComponents(
                    manifestFull, includeFinal: true);
                manifestPhysical =
                    BuildfilePathSafety.ResolvePhysicalPath(manifestFull);
                manifestBytes = ReadRegularBounded(
                    manifestPhysical,
                    FontLibraryManifestCore.MaxManifestBytes,
                    "manifest");
            }
            catch (Exception ex)
            {
                return FontLibraryError(
                    "manifest must be a bounded regular file: " + ex.Message,
                    FontLibraryFailureKind.Io);
            }

            FontLibraryManifestParseResult parsed =
                FontLibraryManifestCore.Parse(manifestBytes);
            if (!parsed.Success)
                return FontLibraryError(
                    parsed.Error, FontLibraryFailureKind.Schema);
            string manifestSha256 =
                FontLibraryHashCore.ComputeSha256(manifestBytes);
            string manifestRoot = Path.GetDirectoryName(manifestPhysical);
            if (string.IsNullOrEmpty(manifestRoot))
                return FontLibraryError(
                    "manifest has no containing directory.",
                    FontLibraryFailureKind.Path);

            if (!TryPrepareOutputPath(
                outFull, mode, out string outputPhysical,
                out string outputParentPhysical, out string outputError))
                return FontLibraryError(
                    outputError, FontLibraryFailureKind.Path);
            if (!TryPrepareReportPath(
                reportFull,
                outputPhysical,
                mode,
                out string reportPublishFull,
                out string reportError))
                return FontLibraryError(
                    reportError, FontLibraryFailureKind.Path);

            if (!TryOpenExpectedGenerationReport(
                reportPublishFull,
                mode,
                out FileStream expectedGenerationReportStream,
                out ProjectionFileSystemSafety.OpenedRegularFileState?
                    expectedReportIdentity,
                out FontLibraryReport expectedGenerationReport,
                out string expectedReportError))
            {
                return FontLibraryResultError(
                    expectedReportError,
                    FontLibraryFailureKind.Tamper);
            }
            try
            {

            Console.Error.WriteLine(
                "font-library: loading and verifying schema-v1 inputs");
            if (!TryLoadFontLibraryInputs(
                parsed.Manifest,
                manifestRoot,
                manifestPhysical,
                manifestBytes,
                selectedJob,
                out FontLibraryPlannerInput plannerInput,
                out Dictionary<string, FontLibraryJobAssets> assets,
                out string inputError))
            {
                return FontLibraryError(
                    inputError, FontLibraryFailureKind.Provenance);
            }

            FontLibraryPlanResult planned = FontLibraryPlanner.Plan(
                parsed.Manifest, plannerInput, selectedJob);
            if (!planned.Success)
                return FontLibraryResultError(
                    planned.Error, planned.FailureKind);
            Console.Error.WriteLine(
                "font-library: planned "
                + planned.Plan.Jobs.Count + " job(s), "
                + planned.Plan.SlotCount + " row(s)");

            FontLibraryReport report;
            FileSystemEntryIdentity? publishedPackageIdentity = null;
            if (mode == "generate" || mode == "dry-run")
            {
                var buildInput = new FontLibraryBuildInput
                {
                    Plan = planned.Plan,
                    ManifestSha256 = manifestSha256,
                    FaceFactory = mode == "generate"
                        ? new SkiaFontLibraryFaceFactory()
                        : null,
                };
                foreach (KeyValuePair<string, FontLibraryJobAssets> asset in assets)
                    buildInput.Assets.Add(asset.Key, asset.Value);

                FontLibraryBuildResult built;
                if (mode == "dry-run")
                {
                    Console.Error.WriteLine(
                        "font-library: dry-run stops after provenance/plan/capacity");
                    built = FontLibraryBuilderCore.DryRun(buildInput);
                }
                else
                {
                    Console.Error.WriteLine(
                        "font-library: rasterizing with verified font bytes");
                    built = FontLibraryBuilderCore.Build(buildInput);
                }
                if (!built.Success)
                    return FontLibraryResultError(
                        built.Error, built.FailureKind);
                report = built.Report;

                if (mode == "generate")
                {
                    FontLibraryValidationResult memoryValidation =
                        FontLibraryPackageValidatorCore.ValidateWithOracle(
                            planned.Plan,
                            built.Package,
                            built.Report,
                            "generate",
                            manifestSha256);
                    if (!memoryValidation.Success)
                        return FontLibraryResultError(
                            memoryValidation.Error,
                            memoryValidation.FailureKind);
                    Console.Error.WriteLine(
                        "font-library: writing exclusive sibling staging tree");
                    if (!TryPublishFontLibrary(
                        planned.Plan,
                        built.Package,
                        built.Report,
                        outputPhysical,
                        outputParentPhysical,
                        out FileSystemEntryIdentity packageIdentity,
                        out report,
                        out string publishError))
                    {
                        return FontLibraryError(
                            publishError, FontLibraryFailureKind.Io);
                    }
                    publishedPackageIdentity = packageIdentity;
                    // Preserve the external finalized-tree oracle, not the
                    // embedded payload-only report or a validate-mode status.
                    report = built.Report;
                }
            }
            else
            {
                Console.Error.WriteLine(
                    "font-library: capturing existing package without rasterization");
                FontLibraryPackage existing;
                try
                {
                    existing = CaptureFontLibraryPackage(
                        outputPhysical,
                        planned.Plan,
                        expectedReportIdentity,
                        expectedGenerationReport?.FullTreeSha256);
                }
                catch (FontLibraryOperationException ex)
                {
                    return FontLibraryResultError(
                        "could not capture package: " + ex.Message,
                        ex.Kind);
                }
                catch (Exception ex)
                {
                    return FontLibraryError(
                        "could not capture package: " + ex.Message,
                        FontLibraryFailureKind.Io);
                }

                FontLibraryValidationResult validation =
                    mode == "roundtrip"
                        ? FontLibraryPackageValidatorCore.RoundTrip(
                            planned.Plan,
                            existing,
                            expectedGenerationReport,
                            manifestSha256)
                        : expectedGenerationReport == null
                            ? FontLibraryPackageValidatorCore.Validate(
                                planned.Plan,
                                existing,
                                mode,
                                manifestSha256)
                            : FontLibraryPackageValidatorCore
                                .ValidateWithOracle(
                                    planned.Plan,
                                    existing,
                                    expectedGenerationReport,
                                    mode,
                                    manifestSha256);
                if (!validation.Success)
                    return FontLibraryResultError(
                        validation.Error,
                        validation.FailureKind);
                report = validation.Report;
                if (expectedGenerationReport == null)
                {
                    Console.Error.WriteLine(
                        "font-library: WARNING: no --report oracle supplied; "
                        + "result proves internal consistency only and cannot "
                        + "detect coordinated package/report edits");
                }
            }

            string reportText =
                FontLibraryReportFormatter.Format(report);
            bool reportIsInput = reportPublishFull != null
                && (mode == "validate" || mode == "roundtrip");
            if (reportPublishFull == null || reportIsInput)
            {
                Console.Out.Write(reportText);
            }
            else
            {
                byte[] reportBytes = new UTF8Encoding(false).GetBytes(reportText);
                if (!TryPublishExternalFontLibraryReport(
                    reportBytes,
                    reportPublishFull,
                    mode == "generate" ? planned.Plan : null,
                    mode == "generate" ? report : null,
                    mode == "generate" ? outputPhysical : null,
                    publishedPackageIdentity,
                    beforeReportPublishForTest: null,
                    out string reportWriteError))
                {
                    return FontLibraryError(
                        "could not publish report: " + reportWriteError,
                        FontLibraryFailureKind.Io);
                }
                Console.Error.WriteLine(
                    "font-library: report written to " + reportPublishFull);
            }

            Console.Error.WriteLine(
                "font-library: " + mode + " succeeded");
            return 0;
            }
            finally
            {
                expectedGenerationReportStream?.Dispose();
            }
        }

        static bool TryLoadFontLibraryInputs(
            FontLibraryManifest manifest,
            string manifestRoot,
            string manifestPath,
            byte[] manifestBytes,
            string selectedJob,
            out FontLibraryPlannerInput plannerInput,
            out Dictionary<string, FontLibraryJobAssets> assets,
            out string error)
        {
            plannerInput = new FontLibraryPlannerInput();
            assets = new Dictionary<string, FontLibraryJobAssets>(
                StringComparer.Ordinal);
            error = "";
            try
            {
                var portablePaths =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
                using FileStream manifestOpened =
                    ProjectionFileSystemSafety
                        .OpenRegularFileForRead(manifestPath);
                byte[] retainedManifestBytes = ReadRegularBounded(
                    manifestOpened,
                    FontLibraryManifestCore.MaxManifestBytes,
                    "manifest");
                if (!FontLibraryHashCore.BytesEqual(
                    retainedManifestBytes, manifestBytes))
                    throw new IOException(
                        "manifest changed after it was parsed.");
                var openedAssets =
                    new List<(string Role, string Hash, string Path,
                        byte[] Data, FileStream Stream)>
                    {
                        (
                            "manifest",
                            FontLibraryHashCore.ComputeSha256(manifestBytes),
                            manifestPath,
                            manifestBytes,
                            manifestOpened),
                    };
                try
                {
                    var cache = new Dictionary<string, byte[]>(
                        StringComparer.Ordinal);
                    long totalInputBytes = 0;
                    foreach (FontLibraryJobManifest job in manifest.Jobs)
                    {
                        if (selectedJob != null
                            && !string.Equals(
                                selectedJob,
                                job.Id,
                                StringComparison.Ordinal))
                            continue;

                        if (job.Corpus != null)
                        {
                            byte[] corpus = ReadDeclaredAsset(
                                manifestRoot,
                                job.Corpus.Path,
                                job.Corpus.Sha256,
                                FontLibraryManifestCore.MaxCorpusBytes,
                                "corpus",
                                portablePaths,
                                openedAssets,
                                cache,
                                ref totalInputBytes);
                            plannerInput.Corpora.Add(
                                job.Id, DecodeStrictUtf8(corpus, "corpus"));
                        }
                        byte[] font = ReadDeclaredAsset(
                            manifestRoot,
                            job.Font.Path,
                            job.Font.Sha256,
                            FontLibraryManifestCore.MaxFontBytes,
                            "font",
                            portablePaths,
                            openedAssets,
                            cache,
                            ref totalInputBytes);
                        if (font.LongLength != job.Font.ByteLength)
                            throw new IOException(
                                "font byteLength does not match the verified file for "
                                + job.Font.Path + ".");
                        byte[] license = ReadDeclaredAsset(
                            manifestRoot,
                            job.License.LicenseFile,
                            job.License.Sha256,
                            FontLibraryManifestCore.MaxLicenseBytes,
                            "license",
                            portablePaths,
                            openedAssets,
                            cache,
                            ref totalInputBytes);
                        // Fail before Skia if the declared license is not strict UTF-8.
                        string licenseText =
                            DecodeStrictUtf8(license, "license");
                        if (string.IsNullOrWhiteSpace(licenseText))
                            throw new IOException("license text is empty.");
                        assets.Add(job.Id, new FontLibraryJobAssets
                        {
                            FontFileData = font,
                            LicenseFileData = license,
                        });
                    }
                    return true;
                }
                finally
                {
                    // Index zero is manifestOpened and is owned by its using
                    // declaration. Asset handles remain open through all
                    // cross-role comparisons, then close deterministically.
                    for (int i = 1; i < openedAssets.Count; i++)
                        openedAssets[i].Stream.Dispose();
                }
            }
            catch (Exception ex)
            {
                error = "input verification failed: " + ex.Message;
                return false;
            }
        }

        static byte[] ReadDeclaredAsset(
            string manifestRoot,
            string relative,
            string expectedHash,
            int cap,
            string role,
            Dictionary<string, string> portablePaths,
            List<(string Role, string Hash, string Path,
                byte[] Data, FileStream Stream)> openedAssets,
            Dictionary<string, byte[]> cache,
            ref long totalInputBytes)
        {
            if (portablePaths.TryGetValue(relative, out string priorSpelling))
            {
                if (!string.Equals(
                    priorSpelling, relative, StringComparison.Ordinal))
                    throw new IOException(
                        "declared paths collide by case: " + relative);
            }
            else
            {
                portablePaths.Add(relative, relative);
            }
            string cacheKey =
                role + "\0" + relative + "\0" + expectedHash;
            if (cache.TryGetValue(cacheKey, out byte[] cached))
                return cached;
            string lexical = Path.GetFullPath(
                Path.Combine(
                    manifestRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!BuildfilePathSafety.IsSameOrDescendantPath(
                lexical, manifestRoot))
                throw new IOException(
                    "declared path escapes the manifest directory: " + relative);
            if (!File.Exists(lexical))
                throw new FileNotFoundException(
                    "declared " + role + " does not exist", relative);
            if (IsReparse(lexical))
                throw new IOException(
                    "declared " + role
                    + " must not be a reparse point/symlink: " + relative);
            string physical =
                BuildfilePathSafety.ResolvePhysicalPath(lexical);
            if (!BuildfilePathSafety.IsSameOrDescendantPath(
                physical, manifestRoot))
                throw new IOException(
                    "declared path resolves outside the manifest directory: "
                    + relative);
            FileStream opened =
                ProjectionFileSystemSafety.OpenRegularFileForRead(physical);
            byte[] data;
            try
            {
                data = ReadRegularBounded(
                    opened,
                    cap,
                    role,
                    allowEmpty: role == "corpus");
            }
            catch
            {
                opened.Dispose();
                throw;
            }
            string actual = FontLibraryHashCore.ComputeSha256(data);
            if (!string.Equals(
                actual, expectedHash, StringComparison.Ordinal))
            {
                opened.Dispose();
                throw new IOException(
                    role + " SHA-256 mismatch for " + relative);
            }
            foreach (var prior in openedAssets)
            {
                if (!ProjectionFileSystemSafety.SameOpenedFile(
                    prior.Stream, opened))
                    continue;
                bool safeSharedRole =
                    string.Equals(prior.Role, role, StringComparison.Ordinal)
                    && string.Equals(
                        prior.Hash, expectedHash, StringComparison.Ordinal);
                if (!safeSharedRole)
                {
                    opened.Dispose();
                    throw new IOException(
                        "cross-role/hard-link alias is forbidden between '"
                        + prior.Path + "' and '" + relative + "'.");
                }
                opened.Dispose();
                cache.Add(cacheKey, prior.Data);
                return prior.Data;
            }
            openedAssets.Add((role, expectedHash, relative, data, opened));
            totalInputBytes = checked(totalInputBytes + data.LongLength);
            if (totalInputBytes > FontLibraryManifestCore.MaxInputBytesTotal)
                throw new IOException(
                    "combined unique input bytes exceed the "
                    + FontLibraryManifestCore.MaxInputBytesTotal
                    + "-byte cap.");
            cache.Add(cacheKey, data);
            return data;
        }

        static bool TryPublishFontLibrary(
            FontLibraryPlan plan,
            FontLibraryPackage package,
            FontLibraryReport generationReport,
            string outFull,
            string outputParentPhysical,
            out FileSystemEntryIdentity publishedIdentity,
            out FontLibraryReport report,
            out string error)
            => TryPublishFontLibrary(
                plan,
                package,
                generationReport,
                outFull,
                outputParentPhysical,
                null,
                out publishedIdentity,
                out report,
                out error);

        internal static bool TryPublishFontLibrary(
            FontLibraryPlan plan,
            FontLibraryPackage package,
            FontLibraryReport generationReport,
            string outFull,
            string outputParentPhysical,
            FontLibraryPublicationTestHooks hooks,
            out FontLibraryReport report,
            out string error)
            => TryPublishFontLibrary(
                plan,
                package,
                generationReport,
                outFull,
                outputParentPhysical,
                hooks,
                out _,
                out report,
                out error);

        internal static bool TryPublishFontLibrary(
            FontLibraryPlan plan,
            FontLibraryPackage package,
            FontLibraryReport generationReport,
            string outFull,
            string outputParentPhysical,
            FontLibraryPublicationTestHooks hooks,
            out FileSystemEntryIdentity publishedIdentity,
            out FontLibraryReport report,
            out string error)
        {
            publishedIdentity = default;
            report = null;
            error = "";
            string stage = null;
            FileSystemEntryIdentity stageIdentity = default;
            bool hasStageIdentity = false;
            bool movedToOutput = false;
            try
            {
                string outputName = Path.GetFileName(outFull);
                stage = BuildfileBuildCore.ReserveScratchDirectory(
                    outputParentPhysical,
                    "." + outputName + ".fontlib-staging-");
                stageIdentity =
                    ProjectionFileSystemSafety
                        .CaptureExistingFileSystemEntryIdentity(stage);
                hasStageIdentity = true;
                WritePackageTree(stage, package);

                CaptureAndValidateFontLibraryPublication(
                    stage,
                    stageIdentity,
                    plan,
                    generationReport,
                    "staging re-read");
                hooks?.AfterStagingValidation?.Invoke(stage);

                // Re-capture the complete identity-bound tree at the actual
                // publication boundary. This is intentionally independent of
                // the earlier durability validation.
                CaptureAndValidateFontLibraryPublication(
                    stage,
                    stageIdentity,
                    plan,
                    generationReport,
                    "final staging");
                hooks?.AfterFinalStagingValidation?.Invoke(stage);

                if (File.Exists(outFull) || Directory.Exists(outFull))
                    throw new IOException(
                        "output appeared before no-replace publication.");
                // The final root check narrows the pre-rename race. The
                // post-move identity/tree checks below remain authoritative
                // and retain, rather than delete, any mismatching output.
                ProjectionFileSystemSafety.VerifyPlainDirectoryIdentity(
                    stage, stageIdentity);
                BuildfileExportCore.PublishDirectoryNoReplace(stage, outFull);
                movedToOutput = true;
                stage = null;

                hooks?.AfterMoveBeforeVerification?.Invoke(outFull);
                CaptureAndValidateFontLibraryPublication(
                    outFull,
                    stageIdentity,
                    plan,
                    generationReport,
                    "published output");
                hooks?.AfterPublishedValidationBeforeRelease?.Invoke(outFull);
                CaptureAndValidateFontLibraryPublication(
                    outFull,
                    stageIdentity,
                    plan,
                    generationReport,
                    "published release");

                // Success and the caller-visible report are released only
                // after the moved directory identity and every output byte
                // have passed the complete on-disk oracle again.
                publishedIdentity = stageIdentity;
                report = generationReport;
                return true;
            }
            catch (Exception ex)
            {
                error = movedToOutput
                    ? "published output verification failed; output retained: "
                        + ex.Message
                    : ex.Message;
                return false;
            }
            finally
            {
                if (stage != null)
                {
                    if (!TryCleanupReservedStagingDirectory(
                        stage,
                        hasStageIdentity,
                        stageIdentity,
                        hooks?.BeforeStagingCleanupFinalIdentityCheck,
                        hooks?.AfterStagingCleanupFinalIdentityCheck,
                        out string stageCleanup))
                        error += "; staging cleanup failed: " + stageCleanup;
                }
            }
        }

        internal static bool TryRollbackPublishedFontLibrary(
            FontLibraryPlan plan,
            FontLibraryReport generationReport,
            string outputPath,
            FileSystemEntryIdentity expectedIdentity,
            out string error)
        {
            error = "";
            try
            {
                ProjectionFileSystemSafety.VerifyPlainDirectoryIdentity(
                    outputPath, expectedIdentity);
                CaptureAndValidateFontLibraryPublication(
                    outputPath,
                    expectedIdentity,
                    plan,
                    generationReport,
                    "report-publication rollback");
                return ProjectionFileSystemSafety
                    .TryDeleteReservedDirectoryIdentityBound(
                        outputPath,
                        expectedIdentity,
                        beforeFinalIdentityCheck: null,
                        afterFinalIdentityCheck: null,
                        out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryPublishExternalFontLibraryReport(
            byte[] reportBytes,
            string reportPath,
            FontLibraryPlan publishedPlan,
            FontLibraryReport publishedReport,
            string publishedPackagePath,
            FileSystemEntryIdentity? publishedPackageIdentity,
            Action<string> beforeReportPublishForTest,
            out string error)
        {
            beforeReportPublishForTest?.Invoke(reportPath);
            if (BuildfileBuildCore.PublishBytesNoReplace(
                reportBytes, reportPath, out error))
                return true;

            if (publishedPlan != null
                && publishedReport != null
                && !string.IsNullOrEmpty(publishedPackagePath)
                && publishedPackageIdentity.HasValue
                && Directory.Exists(publishedPackagePath)
                && !TryRollbackPublishedFontLibrary(
                    publishedPlan,
                    publishedReport,
                    publishedPackagePath,
                    publishedPackageIdentity.Value,
                    out string rollbackError))
            {
                error += "; published package retained because rollback "
                    + "could not prove ownership: " + rollbackError;
            }
            return false;
        }

        static FontLibraryValidationResult
            CaptureAndValidateFontLibraryPublication(
                string root,
                FileSystemEntryIdentity expectedRootIdentity,
                FontLibraryPlan plan,
                FontLibraryReport generationReport,
                string phase)
        {
            ProjectionFileSystemSafety.VerifyPlainDirectoryIdentity(
                root, expectedRootIdentity);
            FontLibraryPackage captured =
                CaptureFontLibraryPackage(
                    root,
                    plan,
                    externalReportIdentity: null,
                    expectedFullTreeSha256:
                        generationReport.FullTreeSha256,
                    expectedRootIdentity: expectedRootIdentity);
            FontLibraryValidationResult validation =
                FontLibraryPackageValidatorCore.ValidateWithOracle(
                    plan, captured, generationReport, "generate");
            if (!validation.Success)
                throw new IOException(
                    phase + " validation failed: "
                    + validation.Error);
            if (!string.Equals(
                    validation.Report.PayloadTreeSha256,
                    generationReport.PayloadTreeSha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    validation.Report.FullTreeSha256,
                    generationReport.FullTreeSha256,
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    phase + " package-tree hash mismatch.");
            }
            ProjectionFileSystemSafety.VerifyPlainDirectoryIdentity(
                root, expectedRootIdentity);
            return validation;
        }

        static bool TryCleanupReservedStagingDirectory(
            string path,
            bool hasExpectedIdentity,
            FileSystemEntryIdentity expectedIdentity,
            Action<string> beforeFinalIdentityCheck,
            Action<string> afterFinalIdentityCheck,
            out string error)
        {
            if (!hasExpectedIdentity)
            {
                error =
                    "reserved staging identity was not captured; cleanup refused";
                return false;
            }
            return ProjectionFileSystemSafety
                .TryDeleteReservedDirectoryIdentityBound(
                    path,
                    expectedIdentity,
                    beforeFinalIdentityCheck,
                    afterFinalIdentityCheck,
                    out error);
        }

        static void WritePackageTree(
            string stage,
            FontLibraryPackage package)
        {
            foreach (string relative in package.Files.Keys)
            {
                string[] parts = relative.Split('/');
                bool rootReport = parts.Length == 1
                    && string.Equals(
                        relative,
                        FontLibraryBuilderCore.PackageReportPath,
                        StringComparison.Ordinal);
                if (!rootReport && parts.Length != 2)
                    throw new IOException(
                        "Package output paths must be package-report.json or job/file.");
                string path;
                if (rootReport)
                {
                    path = Path.Combine(stage, parts[0]);
                }
                else
                {
                    string jobDir = Path.Combine(stage, parts[0]);
                    if (!Directory.Exists(jobDir))
                        Directory.CreateDirectory(jobDir);
                    path = Path.Combine(jobDir, parts[1]);
                }
                using var output = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough);
                byte[] data = package.Files[relative];
                output.Write(data, 0, data.Length);
                output.Flush(flushToDisk: true);
            }
        }

        static FontLibraryPackage CaptureFontLibraryPackage(
            string root,
            FontLibraryPlan plan,
            ProjectionFileSystemSafety.OpenedRegularFileState?
                externalReportIdentity = null,
            string expectedFullTreeSha256 = null,
            FileSystemEntryIdentity? expectedRootIdentity = null)
        {
            EnsureNoReparsePathComponents(root, includeFinal: true);
            int maxEntries = checked(plan.SlotCount + plan.Jobs.Count * 3 + 8);
            long maxBytes = checked(
                (long)plan.SlotCount * FontLibraryPngBytes
                + (long)plan.SlotCount * 512
                + (long)plan.Jobs.Count * 4096
                + FontLibraryReportFormatter.MaxReportBytes);
            ProjectionTreeSnapshot snapshot =
                expectedRootIdentity.HasValue
                    ? ProjectionTreeSnapshotReader.CaptureIdentityBound(
                        root,
                        Math.Max(1, maxEntries),
                        Math.Max(1, maxBytes),
                        FontLibraryManifestCore.MaxFontBytes,
                        beforeFileOpen: null,
                        disallowedFileIdentity: externalReportIdentity,
                        expectedRootIdentity:
                            expectedRootIdentity.Value)
                    : ProjectionTreeSnapshotReader.CaptureIdentityBound(
                        root,
                        Math.Max(1, maxEntries),
                        Math.Max(1, maxBytes),
                        FontLibraryManifestCore.MaxFontBytes,
                        beforeFileOpen: null,
                        disallowedFileIdentity: externalReportIdentity);
            var package = new FontLibraryPackage();
            foreach (ProjectionTreeSnapshotFile file in snapshot.Files)
            {
                string path = file.RelativePath.Replace('\\', '/');
                if (!package.Files.TryAdd(path, file.Data))
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Validation,
                        "Package contains a duplicate file: " + path);
            }

            // Safety/cap/identity-bound capture necessarily precedes hashing,
            // but the immutable external full-tree oracle precedes every
            // package-shape/content check below.
            if (expectedFullTreeSha256 != null
                && !string.Equals(
                    expectedFullTreeSha256,
                    FontLibraryHashCore.ComputeTreeSha256(package.Files),
                    StringComparison.Ordinal))
            {
                throw new FontLibraryOperationException(
                    FontLibraryFailureKind.Tamper,
                    "Immutable external full-tree hash does not match the finalized package.");
            }

            var caseFiles =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in package.Files.Keys)
            {
                if (!caseFiles.Add(path))
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Validation,
                        "Package contains a case-colliding file: " + path);
            }

            var expectedDirs = new HashSet<string>(StringComparer.Ordinal);
            foreach (FontLibraryPlannedJob job in plan.Jobs)
                expectedDirs.Add(job.Manifest.Id);
            var seenDirs = new HashSet<string>(StringComparer.Ordinal);
            var caseDirs =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in snapshot.Directories)
            {
                string dir = raw.Replace('\\', '/');
                if (!expectedDirs.Contains(dir)
                    || !seenDirs.Add(dir)
                    || !caseDirs.Add(dir))
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Validation,
                        "Package contains an unexpected/colliding directory: "
                        + dir);
            }
            if (seenDirs.Count != expectedDirs.Count)
                throw new FontLibraryOperationException(
                    FontLibraryFailureKind.Validation,
                    "Package is missing one or more job directories.");
            return package;
        }

        static bool TryPrepareOutputPath(
            string outFull,
            string mode,
            out string outputPhysical,
            out string outputParentPhysical,
            out string error)
        {
            outputPhysical = null;
            outputParentPhysical = null;
            error = "";
            try
            {
                string parent = Path.GetDirectoryName(outFull);
                if (string.IsNullOrEmpty(parent)
                    || !Directory.Exists(parent))
                {
                    error =
                        "output parent must be an existing directory.";
                    return false;
                }
                if (IsReparse(parent))
                {
                    error =
                        "output parent must not be a reparse point/symlink.";
                    return false;
                }
                EnsureNoReparsePathComponents(
                    parent, includeFinal: true);
                outputParentPhysical =
                    BuildfilePathSafety.ResolvePhysicalPath(parent);
                string leaf = Path.GetFileName(outFull);
                if (string.IsNullOrEmpty(leaf))
                {
                    error = "output must not be a filesystem root.";
                    return false;
                }
                string physicalCandidate =
                    Path.Combine(outputParentPhysical, leaf);
                if (mode == "validate" || mode == "roundtrip")
                {
                    if (!Directory.Exists(outFull)
                        || File.Exists(outFull)
                        || IsReparse(outFull))
                    {
                        error =
                            "validate/roundtrip output must be an existing plain directory.";
                        return false;
                    }
                    EnsureNoReparsePathComponents(
                        outFull, includeFinal: true);
                    outputPhysical =
                        BuildfilePathSafety.ResolvePhysicalPath(outFull);
                }
                else
                {
                    if (File.Exists(outFull) || Directory.Exists(outFull)
                        || File.Exists(physicalCandidate)
                        || Directory.Exists(physicalCandidate))
                    {
                        error =
                            "generate/dry-run output path must not already exist.";
                        return false;
                    }
                    outputPhysical = physicalCandidate;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "invalid output path: " + ex.Message;
                return false;
            }
        }

        static bool TryPrepareReportPath(
            string reportFull,
            string outputPhysical,
            string mode,
            out string reportPhysical,
            out string error)
        {
            reportPhysical = null;
            error = "";
            if (reportFull == null) return true;
            try
            {
                string parent = Path.GetDirectoryName(reportFull);
                if (string.IsNullOrEmpty(parent)
                    || !Directory.Exists(parent)
                    || IsReparse(parent))
                {
                    error =
                        "report parent must be an existing plain directory.";
                    return false;
                }
                EnsureNoReparsePathComponents(
                    parent, includeFinal: true);
                bool reportIsOracle =
                    mode == "validate" || mode == "roundtrip";
                if (reportIsOracle)
                {
                    if (!File.Exists(reportFull)
                        || Directory.Exists(reportFull)
                        || IsReparse(reportFull))
                    {
                        error =
                            "validate/roundtrip --report must be an existing plain generation-report file.";
                        return false;
                    }
                    EnsureNoReparsePathComponents(
                        reportFull, includeFinal: true);
                    reportPhysical =
                        BuildfilePathSafety.ResolvePhysicalPath(reportFull);
                }
                else
                {
                    if (File.Exists(reportFull)
                        || Directory.Exists(reportFull))
                    {
                        error = "report path already exists.";
                        return false;
                    }
                    string reportParentPhysical =
                        BuildfilePathSafety.ResolvePhysicalPath(parent);
                    reportPhysical = Path.Combine(
                        reportParentPhysical, Path.GetFileName(reportFull));
                    if (File.Exists(reportPhysical)
                        || Directory.Exists(reportPhysical))
                    {
                        error =
                            "report path already exists through a filesystem alias.";
                        return false;
                    }
                }
                if (IsSameOrDescendantPhysicalCandidate(
                    reportPhysical, outputPhysical))
                {
                    error =
                        "report must be outside the package output tree.";
                    return false;
                }
                return true;
            }

            catch (Exception ex)
            {
                error = "invalid report path: " + ex.Message;
                return false;
            }
        }

        static bool TryOpenExpectedGenerationReport(
            string reportPhysical,
            string mode,
            out FileStream stream,
            out ProjectionFileSystemSafety.OpenedRegularFileState?
                identity,
            out FontLibraryReport report,
            out string error)
        {
            stream = null;
            identity = null;
            report = null;
            error = "";
            if (reportPhysical == null
                || (mode != "validate" && mode != "roundtrip"))
                return true;
            try
            {
                stream = ProjectionFileSystemSafety.OpenRegularFileForRead(
                    reportPhysical);
                byte[] bytes =
                    ProjectionFileSystemSafety
                        .ReadOpenedRegularFileBoundedStable(
                            stream,
                            FontLibraryReportFormatter.MaxReportBytes,
                            "external generation report",
                            rejectHardLinks: true,
                            out ProjectionFileSystemSafety
                                .OpenedRegularFileState openedIdentity);
                identity = openedIdentity;
                if (!FontLibraryReportFormatter.TryParse(
                    bytes,
                    out report,
                    out string parseError))
                {
                    throw new FormatException(
                        "external generation report is invalid: "
                        + parseError);
                }
                if (string.IsNullOrEmpty(report.FullTreeSha256))
                    throw new FormatException(
                        "external generation report has no fullTreeSha256.");
                return true;
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                stream = null;
                identity = null;
                report = null;
                error =
                    "could not read external generation report: "
                    + ex.Message;
                return false;
            }
        }

        static void EnsureNoReparsePathComponents(
            string path,
            bool includeFinal)
        {
            string full = Path.GetFullPath(path);
            string inspected = includeFinal
                ? full
                : Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(inspected))
                throw new IOException("Path has no inspectable parent.");
            string root = Path.GetPathRoot(inspected);
            if (string.IsNullOrEmpty(root))
                throw new IOException("Path is not fully rooted.");
            string current = root;
            string remainder = inspected.Substring(root.Length);
            foreach (string component in remainder.Split(
                new[]
                {
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar,
                },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        "Symlink/reparse path components are forbidden: "
                        + current);
                }
            }
        }

        static bool IsSameOrDescendantPhysicalCandidate(
            string candidate,
            string root)
        {
            string normalizedCandidate =
                BuildfilePathSafety.NormalizeFullPath(candidate);
            string normalizedRoot =
                BuildfilePathSafety.NormalizeFullPath(root);
            StringComparison comparison =
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            if (string.Equals(
                normalizedCandidate, normalizedRoot, comparison))
                return true;
            string prefix = normalizedRoot.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal)
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(prefix, comparison);
        }

        static byte[] ReadRegularBounded(
            string path,
            int cap,
            string description,
            bool allowEmpty = false)
        {
            using FileStream stream =
                ProjectionFileSystemSafety.OpenRegularFileForRead(path);
            return ReadRegularBounded(
                stream, cap, description, allowEmpty);
        }

        static byte[] ReadRegularBounded(
            FileStream stream,
            int cap,
            string description,
            bool allowEmpty = false)
        {
            if ((!allowEmpty && stream.Length < 1) || stream.Length > cap)
                throw new IOException(
                    description + " size is outside "
                    + (allowEmpty ? "0.." : "1..")
                    + cap + " bytes.");
            int length = checked((int)stream.Length);
            var data = new byte[length];
            int offset = 0;
            while (offset < data.Length)
            {
                int read = stream.Read(data, offset, data.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException(
                        description + " changed/truncated while reading.");
                offset += read;
            }
            if (stream.ReadByte() != -1)
                throw new IOException(
                    description + " grew while reading.");
            return data;
        }

        static string DecodeStrictUtf8(byte[] data, string description)
        {
            int offset = data.Length >= 3
                && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF
                ? 3 : 0;
            try
            {
                return new UTF8Encoding(false, true).GetString(
                    data, offset, data.Length - offset);
            }
            catch (DecoderFallbackException ex)
            {
                throw new IOException(
                    description + " is not strict UTF-8.", ex);
            }
        }

        static bool IsReparse(string path)
        {
            try
            {
                return (File.GetAttributes(path)
                    & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        static bool RequireFontLibraryValue(
            Dictionary<string, string> args,
            string name,
            out string value)
        {
            if (!args.TryGetValue(name, out value)
                || string.IsNullOrEmpty(value))
            {
                FontLibraryError(name + " requires a non-empty value.");
                return false;
            }
            return true;
        }

        static bool ValidateFontLibraryArgv(out string error)
        {
            error = "";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < RawArgs.Length; i++)
            {
                string token = RawArgs[i];
                if (!token.StartsWith("--", StringComparison.Ordinal))
                {
                    error = "unexpected positional argument '" + token + "'.";
                    return false;
                }
                int equals = token.IndexOf('=');
                string key = equals >= 0
                    ? token.Substring(0, equals) : token;
                if (!FontLibraryAllowedFlags.Contains(key))
                {
                    error = "unknown option '" + key + "'.";
                    return false;
                }
                if (!seen.Add(key))
                {
                    error = "duplicate option '" + key + "'.";
                    return false;
                }
                if (key == "--build-font-library")
                {
                    if (equals >= 0)
                    {
                        error =
                            "--build-font-library does not accept a value.";
                        return false;
                    }
                    continue;
                }
                if (equals < 0)
                {
                    if (i + 1 >= RawArgs.Length
                        || RawArgs[i + 1].StartsWith(
                            "-", StringComparison.Ordinal))
                    {
                        error = key + " requires a value.";
                        return false;
                    }
                    i++;
                }
            }
            return true;
        }

        static int FontLibraryError(
            string error,
            FontLibraryFailureKind kind = FontLibraryFailureKind.Usage)
        {
            Console.Error.WriteLine(
                "Error: --build-font-library: " + error);
            WriteFontLibraryFailureReport(
                kind, error);
            return FontLibraryFailure.ExitCode(kind);
        }

        static int FontLibraryResultError(
            string error,
            FontLibraryFailureKind kind)
        {
            Console.Error.WriteLine(
                "Error: --build-font-library: " + error);
            WriteFontLibraryFailureReport(kind, error);
            return FontLibraryFailure.ExitCode(kind);
        }

        static void WriteFontLibraryFailureReport(
            FontLibraryFailureKind kind,
            string error)
        {
            var report = new FontLibraryReport
            {
                Mode = "error",
                Oracle = "failure",
            };
            report.Outcomes.Add(new FontLibraryOutcomeReport
            {
                Kind = kind.ToString(),
                Message = BoundFontLibraryOutcomeMessage(error),
            });
            Console.Out.Write(FontLibraryReportFormatter.Format(report));
        }

        static string BoundFontLibraryOutcomeMessage(string message)
        {
            const int maxCharacters = 4096;
            if (string.IsNullOrEmpty(message)
                || message.Length <= maxCharacters)
                return message ?? "";
            int length = maxCharacters;
            if (char.IsHighSurrogate(message[length - 1])
                && length < message.Length
                && char.IsLowSurrogate(message[length]))
                length--;
            return message.Substring(0, length);
        }
    }
}
