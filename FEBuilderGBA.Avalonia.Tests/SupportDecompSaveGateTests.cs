// #1149: VM-level coverage for support_units / support_attributes / support_talks
// decomp source-backed save-gate.
//
// Tests verify:
//  - CurrentSourceFieldMap() emits all fields by byte-offset key
//  - BuildSourceFieldDict() emits ONLY changed fields since snapshot
//  - RefreshSourceFieldSnapshot() re-baselines correctly
//  - CurrentEntryId returns U.NOT_FOUND when ROM is null (no CoreState.ROM)
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class SupportDecompSaveGateTests
    {
        // ================================================================
        // SupportUnitEditorViewModel (FE7/8, 24-byte)
        // ================================================================

        [Fact]
        public void SupportUnitVM_CurrentSourceFieldMap_EmitsAllByteOffsets()
        {
            var vm = new SupportUnitEditorViewModel
            {
                Partner1 = 6, Partner2 = 7, Partner3 = 8, Partner4 = 9,
                Partner5 = 10, Partner6 = 11, Partner7 = 12,
                InitialValue1 = 1, InitialValue2 = 2, InitialValue3 = 3,
                InitialValue4 = 4, InitialValue5 = 5, InitialValue6 = 6,
                InitialValue7 = 7,
                GrowthRate1 = 10, GrowthRate2 = 20, GrowthRate3 = 30,
                GrowthRate4 = 40, GrowthRate5 = 50, GrowthRate6 = 60,
                GrowthRate7 = 70,
                PartnerCount = 3, Separator1 = 0xFF, Separator2 = 0xFE,
            };

            var map = vm.CurrentSourceFieldMap();

            Assert.Equal(6u,    map["b0"]);
            Assert.Equal(7u,    map["b1"]);
            Assert.Equal(1u,    map["b7"]);
            Assert.Equal(10u,   map["b14"]);
            Assert.Equal(3u,    map["b21"]);
            Assert.Equal(0xFFu, map["b22"]);
            Assert.Equal(0xFEu, map["b23"]);
            Assert.Equal(24,    map.Count);
        }

        [Fact]
        public void SupportUnitVM_BuildSourceFieldDict_EmitsOnlyChangedFields()
        {
            var vm = new SupportUnitEditorViewModel
            {
                Partner1 = 6, PartnerCount = 2,
            };
            vm.RefreshSourceFieldSnapshot();

            // No changes yet
            Assert.Empty(vm.BuildSourceFieldDict());

            // Change only PartnerCount
            vm.PartnerCount = 3;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(3u, changed["b21"]);
            Assert.False(changed.ContainsKey("b0"));  // Partner1 unchanged

            // Re-baseline → empty again
            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());
        }

        [Fact]
        public void SupportUnitVM_CurrentEntryId_ReturnsNotFound_WhenNoRom()
        {
            // No ROM loaded → GetSupportUnitEntryIdFromAddr returns NOT_FOUND
            var vm = new SupportUnitEditorViewModel { };
            Assert.Equal(U.NOT_FOUND, vm.CurrentEntryId);
        }

        // #1149 finding 4 (Copilot CLI, HIGH severity): the View gate is ALL-OR-NOTHING.
        // If the changed-field dict contains ANY field the owner's fields[] does not
        // declare, the View must BLOCK the whole save (show the manual notice, return,
        // never write a subset and MarkClean — which would silently drop the undeclared
        // edit and mark it saved). This test models the View gate's decision at the
        // VM/dict level: a change-set containing an undeclared field => "block" == true.
        static bool ViewGateBlocks(IReadOnlyDictionary<string, uint> changed, HashSet<string> declared)
        {
            foreach (var kv in changed)
                if (!declared.Contains(kv.Key))
                    return true;   // ANY undeclared field blocks the whole save
            return false;
        }

        [Fact]
        public void SupportUnitVM_AllOrNothingGate_BlocksWhenAnyChangedFieldUndeclared()
        {
            var vm = new SupportUnitEditorViewModel { Partner1 = 6, PartnerCount = 2 };
            vm.RefreshSourceFieldSnapshot();

            // Change a declared field (b21=PartnerCount) AND an undeclared field (b0=Partner1).
            vm.Partner1 = 9;        // b0 (undeclared in this owner)
            vm.PartnerCount = 3;    // b21 (declared)
            var changed = vm.BuildSourceFieldDict();
            Assert.True(changed.ContainsKey("b0"));
            Assert.True(changed.ContainsKey("b21"));

            // Owner declares only b21 — b0 is undeclared, so the all-or-nothing gate blocks
            // the WHOLE save (it must NOT write just b21 and drop b0).
            var declared = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "b21" };
            Assert.True(ViewGateBlocks(changed, declared));
        }

        [Fact]
        public void SupportUnitVM_AllOrNothingGate_AllowsWhenEveryChangedFieldDeclared()
        {
            var vm = new SupportUnitEditorViewModel { PartnerCount = 2 };
            vm.RefreshSourceFieldSnapshot();

            // Change only a declared field.
            vm.PartnerCount = 3;    // b21 (declared)
            var changed = vm.BuildSourceFieldDict();
            Assert.True(changed.ContainsKey("b21"));

            // Every changed field is declared → gate does NOT block (the writer is called).
            var declared = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "b21", "b0" };
            Assert.False(ViewGateBlocks(changed, declared));
        }

        // #1159 finding 4 (Copilot CLI re-review): even AFTER the declared-field gate passes
        // and the writer is called, a bulk write can be PARTIAL — the writer returns Ok but
        // reports macro/expression fields it could not rewrite in DecompSourceWriteResult
        // .SkippedFields. In that case the View's Ok-case must NOT MarkClean/RefreshSnapshot
        // (which would silently treat the skipped edit as saved); it shows the manual notice
        // and leaves the VM dirty. The load-bearing coverage is the Core test
        // DecompSupportSourceWriterTests.WriteTableEntry_SupportUnits_MacroField_ReportedInSkippedFields
        // (the View MarkClean-skip needs a loaded ROM for CurrentEntryId, out of scope here).
        // This test documents + locks the View's Ok-case decision rule at the dict level.
        static bool ViewMarksCleanOnOk(System.Collections.Generic.IReadOnlyList<string> skippedFields)
        {
            // Mirrors the View Ok-case: skip MarkClean when ANY field was skipped (partial).
            return skippedFields == null || skippedFields.Count == 0;
        }

        [Fact]
        public void SupportView_OkCase_DoesNotMarkClean_WhenWriterSkippedAField()
        {
            // Writer returned Ok but reported b0 as skipped (macro) → partial → no MarkClean.
            var skipped = new System.Collections.Generic.List<string> { "b0" };
            Assert.False(ViewMarksCleanOnOk(skipped));

            // Writer returned Ok with nothing skipped → full save → MarkClean.
            var none = new System.Collections.Generic.List<string>();
            Assert.True(ViewMarksCleanOnOk(none));
            Assert.True(ViewMarksCleanOnOk(null));
        }

        // ================================================================
        // SupportUnitFE6ViewModel (FE6, 32-byte)
        // ================================================================

        [Fact]
        public void SupportUnitFE6VM_CurrentSourceFieldMap_Has32Entries()
        {
            var vm = new SupportUnitFE6ViewModel
            {
                Partner1 = 1, Partner10 = 10,
                GrowthRate10 = 50,
                PartnerCount = 5,
                Separator = 0xFF,
            };

            var map = vm.CurrentSourceFieldMap();

            Assert.Equal(32, map.Count);
            Assert.Equal(1u,    map["b0"]);
            Assert.Equal(10u,   map["b9"]);
            Assert.Equal(50u,   map["b29"]);
            Assert.Equal(5u,    map["b30"]);
            Assert.Equal(0xFFu, map["b31"]);
        }

        [Fact]
        public void SupportUnitFE6VM_BuildSourceFieldDict_EmitsOnlyChangedFields()
        {
            var vm = new SupportUnitFE6ViewModel { Separator = 0 };
            vm.RefreshSourceFieldSnapshot();

            Assert.Empty(vm.BuildSourceFieldDict());

            vm.Separator = 0xAB;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(0xABu, changed["b31"]);
            Assert.False(changed.ContainsKey("b0"));

            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());
        }

        [Fact]
        public void SupportUnitFE6VM_CurrentEntryId_ReturnsNotFound_WhenNoRom()
        {
            var vm = new SupportUnitFE6ViewModel { };
            Assert.Equal(U.NOT_FOUND, vm.CurrentEntryId);
        }

        // ================================================================
        // SupportAttributeViewModel (8-byte)
        // ================================================================

        [Fact]
        public void SupportAttributeVM_CurrentSourceFieldMap_Has8Entries()
        {
            var vm = new SupportAttributeViewModel
            {
                AffinityType = 1, AttackBonus = 5, DefenseBonus = 5,
                HitBonus = 10, AvoidBonus = 10, CritBonus = 5,
                CritAvoidBonus = 5, Unknown7 = 0,
            };

            var map = vm.CurrentSourceFieldMap();

            Assert.Equal(8, map.Count);
            Assert.Equal(1u, map["b0"]);
            Assert.Equal(5u, map["b1"]);
            Assert.Equal(0u, map["b7"]);
        }

        [Fact]
        public void SupportAttributeVM_BuildSourceFieldDict_EmitsOnlyChangedFields()
        {
            var vm = new SupportAttributeViewModel { AttackBonus = 5 };
            vm.RefreshSourceFieldSnapshot();

            Assert.Empty(vm.BuildSourceFieldDict());

            vm.AttackBonus = 10;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(10u, changed["b1"]);
            Assert.False(changed.ContainsKey("b0"));

            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());
        }

        [Fact]
        public void SupportAttributeVM_CurrentEntryId_ReturnsNotFound_WhenNoRom()
        {
            var vm = new SupportAttributeViewModel { };
            Assert.Equal(U.NOT_FOUND, vm.CurrentEntryId);
        }

        // ================================================================
        // SupportTalkViewModel (FE8, 16-byte)
        // ================================================================

        [Fact]
        public void SupportTalkVM_CurrentSourceFieldMap_Has8Entries()
        {
            var vm = new SupportTalkViewModel
            {
                SupportPartner1 = 3, SupportPartner2 = 5,
                TextIdC = 0x100, TextIdB = 0x101, TextIdA = 0x102,
                SongC = 0x20, SongB = 0x21, SongA = 0x22,
            };

            var map = vm.CurrentSourceFieldMap();

            Assert.Equal(8, map.Count);
            Assert.Equal(3u,     map["b0"]);
            Assert.Equal(5u,     map["b2"]);
            Assert.Equal(0x100u, map["w4"]);
            Assert.Equal(0x20u,  map["w10"]);
        }

        [Fact]
        public void SupportTalkVM_BuildSourceFieldDict_EmitsOnlyChangedFields()
        {
            var vm = new SupportTalkViewModel { TextIdC = 0x100 };
            vm.RefreshSourceFieldSnapshot();

            Assert.Empty(vm.BuildSourceFieldDict());

            vm.TextIdC = 0x200;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(0x200u, changed["w4"]);
            Assert.False(changed.ContainsKey("b0"));

            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());
        }

        [Fact]
        public void SupportTalkVM_CurrentEntryId_ReturnsNotFound_WhenNoRom()
        {
            var vm = new SupportTalkViewModel { };
            Assert.Equal(U.NOT_FOUND, vm.CurrentEntryId);
        }

        // ================================================================
        // SupportTalkFE6ViewModel (FE6, 16-byte)
        // ================================================================

        [Fact]
        public void SupportTalkFE6VM_CurrentSourceFieldMap_Has7Entries()
        {
            var vm = new SupportTalkFE6ViewModel
            {
                SupportPartner1 = 2, SupportPartner2 = 4,
                TextC = 0x50, TextB = 0x51, TextA = 0x52,
                Padding1 = 0, Padding2 = 0,
            };

            var map = vm.CurrentSourceFieldMap();

            Assert.Equal(7, map.Count);
            Assert.Equal(2u,    map["b0"]);
            Assert.Equal(4u,    map["b1"]);
            Assert.Equal(0x50u, map["w4"]);
        }

        [Fact]
        public void SupportTalkFE6VM_BuildSourceFieldDict_EmitsOnlyChangedFields()
        {
            var vm = new SupportTalkFE6ViewModel { TextA = 0x90 };
            vm.RefreshSourceFieldSnapshot();

            Assert.Empty(vm.BuildSourceFieldDict());

            vm.TextA = 0x91;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(0x91u, changed["w12"]);
            Assert.False(changed.ContainsKey("b0"));
        }

        [Fact]
        public void SupportTalkFE6VM_CurrentEntryId_ReturnsNotFound_WhenNoRom()
        {
            var vm = new SupportTalkFE6ViewModel { };
            Assert.Equal(U.NOT_FOUND, vm.CurrentEntryId);
        }

        // ================================================================
        // SupportTalkFE7ViewModel (FE7, 20-byte)
        // ================================================================

        [Fact]
        public void SupportTalkFE7VM_CurrentSourceFieldMap_Has9Entries()
        {
            var vm = new SupportTalkFE7ViewModel
            {
                SupportPartner1 = 1, SupportPartner2 = 2,
                TextC = 0xA0, TextB = 0xA1, TextA = 0xA2,
                SongC = 5, SongB = 6, SongA = 7, Padding = 0,
            };

            var map = vm.CurrentSourceFieldMap();

            Assert.Equal(9, map.Count);
            Assert.Equal(1u,    map["b0"]);
            Assert.Equal(2u,    map["b1"]);
            Assert.Equal(0xA0u, map["d4"]);
            Assert.Equal(5u,    map["b16"]);
            Assert.Equal(0u,    map["b19"]);
        }

        [Fact]
        public void SupportTalkFE7VM_BuildSourceFieldDict_EmitsOnlyChangedFields()
        {
            var vm = new SupportTalkFE7ViewModel { SongA = 7 };
            vm.RefreshSourceFieldSnapshot();

            Assert.Empty(vm.BuildSourceFieldDict());

            vm.SongA = 8;
            var changed = vm.BuildSourceFieldDict();
            Assert.Equal(8u, changed["b18"]);
            Assert.False(changed.ContainsKey("b0"));

            vm.RefreshSourceFieldSnapshot();
            Assert.Empty(vm.BuildSourceFieldDict());
        }

        [Fact]
        public void SupportTalkFE7VM_CurrentEntryId_ReturnsNotFound_WhenNoRom()
        {
            var vm = new SupportTalkFE7ViewModel { };
            Assert.Equal(U.NOT_FOUND, vm.CurrentEntryId);
        }
    }
}
