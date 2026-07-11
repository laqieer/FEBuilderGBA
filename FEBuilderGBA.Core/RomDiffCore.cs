using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform byte-level ROM comparison.
    /// Compares two ROM byte arrays and reports contiguous changed regions.
    /// </summary>
    public static class RomDiffCore
    {
        /// <summary>A contiguous range of bytes that differ between two ROMs.</summary>
        public class DiffRange
        {
            public uint Offset { get; set; }
            public uint Length { get; set; }
        }

        /// <summary>Result of a two-way ROM comparison.</summary>
        public class DiffResult
        {
            public List<DiffRange> Ranges { get; set; } = new List<DiffRange>();
            public uint TotalDiffBytes { get; set; }
            public uint Rom1Size { get; set; }
            public uint Rom2Size { get; set; }

            /// <summary>True if ROM2 has extra trailing bytes beyond ROM1.</summary>
            public bool HasSizeDifference => Rom1Size != Rom2Size;
        }

        /// <summary>
        /// Thrown by <see cref="CompareWithFillBounded"/> when the diff would produce more
        /// distinct changed ranges than the caller's resource-safety limit. A pathological
        /// alternating-byte diff (worst case: one range every other byte) must be rejected
        /// with a stable, explicit error immediately — BEFORE the range list grows past the
        /// limit — rather than silently materializing millions of ranges (and, downstream,
        /// millions of payload files).
        /// </summary>
        public sealed class DiffRangeLimitExceededException : Exception
        {
            public int Limit { get; }

            public DiffRangeLimitExceededException(int limit)
                : base("Diff produced more than " + limit +
                      " distinct changed ranges; refusing (resource-safety limit).")
            {
                Limit = limit;
            }
        }

        /// <summary>
        /// Compare two byte arrays and return contiguous diff ranges.
        /// Compares up to the shorter length, then merges any trailing extra region.
        /// </summary>
        public static DiffResult Compare(byte[] rom1, byte[] rom2)
        {
            if (rom1 == null) throw new ArgumentNullException(nameof(rom1));
            if (rom2 == null) throw new ArgumentNullException(nameof(rom2));

            var result = new DiffResult
            {
                Rom1Size = (uint)rom1.Length,
                Rom2Size = (uint)rom2.Length,
            };

            int commonLen = Math.Min(rom1.Length, rom2.Length);
            int diffStart = -1;

            for (int i = 0; i < commonLen; i++)
            {
                if (rom1[i] != rom2[i])
                {
                    if (diffStart < 0)
                        diffStart = i;
                }
                else
                {
                    if (diffStart >= 0)
                    {
                        uint len = (uint)(i - diffStart);
                        result.Ranges.Add(new DiffRange { Offset = (uint)diffStart, Length = len });
                        result.TotalDiffBytes += len;
                        diffStart = -1;
                    }
                }
            }

            // Close any trailing diff within common range
            if (diffStart >= 0)
            {
                uint len = (uint)(commonLen - diffStart);
                result.Ranges.Add(new DiffRange { Offset = (uint)diffStart, Length = len });
                result.TotalDiffBytes += len;
            }

            // If ROMs differ in size, merge extra region with last range if contiguous
            if (rom1.Length != rom2.Length)
            {
                uint extraStart = (uint)commonLen;
                uint extraLen = (uint)Math.Abs(rom1.Length - rom2.Length);

                // Merge with last range if it ends at commonLen (contiguous)
                if (result.Ranges.Count > 0)
                {
                    var last = result.Ranges[result.Ranges.Count - 1];
                    if (last.Offset + last.Length == extraStart)
                    {
                        last.Length += extraLen;
                        result.TotalDiffBytes += extraLen;
                    }
                    else
                    {
                        result.Ranges.Add(new DiffRange { Offset = extraStart, Length = extraLen });
                        result.TotalDiffBytes += extraLen;
                    }
                }
                else
                {
                    result.Ranges.Add(new DiffRange { Offset = extraStart, Length = extraLen });
                    result.TotalDiffBytes += extraLen;
                }
            }

            return result;
        }

        /// <summary>
        /// Compare a baseline against a target, treating the baseline as though it
        /// were virtually extended to the target's length with <paramref name="fillByte"/>.
        /// Every reported range contains ONLY bytes that differ from the clean baseline
        /// or the declared extension fill (there is no small-gap merge and no trailing
        /// size-difference special case), so the caller may write each range's bytes as
        /// a sparse override that wins over the fill.
        ///
        /// The target MUST NOT be shorter than the baseline (the buildfile exporter
        /// rejects that upstream); a shorter target throws <see cref="ArgumentException"/>.
        /// The existing two-argument <see cref="Compare(byte[], byte[])"/> behavior is
        /// intentionally left unchanged.
        /// </summary>
        public static DiffResult CompareWithFill(byte[] baseline, byte[] target, byte fillByte)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.Length < baseline.Length)
                throw new ArgumentException("Target must not be shorter than the baseline.", nameof(target));

            var result = new DiffResult
            {
                Rom1Size = (uint)baseline.Length,
                Rom2Size = (uint)target.Length,
            };

            int diffStart = -1;
            for (int i = 0; i < target.Length; i++)
            {
                byte baseByte = i < baseline.Length ? baseline[i] : fillByte;
                if (target[i] != baseByte)
                {
                    if (diffStart < 0)
                        diffStart = i;
                }
                else if (diffStart >= 0)
                {
                    uint len = (uint)(i - diffStart);
                    result.Ranges.Add(new DiffRange { Offset = (uint)diffStart, Length = len });
                    result.TotalDiffBytes += len;
                    diffStart = -1;
                }
            }

            if (diffStart >= 0)
            {
                uint len = (uint)(target.Length - diffStart);
                result.Ranges.Add(new DiffRange { Offset = (uint)diffStart, Length = len });
                result.TotalDiffBytes += len;
            }

            return result;
        }

        /// <summary>
        /// Bounded variant of <see cref="CompareWithFill"/>: identical semantics (still
        /// maxGap-0 — no merging of unchanged bytes into payloads), but throws
        /// <see cref="DiffRangeLimitExceededException"/> the instant the NEXT range would
        /// exceed <paramref name="maxRanges"/>, before it is added to the result. This bounds
        /// memory/allocation for a pathological diff (e.g. a worst-case alternating-byte
        /// pattern across a 32 MiB ROM, which would otherwise produce millions of one-byte
        /// ranges) so resource-safety is enforced BEFORE any downstream materialization
        /// (payload files, manifest entries). The existing unbounded <see cref="CompareWithFill"/>
        /// is left completely unchanged for callers that do not need this bound.
        /// </summary>
        public static DiffResult CompareWithFillBounded(byte[] baseline, byte[] target, byte fillByte, int maxRanges)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.Length < baseline.Length)
                throw new ArgumentException("Target must not be shorter than the baseline.", nameof(target));
            if (maxRanges <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRanges), "maxRanges must be positive.");

            var result = new DiffResult
            {
                Rom1Size = (uint)baseline.Length,
                Rom2Size = (uint)target.Length,
            };

            int diffStart = -1;
            for (int i = 0; i < target.Length; i++)
            {
                byte baseByte = i < baseline.Length ? baseline[i] : fillByte;
                if (target[i] != baseByte)
                {
                    if (diffStart < 0)
                        diffStart = i;
                }
                else if (diffStart >= 0)
                {
                    if (result.Ranges.Count >= maxRanges)
                        throw new DiffRangeLimitExceededException(maxRanges);
                    uint len = (uint)(i - diffStart);
                    result.Ranges.Add(new DiffRange { Offset = (uint)diffStart, Length = len });
                    result.TotalDiffBytes += len;
                    diffStart = -1;
                }
            }

            if (diffStart >= 0)
            {
                if (result.Ranges.Count >= maxRanges)
                    throw new DiffRangeLimitExceededException(maxRanges);
                uint len = (uint)(target.Length - diffStart);
                result.Ranges.Add(new DiffRange { Offset = (uint)diffStart, Length = len });
                result.TotalDiffBytes += len;
            }

            return result;
        }

        /// <summary>Format diff result as human-readable summary text.</summary>
        public static string FormatSummary(DiffResult diff)
        {
            if (diff.Ranges.Count == 0)
                return "ROMs are identical.";

            var sb = new StringBuilder();
            foreach (var r in diff.Ranges)
            {
                uint end = r.Offset + r.Length - 1;
                sb.AppendLine($"0x{r.Offset:X06}-0x{end:X06} ({r.Length} bytes changed)");
            }

            sb.AppendLine($"Total: {diff.TotalDiffBytes} bytes differ across {diff.Ranges.Count} region(s)");

            if (diff.HasSizeDifference)
                sb.AppendLine($"Size difference: ROM1={diff.Rom1Size} bytes, ROM2={diff.Rom2Size} bytes");

            return sb.ToString().TrimEnd();
        }

        /// <summary>Format diff result as TSV with hex previews per range.</summary>
        public static string FormatTSV(DiffResult diff, byte[] rom1, byte[] rom2)
        {
            var sb = new StringBuilder();
            sb.AppendLine("StartAddr\tLength\tPreviewOld\tPreviewNew");

            foreach (var r in diff.Ranges)
            {
                string oldHex = HexPreview(rom1, r.Offset, r.Length);
                string newHex = HexPreview(rom2, r.Offset, r.Length);
                sb.AppendLine($"0x{r.Offset:X06}\t{r.Length}\t{oldHex}\t{newHex}");
            }

            return sb.ToString().TrimEnd();
        }

        static string HexPreview(byte[] data, uint offset, uint length)
        {
            int maxPreview = 16;
            int count = (int)Math.Min(length, (uint)maxPreview);
            var sb = new StringBuilder(count * 3);

            for (int i = 0; i < count; i++)
            {
                int idx = (int)offset + i;
                if (idx >= 0 && idx < data.Length)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(data[idx].ToString("X2"));
                }
            }

            if (length > (uint)maxPreview)
                sb.Append("...");

            return sb.ToString();
        }
    }
}
