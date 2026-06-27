using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for the multibyte/JP Map Terrain Name editor (#1601).
    ///
    /// On multibyte ROMs (FE8J) the terrain-name table holds 4-byte POINTERS to
    /// NUL-terminated name strings. The editor previously only surfaced the raw
    /// pointer and wrote it back verbatim — the name string could not be edited.
    /// These tests verify the VM now:
    ///   * decodes real names into the list + selected entry (no more placeholders);
    ///   * writes the edited string back (encode + append + repoint) losslessly;
    ///   * is a byte-identical no-op when nothing is loaded.
    /// </summary>
    [Collection("SharedState")]
    public class MapTerrainNameViewModelTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public MapTerrainNameViewModelTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // -----------------------------------------------------------------
        // List decode parity: names are decoded, not placeholders, and the
        // list matches the reachable sibling TerrainNameEditorViewModel.
        // -----------------------------------------------------------------

        [Fact]
        public void Multibyte_List_DecodesRealNames_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () =>
            {
                Assert.True(CoreState.ROM.RomInfo.is_multibyte, "FE8J must be multibyte");

                var vm = new MapTerrainNameViewModel();
                var list = vm.LoadList();
                Assert.True(list.Count > 0, "FE8J terrain list must be non-empty");

                // 4-byte stride: consecutive entry addresses differ by 4.
                if (list.Count >= 2)
                    Assert.Equal(4u, list[1].addr - list[0].addr);

                // At least one entry must contain a real decoded name, not just the
                // "0xNN " hex prefix — proving the placeholder "Terrain {i}" path is gone.
                bool anyDecoded = false;
                foreach (var item in list)
                {
                    string afterPrefix = item.name.Length > 5 ? item.name.Substring(5).Trim() : "";
                    Assert.DoesNotContain("Terrain ", item.name); // no old placeholder
                    if (afterPrefix.Length > 0) anyDecoded = true;
                }
                Assert.True(anyDecoded, "at least one terrain name must decode to a non-empty string");

                _output.WriteLine($"FE8J terrain entries: {list.Count}");
                for (int i = 0; i < list.Count && i < 6; i++)
                    _output.WriteLine($"  {list[i].name}");
            });
        }

        [Fact]
        public void Multibyte_List_MatchesSiblingEditor_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () =>
            {
                var vm = new MapTerrainNameViewModel();
                var list = vm.LoadList();

                var sibling = new TerrainNameEditorViewModel();
                var siblingList = sibling.LoadTerrainNameList();

                // Both enumerate the same 4-byte slots and decode the same strings,
                // so the addresses must line up 1:1 (names may differ only in the
                // hex-prefix formatting, so compare addresses for a strict parity).
                Assert.Equal(siblingList.Count, list.Count);
                for (int i = 0; i < list.Count; i++)
                    Assert.Equal(siblingList[i].addr, list[i].addr);
            });
        }

        // -----------------------------------------------------------------
        // Selected-entry decode: LoadEntry fills the editable TerrainName.
        // -----------------------------------------------------------------

        [Fact]
        public void Multibyte_LoadEntry_DecodesEditableName_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () =>
            {
                var vm = new MapTerrainNameViewModel();
                var list = vm.LoadList();
                Assert.True(list.Count > 0);

                // Find an entry whose slot points at a real (non-NULL) string.
                bool found = false;
                foreach (var item in list)
                {
                    vm.LoadEntry(item.addr);
                    Assert.True(vm.IsLoaded);
                    Assert.Equal(item.addr, vm.CurrentAddr);
                    if (!string.IsNullOrEmpty(vm.TerrainName))
                    {
                        found = true;
                        _output.WriteLine($"slot 0x{item.addr:X08} -> '{vm.TerrainName}' (ptr 0x{vm.TerrainNamePointer:X08})");
                        break;
                    }
                }
                Assert.True(found, "at least one entry must decode to a non-empty editable name");
            });
        }

        // -----------------------------------------------------------------
        // Write-back round-trip: same string survives; the list still builds.
        // -----------------------------------------------------------------

        [Fact]
        public void Multibyte_WriteSameString_RoundTripsLossless_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () =>
            {
                var vm = new MapTerrainNameViewModel();
                var list = vm.LoadList();
                Assert.True(list.Count > 0);

                uint slot = 0;
                string? original = null;
                foreach (var item in list)
                {
                    vm.LoadEntry(item.addr);
                    if (!string.IsNullOrEmpty(vm.TerrainName))
                    {
                        slot = item.addr;
                        original = vm.TerrainName;
                        break;
                    }
                }
                Assert.NotNull(original);
                _output.WriteLine($"FE8J round-trip slot=0x{slot:X08} name='{original}'");

                // Write the SAME string back under an ambient undo scope.
                var undo = CoreState.Undo.NewUndoData("test");
                using (ROM.BeginUndoScope(undo))
                {
                    vm.LoadEntry(slot);
                    vm.TerrainName = original;
                    vm.Write();
                }

                // Re-read: decoded string must be identical.
                var vm2 = new MapTerrainNameViewModel();
                vm2.LoadEntry(slot);
                Assert.Equal(original, vm2.TerrainName);

                // List still builds with the same count (no corruption).
                var listAfter = new MapTerrainNameViewModel().LoadList();
                Assert.Equal(list.Count, listAfter.Count);
            });
        }

        [Fact]
        public void Multibyte_WriteDifferentString_DecodesBack_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () =>
            {
                var vm = new MapTerrainNameViewModel();
                var list = vm.LoadList();
                Assert.True(list.Count > 0);

                uint slot = 0;
                foreach (var item in list)
                {
                    vm.LoadEntry(item.addr);
                    if (!string.IsNullOrEmpty(vm.TerrainName)) { slot = item.addr; break; }
                }
                Assert.NotEqual(0u, slot);

                // ASCII renders identically through the JP system encoder, so it is a
                // safe, deterministic edited value to assert a write+read-back.
                const string edited = "TEST123";
                uint ptrBefore;
                var undo = CoreState.Undo.NewUndoData("test");
                using (ROM.BeginUndoScope(undo))
                {
                    vm.LoadEntry(slot);
                    ptrBefore = vm.TerrainNamePointer;
                    vm.TerrainName = edited;
                    vm.Write();
                }

                var vm2 = new MapTerrainNameViewModel();
                vm2.LoadEntry(slot);
                Assert.Equal(edited, vm2.TerrainName);
                // The slot was repointed to fresh free space (a grow), so the pointer
                // value must have changed.
                Assert.NotEqual(ptrBefore, vm2.TerrainNamePointer);
                _output.WriteLine($"edited '{edited}' written; slot repointed 0x{ptrBefore:X08} -> 0x{vm2.TerrainNamePointer:X08}");
            });
        }

        [Fact]
        public void Multibyte_NoOpWrite_CurrentAddrZero_LeavesRomByteIdentical_FE8J()
        {
            RomTestHelper.WithRom("FE8J", () =>
            {
                var vm = new MapTerrainNameViewModel();
                var list = vm.LoadList();
                Assert.True(list.Count > 0);

                byte[] before = (byte[])CoreState.ROM.Data.Clone();

                // CurrentAddr == 0 short-circuits to a no-op; the ROM must be untouched.
                var vmNoop = new MapTerrainNameViewModel { TerrainName = "should be ignored" };
                vmNoop.Write();

                Assert.Equal(before.Length, CoreState.ROM.Data.Length);
                Assert.Equal(before, CoreState.ROM.Data);
            });
        }
    }
}
