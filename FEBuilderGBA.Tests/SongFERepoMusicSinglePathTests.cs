using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Tests
{
    /// <summary>
    /// #1383 — single music-import-path guards for the WinForms Song editors.
    ///
    /// Requirement: the new "FE-Repo-Music" buttons must NOT introduce a second
    /// import code path. Both SongTrackForm and SongExchangeForm must route a
    /// chosen music file through the one shared dispatcher
    /// (<see cref="SongTrackForm.ImportMusicFileToSong"/>) — SongTrackForm via its
    /// existing AutoDrag -> ImportButton_Click reuse, SongExchangeForm by calling
    /// the dispatcher directly.
    ///
    /// These are source-text + reflection guards (no GUI / ROM required) so they
    /// run in headless CI.
    /// </summary>
    public class SongFERepoMusicSinglePathTests
    {
        [Fact]
        public void SharedDispatcher_IsPublicStatic_OnSongTrackForm()
        {
            // The single dispatcher must exist as a public static method so
            // SongExchangeForm can reuse it (no duplicate .s/.mid/.wav importer).
            MethodInfo m = typeof(SongTrackForm).GetMethod(
                "ImportMusicFileToSong",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(m);
        }

        [Fact]
        public void SongExchangeForm_CallsSharedDispatcher_NotADuplicateImporter()
        {
            string src = ReadSource("SongExchangeForm.cs");
            // SongExchange routes the FE-Repo-Music selection through the shared
            // dispatcher.
            Assert.Contains("SongTrackForm.ImportMusicFileToSong", src);
            // And it must NOT re-implement its own per-extension .s/.mid import:
            // the only music-file importers (ImportS / ImportMidiFile / ImportWave
            // / ImportInstrument) belong to the shared dispatcher, never here.
            Assert.DoesNotContain("SongUtil.ImportS(", src);
            Assert.DoesNotContain("SongUtil.ImportMidiFile(", src);
            Assert.DoesNotContain("SongUtil.ImportWave(", src);
            // The old clipboard-copy behaviour was removed (now a real import).
            Assert.DoesNotContain("Clipboard.SetText(browser.SelectedFilePath)", src);
        }

        [Fact]
        public void SongTrackForm_FERepoButton_ReusesImportButtonClick_ViaAutoDrag()
        {
            string src = ReadSource("SongTrackForm.cs");
            // The FE-Repo-Music handler must funnel through the existing
            // ImportButton_Click using the AutoDrag mechanism — exactly the same
            // reuse the drag-and-drop handler uses. No separate dispatch.
            int handler = src.IndexOf("void FERepoMusicButton_Click", StringComparison.Ordinal);
            Assert.True(handler >= 0, "SongTrackForm.FERepoMusicButton_Click not found");
            string handlerBody = src.Substring(handler);
            Assert.Contains("AutoDrag", handlerBody);
            Assert.Contains("ImportButton_Click(null, null)", handlerBody);
        }

        [Fact]
        public void SongTrackForm_FERepoButton_IsAlwaysCreated_NotGated()
        {
            // #1815: the FE-Repo-Music button is ALWAYS created (like the graphics
            // FE-Repo button), so it is discoverable even before the music
            // submodule is cloned; its browser then shows the "not found / clone"
            // empty-state. It must NOT be wrapped in an availability gate.
            string src = ReadSource("SongTrackForm.cs");
            Assert.Contains("feRepoMusicButton.Click += FERepoMusicButton_Click", src);
            Assert.DoesNotContain("if (FERepoResourceBrowser.IsMusicRepoAvailable", src);
        }

        [Fact]
        public void SongTrackForm_FERepoButton_GrowsHostPanel_SoItIsNotClipped()
        {
            // #1383 review: the existing action row is full, so the button goes on
            // a new row and the (Dock=Top) host panel must be grown to fit it,
            // otherwise it would be clipped below the panel. (#1815 re-anchored the
            // search from the removed availability gate to the button's Name.)
            string src = ReadSource("SongTrackForm.cs");
            int handler = src.IndexOf("Name = \"FERepoMusicButton\"", StringComparison.Ordinal);
            Assert.True(handler >= 0);
            string block = src.Substring(handler);
            Assert.Contains("row.Height", block);
        }

        [Fact]
        public void SongTrackForm_InstrumentExtCompare_UsesLeadingDot()
        {
            // #1383 review: ext carries the leading dot, so the source-cache skip
            // must compare against ".INSTRUMENT", not the never-matching
            // dot-less "INSTRUMENT".
            string src = ReadSource("SongTrackForm.cs");
            Assert.Contains("ext != \".INSTRUMENT\"", src);
            Assert.DoesNotContain("ext != \"INSTRUMENT\"", src);
        }

        [Fact]
        public void SongExchangeForm_FERepoButton_IsAlwaysCreated_NotGated()
        {
            // #1815: always created (consistent with SongTrackForm + the graphics
            // FE-Repo button), not gated on music-submodule availability.
            string src = ReadSource("SongExchangeForm.cs");
            Assert.Contains("feRepoMusicButton.Click += FERepoMusicButton_Click", src);
            Assert.DoesNotContain("if (FERepoResourceBrowser.IsMusicRepoAvailable", src);
        }

        static string ReadSource(string fileName)
        {
            string root = FindRepoRoot();
            Assert.NotNull(root);
            string path = Path.Combine(root, "FEBuilderGBA", fileName);
            Assert.True(File.Exists(path), $"{fileName} not found at {path}");
            return File.ReadAllText(path);
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
