// SPDX-License-Identifier: GPL-3.0-or-later
// #1605 — wiring tests for the Summon Unit (FE8) editor's new List Expand
// affordance (リストの拡張): an FE8-only table expand that grows the 2-byte
// summon_unit_pointer table and repoints all references + the SPECIFIC
// hardcoded engine sites (SummonUnitExpandCore.ExpandSummonUnitTable).
//
// Three layers of proof:
//   1. Headless Avalonia (AvaloniaFact, no ROM): construct the view and assert
//      the Expand button exists and carries the expected WF label.
//   2. ROM-backed (AvaloniaFact + RomFixture): when an FE8 ROM is present, show
//      the view so LoadList runs, and assert the Expand button is ENABLED and
//      the VM list count matches the Core enumerator. On a non-FE8 ROM the
//      button must be DISABLED. Skips honestly when no ROM is available.
//   3. Source-text scan (Fact): the .axaml carries the button + AutomationId,
//      and the code-behind routes through the Core orchestrator under one undo
//      scope with the FE8-only enabled gate.
using System;
using System.IO;
using global::Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class SummonUnitExpandWiringTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public SummonUnitExpandWiringTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // ---- Layer 1: headless construction (no ROM) -------------------------

        [AvaloniaFact]
        public void SummonUnitView_HasExpandButton_Labelled()
        {
            var view = new SummonUnitViewerView();

            var expand = view.FindControl<Button>("ExpandListButton");
            Assert.NotNull(expand);
            Assert.Equal("リストの拡張", expand!.Content);
        }

        // ---- Layer 2: ROM-backed enabled-state + list parity -----------------

        [AvaloniaFact]
        public void SummonUnitViewer_HasExpandButton_EnabledOnFE8()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("RomFixture not available — FE8U.gba not found in roms/ or ROMS_DIR. Skipping ROM-backed assertions.");
                return;
            }

            var view = new SummonUnitViewerView();
            view.Show(); // fires Opened → LoadList, which sets the FE8 enabled gate

            var expand = view.FindControl<Button>("ExpandListButton");
            Assert.NotNull(expand);

            bool isFe8 = (_fixture.Version == "FE8U" || _fixture.Version == "FE8J");
            if (isFe8)
            {
                Assert.True(expand!.IsEnabled,
                    "ExpandListButton must be ENABLED on an FE8 ROM (#1605).");

                // VM list count must match the Core enumerator (the source of
                // truth the expand grows). This proves the list the editor shows
                // is the same one the expand operates on.
                var vm = new SummonUnitViewerViewModel();
                int vmCount = vm.LoadSummonUnitList().Count;
                uint coreCount = SummonUnitExpandCore.CountSummonUnits(CoreState.ROM);
                Assert.Equal((int)coreCount, vmCount);
                _output.WriteLine($"FE8 ROM ({_fixture.Version}): summon list count = {vmCount} (Core enumerator = {coreCount}).");
            }
            else
            {
                Assert.False(expand!.IsEnabled,
                    $"ExpandListButton must be DISABLED on a non-FE8 ROM ({_fixture.Version}) — summon_unit_pointer == 0 (#1605).");
                _output.WriteLine($"Non-FE8 ROM ({_fixture.Version}): Expand button correctly disabled.");
            }
        }

        // ---- Layer 3: source-text wiring scan (CI-safe, no runtime) ----------

        private static string FindProjectRoot()
        {
            foreach (var start in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                string dir = start;
                for (int i = 0; i < 12; i++)
                {
                    if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                        return dir;
                    string? parent = Path.GetDirectoryName(dir);
                    if (parent == null || parent == dir) break;
                    dir = parent;
                }
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }

        private static string Repo(params string[] parts)
            => Path.Combine(FindProjectRoot(), Path.Combine(parts));

        [Fact]
        public void Axaml_Has_ExpandButton_With_AutomationId()
        {
            string xaml = File.ReadAllText(Repo("FEBuilderGBA.Avalonia", "Views", "SummonUnitViewerView.axaml"));
            Assert.Contains("Name=\"ExpandListButton\"", xaml);
            Assert.Contains("SummonUnitViewer_Expand_Button", xaml);
            Assert.Contains("リストの拡張", xaml);
        }

        [Fact]
        public void CodeBehind_Wires_ExpandHandler_Through_Core_Orchestrator()
        {
            string src = File.ReadAllText(Repo("FEBuilderGBA.Avalonia", "Views", "SummonUnitViewerView.axaml.cs"));
            Assert.Contains("void OnExpandListClick(", src);
            // Routes through the Core orchestrator under one undo scope.
            Assert.Contains("SummonUnitExpandCore.ExpandSummonUnitTable", src);
            Assert.Contains("_undoService.Begin(\"Expand Summon Unit\")", src);
            // FE8-only enabled gate in LoadList.
            Assert.Contains("RomInfo?.version == 8", src);
            Assert.Contains("summon_unit_pointer != 0", src);
        }
    }
}
