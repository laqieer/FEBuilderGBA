// SPDX-License-Identifier: GPL-3.0-or-later
// #1057 (N00/N08) + #1001 PR1 (N10/N18) parity tests for the Song Instrument
// editor's DirectSound wave Export/Import wiring.
//
// The View's Nxx_Export_Click / Nxx_Import_Click open an OS file dialog we
// cannot drive headless, so this is a source-text parity test (same pattern as
// the other *ParityTests in this project): it asserts
//   * the axaml ENABLES the N00/N08/N10/N18 Export + Import buttons (Click
//     handlers wired, the old #1057-deferred tooltip dropped)
//   * the code-behind gates each on its own LOADED ROM byte
//     (0x00/0x08/0x10/0x18) and routes through the Core seam
//     (SongDirectSoundWavCore.ExportWave / .ImportWave) under the undo scope,
//     updating each tab's OWN P4 box
//   * NEGATIVE: N03 (Wave Memory) stays disabled (only the 4 DirectSound tabs
//     are wired).
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

        static string ReadViewModel()
        {
            string? repoRoot = FindRepoRoot();
            Assert.NotNull(repoRoot);
            string path = Path.Combine(repoRoot!, "FEBuilderGBA.Avalonia", "ViewModels",
                "SongInstrumentViewModel.cs");
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
            // #1448: import now opens the conversion dialog (sox/DPCM/SNR) and
            // appends the ready sample via ImportSampleBytes (NOT WavToByte, which
            // would corrupt a DPCM sample). The dialog is launched modally and a
            // null result (Cancel) is a strict no-op.
            Assert.Contains("SongDirectSoundWavCore.ImportSampleBytes", cs);
            Assert.Contains("SongInstrumentImportWaveView", cs);
            Assert.Contains("OpenModal<SongInstrumentImportWaveView, byte[]?>", cs);

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
        // NEGATIVE: the 4 DirectSound tabs N00 (0x00) / N08 (0x08) / N10 (0x10) /
        // N18 (0x18) are ALL wired (#1057 + #1001 PR1). N03 (Wave Memory) is NOT
        // DirectSound and stays disabled — no Click handlers and no enabling.
        // Guards against accidental scope creep into the non-DirectSound wave tab.
        // -----------------------------------------------------------------
        [Fact]
        public void Axaml_NonDirectSound_Wave_Tab_Has_No_Export_Import_Click_Handlers()
        {
            string axaml = ReadView(".axaml");

            // N03 (Wave Memory) got NO Export/Import click handlers — only the 4
            // DirectSound tabs are wired.
            Assert.DoesNotContain("Click=\"N03_Export_Click\"", axaml);
            Assert.DoesNotContain("Click=\"N03_Import_Click\"", axaml);
        }

        [Fact]
        public void CodeBehind_Has_No_NonDirectSound_Wave_Tab_Handlers()
        {
            string cs = ReadView(".axaml.cs");
            // N03 has no per-tab handlers; the 4 DirectSound tabs DO (shared via
            // the ExportWaveGated / ImportWaveGated helpers).
            Assert.DoesNotContain("void N03_Export_Click", cs);
            Assert.DoesNotContain("void N03_Import_Click", cs);
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
            Assert.Contains("SongDirectSoundWavCore.ImportSampleBytes", cs);
            Assert.Contains("_vm.CurrentAddr + 4", cs);
        }

        // -----------------------------------------------------------------
        // POSITIVE (#1001 PR1): the N10 (DirectSound Reverse, 0x10) and N18
        // (DirectSound Fixed Freq Reverse, 0x18) Export/Import buttons are ENABLED
        // with Click handlers; the code-behind gates each on its OWN loaded ROM
        // byte and updates its OWN P4 box. Same DirectSound sample layout as
        // N00/N08, so they reuse SongDirectSoundWavCore verbatim.
        // -----------------------------------------------------------------
        [Theory]
        [InlineData("N10")]
        [InlineData("N18")]
        public void Axaml_Reverse_Export_And_Import_Buttons_Enabled_With_Clicks(string tab)
        {
            string axaml = ReadView(".axaml");

            Assert.Contains($"SongInstrument_{tab}_Export_Button", axaml);
            Assert.Contains($"SongInstrument_{tab}_Import_Button", axaml);
            Assert.Contains($"Click=\"{tab}_Export_Click\"", axaml);
            Assert.Contains($"Click=\"{tab}_Import_Click\"", axaml);

            AssertButtonEnabled(axaml, $"SongInstrument_{tab}_Export_Button");
            AssertButtonEnabled(axaml, $"SongInstrument_{tab}_Import_Button");
            AssertButtonTooltipNotDeferred(axaml, $"SongInstrument_{tab}_Export_Button");
            AssertButtonTooltipNotDeferred(axaml, $"SongInstrument_{tab}_Import_Button");
        }

        [Fact]
        public void CodeBehind_N10_N18_GateOnLoadedReverseBytes_And_UpdateOwnP4Box()
        {
            string cs = ReadView(".axaml.cs");

            // Handlers exist for both reverse tabs.
            Assert.Contains("N10_Export_Click", cs);
            Assert.Contains("N10_Import_Click", cs);
            Assert.Contains("N18_Export_Click", cs);
            Assert.Contains("N18_Import_Click", cs);

            // Each gates on its OWN loaded ROM byte (0x10 / 0x18), NOT the mutable
            // category, so a tab switch can't trick the gate (#1057 Copilot pt 1).
            Assert.Contains("IsLoadedDirectSoundReverse", cs);
            Assert.Contains("IsLoadedDirectSoundFixedFreqReverse", cs);

            // Each import updates its OWN tab's P4 box (the success-display slot).
            Assert.Contains("N10_P4_Box", cs);
            Assert.Contains("N18_P4_Box", cs);

            // Reuse the shared Core seam + own P4 slot (CurrentAddr + 4).
            Assert.Contains("SongDirectSoundWavCore.ImportSampleBytes", cs);
            Assert.Contains("_vm.CurrentAddr + 4", cs);
        }

        [Fact]
        public void ViewModel_HasLoadedReverseGateFlags_RaisedFromLoadedHeaderByte()
        {
            string vm = ReadViewModel();
            // The two new loaded-byte gate flags exist (0x10 / 0x18).
            Assert.Contains("IsLoadedDirectSoundReverse", vm);
            Assert.Contains("IsLoadedDirectSoundFixedFreqReverse", vm);
            Assert.Contains("_loadedHeaderByte == 0x10", vm);
            Assert.Contains("_loadedHeaderByte == 0x18", vm);
            // They are raised when LoadedHeaderByte changes (PropertyChanged).
            Assert.Contains("OnPropertyChanged(nameof(IsLoadedDirectSoundReverse))", vm);
            Assert.Contains("OnPropertyChanged(nameof(IsLoadedDirectSoundFixedFreqReverse))", vm);
        }

        // -----------------------------------------------------------------
        // POSITIVE (#1057): InstExport (PR1, read-only) AND InstImport (PR2,
        // ROM-mutating) are both enabled + wired to the recursive Core seam.
        // -----------------------------------------------------------------
        [Fact]
        public void Axaml_InstExport_And_InstImport_Enabled()
        {
            string axaml = ReadView(".axaml");

            AssertButtonEnabled(axaml, "SongInstrument_InstExport_Button");
            Assert.Contains("Click=\"InstExport_Click\"", axaml);

            // InstImport is now ENABLED + wired (PR2): not disabled, has a Click.
            int impIdx = axaml.IndexOf("SongInstrument_InstImport_Button", StringComparison.Ordinal);
            Assert.True(impIdx >= 0);
            int start = axaml.LastIndexOf('<', impIdx);
            int end = axaml.IndexOf('>', impIdx);
            string impEl = axaml.Substring(start, end - start + 1);
            Assert.DoesNotContain("IsEnabled=\"False\"", impEl);
            Assert.Contains("Click=\"InstImport_Click\"", impEl);
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

        // -----------------------------------------------------------------
        // POSITIVE (#1057 PR2): InstImport routes through the recursive ROM-mutating
        // Core import seam under a SINGLE undo scope (Begin/Commit/Rollback), via the
        // VM's ImportLoadedVoicegroup helper.
        // -----------------------------------------------------------------
        [Fact]
        public void CodeBehind_InstImport_RoutesThroughCoreSeam_UnderUndoScope()
        {
            string cs = ReadView(".axaml.cs");
            Assert.Contains("InstImport_Click", cs);
            // The View delegates to the VM helper which calls the Core import seam.
            Assert.Contains("ImportLoadedVoicegroup", cs);

            int handlerIdx = cs.IndexOf("void InstImport_Click", StringComparison.Ordinal);
            Assert.True(handlerIdx >= 0);
            int next = cs.IndexOf("\n        static string GetActiveTabPrefix", handlerIdx + 10, StringComparison.Ordinal);
            string body = next > handlerIdx ? cs.Substring(handlerIdx, next - handlerIdx) : cs.Substring(handlerIdx);
            // ROM-mutating: opens an undo scope and commits / rolls back.
            Assert.Contains("_undoService.Begin", body);
            Assert.Contains("_undoService.Commit", body);
            Assert.Contains("_undoService.Rollback", body);
        }

        // -----------------------------------------------------------------
        // PR2 path-traversal guard (Copilot review): ResolveInside accepts a bare
        // relative token but REJECTS an absolute path or a ".."-escaping token so a
        // hand-edited TSV can't read a file outside the chosen import directory.
        // -----------------------------------------------------------------
        // NOTE: forward-slash "/" is a path separator on EVERY OS, so "../escape.bin"
        // escapes (and is rejected) everywhere. Backslash "\" is a separator ONLY on
        // Windows — on Linux/macOS "..\escape.bin" is a single literal filename that
        // stays inside the directory, so it is correctly NOT rejected there. The
        // backslash case is covered by the Windows-only test below.
        [Theory]
        [InlineData("voicegroup0x00.Wave.bin", true)]   // bare relative -> accepted
        [InlineData("sub/child.bin", true)]             // relative subdir -> accepted
        [InlineData("../escape.bin", false)]            // parent escape -> rejected (all OS)
        [InlineData("sub/../../escape.bin", false)]     // escapes via .. -> rejected (all OS)
        public void ResolveInside_AcceptsRelative_RejectsEscape(string token, bool shouldResolve)
        {
            string dir = Path.Combine(Path.GetTempPath(), "fe_inst_test");
            string? resolved = FEBuilderGBA.Avalonia.Views.SongInstrumentView.ResolveInside(dir, token);
            if (shouldResolve)
            {
                Assert.NotNull(resolved);
                // The resolved path stays inside the chosen directory.
                Assert.StartsWith(Path.GetFullPath(dir), resolved!, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Null(resolved);
            }
        }

        [Fact]
        public void ResolveInside_RejectsBackslashEscape_Windows()
        {
            // Backslash is a path separator only on Windows; skip elsewhere (the token
            // is a valid in-directory filename on Linux/macOS).
            if (!OperatingSystem.IsWindows()) return;
            string dir = Path.Combine(Path.GetTempPath(), "fe_inst_test");
            Assert.Null(FEBuilderGBA.Avalonia.Views.SongInstrumentView.ResolveInside(dir, "..\\escape.bin"));
        }

        [Fact]
        public void ResolveInside_RejectsAbsolutePath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "fe_inst_test");
            // An absolute/rooted token is rejected regardless of OS form.
            string abs = OperatingSystem.IsWindows() ? @"C:\Windows\system.ini" : "/etc/passwd";
            Assert.Null(FEBuilderGBA.Avalonia.Views.SongInstrumentView.ResolveInside(dir, abs));
        }

        [Fact]
        public void ViewModel_ImportLoadedVoicegroup_RoutesThroughCoreImportSeam()
        {
            string vm = ReadViewModel();
            Assert.Contains("ImportLoadedVoicegroup", vm);
            // The VM helper calls the recursive Core import seam.
            Assert.Contains("SongInstrumentSetCore.ImportAll", vm);
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
