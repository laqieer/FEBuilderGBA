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
        readonly EventScript? _prevProcs;
        readonly ROM _rom;

        public EventScriptViewModelEditTests()
        {
            _prevRom = CoreState.ROM;
            _prevUndo = CoreState.Undo;
            _prevComment = CoreState.CommentCache;
            _prevEs = CoreState.EventScript;
            _prevProcs = CoreState.ProcsScript;

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
            CoreState.ProcsScript = _prevProcs;
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
        public void Disassemble_FirstCommandUsesRequestedAddress_NotStale()
        {
            // RefreshDisplay formats each offset from CurrentAddr; CurrentAddr must be set
            // BEFORE the first render or offsets show a stale/zero base (#1510 review #1).
            var vm = MakeVmDisassembled(LoadEnda());
            Assert.NotEmpty(vm.Commands);
            // The first command's display line must start with the requested base offset.
            Assert.StartsWith($"0x{ScriptOffset:X06}:", vm.Commands[0]);
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
        public void InsertHexCommand_IsWhitespaceTolerant()
        {
            // The watermark example "0100 4200" contains a space; the single-field Insert
            // box must tolerate it (Copilot PR review inline #4).
            var vm = MakeVmDisassembled(LoadEnda());
            vm.SelectedCommandIndex = 0;
            vm.InsertHexText = "02 00 00 00";   // spaced MOVE
            Assert.True(vm.InsertHexCommand());
            Assert.Equal(3, vm.CommandCount);
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
        public void ImportFromText_EmptyOrInvalid_DoesNotMarkDirty()
        {
            var vm = MakeVmDisassembled(LoadEnda());
            Assert.False(vm.IsDirty);
            int countBefore = vm.CommandCount;

            // An invalid append (no line has >= 4 hex bytes) must NOT change the list,
            // must NOT mark dirty, and must report failure (#1510).
            vm.ImportText = "zz\n12\n";   // 0 valid commands
            Assert.False(vm.ImportFromText(clear: false));
            Assert.False(vm.IsDirty);
            Assert.Equal(countBefore, vm.CommandCount);
            Assert.Contains("No valid commands", vm.StatusText);
        }

        [Fact]
        public void ImportFromText_ClearEmpty_OnAlreadyEmpty_DoesNotMarkDirty()
        {
            // Disassemble then clear to empty, then clear-import nothing again: a clear that
            // removes nothing from an already-empty list is a no-op (no false dirty).
            var vm = MakeVmDisassembled(LoadEnda());
            vm.ImportText = "";
            // First clear empties the (2-command) list — that IS a change.
            Assert.True(vm.ImportFromText(clear: true));
            Assert.Equal(0, vm.CommandCount);

            // Second clear-import on the already-empty list changes nothing.
            vm.ImportText = "";
            bool dirtyBefore = vm.IsDirty;
            Assert.False(vm.ImportFromText(clear: true));
            Assert.Equal(0, vm.CommandCount);
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
        public void WriteAll_DangerZoneAddress_RefusesAndReportsUnsafe()
        {
            // Plant a TERM-only script at a danger-zone offset (< 0x200).
            var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Event };
            Array.Copy(new byte[] { 0x0A, 0x00, 0x00, 0x00 }, 0, _rom.Data, 0x100, 4);
            vm.AddressText = "0x100";
            Assert.True(vm.TryParseAddress(out uint addr));
            vm.DisassembleAt(addr);

            byte[] before = U.getBinaryData(_rom.Data, 0x100, 4);
            Assert.False(vm.WriteAll());
            Assert.Contains("Unsafe", vm.StatusText);
            Assert.Equal(before, U.getBinaryData(_rom.Data, 0x100, 4));
        }

        [Fact]
        public void WorldMapFlag_IsCarriedThroughToWriteAll()
        {
            // A world-map event opened via the world-map jump sets IsWorldMapEvent; the VM
            // must carry it into WriteAll so the world-map terminator is appended (not the
            // normal one). To keep the assertion deterministic regardless of the synthetic
            // disassembler vocabulary, we add an explicit MAPTERM command to the ES so the
            // engine's scan stops cleanly, plant ONLY that single LOAD1 with no terminator,
            // and verify the written script ends with the world-map terminator code.
            // (Core terminator selection is also covered by
            // EventScriptEditorCoreTests.Serialize_WorldMapUsesWorldMapTerm.)
            byte[] mapterm = _rom.RomInfo.Default_event_script_mapterm_code;
            byte[] normterm = _rom.RomInfo.Default_event_script_term_code;
            Assert.NotEqual(mapterm[0], normterm[0]); // meaningful only when they differ

            // Stage the world-map kind (production path: the world-map jump calls
            // StageEventKind just before NavigateTo). The single-command list (LOAD1) is
            // serialized in place; with the world-map kind the engine appends mapterm.
            var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Event };
            vm.StageEventKind(isWorldMapEvent: true, isTopLevelEvent: false);
            // LOAD1 then ENDA so the scan terminates cleanly at 1 real command region;
            // we then delete the ENDA so the list has NO terminator and Write-All must
            // append the world-map one.
            Array.Copy(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 }, 0,
                _rom.Data, (int)ScriptOffset, 8);
            vm.AddressText = $"0x{ScriptOffset:X06}";
            Assert.True(vm.TryParseAddress(out uint addr));
            vm.DisassembleAt(addr);
            Assert.True(vm.IsWorldMapEvent); // staged kind applied on disassemble
            // Remove the ENDA terminator so the list has none.
            vm.SelectedCommandIndex = vm.CommandCount - 1;
            if (vm.Commands[vm.SelectedCommandIndex].Contains("ENDA"))
                vm.DeleteSelected();

            Assert.True(vm.WriteAll());
            // After LOAD1 (4 bytes) the engine appended the world-map terminator.
            Assert.Equal(mapterm[0], _rom.Data[ScriptOffset + 4]);
            Assert.NotEqual(normterm[0], _rom.Data[ScriptOffset + 4]);
        }

        [Fact]
        public void EventKind_IsOneShot_DoesNotLeakAcrossReusedEditor()
        {
            // EventScriptView is a cached singleton; a world-map jump that stages the
            // world-map kind must NOT leak into a later normal-script disassembly on the
            // SAME VM (Copilot PR review #1510 — ROM-corruption risk).
            var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Event };

            // 1) World-map jump: stage + disassemble a world-map script.
            Array.Copy(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 }, 0,
                _rom.Data, (int)ScriptOffset, 8);
            vm.StageEventKind(isWorldMapEvent: true, isTopLevelEvent: false);
            vm.AddressText = $"0x{ScriptOffset:X06}";
            Assert.True(vm.TryParseAddress(out uint wmAddr));
            vm.DisassembleAt(wmAddr);
            Assert.True(vm.IsWorldMapEvent);   // staged kind applied

            // 2) Reuse the SAME editor for a NORMAL script (no staging) — kind must revert.
            uint normalOff = 0x2000;
            Array.Copy(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 }, 0,
                _rom.Data, (int)normalOff, 8);
            vm.AddressText = $"0x{normalOff:X06}";
            Assert.True(vm.TryParseAddress(out uint nAddr));
            vm.DisassembleAt(nAddr);
            Assert.False(vm.IsWorldMapEvent);  // NO leak — reverted to chapter default
            Assert.False(vm.IsTopLevelEvent);
        }

        [Fact]
        public void StagedKind_Abandoned_ClearedByManualDisassemble()
        {
            // Stage a world-map kind but NEVER navigate (the abandoned NewAlloc / jump flow).
            // A later manual Disassemble must clear the stale pending kind so a normal script
            // is scanned/written with chapter-event semantics (#1510).
            var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Event };
            vm.StageEventKind(isWorldMapEvent: true, isTopLevelEvent: false);

            // The user abandons the flow, then manually disassembles a normal script. The
            // manual Disassemble path calls ClearStagedEventKind() before DisassembleAt.
            Array.Copy(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 }, 0,
                _rom.Data, (int)ScriptOffset, 8);
            vm.ClearStagedEventKind();           // what Disassemble_Click does first
            vm.AddressText = $"0x{ScriptOffset:X06}";
            Assert.True(vm.TryParseAddress(out uint addr));
            vm.DisassembleAt(addr);

            Assert.False(vm.IsWorldMapEvent);    // stale staged kind did NOT leak
            Assert.False(vm.IsTopLevelEvent);
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

        [Fact]
        public void ProcsScriptType_WriteAll_AppendsProcsEnd_NotEventTerminator()
        {
            // #1585 finding #1: a Procs editor whose terminal command was deleted must, on
            // Write-All, append the Procs `End` (00000000), NOT the FE event terminator
            // (0A000000). This is the ROM-corruption risk Copilot flagged.
            var procEs = new EventScript();
            typeof(EventScript).GetProperty("Scripts")!.SetValue(procEs, new[]
            {
                EventScript.ParseScriptLine("0100XXXX\tPROC_CALL [X:UNIT:U]"),
                EventScript.ParseScriptLine("00000000\tPROC_END [TERM]"),
            });
            CoreState.ProcsScript = procEs;

            // PROC_CALL (4) + PROC_END (4) = 8 bytes planted.
            PlantScript(0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
            var vm = new EventScriptViewModel { ScriptType = EventScript.EventScriptType.Procs };
            vm.AddressText = $"0x{ScriptOffset:X06}";
            Assert.True(vm.TryParseAddress(out uint addr));
            vm.DisassembleAt(addr);

            // Delete the PROC_END so the list has no terminator; Write-All must append the
            // Procs End and NOT the FE event terminator.
            vm.SelectedCommandIndex = vm.CommandCount - 1;
            vm.DeleteSelected();
            Assert.True(vm.WriteAll());

            // After PROC_CALL (4 bytes) the appended terminator must be the Procs End
            // (00000000), NOT the FE event terminator. On FE8U the event terminator is a
            // non-zero multi-byte code (e.g. 40 05 02 ...), so asserting the 8-byte tail
            // is all-zero proves the Procs End — not the event terminator — was appended.
            byte[] eventTerm = _rom.RomInfo.Default_event_script_term_code;
            Assert.NotEqual(0x00, eventTerm[0]); // sanity: FE event term is non-zero on FE8U
            Assert.Equal(0x00, _rom.Data[ScriptOffset + 4]);
            Assert.Equal(0x00, _rom.Data[ScriptOffset + 5]);
            Assert.Equal(0x00, _rom.Data[ScriptOffset + 6]);
            Assert.Equal(0x00, _rom.Data[ScriptOffset + 7]);
        }
    }
}
