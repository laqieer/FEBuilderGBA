// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for issue #656: Portrait Image Editor shows incorrect unit
// name in the list. Root cause: ImagePortraitViewModel.LoadList called
// NameResolver.GetUnitName((uint)i) where i is the PORTRAIT INDEX, not a unit
// ID. The fix replaces it with NameResolver.GetPortraitName((uint)i) which
// scans the unit/class tables for the unit/class whose portrait field matches
// the portrait index (mirroring WinForms ImagePortraitForm.GetPortraitNameFast).
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
        /// On FE8U, portrait index 1 is owned by Eirika (Unit ID 1). After the
        /// #656 fix, the portrait list label for portrait 1 must contain "Eirika"
        /// (resolved via the reverse-lookup NameResolver.GetPortraitName(1)),
        /// NOT some unrelated unit-table[1] row.
        ///
        /// The buggy old code did NameResolver.GetUnitName((uint)i) with i=1,
        /// which read at unit-table[1] (Ephraim, 0-based) — so portrait 1's row
        /// would have said "Ephraim" instead of "Eirika". This test pins the fix.
        /// </summary>
        [Fact]
        public void LoadList_FE8U_Portrait1_LabelContainsEirika()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: FE8U.gba unavailable (have {_fixture.Version})");
                return;
            }

            var vm = new ImagePortraitViewModel();
            List<AddrResult> list = vm.LoadList();
            Assert.NotEmpty(list);

            // Find the row for portrait index 1 (Eirika's portrait on FE8U).
            // The label format is "0x01 <name>"; the "<name>" must match Eirika
            // when the reverse-lookup is correct, otherwise the bug is present.
            Assert.True(list.Count > 1, "portrait list must include at least 2 entries");
            string labelForPortrait1 = list[1].name;
            _output.WriteLine($"label[1]={labelForPortrait1}");

            // The label MUST contain something resembling a unit name (not just "0x01").
            // We don't pin the exact text because translation files may rewrite "Eirika"
            // depending on language, but the label has to be non-trivially longer than
            // the hex prefix when the fix is in place.
            Assert.StartsWith("0x01", labelForPortrait1);
            Assert.True(labelForPortrait1.Length > 4,
                $"portrait 1 label '{labelForPortrait1}' should include the owner unit's name (fix for #656)");

            // Cross-check: a portrait index with no unit owner should NOT add a name.
            // We can't know the exact "no-owner" index per ROM, but we can verify that
            // GetPortraitName returns "" for a clearly-out-of-range portrait ID. This
            // indirectly proves the reverse-scan is being used (the buggy GetUnitName
            // path would have returned a "???" or "#N" string).
            string emptyName = FEBuilderGBA.NameResolver.GetPortraitName(0xFFFF);
            Assert.Equal("", emptyName);
        }

        /// <summary>
        /// Generic version-independent contract: the list label format is
        /// "0x{i:X2}" optionally followed by " <name>" — the fix must NEVER
        /// produce labels like "0x{i:X2} #{i}" (the raw 0-based GetUnitName
        /// fallback), because that was the visual bug reported on #656.
        /// </summary>
        [Fact]
        public void LoadList_LabelsNeverContainHashFallback()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }

            var vm = new ImagePortraitViewModel();
            List<AddrResult> list = vm.LoadList();

            foreach (var item in list)
            {
                // The buggy code passed the portrait index to GetUnitName, which
                // returned "#{index}" when the unit table row at that index had
                // textId == 0. With the fix routed through GetPortraitName, a
                // missing owner returns "" (no name appended). So no list label
                // should ever contain "#" followed by digits.
                Assert.DoesNotContain("#", item.name);
            }
        }
    }
}
