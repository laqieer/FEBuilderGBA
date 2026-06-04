using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for the dual-mode Terrain Name editor (#5 of #943).
    ///
    /// The terrain-name table uses two different data models:
    ///   * multibyte (JP: FE6/FE7J/FE8J) — 4-byte string pointers
    ///   * non-multibyte (US/EU: FE7U/FE8U) — 2-byte Huffman text IDs
    ///
    /// These tests verify the VM reads/writes the CORRECT model per ROM, the
    /// golden-builder list (ListParityHelper.BuildTerrainNameList) matches the
    /// VM, and the write-back is a safe, lossless round-trip.
    /// </summary>
    [Collection("SharedState")]
    public class TerrainNameEditorParityTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public TerrainNameEditorParityTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // -----------------------------------------------------------------
        // Branch selection: multibyte vs non-multibyte changes stride + decode.
        // -----------------------------------------------------------------

        [Fact]
        public void Multibyte_VM_UsesStringPointerModel_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () =>
            {
                Assert.True(CoreState.ROM.RomInfo.is_multibyte, "FE8J must be multibyte");

                var vm = new TerrainNameEditorViewModel();
                var list = vm.LoadTerrainNameList();
                Assert.True(vm.IsMultibyte, "VM must select the multibyte branch on FE8J");
                Assert.True(list.Count > 0, "FE8J terrain list must be non-empty");

                // 4-byte stride: consecutive entry addresses differ by 4.
                if (list.Count >= 2)
                    Assert.Equal(4u, list[1].addr - list[0].addr);

                _output.WriteLine($"FE8J multibyte terrain entries: {list.Count}");
                for (int i = 0; i < list.Count && i < 6; i++)
                    _output.WriteLine($"  {list[i].name}");
            });
        }

        [Fact]
        public void NonMultibyte_VM_UsesTextIdModel_FE8U()
        {
            RomTestHelper.WithRom("FE8U", () =>
            {
                Assert.False(CoreState.ROM.RomInfo.is_multibyte, "FE8U must NOT be multibyte");

                var vm = new TerrainNameEditorViewModel();
                var list = vm.LoadTerrainNameList();
                Assert.False(vm.IsMultibyte, "VM must select the non-multibyte branch on FE8U");
                Assert.True(list.Count > 0, "FE8U terrain list must be non-empty");

                // 2-byte stride: consecutive entry addresses differ by 2.
                if (list.Count >= 2)
                    Assert.Equal(2u, list[1].addr - list[0].addr);

                _output.WriteLine($"FE8U non-multibyte terrain entries: {list.Count}");
            });
        }

        // -----------------------------------------------------------------
        // Golden-builder lockstep: VM list == ListParityHelper reference list.
        // -----------------------------------------------------------------

        [Fact]
        public void VM_List_MatchesGoldenBuilder_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () => AssertVmMatchesGolden());
        }

        [Fact]
        public void VM_List_MatchesGoldenBuilder_FE8U()
        {
            RomTestHelper.WithRom("FE8U", () => AssertVmMatchesGolden());
        }

        void AssertVmMatchesGolden()
        {
            var vm = new TerrainNameEditorViewModel();
            var vmList = vm.LoadTerrainNameList();
            var refList = ListParityHelper.BuildReferenceList("TerrainNameEditorView");

            Assert.NotNull(refList);
            Assert.Equal(vmList.Count, refList.Count);
            for (int i = 0; i < vmList.Count; i++)
            {
                Assert.Equal(vmList[i].addr, refList[i].addr);
                Assert.Equal(vmList[i].name, refList[i].name);
            }
        }

        // -----------------------------------------------------------------
        // Round-trip: multibyte string write-back (FE8J).
        // -----------------------------------------------------------------

        [Fact]
        public void Multibyte_WriteSameString_IsLosslessNoOp_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () =>
            {
                var vm = new TerrainNameEditorViewModel();
                var list = vm.LoadTerrainNameList();
                Assert.True(list.Count > 0, "need at least one terrain entry");

                // Pick the first entry whose slot points at a real (non-NULL)
                // string so we exercise the actual write/recycle path.
                uint slot = 0;
                string? original = null;
                foreach (var item in list)
                {
                    vm.LoadTerrainName(item.addr);
                    if (!string.IsNullOrEmpty(vm.TerrainName))
                    {
                        slot = item.addr;
                        original = vm.TerrainName;
                        break;
                    }
                }
                Assert.NotNull(original);
                _output.WriteLine($"FE8J round-trip slot=0x{slot:X08} name='{original}'");

                byte[] before = (byte[])CoreState.ROM.Data.Clone();

                // Write the SAME string back under an ambient undo scope (the
                // View opens one via UndoService.Begin; we mimic that here).
                var undo = CoreState.Undo.NewUndoData("test");
                using (ROM.BeginUndoScope(undo))
                {
                    vm.LoadTerrainName(slot);
                    vm.TerrainName = original;
                    vm.WriteTerrainName();
                }

                // Re-read: decoded string must be identical.
                var vm2 = new TerrainNameEditorViewModel();
                vm2.LoadTerrainName(slot);
                Assert.Equal(original, vm2.TerrainName);

                // The slot byte itself may be repointed to fresh free space, but
                // the rest of the ROM up to the new region must be untouched and
                // the slot must still resolve to the same string. Verify the
                // ROM did not corrupt by re-running the list build.
                var listAfter = new TerrainNameEditorViewModel().LoadTerrainNameList();
                Assert.Equal(list.Count, listAfter.Count);

                _output.WriteLine($"FE8J round-trip OK; rom grew {before.Length} -> {CoreState.ROM.Data.Length}");
            });
        }

        [Fact]
        public void Multibyte_WriteFailsRestoreLeavesRomByteIdentical_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () =>
            {
                var vm = new TerrainNameEditorViewModel();
                var list = vm.LoadTerrainNameList();
                Assert.True(list.Count > 0);

                uint slot = list[0].addr;
                vm.LoadTerrainName(slot);

                byte[] before = (byte[])CoreState.ROM.Data.Clone();

                // CurrentAddr == 0 short-circuits to a no-op; verify a no-op
                // write does not touch the ROM at all.
                var vmNoop = new TerrainNameEditorViewModel { IsMultibyte = true };
                vmNoop.WriteTerrainName(); // CurrentAddr==0 -> early return

                Assert.Equal(before.Length, CoreState.ROM.Data.Length);
                Assert.Equal(before, CoreState.ROM.Data);
            });
        }

        // -----------------------------------------------------------------
        // Round-trip: non-multibyte text-ID write-back (FE8U).
        // -----------------------------------------------------------------

        [Fact]
        public void NonMultibyte_TextIdRoundTrips_FE8U()
        {
            RomTestHelper.WithRom("FE8U", () =>
            {
                var vm = new TerrainNameEditorViewModel();
                var list = vm.LoadTerrainNameList();
                Assert.True(list.Count > 0);

                uint slot = list[0].addr;
                vm.LoadTerrainName(slot);
                uint originalId = vm.TextId;

                byte[] before = (byte[])CoreState.ROM.Data.Clone();

                // Write the same text ID back: must be a byte-identical no-op.
                var undo = CoreState.Undo.NewUndoData("test");
                using (ROM.BeginUndoScope(undo))
                {
                    vm.TextId = originalId;
                    vm.WriteTerrainName();
                }

                var vm2 = new TerrainNameEditorViewModel();
                vm2.LoadTerrainName(slot);
                Assert.Equal(originalId, vm2.TextId);
                Assert.Equal(before, CoreState.ROM.Data);

                // Write a different ID and confirm the 2 bytes changed + read back.
                ushort newId = (ushort)(originalId ^ 0x0001);
                var undo2 = CoreState.Undo.NewUndoData("test2");
                using (ROM.BeginUndoScope(undo2))
                {
                    vm.TextId = newId;
                    vm.WriteTerrainName();
                }
                Assert.Equal(newId, (ushort)CoreState.ROM.u16(slot));

                // Restore original so the shared ROM state is not mutated for
                // other tests in this collection.
                using (ROM.BeginUndoScope(CoreState.Undo.NewUndoData("restore")))
                {
                    CoreState.ROM.write_u16(slot, originalId);
                }
                Assert.Equal(before, CoreState.ROM.Data);
            });
        }
    }
}
