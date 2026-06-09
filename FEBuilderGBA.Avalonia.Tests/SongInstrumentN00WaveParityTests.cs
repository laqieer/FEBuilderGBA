// SPDX-License-Identifier: GPL-3.0-or-later
// #1057 (N00 slice) parity tests for the Song Instrument editor's DirectSound
// wave Export/Import wiring.
//
// The View's N00_Export_Click / N00_Import_Click open an OS file dialog we
// cannot drive headless, so this is a source-text parity test (same pattern as
// the other *ParityTests in this project): it asserts
//   * the axaml ENABLES the N00 Export + Import buttons (Click handlers wired,
//     the old #1057-deferred tooltip dropped)
//   * the code-behind gates on IsDirectSound and routes through the Core seam
//     (SongDirectSoundWavCore.ExportWave / .ImportWave) under the undo scope
//   * NEGATIVE: N03 (Wave Memory) + N08/N10/N18 wave buttons stay disabled in
//     this slice (only DirectSound is wired now).
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class SongInstrumentN00WaveParityTests
    {
        static string? FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return null;
        }

        static string ReadView(string ext)
        {
            string? repoRoot = FindRepoRoot();
            Assert.NotNull(repoRoot);
            string path = Path.Combine(repoRoot!, "FEBuilderGBA.Avalonia", "Views",
                "SongInstrumentView" + ext);
            Assert.True(File.Exists(path), $"Missing {path}");
            return File.ReadAllText(path);
        }

        [Fact]
        public void Axaml_N00_Export_And_Import_Buttons_Enabled_With_Clicks()
        {
            string axaml = ReadView(".axaml");

            // Both N00 wave buttons are present by their AutomationId.
            Assert.Contains("SongInstrument_N00_Export_Button", axaml);
            Assert.Contains("SongInstrument_N00_Import_Button", axaml);

            // Click handlers wired.
            Assert.Contains("Click=\"N00_Export_Click\"", axaml);
            Assert.Contains("Click=\"N00_Import_Click\"", axaml);

            // The #1057-deferred placeholder tooltip must be gone FROM THE N00
            // wave buttons specifically (the other wave tabs — N03/N08/... — keep
            // it; tightening #4). Scope the assertion to each N00 button element.
            AssertButtonTooltipNotDeferred(axaml, "SongInstrument_N00_Export_Button");
            AssertButtonTooltipNotDeferred(axaml, "SongInstrument_N00_Import_Button");
        }

        [Fact]
        public void Axaml_N00_Wave_Buttons_Not_Disabled()
        {
            // Extract the N00 Export/Import button declarations and assert neither
            // carries IsEnabled="False". (The whole axaml still has plenty of
            // IsEnabled="False" on other deferred buttons — see the negative test
            // below — so we must scope this to the two N00 wave buttons.)
            string axaml = ReadView(".axaml");

            AssertButtonEnabled(axaml, "SongInstrument_N00_Export_Button");
            AssertButtonEnabled(axaml, "SongInstrument_N00_Import_Button");
        }

        [Fact]
        public void CodeBehind_Wires_Core_Export_Import_With_Gate_And_Undo()
        {
            string cs = ReadView(".axaml.cs");

            // Handlers exist.
            Assert.Contains("N00_Export_Click", cs);
            Assert.Contains("N00_Import_Click", cs);

            // Route through the Core seam.
            Assert.Contains("SongDirectSoundWavCore.ExportWave", cs);
            Assert.Contains("SongDirectSoundWavCore.ImportWave", cs);

            // Gate on the DirectSound category specifically (NOT IsWaveMemory).
            Assert.Contains("_vm.IsDirectSound", cs);

            // Import runs under the view's undo scope (Begin/Commit/Rollback).
            Assert.Contains("_undoService.Begin(\"Import DirectSound Wave\")", cs);
            Assert.Contains("_undoService.Commit", cs);
            Assert.Contains("_undoService.Rollback", cs);

            // Import passes the P4 wave-pointer slot (voice entry +4) as an OFFSET.
            Assert.Contains("_vm.CurrentAddr + 4", cs);

            // NOT_FOUND is the import fault sentinel.
            Assert.Contains("U.NOT_FOUND", cs);
        }

        // -----------------------------------------------------------------
        // NEGATIVE: in THIS slice only N00 (DirectSound) is wired. N03 (Wave
        // Memory) and the other wave-pointer tabs (N08/N10/N18) stay disabled —
        // no Click handlers and no enabling. Guards against accidental scope
        // creep into the other wave categories (tightening #4).
        // -----------------------------------------------------------------
        [Fact]
        public void Axaml_Other_Wave_Tabs_Have_No_Export_Import_Click_Handlers()
        {
            string axaml = ReadView(".axaml");

            // None of the other wave tabs got Export/Import click handlers wired.
            foreach (string tab in new[] { "N03", "N08", "N10", "N18" })
            {
                Assert.DoesNotContain($"Click=\"{tab}_Export_Click\"", axaml);
                Assert.DoesNotContain($"Click=\"{tab}_Import_Click\"", axaml);
            }
        }

        [Fact]
        public void CodeBehind_Has_No_Other_Wave_Tab_Handlers()
        {
            string cs = ReadView(".axaml.cs");
            foreach (string tab in new[] { "N03", "N08", "N10", "N18" })
            {
                Assert.DoesNotContain($"void {tab}_Export_Click", cs);
                Assert.DoesNotContain($"void {tab}_Import_Click", cs);
            }
        }

        // Assert the Button element carrying the given AutomationId does NOT have
        // IsEnabled="False" within its element span.
        static void AssertButtonEnabled(string axaml, string automationId)
        {
            int idIdx = axaml.IndexOf(automationId, StringComparison.Ordinal);
            Assert.True(idIdx >= 0, $"AutomationId not found: {automationId}");

            // The element starts at the nearest preceding '<' and ends at the
            // next '>' (or "/>"). The Button opening tag may span multiple lines.
            int start = axaml.LastIndexOf('<', idIdx);
            int end = axaml.IndexOf('>', idIdx);
            Assert.True(start >= 0 && end > start, $"Malformed element for {automationId}");
            string element = axaml.Substring(start, end - start + 1);

            Assert.DoesNotContain("IsEnabled=\"False\"", element);
        }

        // Assert the Button element carrying the given AutomationId does NOT carry
        // the #1057-deferred placeholder tooltip within its element span.
        static void AssertButtonTooltipNotDeferred(string axaml, string automationId)
        {
            int idIdx = axaml.IndexOf(automationId, StringComparison.Ordinal);
            Assert.True(idIdx >= 0, $"AutomationId not found: {automationId}");
            int start = axaml.LastIndexOf('<', idIdx);
            int end = axaml.IndexOf('>', idIdx);
            Assert.True(start >= 0 && end > start, $"Malformed element for {automationId}");
            string element = axaml.Substring(start, end - start + 1);
            Assert.DoesNotContain(
                "Wave/instrument export/import is a planned cross-platform enhancement",
                element);
        }
    }
}
