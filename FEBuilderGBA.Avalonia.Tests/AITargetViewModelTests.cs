using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="AITargetViewModel"/> (issue #1419).
///
/// The AI Target (AI3) table at <c>p32(RomInfo.ai3_pointer)</c> holds a FIXED count of
/// <b>8</b> profiles, 20 bytes each. WinForms <c>AITargetForm</c> uses the count predicate
/// <c>i &lt; 8</c> and <c>StructExportCore</c> hardcodes 8. The Avalonia editor previously used a
/// 16-entry cap plus an all-zero early-stop heuristic, which could over-list (phantom rows 8-15,
/// out-of-table edits) or under-list (an all-zero middle profile truncating the list). These
/// tests pin the corrected fixed-8 behaviour on both the live VM and the parity reference builder.
/// </summary>
[Collection("SharedState")]
public class AITargetViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AITargetViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool Skip()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine("RomFixture not available — skipping ROM-backed assertions.");
            return true;
        }
        return false;
    }

    [Fact]
    public void ProfileCount_IsFixed8()
    {
        // The authoritative count is a compile-time constant; no ROM required.
        Assert.Equal(8u, AITargetViewModel.ProfileCount);
    }

    [Fact]
    public void LoadList_ReturnsExactly8Profiles()
    {
        if (Skip()) return;

        var vm = new AITargetViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.Equal(8, list.Count);
    }

    [Fact]
    public void LoadList_Entries20BytesApart_Tags0To7()
    {
        if (Skip()) return;

        const uint blockSize = 20;
        var vm = new AITargetViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.Equal(8, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.Equal((uint)i, list[i].tag);
            if (i > 0)
            {
                Assert.Equal(list[i - 1].addr + blockSize, list[i].addr);
            }
        }
        // Last profile begins at base + 7*20 — no phantom rows beyond profile 7.
        Assert.Equal(list[0].addr + 7 * blockSize, list[7].addr);
    }

    [Fact]
    public void LoadList_MatchesGetListCount()
    {
        if (Skip()) return;

        var vm = new AITargetViewModel();
        List<AddrResult> list = vm.LoadList();
        Assert.Equal(list.Count, vm.GetListCount());
    }

    /// <summary>
    /// The parity reference builder (registered for <c>AITargetView</c>) must agree with the VM,
    /// so list-parity verification proves the real registered surface was fixed too — not only the
    /// VM. This exercises the public <see cref="ListParityHelper.BuildReferenceList"/> path.
    /// </summary>
    [Fact]
    public void ParityReferenceList_Matches8AndAgreesWithVm()
    {
        if (Skip()) return;

        var vm = new AITargetViewModel();
        List<AddrResult> vmList = vm.LoadList();
        List<AddrResult> refList = ListParityHelper.BuildReferenceList("AITargetView");

        Assert.Equal(8, refList.Count);
        Assert.Equal(vmList.Count, refList.Count);
        for (int i = 0; i < vmList.Count; i++)
        {
            Assert.Equal(vmList[i].addr, refList[i].addr);
            Assert.Equal(vmList[i].tag, refList[i].tag);
        }
    }

    [Fact]
    public void Write_RoundTripsField()
    {
        if (Skip()) return;

        var vm = new AITargetViewModel();
        List<AddrResult> list = vm.LoadList();
        Assert.Equal(8, list.Count);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        Assert.True(vm.IsLoaded);
        uint orig = vm.LethalDamagePriority;

        try
        {
            vm.LethalDamagePriority = (orig + 1) & 0xFF;
            vm.Write();

            var vm2 = new AITargetViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal((orig + 1) & 0xFF, vm2.LethalDamagePriority);
        }
        finally
        {
            vm.LethalDamagePriority = orig;
            vm.Write();
        }
    }
}
