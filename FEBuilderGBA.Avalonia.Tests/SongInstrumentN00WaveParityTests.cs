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

            // Gate on the LOADED ROM byte 0x00 (NOT the mutable category) so a
            // tab switch can't trick the gate (#1057 Copilot plan review pt 1).
            Assert.Contains("_vm.IsLoadedDirectSound", cs);

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
        // NEGATIVE: N00 (0x00) and N08 (0x08) DirectSound are wired (#1057 PR1).
        // N03 (Wave Memory) and the REVERSE DirectSound tabs N10 (0x10) / N18
        // (0x18) stay disabled — no Click handlers and no enabling. Guards against
        // accidental scope creep into the other wave categories.
        // -----------------------------------------------------------------
        [Fact]
        public void Axaml_Other_Wave_Tabs_Have_No_Export_Import_Click_Handlers()
        {
            string axaml = ReadView(".axaml");

            // The still-disabled wave tabs (N03/N10/N18) got NO Export/Import
            // click handlers. N00 + N08 ARE wired now (see N08 positive test).
            foreach (string tab in new[] { "N03", "N10", "N18" })
            {
                Assert.DoesNotContain($"Click=\"{tab}_Export_Click\"", axaml);
                Assert.DoesNotContain($"Click=\"{tab}_Import_Click\"", axaml);
            }
        }

        [Fact]
        public void CodeBehind_Has_No_Other_Wave_Tab_Handlers()
        {
            string cs = ReadView(".axaml.cs");
            // N03/N10/N18 have no per-tab handlers; N00 + N08 DO (shared via the
            // ExportWaveGated / ImportWaveGated helpers).
            foreach (string tab in new[] { "N03", "N10", "N18" })
            {
                Assert.DoesNotContain($"void {tab}_Export_Click", cs);
                Assert.DoesNotContain($"void {tab}_Import_Click", cs);
            }
        }

        // -----------------------------------------------------------------
        // POSITIVE (#1057 PR1): the N08 DirectSound Fixed Freq Export/Import
        // buttons are ENABLED with Click handlers, and the code-behind gates them
        // on the LOADED ROM byte 0x08 (NOT the mutable category) and updates the
        // N08 P4 box on import — proving the N08-only scope + own-slot wiring.
        // -----------------------------------------------------------------
        [Fact]
        public void Axaml_N08_Export_And_Import_Buttons_Enabled_With_Clicks()
        {
            string axaml = ReadView(".axaml");

            Assert.Contains("SongInstrument_N08_Export_Button", axaml);
            Assert.Contains("SongInstrument_N08_Import_Button", axaml);
            Assert.Contains("Click=\"N08_Export_Click\"", axaml);
            Assert.Contains("Click=\"N08_Import_Click\"", axaml);

            AssertButtonEnabled(axaml, "SongInstrument_N08_Export_Button");
            AssertButtonEnabled(axaml, "SongInstrument_N08_Import_Button");
            AssertButtonTooltipNotDeferred(axaml, "SongInstrument_N08_Export_Button");
            AssertButtonTooltipNotDeferred(axaml, "SongInstrument_N08_Import_Button");
        }

        [Fact]
        public void CodeBehind_N08_GatesOnLoadedByte08_And_UpdatesN08P4Box()
        {
            string cs = ReadView(".axaml.cs");

            // Handlers exist.
            Assert.Contains("N08_Export_Click", cs);
            Assert.Contains("N08_Import_Click", cs);

            // Gate on the LOADED ROM byte (type 0x08), NOT the mutable category —
            // a loaded 0x10/0x18 entry switched to the N08 tab in-memory must be
            // rejected (#1057 Copilot plan review pt 1).
            Assert.Contains("IsLoadedDirectSoundFixedFreq", cs);

            // Import updates the N08 P4 box (its OWN slot), not N00_P4_Box.
            Assert.Contains("N08_P4_Box", cs);

            // Shared route through the Core seam + own P4 slot (CurrentAddr + 4).
            Assert.Contains("SongDirectSoundWavCore.ImportWave", cs);
            Assert.Contains("_vm.CurrentAddr + 4", cs);
        }

        // -----------------------------------------------------------------
        // POSITIVE (#1057 PR1): InstExport is enabled + wired to the recursive
        // read-only Core export seam; InstImport stays deferred (disabled).
        // -----------------------------------------------------------------
        [Fact]
        public void Axaml_InstExport_Enabled_InstImport_Disabled()
        {
            string axaml = ReadView(".axaml");

            AssertButtonEnabled(axaml, "SongInstrument_InstExport_Button");
            Assert.Contains("Click=\"InstExport_Click\"", axaml);

            // InstImport stays disabled (deferred to PR 2).
            int impIdx = axaml.IndexOf("SongInstrument_InstImport_Button", StringComparison.Ordinal);
            Assert.True(impIdx >= 0);
            int start = axaml.LastIndexOf('<', impIdx);
            int end = axaml.IndexOf('>', impIdx);
            string impEl = axaml.Substring(start, end - start + 1);
            Assert.Contains("IsEnabled=\"False\"", impEl);
            Assert.DoesNotContain("Click=", impEl);
        }

        [Fact]
        public void CodeBehind_InstExport_RoutesThroughCoreSeam_ReadOnly()
        {
            string cs = ReadView(".axaml.cs");
            Assert.Contains("InstExport_Click", cs);
            // Routes through the Core recursive export seam.
            Assert.Contains("SongInstrumentSetCore.ExportAll", cs);
            // Read-only: NO undo scope is opened in the InstExport handler.
            int handlerIdx = cs.IndexOf("void InstExport_Click", StringComparison.Ordinal);
            Assert.True(handlerIdx >= 0);
            // Scope the no-undo assertion to this handler body (up to the next
            // method declaration).
            int next = cs.IndexOf("\n        async void ", handlerIdx + 10, StringComparison.Ordinal);
            if (next < 0) next = cs.IndexOf("\n        void ", handlerIdx + 10, StringComparison.Ordinal);
            string body = next > handlerIdx ? cs.Substring(handlerIdx, next - handlerIdx) : cs.Substring(handlerIdx);
            Assert.DoesNotContain("_undoService.Begin", body);
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
