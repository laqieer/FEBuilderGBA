using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless ROM-backed tests for the FE7 event battle-data editor (issue #1436 —
/// the Avalonia <see cref="EventBattleDataFE7View"/> was display-only; this verifies the
/// editable W0/B2/B3 NumericUpDowns + undo-tracked Write button, matching WinForms
/// <c>EventBattleDataFE7Form</c>).
///
/// Battle-data struct = 4-byte entry:
///   W0 = u16 attack type @ +0x00, B2 = u8 attacker/side @ +0x02, B3 = u8 damage @ +0x03.
/// </summary>
[Collection("SharedState")]
public class EventBattleDataFE7ViewModelTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public EventBattleDataFE7ViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    // The battle-data scan only finds entries inside FE7-family event scripts.
    bool Skip(out uint addr)
    {
        addr = 0;
        if (!_fixture.IsAvailable || CoreState.ROM?.RomInfo == null)
        {
            _output.WriteLine("ROM not available — skipping.");
            return true;
        }
        addr = EventSubEditorHelper.FindFirstBattleDataAddr(CoreState.ROM);
        if (addr == 0)
        {
            _output.WriteLine($"No FE7 battle-data address found (version {_fixture.Version}) — skipping.");
            return true;
        }
        return false;
    }

    // ====================================================================
    // ViewModel: read / write round-trip
    // ====================================================================

    [Fact]
    public void LoadList_ReturnsBattleDataEntry()
    {
        if (Skip(out uint addr)) return;

        var vm = new EventBattleDataFE7ViewModel();
        var list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.Equal(addr, list[0].addr);
        Assert.Equal(1, vm.GetListCount());
    }

    [Fact]
    public void LoadEntry_ReadsStructFields()
    {
        if (Skip(out uint addr)) return;

        var vm = new EventBattleDataFE7ViewModel();
        vm.LoadEntry(addr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(addr, vm.CurrentAddr);
        Assert.Equal((uint)CoreState.ROM.u16(addr + 0), vm.AttackType);
        Assert.Equal((uint)CoreState.ROM.u8(addr + 2), vm.Attacker);
        Assert.Equal((uint)CoreState.ROM.u8(addr + 3), vm.Damage);
    }

    [Fact]
    public void Write_RoundTripsAllFields()
    {
        if (Skip(out uint addr)) return;

        var vm = new EventBattleDataFE7ViewModel();
        vm.LoadEntry(addr);

        uint w0 = vm.AttackType, b2 = vm.Attacker, b3 = vm.Damage;
        try
        {
            vm.AttackType = 0x0123;
            vm.Attacker = 0x45;
            vm.Damage = 0x67;
            vm.Write();

            var vm2 = new EventBattleDataFE7ViewModel();
            vm2.LoadEntry(addr);
            Assert.Equal(0x0123u, vm2.AttackType);
            Assert.Equal(0x45u, vm2.Attacker);
            Assert.Equal(0x67u, vm2.Damage);

            // Raw ROM bytes match the written values.
            Assert.Equal((ushort)0x0123, CoreState.ROM.u16(addr + 0));
            Assert.Equal((byte)0x45, CoreState.ROM.u8(addr + 2));
            Assert.Equal((byte)0x67, CoreState.ROM.u8(addr + 3));
        }
        finally
        {
            vm.AttackType = w0; vm.Attacker = b2; vm.Damage = b3;
            vm.Write();
        }
    }

    // ====================================================================
    // View-level regression test (the actual #1436 bug was in the View):
    // controls exist, Write button persists edits, undo restores bytes.
    // ====================================================================

    [AvaloniaFact]
    public void View_HasEditableControlsAndWriteButton()
    {
        var view = new EventBattleDataFE7View();

        Assert.NotNull(view.FindControl<NumericUpDown>("AttackTypeUpDown"));
        Assert.NotNull(view.FindControl<NumericUpDown>("AttackerUpDown"));
        Assert.NotNull(view.FindControl<NumericUpDown>("DamageUpDown"));
        Assert.NotNull(view.FindControl<Button>("WriteButton"));
    }

    [AvaloniaFact]
    public void View_WriteButton_PersistsEdits_AndUndoRestores()
    {
        if (Skip(out uint addr)) return;

        Undo? prevUndo = CoreState.Undo;
        CoreState.Undo = new Undo();
        try
        {
            var view = new EventBattleDataFE7View();

            // Reach the private VM and load the resolved entry (drives the same
            // path OnSelected would, without needing the window Opened event).
            var vmField = typeof(EventBattleDataFE7View).GetField(
                "_vm", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(vmField);
            var vm = (EventBattleDataFE7ViewModel)vmField!.GetValue(view)!;
            vm.LoadEntry(addr);
            Assert.True(vm.IsLoaded);

            byte[] original = CoreState.ROM.getBinaryData(addr, 4);

            // Populate the editable controls (as UpdateUI would), then drive the
            // real Write button through its Click handler.
            var attackType = view.FindControl<NumericUpDown>("AttackTypeUpDown")!;
            var attacker = view.FindControl<NumericUpDown>("AttackerUpDown")!;
            var damage = view.FindControl<NumericUpDown>("DamageUpDown")!;
            attackType.Value = 0x0ABC;
            attacker.Value = 0x12;
            damage.Value = 0x34;

            var writeButton = view.FindControl<Button>("WriteButton")!;
            writeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // The edit is persisted to ROM.
            Assert.Equal((ushort)0x0ABC, CoreState.ROM.u16(addr + 0));
            Assert.Equal((byte)0x12, CoreState.ROM.u8(addr + 2));
            Assert.Equal((byte)0x34, CoreState.ROM.u8(addr + 3));

            // Undo-tracked: rolling back restores the original four bytes.
            CoreState.Undo!.RunUndo();
            Assert.Equal(original, CoreState.ROM.getBinaryData(addr, 4));
        }
        finally
        {
            CoreState.Undo = prevUndo;
        }
    }
}
