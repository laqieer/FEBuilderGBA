// SPDX-License-Identifier: GPL-3.0-or-later
// Headless UI tests for EditorTopBarWithInputs (#701 Slice A).
//
// EditorTopBarWithInputs is the editable variant of EditorTopBar — surfaces
// the Read Start / Read Count / Read Size as user-editable NumericUpDowns +
// a Reload button. This test suite locks in the foundational API:
//
//   - ReadStartAddress (uint)  — round-trips through inner NumericUpDown
//   - ReadCount        (int)   — round-trips through inner NumericUpDown
//   - ReadSize         (int)   — round-trips through inner NumericUpDown
//   - ReloadRequested          — raised by Reload button click
//
// NO editor migrates in this PR — Copilot CLI plan review of #701 surfaced
// that each editor's top strip has unique extras (FilterCombo, ChangeType,
// FilterBox, ListExpandButton, etc.) so per-editor migration is properly
// scoped as a follow-up. These tests guarantee the API is stable for those
// downstream migration PRs to consume.
using FEBuilderGBA.Avalonia.Controls;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class EditorTopBarWithInputsTests
{
    [AvaloniaFact]
    public void Control_Constructs_WithExpectedChildren()
    {
        var ctrl = new EditorTopBarWithInputs();
        Assert.NotNull(ctrl);
        // After InitializeComponent, named inputs should be reachable via FindControl.
        Assert.NotNull(ctrl.FindControl<NumericUpDown>("ReadStartInput"));
        Assert.NotNull(ctrl.FindControl<NumericUpDown>("ReadCountInput"));
        Assert.NotNull(ctrl.FindControl<NumericUpDown>("ReadSizeInput"));
        Assert.NotNull(ctrl.FindControl<Button>("ReloadButton"));
    }

    [AvaloniaFact]
    public void ReadStartAddress_SetGet_RoundTrips()
    {
        var ctrl = new EditorTopBarWithInputs();
        ctrl.ReadStartAddress = 0x08000000;
        Assert.Equal(0x08000000u, ctrl.ReadStartAddress);
    }

    [AvaloniaFact]
    public void ReadStartAddress_Zero_RoundTrips()
    {
        var ctrl = new EditorTopBarWithInputs();
        ctrl.ReadStartAddress = 0u;
        Assert.Equal(0u, ctrl.ReadStartAddress);
    }

    [AvaloniaFact]
    public void ReadCount_SetGet_RoundTrips()
    {
        var ctrl = new EditorTopBarWithInputs();
        ctrl.ReadCount = 42;
        Assert.Equal(42, ctrl.ReadCount);
    }

    [AvaloniaFact]
    public void ReadSize_SetGet_RoundTrips()
    {
        var ctrl = new EditorTopBarWithInputs();
        ctrl.ReadSize = 28;
        Assert.Equal(28, ctrl.ReadSize);
    }

    [AvaloniaFact]
    public void ReadStartAddress_SetGet_ReflectsInNumericUpDown()
    {
        // Programmatic Set must propagate to the inner NumericUpDown so that
        // automation can read it back via the standard Avalonia value.
        var ctrl = new EditorTopBarWithInputs();
        ctrl.ReadStartAddress = 0x12345678u;
        var box = ctrl.FindControl<NumericUpDown>("ReadStartInput");
        Assert.NotNull(box);
        Assert.Equal((decimal)0x12345678u, box!.Value);
    }

    [AvaloniaFact]
    public void ReadCount_NumericUpDownChange_ReflectsInProperty()
    {
        // User-typed NumericUpDown value must be visible through the public
        // property without any extra "reload" trip.
        var ctrl = new EditorTopBarWithInputs();
        var box = ctrl.FindControl<NumericUpDown>("ReadCountInput");
        Assert.NotNull(box);
        box!.Value = 17m;
        Assert.Equal(17, ctrl.ReadCount);
    }

    [AvaloniaFact]
    public void ReloadButton_Click_FiresReloadRequested()
    {
        var ctrl = new EditorTopBarWithInputs();
        bool fired = false;
        ctrl.ReloadRequested += (_, _) => fired = true;

        var btn = ctrl.FindControl<Button>("ReloadButton");
        Assert.NotNull(btn);
        btn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(fired);
    }

    [AvaloniaFact]
    public void ReloadRequested_BubblesAsRoutedEvent()
    {
        // The event is registered Bubble so an outer host (Window/Panel) can
        // subscribe at a higher level without having to wire each editor.
        var window = new Window { Width = 200, Height = 100 };
        var ctrl = new EditorTopBarWithInputs();
        window.Content = ctrl;
        bool firedAtWindow = false;
        window.AddHandler(EditorTopBarWithInputs.ReloadRequestedEvent,
            (object? s, RoutedEventArgs e) => firedAtWindow = true);
        window.Show();
        try
        {
            var btn = ctrl.FindControl<Button>("ReloadButton");
            Assert.NotNull(btn);
            btn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(firedAtWindow);
        }
        finally
        {
            window.Close();
        }
    }
}
