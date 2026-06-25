using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Tests for the Text Character Code editor Write button + persistence (issue #1446).
///
/// The Avalonia editor exposed editable Char Code (u16@0) and Terminator (u16@2)
/// NumericUpDowns but had no Write button, so <see cref="TextCharCodeViewModel.Write"/>
/// was never invoked and edits silently never persisted. WinForms <c>TextCharCodeForm</c>
/// persists via an InputFormRef write button + PostWriteHandler. These tests verify the
/// added Write button exists and that clicking it (and calling Write directly) round-trips
/// the two u16 values to the ROM.
///
/// Ref #404: serialize against `SharedState` because <see cref="TextCharCodeView"/>
/// reads `rom.getString` via `CoreState.SystemTextEncoder` during construction, which
/// races with other test classes holding a live `CoreState.ROM`.
/// </summary>
[Collection("SharedState")]
public class TextCharCodeWriteTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TextCharCodeWriteTests(RomFixture fixture, ITestOutputHelper output)
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
    public void Write_RoundTripsCharCodeAndTerminator()
    {
        if (Skip()) return;

        var vm = new TextCharCodeViewModel();
        vm.Initialize();
        Assert.True(vm.GetListCount() > 0, "expected at least one char-code entry");

        vm.SelectIndex(0);
        uint addr = vm.CurrentAddr;
        Assert.NotEqual(0u, addr);

        uint origCode = CoreState.ROM.u16(addr + 0);
        uint origTerm = CoreState.ROM.u16(addr + 2);

        try
        {
            vm.CharCode = 0x1234;
            vm.TerminatorValue = 0xABCD;
            vm.Write();

            // Re-read from a fresh VM at the same address.
            var vm2 = new TextCharCodeViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x1234u, vm2.CharCode);
            Assert.Equal(0xABCDu, vm2.TerminatorValue);

            // And confirm the raw ROM bytes match.
            Assert.Equal(0x1234u, CoreState.ROM.u16(addr + 0));
            Assert.Equal(0xABCDu, CoreState.ROM.u16(addr + 2));
        }
        finally
        {
            vm.CharCode = origCode;
            vm.TerminatorValue = origTerm;
            vm.Write();
        }
    }

    [AvaloniaFact]
    public void View_HasWriteButton()
    {
        var view = new TextCharCodeView();
        var write = view.FindControl<Button>("WriteButton");
        Assert.NotNull(write);
        _output.WriteLine("WriteButton present (OK)");
    }

    [AvaloniaFact]
    public void WriteButton_Click_PersistsBoxValues()
    {
        if (Skip()) return;

        var view = new TextCharCodeView();

        var list = view.FindControl<ListBox>("CharCodeList");
        var charBox = view.FindControl<NumericUpDown>("CharCodeBox");
        var termBox = view.FindControl<NumericUpDown>("TerminatorBox");
        var write = view.FindControl<Button>("WriteButton");
        Assert.NotNull(list);
        Assert.NotNull(charBox);
        Assert.NotNull(termBox);
        Assert.NotNull(write);

        // Select the first entry so CurrentAddr is populated.
        list!.SelectedIndex = 0;
        if (view.DataViewModel is not TextCharCodeViewModel vm || vm.CurrentAddr == 0)
        {
            _output.WriteLine("No entry selected — skipping.");
            return;
        }

        uint addr = vm.CurrentAddr;
        uint origCode = CoreState.ROM.u16(addr + 0);
        uint origTerm = CoreState.ROM.u16(addr + 2);

        try
        {
            charBox!.Value = (decimal)0x4321;
            termBox!.Value = (decimal)0xBEEF;
            write!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(0x4321u, CoreState.ROM.u16(addr + 0));
            Assert.Equal(0xBEEFu, CoreState.ROM.u16(addr + 2));
        }
        finally
        {
            charBox!.Value = (decimal)origCode;
            termBox!.Value = (decimal)origTerm;
            write!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }
}
