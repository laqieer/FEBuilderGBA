// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for #1423 — the Avalonia Song Table editor must write-protect the
// reserved Song ID 0 (silence/unset) entry, matching WinForms
// (SongTableForm.cs:21 UseWriteProtectionID00 = true; InputFormRef
// CheckWriteProtectionID00 hard-refuses) and the sibling SongTrackView.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SongTableWriteProtectionTests
    {
        // FE8U sound_table_pointer = 0x28BC (ROMFE8U.cs). We plant a song table
        // pointer there → base 0x3000, then fill a few 8-byte entries so the
        // VM's LoadSongList/LoadSong see real data.
        const uint TableBase = 0x3000;

        // ------------------------------------------------------------------
        // VM behaviour
        // ------------------------------------------------------------------

        [Fact]
        public void WriteSong_OnSongId0_DoesNotMutateRom_AndReturnsFalse()
        {
            var rom = MakeRomWithSongTable();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTableViewModel();
                // Row 0 = the reserved silence entry at the table base.
                vm.LoadSong(TableBase);
                Assert.True(vm.IsSongIdZero);

                uint before0 = rom.u32(TableBase + 0);
                uint before4 = rom.u32(TableBase + 4);

                // Attempt to overwrite the header pointer + PlayerType.
                vm.SongHeaderPointer = 0x08001234;
                vm.PlayerType = 0x55;
                bool wrote = vm.WriteSong();

                Assert.False(wrote);                       // refusal is observable
                Assert.Equal(before0, rom.u32(TableBase + 0)); // unchanged
                Assert.Equal(before4, rom.u32(TableBase + 4)); // unchanged
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void WriteSong_OnNonZeroSong_Mutates_AndReturnsTrue()
        {
            var rom = MakeRomWithSongTable();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTableViewModel();
                // Row 1 = entry at base + 8.
                vm.LoadSong(TableBase + 8);
                Assert.False(vm.IsSongIdZero);
                Assert.Equal(1u, vm.SongIndex);

                vm.SongHeaderPointer = 0x08005678;
                vm.PlayerType = 0x07;
                bool wrote = vm.WriteSong();

                Assert.True(wrote);
                Assert.Equal(0x08005678u, rom.u32(TableBase + 8 + 0));
                Assert.Equal(0x07u, rom.u32(TableBase + 8 + 4));
            }
            finally { CoreState.ROM = prev; }
        }

        // Copilot Finding 1 regression guard: a direct LoadSong(base + 8) that
        // never went through list selection must still be treated as a non-zero
        // row (SongIndex derived from the table base, not a stale default of 0).
        [Fact]
        public void WriteSong_DirectLoadRow1_WithoutSelection_Mutates()
        {
            var rom = MakeRomWithSongTable();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTableViewModel();
                // Brand-new VM: SongIndex defaults to 0. LoadSong must overwrite
                // it with the derived index so the guard does not over-block.
                vm.LoadSong(TableBase + 8);

                Assert.Equal(1u, vm.SongIndex);
                Assert.False(vm.IsSongIdZero);

                vm.SongHeaderPointer = 0x08009ABC;
                vm.PlayerType = 0x02;
                Assert.True(vm.WriteSong());
                Assert.Equal(0x08009ABCu, rom.u32(TableBase + 8 + 0));
            }
            finally { CoreState.ROM = prev; }
        }

        // WriteSong() must report a refusal (false, no mutation) for an
        // out-of-range entry too, so Write_Click can roll back instead of falsely
        // reporting success (Copilot non-blocking follow-up).
        [Fact]
        public void WriteSong_OutOfRangeAddress_ReturnsFalse_NoMutation()
        {
            var rom = MakeRomWithSongTable();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTableViewModel();
                // Force an address past the end of the ROM without going through
                // LoadSong (which would reject it). CurrentAddr is public.
                vm.CurrentAddr = (uint)rom.Data.Length - 2; // +7 overflows EOF
                vm.SongIndex = 5;                            // not Song ID 0
                vm.SongHeaderPointer = 0x08001111;
                vm.PlayerType = 1;

                Assert.False(vm.WriteSong());
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void IsSongIdZero_TrueForBase_FalseForRow1()
        {
            var rom = MakeRomWithSongTable();
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var vm = new SongTableViewModel();

                vm.LoadSong(TableBase);
                Assert.True(vm.IsSongIdZero);
                Assert.Equal(0u, vm.SongIndex);

                vm.LoadSong(TableBase + 8);
                Assert.False(vm.IsSongIdZero);
                Assert.Equal(1u, vm.SongIndex);

                vm.LoadSong(TableBase + 16);
                Assert.False(vm.IsSongIdZero);
                Assert.Equal(2u, vm.SongIndex);
            }
            finally { CoreState.ROM = prev; }
        }

        // ------------------------------------------------------------------
        // View source — the Write_Click guard mirrors SongTrackView exactly.
        // ------------------------------------------------------------------

        [Fact]
        public void SongTableView_WriteClick_HasWriteProtectionGuard()
        {
            string cs = File.ReadAllText(ViewPath("SongTableView.axaml.cs"));

            // Guard present before the undo scope opens.
            Assert.Contains("Song ID 0 is write-protected (silence song).", cs);
            Assert.Contains("ShowError", cs);

            // The actual guard returns before Begin() so no undo scope opens for
            // row 0. Use the full if-statement as the needle (not the first bare
            // "IsSongIdZero", which now also appears in an OnSongSelected comment)
            // so this assertion fails if the real guard is removed or moved below
            // the undo scope.
            int guardIdx = cs.IndexOf("if (_vm.IsSongIdZero)", StringComparison.Ordinal);
            int beginIdx = cs.IndexOf("_undoService.Begin(\"Edit Song Table\")", StringComparison.Ordinal);
            Assert.True(guardIdx >= 0, "Write_Click must contain the 'if (_vm.IsSongIdZero)' guard.");
            Assert.True(beginIdx >= 0, "Write_Click must open an undo scope.");
            Assert.True(guardIdx < beginIdx,
                "The 'if (_vm.IsSongIdZero)' guard must appear before the undo scope opens.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static ROM MakeRomWithSongTable()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

            // sound_table_pointer (0x28BC for FE8U) -> table base 0x3000.
            uint ptr = rom.RomInfo.sound_table_pointer;
            Assert.Equal(0x28BCu, ptr);
            WritePtr(rom, ptr, TableBase);

            // Plant 3 song-table entries (8 bytes each). Each entry's u32@+0 must
            // be a valid pointer (LoadSongList stops at the first non-pointer).
            for (uint i = 0; i < 3; i++)
            {
                uint entry = TableBase + i * 8;
                WritePtr(rom, entry + 0, 0x4000 + i * 0x100); // header pointer
                WritePtr(rom, entry + 4, 0);                  // PlayerType
            }
            return rom;
        }

        static void WritePtr(ROM rom, uint at, uint targetOffset)
        {
            uint ptr = targetOffset == 0 ? 0 : targetOffset + 0x08000000;
            rom.Data[at + 0] = (byte)(ptr & 0xFF);
            rom.Data[at + 1] = (byte)((ptr >> 8) & 0xFF);
            rom.Data[at + 2] = (byte)((ptr >> 16) & 0xFF);
            rom.Data[at + 3] = (byte)((ptr >> 24) & 0xFF);
        }

        static string ViewPath(string fileName)
            => Path.Combine(FindProjectRoot(), "FEBuilderGBA.Avalonia", "Views", fileName);

        static string FindProjectRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    return dir;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }
    }
}
