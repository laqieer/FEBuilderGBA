using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="MantAnimationViewModel"/> (issue
/// #1178 — port of WinForms <c>MantAnimationForm</c>). The table at
/// <c>p32(RomInfo.mant_command_pointer)</c> holds 4-byte pointers (field
/// <c>D0</c> at +0), one per cape/mant flutter animation. List ids start at
/// <c>RomInfo.mant_command_startadd</c>. The list size is count-driven via a
/// separate u8 at <c>mant_command_count_address</c> (stores <c>count - 1</c>).
/// </summary>
[Collection("SharedState")]
public class MantAnimationViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MantAnimationViewModelTests(RomFixture fixture, ITestOutputHelper output)
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
    public void LoadList_ReturnsEntries_FourBytesApart_BareHexLabel()
    {
        if (Skip()) return;

        var vm = new MantAnimationViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);

        uint startAdd = CoreState.ROM.RomInfo.mant_command_startadd;
        // First list id == startadd; label is the bare hex of that id (NOT
        // "0x..") so U.atoh round-trips for the icon loader + Jump quirk.
        Assert.Equal(startAdd, list[0].tag);
        Assert.Equal(U.ToHexString(startAdd), list[0].name);
        Assert.Equal(startAdd, U.atoh(list[0].name));

        for (int i = 1; i < list.Count; i++)
            Assert.Equal(list[i - 1].addr + 4u, list[i].addr);

        Assert.Equal((uint)list.Count, vm.ReadCount);
    }

    [Fact]
    public void LoadEntry_ReadsD0_IntoP0()
    {
        if (Skip()) return;

        var vm = new MantAnimationViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal(CoreState.ROM.u32(addr), vm.P0);
    }

    [Fact]
    public void Write_RoundTripsD0Pointer()
    {
        if (Skip()) return;

        var vm = new MantAnimationViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        uint orig = vm.P0;

        var undo = CoreState.Undo.NewUndoData("test-mant-write");
        try
        {
            vm.P0 = 0x08123456;
            vm.Write(undo);

            var vm2 = new MantAnimationViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x08123456u, vm2.P0);
        }
        finally
        {
            // Restore the original pointer so the shared fixture ROM is clean.
            var restore = CoreState.Undo.NewUndoData("test-mant-restore");
            vm.P0 = orig;
            vm.Write(restore);
        }
    }

    [Fact]
    public void GetJumpBattleAnimeId_IsLabelMinusOne()
    {
        if (Skip()) return;

        var vm = new MantAnimationViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        vm.LoadEntry(list[0].addr);

        // WF quirk: jump id == U.atoh(label) - 1.
        uint expected = U.atoh(list[0].name) - 1;
        Assert.Equal(expected, vm.GetJumpBattleAnimeId());
    }

    [Fact]
    public void LoadList_NoRom_ReturnsEmpty()
    {
        ROM prev = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new MantAnimationViewModel();
            Assert.Empty(vm.LoadList());
            Assert.Equal(0u, vm.GetJumpBattleAnimeId());
        }
        finally
        {
            CoreState.ROM = prev;
        }
    }

    [Fact]
    public void ExpandList_RejectsSmallerCount()
    {
        if (Skip()) return;

        var vm = new MantAnimationViewModel();
        vm.LoadList();
        Assert.NotEqual(0u, vm.ReadCount);

        string err = vm.ExpandList(vm.ReadCount - 1, null);
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void ExpandList_WritesCountMinusOne_ToCountAddress()
    {
        if (Skip()) return;

        // Mutate an ISOLATED clone of the fixture ROM so the shared fixture is
        // never corrupted. ExpandList relocates the pointer table AND rewrites
        // the count u8 (stores newCount - 1).
        var rom = new ROM();
        bool ok = rom.Load(_fixture.RomPath!, out string _);
        Assert.True(ok);

        ROM prevRom = CoreState.ROM;
        IEtcCache prevComment = CoreState.CommentCache;
        IEtcCache prevLint = CoreState.LintCache;
        try
        {
            CoreState.ROM = rom;
            CoreState.CommentCache = new HeadlessEtcCache();
            CoreState.LintCache = new HeadlessEtcCache();

            uint countAddr = rom.RomInfo.mant_command_count_address;
            Assert.NotEqual(0u, countAddr);

            var vm = new MantAnimationViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            uint oldCount = vm.ReadCount;
            uint oldBase = vm.ReadStartAddress;
            uint newCount = oldCount + 2;

            string err = vm.ExpandList(newCount, null);
            Assert.True(string.IsNullOrEmpty(err), err);

            // Count u8 holds newCount - 1.
            Assert.Equal((byte)(newCount - 1), rom.u8(countAddr));
            // Table relocated and read-config updated.
            Assert.Equal(newCount, vm.ReadCount);
            Assert.NotEqual(oldBase, vm.ReadStartAddress);
        }
        finally
        {
            CoreState.ROM = prevRom;
            CoreState.CommentCache = prevComment;
            CoreState.LintCache = prevLint;
        }
    }
}
