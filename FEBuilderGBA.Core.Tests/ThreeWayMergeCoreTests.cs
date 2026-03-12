using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class ThreeWayMergeCoreTests
    {
        [Fact]
        public void NoChanges_IdenticalOutput()
        {
            byte[] original = { 1, 2, 3, 4, 5 };
            byte[] mine = { 1, 2, 3, 4, 5 };
            byte[] theirs = { 1, 2, 3, 4, 5 };

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Equal(original, result.MergedData);
            Assert.Empty(result.Conflicts);
            Assert.Equal(0, result.ChangesMine);
            Assert.Equal(0, result.ChangesTheirs);
            Assert.Equal(0, result.ChangesBoth);
            Assert.Equal(0, result.ConflictBytes);
        }

        [Fact]
        public void OnlyMyChanges_MyDataWins()
        {
            byte[] original = { 1, 2, 3, 4, 5 };
            byte[] mine = { 1, 99, 3, 88, 5 };
            byte[] theirs = { 1, 2, 3, 4, 5 };

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Equal(mine, result.MergedData);
            Assert.Empty(result.Conflicts);
            Assert.Equal(2, result.ChangesMine);
            Assert.Equal(0, result.ChangesTheirs);
            Assert.Equal(0, result.ChangesBoth);
        }

        [Fact]
        public void OnlyTheirChanges_TheirDataWins()
        {
            byte[] original = { 1, 2, 3, 4, 5 };
            byte[] mine = { 1, 2, 3, 4, 5 };
            byte[] theirs = { 1, 2, 77, 4, 66 };

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Equal(theirs, result.MergedData);
            Assert.Empty(result.Conflicts);
            Assert.Equal(0, result.ChangesMine);
            Assert.Equal(2, result.ChangesTheirs);
            Assert.Equal(0, result.ChangesBoth);
        }

        [Fact]
        public void BothSameChange_NoConflict()
        {
            byte[] original = { 1, 2, 3, 4, 5 };
            byte[] mine = { 1, 42, 3, 4, 5 };
            byte[] theirs = { 1, 42, 3, 4, 5 };

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Equal(new byte[] { 1, 42, 3, 4, 5 }, result.MergedData);
            Assert.Empty(result.Conflicts);
            Assert.Equal(0, result.ChangesMine);
            Assert.Equal(0, result.ChangesTheirs);
            Assert.Equal(1, result.ChangesBoth);
        }

        [Fact]
        public void BothDifferentChange_ConflictDetected()
        {
            byte[] original = { 1, 2, 3, 4, 5 };
            byte[] mine = { 1, 10, 3, 4, 5 };
            byte[] theirs = { 1, 20, 3, 4, 5 };

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Single(result.Conflicts);
            Assert.Equal(1, result.ConflictBytes);

            var conflict = result.Conflicts[0];
            Assert.Equal(1u, conflict.Offset);
            Assert.Equal(1u, conflict.Length);
            Assert.Equal(new byte[] { 2 }, conflict.OriginalBytes);
            Assert.Equal(new byte[] { 10 }, conflict.MyBytes);
            Assert.Equal(new byte[] { 20 }, conflict.TheirBytes);

            // Default resolution is Mine
            Assert.Equal(10, result.MergedData[1]);
        }

        [Fact]
        public void MixedChanges_AcrossRegions()
        {
            // offset:     0  1  2  3  4  5  6  7
            byte[] original = { 1, 2, 3, 4, 5, 6, 7, 8 };
            byte[] mine =     { 1, 99, 3, 4, 50, 6, 70, 8 };  // changed [1], [4], [6]
            byte[] theirs =   { 1, 2, 33, 4, 5, 66, 77, 8 };  // changed [2], [5], [6]

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            // [0]: no change -> 1
            Assert.Equal(1, result.MergedData[0]);
            // [1]: only mine -> 99
            Assert.Equal(99, result.MergedData[1]);
            // [2]: only theirs -> 33
            Assert.Equal(33, result.MergedData[2]);
            // [3]: no change -> 4
            Assert.Equal(4, result.MergedData[3]);
            // [4]: only mine -> 50
            Assert.Equal(50, result.MergedData[4]);
            // [5]: only theirs -> 66
            Assert.Equal(66, result.MergedData[5]);
            // [6]: conflict (mine=70, theirs=77) -> default mine=70
            Assert.Equal(70, result.MergedData[6]);
            // [7]: no change -> 8
            Assert.Equal(8, result.MergedData[7]);

            Assert.Equal(2, result.ChangesMine);   // [1], [4]
            Assert.Equal(2, result.ChangesTheirs);  // [2], [5]
            Assert.Equal(1, result.ConflictBytes);  // [6]
            Assert.Single(result.Conflicts);
        }

        [Fact]
        public void ConsecutiveConflicts_GroupedIntoOneRange()
        {
            byte[] original = { 0, 0, 0, 0, 0 };
            byte[] mine =     { 0, 1, 2, 3, 0 };
            byte[] theirs =   { 0, 4, 5, 6, 0 };

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Single(result.Conflicts);
            var conflict = result.Conflicts[0];
            Assert.Equal(1u, conflict.Offset);
            Assert.Equal(3u, conflict.Length);
            Assert.Equal(new byte[] { 0, 0, 0 }, conflict.OriginalBytes);
            Assert.Equal(new byte[] { 1, 2, 3 }, conflict.MyBytes);
            Assert.Equal(new byte[] { 4, 5, 6 }, conflict.TheirBytes);
            Assert.Equal(3, result.ConflictBytes);
        }

        [Fact]
        public void MultipleConflictRanges_SeparatedByNonConflict()
        {
            byte[] original = { 0, 0, 0, 0, 0 };
            byte[] mine =     { 1, 0, 0, 3, 0 };
            byte[] theirs =   { 2, 0, 0, 4, 0 };

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Equal(2, result.Conflicts.Count);
            Assert.Equal(0u, result.Conflicts[0].Offset);
            Assert.Equal(1u, result.Conflicts[0].Length);
            Assert.Equal(3u, result.Conflicts[1].Offset);
            Assert.Equal(1u, result.Conflicts[1].Length);
        }

        [Fact]
        public void ApplyResolutions_SwitchToTheirs()
        {
            byte[] original = { 0, 0, 0 };
            byte[] mine =     { 1, 0, 0 };
            byte[] theirs =   { 2, 0, 0 };

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);
            Assert.Single(result.Conflicts);

            // Default is mine
            Assert.Equal(1, result.MergedData[0]);

            // Switch to theirs
            result.Conflicts[0].UseMine = false;
            ThreeWayMergeCore.ApplyResolutions(result, theirs);
            Assert.Equal(2, result.MergedData[0]);
        }

        [Fact]
        public void TrailingConflict_FlushedCorrectly()
        {
            byte[] original = { 0, 0, 0 };
            byte[] mine =     { 0, 1, 2 };
            byte[] theirs =   { 0, 3, 4 };

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Single(result.Conflicts);
            Assert.Equal(1u, result.Conflicts[0].Offset);
            Assert.Equal(2u, result.Conflicts[0].Length);
        }

        [Fact]
        public void EmptyArrays_NoChanges()
        {
            byte[] original = Array.Empty<byte>();
            byte[] mine = Array.Empty<byte>();
            byte[] theirs = Array.Empty<byte>();

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Empty(result.MergedData);
            Assert.Empty(result.Conflicts);
        }

        [Fact]
        public void NullArgument_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ThreeWayMergeCore.Merge(null!, new byte[1], new byte[1]));
            Assert.Throws<ArgumentNullException>(() => ThreeWayMergeCore.Merge(new byte[1], null!, new byte[1]));
            Assert.Throws<ArgumentNullException>(() => ThreeWayMergeCore.Merge(new byte[1], new byte[1], null!));
        }

        [Fact]
        public void DifferentLengths_Throws()
        {
            Assert.Throws<ArgumentException>(() => ThreeWayMergeCore.Merge(new byte[3], new byte[4], new byte[3]));
            Assert.Throws<ArgumentException>(() => ThreeWayMergeCore.Merge(new byte[3], new byte[3], new byte[4]));
        }

        [Fact]
        public void LargeArray_Performance()
        {
            // 16 MB ROM-sized test
            int size = 16 * 1024 * 1024;
            byte[] original = new byte[size];
            byte[] mine = new byte[size];
            byte[] theirs = new byte[size];
            Array.Copy(original, mine, size);
            Array.Copy(original, theirs, size);

            // Scatter some changes
            for (int i = 0; i < 1000; i++)
            {
                mine[i * 1000] = 0xFF;
            }
            for (int i = 0; i < 500; i++)
            {
                theirs[i * 2000 + 1] = 0xFE;
            }

            var result = ThreeWayMergeCore.Merge(original, mine, theirs);

            Assert.Equal(1000, result.ChangesMine);
            Assert.Equal(500, result.ChangesTheirs);
            Assert.Empty(result.Conflicts);
        }
    }
}
