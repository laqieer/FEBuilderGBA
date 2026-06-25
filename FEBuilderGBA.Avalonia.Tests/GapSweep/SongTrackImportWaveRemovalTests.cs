// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for #1447: the standalone "Import Wave" main-menu button
// opened a dead no-op `SongTrackImportWaveView` placeholder stub.
//
// Ground truth (WinForms): `SongTrackImportWaveForm` is NOT a standalone menu
// editor — it is a modal sub-dialog opened by `SongTrackForm.ImportMusicFileToSong`
// only when importing a `.wav`. The real WAV->song flow already works in Avalonia
// via `SongTrackView.ImportWaveAsSong` -> `SongTrackWaveImportCore.ImportWaveAsSong`
// (reachable through the "Song Track" main-menu button).
//
// Fix (issue's Option (a)): remove the standalone main-menu exposure (button +
// click handler + GetAllEditorFactories registry entry + NoListEditors entry +
// WinForms ScreenshotFormRegistry pairing) and delete the dead stub view/VM.
// These static-source assertions lock in that removal AND guard that the genuine
// WAV->song path is kept (no functional regression).
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep
{
    public class SongTrackImportWaveRemovalTests
    {
        static string FindRepoRoot()
        {
            string? dir = System.AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return dir!;
        }

        static string Read(params string[] parts)
            => File.ReadAllText(Path.Combine(FindRepoRoot(), Path.Combine(parts)));

        // -----------------------------------------------------------------
        // The standalone dead-end exposure is GONE.
        // -----------------------------------------------------------------

        [Fact]
        public void MainWindow_Axaml_HasNoStandaloneImportWaveButton()
        {
            string axaml = Read("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml");
            Assert.DoesNotContain("Main_ImportWave_Button", axaml);
            Assert.DoesNotContain("OpenSongTrackImportWave_Click", axaml);
        }

        [Fact]
        public void MainWindow_CodeBehind_HasNoImportWaveHandlerOrFactory()
        {
            string cs = Read("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs");
            // No click handler and no named-editor factory for the removed view.
            Assert.DoesNotContain("OpenSongTrackImportWave_Click", cs);
            Assert.DoesNotContain("Open<SongTrackImportWaveView>", cs);
            // The localization line for the removed button is gone too.
            Assert.DoesNotContain("ImportWaveButton", cs);
        }

        [Fact]
        public void DeadStubFiles_AreDeleted()
        {
            string root = FindRepoRoot();
            Assert.False(File.Exists(Path.Combine(root,
                "FEBuilderGBA.Avalonia", "Views", "SongTrackImportWaveView.axaml")));
            Assert.False(File.Exists(Path.Combine(root,
                "FEBuilderGBA.Avalonia", "Views", "SongTrackImportWaveView.axaml.cs")));
            Assert.False(File.Exists(Path.Combine(root,
                "FEBuilderGBA.Avalonia", "ViewModels", "SongTrackImportWaveViewModel.cs")));
        }

        [Fact]
        public void ListParityHelper_NoListEditors_DoesNotReferenceRemovedView()
        {
            string cs = Read("FEBuilderGBA.Avalonia", "Services", "ListParityHelper.cs");
            Assert.DoesNotContain("SongTrackImportWaveView", cs);
        }

        [Fact]
        public void WinFormsScreenshotRegistry_DoesNotPairRemovedView()
        {
            // The WinForms screenshot registry must stay a subset of the Avalonia
            // editor factories (WinFormsScreenshotRegistryTests.Registry_AllNamesExistInAvaloniaRegistry).
            // Since the Avalonia factory entry is gone, the WinForms pairing must be too.
            string cs = Read("FEBuilderGBA", "ScreenshotFormRegistry.cs");
            Assert.DoesNotContain("\"SongTrackImportWaveView\"", cs);
        }

        // -----------------------------------------------------------------
        // The REAL WAV->song flow is kept (no functional regression).
        // -----------------------------------------------------------------

        [Fact]
        public void SongTrackView_StillRoutesWavThroughWaveImportCore()
        {
            string cs = Read("FEBuilderGBA.Avalonia", "Views", "SongTrackView.axaml.cs");
            // The genuine import path remains: SongTrackView.ImportWaveAsSong ->
            // SongTrackWaveImportCore.ImportWaveAsSong.
            Assert.Contains("ImportWaveAsSong", cs);
            Assert.Contains("SongTrackWaveImportCore.ImportWaveAsSong", cs);
        }

        [Fact]
        public void WaveImportCore_SourceStillPresent()
        {
            // The Core seam that backs the real flow must still exist.
            string core = Read("FEBuilderGBA.Core", "SongTrackWaveImportCore.cs");
            Assert.Contains("public static uint ImportWaveAsSong", core);
        }
    }
}
