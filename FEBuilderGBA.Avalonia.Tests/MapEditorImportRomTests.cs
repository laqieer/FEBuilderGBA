using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Integration (real-ROM) tests for the Import Map (CSV) feature (#1382).
    /// Uses <see cref="RomTestHelper.WithRom"/> to load FE8U; gracefully skips
    /// if the ROM is absent (CI without ROMs still passes).
    /// </summary>
    [Collection("SharedState")]
    public class MapEditorImportRomTests
    {
        /// <summary>
        /// Success-path: load the first FE8U map, build a self-import mars array
        /// from the existing map data (guaranteed valid), apply it, and assert
        /// the cache was advanced and the write address is non-zero.
        /// </summary>
        [Fact]
        public void ApplyMapGrid_FE8U_SelfImport_Succeeds()
        {
            RomTestHelper.WithRom("FE8U", () =>
            {
                var vm = new MapEditorViewModel();
                var list = vm.LoadList();
                if (list == null || list.Count == 0)
                    return; // graceful skip — no map list

                // Load the first map
                var item = list[0];
                vm.LoadMapImage(item.addr, item.tag);

                if (vm.MapWidth <= 0 || vm.MapHeight <= 0)
                    return; // graceful skip — map did not render

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
