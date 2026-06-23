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
                // LoadMapImage requires an IImageService to decode the tileset; wire the
                // Skia implementation if a previous test hasn't already (RomTestHelper
                // wires the ROM + caches but not the image service).
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new FEBuilderGBA.SkiaSharp.SkiaImageService();

                var probe = new MapEditorViewModel();
                var list = probe.LoadList();

                // A valid FE8U ROM must yield a non-empty map list. Empty here is a
                // genuine regression, not a reason to skip.
                Assert.NotNull(list);
                Assert.NotEmpty(list);

                // Scan for the first map that actually loads with positive dimensions.
                // (FE8U map ID 0 / some entries are empty placeholders that resolve to
                // no data — those legitimately render 0x0; we want a real chapter map.)
                MapEditorViewModel loaded = null;
                foreach (var entry in list)
                {
                    var candidate = new MapEditorViewModel();
                    candidate.LoadMapImage(entry.addr, entry.tag);
                    if (candidate.MapWidth > 0 && candidate.MapHeight > 0)
                    {
                        loaded = candidate;
                        break;
                    }
                }

                // A valid FE8U ROM must contain at least one renderable map.
                // None => real regression (not a skip).
                Assert.True(loaded != null,
                    "No FE8U map loaded with positive dimensions — expected at least one renderable map.");
                var vm = loaded!;

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
