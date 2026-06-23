using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Integration (real-ROM) tests for the Import Map (CSV) feature (#1382).
    /// Uses <see cref="RomTestHelper.WithRom"/> to load FE8U. The test is
    /// SKIPPED (not green-passed) when FE8U.gba is genuinely unavailable; when
    /// the ROM IS present it runs real assertions so a regression surfaces as a
    /// failure rather than a silent skip.
    /// </summary>
    [Collection("SharedState")]
    public class MapEditorImportRomTests
    {
        /// <summary>
        /// Success-path: load the first FE8U map, build a self-import mars array
        /// from the existing map data (guaranteed valid), apply it, and assert
        /// the cache was advanced and the write address is non-zero.
        /// </summary>
        [SkippableFact]
        public void ApplyMapGrid_FE8U_SelfImport_Succeeds()
        {
            // ROM-unavailable → SKIP (not pass). Anything after a real ROM loads
            // is a real assertion, so an empty map list / failed load is a FAILURE.
            string? romPath = TestRomLocator.FindRom("FE8U");
            Skip.If(romPath == null, "FE8U.gba not available — skipping real-ROM import test.");

            RomTestHelper.WithRom("FE8U", () =>
            {
                var vm = new MapEditorViewModel();
                var list = vm.LoadList();

                // A valid FE8U ROM must yield a non-empty map list. Empty here is a
                // genuine regression, not a reason to skip.
                Assert.NotNull(list);
                Assert.NotEmpty(list);

                // Load the first map
                var item = list[0];
                vm.LoadMapImage(item.addr, item.tag);

                // A valid first map must render with positive dimensions.
                Assert.True(vm.MapWidth > 0, $"MapWidth was {vm.MapWidth}; expected > 0 after loading the first map.");
                Assert.True(vm.MapHeight > 0, $"MapHeight was {vm.MapHeight}; expected > 0 after loading the first map.");

                // Build mars from the current cache (self-import = lossless no-op at tile level)
                byte[] snapshot = vm.GetMapDataSnapshot();
                Assert.NotNull(snapshot);
                Assert.True(snapshot.Length >= 2 + vm.MapWidth * vm.MapHeight * 2);

                ushort[] mars = new ushort[vm.MapWidth * vm.MapHeight];
                for (int i = 0; i < mars.Length; i++)
                {
                    int offset = 2 + i * 2;
                    mars[i] = (ushort)(snapshot[offset] | (snapshot[offset + 1] << 8));
                }

                bool ok = vm.ApplyMapGrid(mars, vm.MapWidth, vm.MapHeight, out string err, out uint addr);
                Assert.True(ok, err ?? "(null error)");
                Assert.NotEqual(0u, addr);

                // Cache advanced: grid bytes must match what we wrote
                byte[] after = vm.GetMapDataSnapshot();
                Assert.NotNull(after);
                for (int i = 0; i < mars.Length; i++)
                {
                    int offset = 2 + i * 2;
                    ushort written = (ushort)(after[offset] | (after[offset + 1] << 8));
                    Assert.Equal(mars[i], written);
                }
            });
        }
    }
}
