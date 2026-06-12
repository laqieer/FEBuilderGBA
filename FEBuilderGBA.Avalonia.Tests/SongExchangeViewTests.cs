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
