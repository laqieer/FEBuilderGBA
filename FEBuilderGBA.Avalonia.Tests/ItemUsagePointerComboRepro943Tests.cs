// SPDX-License-Identifier: GPL-3.0-or-later
// Repro + regression tests for bug #4 of issue #943:
// In the Item Usage Pointer editor, after a list row loads, the "Function"
// combo box stays BLANK — it should auto-select the named-function entry
// whose hex key matches the current pointer.
//
// These tests load a MULTIBYTE ROM (FE8J) — the version where the original
// FE8 offscreen render confirmed the combo was empty — and exercise the
// EXACT logic the view uses, split into separate assertions so the failing
// one pins the root cause.
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ItemUsagePointerComboRepro943Tests
    {
        readonly ITestOutputHelper _output;

        public ItemUsagePointerComboRepro943Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Replicates the EXACT hex-token extraction the view's UpdateUI match
        /// loop and FunctionCombo_SelectionChanged use, so the test pins the
        /// real production behavior.
        /// </summary>
        static uint ParseHexKey(string line)
        {
            int eq = line.IndexOf('=');
            string hexToken = eq >= 0 ? line.Substring(0, eq).Trim() : line.Trim();
            return U.atoh(hexToken);
        }

        /// <summary>The same match index the view computes in UpdateUI.</summary>
        static int ComputeMatchIndex(IReadOnlyList<string> lines, uint pointer)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (ParseHexKey(lines[i]) == pointer)
                    return i;
            }
            return -1;
        }

        // -------------------------------------------------------------------
        // Assertion 1: LoadFunctionLines returns a NON-empty list for FE8J.
        // (candidate a: config-resolution / lang / ClipComment failure)
        // -------------------------------------------------------------------
        [Fact]
        public void Assertion1_LoadFunctionLines_NonEmpty_ForMultibyteRom()
        {
            string? romPath = TestRomLocator.FindRom("FE8J");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8J ROM not available (roms/FE8J.gba or ROMS_DIR) — #4 repro did NOT execute.");
                return;
            }
            RomTestHelper.WithRom("FE8J", () =>
            {
                Assert.True(CoreState.ROM.RomInfo.is_multibyte,
                    "FE8J must be multibyte for this repro");

                var vm = new ItemUsagePointerViewerViewModel();
                var lines = vm.LoadFunctionLines(0); // 0 = Usability

                _output.WriteLine($"LoadFunctionLines(0) returned {lines.Count} lines");
                if (lines.Count > 0)
                    _output.WriteLine($"first line = '{lines[0]}'");

                Assert.NotEmpty(lines);
            });
        }

        // -------------------------------------------------------------------
        // Assertion 2: at least one parsed hex key equals a real row pointer.
        // (candidate b: hex token mangled / pointer mismatch)
        // -------------------------------------------------------------------
        [Fact]
        public void Assertion2_SomeFunctionLineMatches_FirstRowPointer()
        {
            string? romPath = TestRomLocator.FindRom("FE8J");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8J ROM not available (roms/FE8J.gba or ROMS_DIR) — #4 repro did NOT execute.");
                return;
            }
            RomTestHelper.WithRom("FE8J", () =>
            {
                var vm = new ItemUsagePointerViewerViewModel();
                var rows = vm.LoadList(0);
                Assert.NotEmpty(rows);

                var lines = vm.LoadFunctionLines(0);

                uint firstRowAddr = rows[0].addr;
                vm.LoadEntry(firstRowAddr);
                uint ptr = vm.UsabilityPointer;
                _output.WriteLine($"first row addr=0x{firstRowAddr:X08} UsabilityPointer=0x{ptr:X08}");

                bool any = false;
                foreach (var line in lines)
                {
                    if (ParseHexKey(line) == ptr) { any = true; _output.WriteLine($"match: '{line}'"); break; }
                }
                Assert.True(any,
                    $"No function line's hex key equals the first row pointer 0x{ptr:X08}");
            });
        }

        // -------------------------------------------------------------------
        // Assertion 3: the match index the VIEW computes is >= 0 for the
        // loaded pointer. (candidate c/d: view-layer selection / binding)
        // -------------------------------------------------------------------
        [Fact]
        public void Assertion3_ViewMatchIndex_IsNonNegative_ForLoadedPointer()
        {
            string? romPath = TestRomLocator.FindRom("FE8J");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8J ROM not available (roms/FE8J.gba or ROMS_DIR) — #4 repro did NOT execute.");
                return;
            }
            RomTestHelper.WithRom("FE8J", () =>
            {
                var vm = new ItemUsagePointerViewerViewModel();
                var rows = vm.LoadList(0);
                Assert.NotEmpty(rows);

                // _currentFunctionLines is loaded in LoadListForFilter BEFORE
                // the first row selection fires UpdateUI — replicate that.
                var lines = vm.LoadFunctionLines(0);

                vm.LoadEntry(rows[0].addr);
                int matchIdx = ComputeMatchIndex(lines, vm.UsabilityPointer);
                _output.WriteLine($"matchIdx={matchIdx} for pointer=0x{vm.UsabilityPointer:X08}");

                Assert.True(matchIdx >= 0,
                    $"View match loop produced no selection (matchIdx={matchIdx}) " +
                    $"for pointer 0x{vm.UsabilityPointer:X08}");
            });
        }

        // -------------------------------------------------------------------
        // Assertion 4 (END-TO-END, view-level): after the WHOLE view opens
        // (InitialLoad -> LoadListForFilter -> SelectFirst -> OnSelected ->
        // UpdateUI), the FunctionCombo must have a selected item that matches
        // the loaded UsabilityPointer. This is the actual user-visible bug.
        // -------------------------------------------------------------------
        [AvaloniaFact]
        public void Assertion4_ViewLevel_FunctionComboSelected_AfterOpen()
        {
            string? romPath = TestRomLocator.FindRom("FE8J");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8J ROM not available (roms/FE8J.gba or ROMS_DIR) — #4 repro did NOT execute.");
                return;
            }
            RomTestHelper.WithRom("FE8J", () =>
            {
                var view = new ItemUsagePointerViewerView();
                view.Show();
                // Pump deferred dispatcher work + layout so the view's open flow
                // (InitialLoad -> LoadListForFilter -> SelectFirst -> OnSelected
                // -> UpdateUI) fully settles, mirroring a live frame.
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                try
                {
                    var combo = view.FindControl<ComboBox>("FunctionCombo");
                    Assert.NotNull(combo);

                    var pointerBox = view.FindControl<NumericUpDown>("UsabilityPointerBox");
                    Assert.NotNull(pointerBox);
                    uint loadedPtr = (uint)(pointerBox!.Value ?? 0m);

                    _output.WriteLine(
                        $"FunctionCombo.SelectedIndex={combo!.SelectedIndex} " +
                        $"ItemCount={(combo.ItemsSource as IReadOnlyList<string>)?.Count ?? -1} " +
                        $"loadedPtr=0x{loadedPtr:X08} " +
                        $"SelectedItem='{combo.SelectedItem}'");

                    // BUG #4: this is blank (SelectedIndex == -1) on master.
                    Assert.True(combo.SelectedIndex >= 0,
                        $"FunctionCombo is BLANK after view open " +
                        $"(SelectedIndex={combo.SelectedIndex}) for pointer 0x{loadedPtr:X08}");

                    // The selected entry's hex key must equal the loaded pointer.
                    string selected = combo.SelectedItem as string ?? "";
                    Assert.Equal(loadedPtr, ParseHexKey(selected));
                }
                finally
                {
                    view.Close();
                }
            });
        }
    }
}
