// SPDX-License-Identifier: GPL-3.0-or-later
// EventScriptViewModel structural-editing tests (#1435).
//
// Beyond the read-only disassembly viewer, the Avalonia Event Script editor now
// supports insert / delete / move / import-from-text + Write-All, all backed by
// the cross-platform EventScriptEditorCore engine. These tests drive the VM the
// same way the View does (set CoreState.EventScript to a synthetic vocabulary,
// plant a script in the ROM, DisassembleAt, then exercise the toolbar methods)
// and verify the command list + ROM mutate correctly, and that a refused
// (no-reference relocate) Write-All leaves the source bytes intact.
//
// Marked [Collection("SharedState")] — mutates CoreState.ROM / EventScript /
// CommentCache / Undo.
using System;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class EventScriptViewModelEditTests : IDisposable
    {
        const uint ScriptOffset = 0x1000;

        readonly ROM? _prevRom;
        readonly Undo? _prevUndo;
        readonly object? _prevComment;
        readonly EventScript? _prevEs;
        readonly ROM _rom;

        public EventScriptViewModelEditTests()
        {
            _prevRom = CoreState.ROM;
            _prevUndo = CoreState.Undo;
            _prevComment = CoreState.CommentCache;
            _prevEs = CoreState.EventScript;

            _rom = new ROM();
            // FE8U requires a >= 0x1000000 (16 MB) ROM in LoadLow; smaller and RomInfo
            // is left null (IsExitCode's version check would NRE).
            _rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U (real term codes)
            CoreState.ROM = _rom;
            CoreState.Undo = new Undo();
            CoreState.CommentCache = new HeadlessEtcCache();
            CoreState.EventScript = StdEs();
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.Undo = _prevUndo;
            CoreState.CommentCache = (IEtcCache?)_prevComment;
            CoreState.EventScript = _prevEs;
        }

        static EventScript StdEs()
        {
            var es = new EventScript();
            typeof(EventScript).GetProperty("Scripts")!.SetValue(es, new[]
            {
                EventScript.ParseScriptLine("0100XXXX\tLOAD1 [X:UNIT:Units]"),
                EventScript.ParseScriptLine("0200XXXX\tMOVE [X:UNIT:Units]"),
                EventScript.ParseScriptLine("0A000000\tENDA [TERM]"),
            });
            return es;
        }

        void PlantScript(params byte[] bytes)
        {
            Array.Copy(bytes, 0, _rom.Data, (int)ScriptOffset, bytes.Length);
        }

        EventScriptViewModel MakeVmDisassembled(params byte[] scriptBytes)
        {
            PlantScript(scriptBytes);
            var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Event };
            vm.AddressText = $"0x{ScriptOffset:X06}";
            Assert.True(vm.TryParseAddress(out uint addr));
            vm.DisassembleAt(addr);
            return vm;
        }

        // LOAD1 + ENDA (8 bytes).
        static byte[] LoadEnda() => new byte[] { 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 };

        [Fact]
        public void Disassemble_PopulatesCommandsAndCatalog()
        {
            var vm = MakeVmDisassembled(LoadEnda());
            Assert.Equal(2, vm.CommandCount);
            Assert.Equal(2, vm.Commands.Count);
            Assert.Equal(3, vm.AvailableCommands.Count); // LOAD1, MOVE, ENDA
        }

        [Fact]
        public void InsertHexCommand_AddsToList()
        {
            var vm = MakeVmDisassembled(LoadEnda());
            vm.SelectedCommandIndex = 0;           // after LOAD1
            vm.InsertHexText = "02000000";          // MOVE
            Assert.True(vm.InsertHexCommand());

            Assert.Equal(3, vm.CommandCount);
            Assert.True(vm.IsDirty);
            Assert.Equal(1, vm.SelectedCommandIndex);
            Assert.Contains("MOVE", vm.Commands[1]);
        }

        [Fact]
        public void InsertSelectedCatalogCommand_UsesCatalogPick()
        {
            var vm = MakeVmDisassembled(LoadEnda());
            vm.SelectedCommandIndex = 0;
            vm.SelectedCommandCatalogIndex = 1;    // MOVE in the catalog
            Assert.True(vm.InsertSelectedCatalogCommand());

            Assert.Equal(3, vm.CommandCount);
            Assert.Contains("MOVE", vm.Commands[1]);
        }

        [Fact]
        public void DeleteSelected_RemovesCommand()
        {
            var vm = MakeVmDisassembled(new byte[]
            {
                0x01, 0x00, 0x00, 0x00,   // LOAD1
                0x02, 0x00, 0x00, 0x00,   // MOVE
                0x0A, 0x00, 0x00, 0x00,   // ENDA
            });
            Assert.Equal(3, vm.CommandCount);
            vm.SelectedCommandIndex = 1;            // MOVE
            Assert.True(vm.DeleteSelected());

            Assert.Equal(2, vm.CommandCount);
            Assert.DoesNotContain(vm.Commands, c => c.Contains("MOVE"));
        }

        [Fact]
        public void MoveUpDown_ReordersCommands()
        {
            var vm = MakeVmDisassembled(new byte[]
            {
                0x01, 0x00, 0x00, 0x00,   // LOAD1
                0x02, 0x00, 0x00, 0x00,   // MOVE
                0x0A, 0x00, 0x00, 0x00,   // ENDA
            });
            vm.SelectedCommandIndex = 1;            // MOVE
            Assert.True(vm.MoveSelectedUp());
            Assert.Equal(0, vm.SelectedCommandIndex);
            Assert.Contains("MOVE", vm.Commands[0]);

            Assert.True(vm.MoveSelectedDown());
            Assert.Equal(1, vm.SelectedCommandIndex);
            Assert.Contains("MOVE", vm.Commands[1]);
        }

        [Fact]
        public void ImportFromText_Append_AddsCommands()
        {
            var vm = MakeVmDisassembled(LoadEnda());
            vm.ImportText = "02000000\n";
            vm.SelectedCommandIndex = -1;           // append at end
            Assert.True(vm.ImportFromText(clear: false));
            Assert.Equal(3, vm.CommandCount);
        }

        [Fact]
        public void WriteAll_InPlaceShrink_Succeeds()
        {
            var vm = MakeVmDisassembled(new byte[]
            {
                0x01, 0x00, 0x00, 0x00,   // LOAD1
                0x02, 0x00, 0x00, 0x00,   // MOVE
                0x0A, 0x00, 0x00, 0x00,   // ENDA
            });
            vm.SelectedCommandIndex = 1;
            vm.DeleteSelected();                    // shrink → fits in place
            Assert.True(vm.WriteAll());
            Assert.False(vm.IsDirty);

            // LOAD1 then ENDA now at the original base, tail zeroed.
            Assert.Equal(0x01, _rom.Data[ScriptOffset + 0]);
            Assert.Equal(0x0A, _rom.Data[ScriptOffset + 4]);
            Assert.Equal(0x00, _rom.Data[ScriptOffset + 8]);
        }

        [Fact]
        public void WriteAll_GrowWithReference_RelocatesAndRepoints()
        {
            var vm = MakeVmDisassembled(LoadEnda());
            // Plant a raw pointer to the script base elsewhere so relocation is allowed.
            uint ownerSlot = 0x4000;
            uint basePtr = U.toPointer(ScriptOffset);
            _rom.Data[ownerSlot + 0] = (byte)(basePtr & 0xFF);
            _rom.Data[ownerSlot + 1] = (byte)((basePtr >> 8) & 0xFF);
            _rom.Data[ownerSlot + 2] = (byte)((basePtr >> 16) & 0xFF);
            _rom.Data[ownerSlot + 3] = (byte)((basePtr >> 24) & 0xFF);

            // Grow the script so it must relocate.
            vm.SelectedCommandIndex = 0;
            vm.InsertHexText = "02000000"; vm.InsertHexCommand();
            vm.SelectedCommandIndex = 1;
            vm.InsertHexText = "02000000"; vm.InsertHexCommand();

            Assert.True(vm.WriteAll());
            Assert.False(vm.IsDirty);

            // The owner pointer was repointed away from the old base.
            uint newSlotVal = (uint)(_rom.Data[ownerSlot]
                | (_rom.Data[ownerSlot + 1] << 8)
                | (_rom.Data[ownerSlot + 2] << 16)
                | (_rom.Data[ownerSlot + 3] << 24));
            Assert.NotEqual(basePtr, newSlotVal);
            Assert.True(U.isSafetyPointer(newSlotVal));
        }

        [Fact]
        public void WriteAll_GrowNoReference_RefusesAndLeavesSourceIntact()
        {
            var vm = MakeVmDisassembled(LoadEnda());
            byte[] before = U.getBinaryData(_rom.Data, ScriptOffset, 8);

            // Grow without any inbound reference → relocate would orphan → refuse.
            vm.SelectedCommandIndex = 0;
            vm.InsertHexText = "02000000"; vm.InsertHexCommand();
            vm.SelectedCommandIndex = 1;
            vm.InsertHexText = "02000000"; vm.InsertHexCommand();

            Assert.False(vm.WriteAll());
            Assert.Contains("Refused", vm.StatusText);

            byte[] after = U.getBinaryData(_rom.Data, ScriptOffset, 8);
            Assert.Equal(before, after); // source untouched
        }

        [Fact]
        public void ProcsScriptType_Disassembles_ViaSharedEngine()
        {
            // The same VM engine drives Procs scripts by ScriptType — set up a synthetic
            // Procs vocabulary and prove it disassembles + the catalog populates.
            var procEs = new EventScript();
            typeof(EventScript).GetProperty("Scripts")!.SetValue(procEs, new[]
            {
                EventScript.ParseScriptLine("0100XXXX\tPROC_CALL [X:UNIT:U]"),
                EventScript.ParseScriptLine("00000000\tPROC_END [TERM]"),
            });
            CoreState.ProcsScript = procEs;

            PlantScript(0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
            var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Procs };
            vm.AddressText = $"0x{ScriptOffset:X06}";
            Assert.True(vm.TryParseAddress(out uint addr));
            vm.DisassembleAt(addr);

            Assert.True(vm.CommandCount >= 1);
            Assert.Equal(2, vm.AvailableCommands.Count);
        }
    }
}
