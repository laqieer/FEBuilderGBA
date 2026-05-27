// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for issue #656: Portrait Image Editor shows incorrect unit
// name in the list. Root cause: ImagePortraitViewModel.LoadList called
// NameResolver.GetUnitName((uint)i) where i is the PORTRAIT INDEX, not a unit
// ID. The fix replaces it with NameResolver.GetPortraitName((uint)i) which
// scans the unit/class tables for the unit/class whose portrait field matches
// the portrait index (mirroring WinForms ImagePortraitForm.GetPortraitNameFast).
//
// These tests are DIFFERENTIAL: they compare what NameResolver.GetUnitName(i)
// (old buggy path) would have produced versus NameResolver.GetPortraitName(i)
// (new fixed path) for a portrait index where the two paths return different
// names. The assertion then pins the label to the NEW value and explicitly
// rejects the OLD value. Running these tests against the pre-fix code
// (`GetUnitName((uint)i)`) MUST fail; running them against the post-fix code
// (`GetPortraitName((uint)i)`) MUST pass. That is what makes them genuine
// regression tests for #656.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ImagePortraitViewModelLoadListTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public ImagePortraitViewModelLoadListTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// Differential regression test for #656.
        ///
        /// On FE8U:
        ///   - Portrait index 1 is owned by Eirika (unit-table row 0).
        ///   - Unit-table row 1 (0-based) is Ephraim.
        /// Therefore:
        ///   - OLD buggy code: GetUnitName(1) returned Ephraim's name → label
        ///     "0x01 Ephraim".
        ///   - NEW fixed code: GetPortraitName(1) reverse-scans for the unit/class
        ///     whose portrait field == 1 → returns Eirika's name → label
        ///     "0x01 Eirika".
        ///
        /// The test scans the FE8U list, finds a portrait index where the two
        /// resolvers disagree (we expect index 1 to satisfy this), and asserts
        /// the label contains the NEW value and does NOT contain the OLD value.
        /// </summary>
        [Fact]
        public void LoadList_FE8U_LabelUsesPortraitOwnerNotZeroBasedUnitTable()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: FE8U.gba unavailable (have {_fixture.Version})");
                return;
            }

            var vm = new ImagePortraitViewModel();
            List<AddrResult> list = vm.LoadList();
            Assert.NotEmpty(list);

            // Find a portrait index where the OLD buggy resolver (GetUnitName(i),
            // i.e. unit-table[i] 0-based) returns something different from the
            // NEW correct resolver (GetPortraitName(i), reverse-scan).
            int differentialIndex = -1;
            string newName = null!;
            string oldName = null!;
            for (int i = 1; i < list.Count && i < 64; i++)
            {
                string n = FEBuilderGBA.NameResolver.GetPortraitName((uint)i);
                string o = FEBuilderGBA.NameResolver.GetUnitName((uint)i);

                // Skip indices where the OLD resolver returned a no-op ("???" / "#i")
                // because the old code already filtered those out before appending —
                // those indices do not produce differential labels.
                if (string.IsNullOrEmpty(n)) continue;
                if (o == "???" || o == $"#{i}") continue;
                if (n == o) continue;

                differentialIndex = i;
                newName = n;
                oldName = o;
                break;
            }

            Assert.True(differentialIndex >= 0,
                "expected at least one portrait index where GetPortraitName and " +
                "GetUnitName disagree on FE8U (e.g. portrait 1 = Eirika vs " +
                "unit-table[1] = Ephraim) — without such an index this test " +
                "cannot distinguish old vs new behavior");

            string label = list[differentialIndex].name;
            _output.WriteLine($"differentialIndex={differentialIndex} label='{label}' " +
                $"newName='{newName}' oldName='{oldName}'");

            Assert.StartsWith($"0x{differentialIndex:X2}", label);

            // The fix's contract: label MUST contain the portrait OWNER's name
            // (GetPortraitName), not the 0-based unit-table row's name (GetUnitName).
            Assert.Contains(newName, label);
            Assert.DoesNotContain(oldName, label);
        }

        /// <summary>
        /// Differential regression test (#656) — sweep all list entries and verify
        /// that for every index where GetPortraitName and GetUnitName disagree,
        /// the rendered label uses the GetPortraitName value (new behavior) and
        /// NEVER the GetUnitName value (old buggy behavior).
        ///
        /// This is the broad version of the targeted test above: it pins the fix
        /// across ALL differential indices, not just one. Running this against
        /// the pre-fix code fails because labels would contain GetUnitName values.
        /// </summary>
        [Fact]
        public void LoadList_AllDifferentialIndices_UsePortraitNameNotUnitName()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            var vm = new ImagePortraitViewModel();
            List<AddrResult> list = vm.LoadList();
            Assert.NotEmpty(list);

            int differentialChecks = 0;
            for (int i = 0; i < list.Count; i++)
            {
                string portraitName = FEBuilderGBA.NameResolver.GetPortraitName((uint)i);
                string unitName = FEBuilderGBA.NameResolver.GetUnitName((uint)i);

                if (string.IsNullOrEmpty(portraitName)) continue;
                if (unitName == "???" || unitName == $"#{i}") continue;
                if (portraitName == unitName) continue;

                // Both resolvers return a usable name AND they differ → this
                // index is differential. The fixed label MUST use portraitName.
                differentialChecks++;
                string label = list[i].name;
                Assert.Contains(portraitName, label);
                Assert.DoesNotContain(unitName, label);
            }

            _output.WriteLine($"differentialChecks={differentialChecks} " +
                $"(ROM={_fixture.Version}, listCount={list.Count})");
            Assert.True(differentialChecks > 0,
                "expected at least one portrait index where GetPortraitName and " +
                "GetUnitName disagree — without any differential indices this " +
                "test would silently pass on both old and new behavior");
        }
    }
}
