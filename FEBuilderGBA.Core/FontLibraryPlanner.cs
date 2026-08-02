// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// Already strict-UTF-8-decoded corpus text, keyed by job id. Filesystem and
    /// hash verification happen before this pure planning boundary.
    /// </summary>
    public sealed class FontLibraryPlannerInput
    {
        public Dictionary<string, string> Corpora { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public sealed class FontLibraryPlan
    {
        public List<FontLibraryPlannedJob> Jobs { get; } =
            new List<FontLibraryPlannedJob>();
        public int SlotCount { get; set; }
    }

    public sealed class FontLibraryPlannedJob
    {
        public FontLibraryJobManifest Manifest { get; set; }
        public List<FontLibraryPlannedSlot> Slots { get; } =
            new List<FontLibraryPlannedSlot>();
    }

    public sealed class FontLibraryPlannedSlot
    {
        public int UnicodeScalar { get; set; }
        public string Character { get; set; } = "";
        public uint Moji { get; set; }
        public FontLibraryStyle Style { get; set; }
    }

    public sealed class FontLibraryPlanResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = "";
        public FontLibraryPlan Plan { get; set; }
        public FontLibraryFailureKind FailureKind { get; set; }
    }

    /// <summary>
    /// Pure scalar-to-slot planner. It performs no encoding inference, file I/O,
    /// hashing or rasterization.
    /// </summary>
    public static class FontLibraryPlanner
    {
        public static FontLibraryPlanResult Plan(
            FontLibraryManifest manifest,
            FontLibraryPlannerInput input,
            string selectedJobId = null)
        {
            var result = new FontLibraryPlanResult();
            if (manifest == null || manifest.SchemaVersion != 1)
                return Fail(result,
                    "A parsed schema-v1 manifest is required.",
                    FontLibraryFailureKind.Schema);
            input ??= new FontLibraryPlannerInput();

            try
            {
                var jobs = new List<FontLibraryJobManifest>();
                foreach (FontLibraryJobManifest job in manifest.Jobs)
                {
                    if (selectedJobId == null
                        || string.Equals(job.Id, selectedJobId,
                            StringComparison.Ordinal))
                        jobs.Add(job);
                }
                if (selectedJobId != null && jobs.Count == 0)
                    throw new FontLibraryOperationException(
                        FontLibraryFailureKind.Usage,
                        "Selected job '" + selectedJobId + "' does not exist.");
                jobs.Sort((a, b) =>
                    StringComparer.Ordinal.Compare(a.Id, b.Id));

                var preflight = new List<PreflightJob>();
                int totalRows = 0;
                foreach (FontLibraryJobManifest job in jobs)
                {
                    if (job.Styles == null
                        || job.Styles.Count < 1
                        || job.Styles.Count
                            > FontLibraryManifestCore.MaxStylesPerJob)
                        throw JobError(job,
                            FontLibraryFailureKind.Schema,
                            "one or two styles are required");
                    if (job.Mapping == null)
                        throw JobError(job,
                            FontLibraryFailureKind.Schema,
                            "an explicit mapping is required");
                    var scalars = new SortedSet<int>();
                    var ranges =
                        new List<FontLibraryScalarRange>(job.ScalarRanges);
                    ranges.Sort((a, b) =>
                    {
                        int byStart = a.Start.CompareTo(b.Start);
                        return byStart != 0
                            ? byStart : a.End.CompareTo(b.End);
                    });
                    int coveredThrough = -1;
                    foreach (FontLibraryScalarRange range in ranges)
                    {
                        long length = (long)range.End - range.Start + 1;
                        if (length <= 0)
                            throw JobError(job,
                                FontLibraryFailureKind.Validation,
                                "scalar range length is invalid");
                        if (range.End <= coveredThrough) continue;
                        int first = range.Start <= coveredThrough
                            ? coveredThrough + 1 : range.Start;
                        for (int scalar = first; scalar <= range.End; scalar++)
                        {
                            RejectWhitespace(
                                job, scalar, "scalarRanges");
                            scalars.Add(scalar);
                            EnsureRowCapacity(job, scalars.Count);
                            if (scalar == 0x10FFFF) break;
                        }
                        coveredThrough = range.End;
                    }
                    if (job.Mapping.Mode == FontLibraryMappingMode.Fixed)
                    {
                        foreach (FontLibraryFixedMapping entry
                            in job.Mapping.Entries)
                        {
                            RejectWhitespace(
                                job, entry.Scalar, "mapping.entries");
                        }
                    }

                    if (job.Corpus != null)
                    {
                        if (!input.Corpora.TryGetValue(
                            job.Id, out string corpus))
                            throw JobError(job,
                                FontLibraryFailureKind.Provenance,
                                "verified strict-UTF-8 corpus text is missing");
                        AddCorpusScalars(job, corpus, scalars);
                    }
                    if (scalars.Count == 0)
                        throw JobError(job,
                            FontLibraryFailureKind.Validation,
                            "plans no Unicode scalars");

                    long rowCount =
                        (long)scalars.Count * job.Styles.Count;
                    totalRows = checked(totalRows + (int)rowCount);
                    if (totalRows > FontLibraryManifestCore.MaxRowsTotal)
                        throw new FontLibraryOperationException(
                            FontLibraryFailureKind.Exhaustion,
                            "Selected-run style-expanded row count exceeds the "
                            + FontLibraryManifestCore.MaxRowsTotal.ToString(
                                CultureInfo.InvariantCulture)
                            + "-row cap.");

                    Dictionary<int, uint> mapping = BuildMapping(job, scalars);
                    var scalarMoji = new List<(int Scalar, uint Moji)>();
                    var mojiSlots = new HashSet<uint>();
                    foreach (int scalar in scalars)
                    {
                        if (!mapping.TryGetValue(scalar, out uint moji))
                            throw JobError(job,
                                FontLibraryFailureKind.Conflict,
                                "mapping does not cover U+"
                                + scalar.ToString("X4",
                                    CultureInfo.InvariantCulture));
                        uint mojiSlot =
                            GetMojiSlotIdentity(job, moji);
                        if (!mojiSlots.Add(mojiSlot))
                            throw JobError(job,
                                FontLibraryFailureKind.Conflict,
                                job.Format == FontLibraryFormat.Zh16x13
                                    ? "two Unicode scalars map to the same "
                                        + "ZH engine glyph slot "
                                        + U.ToHexString(mojiSlot)
                                    : "two Unicode scalars map to the same "
                                        + "moji " + U.ToHexString(moji));
                        scalarMoji.Add((scalar, moji));
                    }
                    scalarMoji.Sort((a, b) =>
                    {
                        int byMoji = a.Moji.CompareTo(b.Moji);
                        return byMoji != 0
                            ? byMoji : a.Scalar.CompareTo(b.Scalar);
                    });

                    preflight.Add(new PreflightJob(job, scalarMoji));
                }

                // No package rows are allocated until every selected job has
                // passed style-expanded capacity and mapping preflight.
                var plan = new FontLibraryPlan
                {
                    SlotCount = totalRows,
                };
                foreach (PreflightJob ready in preflight)
                {
                    var plannedJob = new FontLibraryPlannedJob
                    {
                        Manifest = ready.Manifest,
                    };
                    foreach ((int scalar, uint moji) in ready.ScalarMoji)
                    {
                        // Stable style order is always item then text, regardless
                        // of the source array order.
                        if (ready.Manifest.Styles.Contains(
                            FontLibraryStyle.Item))
                            AddSlot(plannedJob, scalar, moji,
                                FontLibraryStyle.Item);
                        if (ready.Manifest.Styles.Contains(
                            FontLibraryStyle.Text))
                            AddSlot(plannedJob, scalar, moji,
                                FontLibraryStyle.Text);
                    }
                    plan.Jobs.Add(plannedJob);
                }

                result.Success = true;
                result.Plan = plan;
                return result;
            }
            catch (FontLibraryOperationException ex)
            {
                return Fail(result, ex.Message, ex.Kind);
            }
            catch (Exception ex) when (
                ex is InvalidOperationException
                || ex is OverflowException
                || ex is ArgumentException)
            {
                return Fail(
                    result, ex.Message, FontLibraryFailureKind.Validation);
            }
        }

        sealed class PreflightJob
        {
            internal PreflightJob(
                FontLibraryJobManifest manifest,
                List<(int Scalar, uint Moji)> scalarMoji)
            {
                Manifest = manifest;
                ScalarMoji = scalarMoji;
            }

            internal FontLibraryJobManifest Manifest { get; }
            internal List<(int Scalar, uint Moji)> ScalarMoji { get; }
        }

        static void AddSlot(
            FontLibraryPlannedJob job,
            int scalar,
            uint moji,
            FontLibraryStyle style)
        {
            job.Slots.Add(new FontLibraryPlannedSlot
            {
                UnicodeScalar = scalar,
                Character = char.ConvertFromUtf32(scalar),
                Moji = moji,
                Style = style,
            });
        }

        static void AddCorpusScalars(
            FontLibraryJobManifest job,
            string corpus,
            SortedSet<int> scalars)
        {
            if (corpus == null)
                throw JobError(job,
                    FontLibraryFailureKind.Provenance,
                    "corpus is null");
            var separators = new HashSet<int>(
                job.Corpus?.Separators ?? new List<int>());
            for (int index = 0; index < corpus.Length;)
            {
                int scalar;
                char first = corpus[index];
                if (char.IsHighSurrogate(first))
                {
                    if (index + 1 >= corpus.Length
                        || !char.IsLowSurrogate(corpus[index + 1]))
                        throw JobError(job,
                            FontLibraryFailureKind.Validation,
                            "corpus contains an unpaired high surrogate");
                    scalar = char.ConvertToUtf32(first, corpus[index + 1]);
                    index += 2;
                }
                else
                {
                    if (char.IsLowSurrogate(first))
                        throw JobError(job,
                            FontLibraryFailureKind.Validation,
                            "corpus contains an unpaired low surrogate");
                    scalar = first;
                    index++;
                }
                if (separators.Contains(scalar))
                    continue;
                RejectWhitespace(job, scalar, "corpus");
                scalars.Add(scalar);
                EnsureRowCapacity(job, scalars.Count);
            }
        }

        static void EnsureRowCapacity(
            FontLibraryJobManifest job,
            int scalarCount)
        {
            if (scalarCount < 0)
                throw JobError(job,
                    FontLibraryFailureKind.Validation,
                    "scalar count is invalid");
            long rows = (long)scalarCount * job.Styles.Count;
            if (rows > FontLibraryManifestCore.MaxRowsTotal)
                throw JobError(job,
                    FontLibraryFailureKind.Exhaustion,
                    "style expansion produces "
                    + rows.ToString(CultureInfo.InvariantCulture)
                    + " rows and cannot fit within the selected-run "
                    + FontLibraryManifestCore.MaxRowsTotal.ToString(
                        CultureInfo.InvariantCulture)
                    + "-row cap during preflight");
        }

        static Dictionary<int, uint> BuildMapping(
            FontLibraryJobManifest job,
            SortedSet<int> scalars)
        {
            var result = new Dictionary<int, uint>();
            if (job.Mapping.Mode == FontLibraryMappingMode.Fixed)
            {
                foreach (FontLibraryFixedMapping entry in job.Mapping.Entries)
                {
                    if (!result.TryAdd(entry.Scalar, entry.Moji))
                        throw JobError(job,
                            FontLibraryFailureKind.Conflict,
                            "fixed mapping duplicates U+"
                            + entry.Scalar.ToString(
                                "X4", CultureInfo.InvariantCulture));
                }
                foreach (int mappedScalar in result.Keys)
                {
                    if (!scalars.Contains(mappedScalar))
                        throw JobError(job,
                            FontLibraryFailureKind.Conflict,
                            "fixed mapping contains scalar not selected by corpus/ranges: U+"
                            + mappedScalar.ToString("X4",
                                CultureInfo.InvariantCulture));
                }
            }
            else
            {
                ulong capacity =
                    (ulong)job.Mapping.MojiEnd - job.Mapping.MojiStart + 1;
                if ((ulong)scalars.Count > capacity)
                    throw JobError(job,
                        FontLibraryFailureKind.Exhaustion,
                        "range mapping has "
                        + capacity.ToString(CultureInfo.InvariantCulture)
                        + " moji slots for "
                        + scalars.Count.ToString(CultureInfo.InvariantCulture)
                        + " selected scalars");
                ulong next = job.Mapping.MojiStart;
                foreach (int scalar in scalars)
                {
                    if (scalar < job.Mapping.UnicodeStart
                        || scalar > job.Mapping.UnicodeEnd)
                        throw JobError(job,
                            FontLibraryFailureKind.Conflict,
                            "bounded mapping does not cover U+"
                            + scalar.ToString("X4",
                                CultureInfo.InvariantCulture));
                    if (next > job.Mapping.MojiEnd || next > uint.MaxValue)
                        throw JobError(job,
                            FontLibraryFailureKind.Exhaustion,
                            "bounded mapping is exhausted");
                    result.Add(scalar, (uint)next);
                    next++;
                }
            }
            return result;
        }

        static uint GetMojiSlotIdentity(
            FontLibraryJobManifest job,
            uint moji)
        {
            if (job.Format != FontLibraryFormat.Zh16x13)
                return moji;
            if (moji > ushort.MaxValue)
                throw JobError(job,
                    FontLibraryFailureKind.Validation,
                    "ZH moji must fit in the 16-bit engine code domain: "
                    + U.ToHexString(moji));
            uint codeB = FontGlyphZHCore.CalcCodeB(moji);
            if (codeB == U.NOT_FOUND)
                throw JobError(job,
                    FontLibraryFailureKind.Validation,
                    "ZH moji has no engine glyph slot: "
                    + U.ToHexString(moji));
            return codeB;
        }

        static void RejectWhitespace(
            FontLibraryJobManifest job,
            int scalar,
            string source)
        {
            string text = char.ConvertFromUtf32(scalar);
            if (char.IsWhiteSpace(text, 0))
                throw JobError(job,
                    FontLibraryFailureKind.Validation,
                    source + " contains unconfigured Unicode whitespace U+"
                    + scalar.ToString("X4", CultureInfo.InvariantCulture)
                    + (string.Equals(
                        source, "corpus", StringComparison.Ordinal)
                        ? "; list intentional separators in corpus.separators"
                        : "; explicit " + source
                            + " must not select whitespace"));
        }

        static FontLibraryOperationException JobError(
            FontLibraryJobManifest job,
            FontLibraryFailureKind kind,
            string detail)
            => new FontLibraryOperationException(
                kind,
                "Job '" + (job?.Id ?? "") + "': " + detail + ".");

        static FontLibraryPlanResult Fail(
            FontLibraryPlanResult result,
            string error,
            FontLibraryFailureKind kind)
        {
            result.Error = "Font-library planning failed: " + error;
            result.FailureKind = kind;
            return result;
        }
    }
}
