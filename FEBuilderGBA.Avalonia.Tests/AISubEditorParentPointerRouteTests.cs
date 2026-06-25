// SPDX-License-Identifier: GPL-3.0-or-later
// #1414 (Copilot CLI PR-review #1) — the parent-pointer route through the real
// IEditorView.NavigateTo(uint) must actually LOAD a non-zero parent pointer.
//
// After #1414 each sub-editor's standalone LoadList() holds only the addr-0
// placeholder, so EntryList.SelectAddress(realAddr) returns false and (before the
// fix) NavigateTo left CurrentAddr at 0 — meaning the future AIScript per-param
// dispatch route would silently fail to load and Write would no-op. NavigateTo
// now falls back to a direct load for a non-zero address the list can't select.
//
// These tests exercise the REAL IEditorView route (view.NavigateTo), not the VM's
// LoadEntry directly:
//   - NavigateTo(realAddr) loads the supplied address (CurrentAddr == realAddr,
//     IsLoaded) so a subsequent Write targets it;
//   - NavigateTo(0) keeps the standalone placeholder/no-op (CurrentAddr stays 0);
//   - clicking Write after NavigateTo(realAddr) mutates ONLY the supplied address.
//
// Uses [AvaloniaFact] (headless) because it constructs real Window views.
using System;
using System.Collections.Generic;
using System.Reflection;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AISubEditorParentPointerRouteTests
    {
        // A real, safe, in-bounds write target well past the GBA header.
        const uint Target = 0x300000;

        interface IViewProbe
        {
            Window MakeView();
        }

        sealed class TilesProbe : IViewProbe { public Window MakeView() => new AITilesView(); }
        sealed class UnitsProbe : IViewProbe { public Window MakeView() => new AIUnitsView(); }
        sealed class CoordProbe : IViewProbe { public Window MakeView() => new AIASMCoordinateView(); }
        sealed class RangeProbe : IViewProbe { public Window MakeView() => new AIASMRangeView(); }
        sealed class CallTalkProbe : IViewProbe { public Window MakeView() => new AIASMCALLTALKView(); }

        static IEnumerable<IViewProbe> Probes()
        {
            yield return new TilesProbe();
            yield return new UnitsProbe();
            yield return new CoordProbe();
            yield return new RangeProbe();
            yield return new CallTalkProbe();
        }

        static uint CurrentAddrOf(Window view)
        {
            var vm = (object?)((dynamic)view).DataViewModel;
            Assert.NotNull(vm);
            return (uint)vm!.GetType().GetProperty("CurrentAddr")!.GetValue(vm)!;
        }

        static bool IsLoadedOf(Window view) => ((IEditorView)view).IsLoaded;

        [AvaloniaFact]
        public void NavigateTo_RealParentPointer_LoadsSuppliedAddress()
        {
            RomTestHelper.WithRom("FE8U", () =>
            {
                ROM rom = CoreState.ROM;
                Assert.True(U.isSafetyOffset(Target) && Target + 4 <= (uint)rom.Data.Length);

                foreach (IViewProbe probe in Probes())
                {
                    Window view = probe.MakeView();
                    // The view's Opened handler runs LoadList() in real use; here we
                    // call NavigateTo directly as the parent dispatch would after open.
                    ((IEditorView)view).NavigateTo(Target);

                    Assert.Equal(Target, CurrentAddrOf(view));
                    Assert.True(IsLoadedOf(view),
                        $"{view.GetType().Name}: NavigateTo(realAddr) must load the supplied parent pointer.");
                }
            });
        }

        [AvaloniaFact]
        public void NavigateTo_Zero_KeepsStandalonePlaceholderNoOp()
        {
            RomTestHelper.WithRom("FE8U", () =>
            {
                foreach (IViewProbe probe in Probes())
                {
                    Window view = probe.MakeView();
                    ((IEditorView)view).NavigateTo(0);
                    // addr 0 stays the guarded no-op placeholder — never a loaded
                    // non-zero heuristic target.
                    Assert.Equal(0u, CurrentAddrOf(view));
                }
            });
        }

        [AvaloniaFact]
        public void Write_AfterNavigateToRealPointer_MutatesOnlySuppliedAddress()
        {
            RomTestHelper.WithRom("FE8U", () =>
            {
                ROM rom = CoreState.ROM;

                foreach (IViewProbe probe in Probes())
                {
                    Window view = probe.MakeView();
                    ((IEditorView)view).NavigateTo(Target);
                    Assert.Equal(Target, CurrentAddrOf(view));

                    // Set the first editable NumericUpDown to a sentinel so the Write
                    // handler (which reads the boxes) has something to persist.
                    var firstBox = view.GetVisualOrLogicalNumericUpDown();
                    Assert.NotNull(firstBox);
                    firstBox!.Value = 0x7F;

                    byte[] before = (byte[])rom.Data.Clone();
                    InvokeWriteClick(view);
                    byte[] after = rom.Data;

                    for (int i = 0; i < before.Length; i++)
                    {
                        if (i >= Target && i < Target + 4) continue;
                        Assert.True(before[i] == after[i],
                            $"{view.GetType().Name}: Write() mutated a byte outside the supplied target at 0x{i:X}.");
                    }
                }
            });
        }

        // Click the WriteButton via its registered handler (the views wire
        // WriteButton.Click += OnWrite in the constructor).
        static void InvokeWriteClick(Window view)
        {
            var writeButton = view.FindControl<Button>("WriteButton");
            Assert.NotNull(writeButton);
            writeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    static class ViewProbeExtensions
    {
        // Find the first NumericUpDown in the view's logical tree.
        public static NumericUpDown? GetVisualOrLogicalNumericUpDown(this Control root)
        {
            foreach (var d in root.GetLogicalDescendants())
                if (d is NumericUpDown n) return n;
            return null;
        }

        static IEnumerable<global::Avalonia.LogicalTree.ILogical> GetLogicalDescendants(this Control root)
        {
            foreach (var child in ((global::Avalonia.LogicalTree.ILogical)root).LogicalChildren)
            {
                yield return child;
                if (child is Control c)
                    foreach (var sub in c.GetLogicalDescendants())
                        yield return sub;
            }
        }
    }
}
