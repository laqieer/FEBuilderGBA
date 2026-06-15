// SPDX-License-Identifier: GPL-3.0-or-later
// #1148: VM-level coverage for the chapter/map (map_settings) decomp source-backed save-gate.
//
// The View's OnWriteClick routing (decomp → DecompSourceWriterCore "map_settings", classic
// → ROM) and the writer's byte-exact rewrite are covered by the Core tests
// (DecompChapterMapWriterCoreTests) + the CLI E2E (WriteSourceE2ETests). These tests verify
// the two ViewModel seams the View relies on:
//   - MapSettingViewModel/MapSettingFE6ViewModel.BuildSourceFieldDict() emits ONLY the
//     source-writable chapter fields the user changed (pointers excluded);
//   - HasUnsupportedFieldChanges() distinguishes a pointer-only edit from "no change".
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class DecompChapterMapWriteGateTests
    {
        [Fact]
        public void MapSettingVM_BuildSourceFieldDict_EmitsOnlyChangedFields_PointersExcluded()
        {
            var vm = new MapSettingViewModel
            {
                Weather = 1,
                FogLevel = 3,
                PlayerPhaseBGM = 0x20,
                ChapterNumber = 5,
                CpPointer = 0x08123456,         // pointer — must NEVER be source-emitted
                DiffPtrEliwoodNormal = 0x08AAAAAA,
            };
            vm.RefreshSourceFieldSnapshot();

            // No edits → nothing changed.
            Assert.Empty(vm.BuildSourceFieldDict());
            Assert.False(vm.HasUnsupportedFieldChanges());

            // Edit ONLY Weather. Other scalar fields must NOT be emitted (stale-preview guard),
            // and the pointer fields are never present at all.
            vm.Weather = 7;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(7u, changed["Weather"]);
            Assert.Equal(7u, changed["weather"]);  // synonym key
            Assert.False(changed.ContainsKey("FogLevel"));
            Assert.False(changed.ContainsKey("BGM1"));
            Assert.False(changed.ContainsKey("ChapterNumber"));
            // Pointer fields are not part of the source-writable map at all.
            Assert.False(changed.ContainsKey("EventDataPtr"));
            Assert.False(changed.ContainsKey("DiffPtrEliwoodNormal"));
            // Weather was a supported edit, not a pointer edit.
            Assert.False(vm.HasUnsupportedFieldChanges());

            // Re-baseline → no longer changed.
            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());
            Assert.False(vm.HasUnsupportedFieldChanges());
        }

        [Fact]
        public void MapSettingVM_PointerOnlyEdit_DetectedAsUnsupported_NotAsNoChange()
        {
            var vm = new MapSettingViewModel { Weather = 1, CpPointer = 0x08123456 };
            vm.RefreshSourceFieldSnapshot();

            // The user edits ONLY the EventData pointer (an unsupported field). The supported
            // change-set is empty, but HasUnsupportedFieldChanges() must flag it so the
            // save-gate shows a ROM-only/manual notice instead of "no change needed".
            vm.CpPointer = 0x08999999;
            Assert.Empty(vm.BuildSourceFieldDict());
            Assert.True(vm.HasUnsupportedFieldChanges());

            // After re-baseline (post-write or reload), the pointer edit is the new baseline.
            vm.RefreshSourceFieldSnapshot();
            Assert.False(vm.HasUnsupportedFieldChanges());
        }

        [Fact]
        public void MapSettingFE6VM_BuildSourceFieldDict_EmitsOnlyChangedFields_PointerExcluded()
        {
            var vm = new MapSettingFE6ViewModel
            {
                Weather = 1,
                FogLevel = 2,
                ChapterNumber = 4,
                CpPointer = 0x08123456,
            };
            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());

            vm.FogLevel = 5;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(5u, changed["FogLevel"]);
            Assert.False(changed.ContainsKey("Weather"));
            Assert.False(changed.ContainsKey("ChapterNumber"));
            Assert.False(changed.ContainsKey("EventDataPtr"));
            Assert.False(vm.HasUnsupportedFieldChanges());
        }

        [Fact]
        public void MapSettingFE6VM_PointerOnlyEdit_DetectedAsUnsupported()
        {
            var vm = new MapSettingFE6ViewModel { Weather = 1, CpPointer = 0x08123456 };
            vm.RefreshSourceFieldSnapshot();

            vm.CpPointer = 0x08777777;
            Assert.Empty(vm.BuildSourceFieldDict());
            Assert.True(vm.HasUnsupportedFieldChanges());
        }
    }
}
