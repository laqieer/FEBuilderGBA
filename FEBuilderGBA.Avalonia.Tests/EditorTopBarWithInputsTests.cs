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
using System.Linq;
using FEBuilderGBA.Avalonia.Controls;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using global::Avalonia.LogicalTree;
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

    [AvaloniaFact]
    public void HostAutomationId_PropagatesDerivedIdsToInnerControls()
    {
        // Hosts set ONE id on the control; inner inputs/button get derived
        // suffixes so E2E selectors can address each independently.
        var window = new Window { Width = 200, Height = 100 };
        var ctrl = new EditorTopBarWithInputs();
        AutomationProperties.SetAutomationId(ctrl, "MonsterItem_TopBar");
        window.Content = ctrl;
        window.Show();
        try
        {
            var start = ctrl.FindControl<NumericUpDown>("ReadStartInput");
            var count = ctrl.FindControl<NumericUpDown>("ReadCountInput");
            var size = ctrl.FindControl<NumericUpDown>("ReadSizeInput");
            var reload = ctrl.FindControl<Button>("ReloadButton");
            Assert.NotNull(start);
            Assert.NotNull(count);
            Assert.NotNull(size);
            Assert.NotNull(reload);
            // "_TopBar" suffix is stripped from the host id before deriving.
            Assert.Equal("MonsterItem_ReadStart_Input", AutomationProperties.GetAutomationId(start));
            Assert.Equal("MonsterItem_ReadCount_Input", AutomationProperties.GetAutomationId(count));
            Assert.Equal("MonsterItem_ReadSize_Input", AutomationProperties.GetAutomationId(size));
            Assert.Equal("MonsterItem_Reload_Button", AutomationProperties.GetAutomationId(reload));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ExplicitAutomationIdOverride_TakesPrecedence()
    {
        // Migrated editors set explicit *AutomationId styled-properties to
        // preserve pre-#701 selectors that the existing E2E tests rely on
        // (e.g. SongInstrument_ReadStartAddress_Input — a non-standard
        // legacy id from the SongInstrument editor).
        var window = new Window { Width = 200, Height = 100 };
        var ctrl = new EditorTopBarWithInputs();
        AutomationProperties.SetAutomationId(ctrl, "SongInstrument_TopBar");
        ctrl.ReadStartAutomationId = "SongInstrument_ReadStartAddress_Input";
        ctrl.ReadCountAutomationId = "SongInstrument_ReadCount_Input";
        ctrl.ReadSizeAutomationId = "SongInstrument_ReadSize_Input";
        ctrl.ReloadAutomationId = "SongInstrument_ReloadList_Button";
        window.Content = ctrl;
        window.Show();
        try
        {
            var start = ctrl.FindControl<NumericUpDown>("ReadStartInput");
            var count = ctrl.FindControl<NumericUpDown>("ReadCountInput");
            var size = ctrl.FindControl<NumericUpDown>("ReadSizeInput");
            var reload = ctrl.FindControl<Button>("ReloadButton");
            Assert.NotNull(start);
            Assert.NotNull(count);
            Assert.NotNull(size);
            Assert.NotNull(reload);
            Assert.Equal("SongInstrument_ReadStartAddress_Input", AutomationProperties.GetAutomationId(start));
            Assert.Equal("SongInstrument_ReadCount_Input", AutomationProperties.GetAutomationId(count));
            Assert.Equal("SongInstrument_ReadSize_Input", AutomationProperties.GetAutomationId(size));
            Assert.Equal("SongInstrument_ReloadList_Button", AutomationProperties.GetAutomationId(reload));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TwoInstancesInSameVisualTree_HaveDistinctAutomationIds()
    {
        // The MonsterItemViewer hosts 3 read-bars in 3 tabs — must NOT
        // produce duplicate AutomationIds in the visual tree (would break
        // the AutomationIdTests sweep and any UIAutomation lookup).
        var window = new Window { Width = 400, Height = 200 };
        var panel = new StackPanel();
        var a = new EditorTopBarWithInputs();
        var b = new EditorTopBarWithInputs();
        AutomationProperties.SetAutomationId(a, "MonsterTab1_TopBar");
        AutomationProperties.SetAutomationId(b, "MonsterTab2_TopBar");
        panel.Children.Add(a);
        panel.Children.Add(b);
        window.Content = panel;
        window.Show();
        try
        {
            // Pull every NumericUpDown / Button under the window and prove
            // no derived id is shared between the two instances.
            var ids = window
                .GetLogicalDescendants()
                .OfType<Control>()
                .Select(AutomationProperties.GetAutomationId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
            // The core invariant: NO duplicates across the two instances.
            // (Host UserControls themselves also expose their own ids
            // "MonsterTab1_TopBar" / "MonsterTab2_TopBar", so total count
            // depends on tree shape — we assert distinctness, not exact size.)
            Assert.Equal(ids.Count, ids.Distinct().Count());
            // All 8 derived ids are present and unique.
            Assert.Contains("MonsterTab1_ReadStart_Input", ids);
            Assert.Contains("MonsterTab1_ReadCount_Input", ids);
            Assert.Contains("MonsterTab1_ReadSize_Input", ids);
            Assert.Contains("MonsterTab1_Reload_Button", ids);
            Assert.Contains("MonsterTab2_ReadStart_Input", ids);
            Assert.Contains("MonsterTab2_ReadCount_Input", ids);
            Assert.Contains("MonsterTab2_ReadSize_Input", ids);
            Assert.Contains("MonsterTab2_Reload_Button", ids);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NoHostAutomationId_LeavesInnerIdsEmpty()
    {
        // If the host never set an AutomationId on the control, the inner
        // ids must stay empty (NOT a hard-coded "ETBI_*" that would collide
        // across instances).
        var window = new Window { Width = 200, Height = 100 };
        var ctrl = new EditorTopBarWithInputs();
        window.Content = ctrl;
        window.Show();
        try
        {
            var start = ctrl.FindControl<NumericUpDown>("ReadStartInput");
            var count = ctrl.FindControl<NumericUpDown>("ReadCountInput");
            var size = ctrl.FindControl<NumericUpDown>("ReadSizeInput");
            var reload = ctrl.FindControl<Button>("ReloadButton");
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetAutomationId(start!)));
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetAutomationId(count!)));
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetAutomationId(size!)));
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetAutomationId(reload!)));
        }
        finally
        {
            window.Close();
        }
    }
}
