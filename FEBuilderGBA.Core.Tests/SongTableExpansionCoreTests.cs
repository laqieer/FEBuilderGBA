using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public sealed class SongTableExpansionCoreTests : IDisposable
    {
        const uint TableBase = 0x3000;
        const uint FreeBase = 0x100000;

        readonly ROM _savedRom = CoreState.ROM;
        readonly Undo _savedUndo = CoreState.Undo;
        readonly IEtcCache _savedComment = CoreState.CommentCache;
        readonly IEtcCache _savedLint = CoreState.LintCache;
        readonly DecompProject _savedDecomp = CoreState.DecompProject;

        public SongTableExpansionCoreTests()
        {
            CoreState.DecompProject = null;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.Undo = _savedUndo;
            CoreState.CommentCache = _savedComment;
            CoreState.LintCache = _savedLint;
            CoreState.DecompProject = _savedDecomp;
        }

        [Fact]
        public void Expand_AuditFailureAfterGrowth_RestoresRomCachesAndUndo()
        {
            ROM rom = MakeRom(3, addFreeSpace: false);
            byte[] unaligned = new byte[rom.Data.Length + 1];
            Array.Copy(rom.Data, unaligned, rom.Data.Length);
            rom.SwapNewROMDataDirect(unaligned);
            CoreState.ROM = rom;
            var comment = new HeadlessEtcCache();
            var lint = new HeadlessEtcCache();
            CoreState.CommentCache = comment;
            CoreState.LintCache = lint;
            comment.Update(TableBase + 8, "comment-row");
            comment.Update((uint)rom.Data.Length + 4, "comment-tail");
            lint.Update(TableBase + 8, "lint-row");
            lint.Update((uint)rom.Data.Length + 8, "lint-tail");
            for (uint i = 0; i < 65; i++)
                WriteU32(rom.Data, 0x200 + i * 4, U.toPointer(TableBase));

            byte[] romBefore = (byte[])rom.Data.Clone();
            var commentBefore = Capture(comment);
            var lintBefore = Capture(lint);
            var undo = NewUndo(rom, "failing expansion");

            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo))
                result = SongTableExpansionCore.Expand(rom, 5);

            Assert.False(result.Success);
            Assert.Contains("implausible", result.Error);
            Assert.Equal(romBefore, rom.Data);
            Assert.Equal(romBefore.Length, rom.Data.Length);
            Assert.False(rom.Modified);
            Assert.Equal(commentBefore, Capture(comment));
            Assert.Equal(lintBefore, Capture(lint));
            Assert.Empty(undo.list);
            Assert.Empty(undo.CachePatches);
        }

        [Fact]
        public void Expand_UndoRedo_RestoresDestinationCollisions()
        {
            ROM rom = MakeRom(3);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var comment = new HeadlessEtcCache();
            var lint = new HeadlessEtcCache();
            CoreState.CommentCache = comment;
            CoreState.LintCache = lint;
            comment.Update(TableBase + 8, "comment-moved");
            comment.Update(FreeBase + 8, "comment-destination");
            lint.Update(TableBase + 8, "lint-moved");
            lint.Update(FreeBase + 8, "lint-destination");

            Undo.UndoData undo = CoreState.Undo.NewUndoData("expand");
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo))
                result = SongTableExpansionCore.Expand(rom, 5);
            Assert.True(result.Success, result.Error);
            CoreState.Undo.Push(undo);

            Assert.Equal("comment-moved", comment.At(result.NewBaseAddress + 8));
            Assert.Equal("lint-moved", lint.At(result.NewBaseAddress + 8));

            CoreState.Undo.RunUndo();
            Assert.Equal("comment-moved", comment.At(TableBase + 8));
            Assert.Equal("comment-destination", comment.At(FreeBase + 8));
            Assert.Equal("lint-moved", lint.At(TableBase + 8));
            Assert.Equal("lint-destination", lint.At(FreeBase + 8));

            Assert.True(CoreState.Undo.RunRedo());
            Assert.False(comment.CheckFast(TableBase + 8));
            Assert.Equal("comment-moved", comment.At(result.NewBaseAddress + 8));
            Assert.False(lint.CheckFast(TableBase + 8));
            Assert.Equal("lint-moved", lint.At(result.NewBaseAddress + 8));
        }

        [Fact]
        public void Expand_WithLaterEdit_UndoAndRedoReconstructEveryState()
        {
            ROM rom = MakeRom(3);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var comment = new HeadlessEtcCache();
            CoreState.CommentCache = comment;
            CoreState.LintCache = new HeadlessEtcCache();
            comment.Update(TableBase + 8, "moved");

            Undo.UndoData expandUndo = CoreState.Undo.NewUndoData("expand");
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(expandUndo))
                result = SongTableExpansionCore.Expand(rom, 5);
            Assert.True(result.Success, result.Error);
            CoreState.Undo.Push(expandUndo);

            const uint editAddr = 0x800;
            Undo.UndoData editUndo = CoreState.Undo.NewUndoData("edit");
            using (ROM.BeginUndoScope(editUndo))
                rom.write_u8(editAddr, 0xAA);
            CoreState.Undo.Push(editUndo);

            CoreState.Undo.RunUndo();
            Assert.Equal(0u, rom.u8(editAddr));
            Assert.Equal("moved", comment.At(result.NewBaseAddress + 8));

            CoreState.Undo.RunUndo();
            Assert.Equal("moved", comment.At(TableBase + 8));

            Assert.True(CoreState.Undo.RunRedo());
            Assert.Equal("moved", comment.At(result.NewBaseAddress + 8));
            Assert.Equal(0u, rom.u8(editAddr));

            Assert.True(CoreState.Undo.RunRedo());
            Assert.Equal(0xAAu, rom.u8(editAddr));
            Assert.Equal("moved", comment.At(result.NewBaseAddress + 8));
        }

        [Fact]
        public void Undo_TipHadNoCache_DoesNotMutateLaterInstalledCache()
        {
            ROM rom = MakeRom(3, addFreeSpace: false);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.CommentCache = null;
            CoreState.LintCache = new HeadlessEtcCache();
            uint originalLength = (uint)rom.Data.Length;

            Undo.UndoData expandUndo = CoreState.Undo.NewUndoData("expand");
            using (ROM.BeginUndoScope(expandUndo))
            {
                Assert.True(
                    SongTableExpansionCore.Expand(rom, 5).Success);
            }
            CoreState.Undo.Push(expandUndo);

            CoreState.CommentCache = null;
            Undo.UndoData editUndo = CoreState.Undo.NewUndoData("edit");
            using (ROM.BeginUndoScope(editUndo))
                rom.write_u8(0x800, 0xAA);
            CoreState.Undo.Push(editUndo);

            CoreState.Undo.RunUndo();
            var replacement = new HeadlessEtcCache();
            uint replacementKey = originalLength + 4;
            replacement.Update(replacementKey, "replacement");
            CoreState.CommentCache = replacement;

            CoreState.Undo.RunUndo();

            Assert.Equal("replacement", replacement.At(replacementKey));
            Assert.False(replacement.CheckFast(TableBase + 8));
        }

        [Fact]
        public void Undo_GrownRom_RestoresDestinationCacheCapturedBeforeShrink()
        {
            ROM rom = MakeRom(3, addFreeSpace: false);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var comment = new HeadlessEtcCache();
            CoreState.CommentCache = comment;
            CoreState.LintCache = new HeadlessEtcCache();
            uint originalLength = (uint)rom.Data.Length;
            comment.Update(TableBase + 8, "moved");
            comment.Update(originalLength + 8, "destination-before");

            Undo.UndoData undo = CoreState.Undo.NewUndoData("expand");
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo))
                result = SongTableExpansionCore.Expand(rom, 5);
            Assert.True(result.Success, result.Error);
            CoreState.Undo.Push(undo);

            Assert.Equal("moved", comment.At(result.NewBaseAddress + 8));
            CoreState.Undo.RunUndo();

            Assert.Equal(originalLength, (uint)rom.Data.Length);
            Assert.Equal("moved", comment.At(TableBase + 8));
            Assert.Equal(
                "destination-before", comment.At(originalLength + 8));
        }

        [Fact]
        public void Undo_RestoresIntoReplacementLiveCacheInstance()
        {
            ROM rom = MakeRom(3);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var original = new HeadlessEtcCache();
            CoreState.CommentCache = original;
            CoreState.LintCache = new HeadlessEtcCache();
            original.Update(TableBase + 8, "original-row");

            Undo.UndoData expandUndo = CoreState.Undo.NewUndoData("expand");
            using (ROM.BeginUndoScope(expandUndo))
            {
                Assert.True(
                    SongTableExpansionCore.Expand(rom, 5).Success);
            }
            CoreState.Undo.Push(expandUndo);

            var replacement = new HeadlessEtcCache();
            replacement.Update(0x777, "replacement-only");
            CoreState.CommentCache = replacement;

            CoreState.Undo.RunUndo();

            Assert.Equal("replacement-only", replacement.At(0x777));
            Assert.Equal("original-row", replacement.At(TableBase + 8));
        }

        [Fact]
        public void Expand_UnsupportedCache_RefusesBeforeRomMutation()
        {
            ROM rom = MakeRom(3);
            CoreState.ROM = rom;
            CoreState.CommentCache = new UnsupportedCache();
            CoreState.LintCache = new HeadlessEtcCache();
            byte[] before = (byte[])rom.Data.Clone();

            DataExpansionCore.ExpandResult result =
                SongTableExpansionCore.Expand(rom, 5);

            Assert.False(result.Success);
            Assert.Contains("transactional comment cache", result.Error);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void Expand_MissingRangeRestore_RollsBackWithoutCommit()
        {
            ROM rom = MakeRom(3);
            CoreState.ROM = rom;
            CoreState.CommentCache = new FullOnlyCache();
            CoreState.LintCache = new HeadlessEtcCache();
            byte[] before = (byte[])rom.Data.Clone();
            var undo = NewUndo(rom, "range restore unsupported");

            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo))
                result = SongTableExpansionCore.Expand(rom, 5);

            Assert.False(result.Success);
            Assert.Equal(before, rom.Data);
            Assert.Empty(undo.list);
            Assert.Empty(undo.CachePatches);
        }

        [Fact]
        public void Undo_UnsupportedReplacementCache_DoesNotThrowAfterRomRestore()
        {
            ROM rom = MakeRom(3);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.CommentCache = new HeadlessEtcCache();
            CoreState.LintCache = new HeadlessEtcCache();
            byte[] before = (byte[])rom.Data.Clone();

            Undo.UndoData undo = CoreState.Undo.NewUndoData("expand");
            using (ROM.BeginUndoScope(undo))
            {
                Assert.True(
                    SongTableExpansionCore.Expand(rom, 5).Success);
            }
            CoreState.Undo.Push(undo);
            CoreState.CommentCache = new UnsupportedCache();

            Exception error = Record.Exception(
                () => CoreState.Undo.RunUndo());

            Assert.Null(error);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void Undo_FullOnlyReplacementCache_RemainsUnchanged()
        {
            ROM rom = MakeRom(3);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.CommentCache = new HeadlessEtcCache();
            CoreState.LintCache = new HeadlessEtcCache();

            Undo.UndoData undo = CoreState.Undo.NewUndoData("expand");
            using (ROM.BeginUndoScope(undo))
            {
                Assert.True(
                    SongTableExpansionCore.Expand(rom, 5).Success);
            }
            CoreState.Undo.Push(undo);

            var replacement = new FullOnlyCache();
            replacement.Update(0x777, "replacement");
            CoreState.CommentCache = replacement;
            CoreState.Undo.RunUndo();

            Assert.Equal("replacement", replacement.At(0x777));
        }

        [Fact]
        public void Expand_UsesExplicitRomWhenGlobalDiffers()
        {
            ROM target = MakeRom(3);
            ROM other = MakeRom(1);
            CoreState.ROM = other;
            CoreState.CommentCache = new HeadlessEtcCache();
            CoreState.LintCache = new HeadlessEtcCache();

            DataExpansionCore.ExpandResult result =
                SongTableExpansionCore.Expand(target, 5);

            Assert.True(result.Success, result.Error);
            Assert.Equal(
                result.NewBaseAddress,
                target.p32(target.RomInfo.sound_table_pointer));
            Assert.Equal(
                TableBase,
                other.p32(other.RomInfo.sound_table_pointer));
        }

        [Fact]
        public void Undo_AfterBranchTruncation_CapturesNewTip()
        {
            ROM rom = MakeRom(3);
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            var comment = new HeadlessEtcCache();
            CoreState.CommentCache = comment;
            CoreState.LintCache = new HeadlessEtcCache();
            comment.Update(TableBase + 8, "original");

            Undo.UndoData expandUndo = CoreState.Undo.NewUndoData("expand");
            using (ROM.BeginUndoScope(expandUndo))
            {
                Assert.True(
                    SongTableExpansionCore.Expand(rom, 5).Success);
            }
            CoreState.Undo.Push(expandUndo);
            CoreState.Undo.RunUndo();
            Assert.Equal("original", comment.At(TableBase + 8));

            Undo.UndoData replacementEdit =
                CoreState.Undo.NewUndoData("replacement edit");
            using (ROM.BeginUndoScope(replacementEdit))
                rom.write_u8(0x800, 0xBB);
            CoreState.Undo.Push(replacementEdit);
            Assert.Single(CoreState.Undo.UndoBuffer);

            CoreState.Undo.RunUndo();

            Assert.Equal(0u, rom.u8(0x800));
            Assert.Equal("original", comment.At(TableBase + 8));
            Assert.True(CoreState.Undo.CanRedo);
        }

        [Fact]
        public void DefaultExpandTableTo_RepointsCacheWithoutUndoPatch()
        {
            ROM rom = MakeRom(3);
            CoreState.ROM = rom;
            var comment = new HeadlessEtcCache();
            CoreState.CommentCache = comment;
            CoreState.LintCache = new HeadlessEtcCache();
            comment.Update(TableBase + 8, "forward-only");
            var undo = NewUndo(rom, "default expand");

            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo))
            {
                result = DataExpansionCore.ExpandTableTo(
                    rom,
                    rom.RomInfo.sound_table_pointer,
                    8,
                    3,
                    5);
            }

            Assert.True(result.Success, result.Error);
            Assert.Equal(
                "forward-only", comment.At(result.NewBaseAddress + 8));
            Assert.Empty(undo.CachePatches);
        }

        static ROM MakeRom(
            uint count,
            int length = 0x1000000,
            bool addFreeSpace = true)
        {
            var rom = new ROM();
            rom.LoadLow("song-core-expand.gba", new byte[length], "BE8E01");
            WriteU32(
                rom.Data,
                rom.RomInfo.sound_table_pointer,
                U.toPointer(TableBase));
            for (uint i = 0; i < count; i++)
            {
                uint row = TableBase + i * 8;
                WriteU32(
                    rom.Data,
                    row,
                    U.toPointer(0x4000 + i * 0x20));
                WriteU32(rom.Data, row + 4, i);
            }
            WriteU32(rom.Data, TableBase + count * 8, 0xFFFFFFFF);

            if (addFreeSpace)
            {
                for (uint i = 0; i < 0x2000; i++)
                    rom.Data[FreeBase + i] = 0xFF;
            }
            return rom;
        }

        static Undo.UndoData NewUndo(ROM rom, string name)
        {
            return new Undo.UndoData
            {
                time = DateTime.Now,
                name = name,
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
        }

        static Dictionary<uint, string> Capture(IEtcCache cache)
        {
            Assert.True(cache.TryCaptureAll(out var snapshot));
            return snapshot.Entries.ToDictionary(
                pair => pair.Key, pair => pair.Value);
        }

        static void WriteU32(byte[] data, uint offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        sealed class UnsupportedCache : IEtcCache
        {
            public void RemoveOverRange(uint range) { }
            public void RemoveRange(uint start, uint end) { }
            public bool CheckFast(uint num) => false;
            public string At(uint num, string def = "") => def;
            public string S_At(uint num) => "";
            public bool TryGetValue(uint num, out string outData)
            {
                outData = "";
                return false;
            }
            public void Update(uint addr, string comment) { }
            public void Remove(uint addr) { }
        }

        sealed class FullOnlyCache : IEtcCache
        {
            readonly HeadlessEtcCache _inner = new();

            public void RemoveOverRange(uint range) =>
                _inner.RemoveOverRange(range);
            public void RemoveRange(uint start, uint end) =>
                _inner.RemoveRange(start, end);
            public bool CheckFast(uint num) => _inner.CheckFast(num);
            public string At(uint num, string def = "") =>
                _inner.At(num, def);
            public string S_At(uint num) => _inner.S_At(num);
            public bool TryGetValue(uint num, out string outData) =>
                _inner.TryGetValue(num, out outData);
            public void Update(uint addr, string comment) =>
                _inner.Update(addr, comment);
            public void Remove(uint addr) => _inner.Remove(addr);
            public void RepointEtcData(
                uint oldAddr, uint oldSize, uint newAddr) =>
                _inner.RepointEtcData(oldAddr, oldSize, newAddr);
            public bool TryCaptureAll(out EtcCacheSnapshot snapshot) =>
                _inner.TryCaptureAll(out snapshot);
            public bool TryCaptureRanges(
                IReadOnlyList<EtcCacheRange> ranges,
                out EtcCacheSnapshot snapshot) =>
                _inner.TryCaptureRanges(ranges, out snapshot);
            public bool TryRestoreAll(EtcCacheSnapshot snapshot) =>
                _inner.TryRestoreAll(snapshot);
        }
    }
}
