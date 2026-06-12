// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia headless tests for the Song Exchange Tool view (#1002 Slice 3):
//   * The View is no longer a placeholder — it has two song ListBoxes, an
//     "Open Other ROM" button, and a "Convert" button wired under an undo scope.
//   * Loading a ROM populates the current-song ListBox via LoadCurrentSongs.
//   * The dest-index-0 guard blocks a convert to SongID 0x0.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SongExchangeViewTests
    {
        // ------------------------------------------------------------------
        // Source-text assertions (no ROM / no Avalonia app needed).
        // ------------------------------------------------------------------

        [Fact]
        public void View_IsNoLongerPlaceholder()
        {
            string axaml = File.ReadAllText(ViewPath("SongExchangeView.axaml"));
            string cs = File.ReadAllText(ViewPath("SongExchangeView.axaml.cs"));

            // Two song lists + Open Other ROM + Convert buttons.
            Assert.Contains("SongExchange_My_List", axaml);
            Assert.Contains("SongExchange_Other_List", axaml);
            Assert.Contains("SongExchange_OpenOther_Button", axaml);
            Assert.Contains("SongExchange_Convert_Button", axaml);
            Assert.Contains("Click=\"OpenOtherRom_Click\"", axaml);
            Assert.Contains("Click=\"Convert_Click\"", axaml);

            // Existing harness IDs preserved.
            Assert.Contains("SongExchange_Entry_List", axaml);
            Assert.Contains("SongExchange_Addr_Label", axaml);

            // Convert runs under an undo scope and routes through the VM.
            Assert.Contains("_undoService.Begin", cs);
            Assert.Contains("_undoService.Commit", cs);
            Assert.Contains("_undoService.Rollback", cs);
            Assert.Contains("_vm.Convert(", cs);
            // Dest-0 guard + file picker.
            Assert.Contains("SongID 0x0", cs);
            Assert.Contains("OpenFilePickerAsync", cs);
            // Review fix vPo — the OtherRom label is localized via R._ (no raw format string).
            Assert.Contains("R._(\"Other ROM: {0} ({1} songs)\"", cs);
            // Review fix vPv — the View surfaces the partial-corrupt structure warning.
            Assert.Contains("LastConvertHadStructureWarning", cs);
            Assert.Contains("some instrument data was corrupt", cs);
        }

        // ------------------------------------------------------------------
        // ViewModel behavior against a REAL ROM.
        // ------------------------------------------------------------------

        [Fact]
        public void ViewModel_LoadCurrentSongs_PopulatesDisplay()
        {
            string romPath = FindTestRom();
            if (romPath == null) return; // skip if no ROM.

            var prev = CoreState.ROM;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;

                var vm = new SongExchangeViewModel();
                vm.LoadCurrentSongs();

                Assert.True(vm.IsLoaded);
                Assert.NotEmpty(vm.MySongList);
                Assert.NotEmpty(vm.MySongDisplay);
                Assert.Equal(vm.MySongList.Count, vm.MySongDisplay.Count);
                // WF display format.
                Assert.Contains("Table:", vm.MySongDisplay[1]);
                Assert.Contains("Voices:", vm.MySongDisplay[1]);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void ViewModel_ConvertWithoutOtherRom_ReturnsError()
        {
            string romPath = FindTestRom();
            if (romPath == null) return;

            var prev = CoreState.ROM;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                if (CoreState.Undo == null) CoreState.Undo = new Undo();

                var vm = new SongExchangeViewModel();
                vm.LoadCurrentSongs();

                // No other ROM loaded -> error (no scope needed for the guard path).
                Undo.UndoData undo = CoreState.Undo.NewUndoData("t");
                string err;
                using (ROM.BeginUndoScope(undo))
                    err = vm.Convert(0, 1, undo);
                Assert.NotEqual("", err);
                Assert.False(vm.HasOtherRom);
            }
            finally { CoreState.ROM = prev; }
        }

        [Fact]
        public void ViewModel_ConvertToDestZero_IsBlocked()
        {
            string romPath = FindTestRom();
            if (romPath == null) return;

            var prev = CoreState.ROM;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;
                if (CoreState.Undo == null) CoreState.Undo = new Undo();

                var vm = new SongExchangeViewModel();
                vm.LoadCurrentSongs();
                // Load the SAME ROM as the donor so OtherSongList is populated.
                vm.LoadOtherRom(File.ReadAllBytes(romPath), romPath);
                Assert.True(vm.HasOtherRom);

                Undo.UndoData undo = CoreState.Undo.NewUndoData("t");
                byte[] before = (byte[])rom.Data.Clone();
                string err;
                using (ROM.BeginUndoScope(undo))
                    err = vm.Convert(1, 0, undo); // dest index 0 = SongID 0x0.
                Assert.Contains("0x0", err);
                Assert.Equal(before, rom.Data); // no mutation.
            }
            finally { CoreState.ROM = prev; }
        }

        // ------------------------------------------------------------------
        // Review fix vPv — the VM exposes a structure-warning flag that the View
        // reads to warn on a partial-corrupt source. Build synthetic donor + dest
        // ROMs (with a planted song-table signature) so the VM parse + convert run
        // hermetically. A donor voice with a truncated sample flags the warning.
        // ------------------------------------------------------------------
        [Fact]
        public void ViewModel_PartialCorruptSource_SetsStructureWarningFlag()
        {
            var prev = CoreState.ROM;
            try
            {
                // DEST ROM: a synthetic FE8U ROM (real RomInfo, so LoadCurrentSongs'
                // sound_table_pointer=0x28BC path works) with a clean song table planted.
                byte[] destData = BuildSongTableRom(forFe8uPointerSlot: true, corruptVoice: false);
                var destRom = new ROM();
                destRom.LoadLow("dest.gba", destData, "BE8E01"); // FE8U RomInfo.
                CoreState.ROM = destRom;
                if (CoreState.Undo == null) CoreState.Undo = new Undo();

                // DONOR ROM bytes: located BY SCAN (signature planted), with a truncated
                // DirectSound sample on song #1's voice (flags HadStructureWarning).
                byte[] donorData = BuildSongTableRom(forFe8uPointerSlot: false, corruptVoice: true);

                var vm = new SongExchangeViewModel();
                vm.LoadCurrentSongs();
                vm.LoadOtherRom(donorData, "donor.gba");

                // Default before any convert.
                Assert.False(vm.LastConvertHadStructureWarning);

                // The synthetic tables are deterministic: both lists MUST parse to >= 2
                // entries (song #0 dummy + song #1). Preconditions, not skips.
                Assert.True(vm.MySongList.Count > 1, "dest synthetic song table must parse >= 2 entries");
                Assert.True(vm.OtherSongList.Count > 1, "donor synthetic song table must parse >= 2 entries");

                Undo.UndoData undo = CoreState.Undo.NewUndoData("t");
                string err;
                using (ROM.BeginUndoScope(undo))
                    err = vm.Convert(1, 1, undo);

                Assert.Equal("", err);
                Assert.True(vm.LastConvertHadStructureWarning,
                    "a partial-corrupt source must set LastConvertHadStructureWarning");
            }
            finally { CoreState.ROM = prev; }
        }

        // Build a ROM whose song table is locatable either via the FE8U
        // sound_table_pointer (0x28BC, for the loaded DEST) OR by SCANNING the
        // planted 30-byte sound-engine signature (for the DONOR). Song #0 = dummy
        // (tc=0), song #1 = a 1-track DirectSound song.
        static byte[] BuildSongTableRom(bool forFe8uPointerSlot, bool corruptVoice)
        {
            byte[] d = new byte[0x1000000]; // 16 MiB so the FE8U RomInfo loads cleanly.
            uint tableOff = 0x300000;       // table start, well clear of headers/samples.

            if (forFe8uPointerSlot)
            {
                // DEST path: LoadCurrentSongs reads the pointer at FE8U's 0x28BC.
                Gba(d, 0x28BC, tableOff);
            }
            else
            {
                // DONOR path: plant the sound-engine signature so the byte-scan
                // resolves the table-pointer slot at found + 30 + 10.
                byte[] sig = new byte[] {
                    0x00, 0xB5, 0x00, 0x04, 0x07, 0x4A, 0x08, 0x49,
                    0x40, 0x0B, 0x40, 0x18, 0x83, 0x88, 0x59, 0x00,
                    0xC9, 0x18, 0x89, 0x00, 0x89, 0x18, 0x0A, 0x68,
                    0x01, 0x68, 0x10, 0x1C, 0x00, 0xF0
                };
                uint sigAt = 0x500;
                Array.Copy(sig, 0, d, (int)sigAt, sig.Length);
                uint slotOff = sigAt + (uint)sig.Length + 10;
                Gba(d, slotOff, tableOff);
            }

            uint dummyHdr = 0x301000, songHdr = 0x302000, voices = 0x303000, track0 = 0x304000, sample = 0x305000;

            // Song table: entry0 -> dummy header (tc=0); entry1 -> song header.
            Gba(d, tableOff + 0, dummyHdr);
            Gba(d, tableOff + 8, songHdr);
            d[dummyHdr] = 0; // dummy tc=0.

            // song #1 header: tc=1, voices ptr, track0 ptr.
            d[songHdr] = 1;
            d[songHdr + 2] = 0x0A;
            d[songHdr + 3] = 0x80;
            Gba(d, songHdr + 4, voices);
            Gba(d, songHdr + 8, track0);

            // voice 0 = DirectSound.
            d[voices] = 0x00;
            uint sampleAt = corruptVoice ? (uint)(d.Length - 8) : sample;
            Gba(d, voices + 4, sampleAt);
            if (!corruptVoice)
            {
                d[sample + 0] = 0x7A; d[sample + 1] = 0x6B; d[sample + 2] = 0x5C; d[sample + 3] = 0x4D;
                d[sample + 0x0C] = 4; // body length at +12
                d[sample + 0x10] = 0xDE; d[sample + 0x11] = 0xAD; d[sample + 0x12] = 0xBE; d[sample + 0x13] = 0xEF;
            }

            // track0: BD 00, B1.
            d[track0] = 0xBD; d[track0 + 1] = 0x00; d[track0 + 2] = 0xB1;
            return d;
        }

        static void Gba(byte[] d, uint at, uint off)
        {
            uint p = off + 0x08000000;
            d[at + 0] = (byte)(p & 0xFF);
            d[at + 1] = (byte)((p >> 8) & 0xFF);
            d[at + 2] = (byte)((p >> 16) & 0xFF);
            d[at + 3] = (byte)((p >> 24) & 0xFF);
        }

        // ------------------------------------------------------------------
        // Headless UI: the current-song ListBox populates on Open.
        // ------------------------------------------------------------------

        [AvaloniaFact]
        public void View_OnOpen_PopulatesCurrentSongListBox()
        {
            string romPath = FindTestRom();
            if (romPath == null) return;

            var prev = CoreState.ROM;
            try
            {
                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return;
                CoreState.ROM = rom;

                var view = new global::FEBuilderGBA.Avalonia.Views.SongExchangeView();
                var myList = view.FindControl<ListBox>("MyList");
                Assert.NotNull(myList);

                // Drive the VM through LoadCurrentSongs (what Opened does).
                var vmField = typeof(global::FEBuilderGBA.Avalonia.Views.SongExchangeView).GetField(
                    "_vm",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(vmField);
                var vm = (SongExchangeViewModel)vmField!.GetValue(view)!;
                vm.LoadCurrentSongs();

                // The ListBox is bound to MySongDisplay, which must be populated.
                Assert.NotEmpty(vm.MySongDisplay);
            }
            finally { CoreState.ROM = prev; }
        }

        // ------------------------------------------------------------------
        // Helpers.
        // ------------------------------------------------------------------

        static string ViewPath(string fileName)
            => Path.Combine(FindProjectRoot(), "FEBuilderGBA.Avalonia", "Views", fileName);

        static string FindProjectRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }

        static string FindTestRom()
        {
            string root;
            try { root = FindProjectRoot(); } catch { return null; }
            string romsDir = Path.Combine(root, "roms");
            if (!Directory.Exists(romsDir)) return null;
            string[] preferred = { "FE8U.gba", "FE7U.gba", "FE8J.gba", "FE7J.gba", "FE6.gba" };
            foreach (string name in preferred)
            {
                string path = Path.Combine(romsDir, name);
                if (File.Exists(path)) return path;
            }
            string[] gba = Directory.GetFiles(romsDir, "*.gba");
            return gba.Length > 0 ? gba[0] : null;
        }
    }
}
