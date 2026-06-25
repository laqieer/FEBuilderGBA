using System;
using System.Collections.Generic;
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
/// Tests for the FE7 event move-data editor (issue #1440 — the Avalonia
/// <see cref="EventMoveDataFE7View"/> collapsed the variable-length move-command
/// sequence into ONE row, edited only the first command's B0 direction byte, and
/// had no B1/time field for the appended types 9 (Highlight) / 0xC (Speed change)).
///
/// Move-data block = variable-length sequence of 1-byte commands; types 9/0xC
/// carry an extra B1/time byte at offset+1 (stride 2), all others stride 1; the
/// walk stops at the first non-enable byte (e.g. 0x04 terminator).
///
/// The walk / read / write / undo tests build a deterministic SYNTHETIC ROM, so
/// they run identically with or without a real ROM checkout. The scan-based
/// LoadList path needs real event scripts and is a separate SkippableFact.
/// </summary>
[Collection("SharedState")]
public class EventMoveDataFE7ViewModelTests : IClassFixture<RomFixture>, IDisposable
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly ROM? _savedRom;
    private readonly Undo? _savedUndo;

    // Fixed base address of the synthetic move-data block.
    const uint BlockAddr = 0x1000;

    public EventMoveDataFE7ViewModelTests(RomFixture fixture, ITestOutputHelper output)
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
    /// Install a deterministic synthetic ROM with one move-data block at
    /// <see cref="BlockAddr"/>:
    ///   +0: 00 (Left)
    ///   +1: 09 (Highlight) +2: 14 (time param)
    ///   +3: 0C (Speed)     +4: 02 (time param)
    ///   +5: 02 (Down)
    ///   +6: 04 (End / terminator)
    /// => 4 commands.
    /// </summary>
    static ROM MakeSyntheticRom()
    {
        var rom = new ROM();
        rom.LoadLow("movedata-1440.gba", new byte[0x200000], "NAZO");
        byte[] block = { 0x00, 0x09, 0x14, 0x0C, 0x02, 0x02, 0x04 };
        for (int i = 0; i < block.Length; i++)
            rom.Data[BlockAddr + i] = block[i];
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        return rom;
    }

    // ====================================================================
    // Walk: each command is its own row, with correct variable stride.
    // ====================================================================

    [Fact]
    public void WalkCommands_YieldsOneRowPerCommand_WithVariableStride()
    {
        var rom = MakeSyntheticRom();

        List<AddrResult> list = EventMoveDataFE7Core.WalkCommands(rom, BlockAddr);

        Assert.Equal(4, list.Count);
        Assert.Equal(BlockAddr + 0, list[0].addr); // Left
        Assert.Equal(BlockAddr + 1, list[1].addr); // Highlight (+2 stride)
        Assert.Equal(BlockAddr + 3, list[2].addr); // Speed change (+2 stride)
        Assert.Equal(BlockAddr + 5, list[3].addr); // Down
    }

    [Fact]
    public void GetListCount_ReflectsAllCommands_NotJustFirst()
    {
        // Regression for Copilot PR-review finding #1: --data-verify-full uses
        // GetListCount() as its loop bound, so a multi-command block reporting
        // only 1 would leave commands 2..N unverified.
        MakeSyntheticRom();

        var vm = new EventMoveDataFE7ViewModel();
        var list = vm.LoadListFrom(BlockAddr);

        Assert.Equal(4, list.Count);
        Assert.Equal(4, vm.GetListCount()); // NOT 1
    }

    [Fact]
    public void GetListCount_Zero_WhenNothingLoaded()
    {
        MakeSyntheticRom();
        var vm = new EventMoveDataFE7ViewModel();
        Assert.Equal(0, vm.GetListCount());
    }

    // ====================================================================
    // ViewModel: read / write round-trip incl. B1/time for type 0xC.
    // ====================================================================

    [Fact]
    public void LoadEntry_NormalCommand_HasNoTimeField()
    {
        MakeSyntheticRom();

        var vm = new EventMoveDataFE7ViewModel();
        vm.LoadEntry(BlockAddr + 0); // Left

        Assert.True(vm.IsLoaded);
        Assert.Equal(BlockAddr + 0, vm.CurrentAddr);
        Assert.Equal(0x00u, vm.MoveDirection);
        Assert.False(vm.HasTimeField);
    }

    [Fact]
    public void LoadEntry_AppendedCommand_ReadsTimeByte()
    {
        MakeSyntheticRom();

        var vm = new EventMoveDataFE7ViewModel();
        vm.LoadEntry(BlockAddr + 3); // Speed change (0C)

        Assert.True(vm.IsLoaded);
        Assert.Equal(0x0Cu, vm.MoveDirection);
        Assert.True(vm.HasTimeField);
        Assert.Equal(0x02u, vm.Time); // the param byte at +4
    }

    [Fact]
    public void Write_AppendedCommand_PersistsDirectionAndTime()
    {
        var rom = MakeSyntheticRom();

        var vm = new EventMoveDataFE7ViewModel();
        vm.LoadEntry(BlockAddr + 1); // Highlight (09), time 0x14

        vm.MoveDirection = 0x0C; // change type to Speed change (still appended)
        vm.Time = 0x33;
        vm.Write();

        Assert.Equal((byte)0x0C, rom.u8(BlockAddr + 1));
        Assert.Equal((byte)0x33, rom.u8(BlockAddr + 2));

        // Fresh VM reads back the persisted values.
        var vm2 = new EventMoveDataFE7ViewModel();
        vm2.LoadEntry(BlockAddr + 1);
        Assert.Equal(0x0Cu, vm2.MoveDirection);
        Assert.True(vm2.HasTimeField);
        Assert.Equal(0x33u, vm2.Time);
    }

    [Fact]
    public void Write_NormalCommand_DoesNotTouchNextByte()
    {
        var rom = MakeSyntheticRom();

        var vm = new EventMoveDataFE7ViewModel();
        vm.LoadEntry(BlockAddr + 0); // Left (single byte)

        uint nextBefore = rom.u8(BlockAddr + 1);
        vm.MoveDirection = 0x02; // Down (still single byte)
        vm.Time = 0x77;          // must be ignored — no time field for this type
        vm.Write();

        Assert.Equal((byte)0x02, rom.u8(BlockAddr + 0));
        // The next command byte (the Highlight) must be untouched.
        Assert.Equal(nextBefore, rom.u8(BlockAddr + 1));
    }

    // ====================================================================
    // View-level regression (the #1440 bug was in the View/VM combo):
    // list has every command; Time row shows for 9/0xC; direction change
    // reveals it live (mirrors WinForms B0_ValueChanged); undo restores.
    // ====================================================================

    [AvaloniaFact]
    public void View_HasDirectionAndTimeControls()
    {
        var view = new EventMoveDataFE7View();

        Assert.NotNull(view.FindControl<NumericUpDown>("MoveDirectionUpDown"));
        Assert.NotNull(view.FindControl<NumericUpDown>("TimeUpDown"));
        Assert.NotNull(view.FindControl<Button>("WriteButton"));
    }

    [AvaloniaFact]
    public void View_TimeRow_VisibleOnlyForAppendedTypes()
    {
        MakeSyntheticRom();
        var view = new EventMoveDataFE7View();
        var vm = Assert.IsType<EventMoveDataFE7ViewModel>(view.DataViewModel);

        var dir = view.FindControl<NumericUpDown>("MoveDirectionUpDown")!;
        var time = view.FindControl<NumericUpDown>("TimeUpDown")!;

        // Select a normal command -> time row hidden.
        view.NavigateTo(BlockAddr + 0);
        vm.LoadEntry(BlockAddr + 0);
        // drive the same path OnSelected/UpdateUI would via direction-change handler:
        dir.Value = 0x00;
        Assert.False(time.IsVisible);

        // Change the direction to Speed change (0C) -> time row revealed live.
        dir.Value = 0x0C;
        Assert.True(time.IsVisible);

        // Change back to a normal direction -> hidden again.
        dir.Value = 0x01;
        Assert.False(time.IsVisible);
    }

    [AvaloniaFact]
    public void View_ChangeNormalToAppended_ThenWrite_PersistsTime_AndUndoRestores()
    {
        // Copilot review finding #2: a 1 -> 0xC edit before write must reveal the
        // Time row and write the param byte. Build a block where +0 is a Down (02)
        // command followed by a spare byte we can repurpose into the time param.
        var rom = new ROM();
        rom.LoadLow("movedata-1440b.gba", new byte[0x200000], "NAZO");
        // +0: 01 (Right) ; +1: 02 (Down) ; +2: 04 (End)
        rom.Data[BlockAddr + 0] = 0x01;
        rom.Data[BlockAddr + 1] = 0x02;
        rom.Data[BlockAddr + 2] = 0x04;
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        byte[] original = rom.getBinaryData(BlockAddr, 3);

        var view = new EventMoveDataFE7View();
        var vm = Assert.IsType<EventMoveDataFE7ViewModel>(view.DataViewModel);
        vm.LoadEntry(BlockAddr + 0); // Right (normal)
        Assert.True(vm.IsLoaded);

        var dir = view.FindControl<NumericUpDown>("MoveDirectionUpDown")!;
        var time = view.FindControl<NumericUpDown>("TimeUpDown")!;

        // Change 1 (Right) -> 0xC (Speed change): row appears, set a time value.
        dir.Value = 0x0C;
        Assert.True(time.IsVisible);
        time.Value = 0x55;

        var writeButton = view.FindControl<Button>("WriteButton")!;
        writeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // Direction + time persisted (the old +1 Down byte becomes the time param).
        Assert.Equal((byte)0x0C, rom.u8(BlockAddr + 0));
        Assert.Equal((byte)0x55, rom.u8(BlockAddr + 1));

        // Undo restores the original bytes byte-identically.
        CoreState.Undo!.RunUndo();
        Assert.Equal(original, rom.getBinaryData(BlockAddr, 3));
    }

    // ====================================================================
    // Real-ROM scan path (LoadList needs real FE7 event scripts) — a true SKIP.
    // ====================================================================

    [SkippableFact]
    public void LoadList_ReturnsMoveDataCommands_OnRealRom()
    {
        Skip.IfNot(_fixture.IsAvailable && CoreState.ROM?.RomInfo != null,
            "No ROM available for the LoadList scan path.");

        uint addr = EventSubEditorHelper.FindFirstMoveDataAddr(CoreState.ROM);
        Skip.If(addr == 0,
            $"No FE7 move-data address found (version {_fixture.Version}).");

        var vm = new EventMoveDataFE7ViewModel();
        var list = vm.LoadList();

        Assert.NotEmpty(list);
        Assert.Equal(addr, list[0].addr);
        // The walk must surface MORE than one command for any non-trivial block,
        // OR at least the first command's direction byte must match raw ROM.
        Assert.Equal((uint)CoreState.ROM.u8(addr), vm.MoveDirection);
    }
}
