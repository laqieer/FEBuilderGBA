// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FEBuilderGBA.Core
{
    public sealed class FontLibraryJobAssets
    {
        public byte[] FontFileData { get; set; } = Array.Empty<byte>();
        public byte[] LicenseFileData { get; set; } = Array.Empty<byte>();
    }

    public sealed class FontLibraryBuildInput
    {
        public FontLibraryPlan Plan { get; set; }
        public string ManifestSha256 { get; set; } = "";
        public Dictionary<string, FontLibraryJobAssets> Assets { get; } =
            new Dictionary<string, FontLibraryJobAssets>(StringComparer.Ordinal);
        public IFontLibraryFaceFactory FaceFactory { get; set; }
    }

    /// <summary>
    /// Immutable-by-convention package tree. Keys are canonical ordinal '/'
    /// relative paths; values are complete file bytes.
    /// </summary>
    public sealed class FontLibraryPackage
    {
        public SortedDictionary<string, byte[]> Files { get; } =
            new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
    }

    public sealed class FontLibraryJobReport
    {
        public string Id { get; set; } = "";
        public string Locale { get; set; } = "";
        public string Format { get; set; } = "";
        public int ScalarCount { get; set; }
        public int RowCount { get; set; }
        public string CorpusSha256 { get; set; } = "";
        public int NativeWidth { get; set; }
        public int NativeHeight { get; set; }
        public int PngWidth { get; set; }
        public int PngHeight { get; set; }
        public float FontSize { get; set; }
        public int VerticalOffset { get; set; }
        public int FontByteLength { get; set; }
        public string FontFamily { get; set; } = "";
        public string FontVersion { get; set; } = "";
        public string FontSha256 { get; set; } = "";
        public string FontSourceUrl { get; set; } = "";
        public string LicenseId { get; set; } = "";
        public string LicenseFile { get; set; } = "";
        public string LicenseSha256 { get; set; } = "";
        public string LicenseSourceUrl { get; set; } = "";
        public string PackedSha256 { get; set; } = "";
        public string PngSha256 { get; set; } = "";
        public List<FontLibraryGlyphReport> Glyphs { get; } =
            new List<FontLibraryGlyphReport>();

        public int SlotCount
        {
            get => RowCount;
            set => RowCount = value;
        }
    }

    public sealed class FontLibraryGlyphReport
    {
        public int UnicodeScalar { get; set; }
        public string Character { get; set; } = "";
        public string Style { get; set; } = "";
        public uint Moji { get; set; }
        public int Width { get; set; }
        public int NativeWidth { get; set; }
        public int NativeHeight { get; set; }
        public int PngWidth { get; set; }
        public int PngHeight { get; set; }
        public string Filename { get; set; } = "";
        public string PackedSha256 { get; set; } = "";
        public string PngSha256 { get; set; } = "";
    }

    public sealed class FontLibraryOutcomeReport
    {
        public string Kind { get; set; } = "";
        public string JobId { get; set; } = "";
        public int UnicodeScalar { get; set; } = -1;
        public uint Moji { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class FontLibraryReport
    {
        public string Mode { get; set; } = "";
        public string Oracle { get; set; } = "";
        public string ManifestSha256 { get; set; } = "";
        public List<FontLibraryJobReport> Jobs { get; } =
            new List<FontLibraryJobReport>();
        public List<FontLibraryOutcomeReport> Outcomes { get; } =
            new List<FontLibraryOutcomeReport>();
        /// <summary>Hash of package files except package-report.json.</summary>
        public string PayloadTreeSha256 { get; set; } = "";
        /// <summary>
        /// Hash of the finalized package including package-report.json. This is
        /// external-oracle data and is never embedded in the package report.
        /// </summary>
        public string FullTreeSha256 { get; set; } = "";
    }

    public sealed class FontLibraryBuildResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public FontLibraryFailureKind FailureKind { get; set; }
        public FontLibraryPackage Package { get; set; }
        public FontLibraryReport Report { get; set; }
    }

    public sealed class FontLibraryValidationResult
    {
        public bool Success { get; set; }
        public bool ReproducibilityDrift { get; set; }
        public string Error { get; set; } = "";
        public FontLibraryFailureKind FailureKind { get; set; }
        public FontLibraryReport Report { get; set; }
    }

    /// <summary>
    /// Pure in-memory package generator. All files are completed and validated
    /// before a caller is allowed to publish a tree.
    /// </summary>
    public static class FontLibraryBuilderCore
    {
        public const string PackageReportPath = "package-report.json";

        /// <summary>
        /// Verify provenance and emit a stable capacity report.  The supplied
        /// face factory is deliberately ignored: dry-run never opens a face,
        /// rasterizes, constructs a package tree, or produces package bytes.
        /// Keeping the same input shape makes this property directly provable
        /// with a throwing factory.
        /// </summary>
        public static FontLibraryBuildResult DryRun(FontLibraryBuildInput input)
        {
            var result = new FontLibraryBuildResult();
            if (input?.Plan == null)
                return Fail(result,
                    "A font-library plan is required.",
                    FontLibraryFailureKind.Schema);
            try
            {
                VerifyPlanCapacity(input.Plan);
                var report = new FontLibraryReport
                {
                    Mode = "dry-run",
                    Oracle = "plan-and-provenance-only",
                    ManifestSha256 = input.ManifestSha256 ?? "",
                };
                foreach (FontLibraryPlannedJob plannedJob in input.Plan.Jobs)
                {
                    FontLibraryJobManifest job = plannedJob.Manifest
                        ?? throw new FontLibraryOperationException(
                            FontLibraryFailureKind.Schema,
                            "Planned job has no manifest.");
                    FontLibraryJobAssets assets =
                        RequireAssets(input, job);
                    VerifyAssets(job, assets);
                    FontLibraryJobReport jobReport = CreateJobReport(
                        plannedJob, "", "");
                    foreach (FontLibraryPlannedSlot slot in plannedJob.Slots)
                    {
                        jobReport.Glyphs.Add(CreateGlyphReport(
                            slot, job.Format, width: 0, filename: "",
                            packedSha256: "", pngSha256: ""));
                    }
                    report.Jobs.Add(jobReport);
                }
                result.Success = true;
                result.Report = report;
                return result;
            }
            catch (FontLibraryOperationException ex)
            {
                return Fail(result, ex.Message, ex.Kind);
            }
            catch (Exception ex) when (
                ex is InvalidOperationException
                || ex is ArgumentException
                || ex is CryptographicException
                || ex is OverflowException
                || ex is DecoderFallbackException)
            {
                return Fail(
                    result, ex.Message, FontLibraryFailureKind.Provenance);
            }
        }

        public static FontLibraryBuildResult Build(FontLibraryBuildInput input)
        {
            var result = new FontLibraryBuildResult();
            if (input?.Plan == null)
                return Fail(result,
                    "A font-library plan is required.",
                    FontLibraryFailureKind.Schema);
            if (input.FaceFactory == null)
                return Fail(result,
                    "A strict font face factory is required.",
                    FontLibraryFailureKind.Font);

            try
            {
                // Recheck the public plan seam before package allocation or
                // face creation; callers cannot bypass planner preflight by
                // constructing a plan object directly.
                VerifyPlanCapacity(input.Plan);
                var package = new FontLibraryPackage();
                var report = new FontLibraryReport
                {
                    Mode = "generate",
                    Oracle = "generation",
                    ManifestSha256 = input.ManifestSha256 ?? "",
                };

                foreach (FontLibraryPlannedJob plannedJob in input.Plan.Jobs)
                {
                    FontLibraryJobManifest job = plannedJob.Manifest
                        ?? throw new FontLibraryOperationException(
                            FontLibraryFailureKind.Schema,
                            "Planned job has no manifest.");
                    FontLibraryJobAssets assets =
                        RequireAssets(input, job);
                    VerifyAssets(job, assets);

                    var faceSpec = new FontLibraryFaceSpec
                    {
                        FontFileData = assets.FontFileData,
                        ExpectedFamily = job.Font.Family,
                        ExpectedVersion = job.Font.Version,
                        Size = job.Font.Size,
                    };
                    IFontLibraryFace face = null;
                    bool opened;
                    string faceError;
                    try
                    {
                        opened = input.FaceFactory.TryOpenFace(
                            faceSpec, out face, out faceError);
                    }
                    catch
                    {
                        try
                        {
                            face?.Dispose();
                        }
                        catch
                        {
                            // Preserve the native/open failure; disposal was
                            // still deterministically attempted.
                        }
                        throw;
                    }
                    if (!opened || face == null)
                    {
                        face?.Dispose();
                        throw JobError(job,
                            FontLibraryFailureKind.Font,
                            "strict font face open failed: " + faceError);
                    }

                    try
                    {
                        BuildJob(package, report, plannedJob, face);
                    }
                    catch
                    {
                        try
                        {
                            face.Dispose();
                        }
                        catch
                        {
                            // Preserve the typed build failure while still
                            // deterministically attempting native cleanup.
                        }
                        throw;
                    }
                    try
                    {
                        face.Dispose();
                    }
                    catch (Exception ex)
                    {
                        throw JobError(job,
                            FontLibraryFailureKind.Font,
                            "strict font face disposal failed: "
                            + ex.Message);
                    }
                }

                report.PayloadTreeSha256 =
                    FontLibraryHashCore.ComputeTreeSha256(package.Files);
                AddFile(
                    package,
                    PackageReportPath,
                    FontLibraryText.Utf8(
                        FontLibraryReportFormatter.FormatEmbedded(report)));
                report.FullTreeSha256 =
                    FontLibraryHashCore.ComputeTreeSha256(package.Files);
                result.Success = true;
                result.Package = package;
                result.Report = report;
                return result;
            }
            catch (FontLibraryOperationException ex)
            {
                return Fail(result, ex.Message, ex.Kind);
            }
            catch (Exception ex)
            {
                return Fail(
                    result, ex.Message, FontLibraryFailureKind.Font);
            }
        }

        static void BuildJob(
            FontLibraryPackage package,
            FontLibraryReport report,
            FontLibraryPlannedJob plannedJob,
            IFontLibraryFace face)
        {
            FontLibraryJobManifest job = plannedJob.Manifest;
            var manifestRows = new List<FontBulkManifestRow>();
            var slotRows = new List<FontLibrarySlotRecord>();
            using IncrementalHash packedAggregate =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using IncrementalHash pngAggregate =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (FontLibraryPlannedSlot slot in plannedJob.Slots)
            {
                bool item = slot.Style == FontLibraryStyle.Item;
                bool hasGlyph;
                try
                {
                    hasGlyph = face.HasGlyph(slot.UnicodeScalar);
                }
                catch (Exception ex)
                {
                    throw JobError(job,
                        FontLibraryFailureKind.Font,
                        "glyph availability query failed for U+"
                        + slot.UnicodeScalar.ToString(
                            "X4", CultureInfo.InvariantCulture)
                        + ": " + ex.Message);
                }
                if (!hasGlyph)
                    throw JobError(job,
                        FontLibraryFailureKind.MissingGlyph,
                        "verified face has no glyph for U+"
                        + slot.UnicodeScalar.ToString(
                            "X4", CultureInfo.InvariantCulture));
                byte[] engineTile;
                int width;
                FontLibraryFaceFailureKind rasterFailure;
                string renderError;
                bool rendered;
                try
                {
                    rendered = face.TryRasterizeGlyph(
                        slot.UnicodeScalar,
                        item,
                        job.VerticalOffset,
                        out engineTile,
                        out width,
                        out rasterFailure,
                        out renderError);
                }
                catch (Exception ex)
                {
                    throw JobError(job,
                        FontLibraryFailureKind.Font,
                        "U+"
                        + slot.UnicodeScalar.ToString(
                            "X4", CultureInfo.InvariantCulture)
                        + " " + StyleText(slot.Style)
                        + " rasterization threw: " + ex.Message);
                }
                if (!rendered)
                {
                    throw JobError(job,
                        rasterFailure
                            == FontLibraryFaceFailureKind.MissingGlyph
                            ? FontLibraryFailureKind.MissingGlyph
                            : FontLibraryFailureKind.Font,
                        "U+"
                        + slot.UnicodeScalar.ToString("X4",
                            CultureInfo.InvariantCulture)
                        + " " + StyleText(slot.Style)
                        + " rasterization failed: " + renderError);
                }
                if (width < 1 || width > 16)
                    throw JobError(job,
                        FontLibraryFailureKind.Font,
                        "rasterizer width is outside 1..16");

                byte[] mainIndices =
                    IndexedPngCore.UnpackEngineTile(engineTile);
                byte[] packageIndices =
                    IndexedPngCore.RemapRasterizerIndices(
                        mainIndices, job.Format);
                byte[] packed;
                if (job.Format == FontLibraryFormat.Zh16x13)
                {
                    // Established native geometry is the TOP 16x13 rows of the
                    // ordinary raster.  Item/text shifts belong only to the
                    // emitted package cell, never to source-row selection.
                    byte[] native =
                        FontGlyphZHIndexCore.CropTop16x13(packageIndices);
                    packed = FontGlyphZHCore.PackGlyphZHBytes(native, width);
                    if (packed == null)
                        throw JobError(job,
                            FontLibraryFailureKind.Font,
                            "could not pack rasterized ZH indices");
                    // Emit the exact engine-representable image. Width and the
                    // fixed 40-byte stream can discard columns/tail pixels; a
                    // package PNG must depict the bytes it hashes, not hidden
                    // pre-pack raster data.
                    native = FontGlyphZHIndexCore.UnpackGlyphBytes(
                        packed, width);
                    packageIndices = FontGlyphZHIndexCore.ShiftTo16x16(
                        native, item);
                    if (!FontGlyphZHIndexCore.HasZeroPadding(
                        packageIndices, item))
                        throw JobError(job,
                            FontLibraryFailureKind.Validation,
                            "ZH padding is not zero");
                }
                else
                {
                    packed = IndexedPngCore.PackEngineTile(packageIndices);
                }
                if (packed == null)
                    throw JobError(job,
                        FontLibraryFailureKind.Font,
                        "could not pack rasterized indices");
                if (!ContainsInk(packageIndices))
                    throw JobError(job,
                        FontLibraryFailureKind.MissingGlyph,
                        "glyph became all-background after format geometry");

                string style = StyleText(slot.Style);
                string filename =
                    FontBulkManifestCore.CanonicalFilename(style, slot.Moji);
                string relative = job.Id + "/" + filename;
                byte[] png = IndexedPngCore.Encode(
                    packageIndices, job.Format, slot.Style);
                AddFile(package, relative, png);

                string packedHash =
                    FontLibraryHashCore.ComputeSha256(packed);
                string pngHash =
                    FontLibraryHashCore.ComputeSha256(png);
                packedAggregate.AppendData(packed);
                pngAggregate.AppendData(png);
                manifestRows.Add(new FontBulkManifestRow
                {
                    Character = slot.Character,
                    Type = style,
                    Width = width,
                    Filename = filename,
                    Moji = slot.Moji,
                });
                slotRows.Add(new FontLibrarySlotRecord
                {
                    Moji = slot.Moji,
                    UnicodeScalar = slot.UnicodeScalar,
                    Style = slot.Style,
                    Width = width,
                    Filename = filename,
                    PackedSha256 = packedHash,
                    PngSha256 = pngHash,
                });
            }

            string manifest = FontBulkManifestCore.Format(
                manifestRows, FontBulkManifestMode.StrictLibrary);
            AddFile(package,
                job.Id + "/" + job.Id + ".fontall.txt",
                FontLibraryText.Utf8(manifest));
            AddFile(package,
                job.Id + "/slots.tsv",
                FontLibraryText.Utf8(FontLibrarySlotTsv.Format(slotRows)));

            FontLibraryJobReport jobReport = CreateJobReport(
                plannedJob,
                FontLibraryHashCore.Hex(
                    packedAggregate.GetHashAndReset()),
                FontLibraryHashCore.Hex(
                    pngAggregate.GetHashAndReset()));
            for (int i = 0; i < plannedJob.Slots.Count; i++)
            {
                FontLibraryPlannedSlot slot = plannedJob.Slots[i];
                FontLibrarySlotRecord row = slotRows[i];
                jobReport.Glyphs.Add(CreateGlyphReport(
                    slot,
                    plannedJob.Manifest.Format,
                    row.Width,
                    row.Filename,
                    row.PackedSha256,
                    row.PngSha256));
            }
            report.Jobs.Add(jobReport);
        }

        static void VerifyAssets(
            FontLibraryJobManifest job,
            FontLibraryJobAssets assets)
        {
            if (assets.FontFileData == null
                || assets.FontFileData.Length == 0
                || assets.FontFileData.Length
                    > FontLibraryManifestCore.MaxFontBytes)
                throw JobError(job,
                    FontLibraryFailureKind.Provenance,
                    "font asset size is invalid");
            if (assets.FontFileData.Length != job.Font.ByteLength)
                throw JobError(job,
                    FontLibraryFailureKind.Provenance,
                    "font byteLength does not match the verified file");
            if (assets.LicenseFileData == null
                || assets.LicenseFileData.Length == 0
                || assets.LicenseFileData.Length
                    > FontLibraryManifestCore.MaxLicenseBytes)
                throw JobError(job,
                    FontLibraryFailureKind.Provenance,
                    "license asset size is invalid");
            if (!string.Equals(
                FontLibraryHashCore.ComputeSha256(assets.FontFileData),
                job.Font.Sha256,
                StringComparison.Ordinal))
                throw JobError(job,
                    FontLibraryFailureKind.Provenance,
                    "font SHA-256 mismatch");
            if (!string.Equals(
                FontLibraryHashCore.ComputeSha256(assets.LicenseFileData),
                job.License.Sha256,
                StringComparison.Ordinal))
                throw JobError(job,
                    FontLibraryFailureKind.Provenance,
                    "license SHA-256 mismatch");
            // A license is text provenance, not an opaque binary sidecar.
            string licenseText =
                FontLibraryText.DecodeStrictUtf8(assets.LicenseFileData);
            if (string.IsNullOrWhiteSpace(licenseText))
                throw JobError(job,
                    FontLibraryFailureKind.Provenance,
                    "license text is empty");
        }

        static FontLibraryJobAssets RequireAssets(
            FontLibraryBuildInput input,
            FontLibraryJobManifest job)
        {
            if (!input.Assets.TryGetValue(
                job.Id, out FontLibraryJobAssets assets)
                || assets == null)
                throw JobError(job,
                    FontLibraryFailureKind.Provenance,
                    "verified font/license assets are missing");
            return assets;
        }

        static void VerifyPlanCapacity(FontLibraryPlan plan)
        {
            long rows = 0;
            foreach (FontLibraryPlannedJob job in plan.Jobs)
            {
                if (job == null)
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Validation,
                        "Plan contains a null job.");
                int jobRows = job.Slots.Count;
                if (jobRows < 1)
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Validation,
                        "Plan contains an empty job.");
                rows += jobRows;
                if (rows > FontLibraryManifestCore.MaxRowsTotal)
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Exhaustion,
                        "Plan exceeds the selected-run style-expanded row cap.");
            }
            if (rows != plan.SlotCount)
                throw new FontLibraryOperationException(
                    FontLibraryFailureKind.Validation,
                    "Plan row count does not match SlotCount.");
        }

        internal static FontLibraryJobReport CreateJobReport(
            FontLibraryPlannedJob plannedJob,
            string packedSha256,
            string pngSha256)
        {
            FontLibraryJobManifest job = plannedJob.Manifest;
            return new FontLibraryJobReport
            {
                Id = job.Id,
                Locale = job.Locale,
                Format = job.Format == FontLibraryFormat.Main16x16
                    ? "main-16x16" : "zh-16x13",
                ScalarCount = CountDistinctScalars(plannedJob.Slots),
                RowCount = plannedJob.Slots.Count,
                CorpusSha256 = job.Corpus?.Sha256 ?? "",
                NativeWidth = 16,
                NativeHeight = job.Format == FontLibraryFormat.Main16x16
                    ? 16 : 13,
                PngWidth = 16,
                PngHeight = 16,
                FontSize = job.Font.Size,
                VerticalOffset = job.VerticalOffset,
                FontByteLength = job.Font.ByteLength,
                FontFamily = job.Font.Family,
                FontVersion = job.Font.Version,
                FontSha256 = job.Font.Sha256,
                FontSourceUrl = job.Font.SourceUrl,
                LicenseId = job.License.LicenseId,
                LicenseFile = job.License.LicenseFile,
                LicenseSha256 = job.License.Sha256,
                LicenseSourceUrl = job.License.SourceUrl,
                PackedSha256 = packedSha256 ?? "",
                PngSha256 = pngSha256 ?? "",
            };
        }

        internal static FontLibraryGlyphReport CreateGlyphReport(
            FontLibraryPlannedSlot slot,
            FontLibraryFormat format,
            int width,
            string filename,
            string packedSha256,
            string pngSha256)
            => new FontLibraryGlyphReport
            {
                UnicodeScalar = slot.UnicodeScalar,
                Character = slot.Character,
                Style = StyleText(slot.Style),
                Moji = slot.Moji,
                Width = width,
                NativeWidth = 16,
                NativeHeight = format == FontLibraryFormat.Main16x16
                    ? 16 : 13,
                PngWidth = 16,
                PngHeight = 16,
                Filename = filename ?? "",
                PackedSha256 = packedSha256 ?? "",
                PngSha256 = pngSha256 ?? "",
            };

        static void AddFile(
            FontLibraryPackage package,
            string relativePath,
            byte[] data)
        {
            if (!FontLibraryPath.IsCanonicalRelative(relativePath))
                throw new InvalidOperationException(
                    "Non-canonical package path: " + relativePath);
            if (data == null)
                throw new InvalidOperationException(
                    "Package file data is null: " + relativePath);
            if (!package.Files.TryAdd(relativePath, data))
                throw new InvalidOperationException(
                    "Duplicate package path: " + relativePath);
        }

        static int CountDistinctScalars(
            List<FontLibraryPlannedSlot> slots)
        {
            var values = new HashSet<int>();
            foreach (FontLibraryPlannedSlot slot in slots)
                values.Add(slot.UnicodeScalar);
            return values.Count;
        }

        internal static bool ContainsInk(byte[] indices)
        {
            for (int i = 0; i < indices.Length; i++)
                if (indices[i] != 0) return true;
            return false;
        }

        internal static string StyleText(FontLibraryStyle style)
            => style == FontLibraryStyle.Item ? "item" : "text";

        static FontLibraryOperationException JobError(
            FontLibraryJobManifest job,
            FontLibraryFailureKind kind,
            string detail)
            => new FontLibraryOperationException(
                kind,
                "Job '" + (job?.Id ?? "") + "': " + detail + ".");

        static FontLibraryBuildResult Fail(
            FontLibraryBuildResult result,
            string error,
            FontLibraryFailureKind kind)
        {
            result.Error = "Font-library build failed: " + error;
            result.FailureKind = kind;
            return result;
        }
    }

    /// <summary>
    /// Strict package validator and non-rasterizing canonical round-trip.
    /// Width is consumed from manifest/slots and only range-validated; it is
    /// never re-derived from pixels.
    /// </summary>
    public static class FontLibraryPackageValidatorCore
    {
        public static FontLibraryValidationResult Validate(
            FontLibraryPlan plan,
            FontLibraryPackage package,
            string mode = "validate",
            string currentManifestSha256 = null)
            => ValidateInternal(
                plan, package, mode, roundTrip: false,
                expectedGenerationReport: null,
                currentManifestSha256);

        public static FontLibraryValidationResult ValidateWithOracle(
            FontLibraryPlan plan,
            FontLibraryPackage package,
            FontLibraryReport expectedGenerationReport,
            string mode = "validate",
            string currentManifestSha256 = null)
            => ValidateInternal(
                plan, package, mode, roundTrip: false,
                expectedGenerationReport,
                currentManifestSha256);

        public static FontLibraryValidationResult RoundTrip(
            FontLibraryPlan plan,
            FontLibraryPackage package,
            FontLibraryReport expectedGenerationReport = null,
            string currentManifestSha256 = null)
            => ValidateInternal(
                plan, package, "roundtrip", roundTrip: true,
                expectedGenerationReport,
                currentManifestSha256);

        static FontLibraryValidationResult ValidateInternal(
            FontLibraryPlan plan,
            FontLibraryPackage package,
            string mode,
            bool roundTrip,
            FontLibraryReport expectedGenerationReport,
            string currentManifestSha256)
        {
            var result = new FontLibraryValidationResult();
            if (plan == null || package == null)
                return Fail(
                    result,
                    "Plan and package are required.",
                    roundTrip,
                    FontLibraryFailureKind.Validation);
            try
            {
                // The external report is the immutable finalized-tree oracle.
                // Check it before trusting or parsing any package member.
                string actualFullTreeSha256 =
                    FontLibraryHashCore.ComputeTreeSha256(package.Files);
                if (expectedGenerationReport != null)
                {
                    if (string.IsNullOrEmpty(
                            expectedGenerationReport.FullTreeSha256))
                    {
                        throw new FontLibraryOperationException(
                            FontLibraryFailureKind.Tamper,
                            "Immutable external report has no finalized full-tree hash.");
                    }
                    if (!string.Equals(
                        expectedGenerationReport.FullTreeSha256,
                        actualFullTreeSha256,
                        StringComparison.Ordinal))
                    {
                        throw new FontLibraryOperationException(
                            FontLibraryFailureKind.Tamper,
                            "Immutable external full-tree hash does not match the finalized package.");
                    }
                }

                var expectedPaths =
                    new HashSet<string>(StringComparer.Ordinal);
                byte[] packageReportBytes = RequireFile(
                    package,
                    expectedPaths,
                    FontLibraryBuilderCore.PackageReportPath);
                if (!FontLibraryReportFormatter.TryParse(
                    packageReportBytes,
                    out FontLibraryReport packageReport,
                    out string packageReportError))
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Tamper,
                        "package-report.json is invalid: "
                        + packageReportError);
                if (!FontLibraryHashCore.BytesEqual(
                    packageReportBytes,
                    FontLibraryText.Utf8(
                        FontLibraryReportFormatter.FormatEmbedded(
                            packageReport))))
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Tamper,
                        "Embedded package report must use the payload-only shape without a self-referential full-tree field.");
                if (packageReport.PayloadTreeSha256.Length == 0)
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Tamper,
                        "Embedded package report has no payload-tree hash.");
                if (!string.IsNullOrEmpty(currentManifestSha256)
                    && !string.Equals(
                        packageReport.ManifestSha256,
                        currentManifestSha256,
                        StringComparison.Ordinal))
                {
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Tamper,
                        "Current manifest SHA-256 does not match the "
                        + "generation manifest recorded by the package.");
                }
                if (expectedGenerationReport != null
                    && !string.Equals(
                        expectedGenerationReport.ManifestSha256,
                        packageReport.ManifestSha256,
                        StringComparison.Ordinal))
                {
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Tamper,
                        "Immutable external report manifest SHA-256 does "
                        + "not match the package.");
                }

                var generationReport = new FontLibraryReport
                {
                    Mode = "generate",
                    Oracle = "generation",
                    ManifestSha256 = currentManifestSha256
                        ?? packageReport.ManifestSha256,
                };
                var rebuilt = roundTrip ? new FontLibraryPackage() : null;

                foreach (FontLibraryPlannedJob plannedJob in plan.Jobs)
                {
                    ValidateJob(
                        package, rebuilt, expectedPaths,
                        plannedJob, generationReport);
                }

                foreach (string path in package.Files.Keys)
                {
                    if (!FontLibraryPath.IsCanonicalRelative(path))
                        throw new InvalidOperationException(
                            "Package contains unsafe path '" + path + "'.");
                    if (!expectedPaths.Contains(path))
                        throw new InvalidOperationException(
                            "Package contains unexpected file '" + path + "'.");
                }
                if (expectedPaths.Count != package.Files.Count)
                    throw new InvalidOperationException(
                        "Package is missing one or more required files.");

                generationReport.PayloadTreeSha256 =
                    ComputePayloadTreeHash(package);
                if (!PayloadReportsEqual(packageReport, generationReport))
                {
                    string expectedReport =
                        FontLibraryReportFormatter.FormatEmbedded(
                            packageReport);
                    string actualReport =
                        FontLibraryReportFormatter.FormatEmbedded(
                            generationReport);
                    int difference =
                        FirstDifference(expectedReport, actualReport);
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Tamper,
                        "package-report.json does not match recomputed "
                        + "package hashes/provenance at character "
                        + difference.ToString(CultureInfo.InvariantCulture)
                        + " (expected "
                        + CharAt(expectedReport, difference)
                        + ", actual "
                        + CharAt(actualReport, difference)
                        + ", field "
                        + FieldAt(expectedReport, difference)
                        + ", contract "
                        + ReportDifference(packageReport, generationReport)
                        + ".");
                }
                if (expectedGenerationReport != null
                    && (!PayloadReportsEqual(
                            expectedGenerationReport, generationReport)
                        || !string.Equals(
                            expectedGenerationReport.PayloadTreeSha256,
                            packageReport.PayloadTreeSha256,
                            StringComparison.Ordinal)))
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Tamper,
                        "immutable external generation report does not match the package.");

                if (roundTrip)
                {
                    generationReport.PayloadTreeSha256 =
                        ComputePayloadTreeHash(rebuilt);
                    rebuilt.Files.Add(
                        FontLibraryBuilderCore.PackageReportPath,
                        FontLibraryText.Utf8(
                            FontLibraryReportFormatter.FormatEmbedded(
                                generationReport)));
                    if (!TreesEqual(package.Files, rebuilt.Files))
                    {
                        result.ReproducibilityDrift = true;
                        result.FailureKind =
                            FontLibraryFailureKind.RoundtripMismatch;
                        result.Error =
                            "Font-library round-trip bytes differ.";
                        result.Report = CreateOutcomeReport(
                            packageReport,
                            mode,
                            expectedGenerationReport != null,
                            actualFullTreeSha256);
                        return result;
                    }
                }

                result.Success = true;
                result.Report = CreateOutcomeReport(
                    packageReport,
                    mode,
                    expectedGenerationReport != null,
                    actualFullTreeSha256);
                return result;
            }
            catch (FontLibraryOperationException ex)
            {
                return Fail(
                    result, ex.Message, roundTrip, ex.Kind);
            }
            catch (Exception ex) when (
                ex is InvalidOperationException
                || ex is ArgumentException
                || ex is FormatException
                || ex is OverflowException)
            {
                return Fail(
                    result,
                    ex.Message,
                    roundTrip,
                    FontLibraryFailureKind.Validation);
            }
        }

        static void ValidateJob(
            FontLibraryPackage package,
            FontLibraryPackage rebuilt,
            HashSet<string> expectedPaths,
            FontLibraryPlannedJob plannedJob,
            FontLibraryReport report)
        {
            FontLibraryJobManifest job = plannedJob.Manifest;
            string manifestPath =
                job.Id + "/" + job.Id + ".fontall.txt";
            string slotsPath = job.Id + "/slots.tsv";
            byte[] manifestBytes =
                RequireFile(package, expectedPaths, manifestPath);
            byte[] slotsBytes =
                RequireFile(package, expectedPaths, slotsPath);
            string manifestText =
                FontLibraryText.DecodeStrictUtf8NoBom(manifestBytes);
            string slotsText =
                FontLibraryText.DecodeStrictUtf8NoBom(slotsBytes);

            FontBulkManifestParseResult parsed =
                FontBulkManifestCore.Parse(
                    manifestText, FontBulkManifestMode.StrictLibrary);
            if (!parsed.Success)
                throw new InvalidOperationException(
                    manifestPath + ": " + parsed.Error);
            List<FontLibrarySlotRecord> records =
                FontLibrarySlotTsv.Parse(slotsText);
            if (records.Count != plannedJob.Slots.Count
                || parsed.Rows.Count != plannedJob.Slots.Count)
                throw new InvalidOperationException(
                    "Job '" + job.Id + "' slot counts do not match the plan.");

            using IncrementalHash packedAggregate =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using IncrementalHash pngAggregate =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var canonicalRows = new List<FontBulkManifestRow>();
            var canonicalRecords = new List<FontLibrarySlotRecord>();

            for (int i = 0; i < plannedJob.Slots.Count; i++)
            {
                FontLibraryPlannedSlot planned = plannedJob.Slots[i];
                FontBulkManifestRow row = parsed.Rows[i];
                FontLibrarySlotRecord record = records[i];
                string expectedStyle =
                    FontLibraryBuilderCore.StyleText(planned.Style);
                string expectedFilename =
                    FontBulkManifestCore.CanonicalFilename(
                        expectedStyle, planned.Moji);
                if (row.Moji != planned.Moji
                    || row.Type != expectedStyle
                    || row.Character != planned.Character
                    || row.Filename != expectedFilename
                    || record.Moji != planned.Moji
                    || record.UnicodeScalar != planned.UnicodeScalar
                    || record.Style != planned.Style
                    || record.Filename != expectedFilename
                    || record.Width != row.Width)
                {
                    throw new InvalidOperationException(
                        "Job '" + job.Id + "' slot "
                        + i.ToString(CultureInfo.InvariantCulture)
                        + " does not match the stable plan/manifest order.");
                }
                if (row.Width < 1 || row.Width > 16)
                    throw new InvalidOperationException(
                        "Stored width is outside 1..16.");

                string pngPath = job.Id + "/" + expectedFilename;
                byte[] png = RequireFile(package, expectedPaths, pngPath);
                string pngHash = FontLibraryHashCore.ComputeSha256(png);
                if (!string.Equals(
                    pngHash, record.PngSha256, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        pngPath + " SHA-256 does not match slots.tsv.");
                IndexedPngCoreReadResult read =
                    IndexedPngCore.Read(png, job.Format, planned.Style);
                if (!read.Success)
                    throw new InvalidOperationException(
                        pngPath + ": " + read.Error);
                if (!FontLibraryBuilderCore.ContainsInk(read.Indices))
                    throw new InvalidOperationException(
                        pngPath + " is an all-background glyph.");
                bool item = planned.Style == FontLibraryStyle.Item;
                byte[] packed;
                byte[] roundTripIndices;
                if (job.Format == FontLibraryFormat.Zh16x13)
                {
                    if (!FontGlyphZHIndexCore.HasZeroPadding(
                        read.Indices, item))
                        throw new InvalidOperationException(
                            pngPath + " has non-zero ZH padding rows.");
                    byte[] native =
                        FontGlyphZHIndexCore.UnshiftTo16x13(
                            read.Indices, item);
                    packed = FontGlyphZHCore.PackGlyphZHBytes(
                        native, row.Width);
                    roundTripIndices =
                        FontGlyphZHIndexCore.ShiftTo16x16(
                            FontGlyphZHIndexCore.UnpackGlyphBytes(
                                packed, row.Width),
                            item);
                }
                else
                {
                    packed =
                        IndexedPngCore.PackEngineTile(read.Indices);
                    roundTripIndices =
                        IndexedPngCore.UnpackEngineTile(packed);
                }
                if (packed == null)
                    throw new InvalidOperationException(
                        pngPath + " cannot be packed.");
                string packedHash =
                    FontLibraryHashCore.ComputeSha256(packed);
                if (!string.Equals(
                    packedHash, record.PackedSha256,
                    StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        pngPath + " packed SHA-256 does not match slots.tsv.");
                packedAggregate.AppendData(packed);
                pngAggregate.AppendData(png);

                canonicalRows.Add(new FontBulkManifestRow
                {
                    Character = planned.Character,
                    Type = expectedStyle,
                    Width = row.Width,
                    Filename = expectedFilename,
                    Moji = planned.Moji,
                });
                if (rebuilt != null)
                {
                    byte[] roundTripPng = IndexedPngCore.Encode(
                        roundTripIndices, job.Format, planned.Style);
                    rebuilt.Files.Add(pngPath, roundTripPng);
                    canonicalRecords.Add(new FontLibrarySlotRecord
                    {
                        Moji = record.Moji,
                        UnicodeScalar = record.UnicodeScalar,
                        Style = record.Style,
                        Width = record.Width,
                        Filename = record.Filename,
                        PackedSha256 = packedHash,
                        PngSha256 =
                            FontLibraryHashCore.ComputeSha256(roundTripPng),
                    });
                }
                else
                {
                    canonicalRecords.Add(record);
                }
            }

            string canonicalManifest = FontBulkManifestCore.Format(
                canonicalRows, FontBulkManifestMode.StrictLibrary);
            string canonicalInputSlots =
                FontLibrarySlotTsv.Format(records);
            byte[] canonicalManifestBytes =
                FontLibraryText.Utf8(canonicalManifest);
            byte[] canonicalInputSlotsBytes =
                FontLibraryText.Utf8(canonicalInputSlots);
            if (!FontLibraryHashCore.BytesEqual(
                manifestBytes, canonicalManifestBytes))
                throw new InvalidOperationException(
                    manifestPath + " is not canonical.");
            if (!FontLibraryHashCore.BytesEqual(
                slotsBytes, canonicalInputSlotsBytes))
                throw new InvalidOperationException(
                    slotsPath + " is not canonical.");
            if (rebuilt != null)
            {
                rebuilt.Files.Add(manifestPath, canonicalManifestBytes);
                rebuilt.Files.Add(
                    slotsPath,
                    FontLibraryText.Utf8(
                        FontLibrarySlotTsv.Format(canonicalRecords)));
            }

            FontLibraryJobReport jobReport =
                FontLibraryBuilderCore.CreateJobReport(
                plannedJob,
                FontLibraryHashCore.Hex(
                    packedAggregate.GetHashAndReset()),
                FontLibraryHashCore.Hex(
                    pngAggregate.GetHashAndReset()));
            for (int i = 0; i < plannedJob.Slots.Count; i++)
            {
                FontLibraryPlannedSlot slot = plannedJob.Slots[i];
                FontLibrarySlotRecord record = records[i];
                jobReport.Glyphs.Add(
                    FontLibraryBuilderCore.CreateGlyphReport(
                        slot,
                        job.Format,
                        record.Width,
                        record.Filename,
                        record.PackedSha256,
                        record.PngSha256));
            }
            report.Jobs.Add(jobReport);
        }

        static byte[] RequireFile(
            FontLibraryPackage package,
            HashSet<string> expectedPaths,
            string path)
        {
            expectedPaths.Add(path);
            if (!package.Files.TryGetValue(path, out byte[] data)
                || data == null)
                throw new InvalidOperationException(
                    "Package is missing '" + path + "'.");
            return data;
        }

        static int CountDistinctScalars(
            List<FontLibraryPlannedSlot> slots)
        {
            var values = new HashSet<int>();
            foreach (FontLibraryPlannedSlot slot in slots)
                values.Add(slot.UnicodeScalar);
            return values.Count;
        }

        static bool TreesEqual(
            SortedDictionary<string, byte[]> left,
            SortedDictionary<string, byte[]> right)
        {
            if (left.Count != right.Count) return false;
            using IEnumerator<KeyValuePair<string, byte[]>> a =
                left.GetEnumerator();
            using IEnumerator<KeyValuePair<string, byte[]>> b =
                right.GetEnumerator();
            while (a.MoveNext())
            {
                if (!b.MoveNext()
                    || !string.Equals(
                        a.Current.Key, b.Current.Key,
                        StringComparison.Ordinal)
                    || !FontLibraryHashCore.BytesEqual(
                        a.Current.Value, b.Current.Value))
                    return false;
            }
            return !b.MoveNext();
        }

        static string ComputePayloadTreeHash(
            FontLibraryPackage package)
        {
            var payload =
                new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, byte[]> file in package.Files)
            {
                if (string.Equals(
                    file.Key,
                    FontLibraryBuilderCore.PackageReportPath,
                    StringComparison.Ordinal))
                    continue;
                payload.Add(file.Key, file.Value);
            }
            return FontLibraryHashCore.ComputeTreeSha256(payload);
        }

        static bool PayloadReportsEqual(
            FontLibraryReport left,
            FontLibraryReport right)
            => left != null && right != null
                && string.Equals(
                    FontLibraryReportFormatter.FormatEmbedded(left),
                    FontLibraryReportFormatter.FormatEmbedded(right),
                    StringComparison.Ordinal);

        static int FirstDifference(string left, string right)
        {
            int length = Math.Min(left.Length, right.Length);
            for (int i = 0; i < length; i++)
                if (left[i] != right[i])
                    return i;
            return left.Length == right.Length ? -1 : length;
        }

        static string CharAt(string value, int index)
            => index >= 0 && index < value.Length
                ? "U+" + ((int)value[index]).ToString(
                    "X4", CultureInfo.InvariantCulture)
                : "<eof>";

        static string FieldAt(string value, int index)
        {
            if (index < 0) return "<none>";
            int colon = value.LastIndexOf(':', Math.Min(
                index, value.Length - 1));
            if (colon < 1) return "<unknown>";
            int end = value.LastIndexOf('"', colon - 1);
            if (end < 1) return "<unknown>";
            int start = value.LastIndexOf('"', end - 1);
            return start >= 0
                ? value.Substring(start + 1, end - start - 1)
                : "<unknown>";
        }

        static string ReportDifference(
            FontLibraryReport expected,
            FontLibraryReport actual)
        {
            if (expected.Jobs.Count != actual.Jobs.Count)
                return "jobs.count";
            for (int i = 0; i < expected.Jobs.Count; i++)
            {
                FontLibraryJobReport left = expected.Jobs[i];
                FontLibraryJobReport right = actual.Jobs[i];
                string prefix = "jobs[" + i.ToString(
                    CultureInfo.InvariantCulture) + "]";
                if (left.PackedSha256 != right.PackedSha256)
                    return prefix + ".packedSha256";
                if (left.PngSha256 != right.PngSha256)
                    return prefix + ".pngSha256";
                if (left.Glyphs.Count != right.Glyphs.Count)
                    return prefix + ".glyphs.count";
                for (int glyph = 0; glyph < left.Glyphs.Count; glyph++)
                {
                    FontLibraryGlyphReport a = left.Glyphs[glyph];
                    FontLibraryGlyphReport b = right.Glyphs[glyph];
                    if (a.PackedSha256 != b.PackedSha256)
                        return prefix + ".glyphs["
                            + glyph.ToString(CultureInfo.InvariantCulture)
                            + "].packedSha256";
                    if (a.PngSha256 != b.PngSha256)
                        return prefix + ".glyphs["
                            + glyph.ToString(CultureInfo.InvariantCulture)
                            + "].pngSha256(expected="
                            + a.PngSha256.Substring(0, 8)
                            + ",actual="
                            + b.PngSha256.Substring(0, 8) + ")";
                }
            }
            return "other";
        }

        static FontLibraryReport CreateOutcomeReport(
            FontLibraryReport generation,
            string mode,
            bool hasExternalOracle,
            string fullTreeSha256)
        {
            var result = new FontLibraryReport
            {
                Mode = mode,
                Oracle = hasExternalOracle
                    ? "immutable-external-report"
                    : "internal-consistency-only",
                ManifestSha256 = generation.ManifestSha256,
                PayloadTreeSha256 = generation.PayloadTreeSha256,
                FullTreeSha256 = fullTreeSha256,
            };
            foreach (FontLibraryJobReport job in generation.Jobs)
            {
                result.Jobs.Add(new FontLibraryJobReport
                {
                    Id = job.Id,
                    Locale = job.Locale,
                    Format = job.Format,
                    ScalarCount = job.ScalarCount,
                    RowCount = job.RowCount,
                    CorpusSha256 = job.CorpusSha256,
                    NativeWidth = job.NativeWidth,
                    NativeHeight = job.NativeHeight,
                    PngWidth = job.PngWidth,
                    PngHeight = job.PngHeight,
                    FontSize = job.FontSize,
                    VerticalOffset = job.VerticalOffset,
                    FontByteLength = job.FontByteLength,
                    FontFamily = job.FontFamily,
                    FontVersion = job.FontVersion,
                    FontSha256 = job.FontSha256,
                    FontSourceUrl = job.FontSourceUrl,
                    LicenseId = job.LicenseId,
                    LicenseFile = job.LicenseFile,
                    LicenseSha256 = job.LicenseSha256,
                    LicenseSourceUrl = job.LicenseSourceUrl,
                    PackedSha256 = job.PackedSha256,
                    PngSha256 = job.PngSha256,
                });
                FontLibraryJobReport copied =
                    result.Jobs[result.Jobs.Count - 1];
                foreach (FontLibraryGlyphReport glyph in job.Glyphs)
                {
                    copied.Glyphs.Add(new FontLibraryGlyphReport
                    {
                        UnicodeScalar = glyph.UnicodeScalar,
                        Character = glyph.Character,
                        Style = glyph.Style,
                        Moji = glyph.Moji,
                        Width = glyph.Width,
                        NativeWidth = glyph.NativeWidth,
                        NativeHeight = glyph.NativeHeight,
                        PngWidth = glyph.PngWidth,
                        PngHeight = glyph.PngHeight,
                        Filename = glyph.Filename,
                        PackedSha256 = glyph.PackedSha256,
                        PngSha256 = glyph.PngSha256,
                    });
                }
            }
            foreach (FontLibraryOutcomeReport outcome in generation.Outcomes)
                result.Outcomes.Add(outcome);
            return result;
        }

        static FontLibraryValidationResult Fail(
            FontLibraryValidationResult result,
            string error,
            bool roundTrip,
            FontLibraryFailureKind kind)
        {
            result.Error = "Font-library "
                + (roundTrip ? "round-trip" : "validation")
                + " failed: " + error;
            result.FailureKind = kind;
            result.ReproducibilityDrift =
                kind == FontLibraryFailureKind.RoundtripMismatch;
            return result;
        }
    }

    public static class FontLibraryReportFormatter
    {
        public const int MaxReportBytes = 64 * 1024 * 1024;

        /// <summary>
        /// Canonical external JSON plus one final LF. A finalized report emits
        /// both the payload and full-tree digests.
        /// </summary>
        public static string Format(FontLibraryReport report)
            => FormatCore(
                report,
                includeFullTree: true);

        /// <summary>
        /// Canonical embedded JSON. The full-tree digest is deliberately
        /// omitted because package-report.json cannot hash itself.
        /// </summary>
        public static string FormatEmbedded(FontLibraryReport report)
            => FormatCore(report, includeFullTree: false);

        static string FormatCore(
            FontLibraryReport report,
            bool includeFullTree)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var sb = new StringBuilder();
            sb.Append("{\"schemaVersion\":1,\"mode\":");
            AppendJsonString(sb, report.Mode);
            sb.Append(",\"oracle\":");
            AppendJsonString(sb, report.Oracle);
            sb.Append(",\"manifestSha256\":");
            AppendJsonString(sb, report.ManifestSha256);
            sb.Append(",\"jobs\":[");
            for (int i = 0; i < report.Jobs.Count; i++)
            {
                if (i != 0) sb.Append(',');
                FontLibraryJobReport job = report.Jobs[i];
                sb.Append("{\"id\":");
                AppendJsonString(sb, job.Id);
                sb.Append(",\"locale\":");
                AppendJsonString(sb, job.Locale);
                sb.Append(",\"format\":");
                AppendJsonString(sb, job.Format);
                sb.Append(",\"scalarCount\":");
                sb.Append(job.ScalarCount.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"rowCount\":");
                sb.Append(job.RowCount.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"corpusSha256\":");
                AppendJsonString(sb, job.CorpusSha256);
                sb.Append(",\"dimensions\":{\"nativeWidth\":");
                sb.Append(job.NativeWidth.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"nativeHeight\":");
                sb.Append(job.NativeHeight.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"pngWidth\":");
                sb.Append(job.PngWidth.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"pngHeight\":");
                sb.Append(job.PngHeight.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
                sb.Append(",\"verticalOffset\":");
                sb.Append(job.VerticalOffset.ToString(
                    CultureInfo.InvariantCulture));
                sb.Append(",\"font\":{\"byteLength\":");
                sb.Append(job.FontByteLength.ToString(
                    CultureInfo.InvariantCulture));
                sb.Append(",\"size\":");
                sb.Append(job.FontSize.ToString(
                    "R", CultureInfo.InvariantCulture));
                sb.Append(",\"family\":");
                AppendJsonString(sb, job.FontFamily);
                sb.Append(",\"version\":");
                AppendJsonString(sb, job.FontVersion);
                sb.Append(",\"sha256\":");
                AppendJsonString(sb, job.FontSha256);
                sb.Append(",\"sourceUrl\":");
                AppendJsonString(sb, job.FontSourceUrl);
                sb.Append("},\"license\":{\"licenseId\":");
                AppendJsonString(sb, job.LicenseId);
                sb.Append(",\"licenseFile\":");
                AppendJsonString(sb, job.LicenseFile);
                sb.Append(",\"sha256\":");
                AppendJsonString(sb, job.LicenseSha256);
                sb.Append(",\"sourceUrl\":");
                AppendJsonString(sb, job.LicenseSourceUrl);
                sb.Append('}');
                sb.Append(",\"packedSha256\":");
                AppendJsonString(sb, job.PackedSha256);
                sb.Append(",\"pngSha256\":");
                AppendJsonString(sb, job.PngSha256);
                sb.Append(",\"glyphs\":[");
                for (int glyphIndex = 0;
                    glyphIndex < job.Glyphs.Count;
                    glyphIndex++)
                {
                    if (glyphIndex != 0) sb.Append(',');
                    FontLibraryGlyphReport glyph =
                        job.Glyphs[glyphIndex];
                    sb.Append("{\"unicodeScalar\":");
                    sb.Append(glyph.UnicodeScalar.ToString(
                        CultureInfo.InvariantCulture));
                    sb.Append(",\"character\":");
                    AppendJsonString(sb, glyph.Character);
                    sb.Append(",\"style\":");
                    AppendJsonString(sb, glyph.Style);
                    sb.Append(",\"moji\":");
                    sb.Append(glyph.Moji.ToString(
                        CultureInfo.InvariantCulture));
                    sb.Append(",\"width\":");
                    sb.Append(glyph.Width.ToString(
                        CultureInfo.InvariantCulture));
                    sb.Append(",\"nativeWidth\":");
                    sb.Append(glyph.NativeWidth.ToString(
                        CultureInfo.InvariantCulture));
                    sb.Append(",\"nativeHeight\":");
                    sb.Append(glyph.NativeHeight.ToString(
                        CultureInfo.InvariantCulture));
                    sb.Append(",\"pngWidth\":");
                    sb.Append(glyph.PngWidth.ToString(
                        CultureInfo.InvariantCulture));
                    sb.Append(",\"pngHeight\":");
                    sb.Append(glyph.PngHeight.ToString(
                        CultureInfo.InvariantCulture));
                    sb.Append(",\"filename\":");
                    AppendJsonString(sb, glyph.Filename);
                    sb.Append(",\"packedSha256\":");
                    AppendJsonString(sb, glyph.PackedSha256);
                    sb.Append(",\"pngSha256\":");
                    AppendJsonString(sb, glyph.PngSha256);
                    sb.Append('}');
                }
                sb.Append(']');
                sb.Append('}');
            }
            sb.Append("],\"outcomes\":[");
            for (int i = 0; i < report.Outcomes.Count; i++)
            {
                if (i != 0) sb.Append(',');
                FontLibraryOutcomeReport outcome = report.Outcomes[i];
                sb.Append("{\"kind\":");
                AppendJsonString(sb, outcome.Kind);
                sb.Append(",\"jobId\":");
                AppendJsonString(sb, outcome.JobId);
                sb.Append(",\"unicodeScalar\":");
                sb.Append(outcome.UnicodeScalar.ToString(
                    CultureInfo.InvariantCulture));
                sb.Append(",\"moji\":");
                sb.Append(outcome.Moji.ToString(
                    CultureInfo.InvariantCulture));
                sb.Append(",\"message\":");
                AppendJsonString(sb, outcome.Message);
                sb.Append('}');
            }
            sb.Append("],\"payloadTreeSha256\":");
            AppendJsonString(sb, report.PayloadTreeSha256);
            if (includeFullTree)
            {
                sb.Append(",\"fullTreeSha256\":");
                AppendJsonString(sb, report.FullTreeSha256);
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Parse only the exact canonical external or embedded grammar emitted
        /// by <see cref="Format"/> / <see cref="FormatEmbedded"/>. This makes
        /// an external report an immutable byte-stable oracle rather than a
        /// permissive JSON hint.
        /// </summary>
        public static bool TryParse(
            byte[] utf8,
            out FontLibraryReport report,
            out string error)
        {
            report = null;
            error = "";
            try
            {
                if (utf8 == null || utf8.Length < 1
                    || utf8.Length > MaxReportBytes)
                    throw new FormatException(
                        "Report size is outside the bounded range.");
                string json = FontLibraryText.DecodeStrictUtf8NoBom(utf8);
                using JsonDocument document = JsonDocument.Parse(
                    json,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 16,
                    });
                JsonElement root = document.RootElement;
                RequireObject(root, "report");
                bool hasFullTree =
                    root.TryGetProperty("fullTreeSha256", out _);
                if (hasFullTree)
                {
                    RequireProperties(
                        root,
                        "report",
                        "schemaVersion", "mode", "oracle",
                        "manifestSha256", "jobs", "outcomes",
                        "payloadTreeSha256", "fullTreeSha256");
                }
                else
                {
                    RequireProperties(
                        root,
                        "report",
                        "schemaVersion", "mode", "oracle",
                        "manifestSha256", "jobs", "outcomes",
                        "payloadTreeSha256");
                }
                if (RequireInt(root, "schemaVersion", "report") != 1)
                    throw new FormatException(
                        "report.schemaVersion must be 1.");
                var parsed = new FontLibraryReport
                {
                    Mode = RequireString(root, "mode", "report"),
                    Oracle = RequireString(root, "oracle", "report"),
                    ManifestSha256 = RequireString(
                        root,
                        "manifestSha256",
                        "report",
                        allowEmpty: true),
                    PayloadTreeSha256 = RequireString(
                        root,
                        "payloadTreeSha256",
                        "report",
                        allowEmpty: true),
                    FullTreeSha256 = hasFullTree
                        ? RequireString(
                            root,
                            "fullTreeSha256",
                            "report",
                            allowEmpty: true)
                        : "",
                };
                ValidateHashOrEmpty(
                    parsed.ManifestSha256,
                    "report.manifestSha256");
                ValidateHashOrEmpty(
                    parsed.PayloadTreeSha256,
                    "report.payloadTreeSha256");
                ValidateHashOrEmpty(
                    parsed.FullTreeSha256,
                    "report.fullTreeSha256");
                JsonElement jobs = Require(
                    root, "jobs", JsonValueKind.Array, "report");
                if (jobs.GetArrayLength()
                    > FontLibraryManifestCore.MaxJobs)
                    throw new FormatException(
                        "report.jobs exceeds the cap.");
                string priorId = null;
                int index = 0;
                foreach (JsonElement element in jobs.EnumerateArray())
                {
                    string context = "report.jobs["
                        + index.ToString(CultureInfo.InvariantCulture) + "]";
                    RequireObject(element, context);
                    RequireProperties(
                        element,
                        context,
                        "id", "locale", "format", "scalarCount",
                        "rowCount", "corpusSha256", "dimensions",
                        "verticalOffset",
                        "font", "license", "packedSha256",
                        "pngSha256", "glyphs");
                    JsonElement dimensions = Require(
                        element,
                        "dimensions",
                        JsonValueKind.Object,
                        context);
                    RequireProperties(
                        dimensions,
                        context + ".dimensions",
                        "nativeWidth", "nativeHeight",
                        "pngWidth", "pngHeight");
                    JsonElement font = Require(
                        element, "font", JsonValueKind.Object, context);
                    RequireProperties(
                        font,
                        context + ".font",
                        "byteLength", "size", "family", "version", "sha256",
                        "sourceUrl");
                    JsonElement license = Require(
                        element, "license", JsonValueKind.Object, context);
                    RequireProperties(
                        license,
                        context + ".license",
                        "licenseId", "licenseFile", "sha256",
                        "sourceUrl");
                    var job = new FontLibraryJobReport
                    {
                        Id = RequireString(element, "id", context),
                        Locale = RequireString(
                            element, "locale", context),
                        Format = RequireString(
                            element, "format", context),
                        ScalarCount = RequireInt(
                            element, "scalarCount", context),
                        RowCount = RequireInt(
                            element, "rowCount", context),
                        CorpusSha256 = RequireString(
                            element,
                            "corpusSha256",
                            context,
                            allowEmpty: true),
                        NativeWidth = RequireInt(
                            dimensions, "nativeWidth",
                            context + ".dimensions"),
                        NativeHeight = RequireInt(
                            dimensions, "nativeHeight",
                            context + ".dimensions"),
                        PngWidth = RequireInt(
                            dimensions, "pngWidth",
                            context + ".dimensions"),
                        PngHeight = RequireInt(
                            dimensions, "pngHeight",
                            context + ".dimensions"),
                        VerticalOffset = RequireInt(
                            element, "verticalOffset", context),
                        FontByteLength = RequireInt(
                            font, "byteLength", context + ".font"),
                        FontSize = RequireSingle(
                            font, "size", context + ".font"),
                        FontFamily = RequireString(
                            font, "family", context + ".font"),
                        FontVersion = RequireString(
                            font, "version", context + ".font"),
                        FontSha256 = RequireString(
                            font, "sha256", context + ".font"),
                        FontSourceUrl = RequireString(
                            font, "sourceUrl", context + ".font"),
                        LicenseId = RequireString(
                            license, "licenseId", context + ".license"),
                        LicenseFile = RequireString(
                            license, "licenseFile", context + ".license"),
                        LicenseSha256 = RequireString(
                            license, "sha256", context + ".license"),
                        LicenseSourceUrl = RequireString(
                            license, "sourceUrl", context + ".license"),
                        PackedSha256 = RequireString(
                            element, "packedSha256", context,
                            allowEmpty: true),
                        PngSha256 = RequireString(
                            element, "pngSha256", context,
                            allowEmpty: true),
                    };
                    if (job.ScalarCount < 1
                        || job.RowCount < 1
                        || job.FontByteLength < 1)
                        throw new FormatException(
                            context + " counts/byteLength must be positive.");
                    ValidateHashOrEmpty(
                        job.CorpusSha256, context + ".corpusSha256");
                    ValidateHashOrEmpty(
                        job.FontSha256, context + ".font.sha256");
                    ValidateHashOrEmpty(
                        job.LicenseSha256, context + ".license.sha256");
                    ValidateHashOrEmpty(
                        job.PackedSha256, context + ".packedSha256");
                    ValidateHashOrEmpty(
                        job.PngSha256, context + ".pngSha256");
                    if ((job.Format != "main-16x16"
                            && job.Format != "zh-16x13")
                        || job.NativeWidth != 16
                        || job.NativeHeight
                            != (job.Format == "main-16x16" ? 16 : 13)
                        || job.PngWidth != 16
                        || job.PngHeight != 16
                        || float.IsNaN(job.FontSize)
                        || float.IsInfinity(job.FontSize)
                        || job.FontSize <= 0
                        || job.FontSize > 256)
                    {
                        throw new FormatException(
                            context + " format/dimensions are invalid.");
                    }
                    JsonElement glyphs = Require(
                        element, "glyphs", JsonValueKind.Array, context);
                    if (glyphs.GetArrayLength()
                        > FontLibraryManifestCore.MaxRowsTotal)
                        throw new FormatException(
                            context + ".glyphs exceeds the row cap.");
                    int glyphIndex = 0;
                    uint priorMoji = 0;
                    int priorStyle = -1;
                    bool hasPriorGlyph = false;
                    foreach (JsonElement glyphElement
                        in glyphs.EnumerateArray())
                    {
                        string glyphContext = context + ".glyphs["
                            + glyphIndex.ToString(
                                CultureInfo.InvariantCulture) + "]";
                        RequireObject(glyphElement, glyphContext);
                        RequireProperties(
                            glyphElement,
                            glyphContext,
                            "unicodeScalar", "character", "style",
                            "moji", "width", "nativeWidth",
                            "nativeHeight", "pngWidth", "pngHeight",
                            "filename", "packedSha256", "pngSha256");
                        string style = RequireString(
                            glyphElement, "style", glyphContext);
                        if (style != "item" && style != "text")
                            throw new FormatException(
                                glyphContext + ".style is invalid.");
                        int styleRank = style == "item" ? 0 : 1;
                        int scalar = RequireInt(
                            glyphElement,
                            "unicodeScalar",
                            glyphContext);
                        uint mojiValue = RequireUInt(
                            glyphElement, "moji", glyphContext);
                        if (scalar < 0)
                            throw new FormatException(
                                glyphContext
                                + " scalar/moji must be nonnegative.");
                        var glyph = new FontLibraryGlyphReport
                        {
                            UnicodeScalar = scalar,
                            Character = RequireString(
                                glyphElement,
                                "character",
                                glyphContext),
                            Style = style,
                            Moji = mojiValue,
                            Width = RequireInt(
                                glyphElement, "width", glyphContext),
                            NativeWidth = RequireInt(
                                glyphElement,
                                "nativeWidth",
                                glyphContext),
                            NativeHeight = RequireInt(
                                glyphElement,
                                "nativeHeight",
                                glyphContext),
                            PngWidth = RequireInt(
                                glyphElement,
                                "pngWidth",
                                glyphContext),
                            PngHeight = RequireInt(
                                glyphElement,
                                "pngHeight",
                                glyphContext),
                            Filename = RequireString(
                                glyphElement,
                                "filename",
                                glyphContext,
                                allowEmpty: true),
                            PackedSha256 = RequireString(
                                glyphElement,
                                "packedSha256",
                                glyphContext,
                                allowEmpty: true),
                            PngSha256 = RequireString(
                                glyphElement,
                                "pngSha256",
                                glyphContext,
                                allowEmpty: true),
                        };
                        bool dryRun = parsed.Mode == "dry-run";
                        if ((!dryRun
                                && (glyph.Width < 1
                                    || glyph.Width > 16))
                            || (dryRun && glyph.Width != 0)
                            || glyph.NativeWidth != 16
                            || glyph.NativeHeight != job.NativeHeight
                            || glyph.PngWidth != 16
                            || glyph.PngHeight != 16)
                        {
                            throw new FormatException(
                                glyphContext
                                + " width/dimensions are invalid.");
                        }
                        ValidateHashOrEmpty(
                            glyph.PackedSha256,
                            glyphContext + ".packedSha256");
                        ValidateHashOrEmpty(
                            glyph.PngSha256,
                            glyphContext + ".pngSha256");
                        if (!dryRun
                            && (glyph.PackedSha256.Length != 64
                                || glyph.PngSha256.Length != 64
                                || glyph.Filename.Length == 0))
                        {
                            throw new FormatException(
                                glyphContext
                                + " generated hashes/filename are required.");
                        }
                        if (hasPriorGlyph
                            && (glyph.Moji < priorMoji
                                || (glyph.Moji == priorMoji
                                    && styleRank <= priorStyle)))
                        {
                            throw new FormatException(
                                context
                                + ".glyphs are not canonically ordered.");
                        }
                        priorMoji = glyph.Moji;
                        priorStyle = styleRank;
                        hasPriorGlyph = true;
                        job.Glyphs.Add(glyph);
                        glyphIndex++;
                    }
                    if (job.Glyphs.Count != job.RowCount)
                        throw new FormatException(
                            context + ".glyphs count does not match rowCount.");
                    if (priorId != null
                        && StringComparer.Ordinal.Compare(
                            priorId, job.Id) >= 0)
                        throw new FormatException(
                            "report job ids must be strictly ordinal-sorted.");
                    priorId = job.Id;
                    parsed.Jobs.Add(job);
                    index++;
                }
                JsonElement outcomes = Require(
                    root, "outcomes", JsonValueKind.Array, "report");
                if (outcomes.GetArrayLength()
                    > FontLibraryManifestCore.MaxRowsTotal)
                    throw new FormatException(
                        "report.outcomes exceeds the row cap.");
                int outcomeIndex = 0;
                foreach (JsonElement outcomeElement
                    in outcomes.EnumerateArray())
                {
                    string context = "report.outcomes["
                        + outcomeIndex.ToString(
                            CultureInfo.InvariantCulture) + "]";
                    RequireObject(outcomeElement, context);
                    RequireProperties(
                        outcomeElement,
                        context,
                        "kind", "jobId", "unicodeScalar",
                        "moji", "message");
                    uint moji = RequireUInt(
                        outcomeElement, "moji", context);
                    parsed.Outcomes.Add(new FontLibraryOutcomeReport
                    {
                        Kind = RequireString(
                            outcomeElement, "kind", context),
                        JobId = RequireString(
                            outcomeElement,
                            "jobId",
                            context,
                            allowEmpty: true),
                        UnicodeScalar = RequireInt(
                            outcomeElement,
                            "unicodeScalar",
                            context),
                        Moji = moji,
                        Message = RequireString(
                            outcomeElement, "message", context),
                    });
                    outcomeIndex++;
                }
                if (!string.Equals(
                    hasFullTree
                        ? Format(parsed)
                        : FormatEmbedded(parsed),
                    json,
                    StringComparison.Ordinal))
                    throw new FormatException(
                        "Report JSON is not in canonical byte form.");
                report = parsed;
                return true;
            }
            catch (Exception ex) when (
                ex is JsonException
                || ex is FormatException
                || ex is InvalidOperationException
                || ex is DecoderFallbackException
                || ex is OverflowException)
            {
                error = ex.Message;
                report = null;
                return false;
            }
        }

        static JsonElement Require(
            JsonElement element,
            string name,
            JsonValueKind kind,
            string context)
        {
            if (!element.TryGetProperty(name, out JsonElement value)
                || value.ValueKind != kind)
                throw new FormatException(
                    context + "." + name + " must be "
                    + kind.ToString().ToLowerInvariant() + ".");
            return value;
        }

        static void RequireObject(JsonElement element, string context)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new FormatException(context + " must be an object.");
        }

        static void RequireProperties(
            JsonElement element,
            string context,
            params string[] expected)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new FormatException(
                        context + " contains duplicate property '"
                        + property.Name + "'.");
            }
            if (names.Count != expected.Length)
                throw new FormatException(
                    context + " has missing/unknown properties.");
            foreach (string name in expected)
            {
                if (!names.Contains(name))
                    throw new FormatException(
                        context + " is missing property '" + name + "'.");
            }
        }

        static int RequireInt(
            JsonElement element,
            string name,
            string context)
        {
            JsonElement value = Require(
                element, name, JsonValueKind.Number, context);
            if (!value.TryGetInt32(out int number))
                throw new FormatException(
                    context + "." + name + " must be an int32.");
            return number;
        }

        static uint RequireUInt(
            JsonElement element,
            string name,
            string context)
        {
            JsonElement value = Require(
                element, name, JsonValueKind.Number, context);
            if (!value.TryGetUInt32(out uint number))
                throw new FormatException(
                    context + "." + name + " must be a uint32.");
            return number;
        }

        static float RequireSingle(
            JsonElement element,
            string name,
            string context)
        {
            JsonElement value = Require(
                element, name, JsonValueKind.Number, context);
            if (!value.TryGetSingle(out float number))
                throw new FormatException(
                    context + "." + name + " must be a single.");
            return number;
        }

        static string RequireString(
            JsonElement element,
            string name,
            string context,
            bool allowEmpty = false)
        {
            JsonElement value = Require(
                element, name, JsonValueKind.String, context);
            string text = value.GetString();
            if (text == null || (!allowEmpty && text.Length == 0)
                || text.Length > 4096)
                throw new FormatException(
                    context + "." + name
                    + " must be a bounded"
                    + (allowEmpty ? "" : " non-empty")
                    + " string.");
            return text;
        }

        static void ValidateHashOrEmpty(string value, string context)
        {
            if (value.Length == 0) return;
            if (value.Length != 64)
                throw new FormatException(
                    context + " must be lowercase SHA-256.");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')))
                    throw new FormatException(
                        context + " must be lowercase SHA-256.");
            }
        }

        static void AppendJsonString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u")
                                .Append(((int)c).ToString(
                                    "X4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    internal sealed class FontLibrarySlotRecord
    {
        internal uint Moji;
        internal int UnicodeScalar;
        internal FontLibraryStyle Style;
        internal int Width;
        internal string Filename = "";
        internal string PackedSha256 = "";
        internal string PngSha256 = "";
    }

    internal static class FontLibrarySlotTsv
    {
        internal const string Header =
            "moji\tunicode\tstyle\twidth\tfilename\tpackedSha256\tpngSha256";

        internal static string Format(List<FontLibrarySlotRecord> rows)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (FontLibrarySlotRecord row in rows)
            {
                sb.Append(U.ToHexString(row.Moji)).Append('\t');
                sb.Append("U+").Append(row.UnicodeScalar.ToString(
                    "X4", CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(FontLibraryBuilderCore.StyleText(row.Style)).Append('\t');
                sb.Append(row.Width.ToString(CultureInfo.InvariantCulture))
                    .Append('\t');
                sb.Append(row.Filename).Append('\t');
                sb.Append(row.PackedSha256).Append('\t');
                sb.Append(row.PngSha256).Append('\n');
            }
            return sb.ToString();
        }

        internal static List<FontLibrarySlotRecord> Parse(string text)
        {
            if (text == null || text.IndexOf('\r') >= 0)
                throw new InvalidOperationException(
                    "slots.tsv must use strict LF text.");
            string[] lines = text.Split('\n');
            if (lines.Length < 2
                || !string.Equals(lines[0], Header, StringComparison.Ordinal)
                || lines[lines.Length - 1].Length != 0)
                throw new InvalidOperationException(
                    "slots.tsv has a non-canonical header/ending.");
            var rows = new List<FontLibrarySlotRecord>();
            for (int i = 1; i < lines.Length - 1; i++)
            {
                if (lines[i].Length == 0)
                    throw new InvalidOperationException(
                        "slots.tsv contains a blank row.");
                string[] columns = lines[i].Split('\t');
                if (columns.Length != 7)
                    throw new InvalidOperationException(
                        "slots.tsv rows require exactly seven columns.");
                if (!uint.TryParse(columns[0], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out uint moji)
                    || columns[0] != U.ToHexString(moji))
                    throw new InvalidOperationException(
                        "slots.tsv moji is not canonical.");
                int scalar = ParseScalar(columns[1]);
                FontLibraryStyle style = columns[2] switch
                {
                    "item" => FontLibraryStyle.Item,
                    "text" => FontLibraryStyle.Text,
                    _ => throw new InvalidOperationException(
                        "slots.tsv style is invalid."),
                };
                if (!int.TryParse(columns[3], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int width)
                    || width < 1 || width > 16
                    || columns[3] != width.ToString(
                        CultureInfo.InvariantCulture))
                    throw new InvalidOperationException(
                        "slots.tsv width is invalid.");
                if (columns[4]
                    != FontBulkManifestCore.CanonicalFilename(
                        columns[2], moji))
                    throw new InvalidOperationException(
                        "slots.tsv filename is not canonical.");
                ValidateHash(columns[5]);
                ValidateHash(columns[6]);
                rows.Add(new FontLibrarySlotRecord
                {
                    Moji = moji,
                    UnicodeScalar = scalar,
                    Style = style,
                    Width = width,
                    Filename = columns[4],
                    PackedSha256 = columns[5],
                    PngSha256 = columns[6],
                });
            }
            return rows;
        }

        static int ParseScalar(string text)
        {
            if (!text.StartsWith("U+", StringComparison.Ordinal)
                || !int.TryParse(text.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out int scalar)
                || !FontLibraryManifestCore.IsUnicodeScalar(scalar)
                || text != "U+" + scalar.ToString(
                    "X4", CultureInfo.InvariantCulture))
                throw new InvalidOperationException(
                    "slots.tsv Unicode scalar is not canonical.");
            return scalar;
        }

        static void ValidateHash(string hash)
        {
            if (hash == null || hash.Length != 64)
                throw new InvalidOperationException(
                    "slots.tsv SHA-256 is invalid.");
            for (int i = 0; i < hash.Length; i++)
                if (!((hash[i] >= '0' && hash[i] <= '9')
                    || (hash[i] >= 'a' && hash[i] <= 'f')))
                    throw new InvalidOperationException(
                        "slots.tsv SHA-256 is not lowercase hexadecimal.");
        }
    }

    public static class FontLibraryHashCore
    {
        public static string ComputeSha256(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Hex(SHA256.HashData(data));
        }

        /// <summary>
        /// Hash ordinal '/' paths and bytes as:
        /// UTF8(path), NUL, uint64-BE(length), content. No host separators,
        /// timestamps or filesystem enumeration order enter the digest.
        /// </summary>
        public static string ComputeTreeSha256(
            IEnumerable<KeyValuePair<string, byte[]>> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            var ordered =
                new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                if (!FontLibraryPath.IsCanonicalRelative(file.Key)
                    || file.Value == null
                    || !ordered.TryAdd(file.Key, file.Value))
                    throw new ArgumentException(
                        "Tree contains an invalid/duplicate path.", nameof(files));
            }
            using IncrementalHash hash =
                IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (KeyValuePair<string, byte[]> file in ordered)
            {
                byte[] path = Encoding.UTF8.GetBytes(file.Key);
                hash.AppendData(path);
                hash.AppendData(new byte[] { 0 });
                byte[] length = new byte[8];
                ulong value = (ulong)file.Value.LongLength;
                for (int i = 7; i >= 0; i--)
                {
                    length[i] = (byte)value;
                    value >>= 8;
                }
                hash.AppendData(length);
                hash.AppendData(file.Value);
            }
            return Hex(hash.GetHashAndReset());
        }

        internal static string Hex(byte[] data)
        {
            var chars = new char[data.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (int i = 0; i < data.Length; i++)
            {
                chars[i * 2] = alphabet[data[i] >> 4];
                chars[i * 2 + 1] = alphabet[data[i] & 15];
            }
            return new string(chars);
        }

        internal static bool BytesEqual(byte[] left, byte[] right)
            => left != null && right != null
                && CryptographicOperations.FixedTimeEquals(left, right);
    }

    internal static class FontLibraryText
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static byte[] Utf8(string text)
            => StrictUtf8.GetBytes(text ?? "");

        internal static string DecodeStrictUtf8(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            int offset = bytes.Length >= 3
                && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                ? 3 : 0;
            return StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
        }

        internal static string DecodeStrictUtf8NoBom(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length >= 3
                && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                throw new InvalidOperationException(
                    "Canonical package text must not contain a UTF-8 BOM.");
            return StrictUtf8.GetString(bytes);
        }
    }

    internal static class FontLibraryPath
    {
        internal static bool IsCanonicalRelative(string path)
        {
            if (string.IsNullOrEmpty(path)
                || path[0] == '/' || path[0] == '\\'
                || path.IndexOf('\\') >= 0
                || path.IndexOf(':') >= 0)
                return false;
            string[] parts = path.Split('/');
            foreach (string part in parts)
                if (part.Length == 0 || part == "." || part == "..")
                    return false;
            return true;
        }
    }
}
