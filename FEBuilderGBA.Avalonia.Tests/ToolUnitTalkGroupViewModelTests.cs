// SPDX-License-Identifier: GPL-3.0-or-later
// #1197 — Unit Talk Group tool (Avalonia port of WinForms ToolUnitTalkGroupForm).
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ToolUnitTalkGroupEncodingTests
    {
        [Fact]
        public void AddrGroupEncoding_RoundTrips_AndGroupZeroSelectable()
        {
            Assert.Equal(1u, ToolUnitTalkGroupViewModel.AddrFromGroup(0));   // group 0 -> non-zero addr
            for (uint g = 0; g <= ToolUnitTalkGroupViewModel.MaxTalkGroup; g++)
                Assert.Equal(g, ToolUnitTalkGroupViewModel.GroupFromAddr(ToolUnitTalkGroupViewModel.AddrFromGroup(g)));
        }

        [Fact]
        public void NoRom_NotSupported_EmptyList()
        {
            var prev = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new ToolUnitTalkGroupViewModel();
                Assert.False(vm.SupportsTalkGroup);
                Assert.Empty(vm.LoadList());
                Assert.Empty(vm.UnitsInGroup(0));
            }
            finally { CoreState.ROM = prev; }
        }
    }

    [Collection("SharedState")]
    public class ToolUnitTalkGroupRomTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;
        public ToolUnitTalkGroupRomTests(RomFixture fixture, ITestOutputHelper output)
        { _fixture = fixture; _output = output; }

        [Fact]
        public void LoadList_OnFE7or8_ListsAllGroups_WithUnitsInGroupZero()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }
            var vm = new ToolUnitTalkGroupViewModel();
            if (!vm.SupportsTalkGroup) { _output.WriteLine("SKIP: FE6 has no talk groups"); return; }

            var list = vm.LoadList();
            Assert.Equal((int)ToolUnitTalkGroupViewModel.MaxTalkGroup + 1, list.Count);   // 0..0xD
            Assert.All(list, r => Assert.False(r.isNULL()));                              // every row selectable
            // Most player units default to talk group 0, so it must have members.
            Assert.NotEmpty(vm.UnitsInGroup(0));
        }
    }
}
