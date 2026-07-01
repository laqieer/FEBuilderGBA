using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Integration (real-ROM) tests for the Map Resize feature (#1735). Uses
    /// <see cref="RomTestHelper.WithRom"/> to load FE8U; SKIPPED (not passed) when
    /// FE8U.gba is genuinely unavailable, but runs real assertions when present so a
    /// regression surfaces as a failure rather than a silent skip.
    /// </summary>
    [Collection("SharedState")]
    public class MapEditorResizeRomTests
    {
        static ushort ReadTileAt(byte[] map, int w, int x, int y)
        {
            int off = 2 + (y * w + x) * 2;
            return (ushort)(map[off] | (map[off + 1] << 8));
        }

        // Load the first FE8U map that renders with positive dimensions, returning the
        // loaded VM plus the list entry it came from (so a fresh reload can prove the
        // pointer was repointed after a resize).
        static MapEditorViewModel LoadFirstRenderableMap(out uint entryAddr, out uint entryTag)
        {
            entryAddr = 0;
            entryTag = 0;

            if (CoreState.ImageService == null)
                CoreState.ImageService = new FEBuilderGBA.SkiaSharp.SkiaImageService();

            var probe = new MapEditorViewModel();
            var list = probe.LoadList();
            Assert.NotNull(list);
            Assert.NotEmpty(list);

            foreach (var entry in list)
            {
                var candidate = new MapEditorViewModel();
                candidate.LoadMapImage(entry.addr, entry.tag);
                if (candidate.MapWidth > 0 && candidate.MapHeight > 0)
                {
                    entryAddr = entry.addr;
                    entryTag = entry.tag;
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>
        /// Grow a real FE8U map by one row/column (whichever stays within the FE
        /// height-dependent limits): assert dimensions advance, kept tiles are
        /// preserved, new cells are fill 0, and — crucially — a FRESH reload of the
        /// same map entry reflects the new size, proving the recompressed data was
        /// written to free space and the map pointer was repointed (relocate-on-grow).
        /// </summary>
        [SkippableFact]
        public void ApplyMapResize_FE8U_Grow_RelocatesAndRepoints()
        {
            string? romPath = TestRomLocator.FindRom("FE8U");
            Skip.If(romPath == null, "FE8U.gba not available — skipping real-ROM resize test.");

            RomTestHelper.WithRom("FE8U", () =>
            {
                var vm = LoadFirstRenderableMap(out uint entryAddr, out uint entryTag);
                Assert.True(vm != null, "No FE8U map loaded with positive dimensions.");

                int oldW = vm!.MapWidth, oldH = vm.MapHeight;
                byte[] before = vm.GetMapDataSnapshot();
                Assert.NotNull(before);

                // Pick a grow that stays within the FE main-map limits.
                int addRight = 0, addBottom = 0;
                if (oldW + 1 <= MapEditorTilesetCore.GetLimitMapWidth(oldH))
                    addRight = 1;
                else if (oldH + 1 <= MapEditorTilesetCore.MAP_MAX_HEIGHT &&
                         oldW <= MapEditorTilesetCore.GetLimitMapWidth(oldH + 1))
                    addBottom = 1;
                Skip.If(addRight == 0 && addBottom == 0,
                    "Selected FE8U map is already at the maximum size — no safe grow.");

                bool ok = vm.ApplyMapResize(0, 0, addRight, addBottom, 0, out string err, out uint addr);
                Assert.True(ok, err ?? "(null error)");
                Assert.NotEqual(0u, addr);
                Assert.Equal(oldW + addRight, vm.MapWidth);
                Assert.Equal(oldH + addBottom, vm.MapHeight);

                byte[] after = vm.GetMapDataSnapshot();
                Assert.NotNull(after);
                Assert.Equal(vm.MapWidth, after[0]);
                Assert.Equal(vm.MapHeight, after[1]);

                // Every original tile is preserved at its original coordinate.
                for (int y = 0; y < oldH; y++)
                    for (int x = 0; x < oldW; x++)
                        Assert.Equal(ReadTileAt(before, oldW, x, y), ReadTileAt(after, vm.MapWidth, x, y));

                // New cells are the fill tile (0).
                if (addRight > 0)
                    for (int y = 0; y < vm.MapHeight; y++)
                        Assert.Equal(0, ReadTileAt(after, vm.MapWidth, vm.MapWidth - 1, y));
                if (addBottom > 0)
                    for (int x = 0; x < vm.MapWidth; x++)
                        Assert.Equal(0, ReadTileAt(after, vm.MapWidth, x, vm.MapHeight - 1));

                // Reload the SAME entry from ROM: PlistToOffset re-reads the pointer
                // table, so matching new dimensions prove the pointer was repointed to
                // the freshly-written (relocated) map data.
                var reloaded = new MapEditorViewModel();
                reloaded.LoadMapImage(entryAddr, entryTag);
                Assert.Equal(vm.MapWidth, reloaded.MapWidth);
                Assert.Equal(vm.MapHeight, reloaded.MapHeight);
            });
        }

        /// <summary>
        /// A resize that would drop below the 15×10 minimum is refused, and the VM's
        /// cached dimensions are left unchanged (no partial write).
        /// </summary>
        [SkippableFact]
        public void ApplyMapResize_FE8U_TooSmall_Refused_StateUnchanged()
        {
            string? romPath = TestRomLocator.FindRom("FE8U");
            Skip.If(romPath == null, "FE8U.gba not available — skipping real-ROM resize-reject test.");

            RomTestHelper.WithRom("FE8U", () =>
            {
                var vm = LoadFirstRenderableMap(out _, out _);
                Assert.True(vm != null, "No FE8U map loaded with positive dimensions.");

                int oldW = vm!.MapWidth, oldH = vm.MapHeight;

                // Crop the entire width away → new width 0 (< 15) → must be rejected.
                bool ok = vm.ApplyMapResize(0, -oldW, 0, 0, 0, out string err, out uint addr);
                Assert.False(ok);
                Assert.NotNull(err);
                Assert.Equal(0u, addr);
                // Dimensions untouched.
                Assert.Equal(oldW, vm.MapWidth);
                Assert.Equal(oldH, vm.MapHeight);
            });
        }
    }
}
