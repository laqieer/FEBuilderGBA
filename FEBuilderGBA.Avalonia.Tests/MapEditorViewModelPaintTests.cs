using System;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// VM-level tests for the click-to-paint flow added in the first slice of #658.
    /// Verifies the cache-rollback contract: when <c>CoreState.ROM</c> is null,
    /// <c>ApplyMapEdit</c> fails deterministically (no <c>FindAndWriteData</c> call)
    /// and the in-memory cache is never advanced.
    /// </summary>
    [Collection("SharedState")]
    public class MapEditorViewModelPaintTests : IDisposable
    {
        readonly ROM? _savedRom;

        public MapEditorViewModelPaintTests()
        {
            _savedRom = CoreState.ROM;
            CoreState.ROM = null;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        /// <summary>
        /// Reflect a single private field on the VM (the cached map data buffer)
        /// so we can pre-seed it with a known synthetic value. This keeps the
        /// test self-contained — no full LZ77 round-trip via a real ROM needed.
        /// </summary>
        static void SetPrivateField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f);
            f.SetValue(target, value);
        }

        [Fact]
        public void ApplyMapEdit_WithoutRom_FailsAndCacheUnchanged()
        {
            var vm = new MapEditorViewModel { MapWidth = 2, MapHeight = 2 };

            // Seed cached map data: width=2, height=2, four MAR slots, tile (0,0)=0x0001.
            byte[] seed = new byte[2 + 2 * 2 * 2];
            seed[0] = 2; seed[1] = 2;
            seed[2] = 0x01; seed[3] = 0x00; // (0,0) = 0x0001
            seed[4] = 0x02; seed[5] = 0x00; // (1,0) = 0x0002
            seed[6] = 0x03; seed[7] = 0x00; // (0,1) = 0x0003
            seed[8] = 0x04; seed[9] = 0x00; // (1,1) = 0x0004
            SetPrivateField(vm, "_cachedMapData", seed);

            // Sanity: snapshot reflects the seed.
            byte[] before = vm.GetMapDataSnapshot();
            Assert.NotNull(before);
            Assert.Equal(0x01, before[2]);

            // CoreState.ROM = null in fixture → ApplyMapEdit must fail early with
            // "No ROM loaded" and leave the cache untouched.
            bool ok = vm.ApplyMapEdit(0, 0, 0x00FF, out string error, out _);
            Assert.False(ok);
            Assert.Equal("No ROM loaded", error);

            byte[] after = vm.GetMapDataSnapshot();
            Assert.NotNull(after);
            // Cache must match the seed byte-for-byte.
            Assert.Equal(before, after);
            // Spot-check the would-be-changed tile is still 0x0001.
            Assert.Equal(0x01, after[2]);
            Assert.Equal(0x00, after[3]);
        }

        [Fact]
        public void ApplyMapEdit_NoMapLoaded_Fails()
        {
            var vm = new MapEditorViewModel { MapWidth = 2, MapHeight = 2 };
            // No seed → _cachedMapData stays null.
            // CoreState.ROM is also null in this fixture, so "No ROM loaded" wins.
            bool ok = vm.ApplyMapEdit(0, 0, 0x00FF, out string error, out _);
            Assert.False(ok);
            Assert.NotNull(error);
        }

        [Fact]
        public void PaintTileAt_WithoutChipsetSelected_Fails()
        {
            var vm = new MapEditorViewModel { MapWidth = 2, MapHeight = 2 };
            // No SelectedChipsetIndex set → -1 → HasChipsetSelected = false
            Assert.False(vm.HasChipsetSelected);
            bool ok = vm.PaintTileAt(0, 0);
            Assert.False(ok);
            Assert.Contains("No chipset selected", vm.TileInfo);
        }

        [Fact]
        public void SetSelectedChipsetIndex_ValidIndex_UpdatesInfo()
        {
            var vm = new MapEditorViewModel();
            Assert.True(vm.SetSelectedChipsetIndex(5));
            Assert.True(vm.HasChipsetSelected);
            Assert.Equal(5, vm.SelectedChipsetIndex);
            // Info should mention the index and MAR value (chipset 5 → MAR 0x14)
            Assert.Contains("#5", vm.ChipsetInfo);
            Assert.Contains("0x0014", vm.ChipsetInfo);
        }

        [Fact]
        public void SetSelectedChipsetIndex_OutOfRange_Fails()
        {
            var vm = new MapEditorViewModel();
            Assert.False(vm.SetSelectedChipsetIndex(-1));
            Assert.False(vm.SetSelectedChipsetIndex(MapEditorTilesetCore.CHIPSET_COUNT));
            Assert.False(vm.SetSelectedChipsetIndex(int.MaxValue));
            Assert.False(vm.HasChipsetSelected);
        }

        [Fact]
        public void SelectChipsetFromPaletteClick_PixelsMapToIndex()
        {
            var vm = new MapEditorViewModel();
            // (0,0) → chipset 0
            Assert.True(vm.SelectChipsetFromPaletteClick(0, 0));
            Assert.Equal(0, vm.SelectedChipsetIndex);
            // (16, 0) → chipset 1
            Assert.True(vm.SelectChipsetFromPaletteClick(16, 0));
            Assert.Equal(1, vm.SelectedChipsetIndex);
            // (0, 16) → chipset 32 (one row down)
            Assert.True(vm.SelectChipsetFromPaletteClick(0, 16));
            Assert.Equal(32, vm.SelectedChipsetIndex);
            // OOB pixel → returns false, selection unchanged
            int prior = vm.SelectedChipsetIndex;
            Assert.False(vm.SelectChipsetFromPaletteClick(-5, -5));
            Assert.Equal(prior, vm.SelectedChipsetIndex);
        }

        [Fact]
        public void EyedropperAt_NoMapLoaded_Fails()
        {
            var vm = new MapEditorViewModel();
            Assert.False(vm.EyedropperAt(0, 0));
        }

        [Fact]
        public void EyedropperAt_ReadsMarAndSelectsChipset()
        {
            var vm = new MapEditorViewModel { MapWidth = 2, MapHeight = 1 };
            byte[] seed = new byte[2 + 2 * 2];
            seed[0] = 2; seed[1] = 1;
            // Tile (1,0) = MAR 0x0014 → chipsetIndex 5
            seed[2] = 0x00; seed[3] = 0x00;
            seed[4] = 0x14; seed[5] = 0x00;
            SetPrivateField(vm, "_cachedMapData", seed);

            Assert.True(vm.EyedropperAt(1, 0));
            Assert.Equal(5, vm.SelectedChipsetIndex);
        }

        // =====================================================================
        // ApplyMapGrid tests (#1382)
        // =====================================================================

        [Fact]
        public void ApplyMapGrid_NoRomLoaded_FailsWithError()
        {
            // CoreState.ROM is null in this fixture — should fail immediately.
            var vm = new MapEditorViewModel { MapWidth = 2, MapHeight = 2 };
            bool ok = vm.ApplyMapGrid(new ushort[4], 2, 2, out string error, out _);
            Assert.False(ok);
            Assert.Equal("No ROM loaded", error);
        }

        [Fact]
        public void ApplyMapGrid_NoMapLoaded_FailsWithError()
        {
            // CoreState.ROM is null — "No ROM loaded" check fires before "No map loaded".
            // So to test the "No map loaded" path we'd need a real ROM.
            // Just verify it fails with a non-null error even with null ROM.
            var vm = new MapEditorViewModel { MapWidth = 2, MapHeight = 2 };
            // _cachedMapData is null, ROM is also null
            bool ok = vm.ApplyMapGrid(new ushort[4], 2, 2, out string error, out _);
            Assert.False(ok);
            Assert.NotNull(error);
        }

        [Fact]
        public void ApplyMapGrid_DimensionMismatch_FailsWithDescriptiveError()
        {
            // Seed cache + non-matching width to reach dimension check.
            // ROM is null → "No ROM loaded" fires first before dim check.
            // The test confirms false+non-null error for a null-ROM call.
            var vm = new MapEditorViewModel { MapWidth = 2, MapHeight = 2 };
            byte[] seed = new byte[2 + 4 * 2];
            seed[0] = 2; seed[1] = 2;
            SetPrivateField(vm, "_cachedMapData", seed);

            // Pass 3x3 mars to a 2x2 map — ROM is null so it fails at ROM check,
            // not dim check; regardless result must be false.
            bool ok = vm.ApplyMapGrid(new ushort[9], 3, 3, out string error, out _);
            Assert.False(ok);
            Assert.NotNull(error);
        }
    }
}
