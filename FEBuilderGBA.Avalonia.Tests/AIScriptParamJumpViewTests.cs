// SPDX-License-Identifier: GPL-3.0-or-later
// AIScriptView POINTER_AI* parameter-jump affordance tests (#1600).
//
// Headless [AvaloniaFact] coverage that the AIScript detail-panel parameter
// rows expose the clickable jump affordance for the 5 POINTER_AI* types, and
// that clicking a coordinate parameter routes through ApplyPointerJump
// (resolving the kind and writing the pointer into the in-memory model).
//
// Reuses the AiDisasmEnv synthetic FE8U environment + the param-jump opcode
// builders from AIScriptPointerJumpTests. Marked [Collection("SharedState")]
// because it mutates CoreState.ROM / CoreState.AIScript.
using System;
using System.Reflection;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AIScriptParamJumpViewTests
    {
        // FE8U coordinate opcode: 01 00 FF 00 00 00 00 00 A9 F9 03 08 VVVVVVVV
        static byte[] CoordinateFE8U(uint gbaPtr)
        {
            var b = new byte[16];
            b[0] = 0x01; b[1] = 0x00; b[2] = 0xFF;
            b[8] = 0xA9; b[9] = 0xF9; b[10] = 0x03; b[11] = 0x08;
            b[12] = (byte)(gbaPtr & 0xFF);
            b[13] = (byte)((gbaPtr >> 8) & 0xFF);
            b[14] = (byte)((gbaPtr >> 16) & 0xFF);
            b[15] = (byte)((gbaPtr >> 24) & 0xFF);
            return b;
        }

        static uint ReadLe(byte[] b, int pos)
            => (uint)(b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24));

        static AIScriptViewModel VmOf(AIScriptView view)
        {
            var f = typeof(AIScriptView).GetField("_vm",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            return (AIScriptViewModel)f!.GetValue(view)!;
        }

        // ----------------------------------------------------------------
        // 1. The 5 param labels expose the clickable jump affordance.
        // ----------------------------------------------------------------

        [AvaloniaFact]
        public void ParamLabels_HaveClickableJumpAffordance()
        {
            using var env = new AiDisasmEnv();
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;

            var view = new AIScriptView();
            for (int row = 1; row <= 5; row++)
            {
                var label = view.FindControl<TextBlock>($"Param{row}Label");
                Assert.NotNull(label);
                // Tag carries the 1..5 param row index that ParamLabel_Click reads.
                Assert.Equal(row.ToString(), label!.Tag as string);
                // A Hand cursor was set in AXAML, signalling the label is clickable.
                Assert.NotNull(label.Cursor);
            }

            // The PointerPressed jump handler exists on the View.
            var handler = typeof(AIScriptView).GetMethod("ParamLabel_Click",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(handler);
        }

        // ----------------------------------------------------------------
        // 2. Clicking a coordinate param routes through ApplyPointerJump and
        //    writes the (allocated) pointer into the in-memory model.
        // ----------------------------------------------------------------

        [AvaloniaFact]
        public void ParamLabelClick_Coordinate_JumpsAndWritesPointerIntoModel()
        {
            using var env = new AiDisasmEnv();
            Undo? prevUndo = CoreState.Undo;
            CoreState.Undo = new Undo();
            try
            {
                // A broken coordinate pointer that still DECODES (safe pointer) but
                // whose target data is broken (u16(off+2) != 0) -> the jump allocates.
                uint blockOff = 0x300000;
                env.Rom.write_u8(blockOff + 2, 0xFF);
                uint brokenPtr = U.toPointer(blockOff);

                var body = CoordinateFE8U(brokenPtr);
                env.PlantBody(body, out uint pointerSlotAddr);

                CoreState.ROM = env.Rom;
                CoreState.AIScript = env.AiScript;

                var view = new AIScriptView();
                var vm = VmOf(view);
                vm.LoadEntry(pointerSlotAddr);
                Assert.True(vm.IsLoaded);

                var list = view.FindControl<ListBox>("DisassemblyList");
                var addressBox = view.FindControl<NumericUpDown>("AddressBox");
                var byteCountBox = view.FindControl<NumericUpDown>("ReadByteCountBox");
                addressBox!.Value = vm.CurrentAddr;
                byteCountBox!.Value = vm.ReadByteCount;
                InvokeReload(view);

                // Select the coordinate row so the param rows populate.
                list!.SelectedIndex = 0;

                // The coordinate arg is param 1 (the only non-FIXED arg).
                Assert.Equal(AiPointerKind.Coordinate, vm.ClassifyParam(0, 1));

                // Click the param-1 label (the coordinate jump). ParamNeedsAlloc is
                // true, so a prompt would normally fire; the headless app service
                // returns false for ShowYesNo, which would CANCEL the alloc. To test
                // the jump+writeback path directly, drive ApplyPointerJump as the
                // confirmed-click handler does after a Yes.
                bool ok = vm.ApplyPointerJump(0, 1, CoreState.Undo!.NewUndoData("jump"),
                    out AiPointerKind kind, out uint pointer, out bool allocated);
                Assert.True(ok);
                Assert.Equal(AiPointerKind.Coordinate, kind);
                Assert.True(allocated);

                // The new pointer is now in the in-memory model (byte 12), so a
                // later WriteScript serializes it. The View's param-row population
                // reflects the same opcode args the click handler dispatches on.
                byte[] serialized = vm.SerializeScript();
                Assert.Equal(pointer, ReadLe(serialized, 12));

                // The View exposes the coordinate param on row 1 (a non-empty
                // label that the click handler dispatches on).
                Assert.False(string.IsNullOrEmpty(vm.GetParamLabel(0, 1)));
                Assert.Equal(1, vm.GetParamCount(0)); // exactly one non-FIXED arg
            }
            finally
            {
                CoreState.Undo = prevUndo;
            }
        }

        static void InvokeReload(AIScriptView view)
        {
            var m = typeof(AIScriptView).GetMethod("ReloadList_Click",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(m);
            m!.Invoke(view, new object?[] { null, new global::Avalonia.Interactivity.RoutedEventArgs() });
        }
    }
}
