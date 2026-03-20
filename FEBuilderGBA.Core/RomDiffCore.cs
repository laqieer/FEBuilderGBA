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
