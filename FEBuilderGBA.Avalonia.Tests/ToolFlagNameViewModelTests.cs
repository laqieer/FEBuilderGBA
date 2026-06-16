// SPDX-License-Identifier: GPL-3.0-or-later
// #1191 — Flag-Name assignment tool (Avalonia port of WinForms ToolFlagNameForm).
// Pure-logic tests + a RomFixture round-trip (Write a custom flag name, then Delete to
// revert). Write/Delete are in-memory only (Save() persists separately), so these tests
// never touch config/etc on disk.
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class ToolFlagNameEncodingTests
    {
        [Fact]
        public void AddrFlagEncoding_RoundTrips_AndFlagZeroIsSelectable()
        {
            Assert.Equal(1u, ToolFlagNameViewModel.AddrFromFlag(0));   // flag 0 -> non-zero (avoids isNULL)
            for (uint flag = 0; flag < 32; flag++)
                Assert.Equal(flag, ToolFlagNameViewModel.FlagFromAddr(ToolFlagNameViewModel.AddrFromFlag(flag)));
        }

        [Fact]
        public void NullCache_LoadListEmpty_WriteFalse()
        {
            var prev = CoreState.FlagCache;
            try
            {
                CoreState.FlagCache = null;
                var vm = new ToolFlagNameViewModel();
                Assert.Empty(vm.LoadList());
                Assert.False(vm.Write(5, "x"));
                vm.LoadEntry(ToolFlagNameViewModel.AddrFromFlag(5));
                Assert.False(vm.HasSelection);
            }
            finally { CoreState.FlagCache = prev; }
        }
    }

    [Collection("SharedState")]
    public class ToolFlagNameRoundTripTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;
        public ToolFlagNameRoundTripTests(RomFixture fixture, ITestOutputHelper output)
        { _fixture = fixture; _output = output; }

        [Fact]
        public void Write_Then_Delete_RoundTrips_CustomFlagName()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }

            var prevCache = CoreState.FlagCache;
            try
            {
                // Fresh cache: shipped base names (by ROM version) + whatever customizations
                // already exist on disk. We never Save(), so nothing is written back.
                CoreState.FlagCache = new EtcCacheFLag();
                var vm = new ToolFlagNameViewModel();

                var list = vm.LoadList();
                Assert.True(list.Count > 1, "flag list should be populated from FlagCache");
                Assert.All(list, r => Assert.False(r.isNULL()));   // every row selectable (no addr==0)

                const uint flag = 1;   // flag 1 has a shipped base name in every version
                uint addr = ToolFlagNameViewModel.AddrFromFlag(flag);
                // The SHIPPED base name — NOT vm.FlagName, which may already hold an on-disk
                // customization (EtcCacheFLag merges config/etc into Flag). Delete() reverts
                // to this shipped base, so the assertion must compare against it.
                EtcCacheFLag.LoadBaseFlagNames().TryGetValue(flag, out string shippedBase);
                shippedBase ??= "";

                vm.LoadEntry(addr);
                Assert.True(vm.HasSelection);

                // Write a custom name -> cache reflects it + it is now "custom".
                Assert.True(vm.Write(flag, "My Custom Flag"));
                vm.LoadEntry(addr);
                Assert.Equal("My Custom Flag", vm.FlagName);
                Assert.True(vm.IsCustom);

                // Delete -> revert to the SHIPPED base name + no longer custom.
                vm.Delete(flag);
                vm.LoadEntry(addr);
                Assert.Equal(shippedBase, vm.FlagName);
                Assert.False(vm.IsCustom);
            }
            finally { CoreState.FlagCache = prevCache; }
        }
    }
}
