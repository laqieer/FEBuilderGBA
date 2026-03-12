using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Byte-level three-way merge for ROM files.
    /// Compares Original, Mine, Theirs and produces a merged result with conflict detection.
    /// </summary>
    public static class ThreeWayMergeCore
    {
        /// <summary>A contiguous range of bytes where all three versions differ.</summary>
        public class ConflictRange
        {
            public uint Offset { get; set; }
            public uint Length { get; set; }
            public byte[] OriginalBytes { get; set; } = Array.Empty<byte>();
            public byte[] MyBytes { get; set; } = Array.Empty<byte>();
            public byte[] TheirBytes { get; set; } = Array.Empty<byte>();

            /// <summary>Resolution choice: true = use Mine, false = use Theirs.</summary>
            public bool UseMine { get; set; } = true;
        }

        /// <summary>Result of a three-way merge operation.</summary>
        public class MergeResult
        {
            /// <summary>The merged byte array (conflicts resolved using default UseMine=true).</summary>
            public byte[] MergedData { get; set; } = Array.Empty<byte>();

            /// <summary>List of conflict ranges where both Mine and Theirs changed differently.</summary>
            public List<ConflictRange> Conflicts { get; set; } = new List<ConflictRange>();

            /// <summary>Number of bytes changed only in Mine.</summary>
            public int ChangesMine { get; set; }

            /// <summary>Number of bytes changed only in Theirs.</summary>
            public int ChangesTheirs { get; set; }

            /// <summary>Number of bytes changed identically in both Mine and Theirs.</summary>
            public int ChangesBoth { get; set; }

            /// <summary>Total conflict byte count.</summary>
            public int ConflictBytes { get; set; }
        }

        /// <summary>
        /// Perform a byte-level three-way merge.
        /// </summary>
        /// <param name="original">The common ancestor ROM bytes.</param>
        /// <param name="mine">My modified ROM bytes.</param>
        /// <param name="theirs">Their modified ROM bytes.</param>
        /// <returns>A MergeResult with merged data and conflict information.</returns>
        /// <exception cref="ArgumentNullException">If any argument is null.</exception>
        /// <exception cref="ArgumentException">If array lengths differ.</exception>
        public static MergeResult Merge(byte[] original, byte[] mine, byte[] theirs)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (mine == null) throw new ArgumentNullException(nameof(mine));
            if (theirs == null) throw new ArgumentNullException(nameof(theirs));
            if (original.Length != mine.Length || original.Length != theirs.Length)
                throw new ArgumentException("All three arrays must have the same length.");

            int len = original.Length;
            byte[] merged = new byte[len];
            var conflicts = new List<ConflictRange>();
            int changesMine = 0;
            int changesTheirs = 0;
            int changesBoth = 0;
            int conflictBytes = 0;

            // Track current conflict range being built
            int conflictStart = -1;

            for (int i = 0; i < len; i++)
            {
                byte o = original[i];
                byte m = mine[i];
                byte t = theirs[i];

                bool mChanged = o != m;
                bool tChanged = o != t;

                if (!mChanged && !tChanged)
                {
                    // No change
                    merged[i] = o;
                    FlushConflict(original, mine, theirs, conflicts, ref conflictStart, i);
                }
                else if (mChanged && !tChanged)
                {
                    // Only mine changed
                    merged[i] = m;
                    changesMine++;
                    FlushConflict(original, mine, theirs, conflicts, ref conflictStart, i);
                }
                else if (!mChanged && tChanged)
                {
                    // Only theirs changed
                    merged[i] = t;
                    changesTheirs++;
                    FlushConflict(original, mine, theirs, conflicts, ref conflictStart, i);
                }
                else if (m == t)
                {
                    // Both changed the same way
                    merged[i] = m;
                    changesBoth++;
                    FlushConflict(original, mine, theirs, conflicts, ref conflictStart, i);
                }
                else
                {
                    // Conflict: both changed differently
                    merged[i] = m; // default to mine
                    conflictBytes++;
                    if (conflictStart < 0)
                        conflictStart = i;
                }
            }

            // Flush any trailing conflict
            FlushConflict(original, mine, theirs, conflicts, ref conflictStart, len);

            return new MergeResult
            {
                MergedData = merged,
                Conflicts = conflicts,
                ChangesMine = changesMine,
                ChangesTheirs = changesTheirs,
                ChangesBoth = changesBoth,
                ConflictBytes = conflictBytes,
            };
        }

        /// <summary>
        /// Apply conflict resolutions to the merged data.
        /// Call after the user has set UseMine on each ConflictRange.
        /// </summary>
        public static void ApplyResolutions(MergeResult result, byte[] theirs)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (theirs == null) throw new ArgumentNullException(nameof(theirs));

            foreach (var c in result.Conflicts)
            {
                byte[] source = c.UseMine ? c.MyBytes : c.TheirBytes;
                Array.Copy(source, 0, result.MergedData, (int)c.Offset, (int)c.Length);
            }
        }

        static void FlushConflict(byte[] original, byte[] mine, byte[] theirs,
            List<ConflictRange> conflicts, ref int conflictStart, int currentIndex)
        {
            if (conflictStart < 0) return;

            int length = currentIndex - conflictStart;
            var cr = new ConflictRange
            {
                Offset = (uint)conflictStart,
                Length = (uint)length,
                OriginalBytes = new byte[length],
                MyBytes = new byte[length],
                TheirBytes = new byte[length],
            };
            Array.Copy(original, conflictStart, cr.OriginalBytes, 0, length);
            Array.Copy(mine, conflictStart, cr.MyBytes, 0, length);
            Array.Copy(theirs, conflictStart, cr.TheirBytes, 0, length);
            conflicts.Add(cr);

            conflictStart = -1;
        }
    }
}
