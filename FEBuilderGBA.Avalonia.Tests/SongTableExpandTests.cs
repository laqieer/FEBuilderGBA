using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SongTableExpandTests : IDisposable
    {
        const uint TableBase = 0x3000;
        readonly ROM? _savedRom = CoreState.ROM;

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            NameResolver.ClearCache();
        }

        [Fact]
        public void LoadSongList_ScansBeyondFormer1024Cap()
        {
            CoreState.ROM = MakeRomWithSongTable(1025);
            var vm = new SongTableViewModel();

            List<AddrResult> rows = vm.LoadSongList();

            Assert.Equal(1025, rows.Count);
            Assert.Equal(1024u, rows[^1].tag);
        }

        [Fact]
        public void ShouldShowExpansion_MatchesConfigOrExpandedAddress()
        {
            ROM rom = MakeRomWithSongTable(3);

            Assert.False(SongTableViewModel.ShouldShowExpansion(
                rom, configuredShow: false));
            Assert.True(SongTableViewModel.ShouldShowExpansion(
                rom, configuredShow: true));

            WriteU32(rom.Data, (int)rom.RomInfo.sound_table_pointer,
                rom.RomInfo.extends_address);
            Assert.True(SongTableViewModel.ShouldShowExpansion(
                rom, configuredShow: false));
        }

        [Fact]
        public void ExpandSongTable_CopiesSilenceRowAndRepointsEveryReference()
        {
            ROM rom = MakeRomWithSongTable(3);
            CoreState.ROM = rom;
            uint pointerSlot = rom.RomInfo.sound_table_pointer;
            const uint extraReference = 0x200;
            WriteU32(rom.Data, (int)extraReference, U.toPointer(TableBase));
            byte[] row0 = rom.getBinaryData(TableBase, 8);
            FillFreeSpace(rom, 0x100000, 0x2000);
            var vm = new SongTableViewModel();

            DataExpansionCore.ExpandResult result = vm.ExpandSongTable(5);

            Assert.True(result.Success, result.Error);
            Assert.Equal(5u, result.NewCount);
            Assert.NotEqual(TableBase, result.NewBaseAddress);
            Assert.Equal(result.NewBaseAddress, rom.p32(pointerSlot));
            Assert.Equal(U.toPointer(result.NewBaseAddress),
                rom.u32(extraReference));
            Assert.Equal(row0, rom.getBinaryData(
                result.NewBaseAddress + 3 * 8, 8));
            Assert.Equal(row0, rom.getBinaryData(
                result.NewBaseAddress + 4 * 8, 8));
            Assert.Equal(0xFFFFFFFFu,
                rom.u32(result.NewBaseAddress + 5 * 8));
            Assert.All(rom.getBinaryData(TableBase, 3 * 8),
                value => Assert.Equal(0, value));
            Assert.Contains(pointerSlot, result.RepointedSlots);
            Assert.Contains(extraReference, result.RepointedSlots);
        }

        [Theory]
        [InlineData(2u)]
        [InlineData(3u)]
        [InlineData(32767u)]
        public void ExpandSongTable_RejectsNonGrowthOrOverMaximum(
            uint newCount)
        {
            ROM rom = MakeRomWithSongTable(3);
            CoreState.ROM = rom;
            byte[] snapshot = (byte[])rom.Data.Clone();
            var vm = new SongTableViewModel();

            DataExpansionCore.ExpandResult result =
                vm.ExpandSongTable(newCount);

            Assert.False(result.Success);
            Assert.Equal(snapshot, rom.Data);
        }

        [Fact]
        public void ExpandSongTable_ImplausibleReferenceFloodRestoresRomAndUndo()
        {
            ROM rom = MakeRomWithSongTable(3);
            CoreState.ROM = rom;
            for (uint i = 0; i < 65; i++)
                WriteU32(rom.Data, (int)(0x200 + i * 4),
                    U.toPointer(TableBase));
            FillFreeSpace(rom, 0x100000, 0x2000);
            byte[] snapshot = (byte[])rom.Data.Clone();
            var undo = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "Rejected Song table expand",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            var vm = new SongTableViewModel();

            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(undo))
            {
                result = vm.ExpandSongTable(5);
            }

            Assert.False(result.Success);
            Assert.Contains("implausible", result.Error);
            Assert.Equal(snapshot, rom.Data);
            Assert.Empty(undo.list);
        }

        [Fact]
        public void ExpandSongTable_AmbientUndoRestoresRom()
        {
            ROM rom = MakeRomWithSongTable(3);
            CoreState.ROM = rom;
            FillFreeSpace(rom, 0x100000, 0x2000);
            byte[] snapshot = (byte[])rom.Data.Clone();
            var undo = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "Song table expand",
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
            var vm = new SongTableViewModel();

            using (ROM.BeginUndoScope(undo))
            {
                DataExpansionCore.ExpandResult result =
                    vm.ExpandSongTable(5);
                Assert.True(result.Success, result.Error);
            }

            for (int i = undo.list.Count - 1; i >= 0; i--)
            {
                Undo.UndoPostion position = undo.list[i];
                Array.Copy(position.data, 0, rom.Data, position.addr,
                    position.data.Length);
            }
            Assert.Equal(snapshot, rom.Data);
        }

        static ROM MakeRomWithSongTable(uint count)
        {
            var rom = new ROM();
            rom.LoadLow("song-expand.gba", new byte[0x1000000],
                "BE8E01");
            WriteU32(rom.Data, (int)rom.RomInfo.sound_table_pointer,
                U.toPointer(TableBase));

            for (uint i = 0; i < count; i++)
            {
                uint addr = TableBase + i * 8;
                WriteU32(rom.Data, (int)addr,
                    U.toPointer(0x4000 + (i % 8) * 0x20));
                WriteU32(rom.Data, (int)(addr + 4), i);
            }
            WriteU32(rom.Data, (int)(TableBase + count * 8),
                0xFFFFFFFF);
            return rom;
        }

        static void FillFreeSpace(ROM rom, uint start, uint length)
        {
            for (uint i = 0; i < length; i++)
                rom.Data[start + i] = 0xFF;
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
