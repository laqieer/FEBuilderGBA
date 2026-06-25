// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for #1427 — the Avalonia Force Sortie editor (EventForceSortieView)
// must wrap its _vm.Write() in an UndoService scope so the ROM write is undo-tracked
// (Edit > Undo can revert it). Before the fix OnWrite called _vm.Write() with no
// Begin/Commit, so the bare EditorFormRef.WriteFields path recorded nothing into the
// undo buffer.
//
// Two layers:
//  1. Static-analysis guard (DiscoverViewCoveredVmMethods over the live
//     EventForceSortieView.axaml.cs) — this is the piece that FAILS pre-fix and
//     PASSES post-fix, proving the view-level scope exists. Mirrors the canary
//     pattern in UndoCoverageScannerTests.
//  2. Behavioral round-trip (ROM-gated) — load an entry, write inside a scope,
//     assert ROM bytes changed, RunUndo, assert bytes restored byte-identical.
using System;
using System.IO;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class EventForceSortieUndoCoverageTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public EventForceSortieUndoCoverageTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // ---- (1) Static-analysis guard: View wraps _vm.Write() in a scope --------

        /// <summary>
        /// The View must register EventForceSortieViewModel.Write as undo-covered:
        /// OnWrite wraps _vm.Write() in _undoService.Begin / try-Commit / catch-Rollback.
        /// FAILS before the #1427 fix (no Begin/Commit), PASSES after.
        /// </summary>
        [Fact]
        public void EventForceSortieView_WrapsVmWrite_InUndoScope()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null)
            {
                _output.WriteLine("SKIP: repo root not found (running from published binary)");
                return;
            }

            string viewPath = Path.Combine(repoRoot,
                "FEBuilderGBA.Avalonia", "Views", "EventForceSortieView.axaml.cs");
            Assert.True(File.Exists(viewPath), $"View source not found at {viewPath}");

            var covered = UndoCoverageScanner.DiscoverViewCoveredVmMethods(new[] { viewPath });
            Assert.Contains(("EventForceSortieViewModel", "Write"), covered);
        }

        // ---- (2) Behavioral guard: write is undoable byte-for-byte ----------------

        /// <summary>
        /// End-to-end: with an undo scope open around the VM's Write(), a mutated
        /// field is committed to ROM and a subsequent RunUndo restores the original
        /// bytes byte-identical. Confirms the VM/WriteFields path participates in
        /// undo once a scope is active (which the View now opens).
        /// </summary>
        [Fact]
        public void Write_UnderUndoScope_IsRevertedByUndo()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            var vm = new EventForceSortieViewModel();
            var list = vm.LoadList();
            if (list == null || list.Count < 2)
            {
                _output.WriteLine($"SKIP: force-sortie list has {list?.Count ?? 0} entries (need >= 2)");
                return;
            }

            uint addr = list[1].addr;
            vm.LoadEntry(addr);
            Assert.Equal(addr, vm.CurrentAddr);

            const uint size = 4; // W0 (u16) + B2 (u8) + B3 (u8)
            byte[] snapshot = new byte[size];
            Array.Copy(CoreState.ROM.Data, (int)addr, snapshot, 0, (int)size);

            try
            {
                uint original = vm.Unit;
                uint testValue = original == 0x42u ? 0x43u : 0x42u;

                var svc = new UndoService();
                svc.Begin("test-force-sortie");
                vm.Unit = testValue;
                vm.Write();
                svc.Commit();

                // ROM byte at the Unit field (u16 @ +0) must reflect the new value.
                Assert.Equal((ushort)testValue, CoreState.ROM.u16(addr));
                bool anyChanged = false;
                for (int i = 0; i < (int)size; i++)
                {
                    if (snapshot[i] != CoreState.ROM.Data[(int)addr + i]) { anyChanged = true; break; }
                }
                Assert.True(anyChanged, "Write did not change any ROM bytes");

                // Undo must restore every byte.
                CoreState.Undo.RunUndo();
                for (int i = 0; i < (int)size; i++)
                {
                    Assert.Equal(snapshot[i], CoreState.ROM.Data[(int)addr + i]);
                }
            }
            finally
            {
                // Safety-net restore so the shared ROM state is clean for other tests.
                Array.Copy(snapshot, 0, CoreState.ROM.Data, (int)addr, (int)size);
            }
        }

        /// <summary>
        /// Walk up from the test binary's base directory looking for
        /// FEBuilderGBA.sln. Returns null when running outside the source tree.
        /// </summary>
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
    }
}
