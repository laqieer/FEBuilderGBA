// SPDX-License-Identifier: GPL-3.0-or-later
// AI Script "Script Change" opcode-picker wiring tests (#766).
//
// Proves the Avalonia AI Script editor's "Script Change" button copies a
// category-picked command's DEFAULT bytes into its Binary Code box in the
// exact format the in-place Update / New path round-trips:
//   1. AIScriptViewModel.FormatInstructionHex is the SAME format GetRowHex
//      emits, and the string it produces parses back through the production
//      hex-parse path (UpdateRow -> ParseInstructionHex) and re-emits an
//      identical hex line (format-stable round-trip);
//   2. the picker VM (AIScriptCategorySelectViewModel) yields a non-null
//      SelectedScript with non-empty Data when a valid command index is
//      selected, ConfirmSelection() returns true, and an empty selection
//      yields false / null;
//   3. [AvaloniaFact]: the modal picker's Select returns the chosen Script
//      (typed Close(result)), and AIScriptView.ApplyPickedScript drops that
//      Script's Data into AsmBox as a hex line UpdateRow accepts.
//
// Reuses the AiDisasmEnv synthetic FE8U environment from
// AIScriptDisassemblyTests.cs (#757) — it loads a real width-16 FE8 AI
// EventScript so CoreState.AIScript.Scripts is populated with real opcode
// templates (each Script.Data is the parsed default bytes). Marked
// [Collection("SharedState")] because the suite mutates CoreState.ROM /
// CoreState.AIScript / CoreState.CommentCache / CoreState.BaseDirectory.
// Per the plan, no [Fact] commits a UndoService — these are read / UI-only.
using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class AIScriptChangePickerTests
    {
        // 16-byte AI instruction helpers (FE8 table), mirroring the other
        // AIScript test fixtures. Attack05's byte[1] is PROBABILITY; byte[2]
        // is the 0xFF FIXED marker.
        static byte[] Attack05(byte probability)
        {
            var b = new byte[16];
            b[0] = 0x05; b[1] = probability; b[2] = 0xFF;
            return b;
        }

        static byte[] ExitOpcode(byte id)
        {
            var b = new byte[16];
            b[0] = 0x03; b[1] = 0x00; b[2] = 0xFF; b[3] = id;
            return b;
        }

        // ================================================================
        // 1. FormatInstructionHex format-consistency + parse round-trip.
        // ================================================================

        [Fact]
        public void FormatInstructionHex_MatchesGetRowHex_AndRoundTripsThroughUpdateRow()
        {
            using var env = new AiDisasmEnv();

            // Load a real two-opcode script so GetRowHex has a row 0 to render.
            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            var vm = env.LoadVmAt(body.ToArray());
            vm.DisassembleScript();

            // GetRowHex(0) and FormatInstructionHex(rowBytes) must be the SAME
            // string — GetRowHex delegates to FormatInstructionHex (WU1).
            byte[] row0Bytes = Attack05(0x64);
            string viaFormatter = AIScriptViewModel.FormatInstructionHex(row0Bytes);
            string? viaRow = vm.GetRowHex(0);
            Assert.False(string.IsNullOrWhiteSpace(viaFormatter));
            Assert.Equal(viaRow, viaFormatter);

            // The formatter output is space-separated 2-digit upper hex.
            Assert.Equal("05 64 FF 00 00 00 00 00 00 00 00 00 00 00 00 00", viaFormatter);

            // Format-stable parse round-trip: feeding the formatted hex back
            // through the production parse path (UpdateRow -> ParseInstructionHex
            // -> DisAseemble) succeeds and re-emits an identical GetRowHex line.
            string? updated = vm.UpdateRow(0, viaFormatter);
            Assert.NotNull(updated);
            Assert.Equal(viaFormatter, vm.GetRowHex(0));
        }

        [Fact]
        public void FormatInstructionHex_NullBytes_ReturnsEmpty()
        {
            // Defensive: a null Data must not throw (ApplyPickedScript guards on
            // the Script, but the formatter is also called directly).
            Assert.Equal("", AIScriptViewModel.FormatInstructionHex(null!));
        }

        // ================================================================
        // 2. Picker VM selection / confirm contract.
        // ================================================================

        [Fact]
        public void PickerViewModel_ValidSelection_SetsScriptAndConfirms()
        {
            using var env = new AiDisasmEnv();

            var vm = new AIScriptCategorySelectViewModel();
            vm.Load();
            Assert.True(vm.IsLoaded);
            Assert.NotEmpty(vm.ScriptNames); // real FE8 AI commands loaded

            // No selection yet: ConfirmSelection() is false, SelectedScript null.
            Assert.False(vm.ConfirmSelection());
            Assert.Null(vm.SelectedScript);

            // Select a valid command index — UpdateInfo sets SelectedScript.
            vm.SelectedScriptIndex = 0;
            Assert.NotNull(vm.SelectedScript);
            Assert.NotNull(vm.SelectedScript!.Data);
            Assert.NotEmpty(vm.SelectedScript.Data); // real opcode default bytes

            // ConfirmSelection() now returns true and re-resolves SelectedScript.
            Assert.True(vm.ConfirmSelection());
            Assert.NotNull(vm.SelectedScript);
        }

        [Fact]
        public void PickerViewModel_ClearedSelection_DoesNotConfirm()
        {
            using var env = new AiDisasmEnv();

            var vm = new AIScriptCategorySelectViewModel();
            vm.Load();
            Assert.NotEmpty(vm.ScriptNames);

            // Select then clear: a -1 index clears SelectedScript and refuses.
            vm.SelectedScriptIndex = 0;
            Assert.NotNull(vm.SelectedScript);

            vm.SelectedScriptIndex = -1;
            Assert.Null(vm.SelectedScript);
            Assert.False(vm.ConfirmSelection());
        }

        // ================================================================
        // 3a. [AvaloniaFact] Modal picker Select returns the chosen Script.
        // ================================================================

        [AvaloniaFact]
        public void Picker_Select_ReturnsChosenScript()
        {
            using var env = new AiDisasmEnv();
            // Re-assert env's ROM / AI script as active (defensive vs shared
            // collection churn) before constructing the picker, whose VM Load()
            // reads CoreState.AIScript.Scripts.
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;

            var picker = new ScriptCommandPickerView(EventScript.EventScriptType.AI);
            var scriptList = picker.FindControl<ListBox>("ScriptList");
            Assert.NotNull(scriptList);

            // Selecting a row drives the VM's SelectedScriptIndex via the
            // SelectionChanged handler.
            var items = scriptList!.ItemsSource as IEnumerable<string>;
            Assert.NotNull(items);
            Assert.NotEmpty(items!);
            scriptList.SelectedIndex = 0;

            // Invoke the private Select handler (mirrors a Select-button click).
            // It confirms the selection and Close(result)s with the Script.
            InvokePicker(picker, "Select_Click");

            Assert.NotNull(picker.SelectedScript);
            Assert.NotNull(picker.SelectedScript!.Data);
            Assert.NotEmpty(picker.SelectedScript.Data);
        }

        [AvaloniaFact]
        public void Picker_Select_NoSelection_DoesNotReturnAndShowsHint()
        {
            using var env = new AiDisasmEnv();
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;

            var picker = new ScriptCommandPickerView(EventScript.EventScriptType.AI);
            var scriptList = picker.FindControl<ListBox>("ScriptList");
            var info = picker.FindControl<TextBlock>("InfoLabel");
            Assert.NotNull(scriptList);
            Assert.NotNull(info);

            // No command selected.
            scriptList!.SelectedIndex = -1;
            InvokePicker(picker, "Select_Click");

            // No result returned; an inline hint is shown and the dialog stays.
            Assert.Null(picker.SelectedScript);
            Assert.False(string.IsNullOrWhiteSpace(info!.Text));
        }

        [AvaloniaFact]
        public void Picker_Configure_ReplacesPreviousVmState()
        {
            using var env = new AiDisasmEnv();
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;

            var picker = new ScriptCommandPickerView(EventScript.EventScriptType.AI);
            Assert.IsType<AIScriptCategorySelectViewModel>(picker.DataContext);

            picker.Configure(EventScript.EventScriptType.Procs);

            var procsVm = Assert.IsType<ProcsScriptCategorySelectViewModel>(picker.DataContext);
            var categoryList = picker.FindControl<ListBox>("CategoryList");
            var scriptList = picker.FindControl<ListBox>("ScriptList");
            Assert.Same(procsVm.Categories, categoryList!.ItemsSource);
            Assert.Same(procsVm.ScriptNames, scriptList!.ItemsSource);
            Assert.Null(picker.SelectedScript);
            Assert.Null(picker.DialogResult);
        }

        // ================================================================
        // 3b. [AvaloniaFact] ApplyPickedScript drops Data into AsmBox as hex
        //     that UpdateRow / ParseInstructionHex accepts.
        // ================================================================

        [AvaloniaFact]
        public void View_ApplyPickedScript_PopulatesBinaryCodeWithParseableHex()
        {
            using var env = new AiDisasmEnv();
            CoreState.ROM = env.Rom;
            CoreState.AIScript = env.AiScript;

            // Plant a real script + pointer slot, and drive the view's VM
            // through LoadEntry so it has a populated, editable model whose
            // row 0 we can re-decode the applied hex into.
            var body = new List<byte>();
            body.AddRange(Attack05(0x64));
            body.AddRange(ExitOpcode(0x00));
            env.PlantBody(body.ToArray(), out uint pointerSlotAddr);

            var view = new AIScriptView();
            var asmBox = view.FindControl<TextBox>("AsmBox");
            var nameLabel = view.FindControl<TextBlock>("ScriptCodeNameLabel");
            var list = view.FindControl<ListBox>("DisassemblyList");
            Assert.NotNull(asmBox);
            Assert.NotNull(nameLabel);
            Assert.NotNull(list);

            var vmField = typeof(AIScriptView).GetField(
                "_vm",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(vmField);
            var vm = (AIScriptViewModel)vmField!.GetValue(view)!;

            // Re-assert ROM as active (constructing the view re-enters list/VM
            // init), then load + re-read so the model has a row to edit.
            CoreState.ROM = env.Rom;
            vm.LoadEntry(pointerSlotAddr);
            Assert.True(vm.IsLoaded);

            var addressBox = view.FindControl<NumericUpDown>("AddressBox");
            var byteCountBox = view.FindControl<NumericUpDown>("ReadByteCountBox");
            addressBox!.Value = vm.CurrentAddr;
            byteCountBox!.Value = vm.ReadByteCount;
            Invoke(view, "ReloadList_Click");

            // Pick a real command from the AI script table and apply it. Use a
            // DoNothing-like template via the picker VM so Data is a real
            // opcode default (non-empty).
            var pickerVm = new AIScriptCategorySelectViewModel();
            pickerVm.Load();
            Assert.NotEmpty(pickerVm.ScriptNames);
            pickerVm.SelectedScriptIndex = 0;
            EventScript.Script? chosen = pickerVm.SelectedScript;
            Assert.NotNull(chosen);
            Assert.NotEmpty(chosen!.Data);

            // Drive the factored apply-path (avoids relying on a live modal).
            view.ApplyPickedScript(chosen);

            // AsmBox now holds the chosen command's bytes as a hex line, and the
            // name label shows its mnemonic.
            Assert.False(string.IsNullOrWhiteSpace(asmBox!.Text));
            Assert.Equal(AIScriptViewModel.FormatInstructionHex(chosen.Data), asmBox.Text);
            Assert.False(string.IsNullOrWhiteSpace(nameLabel!.Text));

            // The hex AsmBox now holds must be accepted by the production
            // Update path (re-decodes row 0 from the applied hex).
            list!.SelectedIndex = 0;
            string? updated = vm.UpdateRow(0, asmBox.Text!);
            Assert.NotNull(updated);
        }

        // ----------------------------------------------------------------
        // Reflection helpers (the click handlers are private).
        // ----------------------------------------------------------------

        static void Invoke(AIScriptView view, string method)
        {
            var m = typeof(AIScriptView).GetMethod(
                method,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(m);
            m!.Invoke(view, new object?[] { null, new global::Avalonia.Interactivity.RoutedEventArgs() });
        }

        static void InvokePicker(ScriptCommandPickerView picker, string method)
        {
            var m = typeof(ScriptCommandPickerView).GetMethod(
                method,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(m);
            m!.Invoke(picker, new object?[] { null, new global::Avalonia.Interactivity.RoutedEventArgs() });
        }
    }
}
