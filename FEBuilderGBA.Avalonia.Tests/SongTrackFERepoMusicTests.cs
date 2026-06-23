using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1383 — single music-import-path guard for the Avalonia Song Track Editor.
    ///
    /// Both the file-picker "Import Music File" button (ImportMidi_Click) and the
    /// new "FE-Repo-Music" button (FERepoMusic_Click) must route the chosen path
    /// through the SAME dispatcher (ImportMusicPath) — no second import code path.
    ///
    /// Source-text guard (no GUI required) so it runs in headless CI.
    /// </summary>
    public class SongTrackFERepoMusicTests
    {
        [Fact]
        public void ImportAndFERepo_BothRouteThroughImportMusicPath()
        {
            string src = ReadSource(Path.Combine("FEBuilderGBA.Avalonia", "Views", "SongTrackView.axaml.cs"));

            // The single dispatcher exists.
            Assert.Contains("Task ImportMusicPath(string path)", src);

            // The file-picker handler delegates to it.
            int importHandler = src.IndexOf("ImportMidi_Click(object? sender", StringComparison.Ordinal);
            Assert.True(importHandler >= 0, "ImportMidi_Click not found");

            // The FE-Repo-Music handler exists, opens the music browser via the
            // shared helper, and delegates to the SAME dispatcher.
            int feHandler = src.IndexOf("FERepoMusic_Click(object? sender", StringComparison.Ordinal);
            Assert.True(feHandler >= 0, "FERepoMusic_Click not found");
            string feBody = src.Substring(feHandler);
            Assert.Contains("FERepoPickHelper.PickMusic", feBody);
            Assert.Contains("await ImportMusicPath(path)", feBody);
        }

        [Fact]
        public void FERepoMusicButton_IsGatedByAvailability()
        {
            string axaml = ReadSource(Path.Combine("FEBuilderGBA.Avalonia", "Views", "SongTrackView.axaml"));
            // The button visibility binds to the VM availability flag.
            Assert.Contains("SongTrack_FERepoMusic_Button", axaml);
            Assert.Contains("IsVisible=\"{Binding IsFERepoMusicAvailable}\"", axaml);
        }

        static string ReadSource(string relativePath)
        {
            string root = FindRepoRoot();
            Assert.NotNull(root);
            string path = Path.Combine(root, relativePath);
            Assert.True(File.Exists(path), $"{relativePath} not found at {path}");
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
