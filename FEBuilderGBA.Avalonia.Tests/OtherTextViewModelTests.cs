using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for <see cref="OtherTextViewModel"/> (issue #1163 — port of WinForms
/// <c>OtherTextForm</c>). The entry list comes from the <c>other_text_</c> config; each slot holds a
/// p32 to a null-terminated system-encoded literal string. Writing re-encodes the text, appends it to
/// free space, and repoints the slot.
/// </summary>
[Collection("SharedState")]
public class OtherTextViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public OtherTextViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool Skip()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine("RomFixture not available — skipping.");
            return true;
        }
        return false;
    }

    [Fact]
    public void LoadList_ReturnsEntries()
    {
        if (Skip()) return;

        var vm = new OtherTextViewModel();
        List<AddrResult> list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.Equal(list.Count, vm.GetListCount());
    }

    [Fact]
    public void LoadEntry_ReadsStringAndPointer()
    {
        if (Skip()) return;

        var vm = new OtherTextViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal(CoreState.ROM.p32(addr), vm.StringAddr);
        Assert.NotNull(vm.Text);
    }

    [Fact]
    public void Write_RoundTripsText_ViaAppendAndRepoint()
    {
        if (Skip()) return;

        var vm = new OtherTextViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        uint addr = list[0].addr;
        vm.LoadEntry(addr);
        uint origPointer = CoreState.ROM.p32(addr);

        try
        {
            vm.Text = "Test";
            var undo = CoreState.Undo.NewUndoData("OtherText test");
            Assert.True(vm.Write(undo));

            // The slot must now point at a NEW location holding the edited string.
            Assert.NotEqual(origPointer, CoreState.ROM.p32(addr));

            var vm2 = new OtherTextViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal("Test", vm2.Text);
        }
        finally
        {
            // Restore the original string pointer (the appended copy is left orphaned in the
            // in-memory fixture, which is discarded after the test collection).
            CoreState.ROM.write_p32(addr, origPointer);
        }
    }
}
