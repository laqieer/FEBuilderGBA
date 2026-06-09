// SPDX-License-Identifier: GPL-3.0-or-later
// #1017 parity tests for the FE8 Event Unit editor's editable after-coord
// (move-path) list.
//
// The list is data-template-bound + the Write path goes through an OS-driven
// editor, so this is a source-text parity test (same pattern as the other
// *ParityTests in this project): it asserts the axaml exposes the editable
// list + Add/Remove buttons by AutomationId, that the synthetic START-row
// fields are disabled via IsAfterRow, and that the VM wires the Core
// EventUnitCoordCore read/write seam (FE8-gated).
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class EventUnitCoordListParityTests
    {
        static string? FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return null;
        }

        static string ReadFile(params string[] parts)
        {
            string? repoRoot = FindRepoRoot();
            Assert.NotNull(repoRoot);
            string path = Path.Combine(repoRoot!, Path.Combine(parts));
            Assert.True(File.Exists(path), $"Missing {path}");
            return File.ReadAllText(path);
        }

        static string ReadView(string ext)
            => ReadFile("FEBuilderGBA.Avalonia", "Views", "EventUnitView" + ext);

        static string ReadViewModel()
            => ReadFile("FEBuilderGBA.Avalonia", "ViewModels", "EventUnitViewModel.cs");

        [Fact]
        public void Axaml_Has_EditableAfterCoordList_AndButtons()
        {
            string axaml = ReadView(".axaml");
            // The editable list + Add/Remove controls exist by AutomationId.
            Assert.Contains("EventUnit_AfterCoords_List", axaml);
            Assert.Contains("EventUnit_AddCoord_Button", axaml);
            Assert.Contains("EventUnit_RemoveCoord_Button", axaml);
            Assert.Contains("Click=\"AddCoord_Click\"", axaml);
            Assert.Contains("Click=\"RemoveCoord_Click\"", axaml);
            // Panel is FE8-gated (toggled from IsFE8 in the code-behind).
            Assert.Contains("AfterCoordsPanel", axaml);
        }

        [Fact]
        public void Axaml_StartRow_SyntheticFields_AreDisabled()
        {
            string axaml = ReadView(".axaml");
            // Row-0 (START) synthetic fields (Speed/UnitId/Unk1/Unk2/Wait) are
            // disabled via the per-row IsAfterRow binding; X/Y/Ext stay editable.
            Assert.Contains("IsEnabled=\"{Binding IsAfterRow}\"", axaml);
            // The eight per-row columns are bound two-way.
            Assert.Contains("{Binding Speed}", axaml);
            Assert.Contains("{Binding UnitId}", axaml);
            Assert.Contains("{Binding Unk1}", axaml);
            Assert.Contains("{Binding Unk2}", axaml);
            Assert.Contains("{Binding Wait}", axaml);
            Assert.Contains("{Binding X}", axaml);
            Assert.Contains("{Binding Y}", axaml);
            Assert.Contains("{Binding Ext}", axaml);
        }

        [Fact]
        public void CodeBehind_Wires_AddRemove_UnderUndoScope()
        {
            string cs = ReadView(".axaml.cs");
            Assert.Contains("AddCoord_Click", cs);
            Assert.Contains("RemoveCoord_Click", cs);
            Assert.Contains("_vm.AddCoord", cs);
            Assert.Contains("_vm.RemoveCoord", cs);
            // The whole block (incl. coords) writes under ONE undo scope.
            Assert.Contains("_undoService.Begin", cs);
            Assert.Contains("_undoService.Commit", cs);
            Assert.Contains("_undoService.Rollback", cs);
            // The list is bound to the VM collection.
            Assert.Contains("AfterCoordsList.ItemsSource = _vm.AfterCoords", cs);
        }

        [Fact]
        public void ViewModel_References_Core_EventUnitCoordCore_FE8Gated()
        {
            string vm = ReadViewModel();
            // Read + write go through the Core seam.
            Assert.Contains("EventUnitCoordCore.ReadAfterCoords", vm);
            Assert.Contains("EventUnitCoordCore.WriteAfterCoords", vm);
            // FE8-only (20-byte block).
            Assert.Contains("dataSize == 20", vm);
            Assert.Contains("public bool IsFE8", vm);
            // The unified row VM with all 8 fields + the START-row flag.
            Assert.Contains("class Fe8CoordRowViewModel", vm);
            Assert.Contains("public bool IsStartRow", vm);
            Assert.Contains("public bool IsAfterRow", vm);
            // RemoveCoord refuses index 0 (the START row).
            Assert.Contains("index <= 0", vm);
            // The B7 u8 cap is honored when adding.
            Assert.Contains("MaxAfterCoordRecords", vm);
        }
    }
}
