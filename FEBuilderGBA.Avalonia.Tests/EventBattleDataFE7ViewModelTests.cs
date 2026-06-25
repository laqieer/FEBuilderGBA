using System;
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
/// Tests for the FE7 event battle-data editor (issue #1436 — the Avalonia
/// <see cref="EventBattleDataFE7View"/> was display-only; this verifies the editable
/// W0/B2/B3 NumericUpDowns + undo-tracked Write button, matching WinForms
/// <c>EventBattleDataFE7Form</c>).
///
/// Battle-data struct = 4-byte entry:
///   W0 = u16 attack type @ +0x00, B2 = u8 attacker/side @ +0x02, B3 = u8 damage @ +0x03.
///
/// The LoadEntry/Write/undo tests build a deterministic SYNTHETIC ROM with a known
/// battle-data entry, so they run identically with or without a real ROM checkout
/// (the scan-based LoadList path needs real event scripts and is a separate
/// SkippableFact gated on <see cref="RomFixture"/>).
/// </summary>
[Collection("SharedState")]
public class EventBattleDataFE7ViewModelTests : IClassFixture<RomFixture>, IDisposable
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly ROM? _savedRom;
    private readonly Undo? _savedUndo;

    // Fixed address of the synthetic battle-data entry.
    const uint EntryAddr = 0x1000;

    public EventBattleDataFE7ViewModelTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    /// <summary>
    /// Install a deterministic synthetic ROM with one 4-byte battle-data entry at
    /// <see cref="EntryAddr"/> (W0=0x0123, B2=0x45, B3=0x67) and a fresh undo buffer.
    /// </summary>
    static ROM MakeSyntheticRom()
    {
        var rom = new ROM();
        rom.LoadLow("battledata-1436.gba", new byte[0x200000], "NAZO");
        rom.Data[EntryAddr + 0] = 0x23; // W0 low
        rom.Data[EntryAddr + 1] = 0x01; // W0 high
        rom.Data[EntryAddr + 2] = 0x45; // B2
        rom.Data[EntryAddr + 3] = 0x67; // B3
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        return rom;
    }

    // ====================================================================
    // ViewModel: read / write round-trip (deterministic synthetic ROM)
    // ====================================================================

    [Fact]
    public void LoadEntry_ReadsStructFields()
    {
        MakeSyntheticRom();

        var vm = new EventBattleDataFE7ViewModel();
        vm.LoadEntry(EntryAddr);

        Assert.True(vm.IsLoaded);
        Assert.Equal(EntryAddr, vm.CurrentAddr);
        Assert.Equal(0x0123u, vm.AttackType);
        Assert.Equal(0x45u, vm.Attacker);
        Assert.Equal(0x67u, vm.Damage);
    }

    [Fact]
    public void Write_RoundTripsAllFields()
    {
        var rom = MakeSyntheticRom();

        var vm = new EventBattleDataFE7ViewModel();
        vm.LoadEntry(EntryAddr);

        vm.AttackType = 0x0ABC;
        vm.Attacker = 0x12;
        vm.Damage = 0x34;
        vm.Write();

        // Raw ROM bytes match the written values (little-endian u16 + two bytes).
        Assert.Equal((ushort)0x0ABC, rom.u16(EntryAddr + 0));
        Assert.Equal((byte)0x12, rom.u8(EntryAddr + 2));
        Assert.Equal((byte)0x34, rom.u8(EntryAddr + 3));

        // A fresh VM reads the persisted values back.
        var vm2 = new EventBattleDataFE7ViewModel();
        vm2.LoadEntry(EntryAddr);
        Assert.Equal(0x0ABCu, vm2.AttackType);
        Assert.Equal(0x12u, vm2.Attacker);
        Assert.Equal(0x34u, vm2.Damage);
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
        var rom = MakeSyntheticRom();
        byte[] original = rom.getBinaryData(EntryAddr, 4);

        var view = new EventBattleDataFE7View();

        // Load the synthetic entry via the view's real VM (exposed as DataViewModel),
        // driving the same path OnSelected would, without needing the window Opened event.
        var vm = Assert.IsType<EventBattleDataFE7ViewModel>(view.DataViewModel);
        vm.LoadEntry(EntryAddr);
        Assert.True(vm.IsLoaded);

        // Populate the editable controls (as UpdateUI would), then drive the real
        // Write button through its Click handler.
        var attackType = view.FindControl<NumericUpDown>("AttackTypeUpDown")!;
        var attacker = view.FindControl<NumericUpDown>("AttackerUpDown")!;
        var damage = view.FindControl<NumericUpDown>("DamageUpDown")!;
        attackType.Value = 0x0DEF;
        attacker.Value = 0x21;
        damage.Value = 0x43;

        var writeButton = view.FindControl<Button>("WriteButton")!;
        writeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // The edit is persisted to ROM.
        Assert.Equal((ushort)0x0DEF, rom.u16(EntryAddr + 0));
        Assert.Equal((byte)0x21, rom.u8(EntryAddr + 2));
        Assert.Equal((byte)0x43, rom.u8(EntryAddr + 3));

        // Undo-tracked (the OnWrite handler wraps Write in UndoService.Begin/Commit):
        // rolling back restores the original four bytes byte-identically.
        CoreState.Undo!.RunUndo();
        Assert.Equal(original, rom.getBinaryData(EntryAddr, 4));
    }

    // ====================================================================
    // Real-ROM scan path (LoadList needs real FE7 event scripts) — a true
    // SKIP (not a silent pass) when no battle-data address resolves.
    // ====================================================================

    [SkippableFact]
    public void LoadList_ReturnsBattleDataEntry_OnRealRom()
    {
        Skip.IfNot(_fixture.IsAvailable && CoreState.ROM?.RomInfo != null,
            "No ROM available for the LoadList scan path.");

        uint addr = EventSubEditorHelper.FindFirstBattleDataAddr(CoreState.ROM);
        Skip.If(addr == 0,
            $"No FE7 battle-data address found (version {_fixture.Version}).");

        var vm = new EventBattleDataFE7ViewModel();
        var list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.Equal(addr, list[0].addr);
        Assert.Equal(1, vm.GetListCount());

        // LoadList resolves and loads the entry; its fields match raw ROM.
        Assert.Equal((uint)CoreState.ROM.u16(addr + 0), vm.AttackType);
        Assert.Equal((uint)CoreState.ROM.u8(addr + 2), vm.Attacker);
        Assert.Equal((uint)CoreState.ROM.u8(addr + 3), vm.Damage);
    }
}
