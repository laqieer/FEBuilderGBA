// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using FEBuilderGBA.SkiaSharp;
using FEBuilderGBA.SharedTest;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Real-process coverage for all four no-ROM font-library CLI modes.
    /// </summary>
    public sealed class FontLibraryCliTests
    {
        const string TuffySha256 =
            "e4818f18c471c4a6a7f1d5f80c7a0ef0fd7b95b63364fd48f15768c449d8f958";
        const string TuffyLicenseSha256 =
            "4ecde2d1a2a687ed1ea32dc15c99bfd7748aa55e9db57a75cc520250d38338a4";
        const string NotoSha256 =
            "c6571b2b9ee46fbc638b2a9f6ab0733a22a6811d71da572b822871ea3ff4dd03";
        const string NotoLicenseSha256 =
            "ab87c7448ce42cb84654b694b2c0f997ebe45415ef8f05561af7b9d1b62cde45";
        const string CorpusSha256 =
            "a0c7716669b5ded0d8051abfc232b828d489f7d889b2928eefcf43f0b8f495d6";
        const string MultiJobPayloadTreeSha256 =
            "2db9c593b6f854bf0519dfa705141f03f6687e608e82583ceae2f58708352021";
        const string MultiJobFullTreeSha256 =
            "676177f099de56ee408f0ba7ef854188f9006221670ec8773d7560b72814e837";

        [Fact]
        public void Cli_FourModes_MultiJob_NoReplaceAndDeterministicTree()
        {
            string? cli = ResolveCliPath(out string cliProbes);
            if (cli == null)
            {
                bool requiredX64 =
                    (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    && RuntimeInformation.ProcessArchitecture
                        == Architecture.X64;
                Assert.False(
                    requiredX64,
                    "The real CLI apphost/DLL is mandatory on "
                    + "Windows/Linux x64. Set FEBUILDERGBA_CLI_EXE or "
                    + "build Debug/Release.\n" + cliProbes);
                return; // macOS arm64 uses the structural Core coverage.
            }

            string root = Path.Combine(
                Path.GetTempPath(),
                "febuilder-font-library-cli-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string fontSource = Path.Combine(
                    AppContext.BaseDirectory,
                    "Fonts",
                    "Tuffy-Regular.ttf");
                string licenseSource = Path.Combine(
                    AppContext.BaseDirectory,
                    "Fonts",
                    "OFL.txt");
                string notoSource = Path.Combine(
                    AppContext.BaseDirectory,
                    "Fonts",
                    "NotoSansCJKsc-Subset.otf");
                string notoLicenseSource = Path.Combine(
                    AppContext.BaseDirectory,
                    "Fonts",
                    "Noto-OFL.txt");
                Assert.True(File.Exists(fontSource));
                Assert.True(File.Exists(licenseSource));
                Assert.True(File.Exists(notoSource));
                Assert.True(File.Exists(notoLicenseSource));
                string fontPath = Path.Combine(root, "Tuffy-Regular.ttf");
                string licensePath = Path.Combine(root, "OFL.txt");
                string notoPath = Path.Combine(
                    root, "NotoSansCJKsc-Subset.otf");
                string notoLicensePath = Path.Combine(
                    root, "Noto-OFL.txt");
                string corpusPath = Path.Combine(root, "zh-corpus.txt");
                File.Copy(fontSource, fontPath);
                File.Copy(licenseSource, licensePath);
                File.Copy(notoSource, notoPath);
                File.Copy(notoLicenseSource, notoLicensePath);
                File.WriteAllText(
                    corpusPath, "\u4F60", new UTF8Encoding(false));
                Assert.Equal(
                    TuffySha256,
                    FontLibraryHashCore.ComputeSha256(
                        File.ReadAllBytes(fontPath)));
                Assert.Equal(
                    TuffyLicenseSha256,
                    FontLibraryHashCore.ComputeSha256(
                        File.ReadAllBytes(licensePath)));
                Assert.Equal(
                    NotoSha256,
                    FontLibraryHashCore.ComputeSha256(
                        File.ReadAllBytes(notoPath)));
                Assert.Equal(
                    NotoLicenseSha256,
                    FontLibraryHashCore.ComputeSha256(
                        File.ReadAllBytes(notoLicensePath)));
                Assert.Equal(
                    CorpusSha256,
                    FontLibraryHashCore.ComputeSha256(
                        File.ReadAllBytes(corpusPath)));
                var faceFactory = new SkiaFontLibraryFaceFactory();
                Assert.True(faceFactory.TryOpenFace(
                    new FontLibraryFaceSpec
                    {
                        FontFileData = File.ReadAllBytes(notoPath),
                        ExpectedFamily = "Noto Sans CJK SC",
                        ExpectedVersion =
                            "Version 2.004;hotconv 1.0.118;makeotfexe 2.5.65603",
                        Size = 12,
                    },
                    out IFontLibraryFace notoFace,
                    out string notoFaceError),
                    notoFaceError);
                using (notoFace)
                    Assert.True(notoFace.HasGlyph(0x65E5));
                string manifest = Path.Combine(root, "library.json");
                File.WriteAllText(
                    manifest,
                    MultiJobManifest(),
                    new UTF8Encoding(false));

                string dryOut = Path.Combine(root, "dry-output");
                ProcessResult dry = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", dryOut,
                    "--mode", "dry-run");
                AssertSuccess(dry);
                Assert.False(
                    File.Exists(dryOut) || Directory.Exists(dryOut));
                Assert.Empty(Directory.EnumerateFileSystemEntries(
                    root,
                    "*.fontlib-staging-*",
                    SearchOption.TopDirectoryOnly));
                Assert.Contains("\"mode\":\"dry-run\"", dry.Stdout);
                Assert.Contains("\"id\":\"alpha\"", dry.Stdout);
                Assert.Contains("\"id\":\"kappa\"", dry.Stdout);
                Assert.Contains("\"id\":\"zeta\"", dry.Stdout);
                Assert.Contains("font-library:", dry.Stderr);

                string selectedOut = Path.Combine(root, "selected-output");
                ProcessResult selected = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", selectedOut,
                    "--mode", "dry-run",
                    "--job", "alpha");
                AssertSuccess(selected);
                Assert.Contains("\"id\":\"alpha\"", selected.Stdout);
                Assert.DoesNotContain("\"id\":\"zeta\"", selected.Stdout);

                if (RuntimeInformation.ProcessArchitecture
                        == Architecture.X64
                    && (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)))
                {
                    ProcessResult selectedGenerate = Run(
                        cli,
                        "--build-font-library",
                        "--manifest", manifest,
                        "--out", selectedOut,
                        "--mode", "generate",
                        "--job", "alpha");
                    AssertSuccess(selectedGenerate);
                    AssertAlphaPackageMatchesRenderGoldens(
                        selectedOut, selectedGenerate.Stdout);
                }

                string reportPath = Path.Combine(root, "report.json");
                ProcessResult reportRun = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", Path.Combine(root, "report-dry-output"),
                    "--mode", "dry-run",
                    "--report", reportPath);
                AssertSuccess(reportRun);
                Assert.Equal("", reportRun.Stdout);
                Assert.True(File.Exists(reportPath));
                Assert.Contains(
                    "\"payloadTreeSha256\":",
                    File.ReadAllText(reportPath));
                Assert.Contains(
                    "\"fullTreeSha256\":\"\"",
                    File.ReadAllText(reportPath));
                ProcessResult reportNoReplace = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", Path.Combine(root, "report-dry-output-2"),
                    "--mode", "dry-run",
                    "--report", reportPath);
                Assert.Equal(1, reportNoReplace.ExitCode);

                string outputReportAlias =
                    Path.Combine(root, "output-report-alias");
                ProcessResult aliasedOutputAndReport = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", outputReportAlias,
                    "--mode", "generate",
                    "--report", outputReportAlias);
                Assert.Equal(1, aliasedOutputAndReport.ExitCode);
                Assert.Contains(
                    "outside the package output tree",
                    aliasedOutputAndReport.Stderr);
                Assert.False(File.Exists(outputReportAlias));
                Assert.False(Directory.Exists(outputReportAlias));

                string generated = Path.Combine(root, "generated");
                string generationReportPath =
                    Path.Combine(root, "generation-report.json");
                ProcessResult generate = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generated,
                    "--mode", "generate",
                    "--report", generationReportPath);
                AssertSuccess(generate);
                Assert.Equal("", generate.Stdout);
                string generationReportText =
                    File.ReadAllText(generationReportPath);
                Assert.True(Directory.Exists(
                    Path.Combine(generated, "alpha")));
                Assert.True(Directory.Exists(
                    Path.Combine(generated, "kappa")));
                Assert.True(Directory.Exists(
                    Path.Combine(generated, "zeta")));
                Assert.Equal(
                    6,
                    Directory.GetFiles(
                        generated, "*.png",
                        SearchOption.AllDirectories).Length);
                Assert.True(File.Exists(Path.Combine(
                    generated, "kappa", "text_93FA.png")));
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    && RuntimeInformation.ProcessArchitecture
                        == Architecture.X64)
                {
                    // A real Noto ZH PNG proves the CLI process loaded the
                    // pinned Linux native Skia asset and rasterized through it.
                    Assert.True(File.Exists(Path.Combine(
                        generated, "zeta", "item_8181.png")));
                }
                string embeddedReportText = File.ReadAllText(Path.Combine(
                    generated,
                    FontLibraryBuilderCore.PackageReportPath));
                Assert.Contains(
                    "\"payloadTreeSha256\":", embeddedReportText);
                Assert.DoesNotContain(
                    "\"fullTreeSha256\":", embeddedReportText);
                Assert.Contains(
                    "\"fullTreeSha256\":", generationReportText);
                Assert.True(FontLibraryReportFormatter.TryParse(
                    File.ReadAllBytes(generationReportPath),
                    out FontLibraryReport generationReport,
                    out string generationReportError),
                    generationReportError);
                Assert.Equal(3, generationReport.Jobs.Count);
                Assert.Equal(64, generationReport.ManifestSha256.Length);
                Assert.Empty(generationReport.Outcomes);
                Assert.All(generationReport.Jobs, job =>
                {
                    Assert.NotEmpty(job.Locale);
                    Assert.Contains(job.Format, new[]
                    {
                        "main-16x16",
                        "zh-16x13",
                    });
                    Assert.Equal(16, job.NativeWidth);
                    Assert.Equal(
                        job.Format == "main-16x16" ? 16 : 13,
                        job.NativeHeight);
                    Assert.Equal(16, job.PngWidth);
                    Assert.Equal(16, job.PngHeight);
                    Assert.True(job.FontByteLength > 0);
                    Assert.NotEmpty(job.FontFamily);
                    Assert.NotEmpty(job.FontVersion);
                    Assert.Equal(64, job.FontSha256.Length);
                    Assert.NotEmpty(job.FontSourceUrl);
                    Assert.Equal("OFL-1.1", job.LicenseId);
                    Assert.NotEmpty(job.LicenseFile);
                    Assert.Equal(64, job.LicenseSha256.Length);
                    Assert.NotEmpty(job.LicenseSourceUrl);
                    Assert.Equal(64, job.PackedSha256.Length);
                    Assert.Equal(64, job.PngSha256.Length);
                    Assert.Equal(job.RowCount, job.Glyphs.Count);
                    Assert.All(job.Glyphs, glyph =>
                    {
                        Assert.True(glyph.UnicodeScalar > 0);
                        Assert.NotEmpty(glyph.Character);
                        Assert.Contains(glyph.Style, new[]
                        {
                            "item",
                            "text",
                        });
                        Assert.InRange(glyph.Width, 1, 16);
                        Assert.Equal(16, glyph.NativeWidth);
                        Assert.Equal(job.NativeHeight, glyph.NativeHeight);
                        Assert.Equal(16, glyph.PngWidth);
                        Assert.Equal(16, glyph.PngHeight);
                        Assert.NotEmpty(glyph.Filename);
                        Assert.Equal(64, glyph.PackedSha256.Length);
                        Assert.Equal(64, glyph.PngSha256.Length);
                    });
                });
                Assert.Equal(64, generationReport.PayloadTreeSha256.Length);
                Assert.Equal(64, generationReport.FullTreeSha256.Length);
                if (RuntimeInformation.ProcessArchitecture
                        == Architecture.X64
                    && (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)))
                {
                    Assert.True(
                        string.Equals(
                            MultiJobPayloadTreeSha256,
                            generationReport.PayloadTreeSha256,
                            StringComparison.Ordinal)
                        && string.Equals(
                            MultiJobFullTreeSha256,
                            generationReport.FullTreeSha256,
                            StringComparison.Ordinal),
                        "Update independent CLI goldens from actual output:"
                        + "\npayload="
                        + generationReport.PayloadTreeSha256
                        + "\nfull="
                        + generationReport.FullTreeSha256);
                }

                ProcessResult generateNoReplace = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generated,
                    "--mode", "generate");
                Assert.Equal(1, generateNoReplace.ExitCode);

                ProcessResult validate = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generated,
                    "--mode", "validate");
                AssertSuccess(validate);
                Assert.Contains("\"mode\":\"validate\"", validate.Stdout);
                Assert.Contains(
                    "internal consistency only", validate.Stderr);
                ProcessResult validateWithOracle = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generated,
                    "--mode", "validate",
                    "--report", generationReportPath);
                AssertSuccess(validateWithOracle);
                Assert.Contains(
                    "\"oracle\":\"immutable-external-report\"",
                    validateWithOracle.Stdout);
                string changedGenerationManifest =
                    Path.Combine(root, "changed-generation.json");
                File.WriteAllText(
                    changedGenerationManifest,
                    File.ReadAllText(manifest)
                        .Replace(
                            "\"size\":12",
                            "\"size\":13",
                            StringComparison.Ordinal)
                        .Replace(
                            "\"verticalOffset\":0",
                            "\"verticalOffset\":1",
                            StringComparison.Ordinal),
                    new UTF8Encoding(false));
                ProcessResult changedGenerationInput = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", changedGenerationManifest,
                    "--out", generated,
                    "--mode", "validate",
                    "--report", generationReportPath);
                Assert.Equal(2, changedGenerationInput.ExitCode);
                Assert.Contains(
                    "manifest SHA-256",
                    changedGenerationInput.Stderr);
                AssertFailureOutcome(
                    changedGenerationInput, "Tamper");
                ProcessResult reportInsideTree = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generated,
                    "--mode", "validate",
                    "--report", Path.Combine(
                        generated,
                        FontLibraryBuilderCore.PackageReportPath));
                Assert.Equal(1, reportInsideTree.ExitCode);
                Assert.Contains(
                    "outside the package output tree",
                    reportInsideTree.Stderr);

                string reportHardLink =
                    Path.Combine(root, "report-hard-link.json");
                Assert.True(CreateHardLink(
                    reportHardLink, generationReportPath));
                try
                {
                    ProcessResult linkedReport = Run(
                        cli,
                        "--build-font-library",
                        "--manifest", manifest,
                        "--out", generated,
                        "--mode", "validate",
                        "--report", reportHardLink);
                    Assert.Equal(2, linkedReport.ExitCode);
                    Assert.Contains("Hard-linked", linkedReport.Stderr);
                }
                finally
                {
                    File.Delete(reportHardLink);
                }

                string packageFile = Path.Combine(
                    generated, "alpha", "text_41.png");
                string packageHardLink =
                    Path.Combine(root, "package-hard-link.png");
                Assert.True(CreateHardLink(packageHardLink, packageFile));
                try
                {
                    ProcessResult linkedPackage = Run(
                        cli,
                        "--build-font-library",
                        "--manifest", manifest,
                        "--out", generated,
                        "--mode", "validate");
                    Assert.Equal(1, linkedPackage.ExitCode);
                    Assert.Contains("Hard-linked", linkedPackage.Stderr);
                }
                finally
                {
                    File.Delete(packageHardLink);
                }

                string packageSymlinkTree =
                    Path.Combine(root, "package-symlink-tree");
                CopyTree(generated, packageSymlinkTree);
                string packageSymlink = Path.Combine(
                    packageSymlinkTree, "alpha", "text_41.png");
                File.Delete(packageSymlink);
                if (OperatingSystem.IsWindows())
                {
                    string reparseTarget =
                        Path.Combine(root, "package-reparse-target");
                    Directory.CreateDirectory(reparseTarget);
                    CreateDirectoryAlias(packageSymlink, reparseTarget);
                }
                else
                {
                    File.CreateSymbolicLink(packageSymlink, packageFile);
                }
                ProcessResult linkedPackageEntry = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", packageSymlinkTree,
                    "--mode", "validate");
                Assert.Equal(1, linkedPackageEntry.ExitCode);
                Assert.True(
                    linkedPackageEntry.Stderr.Contains(
                        "symlink", StringComparison.OrdinalIgnoreCase)
                    || linkedPackageEntry.Stderr.Contains(
                        "following links", StringComparison.OrdinalIgnoreCase),
                    linkedPackageEntry.Stderr);
                if (OperatingSystem.IsWindows())
                    Directory.Delete(packageSymlink);
                else
                    File.Delete(packageSymlink);

                string outputAncestorLink =
                    Path.Combine(root, "output-ancestor-link");
                CreateDirectoryAlias(outputAncestorLink, root);
                ProcessResult linkedOutputAncestor = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", Path.Combine(
                        outputAncestorLink, "generated"),
                    "--mode", "validate");
                Assert.Equal(1, linkedOutputAncestor.ExitCode);
                Assert.Contains(
                    "reparse point/symlink",
                    linkedOutputAncestor.Stderr);
                Directory.Delete(outputAncestorLink);

                string reportSymlink =
                    Path.Combine(root, "report-symlink.json");
                if (OperatingSystem.IsWindows())
                    CreateDirectoryAlias(reportSymlink, root);
                else
                    File.CreateSymbolicLink(
                        reportSymlink, generationReportPath);
                ProcessResult linkedReportEntry = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generated,
                    "--mode", "validate",
                    "--report", reportSymlink);
                Assert.Equal(1, linkedReportEntry.ExitCode);
                Assert.Contains(
                    "plain generation-report file",
                    linkedReportEntry.Stderr);
                if (OperatingSystem.IsWindows())
                    Directory.Delete(reportSymlink);
                else
                    File.Delete(reportSymlink);

                string cappedPackage =
                    Path.Combine(root, "package-over-cap");
                CopyTree(generated, cappedPackage);
                File.WriteAllBytes(
                    Path.Combine(cappedPackage, "oversized.bin"),
                    new byte[
                        FontLibraryReportFormatter.MaxReportBytes
                        + 2 * 1024 * 1024]);
                ProcessResult overCap = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", cappedPackage,
                    "--mode", "validate");
                Assert.Equal(1, overCap.ExitCode);
                Assert.Contains("limit", overCap.Stderr);

                ProcessResult roundtrip = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generated,
                    "--mode", "roundtrip",
                    "--report", generationReportPath);
                AssertSuccess(roundtrip);
                Assert.Contains(
                    "\"mode\":\"roundtrip\"", roundtrip.Stdout);

                // Validation modes consume hashes/schema/package bytes only:
                // replacing both declared fonts with verified non-font bytes
                // must still succeed (opening/rasterizing them would fail).
                byte[] nonFont = Encoding.ASCII.GetBytes("not-a-font");
                string nonFontPath = Path.Combine(root, "not-a-font.bin");
                File.WriteAllBytes(nonFontPath, nonFont);
                string validationManifest =
                    Path.Combine(root, "validation-library.json");
                string validationJson = File.ReadAllText(manifest)
                    .Replace(
                        "Tuffy-Regular.ttf",
                        "not-a-font.bin",
                        StringComparison.Ordinal)
                    .Replace(
                        "NotoSansCJKsc-Subset.otf",
                        "not-a-font.bin",
                        StringComparison.Ordinal)
                    .Replace(
                        FontLibraryHashCore.ComputeSha256(
                            File.ReadAllBytes(fontPath)),
                        FontLibraryHashCore.ComputeSha256(nonFont),
                        StringComparison.Ordinal)
                    .Replace(
                        FontLibraryHashCore.ComputeSha256(
                            File.ReadAllBytes(notoPath)),
                        FontLibraryHashCore.ComputeSha256(nonFont),
                        StringComparison.Ordinal)
                    .Replace(
                        "\"byteLength\":"
                        + File.ReadAllBytes(fontPath).Length,
                        "\"byteLength\":" + nonFont.Length,
                        StringComparison.Ordinal)
                    .Replace(
                        "\"byteLength\":"
                        + File.ReadAllBytes(notoPath).Length,
                        "\"byteLength\":" + nonFont.Length,
                        StringComparison.Ordinal);
                File.WriteAllText(
                    validationManifest,
                    validationJson,
                    new UTF8Encoding(false));
                ProcessResult nonFontDryRun = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", validationManifest,
                    "--out", Path.Combine(root, "non-font-dry-output"),
                    "--mode", "dry-run");
                AssertSuccess(nonFontDryRun);
                Assert.Contains(
                    "stops after provenance/plan/capacity",
                    nonFontDryRun.Stderr);
                string validationPackage =
                    Path.Combine(root, "validation-package");
                CopyTree(generated, validationPackage);
                RewritePackageFontProvenance(
                    validationPackage,
                    nonFont.Length,
                    FontLibraryHashCore.ComputeSha256(nonFont),
                    FontLibraryHashCore.ComputeSha256(
                        File.ReadAllBytes(validationManifest)));
                AssertSuccess(Run(
                    cli,
                    "--build-font-library",
                    "--manifest", validationManifest,
                    "--out", validationPackage,
                    "--mode", "validate"));
                AssertSuccess(Run(
                    cli,
                    "--build-font-library",
                    "--manifest", validationManifest,
                    "--out", validationPackage,
                    "--mode", "roundtrip"));

                string generatedAgain =
                    Path.Combine(root, "generated-again");
                ProcessResult defaultGenerate = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generatedAgain);
                AssertSuccess(defaultGenerate);
                AssertTreesByteEqual(generated, generatedAgain);
                Assert.Equal(
                    ExtractPayloadTreeHash(generationReportText),
                    ExtractPayloadTreeHash(defaultGenerate.Stdout));

                MakeZhRoundTripDrift(generatedAgain);
                ProcessResult lossyValidate = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generatedAgain,
                    "--mode", "validate");
                AssertSuccess(lossyValidate);
                ProcessResult coordinatedTamperWithOracle = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generatedAgain,
                    "--mode", "validate",
                    "--report", generationReportPath);
                Assert.Equal(2, coordinatedTamperWithOracle.ExitCode);
                Assert.Contains(
                    "external full-tree",
                    coordinatedTamperWithOracle.Stderr);
                AssertFailureOutcome(
                    coordinatedTamperWithOracle, "Tamper");
                ProcessResult coordinatedRoundtripWithOracle = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generatedAgain,
                    "--mode", "roundtrip",
                    "--report", generationReportPath);
                Assert.Equal(2, coordinatedRoundtripWithOracle.ExitCode);
                Assert.Contains(
                    "external full-tree",
                    coordinatedRoundtripWithOracle.Stderr);
                AssertFailureOutcome(
                    coordinatedRoundtripWithOracle, "Tamper");
                ProcessResult drift = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generatedAgain,
                    "--mode", "roundtrip");
                Assert.Equal(2, drift.ExitCode);
                Assert.Contains("round-trip bytes differ", drift.Stderr);
                AssertFailureOutcome(
                    drift, "RoundtripMismatch");

                File.WriteAllText(
                    Path.Combine(generated, "alpha", "unexpected.txt"),
                    "unexpected",
                    new UTF8Encoding(false));
                ProcessResult invalid = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", generated,
                    "--mode", "validate");
                Assert.Equal(2, invalid.ExitCode);
                Assert.Contains("Error:", invalid.Stderr);
                AssertFailureOutcome(invalid, "Validation");

                string missingGlyphManifest =
                    Path.Combine(root, "missing-glyph.json");
                File.WriteAllText(
                    missingGlyphManifest,
                    File.ReadAllText(manifest).Replace(
                        "U+0041", "U+10FFFF",
                        StringComparison.Ordinal),
                    new UTF8Encoding(false));
                ProcessResult missingGlyph = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", missingGlyphManifest,
                    "--out", Path.Combine(root, "missing-glyph-output"),
                    "--mode", "generate",
                    "--job", "alpha");
                Assert.Equal(2, missingGlyph.ExitCode);
                Assert.Contains("has no glyph", missingGlyph.Stderr);

                string badProvenanceManifest =
                    Path.Combine(root, "bad-provenance.json");
                File.WriteAllText(
                    badProvenanceManifest,
                    File.ReadAllText(manifest).Replace(
                        FontLibraryHashCore.ComputeSha256(
                            File.ReadAllBytes(fontPath)),
                        new string('0', 64),
                        StringComparison.Ordinal),
                    new UTF8Encoding(false));
                ProcessResult badProvenance = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", badProvenanceManifest,
                    "--out", Path.Combine(root, "bad-provenance-output"),
                    "--mode", "dry-run",
                    "--job", "alpha");
                Assert.Equal(1, badProvenance.ExitCode);
                Assert.Contains("SHA-256 mismatch", badProvenance.Stderr);

                string badLengthManifest =
                    Path.Combine(root, "bad-byte-length.json");
                File.WriteAllText(
                    badLengthManifest,
                    File.ReadAllText(manifest).Replace(
                        "\"byteLength\":"
                            + File.ReadAllBytes(fontPath).Length,
                        "\"byteLength\":"
                            + (File.ReadAllBytes(fontPath).Length + 1),
                        StringComparison.Ordinal),
                    new UTF8Encoding(false));
                ProcessResult badLength = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", badLengthManifest,
                    "--out", Path.Combine(root, "bad-length-output"),
                    "--mode", "dry-run",
                    "--job", "alpha");
                Assert.Equal(1, badLength.ExitCode);
                Assert.Contains("byteLength", badLength.Stderr);
                AssertFailureOutcome(
                    badLength, "Provenance");

                string conflictManifest =
                    Path.Combine(root, "mapping-conflict.json");
                File.WriteAllText(
                    conflictManifest,
                    File.ReadAllText(manifest).Replace(
                        "\"scalar\":\"U+0041\",\"moji\":65",
                        "\"scalar\":\"U+0041\",\"moji\":65},"
                        + "{\"scalar\":\"U+0041\",\"moji\":66",
                        StringComparison.Ordinal),
                    new UTF8Encoding(false));
                ProcessResult conflict = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", conflictManifest,
                    "--out", Path.Combine(root, "conflict-output"),
                    "--mode", "dry-run",
                    "--job", "alpha");
                Assert.Equal(2, conflict.ExitCode);
                Assert.Contains("duplicates U+0041", conflict.Stderr);

                string exhaustionManifest =
                    Path.Combine(root, "mapping-exhaustion.json");
                string exhaustionJson = File.ReadAllText(manifest)
                    .Replace(
                        "\"scalarRanges\":[{\"start\":\"U+0041\","
                        + "\"end\":\"U+0041\"}]",
                        "\"scalarRanges\":[{\"start\":\"U+0041\","
                        + "\"end\":\"U+0042\"}]",
                        StringComparison.Ordinal)
                    .Replace(
                        "\"mapping\":{\"mode\":\"fixed\",\"entries\":[{"
                        + "\"scalar\":\"U+0041\",\"moji\":65}]}",
                        "\"mapping\":{\"mode\":\"range\","
                        + "\"unicodeStart\":\"U+0041\","
                        + "\"unicodeEnd\":\"U+0042\","
                        + "\"mojiStart\":65,\"mojiEnd\":65}",
                        StringComparison.Ordinal);
                Assert.NotEqual(
                    File.ReadAllText(manifest), exhaustionJson);
                File.WriteAllText(
                    exhaustionManifest,
                    exhaustionJson,
                    new UTF8Encoding(false));
                string exhaustionOutput =
                    Path.Combine(root, "exhaustion-output");
                ProcessResult exhaustion = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", exhaustionManifest,
                    "--out", exhaustionOutput,
                    "--mode", "generate",
                    "--job", "alpha");
                Assert.Equal(2, exhaustion.ExitCode);
                Assert.Contains("range mapping has", exhaustion.Stderr);
                Assert.True(FontLibraryReportFormatter.TryParse(
                    Encoding.UTF8.GetBytes(exhaustion.Stdout),
                    out FontLibraryReport exhaustionReport,
                    out string exhaustionReportError),
                    exhaustionReportError);
                FontLibraryOutcomeReport exhaustionOutcome =
                    Assert.Single(exhaustionReport.Outcomes);
                Assert.Equal("Exhaustion", exhaustionOutcome.Kind);
                Assert.Contains(
                    "range mapping has", exhaustionOutcome.Message);
                Assert.False(
                    File.Exists(exhaustionOutput)
                    || Directory.Exists(exhaustionOutput));

                ProcessResult unknown = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", Path.Combine(root, "unknown-output"),
                    "--unknown", "value");
                Assert.Equal(1, unknown.ExitCode);
                Assert.Contains("unknown option", unknown.Stderr);
                Assert.True(FontLibraryReportFormatter.TryParse(
                    Encoding.UTF8.GetBytes(unknown.Stdout),
                    out FontLibraryReport unknownReport,
                    out string unknownReportError),
                    unknownReportError);
                Assert.Equal(
                    "Usage",
                    Assert.Single(unknownReport.Outcomes).Kind);

                string longPropertyManifest =
                    Path.Combine(root, "long-property.json");
                string longProperty = new string('x', 5000);
                File.WriteAllText(
                    longPropertyManifest,
                    File.ReadAllText(manifest).Replace(
                        "{\"schemaVersion\":1",
                        "{\"" + longProperty + "\":0,\"schemaVersion\":1",
                        StringComparison.Ordinal),
                    new UTF8Encoding(false));
                ProcessResult longDiagnostic = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", longPropertyManifest,
                    "--out", Path.Combine(root, "long-property-output"),
                    "--mode", "dry-run");
                Assert.Equal(1, longDiagnostic.ExitCode);
                Assert.NotEmpty(longDiagnostic.Stderr);
                FontLibraryOutcomeReport longOutcome =
                    AssertFailureOutcome(longDiagnostic, "Schema");
                Assert.InRange(longOutcome.Message.Length, 1, 4096);

                ProcessResult emptyMode = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--out", Path.Combine(root, "empty-mode-output"),
                    "--mode=");
                Assert.Equal(1, emptyMode.ExitCode);
                Assert.Contains(
                    "--mode requires a non-empty value",
                    emptyMode.Stderr);

                ProcessResult mixedCommand = Run(
                    cli,
                    "--build-font-library",
                    "--help");
                Assert.Equal(1, mixedCommand.ExitCode);
                Assert.Contains("unknown option '--help'", mixedCommand.Stderr);

                ProcessResult duplicate = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", manifest,
                    "--manifest", manifest,
                    "--out", Path.Combine(root, "duplicate-output"));
                Assert.Equal(1, duplicate.ExitCode);
                Assert.Contains("duplicate option", duplicate.Stderr);

                string traversalManifest = Path.Combine(
                    root, "child", "..", "library.json");
                ProcessResult traversal = Run(
                    cli,
                    "--build-font-library",
                    "--manifest", traversalManifest,
                    "--out", Path.Combine(root, "traversal-output"));
                Assert.Equal(1, traversal.ExitCode);
                Assert.Contains("must not contain '..'", traversal.Stderr);
                AssertFailureOutcome(traversal, "Path");
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); }
                catch { }
            }
        }

        [Fact]
        public void CliResolver_CoversEnvironmentConfigurationsAndPlatformLayouts()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "febuilder-cli-resolver-"
                + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string appHost = RuntimeInformation.IsOSPlatform(
                    OSPlatform.Windows)
                    ? "FEBuilderGBA.CLI.exe"
                    : "FEBuilderGBA.CLI";
                string envExecutable = Path.Combine(
                    root, "env", appHost);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(envExecutable)!);
                File.WriteAllBytes(envExecutable, new byte[] { 1 });
                Assert.Equal(
                    Path.GetFullPath(envExecutable),
                    ResolveCliPath(
                        root, envExecutable, out string envProbes));
                Assert.Contains(
                    Path.GetFullPath(envExecutable), envProbes);

                File.Delete(envExecutable);
                string projectBin = Path.Combine(
                    root, "FEBuilderGBA.CLI", "bin");
                string rid = RuntimeInformation.RuntimeIdentifier;
                string[] layouts =
                {
                    Path.Combine(
                        "Debug", "net10.0", appHost),
                    Path.Combine(
                        "Release", "net10.0", "FEBuilderGBA.CLI.dll"),
                    Path.Combine(
                        "Debug", "net10.0", rid, appHost),
                    Path.Combine(
                        "x64", "Release", "net10.0",
                        "FEBuilderGBA.CLI.dll"),
                    Path.Combine(
                        "Release", "x64", "net10.0",
                        appHost),
                    Path.Combine(
                        "AnyCPU", "Debug", "net10.0",
                        "FEBuilderGBA.CLI.dll"),
                    Path.Combine(
                        "Release", "Any CPU", "net10.0",
                        appHost),
                };
                foreach (string layout in layouts)
                {
                    if (Directory.Exists(projectBin))
                        Directory.Delete(projectBin, recursive: true);
                    string candidate = Path.Combine(projectBin, layout);
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(candidate)!);
                    File.WriteAllBytes(candidate, new byte[] { 1 });
                    Assert.Equal(
                        Path.GetFullPath(candidate),
                        ResolveCliPath(
                            root, null, out string candidateProbes));
                    Assert.Contains(
                        Path.GetFullPath(candidate), candidateProbes);
                }
                if (Directory.Exists(projectBin))
                    Directory.Delete(projectBin, recursive: true);

                string missingEnvironmentPath =
                    Path.Combine(root, "missing-cli");
                string? unresolved = ResolveCliPath(
                    root, missingEnvironmentPath, out string allProbes);
                Assert.Null(unresolved);
                Assert.Contains(
                    Path.GetFullPath(missingEnvironmentPath), allProbes);
                Assert.Contains(
                    Path.Combine(
                        projectBin, "Debug", "net10.0", appHost),
                    allProbes);
                Assert.Contains(
                    Path.Combine(
                        projectBin, "Release", "net10.0",
                        "FEBuilderGBA.CLI.dll"),
                    allProbes);
                Assert.Contains(
                    Path.Combine(
                        projectBin, "x86", "Debug", "net10.0",
                        appHost),
                    allProbes);
                Assert.Contains(
                    Path.Combine(
                        projectBin, "Debug", "arm64", "net10.0",
                        "FEBuilderGBA.CLI.dll"),
                    allProbes);

                string syntheticDll =
                    Path.Combine(root, "env", "FEBuilderGBA.CLI.dll");
                ProcessStartInfo dllStart =
                    CreateProcessStartInfo(syntheticDll);
                Assert.Equal("dotnet", dllStart.FileName);
                Assert.Equal(
                    Path.GetFullPath(syntheticDll),
                    Assert.Single(dllStart.ArgumentList));
                string syntheticAppHost =
                    Path.Combine(root, appHost);
                ProcessStartInfo appHostStart =
                    CreateProcessStartInfo(syntheticAppHost);
                Assert.Equal(
                    Path.GetFullPath(syntheticAppHost),
                    appHostStart.FileName);
                Assert.Empty(appHostStart.ArgumentList);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); }
                catch { }
            }
        }

        static string MultiJobManifest()
        {
            return "{"
                + "\"schemaVersion\":1,"
                + "\"jobs\":["
                + JobJson(
                    "zeta", "zh-Hans", "U+4F60", 0x8181,
                    "zh-16x13",
                    "NotoSansCJKsc-Subset.otf", NotoSha256, 2901316,
                    "Noto Sans CJK SC",
                    "Version 2.004;hotconv 1.0.118;makeotfexe 2.5.65603",
                    "Noto-OFL.txt", NotoLicenseSha256,
                    "zh-corpus.txt", CorpusSha256)
                + ","
                + JobJson(
                    "kappa", "ja", "U+65E5", 0x93FA,
                    "main-16x16",
                    "NotoSansCJKsc-Subset.otf", NotoSha256, 2901316,
                    "Noto Sans CJK SC",
                    "Version 2.004;hotconv 1.0.118;makeotfexe 2.5.65603",
                    "Noto-OFL.txt", NotoLicenseSha256)
                + ","
                + JobJson(
                    "alpha", "en-US", "U+0041", 65,
                    "main-16x16",
                    "Tuffy-Regular.ttf", TuffySha256, 218708,
                    "Tuffy",
                    "Version 1.272; ttfautohint (v1.6)",
                    "OFL.txt", TuffyLicenseSha256)
                + "]}";
        }

        static string JobJson(
            string id,
            string locale,
            string scalar,
            uint moji,
            string format,
            string fontPath,
            string fontHash,
            int fontByteLength,
            string fontFamily,
            string fontVersion,
            string licensePath,
            string licenseHash,
            string? corpusPath = null,
            string? corpusHash = null)
            => "{"
                + "\"id\":\"" + id + "\","
                + "\"locale\":\"" + locale + "\"," 
                + "\"format\":\"" + format + "\","
                + "\"styles\":[\"text\",\"item\"],"
                + (corpusPath == null
                    ? ""
                    : "\"corpus\":{\"path\":\"" + corpusPath
                        + "\",\"sha256\":\"" + corpusHash + "\"},")
                + "\"scalarRanges\":[{\"start\":\""
                + scalar + "\",\"end\":\"" + scalar + "\"}],"
                + "\"font\":{\"path\":\"" + fontPath + "\","
                + "\"sha256\":\"" + fontHash + "\","
                + "\"byteLength\":" + fontByteLength + ","
                + "\"family\":\"" + fontFamily + "\","
                + "\"version\":\"" + fontVersion + "\","
                + "\"sourceUrl\":\"https://github.com/"
                + "laqieer/FEBuilderGBA\","
                + "\"size\":12},"
                + "\"license\":{\"licenseId\":\"OFL-1.1\","
                + "\"licenseFile\":\"" + licensePath + "\","
                + "\"sha256\":\"" + licenseHash + "\","
                + "\"sourceUrl\":\"https://scripts.sil.org/OFL\"},"
                + "\"verticalOffset\":0,"
                + "\"mapping\":{\"mode\":\"fixed\",\"entries\":[{"
                + "\"scalar\":\"" + scalar + "\","
                + "\"moji\":" + moji.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + "}]}}";

        static ProcessResult Run(string cli, params string[] arguments)
        {
            ProcessStartInfo start = CreateProcessStartInfo(cli);
            foreach (string argument in arguments)
                start.ArgumentList.Add(argument);
            using Process? process = Process.Start(start);
            Assert.NotNull(process);
            System.Threading.Tasks.Task<string> stdout =
                process.StandardOutput.ReadToEndAsync();
            System.Threading.Tasks.Task<string> stderr =
                process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
                throw new TimeoutException(
                    "Font-library CLI did not exit within 120 seconds.");
            }
            System.Threading.Tasks.Task.WaitAll(stdout, stderr);
            return new ProcessResult(
                process.ExitCode, stdout.Result, stderr.Result);
        }

        static ProcessStartInfo CreateProcessStartInfo(string cli)
        {
            string fullPath = Path.GetFullPath(cli);
            bool managedDll = string.Equals(
                Path.GetExtension(fullPath),
                ".dll",
                StringComparison.OrdinalIgnoreCase);
            var start = new ProcessStartInfo
            {
                FileName = managedDll ? "dotnet" : fullPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory =
                    Path.GetDirectoryName(fullPath)
                    ?? Environment.CurrentDirectory,
            };
            if (managedDll)
                start.ArgumentList.Add(fullPath);
            return start;
        }

        static void AssertAlphaPackageMatchesRenderGoldens(
            string packageRoot,
            string stdoutReport)
        {
            string alpha = Path.Combine(packageRoot, "alpha");
            string[] relativeFiles = Directory.GetFiles(
                    packageRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(packageRoot, path)
                    .Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(
                new[]
                {
                    "alpha/alpha.fontall.txt",
                    "alpha/item_41.png",
                    "alpha/slots.tsv",
                    "alpha/text_41.png",
                    FontLibraryBuilderCore.PackageReportPath,
                },
                relativeFiles);
            Assert.Equal(
                FontBulkManifestCore.Header
                + "\nA\titem\t"
                + SkiaFontGoldens.GoldenItemAWidth
                + "\titem_41.png\nA\ttext\t"
                + SkiaFontGoldens.GoldenTextAWidth
                + "\ttext_41.png\n",
                File.ReadAllText(Path.Combine(
                    alpha, "alpha.fontall.txt")));

            foreach ((FontLibraryStyle style, byte[] tile) in new[]
            {
                (FontLibraryStyle.Item, SkiaFontGoldens.GoldenItemA),
                (FontLibraryStyle.Text, SkiaFontGoldens.GoldenTextA),
            })
            {
                string name = style == FontLibraryStyle.Item
                    ? "item_41.png" : "text_41.png";
                IndexedPngCoreReadResult read = IndexedPngCore.Read(
                    File.ReadAllBytes(Path.Combine(alpha, name)),
                    FontLibraryFormat.Main16x16,
                    style);
                Assert.True(read.Success, read.Error);
                Assert.Equal(
                    IndexedPngCore.UnpackEngineTile(tile),
                    read.Indices);
            }

            Assert.True(FontLibraryReportFormatter.TryParse(
                File.ReadAllBytes(Path.Combine(
                    packageRoot,
                    FontLibraryBuilderCore.PackageReportPath)),
                out FontLibraryReport packageReport,
                out string reportError),
                reportError);
            Assert.Equal(
                packageReport.PayloadTreeSha256,
                ExtractPayloadTreeHash(stdoutReport));
        }

        static void MakeZhRoundTripDrift(string packageRoot)
        {
            string slotsPath = Path.Combine(
                packageRoot, "zeta", "slots.tsv");
            string[] lines = File.ReadAllText(slotsPath).Split('\n');
            Assert.True(lines.Length >= 3);
            string[] columns = lines[1].Split('\t');
            Assert.Equal(7, columns.Length);
            FontLibraryStyle style = columns[2] == "item"
                ? FontLibraryStyle.Item
                : FontLibraryStyle.Text;
            int width = int.Parse(
                columns[3],
                System.Globalization.CultureInfo.InvariantCulture);
            string pngPath = Path.Combine(
                packageRoot, "zeta", columns[4]);
            IndexedPngCoreReadResult read = IndexedPngCore.Read(
                File.ReadAllBytes(pngPath),
                FontLibraryFormat.Zh16x13,
                style);
            Assert.True(read.Success, read.Error);
            int nativeY = width == 16 ? 12 : 0;
            int nativeX = width == 16 ? 15 : width;
            int y = nativeY + FontGlyphZHIndexCore.VanillaShift(
                style == FontLibraryStyle.Item);
            int pixel = y * 16 + nativeX;
            read.Indices[pixel] =
                read.Indices[pixel] == 3
                    ? (byte)1 : (byte)3;
            byte[] png = IndexedPngCore.Encode(
                read.Indices,
                FontLibraryFormat.Zh16x13,
                style);
            File.WriteAllBytes(pngPath, png);
            columns[6] = FontLibraryHashCore.ComputeSha256(png);
            lines[1] = string.Join('\t', columns);
            File.WriteAllText(
                slotsPath,
                string.Join('\n', lines),
                new UTF8Encoding(false));

            string packageReportPath = Path.Combine(
                packageRoot,
                FontLibraryBuilderCore.PackageReportPath);
            Assert.True(FontLibraryReportFormatter.TryParse(
                File.ReadAllBytes(packageReportPath),
                out FontLibraryReport packageReport,
                out string reportError),
                reportError);
            FontLibraryJobReport zeta = Assert.Single(
                packageReport.Jobs.Where(job => job.Id == "zeta"));
            FontLibraryGlyphReport changedGlyph = Assert.Single(
                zeta.Glyphs.Where(glyph =>
                    glyph.Filename == columns[4]));
            changedGlyph.PngSha256 = columns[6];
            using (IncrementalHash pngAggregate =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                for (int i = 1; i < lines.Length - 1; i++)
                {
                    string[] row = lines[i].Split('\t');
                    Assert.Equal(7, row.Length);
                    pngAggregate.AppendData(File.ReadAllBytes(
                        Path.Combine(packageRoot, "zeta", row[4])));
                }
                zeta.PngSha256 = Convert.ToHexString(
                    pngAggregate.GetHashAndReset()).ToLowerInvariant();
            }
            var payload =
                new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(
                packageRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(packageRoot, file)
                    .Replace('\\', '/');
                if (relative == FontLibraryBuilderCore.PackageReportPath)
                    continue;
                payload.Add(relative, File.ReadAllBytes(file));
            }
            packageReport.PayloadTreeSha256 =
                FontLibraryHashCore.ComputeTreeSha256(payload);
            File.WriteAllText(
                packageReportPath,
                FontLibraryReportFormatter.FormatEmbedded(packageReport),
                new UTF8Encoding(false));
        }

        static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(
                source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(
                    destination, Path.GetRelativePath(source, directory)));
            }
            foreach (string file in Directory.GetFiles(
                source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(
                    destination, Path.GetRelativePath(source, file));
                File.Copy(file, target);
            }
        }

        static void RewritePackageFontProvenance(
            string packageRoot,
            int byteLength,
            string sha256,
            string manifestSha256)
        {
            string path = Path.Combine(
                packageRoot,
                FontLibraryBuilderCore.PackageReportPath);
            Assert.True(FontLibraryReportFormatter.TryParse(
                File.ReadAllBytes(path),
                out FontLibraryReport report,
                out string error),
                error);
            foreach (FontLibraryJobReport job in report.Jobs)
            {
                job.FontByteLength = byteLength;
                job.FontSha256 = sha256;
            }
            report.ManifestSha256 = manifestSha256;
            File.WriteAllText(
                path,
                FontLibraryReportFormatter.FormatEmbedded(report),
                new UTF8Encoding(false));
        }

        static void AssertSuccess(ProcessResult result)
        {
            Assert.True(
                result.ExitCode == 0,
                "exit=" + result.ExitCode
                + "\nstdout:\n" + result.Stdout
                + "\nstderr:\n" + result.Stderr);
        }

        static FontLibraryOutcomeReport AssertFailureOutcome(
            ProcessResult result,
            string expectedKind)
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.True(FontLibraryReportFormatter.TryParse(
                Encoding.UTF8.GetBytes(result.Stdout),
                out FontLibraryReport report,
                out string error),
                error + "\nstdout:\n" + result.Stdout);
            Assert.Equal("error", report.Mode);
            Assert.Equal("failure", report.Oracle);
            FontLibraryOutcomeReport outcome =
                Assert.Single(report.Outcomes);
            Assert.Equal(expectedKind, outcome.Kind);
            Assert.NotEmpty(outcome.Message);
            return outcome;
        }

        static void AssertTreesByteEqual(string expected, string actual)
        {
            string[] expectedFiles = Directory.GetFiles(
                    expected, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(expected, path)
                    .Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            string[] actualFiles = Directory.GetFiles(
                    actual, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(actual, path)
                    .Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedFiles, actualFiles);
            foreach (string relative in expectedFiles)
            {
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(
                        expected,
                        relative.Replace(
                            '/', Path.DirectorySeparatorChar))),
                    File.ReadAllBytes(Path.Combine(
                        actual,
                        relative.Replace(
                            '/', Path.DirectorySeparatorChar))));
            }
        }

        static void AssertPackageEqualsDirectory(
            FontLibraryPackage expected,
            string actualRoot)
        {
            string[] actualFiles = Directory.GetFiles(
                    actualRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(actualRoot, path)
                    .Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expected.Files.Keys.ToArray(), actualFiles);
            foreach (KeyValuePair<string, byte[]> file in expected.Files)
            {
                Assert.Equal(
                    file.Value,
                    File.ReadAllBytes(Path.Combine(
                        actualRoot,
                        file.Key.Replace(
                            '/', Path.DirectorySeparatorChar))));
            }
        }

        static string ExtractPayloadTreeHash(string report)
        {
            const string marker = "\"payloadTreeSha256\":\"";
            int start = report.IndexOf(
                marker, StringComparison.Ordinal);
            Assert.True(
                start >= 0,
                "Report omitted payloadTreeSha256.");
            start += marker.Length;
            int end = report.IndexOf('"', start);
            Assert.True(end > start);
            return report.Substring(start, end - start);
        }

        static void CreateDirectoryAlias(
            string aliasPath,
            string targetPath)
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(aliasPath, targetPath);
                return;
            }

            var start = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("mklink");
            start.ArgumentList.Add("/J");
            start.ArgumentList.Add(aliasPath);
            start.ArgumentList.Add(targetPath);
            using Process? process = Process.Start(start);
            Assert.NotNull(process);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(
                process.ExitCode == 0,
                "Could not create test junction: "
                + stdout + stderr);
        }

        static bool CreateHardLink(string linkPath, string existingPath)
        {
            if (OperatingSystem.IsWindows())
                return CreateHardLinkWindows(
                    linkPath, existingPath, IntPtr.Zero);
            return CreateHardLinkUnix(existingPath, linkPath) == 0;
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true,
            EntryPoint = "CreateHardLinkW")]
        static extern bool CreateHardLinkWindows(
            string fileName,
            string existingFileName,
            IntPtr securityAttributes);

        [DllImport(
            "libc",
            CharSet = CharSet.Ansi,
            SetLastError = true,
            EntryPoint = "link")]
        static extern int CreateHardLinkUnix(
            string existingPath,
            string linkPath);

        static string? ResolveCliPath(out string probeDiagnostics)
        {
            string? root = FindRepoRoot();
            if (root == null)
            {
                probeDiagnostics =
                    "Repository root probe failed from "
                    + AppContext.BaseDirectory + ".";
                return null;
            }
            return ResolveCliPath(
                root,
                Environment.GetEnvironmentVariable(
                    "FEBUILDERGBA_CLI_EXE"),
                out probeDiagnostics);
        }

        static string? ResolveCliPath(
            string repoRoot,
            string? environmentOverride,
            out string probeDiagnostics)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(environmentOverride))
            {
                string env = Path.GetFullPath(environmentOverride);
                candidates.Add(env);
            }

            string project = Path.Combine(
                repoRoot, "FEBuilderGBA.CLI", "bin");
            string rid = RuntimeInformation.RuntimeIdentifier;
            bool releaseFirst = AppContext.BaseDirectory.IndexOf(
                Path.DirectorySeparatorChar + "Release"
                    + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) >= 0;
            string[] configurations = releaseFirst
                ? new[] { "Release", "Debug" }
                : new[] { "Debug", "Release" };
            string currentPlatform =
                RuntimeInformation.ProcessArchitecture
                    .ToString().ToLowerInvariant();
            string[] platforms =
                new[]
                {
                    currentPlatform,
                    "x64",
                    "x86",
                    "arm64",
                    "AnyCPU",
                    "Any CPU",
                }
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            string appHostName = RuntimeInformation.IsOSPlatform(
                OSPlatform.Windows)
                ? "FEBuilderGBA.CLI.exe"
                : "FEBuilderGBA.CLI";

            void AddOutputDirectory(string directory)
            {
                candidates.Add(Path.Combine(directory, appHostName));
                candidates.Add(Path.Combine(
                    directory, "FEBuilderGBA.CLI.dll"));
            }

            foreach (string configuration in configurations)
            {
                AddOutputDirectory(Path.Combine(
                    project, configuration, "net10.0"));
                AddOutputDirectory(Path.Combine(
                    project, configuration, "net10.0", rid));
                foreach (string platform in platforms)
                {
                    AddOutputDirectory(Path.Combine(
                        project, platform, configuration, "net10.0"));
                    AddOutputDirectory(Path.Combine(
                        project, platform, configuration, "net10.0", rid));
                    AddOutputDirectory(Path.Combine(
                        project, configuration, platform, "net10.0"));
                    AddOutputDirectory(Path.Combine(
                        project, configuration, platform, "net10.0", rid));
                }
            }
            string[] distinctCandidates = candidates
                .Select(Path.GetFullPath)
                .Distinct(
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal)
                .ToArray();
            probeDiagnostics = "CLI paths probed:\n  - "
                + string.Join("\n  - ", distinctCandidates);
            foreach (string candidate in distinctCandidates)
                if (File.Exists(candidate))
                    return candidate;
            return null;
        }

        static string? FindRepoRoot()
        {
            string? directory = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && directory != null; i++)
            {
                if (File.Exists(Path.Combine(
                    directory, "FEBuilderGBA.sln")))
                    return directory;
                directory = Path.GetDirectoryName(directory);
            }
            return null;
        }

        sealed class ProcessResult
        {
            internal ProcessResult(
                int exitCode,
                string stdout,
                string stderr)
            {
                ExitCode = exitCode;
                Stdout = stdout;
                Stderr = stderr;
            }

            internal int ExitCode { get; }
            internal string Stdout { get; }
            internal string Stderr { get; }
        }

    }
}
