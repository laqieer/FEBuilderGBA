// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for issue #656 (round 2): Portrait Import Wizard left
// list shows incorrect unit names. Root cause: ImagePortraitImporterViewModel
// .LoadList called NameResolver.GetUnitName((uint)i) where i is the PORTRAIT
// INDEX, not a unit-table row. The fix replaces it with
// NameResolver.GetPortraitName((uint)i) which scans the unit/class tables
// for an entry whose portrait field matches i (mirroring the editor fix
// from #654/#673 and WinForms ImagePortraitForm.GetPortraitNameFast).
//
// These tests are DIFFERENTIAL: they compare what NameResolver.GetUnitName(i)
// (old buggy path) would have produced versus NameResolver.GetPortraitName(i)
// (new fixed path) for indices where the two resolvers disagree. The
// assertions pin the wizard label to the NEW value and explicitly reject the
// OLD value. Running these tests against the pre-fix code MUST fail; running
// against the post-fix code MUST pass.
//
// Mirrors ImagePortraitViewModelLoadListTests for parity, plus a parity test
// that pins importer labels to the main editor labels so the two list views
// cannot drift again.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ImagePortraitImporterViewModelLoadListTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public ImagePortraitImporterViewModelLoadListTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// Differential regression test for #656 (round 2).
        ///
        /// On FE8U (verified empirically from the wizard's rendered list):
        ///   - Portrait index 1 is owned by Queen (class-table entry whose
        ///     portrait field == 1).
        ///   - Unit-table row 1 (0-based) is Seth.
        /// Therefore:
        ///   - OLD buggy wizard: GetUnitName(1) returned Seth's name → label
        ///     "0x01 Seth".
        ///   - NEW fixed wizard: GetPortraitName(1) reverse-scans for the unit/
        ///     class whose portrait field == 1 → returns Queen's name → label
        ///     "0x01 Queen".
        ///
        /// The test scans the FE8U list, finds a portrait index where the two
        /// resolvers disagree (the differential index is discovered dynamically
        /// rather than hardcoded), and asserts the wizard label contains the
        /// NEW value and does NOT contain the OLD value.
        /// </summary>
        [Fact]
        public void LoadList_FE8U_LabelUsesPortraitOwnerNotZeroBasedUnitTable()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: FE8U.gba unavailable (have {_fixture.Version})");
                return;
            }

            var vm = new ImagePortraitImporterViewModel();
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
                "GetUnitName disagree on FE8U (e.g. portrait 1 = Queen vs " +
                "unit-table[1] = Seth) — without such an index this test " +
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
        /// Differential regression test (#656 round 2) — sweep all list entries
        /// and verify that for every index where GetPortraitName and GetUnitName
        /// disagree, the rendered wizard label uses the GetPortraitName value
        /// (new behavior) and NEVER the GetUnitName value (old buggy behavior).
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

            var vm = new ImagePortraitImporterViewModel();
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

        /// <summary>
        /// Parity regression test (#656 round 2) — the wizard's left list must
        /// show the SAME labels as the main Portrait Image Editor's left list
        /// for every entry. Both view-models enumerate the same portrait_pointer
        /// table with the same datasize, so any drift between them (like the
        /// pre-fix wizard calling GetUnitName instead of GetPortraitName) means
        /// users see different names in two views of the same data.
        ///
        /// Pins the importer to the editor permanently — if a future change
        /// regresses one but not the other, this test catches it immediately.
        /// </summary>
        [Fact]
        public void LoadList_MatchesMainEditor()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            var importerVm = new ImagePortraitImporterViewModel();
            var editorVm = new ImagePortraitViewModel();

            List<AddrResult> importerList = importerVm.LoadList();
            List<AddrResult> editorList = editorVm.LoadList();

            Assert.NotEmpty(importerList);
            Assert.NotEmpty(editorList);
            Assert.Equal(editorList.Count, importerList.Count);

            for (int i = 0; i < importerList.Count; i++)
            {
                Assert.Equal(editorList[i].addr, importerList[i].addr);
                Assert.Equal(editorList[i].name, importerList[i].name);
            }
        }
    }
}
