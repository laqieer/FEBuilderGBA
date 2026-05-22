// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the Avalonia SupportUnitEditorViewModel + SupportUnitFE6ViewModel
// list-label behavior (#358).  These guard the wrong-address-linking +
// missing-portrait + missing-name bug fix: previously the list used the
// iteration index as the unit ID; now it uses the unit-table owner.
using System;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SupportUnitListLabelTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public SupportUnitListLabelTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // -----------------------------------------------------------------
        // Regression: first row label must use the OWNING unit's ID, not 0
        // (the index-based label was the root cause of the missing-first-
        // portrait + wrong-address bugs in #358).
        // -----------------------------------------------------------------
        [Fact]
        public void FirstRow_LabelStartsWithOwningUnitHexId()
        {
            if (!_fixture.IsAvailable) return;
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;
            // FE6 uses its own VM; skip for that to keep this test version-targeted.
            if (rom.RomInfo.version == 6) return;

            var vm = new SupportUnitEditorViewModel();
            var list = vm.LoadSupportUnitList();
            Assert.NotEmpty(list);

            // Resolve the expected first-row label via the same Core helper
            // the production VM uses.
            uint? expectedOwnerUid = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, list[0].addr);
            Assert.NotNull(expectedOwnerUid);
            // Label starts with hex of (uid+1) — never "0x00 " / "00 ".  This
            // is the regression check: previously the label was "00 ..."
            // because the iteration index was 0.
            uint expectedOneBasedId = expectedOwnerUid!.Value + 1;
            string expectedPrefix = U.ToHexString(expectedOneBasedId);
            _output.WriteLine($"First row addr=0x{list[0].addr:X08}, label='{list[0].name}', expectedPrefix='{expectedPrefix}'");
            Assert.StartsWith(expectedPrefix + " ", list[0].name);
            // tag field also records the 1-based UID for icon loaders.
            Assert.Equal(expectedOneBasedId, list[0].tag);
        }

        // -----------------------------------------------------------------
        // Regression: name part of the label matches NameResolver.GetUnitName
        // (#358 — names were "???" or wrong before).
        // -----------------------------------------------------------------
        [Fact]
        public void FirstRow_NameMatchesNameResolver()
        {
            if (!_fixture.IsAvailable) return;
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.RomInfo.version == 6) return;

            var vm = new SupportUnitEditorViewModel();
            var list = vm.LoadSupportUnitList();
            Assert.NotEmpty(list);

            uint? ownerUid = SupportUnitNavigation.GetUnitIdAtSupportAddr(rom, list[0].addr);
            Assert.NotNull(ownerUid);
            string resolvedName = NameResolver.GetUnitName(ownerUid!.Value + 1) ?? "";
            _output.WriteLine($"resolvedName='{resolvedName}', label='{list[0].name}'");
            Assert.Contains(resolvedName, list[0].name);
        }

        // -----------------------------------------------------------------
        // FindRowForAddr: Unit Editor's "Open Support" button passes the raw
        // 0x08xxxxxx pointer.  The lookup must normalize and land on the
        // correct row.  Mirrors WinForms SupportUnitForm.JumpToAddr.
        // -----------------------------------------------------------------
        [Fact]
        public void FindRowForAddr_RawPointer_FindsRow()
        {
            if (!_fixture.IsAvailable) return;
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.RomInfo.version == 6) return;

            var vm = new SupportUnitEditorViewModel();
            var list = vm.LoadSupportUnitList();
            if (list.Count == 0) return;

            uint fileOffset = list[0].addr;
            uint rawPointer = fileOffset | 0x08000000u;

            int idxFromOffset = vm.FindRowForAddr(list, fileOffset);
            int idxFromRaw = vm.FindRowForAddr(list, rawPointer);
            Assert.Equal(0, idxFromOffset);
            Assert.Equal(idxFromOffset, idxFromRaw);
        }

        // -----------------------------------------------------------------
        // FindRowForAddr returns -1 for an address not in the list.
        // -----------------------------------------------------------------
        [Fact]
        public void FindRowForAddr_UnknownAddress_ReturnsNegativeOne()
        {
            if (!_fixture.IsAvailable) return;
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.RomInfo.version == 6) return;
            var vm = new SupportUnitEditorViewModel();
            var list = vm.LoadSupportUnitList();
            // 0xDEAD is not a valid support-unit row address.
            Assert.Equal(-1, vm.FindRowForAddr(list, 0xDEADu));
        }

        // -----------------------------------------------------------------
        // LoadSupportUnit populates SourceUnitId1Based + SourceUnitName.
        // -----------------------------------------------------------------
        [Fact]
        public void LoadSupportUnit_SetsSourceUnitForOwnedRow()
        {
            if (!_fixture.IsAvailable) return;
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null || rom.RomInfo.version == 6) return;

            var vm = new SupportUnitEditorViewModel();
            var list = vm.LoadSupportUnitList();
            if (list.Count == 0) return;

            vm.LoadSupportUnit(list[0].addr);
            _output.WriteLine($"SourceUnitId1Based={vm.SourceUnitId1Based} Name='{vm.SourceUnitName}'");
            // First row owns some unit; the read-only display must reflect it.
            Assert.NotEqual(0u, vm.SourceUnitId1Based);
            Assert.NotEqual("", vm.SourceUnitName);
        }
    }
}
